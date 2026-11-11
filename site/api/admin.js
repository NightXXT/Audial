// POST /api/admin { action, ... } with "Authorization: Bearer <AUDIAL_ADMIN_PASSWORD>". Used by the hidden admin page.
// Actions: list, create, setDays, extend, update, cancel, restore, removePc, delete.
"use strict";

const crypto = require("crypto");
const lib = require("./_lib");

function sameSecret(a, b) {
  const ha = crypto.createHash("sha256").update(String(a)).digest();
  const hb = crypto.createHash("sha256").update(String(b)).digest();
  return crypto.timingSafeEqual(ha, hb);
}

async function authorized(req) {
  const password = process.env.AUDIAL_ADMIN_PASSWORD;
  if (!password || password.length < 12) throw new lib.ServerError("setup", "A senha do painel ainda não foi configurada (mínimo 12 caracteres).");
  const ipKey = "rl:admfail:" + crypto.createHash("sha256").update("audial-rl:" + lib.clientIp(req)).digest("base64url").slice(0, 22);
  const failures = Number((await lib.redis("GET", ipKey)) || 0);
  if (failures >= 10) throw new lib.ServerError("limit", "Senha errada vezes demais. Espere uma hora.", 429);
  const header = String(req.headers.authorization || "");
  const given = header.startsWith("Bearer ") ? header.slice(7) : "";
  if (given && sameSecret(given, password)) return;
  const count = Number(await lib.redis("INCR", ipKey));
  if (count === 1) await lib.redis("EXPIRE", ipKey, 3600);
  throw new lib.ServerError("auth", "Senha incorreta.", 401);
}

function cleanDays(value) {
  if (value === null || value === "" || value === undefined) return null;
  const n = Math.round(Number(value));
  if (!Number.isFinite(n) || n < 1 || n > 3650) throw new lib.ServerError("input", "Prazo inválido (1 a 3650 dias, ou sem limite).", 400);
  return n;
}

function cleanPcs(value) {
  const n = Math.round(Number(value));
  if (!Number.isFinite(n) || n < 1 || n > 20) throw new lib.ServerError("input", "Número de PCs inválido (1 a 20).", 400);
  return n;
}

function cleanBadge(value) {
  return value === "apoiador" ? "apoiador" : "beta";
}

function cleanNote(value) {
  return String(value || "").slice(0, 120);
}

async function load(code) {
  const c = lib.normalizeCode(code);
  const invite = c && (await lib.getInvite(c));
  if (!invite) throw new lib.ServerError("unknown", "Convite não encontrado.", 404);
  return invite;
}

function view(invite, t) {
  return {
    ...invite,
    status: lib.inviteStatus(invite, t),
    daysLeft: invite.expiresAt == null ? null : Math.max(0, Math.ceil((invite.expiresAt - t) / lib.DAY)),
  };
}

const actions = {
  async list() {
    const codes = (await lib.redis("SMEMBERS", "invites")) || [];
    const invites = [];
    for (const code of codes) {
      const invite = await lib.getInvite(code);
      if (invite) invites.push(invite);
    }
    const t = lib.now();
    invites.sort((a, b) => b.createdAt - a.createdAt);
    return { now: t, invites: invites.map((i) => view(i, t)) };
  },

  async create(body) {
    let code;
    for (let i = 0; i < 5 && !code; i++) {
      const candidate = lib.newCode();
      if (!(await lib.getInvite(candidate))) code = candidate;
    }
    if (!code) throw new lib.ServerError("error", "Não consegui gerar um código livre. Tente de novo.");
    const invite = {
      code,
      days: cleanDays(body.days),
      maxPcs: cleanPcs(body.maxPcs ?? 1),
      badge: cleanBadge(body.badge),
      note: cleanNote(body.note),
      createdAt: lib.now(),
      activatedAt: null,
      expiresAt: null,
      cancelled: false,
      pcs: [],
    };
    await lib.saveInvite(invite);
    return { invite: view(invite, lib.now()) };
  },

  // Term counted from activation; already activated invites get their end moved to activation + days.
  async setDays(body) {
    const invite = await load(body.code);
    invite.days = cleanDays(body.days);
    if (invite.activatedAt != null) invite.expiresAt = invite.days == null ? null : invite.activatedAt + invite.days * lib.DAY;
    await lib.saveInvite(invite);
    return { invite: view(invite, lib.now()) };
  },

  // Adds (or with a negative number removes) days. An expired invite extended starts counting from today.
  async extend(body) {
    const invite = await load(body.code);
    const delta = Math.round(Number(body.days));
    if (!Number.isFinite(delta) || delta === 0 || Math.abs(delta) > 3650) throw new lib.ServerError("input", "Número de dias inválido.", 400);
    const t = lib.now();
    if (invite.activatedAt == null) {
      if (invite.days == null) throw new lib.ServerError("input", "Este convite não tem prazo.", 400);
      invite.days = Math.max(1, invite.days + delta);
    } else {
      if (invite.expiresAt == null) throw new lib.ServerError("input", "Este convite não tem prazo.", 400);
      const base = delta > 0 ? Math.max(invite.expiresAt, t) : invite.expiresAt;
      invite.expiresAt = Math.max(invite.activatedAt, base + delta * lib.DAY);
      invite.days = Math.max(1, Math.round((invite.expiresAt - invite.activatedAt) / lib.DAY));
    }
    await lib.saveInvite(invite);
    return { invite: view(invite, t) };
  },

  async update(body) {
    const invite = await load(body.code);
    if (body.maxPcs !== undefined) invite.maxPcs = cleanPcs(body.maxPcs);
    if (body.badge !== undefined) invite.badge = cleanBadge(body.badge);
    if (body.note !== undefined) invite.note = cleanNote(body.note);
    if (body.creator !== undefined) invite.creator = body.creator === true;
    await lib.saveInvite(invite);
    return { invite: view(invite, lib.now()) };
  },

  async cancel(body) {
    const invite = await load(body.code);
    invite.cancelled = true;
    await lib.saveInvite(invite);
    return { invite: view(invite, lib.now()) };
  },

  async restore(body) {
    const invite = await load(body.code);
    invite.cancelled = false;
    await lib.saveInvite(invite);
    return { invite: view(invite, lib.now()) };
  },

  async removePc(body) {
    const invite = await load(body.code);
    invite.pcs = invite.pcs.filter((p) => p.id !== String(body.machine || ""));
    await lib.saveInvite(invite);
    return { invite: view(invite, lib.now()) };
  },

  async delete(body) {
    const invite = await load(body.code);
    await lib.redis("DEL", "inv:" + invite.code);
    await lib.redis("SREM", "invites", invite.code);
    // the community nickname goes too (posts stay, shown as "Alguém do beta")
    const nick = await lib.redis("HGET", "community:nicks", invite.code);
    if (nick) {
      await lib.redis("HDEL", "community:nicknames", nick.toLowerCase());
      await lib.redis("HDEL", "community:nicks", invite.code);
    }
    return { deleted: invite.code };
  },
};

module.exports = lib.handler(["POST"], async (req, res) => {
  await authorized(req);
  const body = await lib.readBody(req);
  const action = actions[String(body.action || "")];
  if (!action || !Object.prototype.hasOwnProperty.call(actions, body.action)) return lib.send(res, 400, { status: "input", message: "Ação desconhecida." });
  lib.send(res, 200, { status: "ok", ...(await action(body)) });
});
