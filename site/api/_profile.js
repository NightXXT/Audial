// Beta profiles (files starting with "_" are not routes on Vercel).
//
// A profile belongs to an invite (there are no accounts yet): a display name, an avatar (one of the ready-made icons or the
// initials, both on a color of a fixed palette) and badges. No photo, no email. The badges are decided HERE, never by the app:
//   criador   the invite marked "creator" in the admin page (stored in Redis, in the invite)
//   beta      every invite
//   apoiador  the invite marked "apoiador" in the admin page (by hand, for who supported by Pix)
//
// The app gets a signed "card": base64 of a small JSON plus an ECDSA P-256 / SHA-256 signature with the same key as the
// licenses (AUDIAL_LICENSE_KEY). The JSON carries typ "audial-profile" and no "code"/"mid", so it can never pass for a
// license (and a license never passes for a card: it has no "typ"). Other people's apps only show a profile whose card
// checks out against the public key built into the app. The card also carries the public half of a key that only that PC
// has ("pk"): in a listening room each person signs the room code and their peer id with it, so copying someone else's card
// (the Creator's, say) is useless without that PC's private key.
//
// Storage: hash "profiles": invite code -> JSON { id, name, avatar: { k, id?, c }, pk, updatedAt }. The invite code never
// leaves the server: the card has a random profile id instead.
"use strict";

const crypto = require("crypto");
const lib = require("./_lib");

const HASH = "profiles";
const CARD_DAYS = 7;
const NAME_MIN = 2;
const NAME_MAX = 24;
const ICONS = ["fone", "nota", "disco", "microfone", "volume", "equalizador", "raio", "estrela", "coracao", "lua", "sol", "planeta", "jogo", "emoji", "cafe", "video"];
const COLORS = 10; // palette indexes 0..9 (the colors themselves are in the app)
const RESERVED = ["audial", "audio", "criador", "creator", "admin", "adm", "administrador", "moderador", "mod", "suporte", "support", "equipe", "team", "staff", "oficial", "official", "dev", "desenvolvedor"];
// words that are reserved only when they are the whole name (short ones would block too much as substrings)
const RESERVED_WHOLE = new Set(["adm", "mod", "dev", "audio", "team"]);
const LEET = { 0: "o", 1: "i", 2: "z", 3: "e", 4: "a", 5: "s", 6: "g", 7: "t", 8: "b", 9: "g", "@": "a", $: "s", "!": "i", "|": "l", "€": "e" };

const fail = (message, status = 400, code = "input") => new lib.ServerError(code, message, status);

/** Removes invisible and control characters (zero width, bidi marks, BOM, line separators…) and extra spaces. */
function stripInvisible(value) {
  return String(value ?? "")
    .normalize("NFC")
    .replace(/[\p{Cc}\p{Cf}\p{Co}\p{Cn}\p{Zl}\p{Zp}ᅟᅠㅤﾠ⠀͏឴឵᠎]/gu, "")
    .replace(/[\p{Zs}]+/gu, " ")
    .trim();
}

/** "Áúd1al" -> "audial": accents off, lowercase, digits and symbols read as letters, repeated letters collapsed. */
function skeletons(name) {
  const base = name.normalize("NFKD").replace(/\p{M}/gu, "").toLowerCase();
  const variants = new Set();
  for (const one of ["i", "l"]) {
    const mapped = [...base].map((ch) => (ch === "1" ? one : LEET[ch] ?? ch)).join("");
    const letters = mapped.replace(/[^a-z]/g, "");
    variants.add(letters);
    variants.add(letters.replace(/(.)\1+/g, "$1"));
  }
  return [...variants];
}

function isReserved(name) {
  return skeletons(name).some((s) => RESERVED.some((r) => (RESERVED_WHOLE.has(r) ? s === r : s.includes(r))));
}

