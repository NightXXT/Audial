// Shared code for the beta invite functions (files starting with "_" are not routes on Vercel).
//
// Storage: Upstash Redis through its REST API (no npm package). Created from the Storage tab of the Vercel project,
// which adds KV_REST_API_URL / KV_REST_API_TOKEN (or UPSTASH_REDIS_REST_URL / UPSTASH_REDIS_REST_TOKEN).
// Secrets (Vercel → Settings → Environment Variables, never in the repository):
//   AUDIAL_LICENSE_KEY     private key (PEM, ECDSA P-256) that signs the licenses; its public half is in the app
//   AUDIAL_ADMIN_PASSWORD  password of the admin page
//
// Only the invite code and an anonymous PC identifier (a hash made by the app) are stored. No name, email or IP:
// the rate limit counts by a hash of the IP that expires in one hour.
"use strict";

const crypto = require("crypto");

const DAY = 86400;
const OFFLINE_DAYS = 7;
const CODE_ALPHABET = "23456789ABCDEFGHJKMNPQRSTVWXYZ";
const CODE_PATTERN = /^AUD-[2-9A-HJKMNP-TV-Z]{4}-[2-9A-HJKMNP-TV-Z]{4}$/;
const MACHINE_PATTERN = /^[A-Za-z0-9_-]{16,64}$/;

function now() {
  return Math.floor(Date.now() / 1000);
}

// ---------- Redis (REST) ----------

function redisConfig() {
  const url = process.env.KV_REST_API_URL || process.env.UPSTASH_REDIS_REST_URL;
  const token = process.env.KV_REST_API_TOKEN || process.env.UPSTASH_REDIS_REST_TOKEN;
  if (!url || !token) throw new ServerError("storage", "O banco de convites ainda não foi configurado.");
  return { url: url.replace(/\/+$/, ""), token };
}

async function redis(...command) {
  const { url, token } = redisConfig();
  const res = await fetch(url, {
    method: "POST",
    headers: { Authorization: "Bearer " + token, "Content-Type": "application/json" },
    body: JSON.stringify(command.map(String)),
  });
  const data = await res.json().catch(() => ({}));
  if (!res.ok || data.error) throw new ServerError("storage", "Falha no banco: " + (data.error || res.status));
  return data.result;
}

class ServerError extends Error {
  constructor(code, message, status = 500) {
    super(message);
    this.code = code;
    this.status = status;
  }
}

// ---------- Invites ----------
// inv:<CODE> = JSON { code, days|null, maxPcs, badge, note, createdAt, activatedAt|null, expiresAt|null,
//                     cancelled, pcs: [{ id, first, last }] }
// invites    = set of every code (for the admin list)

function normalizeCode(text) {
  const raw = String(text || "").toUpperCase().replace(/[^0-9A-Z]/g, "");
  const body = raw.startsWith("AUD") ? raw.slice(3) : raw;
  if (body.length !== 8) return null;
  const code = "AUD-" + body.slice(0, 4) + "-" + body.slice(4);
  return CODE_PATTERN.test(code) ? code : null;
}

function newCode() {
  let body = "";
  for (let i = 0; i < 8; i++) body += CODE_ALPHABET[crypto.randomInt(CODE_ALPHABET.length)];
  return "AUD-" + body.slice(0, 4) + "-" + body.slice(4);
}

async function getInvite(code) {
  const text = await redis("GET", "inv:" + code);
  return text ? JSON.parse(text) : null;
}

async function saveInvite(invite) {
  await redis("SET", "inv:" + invite.code, JSON.stringify(invite));
  await redis("SADD", "invites", invite.code);
}

/** "ok", "cancelled" or "expired" at time t. */
function inviteStatus(invite, t = now()) {
  if (invite.cancelled) return "cancelled";
  if (invite.expiresAt != null && t >= invite.expiresAt) return "expired";
  return "ok";
}

// ---------- License ----------

function signingKey() {
  const pem = process.env.AUDIAL_LICENSE_KEY;
  if (!pem) throw new ServerError("key", "A chave das licenças ainda não foi configurada.");
  // Only the base64 between BEGIN and END matters: a paste that lost its line breaks (or turned them into spaces or
  // "\n") still works, and so does the base64 alone.
  const body = pem
    .replace(/-----(BEGIN|END)[^-]*-----/g, "")
    .replace(/\\n/g, "")
    .replace(/[^A-Za-z0-9+/=]/g, "");
  try {
    return crypto.createPrivateKey({ key: Buffer.from(body, "base64"), format: "der", type: "pkcs8" });
  } catch {
    throw new ServerError("key", "A chave das licenças na Vercel (AUDIAL_LICENSE_KEY) está em formato inválido. Cole de novo o conteúdo do arquivo .pem.");
  }
}

