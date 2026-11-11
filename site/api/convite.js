// GET /api/convite?c=AUD-XXXX-XXXX&d=30&s=<signature> — the invite flyer as a PNG (1080x1350), the same design as the
// flyer made by hand (Desktop\Audial\convite). The Discord bot puts this link in the winner's DM, and the admin page
// opens it for any invite. The link is signed (HMAC with a key derived from AUDIAL_ADMIN_PASSWORD), so nobody can
// make an "official" invite image with a code of their own. Fonts: Inter (OFL, THIRD-PARTY/Inter), in api/_assets.
"use strict";

const crypto = require("crypto");
const fs = require("fs");
const path = require("path");
const lib = require("./_lib");

const ASSETS = path.join(__dirname, "_assets");
const SITE = (process.env.SITE_URL || "https://audial.com.br").replace(/\/+$/, "");

function signingSecret() {
  const password = process.env.AUDIAL_ADMIN_PASSWORD;
  if (!password) throw new lib.ServerError("setup", "A senha do painel ainda não foi configurada.");
  return crypto.createHash("sha256").update("audial-convite-imagem|" + password).digest();
}

function sign(code, days) {
  return crypto.createHmac("sha256", signingSecret()).update(code + "|" + (days || 0)).digest("base64url").slice(0, 22);
}

/** Signed address of the flyer of this invite (days null or 0 = no limit). */
function ticketUrl(code, days) {
  const d = days || 0;
  return `${SITE}/api/convite?c=${encodeURIComponent(code)}&d=${d}&s=${sign(code, d)}`;
}

// ---------- layout (satori: every box with more than one child needs display:flex) ----------

const h = (type, style, ...children) => ({ type, props: { style: { display: "flex", ...style }, children: children.length === 1 ? children[0] : children } });
const t = (text, style) => ({ type: "div", props: { style: { display: "flex", ...style }, children: text } });

function step(n, title, text) {
  return h("div", { flexDirection: "column", flex: 1, background: "#12131A", border: "1px solid rgba(255,255,255,0.09)", borderRadius: 20, padding: "22px 22px 24px" },
    t(n, { fontSize: 15, fontWeight: 800, letterSpacing: 1.5, color: "#FB7185" }),
    t(title, { fontSize: 23, fontWeight: 700, marginTop: 8, letterSpacing: -0.3, color: "#F3F4F6" }),
    t(text, { fontSize: 18.5, lineHeight: 1.4, color: "#A1A7B3", marginTop: 6 }));
}

