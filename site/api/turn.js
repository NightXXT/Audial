// POST /api/turn { license, signature } — short-lived TURN credentials for "Ouvir junto".
//
// Most rooms connect directly (STUN). When one side is behind a strict NAT (4G, some providers' CGNAT), the direct path
// fails and the link has to go through a relay. The relay is Metered's TURN service (free plan, no card); it only ever sees the WebRTC
// traffic, which is already encrypted end to end (DTLS), and the rooms only send small sync messages, never audio.
//
// Only apps with a valid beta license get credentials (lib.authenticate), and they expire in a day.
// Settings (Vercel → Environment Variables):
//   METERED_TURN_APP       the app name of the Metered account (the "xxx" in xxx.metered.live)
//   METERED_TURN_API_KEY   its TURN credential API key (secret)
// Or, instead, Cloudflare Realtime TURN (needs a card on file):
//   CLOUDFLARE_TURN_KEY_ID / CLOUDFLARE_TURN_API_TOKEN
"use strict";

const lib = require("./_lib");

const TTL = 86400;

/** Metered answers [{...}], Cloudflare { iceServers: {...} } or { iceServers: [{...}] }; keep only turn:/turns: entries with credentials. */
function normalize(data) {
  const list = Array.isArray(data) ? data : Array.isArray(data && data.iceServers) ? data.iceServers : data && data.iceServers ? [data.iceServers] : [];
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
  const meteredApp = String(process.env.METERED_TURN_APP || "").trim().replace(/^https?:\/\//i, "").replace(/\.metered\.live.*$/i, "");
  const meteredKey = String(process.env.METERED_TURN_API_KEY || "").trim();
  const keyId = String(process.env.CLOUDFLARE_TURN_KEY_ID || "").trim();
  const token = String(process.env.CLOUDFLARE_TURN_API_TOKEN || "").trim();
  let response;
  if (meteredApp && meteredKey) {
    if (!/^[a-z0-9-]{1,63}$/i.test(meteredApp)) return lib.send(res, 503, { status: "setup", message: "METERED_TURN_APP inválido." });
    response = await fetch(`https://${meteredApp}.metered.live/api/v1/turn/credentials?apiKey=${encodeURIComponent(meteredKey)}`);
  } else if (keyId && token) {
    response = await fetch(`https://rtc.live.cloudflare.com/v1/turn/keys/${encodeURIComponent(keyId)}/credentials/generate-ice-servers`, {
      method: "POST",
      headers: { Authorization: "Bearer " + token, "Content-Type": "application/json" },
      body: JSON.stringify({ ttl: TTL }),
    });
  } else {
    return lib.send(res, 503, { status: "setup", message: "O relay das salas ainda não foi configurado." });
  }
  if (!response.ok) {
    console.error("turn provider", response.status, (await response.text()).slice(0, 200));
    return lib.send(res, 502, { status: "relay", message: "O relay das salas não respondeu." });
  }
  const iceServers = normalize(await response.json());
  lib.send(res, 200, { status: "ok", ttl: TTL, iceServers });
});
