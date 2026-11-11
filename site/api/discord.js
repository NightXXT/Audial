// POST /api/discord — the Audial bot on Discord, as an "Interactions Endpoint" (no process running anywhere: Discord
// calls this URL on every slash command or button click, signed with the app's Ed25519 key).
//
//   /sorteio abrir [ganhadores] [dias] [premio]   (needs "Gerenciar servidor") opens a giveaway in the channel
//   /sorteio sortear                              picks the winners, creates their invites, sends the code by DM
//                                                 (with the invite flyer as an image, see convite.js),
//                                                 gives the Beta role and announces who won (never the code)
//   /sorteio cancelar                             closes the giveaway without a draw
//   /beta codigo:AUD-XXXX-XXXX                    gives the Beta role to whoever has a valid invite (one account per invite)
//   /audial                                       the current version, read from the timeline of the site (index.html)
//
// Settings (Vercel → Environment Variables):
//   DISCORD_PUBLIC_KEY     "Public Key" of the app (Developer Portal → General Information)
//   DISCORD_APP_ID         "Application ID"
//   DISCORD_BOT_TOKEN      the bot's token (secret; Bot → Reset Token)
//   DISCORD_BETA_ROLE_ID   optional: id of the "Beta" role (the bot's role must sit above it)
//
// Stored: the open giveaway of a channel (Discord ids and names of who joined, deleted after the draw) and, on an
// invite, the Discord id of the account that won or claimed it.
"use strict";

const crypto = require("crypto");
const fs = require("fs");
const path = require("path");
const lib = require("./_lib");

const API = (process.env.DISCORD_API || "https://discord.com/api/v10").replace(/\/+$/, "");
const COLOR = 0xf43f5e;
const MIN_ACCOUNT_DAYS = 7;
const DISCORD_EPOCH = 1420070400000n;
const EPHEMERAL = 64;
const GW = (channel) => "dc:gw:" + channel;

// ---------- Discord plumbing ----------

function verify(raw, signature, timestamp) {
  const hex = String(process.env.DISCORD_PUBLIC_KEY || "").trim();
  if (!/^[0-9a-fA-F]{64}$/.test(hex) || !/^[0-9a-fA-F]{128}$/.test(String(signature || "")) || !timestamp) return false;
  const key = crypto.createPublicKey({ key: Buffer.concat([Buffer.from("302a300506032b6570032100", "hex"), Buffer.from(hex, "hex")]), format: "der", type: "spki" });
  return crypto.verify(null, Buffer.concat([Buffer.from(String(timestamp)), raw]), key, Buffer.from(signature, "hex"));
}

async function rawBody(req) {
  if (Buffer.isBuffer(req.rawBody)) return req.rawBody;
  const chunks = [];
  let size = 0;
  for await (const chunk of req) {
    size += chunk.length;
    if (size > 64 * 1024) break;
    chunks.push(chunk);
  }
  return Buffer.concat(chunks);
}

