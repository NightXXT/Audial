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
  // the format is checked first: a malformed call costs no Redis command
  const body = await lib.readBody(req);
  const code = lib.normalizeCode(body.code);
  const machine = String(body.machine || "");
  if (!code || !lib.MACHINE_PATTERN.test(machine)) return lib.send(res, 400, { status: "invalid", message: MESSAGES.invalid });
  if (!(await lib.allow(req, "act", 10))) return lib.send(res, 429, { status: "limit", message: MESSAGES.limit });

  // read, check the PC limit and save under a lock: two PCs activating at the same moment must not both get in
  const [status, answer] = await lib.withLock("inv:" + code, async () => {
    const invite = await lib.getInvite(code);
    if (!invite) return [404, { status: "unknown", message: MESSAGES.unknown }];
    const state = lib.inviteStatus(invite);
    if (state !== "ok") return [403, { status: state, message: MESSAGES[state] }];

    const t = lib.now();
    let pc = invite.pcs.find((p) => p.id === machine);
    if (!pc) {
      if (invite.pcs.length >= invite.maxPcs) return [403, { status: "pcs", message: MESSAGES.pcs }];
      pc = { id: machine, first: t, last: t };
      invite.pcs.push(pc);
    }
    pc.last = t;
    if (invite.activatedAt == null) {
      invite.activatedAt = t;
      invite.expiresAt = invite.days == null ? null : t + invite.days * lib.DAY;
    }
    await lib.saveInvite(invite);
    return [200, { status: "ok", ...lib.issueLicense(invite, machine, t) }];
  });
  lib.send(res, status, answer);
});

module.exports.MESSAGES = MESSAGES;