/**
 * The license the app keeps: base64 of a small JSON plus an ECDSA P-256 / SHA-256 signature of those exact bytes
 * (IEEE P1363, 64 bytes). "until" is when the app must have talked to the server again (7 days offline at most,
 * never past the end of the invite); "exp" is the end of the invite (null = no limit).
 */
function issueLicense(invite, machine, t = now()) {
  const until = invite.expiresAt == null ? t + OFFLINE_DAYS * DAY : Math.min(invite.expiresAt, t + OFFLINE_DAYS * DAY);
  const payload = {
    v: 1,
    code: invite.code,
    mid: machine,
    badge: invite.badge === "apoiador" ? "apoiador" : "beta",
    iat: t,
    until,
    exp: invite.expiresAt ?? null,
  };
  const bytes = Buffer.from(JSON.stringify(payload), "utf8");
  const signature = crypto.sign("sha256", bytes, { key: signingKey(), dsaEncoding: "ieee-p1363" });
  return { license: bytes.toString("base64"), signature: signature.toString("base64"), info: payload };
}

/**
 * Who is calling, from the license the app already has (no password, no new secret): the signature must be ours and
 * still inside its 7 days, and the invite must still be valid with this PC in it. Returns { invite, info }.
 */
async function authenticate(license, signature) {
  const unauthorized = () => new ServerError("license", "Licença do beta inválida. Abra o Audial com um convite válido.", 401);
  if (typeof license !== "string" || typeof signature !== "string" || license.length > 4096 || signature.length > 256) throw unauthorized();
  const bytes = Buffer.from(license, "base64");
  let ok = false;
  try {
    ok = crypto.verify("sha256", bytes, { key: crypto.createPublicKey(signingKey()), dsaEncoding: "ieee-p1363" }, Buffer.from(signature, "base64"));
  } catch (e) {
    if (e instanceof ServerError) throw e;
  }
  if (!ok) throw unauthorized();
  const info = safeJson(bytes.toString("utf8"));
  if (info.v !== 1 || typeof info.code !== "string" || typeof info.mid !== "string" || !(info.until > now())) throw unauthorized();
  const invite = await getInvite(info.code);
  if (!invite || inviteStatus(invite) !== "ok" || !invite.pcs.some((p) => p.id === info.mid)) throw unauthorized();
  return { invite, info };
}

// ---------- HTTP helpers ----------

function send(res, status, body) {
  res.statusCode = status;
  res.setHeader("Content-Type", "application/json; charset=utf-8");
  res.setHeader("Cache-Control", "no-store");
  res.setHeader("X-Robots-Tag", "noindex");
  res.end(JSON.stringify(body));
}

async function readBody(req) {
  if (req.body && typeof req.body === "object") return req.body;
  if (typeof req.body === "string") return safeJson(req.body);
  const chunks = [];
  let size = 0;
  for await (const chunk of req) {
    size += chunk.length;
    if (size > 16 * 1024) throw new ServerError("body", "Pedido grande demais.", 413);
    chunks.push(chunk);
  }
  return safeJson(Buffer.concat(chunks).toString("utf8"));
}

function safeJson(text) {
  try {
    const value = JSON.parse(text || "{}");
    return value && typeof value === "object" ? value : {};
  } catch {
    return {};
  }
}

function clientIp(req) {
  const forwarded = String(req.headers["x-forwarded-for"] || "").split(",")[0].trim();
  return forwarded || String(req.headers["x-real-ip"] || "") || (req.socket && req.socket.remoteAddress) || "?";
}

/** True when this IP made fewer than `limit` calls of this kind in the current hour. Only a hash of the IP is kept. */
async function allow(req, kind, limit) {
  const ip = crypto.createHash("sha256").update("audial-rl:" + clientIp(req)).digest("base64url").slice(0, 22);
  const key = "rl:" + kind + ":" + ip + ":" + Math.floor(now() / 3600);
  const count = Number(await redis("INCR", key));
  if (count === 1) await redis("EXPIRE", key, 3600);
  return count <= limit;
}

function handler(methods, fn) {
  return async (req, res) => {
    if (!methods.includes(req.method)) {
      res.setHeader("Allow", methods.join(", "));
      return send(res, 405, { status: "method", message: "Método não permitido." });
    }
    try {
      await fn(req, res);
    } catch (e) {
      if (e instanceof ServerError) return send(res, e.status, { status: e.code, message: e.message });
      console.error(e);
      send(res, 500, { status: "error", message: "Erro no servidor." });
    }
  };
}

module.exports = {
  DAY,
  OFFLINE_DAYS,
  MACHINE_PATTERN,
  ServerError,
  now,
  redis,
  normalizeCode,
  newCode,
  getInvite,
  saveInvite,
  inviteStatus,
  issueLicense,
  authenticate,
  send,
  readBody,
  allow,
  clientIp,
  handler,
};
