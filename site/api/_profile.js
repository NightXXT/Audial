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
// Storage: hash "profiles": invite code -> JSON { id, name, avatar: { k, id?, ph?, c }, pk, updatedAt }. The invite code never
// leaves the server: the card has a random profile id instead (hash "profile:pid": profile id -> invite code, for friends).
//
// Photo (optional): the app crops it square and shrinks it to 128x128 JPEG (~10 KB) before sending. Here only a small
// baseline/progressive JPEG goes in, and it is rewritten without any metadata segment (EXIF, GPS, comments, ICC…).
// It lives in "photo:<hash>" and the card carries that hash ("ph"), so the picture is covered by the signature: an app
// shows a photo only when the bytes it got hash to the "ph" of a card that checks out.
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

const PID_HASH = "profile:pid";
const PHOTO_MAX = 16 * 1024;
const PHOTO_SIDE_MAX = 256;

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
  if (a.k === "photo") return { k: "photo", c };
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

/**
 * The photo as it will be stored: a JPEG of at most 256x256 and 16 KB, rewritten with only the segments needed to draw
 * it (every APPn and COM segment dropped). Throws when it is anything else.
 */
function cleanPhoto(base64) {
  const bad = () => fail("A foto precisa ser um JPEG pequeno (até 256x256 e 16 KB). Escolha a foto de novo.");
  if (typeof base64 !== "string" || base64.length > Math.ceil((PHOTO_MAX * 4) / 3) + 4) throw bad();
  const data = Buffer.from(base64, "base64");
  if (data.length < 100 || data.length > PHOTO_MAX || data[0] !== 0xff || data[1] !== 0xd8) throw bad();
  const out = [Buffer.from([0xff, 0xd8])];
  let i = 2;
  let size = null;
  for (;;) {
    if (i + 4 > data.length || data[i] !== 0xff) throw bad();
    while (data[i] === 0xff && i < data.length) i++;
    const marker = data[i++];
    if (marker === 0xd9) throw bad(); // image ended before any scan
    if ((marker >= 0xd0 && marker <= 0xd7) || marker === 0x01) continue;
    const length = data.readUInt16BE(i);
    if (length < 2 || i + length > data.length) throw bad();
    const segment = data.subarray(i - 2, i + length);
    if (marker === 0xc0 || marker === 0xc1 || marker === 0xc2) {
      size = { h: data.readUInt16BE(i + 3), w: data.readUInt16BE(i + 5), comps: data[i + 7] };
    } else if (marker >= 0xc3 && marker <= 0xcf && marker !== 0xc4 && marker !== 0xc8 && marker !== 0xcc) {
      throw bad(); // lossless, hierarchical, arithmetic… nothing a normal encoder makes for this
    }
    if (marker === 0xda) {
      if (!size || size.w < 16 || size.h < 16 || size.w > PHOTO_SIDE_MAX || size.h > PHOTO_SIDE_MAX || (size.comps !== 1 && size.comps !== 3)) throw bad();
      out.push(data.subarray(i - 2)); // the scan and everything after it (entropy data, more scans, EOI)
      break;
    }
    const metadata = (marker >= 0xe0 && marker <= 0xef) || marker === 0xfe;
    if (!metadata) out.push(segment);
    i += length;
  }
  const clean = Buffer.concat(out);
  if (clean[clean.length - 2] !== 0xff || clean[clean.length - 1] !== 0xd9) throw bad();
  return clean;
}

function photoHash(bytes) {
  return crypto.createHash("sha256").update(bytes).digest("hex").slice(0, 32);
}

async function getPhoto(hash) {
  if (typeof hash !== "string" || !/^[0-9a-f]{32}$/.test(hash)) return null;
  return (await lib.redis("GET", "photo:" + hash)) || null;
}

/** Removes the stored photo of a profile and turns its avatar into the initials (same color). */
async function dropPhoto(code, profile) {
  if (!profile || profile.avatar?.k !== "photo") return profile;
  if (profile.avatar.ph) await lib.redis("DEL", "photo:" + profile.avatar.ph);
  profile.avatar = { k: "ini", c: profile.avatar.c };
  await lib.redis("HSET", HASH, code, JSON.stringify(profile));
  return profile;
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
  const oldPhoto = old?.avatar?.k === "photo" ? old.avatar.ph : null;
  if (profile.avatar.k === "photo") {
    if (body.photo != null) {
      const bytes = cleanPhoto(body.photo);
      profile.avatar.ph = photoHash(bytes);
      await lib.redis("SET", "photo:" + profile.avatar.ph, bytes.toString("base64"));
    } else if (oldPhoto) {
      profile.avatar.ph = oldPhoto;
    } else {
      throw fail("Escolha uma foto.");
    }
  }
  if (oldPhoto && oldPhoto !== profile.avatar.ph) await lib.redis("DEL", "photo:" + oldPhoto);
  await lib.redis("HSET", HASH, invite.code, JSON.stringify(profile));
  await lib.redis("HSET", PID_HASH, profile.id, invite.code);
  return profile;
}

/** Invite code of a profile id (null when there is no such profile any more). */
async function codeOfPid(pid) {
  if (typeof pid !== "string" || !/^[A-Za-z0-9_-]{8,24}$/.test(pid)) return null;
  const code = await lib.redis("HGET", PID_HASH, pid);
  if (code) return code;
  // profiles made before the index existed
  const all = await allProfiles();
  const found = Object.keys(all).find((c) => all[c].id === pid);
  if (found) await lib.redis("HSET", PID_HASH, pid, found);
  return found || null;
}

async function deleteProfile(code) {
  const profile = await getProfile(code);
  if (profile?.avatar?.k === "photo" && profile.avatar.ph) await lib.redis("DEL", "photo:" + profile.avatar.ph);
  if (profile) await lib.redis("HDEL", PID_HASH, profile.id);
  await lib.redis("HDEL", HASH, code);
}

module.exports = { ICONS, COLORS, NAME_MAX, PID_HASH, stripInvisible, isReserved, cleanName, cleanAvatar, cleanKey, cleanPhoto, photoHash, getPhoto, dropPhoto, badgesOf, getProfile, allProfiles, codeOfPid, card, saveProfile, deleteProfile };
