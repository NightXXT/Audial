// Two small things for the pages of the site (never for /overlay, which keeps its channel in the "#"):
// 1. The address bar stays clean: in-page links scroll to their section without writing "#secao" after the
//    slash, and a link that arrives with "#secao" still lands on it before the address is cleaned up.
// 2. F12, Ctrl+Shift+I/J/C and Ctrl+U (view source) do nothing here. This only discourages the curious: the
//    page's code still reaches every visitor's browser and is public on GitHub, so nothing secret lives here.
(() => {
  const cleanAddress = () => {
    if (location.hash) history.replaceState(history.state, "", location.pathname + location.search);
  };
  const go = (el, smooth) => {
    try { el.scrollIntoView({ behavior: smooth ? "smooth" : "instant", block: "start" }); } catch { el.scrollIntoView(true); }
  };
  document.addEventListener("click", (e) => {
    const a = e.target.closest ? e.target.closest('a[href^="#"]') : null;
    if (!a || e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
    const id = decodeURIComponent(a.getAttribute("href").slice(1));
    const target = id ? document.getElementById(id) : document.documentElement;
    if (!target) return;
    e.preventDefault();
    go(target, !matchMedia("(prefers-reduced-motion: reduce)").matches);
    cleanAddress();
  });
  // arriving with "#secao": jump there first, then clean the address
  if (location.hash) {
    addEventListener("load", () => {
      const el = document.getElementById(decodeURIComponent(location.hash.slice(1)));
      if (el) go(el, false);
      cleanAddress();
    });
  }

  document.addEventListener("keydown", (e) => {
    const k = (e.key || "").toLowerCase();
    const devtools = e.key === "F12"
      || (e.ctrlKey && e.shiftKey && (k === "i" || k === "j" || k === "c"))
      || (e.metaKey && e.altKey && (k === "i" || k === "j" || k === "c" || k === "u"))
      || (e.ctrlKey && !e.shiftKey && k === "u");
    if (devtools) { e.preventDefault(); e.stopPropagation(); }
  }, true);
})();
