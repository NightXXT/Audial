(() => {
  const reduced = matchMedia("(prefers-reduced-motion: reduce)").matches;
  const finePointer = matchMedia("(pointer: fine)").matches;
  const $ = (s, el = document) => el.querySelector(s);
  const $$ = (s, el = document) => [...el.querySelectorAll(s)];
  // phones, tablets and small windows get a lighter version of the effects
  const lite = matchMedia("(max-width: 760px), (hover: none)").matches;

  // Nav gets solid once the page scrolls.
  const nav = $("#nav");
  const onScrollNav = () => nav.classList.toggle("scrolled", scrollY > 12);
  addEventListener("scroll", onScrollNav, { passive: true });
  onScrollNav();

  // Reveal on scroll.
  const io = new IntersectionObserver(entries => {
    for (const e of entries) {
      if (e.isIntersecting) { e.target.classList.add("in"); io.unobserve(e.target); }
    }
  }, { threshold: 0.15, rootMargin: "0px 0px -60px 0px" });
  $$(".reveal").forEach(el => io.observe(el));
  $$(".bento .card").forEach((el, i) => el.style.setProperty("--rd", (i % 3) * 90 + "ms"));

  if (reduced) return;

  // Cursor glow and card spotlight.
  if (finePointer) {
    const glow = $(".cursor-glow");
    addEventListener("pointermove", e => {
      glow.style.setProperty("--mx", e.clientX + "px");
      glow.style.setProperty("--my", e.clientY + "px");
    }, { passive: true });
    $$(".card.spot").forEach(card => {
      card.addEventListener("pointermove", e => {
        const r = card.getBoundingClientRect();
        card.style.setProperty("--sx", e.clientX - r.left + "px");
        card.style.setProperty("--sy", e.clientY - r.top + "px");
      });
    });

    // Magnetic buttons.
    $$(".magnetic").forEach(btn => {
      btn.addEventListener("pointermove", e => {
        const r = btn.getBoundingClientRect();
        const x = (e.clientX - r.left - r.width / 2) * 0.25;
        const y = (e.clientY - r.top - r.height / 2) * 0.35;
        btn.style.transform = `translate(${x}px, ${y}px)`;
      });
      btn.addEventListener("pointerleave", () => { btn.style.transform = ""; });
    });
  }

  // Hero stage: flattens as it scrolls in, tilts toward the mouse.
  const stage = $("#stage");
  const floats = $$(".float", stage);
  let tiltX = 0, tiltY = 0;
  const updateStage = () => {
    const r = stage.getBoundingClientRect();
    const p = Math.min(1, Math.max(0, 1 - (r.top - innerHeight * 0.18) / (innerHeight * 0.7)));
    stage.style.setProperty("--tilt-x", (18 * (1 - p) + tiltX).toFixed(2) + "deg");
    stage.style.setProperty("--tilt-y", tiltY.toFixed(2) + "deg");
    stage.style.setProperty("--stage-scale", (0.92 + 0.08 * p).toFixed(3));
    stage.style.setProperty("--stage-y", (40 * (1 - p)).toFixed(1) + "px");
  };
  addEventListener("scroll", () => requestAnimationFrame(updateStage), { passive: true });
  if (finePointer) {
    $(".hero").addEventListener("pointermove", e => {
      const nx = e.clientX / innerWidth - 0.5;
      const ny = e.clientY / innerHeight - 0.5;
      tiltY = nx * 6;
      tiltX = -ny * 4;
      floats.forEach(f => {
        const d = +f.dataset.depth || 30;
        f.style.setProperty("--px", (nx * d).toFixed(1) + "px");
        f.style.setProperty("--py", (ny * d).toFixed(1) + "px");
      });
      requestAnimationFrame(updateStage);
    });
  }
  updateStage();

  // Fake audio visualizer behind the hero.
  const canvas = $("#visualizer");
  const ctx = canvas.getContext("2d");
  let w = 0, h = 0, dpr = 1, visible = true, frame = 0;
  const resize = () => {
    dpr = lite ? 1 : Math.min(devicePixelRatio || 1, 2);
    w = canvas.clientWidth; h = canvas.clientHeight;
    canvas.width = w * dpr; canvas.height = h * dpr;
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  };
  resize();
  addEventListener("resize", resize);
  new IntersectionObserver(([e]) => { visible = e.isIntersecting; }).observe(canvas);
  const draw = t => {
    requestAnimationFrame(draw);
    if (!visible) return;
    if (lite && frame++ % 2) return; // half the frames on lighter devices
    ctx.clearRect(0, 0, w, h);
    const bars = Math.floor(w / 14);
    const grad = ctx.createLinearGradient(0, 0, w, 0);
    grad.addColorStop(0, "#F43F5E"); grad.addColorStop(0.5, "#F751A1"); grad.addColorStop(1, "#8B5CF6");
    ctx.fillStyle = grad;
    const s = t / 1000;
    for (let i = 0; i < bars; i++) {
      const x = i / bars;
      const beat = Math.pow(Math.max(0, Math.sin(s * 3.6)), 8) * 0.35;
      const v = 0.18 + 0.32 * Math.abs(Math.sin(x * 9 + s * 1.7)) * Math.abs(Math.cos(x * 4.3 - s * 1.1)) + 0.25 * Math.abs(Math.sin(x * 23 + s * 3.1)) * (0.6 + beat) + beat * Math.exp(-Math.pow((x - 0.5) * 3, 2));
      const bh = v * h * 0.8;
      const bx = i * 14 + 4;
      ctx.globalAlpha = 0.35 + 0.65 * Math.exp(-Math.pow((x - 0.5) * 2.2, 2));
      ctx.beginPath();
      ctx.roundRect(bx, (h - bh) / 2, 6, bh, 3);
      ctx.fill();
    }
  };
  requestAnimationFrame(draw);

  // Listen-together demo: one clock drives every "PC", with a shared pause now and then.
  const network = $("#network");
  const clock = $("#syncClock");
  const bars = $$(".sync, .sync-bars span", document);
  const duration = 164; // 2:44
  let pos = 31, last = performance.now(), pausedUntil = 0, nextPause = performance.now() + 7000;
  const tick = now => {
    requestAnimationFrame(tick);
    const dt = (now - last) / 1000; last = now;
    if (now > nextPause) { pausedUntil = now + 1600; nextPause = now + 9000; }
    const paused = now < pausedUntil;
    network.classList.toggle("paused", paused);
    if (!paused) pos = (pos + dt) % duration;
    const pct = (pos / duration * 100).toFixed(2) + "%";
    bars.forEach(b => b.style.setProperty("--sync", pct));
    const m = Math.floor(pos / 60), sec = Math.floor(pos % 60);
    clock.textContent = paused ? "Pausado" : `${m}:${String(sec).padStart(2, "0")}`;
  };
  requestAnimationFrame(tick);

  // Screens showcase: cycles by itself, click to jump.
  const items = $$(".sc-item");
  const shots = $$(".sc-shot");
  const progress = $("#scProgress");
  let current = 0, started = performance.now();
  const period = 4500;
  const show = i => {
    current = i; started = performance.now();
    items.forEach((el, k) => el.classList.toggle("active", k === i));
    shots.forEach((el, k) => el.classList.toggle("active", k === i));
  };
  items.forEach((el, i) => el.addEventListener("click", () => show(i)));
  let showcaseVisible = false;
  new IntersectionObserver(([e]) => { showcaseVisible = e.isIntersecting; if (showcaseVisible) started = performance.now(); }, { threshold: 0.3 }).observe($("#showcase"));
  const cycle = now => {
    requestAnimationFrame(cycle);
    if (!showcaseVisible) return;
    const p = (now - started) / period;
    progress.style.width = Math.min(100, p * 100) + "%";
    if (p >= 1) show((current + 1) % shots.length);
  };
  requestAnimationFrame(cycle);
})();

