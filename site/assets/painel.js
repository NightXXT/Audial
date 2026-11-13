// Admin page for the beta invites (/painel). Talks only to /api/admin (connect-src 'self').
// The password lives in sessionStorage while the tab is open; it is never put in the address or in the page.
(() => {
  const $ = (id) => document.getElementById(id);
  const KEY = "audial-admin";
  let password = "";
  try { password = sessionStorage.getItem(KEY) || ""; } catch {}

  const fmtDate = (t) => (t ? new Date(t * 1000).toLocaleString("pt-BR", { dateStyle: "short", timeStyle: "short" }) : "—");
  const STATUS = { ok: "Ativo", expired: "Expirado", cancelled: "Cancelado" };

  function setMsg(el, text, kind) {
    el.textContent = text || "";
    el.className = "msg" + (kind ? " " + kind : "");
  }

  async function call(action, data) {
    const res = await fetch("/api/admin", {
      method: "POST",
      headers: { "Content-Type": "application/json", Authorization: "Bearer " + password },
      body: JSON.stringify({ action, ...data }),
    });
    const body = await res.json().catch(() => ({ status: "error", message: "Resposta inválida do servidor." }));
    if (res.status === 401) logout("Senha incorreta.");
    if (!res.ok || body.status !== "ok") throw new Error(body.message || "Erro " + res.status);
    return body;
  }

  function logout(message) {
    password = "";
    try { sessionStorage.removeItem(KEY); } catch {}
    $("app").classList.add("hidden");
    $("login").classList.remove("hidden");
    setMsg($("login-msg"), message || "", message ? "err" : "");
  }

  function el(tag, attrs, ...children) {
    const node = document.createElement(tag);
    for (const [k, v] of Object.entries(attrs || {})) {
      if (k === "class") node.className = v;
      else if (k.startsWith("data-")) node.setAttribute(k, v);
      else node[k] = v;
    }
    for (const c of children) if (c != null) node.append(c);
    return node;
  }

  const button = (label, act, code, extra) =>
    el("button", { class: "b s" + (extra ? " " + extra : ""), type: "button", "data-act": act, "data-code": code }, label);

  function render(data) {
    const list = $("list");
    list.replaceChildren();
    const invites = data.invites;
    const active = invites.filter((i) => i.status === "ok");
    const pcs = invites.reduce((n, i) => n + i.pcs.length, 0);
    $("stats").replaceChildren(
      el("span", {}, el("b", {}, String(invites.length)), " convites"),
      el("span", {}, el("b", {}, String(active.length)), " ativos"),
      el("span", {}, el("b", {}, String(pcs)), " PCs ativados"),
      el("span", {}, el("b", {}, String(invites.filter((i) => i.activatedAt == null).length)), " ainda não usados"),
    );
    if (!invites.length) list.append(el("p", { class: "meta" }, "Nenhum convite ainda."));
    for (const inv of invites) {
      const term = inv.days == null ? "sem limite" : inv.days + " dias";
      let left;
      if (inv.activatedAt == null) left = "ainda não ativado (o prazo começa na ativação)";
      else if (inv.expiresAt == null) left = "sem prazo";
      else left = inv.daysLeft + (inv.daysLeft === 1 ? " dia restante" : " dias restantes") + " · termina " + fmtDate(inv.expiresAt);

      const pcsBox = el("div", { class: "pcs" });
      for (const pc of inv.pcs) {
        pcsBox.append(
          el("div", {},
            el("span", {}, pc.id.slice(0, 10) + "…"),
            " · ativado " + fmtDate(pc.first) + " · visto " + fmtDate(pc.last) + " ",
            el("button", { class: "b s d", type: "button", "data-act": "removePc", "data-code": inv.code, "data-machine": pc.id }, "Desligar PC"),
          ),
        );
      }

      list.append(
        el("div", { class: "inv" },
          el("div", { class: "top" },
            el("span", { class: "code" }, inv.code),
            el("span", { class: "pill " + inv.status }, STATUS[inv.status] || inv.status),
            el("span", { class: "pill" + (inv.badge === "apoiador" ? " apoiador" : "") }, inv.badge === "apoiador" ? "Apoiador" : "Beta"),
            inv.creator ? el("span", { class: "pill creator" }, "Criador") : null,
            el("span", { class: "meta" }, inv.note || "(sem nota)"),
          ),
          inv.profile ? el("div", { class: "meta" }, "Perfil: ", el("b", {}, inv.profile.name)) : null,
          el("div", { class: "meta" }, "Prazo: " + term + " · " + left + " · PCs: " + inv.pcs.length + "/" + inv.maxPcs + " · criado " + fmtDate(inv.createdAt) + (inv.discord ? " · ligado ao Discord" : "")),
          inv.pcs.length ? pcsBox : null,
          el("div", { class: "actions" },
            button("Copiar código", "copy", inv.code),
            button("Imagem do convite", "ticket", inv.code),
            inv.days != null ? button("+7 dias", "plus7", inv.code) : null,
            inv.days != null ? button("+30 dias", "plus30", inv.code) : null,
            inv.days != null ? button("−7 dias", "minus7", inv.code) : null,
            button("Mudar prazo", "setDays", inv.code),
            button("Editar", "edit", inv.code),
            button(inv.creator ? "Tirar Criador" : "Tornar Criador", inv.creator ? "uncreator" : "creator", inv.code),
            button(inv.badge === "apoiador" ? "Tirar Apoiador" : "Marcar Apoiador", inv.badge === "apoiador" ? "unsupporter" : "supporter", inv.code),
            inv.profile ? button("Apagar perfil", "deleteProfile", inv.code, "d") : null,
            inv.cancelled ? button("Reativar", "restore", inv.code) : button("Cancelar", "cancel", inv.code, "d"),
            button("Apagar", "delete", inv.code, "d"),
          ),
        ),
      );
    }
    list._invites = invites;
  }

  async function load() {
    setMsg($("list-msg"), "Carregando…");
    try {
      render(await call("list"));
      setMsg($("list-msg"), "");
    } catch (e) {
      setMsg($("list-msg"), e.message, "err");
    }
  }

  async function enter() {
    try {
      const data = await call("list");
      try { sessionStorage.setItem(KEY, password); } catch {}
      $("login").classList.add("hidden");
      $("app").classList.remove("hidden");
      render(data);
    } catch (e) {
      if (password) logout(e.message);
    }
  }

  $("login-form").addEventListener("submit", (e) => {
    e.preventDefault();
    password = $("password").value;
    $("password").value = "";
    setMsg($("login-msg"), "Conferindo…");
    enter();
  });

  $("c-nolimit").addEventListener("change", () => { $("c-days").disabled = $("c-nolimit").checked; });

  $("create-form").addEventListener("submit", async (e) => {
    e.preventDefault();
    const msg = $("create-msg");
    try {
      const { invite } = await call("create", {
        days: $("c-nolimit").checked ? null : Number($("c-days").value),
        maxPcs: Number($("c-pcs").value),
        badge: $("c-badge").value,
        note: $("c-note").value,
      });
      $("c-note").value = "";
      msg.replaceChildren("Convite criado: ", el("span", { class: "new-code" }, invite.code));
      msg.className = "msg ok";
      try { await navigator.clipboard.writeText(invite.code); msg.append(" (copiado)"); } catch {}
      load();
    } catch (err) {
      setMsg(msg, err.message, "err");
    }
  });

  $("discord-register").addEventListener("click", async () => {
    const msg = $("discord-msg");
    setMsg(msg, "Mandando os comandos para o Discord…");
    try {
      const { commands } = await call("discordRegister");
      setMsg(msg, "Pronto: " + commands.join(", ") + " registrados. No Discord, eles aparecem ao digitar / (pode levar alguns minutos).", "ok");
    } catch (e) {
      setMsg(msg, e.message, "err");
    }
  });

  $("refresh").addEventListener("click", load);
  $("logout").addEventListener("click", () => logout());

  $("list").addEventListener("click", async (e) => {
    const b = e.target.closest("button[data-act]");
    if (!b) return;
    const code = b.getAttribute("data-code");
    const inv = ($("list")._invites || []).find((i) => i.code === code);
    const msg = $("list-msg");
    try {
      switch (b.getAttribute("data-act")) {
        case "ticket": {
          const { url } = await call("ticketUrl", { code });
          msg.replaceChildren("Imagem do convite " + code + ": ", el("a", { href: url, target: "_blank", rel: "noopener", style: "color:#FB7185;text-decoration:underline" }, "abrir PNG"), " (clique com o botão direito para salvar).");
          msg.className = "msg ok";
          return;
        }
        case "copy":
          await navigator.clipboard.writeText(code);
          setMsg(msg, code + " copiado.", "ok");
          return;
        case "plus7": await call("extend", { code, days: 7 }); break;
        case "plus30": await call("extend", { code, days: 30 }); break;
        case "minus7": await call("extend", { code, days: -7 }); break;
        case "setDays": {
          const text = prompt("Prazo em dias a partir da ativação (vazio = sem limite):", inv && inv.days != null ? String(inv.days) : "");
          if (text === null) return;
          await call("setDays", { code, days: text.trim() === "" ? null : Number(text) });
          break;
        }
        case "edit": {
          const note = prompt("Nota (só você vê):", inv ? inv.note : "");
          if (note === null) return;
          const pcs = prompt("Máximo de PCs:", inv ? String(inv.maxPcs) : "1");
          if (pcs === null) return;
          const badge = confirm("Selo Apoiador? (OK = Apoiador, Cancelar = Beta)") ? "apoiador" : "beta";
          await call("update", { code, note, maxPcs: Number(pcs), badge });
          break;
        }
        case "cancel":
          if (!confirm("Cancelar " + code + "? O app de quem usa volta para a tela de convite na próxima conferência.")) return;
          await call("cancel", { code });
          break;
        case "restore": await call("restore", { code }); break;
        case "creator":
          if (!confirm("Marcar " + code + " como Criador? Quem usa este convite pode postar avisos, criar enquetes, mudar o status das ideias e apagar posts na Comunidade beta, e o perfil dele mostra o selo de Criador com o ícone do Audial para todos.")) return;
          await call("update", { code, creator: true });
          break;
        case "uncreator": await call("update", { code, creator: false }); break;
        case "supporter": await call("update", { code, badge: "apoiador" }); break;
        case "unsupporter": await call("update", { code, badge: "beta" }); break;
        case "deleteProfile":
          if (!confirm("Apagar o perfil (nome e avatar) de " + code + "? A pessoa pode criar outro.")) return;
          await call("deleteProfile", { code });
          break;
        case "removePc":
          if (!confirm("Desligar este PC do convite? Ele vai precisar digitar o código de novo.")) return;
          await call("removePc", { code, machine: b.getAttribute("data-machine") });
          break;
        case "delete":
          if (!confirm("Apagar " + code + " de vez? Isso não pode ser desfeito.")) return;
          await call("delete", { code });
          break;
        default:
          return;
      }
      setMsg(msg, "Feito.", "ok");
      const data = await call("list");
      render(data);
    } catch (err) {
      setMsg(msg, err.message, "err");
    }
  });

  if (password) enter();
})();
