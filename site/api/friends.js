// POST /api/friends { license, signature, action, ... } — friends, "ouvindo agora" and "ouvir junto" invites.
//
// Who is who: the invite of the license (lib.authenticate) and its beta profile (_profile.js). Other people are only ever
// named by their profile id ("pid", the one in the signed card); invite codes never leave the server.
//
// Rules:
//   - following is a friend request: the other side has to accept. Only accepted friends see each other.
//   - the activity is OFF until the person turns it on ("share"), and "invisible" hides it at any time.
//   - "ouvindo agora" is only the current song (title, artist, an allowed cover and the time), kept for a few minutes
//     (key with TTL, no history). Nothing is stored about what was heard before.
//   - "ouvir junto": an invite to a room code goes to one friend; the room code only comes back to them when they accept.
//
// Storage:
//   fr:<code>     set of friend codes          frin:<code>   set of codes that asked me     frout:<code> set I asked
//   frcfg         hash code -> { share, invisible }
//   now:<code>    JSON { title, artist, art, at }, expires after NOW_TTL
//   lt:<code>     hash fromCode -> JSON { room, at }, the whole key expires after INVITE_TTL
"use strict";

const lib = require("./_lib");
const P = require("./_profile");

const NOW_TTL = 6 * 60;
const INVITE_TTL = 3 * 60;
const MAX_FRIENDS = 200;
const MAX_PENDING = 50;
const ROOM_PATTERN = /^AUD-[A-Z0-9]{5}-[A-Z0-9]{5}$/;
// covers the app may show: the music service's image hosts only (the app loads them itself; nothing else is accepted)
const ART_HOSTS = [/\.ytimg\.com$/, /\.googleusercontent\.com$/, /\.ggpht\.com$/, /^i\.ytimg\.com$/];

const fail = (message, status = 400, code = "input") => new lib.ServerError(code, message, status);

function text(value, max) {
  return P.stripInvisible(value).slice(0, max);
}

function cleanArt(value) {
  if (typeof value !== "string" || value.length > 600) return null;
  try {
    const url = new URL(value);
    if (url.protocol !== "https:" || url.username || url.password || url.port) return null;
    return ART_HOSTS.some((h) => h.test(url.hostname)) ? url.toString() : null;
  } catch {
    return null;
  }
}

async function limit(code, kind, max) {
  const key = "rl:fr:" + kind + ":" + code + ":" + Math.floor(lib.now() / 3600);
  const count = Number(await lib.redis("INCR", key));
  if (count === 1) await lib.redis("EXPIRE", key, 3600);
  if (count > max) throw fail("Calma! Você fez isso muitas vezes nesta hora. Tente de novo mais tarde.", 429, "limit");
}

async function settingsOf(code) {
  const raw = await lib.redis("HGET", "frcfg", code);
  const s = raw ? JSON.parse(raw) : {};
  return { share: s.share === true, invisible: s.invisible === true };
}

/** The other person by profile id; never yourself. */
async function other(ctx, pid) {
  const code = await P.codeOfPid(pid);
  if (!code || !(await P.getProfile(code))) throw fail("Essa pessoa não tem mais perfil no beta.", 404, "unknown");
  if (code === ctx.code) throw fail("Esse é você.");
  return code;
}

const isMember = async (set, code) => Number(await lib.redis("SISMEMBER", set, code)) === 1;

/** Signed cards of these invites (only those with a profile and a valid invite), code -> { pid, card, signature }. */
async function cardsOf(codes) {
  const out = {};
  const list = [...new Set(codes)];
  if (!list.length) return out;
  const profiles = await P.allProfiles();
  const wanted = list.filter((c) => profiles[c]);
  const invites = wanted.length ? (await lib.redis("MGET", ...wanted.map((c) => "inv:" + c))) || [] : [];
  wanted.forEach((c, i) => {
    const invite = invites[i] ? JSON.parse(invites[i]) : null;
    if (invite && lib.inviteStatus(invite) === "ok") out[c] = { pid: profiles[c].id, ...P.card(profiles[c], invite) };
  });
  return out;
}

