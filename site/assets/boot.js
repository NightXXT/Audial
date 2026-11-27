// Launch: until 1 December 2026 (Brasília) the page is in "pre" mode, with the countdown in place of the download
// buttons (CSS: html.pre .post / html:not(.pre) .pre-only). Set here, before the first paint, so nothing flashes.
// ?lancado shows the launched page early (to check it).
(() => {
  const launch = Date.parse("2026-12-01T00:00:00-03:00");
  if (Date.now() < launch && !/[?&]lancado\b/.test(location.search)) document.documentElement.classList.add("pre");
})();

// Loading screen of the site: the "A" lights up alone in the middle, then slides left pulling the name out from behind
// it ("Audial", as in the top bar), a thin line fills and the page comes in. Only on the first page of a visit
// (sessionStorage) and never with "reduce motion". It lives in the <head>, before the first paint: the backdrop is CSS
// on html.boot::before, and the logo goes in as soon as the <body> exists.
(() => {
  try {
    if (matchMedia("(prefers-reduced-motion: reduce)").matches) return;
    if (sessionStorage.getItem("audial-boot")) return;
    sessionStorage.setItem("audial-boot", "1");
  } catch { return; }
  const root = document.documentElement;
  root.classList.add("boot");
  const started = performance.now();
  let screen = null;
  const mount = () => {
    if (screen || !document.body) return;
    screen = document.createElement("div");
    screen.className = "boot-screen";
    screen.setAttribute("aria-hidden", "true");
    screen.innerHTML = '<div class="boot-mark"><img src="/img/logo.png?v=2" alt="" width="64" height="64"><span class="boot-name"><span>udial</span></span></div><i class="boot-bar"></i>';
    document.body.prepend(screen);
  };
  if (document.body) mount(); else document.addEventListener("DOMContentLoaded", mount);
  let leaving = false;
  const leave = () => {
    if (leaving) return;
    leaving = true;
    // long enough to see the "A" pull the name out, never longer than the page needs
    const wait = Math.max(0, 2800 - (performance.now() - started));
    setTimeout(() => {
      root.classList.add("boot-out");
      setTimeout(() => {
        root.classList.remove("boot", "boot-out");
        screen?.remove();
      }, 700);
    }, wait);
  };
  addEventListener("load", leave);
  setTimeout(leave, 3600); // a slow image or font does not hold the page
})();
