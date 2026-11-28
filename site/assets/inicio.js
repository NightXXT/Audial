// Início do Audial (index.html). Só o necessário: as demos rodam enquanto estão na tela, a busca "digita",
// o relógio do Ouvir junto, o vídeo sob demanda, a janela do download e a contagem do lançamento.
(() => {
  const reduced = matchMedia("(prefers-reduced-motion: reduce)").matches;
  const $ = (s, el = document) => el.querySelector(s);

  // demos: a animação só corre com a demo na tela (as outras ficam paradas, sem custo)
  const io = new IntersectionObserver(es => es.forEach(e => e.target.classList.toggle("on", e.isIntersecting)), { threshold: 0.2 });
  document.querySelectorAll("[data-demo]").forEach(d => io.observe(d));
  if (reduced) return;

  // busca: digita a pesquisa no mesmo ciclo de 6 s das animações CSS da demo
  const typed = $("[data-typed]");
  const search = typed?.closest("[data-demo]");
  if (typed) {
    const text = typed.textContent;
    let start = performance.now(), shown = -1;
    const step = now => {
      requestAnimationFrame(step);
      if (!search.classList.contains("on")) { start = now - 4000; return; }
      const t = ((now - start) % 6000) / 6000;
      const n = Math.min(text.length, Math.floor(t / 0.2 * text.length));
      if (n !== shown) typed.textContent = text.slice(0, shown = n);
    };
    requestAnimationFrame(step);
  }

  // Ouvir junto: o relógio acompanha as barras (ciclo de 12 s, pausa no meio)
  const clock = $("[data-clock]");
  const room = clock?.closest("[data-demo]");
  if (clock) {
    let start = performance.now(), shown = "";
    const step = now => {
      requestAnimationFrame(step);
      if (!room.classList.contains("on")) return;
      const t = ((now - start) % 12000) / 12000;
      // mesmas paradas do @keyframes sync: 19% → 46% até 40% do ciclo, parado até 52%, depois 46% → 85%
      const p = t < .4 ? .19 + (.46 - .19) * t / .4 : t < .52 ? .46 : .46 + (.85 - .46) * (t - .52) / .48;
      const s = Math.round(p * 164);
      const label = t >= .4 && t < .52 ? "Pausado" : `${Math.floor(s / 60)}:${String(s % 60).padStart(2, "0")}`;
      if (label !== shown) clock.textContent = shown = label;
    };
    requestAnimationFrame(step);
  }
})();

// o vídeo só carrega quando alguém pede
(() => {
  const btn = document.getElementById("playVideo");
  if (!btn) return;
  btn.addEventListener("click", () => {
    const v = document.createElement("video");
    v.src = "/video/audial.mp4?v=5";
    v.poster = "/img/video-poster.webp?v=4";
    v.controls = true; v.autoplay = true; v.playsInline = true; v.preload = "auto";
    btn.replaceWith(v);
    v.play().catch(() => {});
  });
})();

// Baixar: o download começa (link da release no GitHub) e a janela explica o aviso do Windows
(() => {
  const dlg = document.getElementById("dl-help");
  if (!dlg || typeof dlg.showModal !== "function") return;
  document.addEventListener("click", e => {
    const a = e.target.closest && e.target.closest("a[data-baixar]");
    if (!a || dlg.contains(a)) return;
    if (document.documentElement.classList.contains("pre")) {
      e.preventDefault();
      document.getElementById("baixar")?.scrollIntoView({ behavior: "smooth" });
      return;
    }
    setTimeout(() => { if (!dlg.open) dlg.showModal(); }, 250);
  });
  dlg.querySelector(".dl-close").addEventListener("click", () => dlg.close());
  dlg.addEventListener("click", e => { if (e.target === dlg) dlg.close(); });
})();

// contagem até o lançamento (1º de dezembro de 2026, Brasília); no zero a página recarrega já lançada
(() => {
  const root = document.documentElement;
  if (!root.classList.contains("pre")) return;
  const launch = Date.parse("2026-12-01T00:00:00-03:00");
  const two = n => String(n).padStart(2, "0");
  const q = s => document.querySelectorAll(s);
  const tick = () => {
    const left = launch - Date.now();
    if (left <= 0) { location.reload(); return; }
    const s = Math.floor(left / 1000), d = Math.floor(s / 86400), h = Math.floor(s / 3600) % 24, m = Math.floor(s / 60) % 60;
    q("[data-cd-d]").forEach(el => (el.textContent = two(d)));
    q("[data-cd-h]").forEach(el => (el.textContent = two(h)));
    q("[data-cd-m]").forEach(el => (el.textContent = two(m)));
    q("[data-cd-s]").forEach(el => (el.textContent = two(s % 60)));
    q("[data-cd-short]").forEach(el => (el.textContent = d > 0 ? `${d} ${d === 1 ? "dia" : "dias"} e ${h}h` : `${h}h ${two(m)}min`));
  };
  tick();
  setInterval(tick, 1000);
})();