// The promo video loads only when someone asks for it, so the page stays light.
(() => {
  const btn = document.getElementById("playVideo");
  if (!btn) return;
  btn.addEventListener("click", () => {
    const v = document.createElement("video");
    v.src = "/video/audial.mp4?v=4";
    v.poster = "/img/video-poster.webp?v=2";
    v.controls = true;
    v.autoplay = true;
    v.playsInline = true;
    v.preload = "auto";
    btn.replaceWith(v);
    v.play().catch(() => {});
  });
})();

// Project timeline ("Progresso"): the line fills with the gradient as the page scrolls, down to "now", and each
// milestone it passes lights up. With reduced motion it is simply filled down to "now".
(() => {
  const tl = document.getElementById("timeline");
  if (!tl) return;
  const rail = tl.querySelector(".tl-rail");
  const items = [...tl.querySelectorAll(".tl-item")];
  const dots = items.map(it => it.querySelector(".tl-dot"));
  const nowIndex = Math.max(0, items.findIndex(it => it.classList.contains("now")));
  const reduced = matchMedia("(prefers-reduced-motion: reduce)").matches;
  let centers = [];
  // the line runs exactly from the first dot to the last one, whatever the layout (two sides or phone)
  const measure = () => {
    const box = tl.getBoundingClientRect();
    centers = dots.map(d => { const r = d.getBoundingClientRect(); return r.top + r.height / 2 - box.top; });
    rail.style.top = centers[0] + "px";
    rail.style.bottom = "auto";
    rail.style.height = centers[centers.length - 1] - centers[0] + "px";
  };
  const update = () => {
    const top = tl.getBoundingClientRect().top + centers[0];
    const limit = centers[nowIndex] - centers[0];
    const reach = reduced ? limit : Math.min(limit, Math.max(0, innerHeight * 0.62 - top));
    rail.style.setProperty("--fill", reach.toFixed(1) + "px");
    items.forEach((it, i) => it.classList.toggle("lit", reach > 0 && centers[i] - centers[0] <= reach + 0.5));
  };
  const refresh = () => { measure(); update(); };
  addEventListener("scroll", () => requestAnimationFrame(update), { passive: true });
  addEventListener("resize", refresh);
  addEventListener("load", refresh);
  if (document.fonts) document.fonts.ready.then(refresh);
  new ResizeObserver(refresh).observe(tl);
  refresh();
})();

