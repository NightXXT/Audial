// POST /api/activate { code, machine } → { status: "ok", license, signature, info } or { status, message }
// Registers this PC in the invite (up to maxPcs) and starts the trial on the first activation.
"use strict";

const lib = require("./_lib");

const MESSAGES = {
  invalid: "Esse código não parece um convite do Audial. Ele tem o formato AUD-XXXX-XXXX.",
  unknown: "Convite não encontrado. Confira o código.",
  cancelled: "Este convite foi cancelado.",
  expired: "O período de teste deste convite terminou.",
  pcs: "Este convite já está em uso no número máximo de computadores.",
  limit: "Tentativas demais. Espere uma hora e tente de novo.",
};

module.exports = lib.handler(["POST"], async (req, res) => {
  if (!(await lib.allow(req, "act", 10))) return lib.send(res, 429, { status: "limit", message: MESSAGES.limit });
  const body = await lib.readBody(req);
  const code = lib.normalizeCode(body.code);
  const machine = String(body.machine || "");
  if (!code || !lib.MACHINE_PATTERN.test(machine)) return lib.send(res, 400, { status: "invalid", message: MESSAGES.invalid });

  const invite = await lib.getInvite(code);
  if (!invite) return lib.send(res, 404, { status: "unknown", message: MESSAGES.unknown });
  const status = lib.inviteStatus(invite);
  if (status !== "ok") return lib.send(res, 403, { status, message: MESSAGES[status] });

  const t = lib.now();
  let pc = invite.pcs.find((p) => p.id === machine);
  if (!pc) {
    if (invite.pcs.length >= invite.maxPcs) return lib.send(res, 403, { status: "pcs", message: MESSAGES.pcs });
    pc = { id: machine, first: t, last: t };
    invite.pcs.push(pc);
  }
  pc.last = t;
  if (invite.activatedAt == null) {
    invite.activatedAt = t;
    invite.expiresAt = invite.days == null ? null : t + invite.days * lib.DAY;
  }
  await lib.saveInvite(invite);
  lib.send(res, 200, { status: "ok", ...lib.issueLicense(invite, machine, t) });
});

module.exports.MESSAGES = MESSAGES;
