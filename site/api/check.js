// POST /api/check { code, machine } → a fresh license, or why there is none.
// Called by the app on startup (and every few hours). Never registers a new PC: that is /api/activate.
"use strict";

const lib = require("./_lib");
const { MESSAGES } = require("./activate");

const UNREGISTERED = "Este computador não está mais ligado a este convite. Digite o código de novo.";

module.exports = lib.handler(["POST"], async (req, res) => {
  const body = await lib.readBody(req);
  const code = lib.normalizeCode(body.code);
  const machine = String(body.machine || "");
  if (!code || !lib.MACHINE_PATTERN.test(machine)) return lib.send(res, 400, { status: "invalid", message: MESSAGES.invalid });
  if (!(await lib.allow(req, "chk", 60))) return lib.send(res, 429, { status: "limit", message: MESSAGES.limit });

  const invite = await lib.getInvite(code);
  if (!invite) return lib.send(res, 404, { status: "unknown", message: MESSAGES.unknown });
  const status = lib.inviteStatus(invite);
  if (status !== "ok") return lib.send(res, 403, { status, message: MESSAGES[status] });
  const pc = invite.pcs.find((p) => p.id === machine);
  if (!pc) return lib.send(res, 403, { status: "unregistered", message: UNREGISTERED });

  const t = lib.now();
  if (t - pc.last > 3600) {
    pc.last = t;
    await lib.saveInvite(invite);
  }
  lib.send(res, 200, { status: "ok", ...lib.issueLicense(invite, machine, t) });
});
