// POST /api/profile { license, signature, action, ... } — the beta profile of this invite (see _profile.js).
//   get                         -> { profile|null, card|null, signature|null, badges }
//   set { name, avatar, pk }    -> the same, after saving
//   delete                      -> { profile: null }
"use strict";

const lib = require("./_lib");
const P = require("./_profile");

function answer(invite, profile) {
  if (!profile) return { profile: null, card: null, signature: null, badges: P.badgesOf(invite) };
  return { profile: { name: profile.name, avatar: profile.avatar }, badges: P.badgesOf(invite), ...P.card(profile, invite) };
}

module.exports = lib.handler(["POST"], async (req, res) => {
  if (!(await lib.allow(req, "prof", 120))) return lib.send(res, 429, { status: "limit", message: "Muitos pedidos. Espere um pouco." });
  const body = await lib.readBody(req);
  const { invite } = await lib.authenticate(body.license, body.signature);
  switch (body.action) {
    case "get": {
      const profile = await P.getProfile(invite.code);
      // a new key from this PC (it lost its file, or it is a second PC of the invite) moves the card to it
      const pk = P.cleanKey(body.pk);
      if (profile && pk && pk !== profile.pk) {
        profile.pk = pk;
        await lib.redis("HSET", "profiles", invite.code, JSON.stringify(profile));
      }
      return lib.send(res, 200, { status: "ok", ...answer(invite, profile) });
    }
    case "set": {
      const key = "rl:prof:set:" + invite.code + ":" + Math.floor(lib.now() / 3600);
      const count = Number(await lib.redis("INCR", key));
      if (count === 1) await lib.redis("EXPIRE", key, 3600);
      if (count > 20) return lib.send(res, 429, { status: "limit", message: "Você mudou o perfil muitas vezes nesta hora. Tente mais tarde." });
      return lib.send(res, 200, { status: "ok", ...answer(invite, await P.saveProfile(invite, body)) });
    }
    case "delete":
      await P.deleteProfile(invite.code);
      return lib.send(res, 200, { status: "ok", ...answer(invite, null) });
    default:
      return lib.send(res, 400, { status: "input", message: "Ação desconhecida." });
  }
});
