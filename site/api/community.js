// POST /api/community { license, signature, action, ... } — the closed beta community inside the app.
// Whoever has a valid license gets in (see lib.authenticate); people show up by a nickname tied to their invite.
// The creator is the invite marked "creator" in the admin page: posts news (and pins them on top), creates polls, sets the status of ideas
// and can delete anything. Everyone can post ideas or bugs, vote, comment, answer polls and delete their own posts.
//
// Storage (one Redis hash each, so a whole screen costs a handful of commands on the free plan):
//   community:nicks     code -> nickname            community:nicknames  lowercase nickname -> code
//   community:ideas     id -> idea JSON (votes = list of invite codes)
//   community:comments  idea id -> JSON list of comments
//   community:news      id -> news JSON             community:polls      id -> poll JSON (votes = { code: option })
"use strict";

const crypto = require("crypto");
const lib = require("./_lib");

const STATUSES = ["nova", "analisando", "planejada", "feita", "nao"];
const KINDS = ["ideia", "bug"];
const RESERVED = ["criador", "audial", "admin", "administrador", "moderador", "suporte", "nightx"];
const H = {
  nicks: "community:nicks",
  nicknames: "community:nicknames",
  ideas: "community:ideas",
  comments: "community:comments",
  news: "community:news",
  polls: "community:polls",
};

const fail = (message, status = 400, code = "input") => new lib.ServerError(code, message, status);

function newId() {
  return Date.now().toString(36) + crypto.randomBytes(3).toString("hex");
}

function text(value, min, max, what) {
  // the app's text boxes end lines with "\r" alone: turn every line ending into "\n" before dropping control characters
  const s = String(value ?? "").replace(/\r\n?/g, "\n").replace(/[\u0000-\u0008\u000B-\u001F\u007F]/g, "").replace(/\n{3,}/g, "\n\n").trim();
  if (s.length < min) throw fail(min <= 1 ? `Escreva ${what}.` : `${what[0].toUpperCase() + what.slice(1)} precisa de pelo menos ${min} caracteres.`);
  if (s.length > max) throw fail(`${what[0].toUpperCase() + what.slice(1)} pode ter até ${max} caracteres.`);
  return s;
}

async function hgetJson(hash, field) {
  const value = await lib.redis("HGET", hash, field);
  return value ? JSON.parse(value) : null;
}

async function hgetAllJson(hash) {
  const flat = (await lib.redis("HGETALL", hash)) || [];
  const out = [];
  for (let i = 1; i < flat.length; i += 2) out.push(JSON.parse(flat[i]));
  return out;
}

async function hgetAllMap(hash) {
  const flat = (await lib.redis("HGETALL", hash)) || [];
  const out = {};
  for (let i = 0; i < flat.length; i += 2) out[flat[i]] = flat[i + 1];
  return out;
}

const hset = (hash, field, value) => lib.redis("HSET", hash, field, typeof value === "string" ? value : JSON.stringify(value));

async function limit(code, kind, max) {
  const key = "rl:com:" + kind + ":" + code + ":" + Math.floor(lib.now() / 3600);
  const count = Number(await lib.redis("INCR", key));
  if (count === 1) await lib.redis("EXPIRE", key, 3600);
  if (count > max) throw fail("Calma! Você fez isso muitas vezes nesta hora. Tente de novo mais tarde.", 429, "limit");
}

function author(post, nicks, creators) {
  return {
    nick: nicks[post.code] || "Alguém do beta",
    badge: post.badge === "apoiador" ? "apoiador" : "beta",
    creator: Boolean(post.creator || creators.has(post.code)),
  };
}