// Visualizador atrás do título: as mesmas barras do site atual, a 30 quadros por segundo (15 no celular),
// e parado quando o topo sai da tela.
(() => {
  const canvas = document.getElementById("viz");
  if (!canvas || matchMedia("(prefers-reduced-motion: reduce)").matches) return;
  const ctx = canvas.getContext("2d");
  const lite = matchMedia("(max-width: 760px), (hover: none)").matches;
  let w = 0, h = 0, visible = true, lastDraw = 0, introStart = 0;
  const resize = () => {
    const dpr = lite ? 1 : Math.min(devicePixelRatio || 1, 2);
    w = canvas.clientWidth; h = canvas.clientHeight;
    canvas.width = w * dpr; canvas.height = h * dpr;
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  };
  resize();
  addEventListener("resize", resize);
  new IntersectionObserver(([e]) => { visible = e.isIntersecting; }).observe(canvas);
  const draw = t => {
    requestAnimationFrame(draw);
    if (!visible || t - lastDraw < (lite ? 66 : 33)) return;
    lastDraw = t;
    ctx.clearRect(0, 0, w, h);
    // barras de equalizador: largas, com vão entre elas, e cada uma num ritmo próprio (vizinhas não andam juntas,
    // senão o contorno fica liso e parece uma nuvem)
    if (!introStart) { if (document.documentElement.classList.contains("boot")) return; introStart = t; }
    const intro = Math.min(1, (t - introStart) / 1500);
    const step = 14, bw = 6, bars = Math.floor(w / step), s = t / 1000, off = (w - bars * step + step - bw) / 2;
    const grad = ctx.createLinearGradient(0, 0, w, 0);
    grad.addColorStop(0, "#F43F5E"); grad.addColorStop(0.5, "#F751A1"); grad.addColorStop(1, "#8B5CF6");
    ctx.fillStyle = grad;
    const beat = Math.pow(Math.max(0, Math.sin(s * 3.8)), 10);
    for (let i = 0; i < bars; i++) {
      const x = i / bars;
      const r1 = Math.sin(i * 12.9898) * 43758.5453, r = r1 - Math.floor(r1); // ritmo próprio da barra
      const wave = Math.abs(Math.sin(s * (1.6 + r * 2.4) + r * 6.3));
      const env = 0.35 + 0.65 * Math.exp(-Math.pow((x - 0.5) * 2.4, 2)); // mais alto no meio
      const v = Math.min(1, (0.12 + 0.6 * wave * (0.55 + 0.45 * r) + 0.3 * beat * env) * env);
      // entrada: as barras crescem do meio para as pontas no primeiro segundo e meio
      const grow = Math.min(1, Math.max(0, (intro * 1.5 - Math.abs(x - 0.5) * 1.2) / 0.5));
      if (grow <= 0) continue;
      const bh = Math.max(bw, v * h * 0.9) * (1 - Math.pow(1 - grow, 3));
      ctx.beginPath();
      ctx.roundRect(off + i * step, (h - bh) / 2, bw, bh, bw / 2);
      ctx.fill();
    }
  };
  requestAnimationFrame(draw);
})();

// Natal: liga sozinho de 12 de novembro a 6 de janeiro (Dia de Reis)
(() => {
  const d = new Date(), m = d.getMonth(), day = d.getDate();
  if (!((m === 10 && day >= 12) || m === 11 || (m === 0 && day <= 6))) return;
  const pill = document.querySelector(".nav-pill");
  if (pill) {
    const lights = document.createElement("div");
    lights.className = "xmas-lights";
    lights.setAttribute("aria-hidden", "true");
    for (let i = 0; i < 16; i++) lights.appendChild(document.createElement("i"));
    pill.appendChild(lights);
  }
  if (matchMedia("(prefers-reduced-motion: reduce)").matches) return;
  const snow = document.createElement("div");
  snow.className = "xmas-snow";
  snow.setAttribute("aria-hidden", "true");
  const count = matchMedia("(max-width: 760px), (hover: none)").matches ? 12 : 26;
  for (let i = 0; i < count; i++) {
    const f = document.createElement("i");
    const size = 2 + Math.random() * 4;
    f.style.cssText = `left:${Math.random() * 100}%;width:${size}px;height:${size}px;opacity:${0.35 + Math.random() * 0.5};` +
      `animation-duration:${9 + Math.random() * 12}s,${3 + Math.random() * 3}s;animation-delay:-${Math.random() * 20}s,-${Math.random() * 3}s`;
    snow.appendChild(f);
  }
  document.body.appendChild(snow);
})();