async function api(method, route, body) {
  const token = process.env.DISCORD_BOT_TOKEN;
  if (!token) throw new lib.ServerError("discord", "O token do bot (DISCORD_BOT_TOKEN) ainda não foi configurado.");
  const res = await fetch(API + route, {
    method,
    headers: { Authorization: "Bot " + token, "Content-Type": "application/json", "User-Agent": "AudialBot (https://audial.com.br, 1)" },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (!res.ok) {
    const text = await res.text().catch(() => "");
    const error = new Error(`Discord ${method} ${route}: ${res.status} ${text.slice(0, 200)}`);
    error.status = res.status;
    throw error;
  }
  return res.status === 204 ? null : res.json();
}

const reply = (content, extra = {}) => ({ type: 4, data: { content, allowed_mentions: { parse: [] }, ...extra } });
const secret = (content) => reply(content, { flags: EPHEMERAL });

function userOf(interaction) {
  const user = (interaction.member && interaction.member.user) || interaction.user || {};
  return { id: String(user.id || ""), name: String(user.global_name || user.username || "alguém").slice(0, 40) };
}

function isManager(interaction) {
  const perms = BigInt((interaction.member && interaction.member.permissions) || "0");
  return (perms & 0x20n) !== 0n || (perms & 0x8n) !== 0n; // Manage Guild or Administrator
}

function accountAgeDays(userId) {
  try {
    const created = Number((BigInt(userId) >> 22n) + DISCORD_EPOCH);
    return (Date.now() - created) / 86400000;
  } catch {
    return 0;
  }
}

function option(interaction, name) {
  const walk = (opts) => {
    for (const o of opts || []) {
      if (o.name === name && o.value !== undefined) return o.value;
      const inner = walk(o.options);
      if (inner !== undefined) return inner;
    }
    return undefined;
  };
  return walk(interaction.data && interaction.data.options);
}

async function giveRole(guild, user) {
  const role = String(process.env.DISCORD_BETA_ROLE_ID || "").trim();
  if (!role || !guild) return "sem-cargo";
  try {
    await api("PUT", `/guilds/${guild}/members/${user}/roles/${role}`);
    return "ok";
  } catch (e) {
    console.error(e.message);
    return "falhou";
  }
}

async function sendDm(user, content, image) {
  try {
    const channel = await api("POST", "/users/@me/channels", { recipient_id: user });
    const embeds = image ? [{ color: COLOR, image: { url: image } }] : undefined;
    await api("POST", `/channels/${channel.id}/messages`, { content, embeds, allowed_mentions: { parse: [] } });
    return true;
  } catch (e) {
    console.error(e.message);
    return false;
  }
}

// ---------- commands ----------

const COMMANDS = [
  {
    name: "sorteio",
    description: "Sorteio de convites do beta fechado do Audial",
    default_member_permissions: "32",
    dm_permission: false,
    options: [
      {
        type: 1,
        name: "abrir",
        description: "Abre um sorteio neste canal",
        options: [
          { type: 4, name: "ganhadores", description: "Quantas pessoas ganham (1 a 5). Padrão: 1", min_value: 1, max_value: 5 },
          { type: 4, name: "dias", description: "Prazo do convite em dias (0 = sem limite). Padrão: 30", min_value: 0, max_value: 365 },
          { type: 3, name: "premio", description: "Texto do prêmio", max_length: 100 },
        ],
      },
      { type: 1, name: "sortear", description: "Encerra o sorteio deste canal e escolhe quem ganha" },
      { type: 1, name: "cancelar", description: "Cancela o sorteio aberto neste canal, sem sortear" },
    ],
  },
  {
    name: "beta",
    description: "Ganhe o cargo Beta com o seu código de convite do Audial",
    dm_permission: false,
    options: [{ type: 3, name: "codigo", description: "Seu código (AUD-XXXX-XXXX). Só você vê.", required: true, max_length: 20 }],
  },
  { name: "audial", description: "O que há de novo no Audial" },
];

async function registerCommands() {
  const app = String(process.env.DISCORD_APP_ID || "").trim();
  if (!/^\d{5,25}$/.test(app)) throw new lib.ServerError("discord", "O ID do app (DISCORD_APP_ID) ainda não foi configurado.");
  try {
    const result = await api("PUT", `/applications/${app}/commands`, COMMANDS);
    return result.map((c) => "/" + c.name);
  } catch (e) {
    throw new lib.ServerError("discord", "O Discord recusou: " + e.message, 502);
  }
}

function giveawayMessage(gw, channel) {
  const term = gw.days ? `${gw.days} dias de beta` : "beta sem prazo";
  return {
    embeds: [
      {
        title: "🎟️ Sorteio: " + gw.prize,
        description:
          `Clique em **Participar** para concorrer.\n\n` +
          `• ${gw.winners === 1 ? "1 pessoa ganha" : gw.winners + " pessoas ganham"} um convite do beta fechado (${term}).\n` +
          `• Uma inscrição por pessoa. Contas criadas há menos de ${MIN_ACCOUNT_DAYS} dias não entram.\n` +
          `• Quem ganhar recebe o código por **DM**: deixe suas mensagens diretas abertas.`,
        color: COLOR,
        footer: { text: "Audial · beta fechado · pra todo mundo em 2027" },
      },
    ],
    components: [{ type: 1, components: [{ type: 2, style: 1, label: "Participar", emoji: { name: "🎟️" }, custom_id: "gw:join:" + channel }] }],
  };
}

async function openGiveaway(interaction) {
  const channel = String(interaction.channel_id);
  if (await lib.redis("GET", GW(channel))) return secret("Já tem um sorteio aberto neste canal. Use `/sorteio sortear` ou `/sorteio cancelar` antes.");
  const days = Number(option(interaction, "dias") ?? 30);
  const gw = {
    prize: String(option(interaction, "premio") || "convite do beta fechado do Audial").slice(0, 100),
    winners: Math.min(5, Math.max(1, Number(option(interaction, "ganhadores") || 1))),
    days: days > 0 ? days : null,
    entrants: [],
    createdAt: lib.now(),
    by: userOf(interaction).id,
  };
  await lib.redis("SET", GW(channel), JSON.stringify(gw));
  return { type: 4, data: { ...giveawayMessage(gw, channel), allowed_mentions: { parse: [] } } };
}

async function join(interaction, channel) {
  const user = userOf(interaction);
  const text = await lib.redis("GET", GW(channel));
  if (!text) return secret("Esse sorteio já terminou.");
  if (accountAgeDays(user.id) < MIN_ACCOUNT_DAYS) return secret(`Sua conta do Discord precisa ter pelo menos ${MIN_ACCOUNT_DAYS} dias para participar.`);
  const gw = JSON.parse(text);
  if (gw.entrants.some((e) => e.id === user.id)) return secret(`Você já está participando! Agora são ${gw.entrants.length} pessoas. Boa sorte 🍀`);
  if (gw.entrants.length >= 5000) return secret("O sorteio lotou.");
  gw.entrants.push(user);
  await lib.redis("SET", GW(channel), JSON.stringify(gw));
  return secret(`Pronto, você está no sorteio! Agora são ${gw.entrants.length} pessoas. Se ganhar, o código chega por DM 🍀`);
}

async function draw(interaction) {
  const channel = String(interaction.channel_id);
  const text = await lib.redis("GET", GW(channel));
  if (!text) return secret("Não tem sorteio aberto neste canal.");
  const gw = JSON.parse(text);
  if (gw.entrants.length === 0) return secret("Ninguém entrou no sorteio ainda.");
  const pool = [...gw.entrants];
  const winners = [];
  while (winners.length < gw.winners && pool.length) winners.push(pool.splice(crypto.randomInt(pool.length), 1)[0]);
  await lib.redis("DEL", GW(channel)); // the list of who joined goes away with the draw

  const { createInvite } = require("./admin");
  const results = await Promise.all(
    winners.map(async (w) => {
      const invite = await createInvite({ days: gw.days, maxPcs: 1, badge: "beta", note: "Sorteio Discord: " + w.name, discord: w.id });
      const term = invite.days ? `Ele vale por **${invite.days} dias**, contados a partir de quando você ativar.` : "Ele não tem prazo.";
      const dm = await sendDm(
        w.id,
        `🎉 **Você ganhou um convite do beta fechado do Audial!**\n\n` +
          `Seu código: **${invite.code}**\n${term} Ele funciona em 1 computador.\n\n` +
          `O instalador do Audial chega por mensagem, mandado por quem cria o app. Ao abrir o Audial, digite o código na tela de convite.\n` +
          `Depois, use \`/beta\` no servidor com o mesmo código para ganhar o cargo Beta (se ainda não tiver).\n\n` +
          `Não mostre o código para ninguém. Bem-vindo ao beta 💜`,
        require("./convite").ticketUrl(invite.code, invite.days)
      );
      const role = await giveRole(interaction.guild_id, w.id);
      return { w, dm, role };
    })
  );
  const lines = results.map((r) => `• <@${r.w.id}>${r.dm ? "" : " — não consegui mandar DM: abra suas mensagens diretas e chame a equipe"}`);
  return {
    type: 4,
    data: {
      embeds: [
        {
          title: "🎉 Sorteio encerrado: " + gw.prize,
          description: `Entre ${gw.entrants.length} ${gw.entrants.length === 1 ? "pessoa" : "pessoas"}, ${winners.length === 1 ? "quem ganhou foi" : "ganharam"}:\n${lines.join("\n")}\n\nO código foi por DM. Obrigado a todo mundo que participou!`,
          color: COLOR,
        },
      ],
      allowed_mentions: { users: winners.map((w) => w.id) },
    },
  };
}

async function cancelGiveaway(interaction) {
  const channel = String(interaction.channel_id);
  const removed = Number(await lib.redis("DEL", GW(channel)));
  return removed ? reply("O sorteio deste canal foi cancelado.") : secret("Não tem sorteio aberto neste canal.");
}

async function claimBeta(interaction) {
  const user = userOf(interaction);
  const key = "rl:dcbeta:" + user.id + ":" + Math.floor(lib.now() / 3600);
  const tries = Number(await lib.redis("INCR", key));
  if (tries === 1) await lib.redis("EXPIRE", key, 3600);
  if (tries > 5) return secret("Tentativas demais. Espere uma hora e tente de novo.");
  const code = lib.normalizeCode(option(interaction, "codigo"));
  const invite = code && (await lib.getInvite(code));
  if (!invite) return secret("Não achei esse convite. Confira o código (AUD-XXXX-XXXX).");
  const status = lib.inviteStatus(invite);
  if (status !== "ok") return secret(status === "expired" ? "Esse convite já terminou." : "Esse convite foi cancelado.");
  if (invite.discord && invite.discord !== user.id) return secret("Esse convite já está ligado a outra conta do Discord.");
  if (!invite.discord) {
    invite.discord = user.id;
    await lib.saveInvite(invite);
  }
  const role = await giveRole(interaction.guild_id, user.id);
  if (role === "ok") return secret("Pronto! Você ganhou o cargo **Beta** 💜");
  if (role === "sem-cargo") return secret("Convite confirmado! O cargo Beta ainda não foi configurado no servidor; avise a equipe.");
  return secret("Convite confirmado, mas não consegui dar o cargo. Avise a equipe (o cargo do bot precisa ficar acima do cargo Beta).");
}

function latest() {
  let html = "";
  try {
    html = fs.readFileSync(path.join(__dirname, "..", "index.html"), "utf8");
  } catch {
    return null;
  }
  const item = html.match(/<li class="tl-item now">([\s\S]*?)<\/li>/);
  if (!item) return null;
  const strip = (s) => (s || "").replace(/<[^>]+>/g, "").replace(/\s+/g, " ").trim();
  return {
    version: strip((item[1].match(/tl-ver">([\s\S]*?)</) || [])[1]),
    date: strip((item[1].match(/tl-date">([\s\S]*?)</) || [])[1]),
    title: strip((item[1].match(/<h3>([\s\S]*?)<\/h3>/) || [])[1]),
    text: strip((item[1].match(/<p>([\s\S]*?)<\/p>/) || [])[1]),
  };
}

function about() {
  const now = latest();
  return reply("", {
    embeds: [
      {
        title: now ? `Audial ${now.version} · ${now.title}` : "Audial",
        url: "https://audial.com.br",
        description: (now ? now.text + "\n\n" : "") + "Um app de música de verdade, feito pro Windows. Em beta fechado agora, pra todo mundo em 2027.",
        color: COLOR,
        footer: { text: now && now.date ? "Em teste no beta desde " + now.date.toLowerCase() : "audial.com.br" },
      },
    ],
  });
}

// ---------- entry ----------

async function handle(interaction) {
  if (interaction.type === 1) return { type: 1 }; // PING
  if (interaction.type === 3) {
    const id = String((interaction.data && interaction.data.custom_id) || "");
    if (id.startsWith("gw:join:")) return join(interaction, id.slice("gw:join:".length));
    return secret("Esse botão não vale mais.");
  }
  if (interaction.type !== 2) return secret("Não entendi.");
  const name = interaction.data && interaction.data.name;
  if (name === "audial") return about();
  if (name === "beta") return claimBeta(interaction);
  if (name === "sorteio") {
    if (!isManager(interaction)) return secret("Só a equipe pode mexer nos sorteios.");
    const sub = ((interaction.data.options || [])[0] || {}).name;
    if (sub === "abrir") return openGiveaway(interaction);
    if (sub === "sortear") return draw(interaction);
    if (sub === "cancelar") return cancelGiveaway(interaction);
  }
  return secret("Comando desconhecido.");
}

module.exports = async (req, res) => {
  if (req.method !== "POST") {
    res.statusCode = 405;
    return res.end();
  }
  const raw = await rawBody(req);
  if (!verify(raw, req.headers["x-signature-ed25519"], req.headers["x-signature-timestamp"])) {
    res.statusCode = 401;
    return res.end("invalid request signature");
  }
  let interaction;
  try {
    interaction = JSON.parse(raw.toString("utf8"));
  } catch {
    res.statusCode = 400;
    return res.end();
  }
  let answer;
  try {
    answer = await handle(interaction);
  } catch (e) {
    console.error(e);
    answer = secret(e instanceof lib.ServerError ? e.message : "Deu um erro aqui do meu lado. Tente de novo daqui a pouco.");
  }
  res.statusCode = 200;
  res.setHeader("Content-Type", "application/json");
  res.end(JSON.stringify(answer));
};

module.exports.registerCommands = registerCommands;
module.exports.COMMANDS = COMMANDS;