const actions = {
  async state(ctx) {
    const [friends, incoming, outgoing, inviteMap, settings] = [
      (await lib.redis("SMEMBERS", "fr:" + ctx.code)) || [],
      (await lib.redis("SMEMBERS", "frin:" + ctx.code)) || [],
      (await lib.redis("SMEMBERS", "frout:" + ctx.code)) || [],
      (await lib.redis("HGETALL", "lt:" + ctx.code)) || [],
      await settingsOf(ctx.code),
    ];
    const cards = await cardsOf([...friends, ...incoming, ...outgoing]);
    // what each friend is hearing: one MGET, and only for those who share and are not invisible
    const nowTexts = friends.length ? (await lib.redis("MGET", ...friends.map((c) => "now:" + c))) || [] : [];
    const cfgTexts = friends.length ? (await lib.redis("HMGET", "frcfg", ...friends)) || [] : [];
    const t = lib.now();
    const invites = [];
    for (let i = 0; i < inviteMap.length; i += 2) {
      const inv = JSON.parse(inviteMap[i + 1]);
      if (cards[inviteMap[i]] && friends.includes(inviteMap[i]) && t - inv.at < INVITE_TTL) invites.push({ pid: cards[inviteMap[i]].pid, at: inv.at });
    }
    return {
      me: { pid: ctx.profile.id, ...settings },
      friends: friends
        .filter((c) => cards[c])
        .map((c, i) => {
          const cfg = cfgTexts[friends.indexOf(c)] ? JSON.parse(cfgTexts[friends.indexOf(c)]) : {};
          const now = nowTexts[friends.indexOf(c)];
          const visible = cfg.share === true && cfg.invisible !== true && now;
          return { ...cards[c], now: visible ? JSON.parse(now) : null };
        }),
      incoming: incoming.filter((c) => cards[c]).map((c) => cards[c]),
      outgoing: outgoing.filter((c) => cards[c]).map((c) => cards[c]),
      invites,
    };
  },

  async request(ctx, body) {
    const code = await other(ctx, body.pid);
    if (await isMember("fr:" + ctx.code, code)) return { result: "friends" };
    await limit(ctx.code, "req", 30);
    // they had already asked me: this is an accept
    if (await isMember("frin:" + ctx.code, code)) return actions.accept(ctx, body);
    if (Number(await lib.redis("SCARD", "fr:" + ctx.code)) >= MAX_FRIENDS) throw fail("Você chegou ao limite de amigos.");
    if (Number(await lib.redis("SCARD", "frin:" + code)) >= MAX_PENDING) throw fail("Essa pessoa tem pedidos demais esperando. Tente mais tarde.");
    await lib.redis("SADD", "frin:" + code, ctx.code);
    await lib.redis("SADD", "frout:" + ctx.code, code);
    return { result: "requested" };
  },

  async accept(ctx, body) {
    const code = await other(ctx, body.pid);
    if (!(await isMember("frin:" + ctx.code, code))) throw fail("Esse pedido de amizade não existe mais.", 404, "unknown");
    if (Number(await lib.redis("SCARD", "fr:" + ctx.code)) >= MAX_FRIENDS) throw fail("Você chegou ao limite de amigos.");
    await lib.redis("SADD", "fr:" + ctx.code, code);
    await lib.redis("SADD", "fr:" + code, ctx.code);
    await lib.redis("SREM", "frin:" + ctx.code, code);
    await lib.redis("SREM", "frout:" + code, ctx.code);
    await lib.redis("SREM", "frin:" + code, ctx.code);
    await lib.redis("SREM", "frout:" + ctx.code, code);
    return { result: "friends" };
  },

  async decline(ctx, body) {
    const code = await other(ctx, body.pid);
    await lib.redis("SREM", "frin:" + ctx.code, code);
    await lib.redis("SREM", "frout:" + code, ctx.code);
    return {};
  },

  async cancel(ctx, body) {
    const code = await other(ctx, body.pid);
    await lib.redis("SREM", "frout:" + ctx.code, code);
    await lib.redis("SREM", "frin:" + code, ctx.code);
    return {};
  },

  async remove(ctx, body) {
    const code = await other(ctx, body.pid);
    await lib.redis("SREM", "fr:" + ctx.code, code);
    await lib.redis("SREM", "fr:" + code, ctx.code);
    await lib.redis("HDEL", "lt:" + ctx.code, code);
    await lib.redis("HDEL", "lt:" + code, ctx.code);
    return {};
  },

  async settings(ctx, body) {
    const s = await settingsOf(ctx.code);
    if (body.share !== undefined) s.share = body.share === true;
    if (body.invisible !== undefined) s.invisible = body.invisible === true;
    await lib.redis("HSET", "frcfg", ctx.code, JSON.stringify(s));
    if (!s.share || s.invisible) await lib.redis("DEL", "now:" + ctx.code);
    return { me: { pid: ctx.profile.id, ...s } };
  },

  // The song playing now. Ignored (and the old one erased) while sharing is off or invisible.
  async presence(ctx, body) {
    await limit(ctx.code, "now", 240);
    const s = await settingsOf(ctx.code);
    if (!s.share || s.invisible || body.clear === true) {
      await lib.redis("DEL", "now:" + ctx.code);
      return { stored: false };
    }
    const title = text(body.title, 150);
    if (!title) throw fail("Música sem título.");
    const now = { title, artist: text(body.artist, 150), art: cleanArt(body.art), at: lib.now() };
    await lib.redis("SET", "now:" + ctx.code, JSON.stringify(now), "EX", NOW_TTL);
    return { stored: true };
  },

  // "Fulano quer ouvir junto": the room code waits on the server and only goes to the friend who accepts.
  async invite(ctx, body) {
    const code = await other(ctx, body.pid);
    if (!(await isMember("fr:" + ctx.code, code))) throw fail("Vocês ainda não são amigos.", 403, "forbidden");
    const room = String(body.room || "").toUpperCase();
    if (!ROOM_PATTERN.test(room)) throw fail("Código de sala inválido.");
    await limit(ctx.code, "inv", 30);
    await lib.redis("HSET", "lt:" + code, ctx.code, JSON.stringify({ room, at: lib.now() }));
    await lib.redis("EXPIRE", "lt:" + code, INVITE_TTL);
    return {};
  },

  async respond(ctx, body) {
    const code = await other(ctx, body.pid);
    const raw = await lib.redis("HGET", "lt:" + ctx.code, code);
    await lib.redis("HDEL", "lt:" + ctx.code, code);
    if (!raw || body.accept !== true) return { room: null };
    const inv = JSON.parse(raw);
    if (lib.now() - inv.at >= INVITE_TTL || !(await isMember("fr:" + ctx.code, code))) throw fail("Esse convite já expirou.", 410, "expired");
    return { room: inv.room };
  },
};

module.exports = lib.handler(["POST"], async (req, res) => {
  if (!(await lib.allow(req, "fr", 900))) return lib.send(res, 429, { status: "limit", message: "Muitos pedidos. Espere um pouco." });
  const body = await lib.readBody(req);
  const action = Object.prototype.hasOwnProperty.call(actions, body.action) ? actions[body.action] : null;
  if (!action) return lib.send(res, 400, { status: "input", message: "Ação desconhecida." });
  const { invite } = await lib.authenticate(body.license, body.signature);
  const profile = await P.getProfile(invite.code);
  if (!profile) return lib.send(res, 409, { status: "profile", message: "Crie seu perfil nas Configurações para usar os amigos." });
  lib.send(res, 200, { status: "ok", ...(await action({ code: invite.code, profile }, body)) });
});