function layout(code, days, logo) {
  const meta = days > 0 ? `Vale por ${days} dias a partir da ativação, em 1 computador.` : "Vale para 1 computador, sem prazo.";
  return h("div", { width: 1080, height: 1350, flexDirection: "column", position: "relative", background: "#07080B", color: "#F3F4F6", fontFamily: "Inter", padding: "64px 76px 0" },
    // glows
    h("div", { position: "absolute", width: 1100, height: 1100, right: -480, top: -520, backgroundImage: "radial-gradient(circle, rgba(244,63,94,0.30) 0%, rgba(139,92,246,0.12) 38%, rgba(139,92,246,0) 70%)" }),
    h("div", { position: "absolute", width: 1000, height: 1000, left: -420, bottom: -480, backgroundImage: "radial-gradient(circle, rgba(139,92,246,0.22) 0%, rgba(244,63,94,0.06) 40%, rgba(244,63,94,0) 70%)" }),
    // top
    h("div", { alignItems: "center", gap: 14 },
      { type: "img", props: { src: logo, width: 52, height: 52, style: { width: 52, height: 52 } } },
      t("Audial", { fontSize: 30, fontWeight: 800, letterSpacing: -0.6 }),
      h("div", { marginLeft: "auto", fontSize: 15, fontWeight: 700, letterSpacing: 2, color: "#FB7185", border: "1.5px solid rgba(251,113,133,0.45)", borderRadius: 999, padding: "9px 18px" }, "PROGRAMA BETA")),
    t("CONVITE", { marginTop: 92, fontSize: 19, fontWeight: 700, letterSpacing: 3.2, color: "#FB7185" }),
    h("div", { flexDirection: "column", marginTop: 18 },
      t("Você foi", { fontSize: 104, lineHeight: 0.98, letterSpacing: -4.5, fontWeight: 800 }),
      t("convidado.", { alignSelf: "flex-start", fontSize: 104, lineHeight: 1.05, letterSpacing: -4.5, fontWeight: 800, backgroundImage: "linear-gradient(90deg, #FF8FA3, #F751A1 45%, #B794F6)", backgroundClip: "text", color: "transparent", paddingBottom: 6 })),
    h("div", { flexWrap: "wrap", fontSize: 29, lineHeight: 1.42, color: "#A1A7B3", marginTop: 30, maxWidth: 880 },
      "Você é uma das poucas pessoas chamadas para testar o Audial, o player de música feito de verdade para Windows, antes do lançamento em 2027."),
    // ticket with a gradient border
    h("div", { marginTop: 52, borderRadius: 28, padding: 2, backgroundImage: "linear-gradient(135deg, rgba(244,63,94,0.95), rgba(139,92,246,0.9))" },
      h("div", { flexDirection: "column", flex: 1, borderRadius: 26, backgroundImage: "linear-gradient(160deg, #1B1322, #12131A 70%)", padding: "34px 40px 36px" },
        t("SEU CÓDIGO DE CONVITE", { fontSize: 17, fontWeight: 700, letterSpacing: 2.4, color: "#A1A7B3" }),
        t(code, { fontSize: 64, fontWeight: 800, letterSpacing: 3, marginTop: 10 }),
        t(meta, { fontSize: 21, color: "#A1A7B3", marginTop: 12 }))),
    h("div", { gap: 18, marginTop: 30 },
      step("01", "Instale", "Abra o instalador que veio junto com este convite."),
      step("02", "Digite o código", "O Audial pede o convite na primeira vez que abre."),
      step("03", "Conte o que achou", "Sua opinião ajuda a deixar o app pronto.")),
    h("div", { position: "absolute", left: 76, right: 76, bottom: 54, alignItems: "flex-end", justifyContent: "space-between", gap: 30 },
      t("audial.com.br", { fontSize: 24, fontWeight: 700 }),
      t("O convite é pessoal: não repasse o código. O Audial é um projeto independente e não é afiliado ao YouTube nem ao Google.", { fontSize: 15, lineHeight: 1.5, color: "#7E8594", maxWidth: 600, textAlign: "right" })));
}

let cache;
function assets() {
  if (!cache) {
    const font = (w) => fs.readFileSync(path.join(ASSETS, `inter-latin-${w}-normal.woff`));
    cache = {
      fonts: [
        { name: "Inter", data: font(400), weight: 400, style: "normal" },
        { name: "Inter", data: font(700), weight: 700, style: "normal" },
        { name: "Inter", data: font(800), weight: 800, style: "normal" },
      ],
      logo: "data:image/png;base64," + fs.readFileSync(path.join(ASSETS, "logo.png")).toString("base64"),
    };
  }
  return cache;
}

async function render(code, days) {
  const { ImageResponse } = await import("@vercel/og");
  const a = assets();
  const image = new ImageResponse(layout(code, days, a.logo), { width: 1080, height: 1350, fonts: a.fonts });
  return Buffer.from(await image.arrayBuffer());
}

module.exports = lib.handler(["GET"], async (req, res) => {
  const url = new URL(req.url, "http://x");
  const code = lib.normalizeCode(url.searchParams.get("c"));
  const days = Math.max(0, Math.min(3650, Math.round(Number(url.searchParams.get("d")) || 0)));
  const given = String(url.searchParams.get("s") || "");
  const expected = code ? sign(code, days) : "";
  if (!code || given.length !== expected.length || !crypto.timingSafeEqual(Buffer.from(given), Buffer.from(expected))) {
    return lib.send(res, 403, { status: "signature", message: "Link de convite inválido." });
  }
  const png = await render(code, days);
  res.statusCode = 200;
  res.setHeader("Content-Type", "image/png");
  res.setHeader("Cache-Control", "public, max-age=86400, immutable");
  res.setHeader("X-Robots-Tag", "noindex");
  res.end(png);
});

module.exports.ticketUrl = ticketUrl;
module.exports.render = render;