// Christmas theme: on by itself from November 12 to January 6 (Dia de Reis), off the rest of the year.
// Light snow, a Santa hat on the logo and a string of lights under the nav, in the Audial colors.
(() => {
  const d = new Date(), m = d.getMonth(), day = d.getDate();
  if (!((m === 10 && day >= 12) || m === 11 || (m === 0 && day <= 6))) return;
  document.documentElement.classList.add("xmas");
  const nav = document.getElementById("nav");
  if (nav) {
    const lights = document.createElement("div");
    lights.className = "xmas-lights";
    lights.setAttribute("aria-hidden", "true");
    for (let i = 0; i < 40; i++) lights.appendChild(document.createElement("i"));
    nav.appendChild(lights);
  }
  if (matchMedia("(prefers-reduced-motion: reduce)").matches) return;
  const snow = document.createElement("div");
  snow.className = "xmas-snow";
  snow.setAttribute("aria-hidden", "true");
  const count = matchMedia("(max-width: 760px)").matches ? 22 : 45;
  for (let i = 0; i < count; i++) {
    const f = document.createElement("i");
    const size = 2 + Math.random() * 4;
    f.style.cssText = `left:${Math.random() * 100}%;width:${size}px;height:${size}px;opacity:${0.35 + Math.random() * 0.5};` +
      `animation-duration:${9 + Math.random() * 12}s,${3 + Math.random() * 3}s;animation-delay:-${Math.random() * 20}s,-${Math.random() * 3}s`;
    snow.appendChild(f);
  }
  document.body.appendChild(snow);
})();
