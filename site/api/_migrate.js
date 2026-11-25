// Copies the whole database from Upstash (REST) to Redis Cloud (REDIS_URL), from /painel. Run once, when Upstash
// answers again (its monthly quota). Every key is copied with its type and remaining lifetime; the rate-limit
// counters (rl:*) are skipped, they only live an hour anyway. The marker "audial:migrated" is written last: only
// then does the site start using Redis Cloud (see _lib.js), so a copy that stops halfway changes nothing and can
// simply be run again.
"use strict";

const lib = require("./_lib");

const BATCH = 200;

/** Several commands in one Upstash request (its /pipeline endpoint). */
async function pipeline(commands) {
  if (!commands.length) return [];
  const url = (process.env.KV_REST_API_URL || process.env.UPSTASH_REDIS_REST_URL || "").replace(/\/+$/, "");
  const token = process.env.KV_REST_API_TOKEN || process.env.UPSTASH_REDIS_REST_TOKEN;
  if (!url || !token) throw new lib.ServerError("storage", "As variáveis do Upstash não estão configuradas.");
  const res = await fetch(url + "/pipeline", {
    method: "POST",
    headers: { Authorization: "Bearer " + token, "Content-Type": "application/json" },
    body: JSON.stringify(commands.map((c) => c.map(String))),
  });
  const data = await res.json().catch(() => null);
  if (!res.ok || !Array.isArray(data)) {
    const msg = data && data.error ? data.error : res.status;
    throw new lib.ServerError("storage", "Upstash: " + msg);
  }
  return data.map((d) => {
    if (d && d.error) throw new lib.ServerError("storage", "Upstash: " + d.error);
    return d ? d.result : null;
  });
}

async function migrate() {
  const target = lib.tcp();
  if (!target) throw new lib.ServerError("setup", "Coloque a variável REDIS_URL (Redis Cloud) no Render antes de copiar.");
  await target.ping();
  const started = Date.now();
  const counts = { string: 0, set: 0, hash: 0, list: 0, zset: 0, skipped: 0 };
  let cursor = "0";
  do {
    const [next, keys] = await lib.upstash("SCAN", cursor, "COUNT", 500);
    cursor = String(next);
    const wanted = keys.filter((k) => !k.startsWith("rl:") && k !== lib.MIGRATED);
    counts.skipped += keys.length - wanted.length;
    for (let i = 0; i < wanted.length; i += BATCH) {
      const chunk = wanted.slice(i, i + BATCH);
      const meta = await pipeline(chunk.flatMap((k) => [["TYPE", k], ["PTTL", k]]));
      const reads = chunk.map((k, j) => {
        const type = meta[j * 2];
        if (type === "string") return ["GET", k];
        if (type === "set") return ["SMEMBERS", k];
        if (type === "hash") return ["HGETALL", k];
        if (type === "list") return ["LRANGE", k, 0, -1];
        if (type === "zset") return ["ZRANGE", k, 0, -1, "WITHSCORES"];
        return null;
      });
      const values = await pipeline(reads.filter(Boolean));
      const write = target.multi();
      let v = 0;
      chunk.forEach((k, j) => {
        if (!reads[j]) {
          counts.skipped++; // gone in between, or a type the site never uses
          return;
        }
        const type = meta[j * 2];
        const ttl = Number(meta[j * 2 + 1]);
        const value = values[v++];
        write.del(k);
        if (type === "string") {
          if (value === null) return;
          write.set(k, value);
        } else if (type === "set") {
          if (!value.length) return;
          write.sadd(k, ...value);
        } else if (type === "hash") {
          if (!value.length) return;
          write.hset(k, ...value);
        } else if (type === "list") {
          if (!value.length) return;
          write.rpush(k, ...value);
        } else if (type === "zset") {
          if (!value.length) return;
          const args = [];
          for (let z = 0; z < value.length; z += 2) args.push(value[z + 1], value[z]);
          write.zadd(k, ...args);
        }
        if (ttl > 0) write.pexpire(k, ttl);
        counts[type]++;
      });
      await write.exec();
    }
  } while (cursor !== "0");
  const copied = counts.string + counts.set + counts.hash + counts.list + counts.zset;
  // spot check: the invite list must be the same on both sides before switching
  const [before, after] = [Number(await lib.upstash("SCARD", "invites")) || 0, Number(await target.scard("invites")) || 0];
  if (before !== after) throw new lib.ServerError("storage", `A cópia não bateu (convites: ${before} no Upstash, ${after} no Redis Cloud). Nada foi trocado; tente de novo.`);
  await target.set(lib.MIGRATED, new Date().toISOString());
  return { copied, invites: after, counts, seconds: Math.round((Date.now() - started) / 1000) };
}

module.exports = { migrate };