/** The name as it will be stored, or an error saying why not. */
function cleanName(value, creator = false) {
  const name = stripInvisible(value);
  if (name.length < NAME_MIN) throw fail(`O nome precisa de pelo menos ${NAME_MIN} caracteres.`);
  if ([...name].length > NAME_MAX) throw fail(`O nome pode ter até ${NAME_MAX} caracteres.`);
  // Latin letters (with accents), digits and a few separators only: no look-alike letters from other alphabets, no emoji,
  // no combining marks piled on top of letters
  if (!/^[\p{Script=Latin}\p{Nd}][\p{Script=Latin}\p{Nd} .'_-]*$/u.test(name) || /\p{M}/u.test(name)) throw fail("Use letras, números, espaço, ponto, apóstrofo, _ ou -.");
  if (!/\p{L}/u.test(name)) throw fail("O nome precisa ter pelo menos uma letra.");
  if (/[ .'_-]{2,}/.test(name)) throw fail("Não repita espaços ou sinais seguidos.");
  if (!creator && isReserved(name)) throw fail("Esse nome lembra o Audial ou a equipe. Escolha outro.");
  return name;
}

function cleanAvatar(value) {
  const a = value && typeof value === "object" ? value : {};
  const c = Math.round(Number(a.c));
  if (!(c >= 0 && c < COLORS)) throw fail("Cor inválida.");
  if (a.k === "ini") return { k: "ini", c };
  if (a.k === "icon" && ICONS.includes(a.id)) return { k: "icon", id: a.id, c };
  throw fail("Avatar inválido.");
}

/** Public half (SPKI, base64) of the PC's own P-256 key, or null. */
function cleanKey(value) {
  if (typeof value !== "string" || value.length > 200) return null;
  try {
    const key = crypto.createPublicKey({ key: Buffer.from(value, "base64"), format: "der", type: "spki" });
    if (key.asymmetricKeyType !== "ec" || key.asymmetricKeyDetails?.namedCurve !== "prime256v1") return null;
    return key.export({ format: "der", type: "spki" }).toString("base64");
  } catch {
    return null;
  }
}

function badgesOf(invite) {
  const badges = [];
  if (invite.creator === true) badges.push("criador");
  badges.push("beta");
  if (invite.badge === "apoiador") badges.push("apoiador");
  return badges;
}

async function getProfile(code) {
  const text = await lib.redis("HGET", HASH, code);
  return text ? JSON.parse(text) : null;
}

/** All profiles at once (one command), code -> profile. */
async function allProfiles() {
  const flat = (await lib.redis("HGETALL", HASH)) || [];
  const out = {};
  for (let i = 0; i < flat.length; i += 2) out[flat[i]] = JSON.parse(flat[i + 1]);
  return out;
}

/** The signed card of a profile, with the badges of its invite right now. */
function card(profile, invite, t = lib.now()) {
  const payload = {
    typ: "audial-profile",
    v: 1,
    pid: profile.id,
    name: profile.name,
    avatar: profile.avatar,
    badges: badgesOf(invite),
    pk: profile.pk || null,
    iat: t,
    exp: t + CARD_DAYS * lib.DAY,
  };
  const bytes = Buffer.from(JSON.stringify(payload), "utf8");
  const signature = crypto.sign("sha256", bytes, { key: lib.signingKey(), dsaEncoding: "ieee-p1363" });
  return { card: bytes.toString("base64"), signature: signature.toString("base64") };
}

async function saveProfile(invite, body) {
  const old = await getProfile(invite.code);
  const profile = {
    id: old?.id || crypto.randomBytes(9).toString("base64url"),
    name: cleanName(body.name, invite.creator === true),
    avatar: cleanAvatar(body.avatar),
    pk: cleanKey(body.pk) ?? old?.pk ?? null,
    updatedAt: lib.now(),
  };
  await lib.redis("HSET", HASH, invite.code, JSON.stringify(profile));
  return profile;
}

async function deleteProfile(code) {
  await lib.redis("HDEL", HASH, code);
}

module.exports = { ICONS, COLORS, NAME_MAX, stripInvisible, isReserved, cleanName, cleanAvatar, cleanKey, badgesOf, getProfile, allProfiles, card, saveProfile, deleteProfile };
