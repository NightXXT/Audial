// POST /api/turn { license, signature } — short-lived TURN credentials for "Ouvir junto".
//
// Most rooms connect directly (STUN). When one side is behind a strict NAT (4G, some providers' CGNAT), the direct path
// fails and the link has to go through a relay. The relay is Cloudflare's TURN service; it only ever sees the WebRTC
// traffic, which is already encrypted end to end (DTLS), and the rooms only send small sync messages, never audio.
//
// Only apps with a valid beta license get credentials (lib.authenticate), and they expire in a day.
// Settings (Vercel → Environment Variables):
//   CLOUDFLARE_TURN_KEY_ID     "Turn Token ID" of the TURN key (Cloudflare → Realtime → TURN Server)
//   CLOUDFLARE_TURN_API_TOKEN  its "API Token" (secret)
"use strict";

const lib = require("./_lib");

const TTL = 86400;

/** Cloudflare answers { iceServers: {...} } or { iceServers: [{...}] }; keep only turn:/turns: entries with credentials. */
function normalize(data) {
  const list = Array.isArray(data && data.iceServers) ? data.iceServers : data && data.iceServers ? [data.iceServers] : [];
  const out = [];
  for (const server of list) {
    const urls = (Array.isArray(server.urls) ? server.urls : [server.urls]).filter((u) => typeof u === "string" && /^turns?:/i.test(u));
    if (urls.length && typeof server.username === "string" && typeof server.credential === "string") {
      out.push({ urls, username: server.username, credential: server.credential });
    }
  }
  return out;
}

module.exports = lib.handler(["POST"], async (req, res) => {
  if (!(await lib.allow(req, "turn", 60))) return lib.send(res, 429, { status: "limit", message: "Muitos pedidos. Espere um pouco." });
  const body = await lib.readBody(req);
  await lib.authenticate(body.license, body.signature);
  const keyId = String(process.env.CLOUDFLARE_TURN_KEY_ID || "").trim();
  const token = String(process.env.CLOUDFLARE_TURN_API_TOKEN || "").trim();
  if (!keyId || !token) return lib.send(res, 503, { status: "setup", message: "O relay das salas ainda não foi configurado." });
  const response = await fetch(`https://rtc.live.cloudflare.com/v1/turn/keys/${encodeURIComponent(keyId)}/credentials/generate-ice-servers`, {
    method: "POST",
    headers: { Authorization: "Bearer " + token, "Content-Type": "application/json" },
    body: JSON.stringify({ ttl: TTL }),
  });
  if (!response.ok) {
    console.error("cloudflare turn", response.status, (await response.text()).slice(0, 200));
    return lib.send(res, 502, { status: "relay", message: "O relay das salas não respondeu." });
  }
  const iceServers = normalize(await response.json());
  lib.send(res, 200, { status: "ok", ttl: TTL, iceServers });
});