const actions = {
  async me(ctx) {
    return { me: ctx.me };
  },

  async setNick(ctx, body) {
    const nick = text(body.nick, 3, 20, "o apelido");
    if (!/^[\p{L}\p{N}][\p{L}\p{N} ._-]*$/u.test(nick)) throw fail("Use letras, números, espaço, ponto, _ ou -.");
    const key = nick.toLowerCase();
    if (!ctx.me.creator && RESERVED.some((r) => key.replace(/[ ._-]/g, "").includes(r))) throw fail("Esse apelido é reservado. Escolha outro.");
    const owner = await lib.redis("HGET", H.nicknames, key);
    if (owner && owner !== ctx.code) throw fail("Esse apelido já está em uso. Escolha outro.");
    const old = await lib.redis("HGET", H.nicks, ctx.code);
    if (!owner && !(await lib.redis("HSETNX", H.nicknames, key, ctx.code))) throw fail("Esse apelido já está em uso. Escolha outro.");
    if (old && old.toLowerCase() !== key) await lib.redis("HDEL", H.nicknames, old.toLowerCase());
    await lib.redis("HSET", H.nicks, ctx.code, nick);
    return { me: { ...ctx.me, nick } };
  },

  async feed(ctx) {
    const [nicks, ideas, news, polls] = [await hgetAllMap(H.nicks), await hgetAllJson(H.ideas), await hgetAllJson(H.news), await hgetAllJson(H.polls)];
    const creators = new Set([...ideas, ...news].filter((p) => p.creator).map((p) => p.code));
    const order = (s) => (s === "feita" || s === "nao" ? 1 : 0);
    ideas.sort((a, b) => order(a.status) - order(b.status) || b.votes.length - a.votes.length || b.createdAt - a.createdAt);
    news.sort((a, b) => Number(Boolean(b.pinned)) - Number(Boolean(a.pinned)) || b.createdAt - a.createdAt);
    polls.sort((a, b) => Number(a.closed) - Number(b.closed) || b.createdAt - a.createdAt);
    return {
      me: ctx.me,
      news: news.slice(0, 20).map((n) => ({ id: n.id, title: n.title, body: n.body, pinned: Boolean(n.pinned), createdAt: n.createdAt, author: author(n, nicks, creators) })),
      polls: polls.slice(0, 10).map((p) => {
        const counts = p.options.map(() => 0);
        for (const v of Object.values(p.votes)) if (counts[v] !== undefined) counts[v]++;
        return { id: p.id, question: p.question, options: p.options, counts, total: Object.keys(p.votes).length, myVote: p.votes[ctx.code] ?? null, closed: p.closed, createdAt: p.createdAt };
      }),
      ideas: ideas.slice(0, 200).map((i) => ({
        id: i.id,
        kind: i.kind,
        title: i.title,
        body: i.body,
        status: i.status,
        votes: i.votes.length,
        voted: i.votes.includes(ctx.code),
        mine: i.code === ctx.code,
        comments: i.comments || 0,
        createdAt: i.createdAt,
        author: author(i, nicks, creators),
      })),
    };
  },

  async createIdea(ctx, body) {
    const kind = KINDS.includes(body.kind) ? body.kind : "ideia";
    const title = text(body.title, 4, 100, "o título");
    const content = text(body.body, 0, 2000, "a descrição");
    await limit(ctx.code, "post", 20);
    const idea = { id: newId(), kind, title, body: content, code: ctx.code, badge: ctx.me.badge, creator: ctx.me.creator, status: "nova", votes: [ctx.code], comments: 0, createdAt: lib.now() };
    await hset(H.ideas, idea.id, idea);
    return { id: idea.id };
  },

  async vote(ctx, body) {
    const idea = await hgetJson(H.ideas, String(body.id || ""));
    if (!idea) throw fail("Essa ideia não existe mais.", 404, "unknown");
    await limit(ctx.code, "vote", 200);
    const has = idea.votes.includes(ctx.code);
    idea.votes = has ? idea.votes.filter((c) => c !== ctx.code) : [...idea.votes, ctx.code];
    await hset(H.ideas, idea.id, idea);
    return { votes: idea.votes.length, voted: !has };
  },

  async comments(ctx, body) {
    const id = String(body.id || "");
    const [list, nicks] = [(await hgetJson(H.comments, id)) || [], await hgetAllMap(H.nicks)];
    const creators = new Set(list.filter((c) => c.creator).map((c) => c.code));
    return { comments: list.map((c) => ({ id: c.id, text: c.text, createdAt: c.createdAt, mine: c.code === ctx.code, author: author(c, nicks, creators) })) };
  },

  async comment(ctx, body) {
    const idea = await hgetJson(H.ideas, String(body.id || ""));
    if (!idea) throw fail("Essa ideia não existe mais.", 404, "unknown");
    const content = text(body.text, 1, 1000, "o comentário");
    await limit(ctx.code, "post", 40);
    const list = (await hgetJson(H.comments, idea.id)) || [];
    if (list.length >= 300) throw fail("Essa conversa chegou ao limite de comentários.");
    list.push({ id: newId(), code: ctx.code, badge: ctx.me.badge, creator: ctx.me.creator, text: content, createdAt: lib.now() });
    idea.comments = list.length;
    await hset(H.comments, idea.id, list);
    await hset(H.ideas, idea.id, idea);
    return { comments: list.length };
  },

  async deleteComment(ctx, body) {
    const idea = await hgetJson(H.ideas, String(body.id || ""));
    if (!idea) throw fail("Essa ideia não existe mais.", 404, "unknown");
    const list = (await hgetJson(H.comments, idea.id)) || [];
    const target = list.find((c) => c.id === body.commentId);
    if (!target) return { comments: list.length };
    if (target.code !== ctx.code && !ctx.me.creator) throw fail("Só quem escreveu pode apagar.", 403, "forbidden");
    const rest = list.filter((c) => c !== target);
    idea.comments = rest.length;
    await hset(H.comments, idea.id, rest);
    await hset(H.ideas, idea.id, idea);
    return { comments: rest.length };
  },

  async deleteIdea(ctx, body) {
    const idea = await hgetJson(H.ideas, String(body.id || ""));
    if (!idea) return {};
    if (idea.code !== ctx.code && !ctx.me.creator) throw fail("Só quem escreveu pode apagar.", 403, "forbidden");
    await lib.redis("HDEL", H.ideas, idea.id);
    await lib.redis("HDEL", H.comments, idea.id);
    return {};
  },

  async votePoll(ctx, body) {
    const poll = await hgetJson(H.polls, String(body.id || ""));
    if (!poll) throw fail("Essa enquete não existe mais.", 404, "unknown");
    if (poll.closed) throw fail("Essa enquete já foi encerrada.");
    const option = Math.round(Number(body.option));
    if (!(option >= 0 && option < poll.options.length)) throw fail("Opção inválida.");
    await limit(ctx.code, "vote", 200);
    poll.votes[ctx.code] = option;
    await hset(H.polls, poll.id, poll);
    return {};
  },

  // ---- creator only ----

  async setStatus(ctx, body) {
    creatorOnly(ctx);
    const idea = await hgetJson(H.ideas, String(body.id || ""));
    if (!idea) throw fail("Essa ideia não existe mais.", 404, "unknown");
    if (!STATUSES.includes(body.status)) throw fail("Status inválido.");
    idea.status = body.status;
    await hset(H.ideas, idea.id, idea);
    return {};
  },

  async postNews(ctx, body) {
    creatorOnly(ctx);
    const item = { id: newId(), code: ctx.code, badge: ctx.me.badge, creator: true, title: text(body.title, 3, 100, "o título"), body: text(body.body, 1, 3000, "o aviso"), pinned: body.pinned === true, createdAt: lib.now() };
    await hset(H.news, item.id, item);
    return { id: item.id };
  },

  // A pinned news item stays above the newer ones (e.g. the welcome message).
  async pinNews(ctx, body) {
    creatorOnly(ctx);
    const item = await hgetJson(H.news, String(body.id || ""));
    if (!item) throw fail("Esse aviso não existe mais.", 404, "unknown");
    item.pinned = body.pinned !== false;
    await hset(H.news, item.id, item);
    return {};
  },

  async deleteNews(ctx, body) {
    creatorOnly(ctx);
    await lib.redis("HDEL", H.news, String(body.id || ""));
    return {};
  },

  async createPoll(ctx, body) {
    creatorOnly(ctx);
    const question = text(body.question, 3, 150, "a pergunta");
    const options = (Array.isArray(body.options) ? body.options : []).map((o) => String(o ?? "").trim()).filter(Boolean);
    if (options.length < 2 || options.length > 6) throw fail("A enquete precisa de 2 a 6 opções.");
    const poll = { id: newId(), question, options: options.map((o) => text(o, 1, 60, "a opção")), votes: {}, closed: false, createdAt: lib.now() };
    await hset(H.polls, poll.id, poll);
    return { id: poll.id };
  },

  async closePoll(ctx, body) {
    creatorOnly(ctx);
    const poll = await hgetJson(H.polls, String(body.id || ""));
    if (!poll) return {};
    poll.closed = body.closed !== false;
    await hset(H.polls, poll.id, poll);
    return {};
  },

  async deletePoll(ctx, body) {
    creatorOnly(ctx);
    await lib.redis("HDEL", H.polls, String(body.id || ""));
    return {};
  },
};

function creatorOnly(ctx) {
  if (!ctx.me.creator) throw fail("Só o criador pode fazer isso.", 403, "forbidden");
}

const OPEN_WITHOUT_NICK = new Set(["me", "setNick"]);

module.exports = lib.handler(["POST"], async (req, res) => {
  if (!(await lib.allow(req, "com", 600))) return lib.send(res, 429, { status: "limit", message: "Muitos pedidos. Espere um pouco." });
  const body = await lib.readBody(req);
  const action = Object.prototype.hasOwnProperty.call(actions, body.action) ? actions[body.action] : null;
  if (!action) return lib.send(res, 400, { status: "input", message: "Ação desconhecida." });
  const { invite } = await lib.authenticate(body.license, body.signature);
  const nick = await lib.redis("HGET", H.nicks, invite.code);
  const ctx = { code: invite.code, me: { nick: nick || null, badge: invite.badge === "apoiador" ? "apoiador" : "beta", creator: Boolean(invite.creator) } };
  if (!nick && !OPEN_WITHOUT_NICK.has(body.action)) return lib.send(res, 409, { status: "nick", message: "Escolha um apelido para entrar na comunidade.", me: ctx.me });
  lib.send(res, 200, { status: "ok", ...(await action(ctx, body)) });
});
