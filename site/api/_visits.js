// Page visits, counted by the Node server (server.js) without cookies, IPs or any id: for each day (São Paulo time)
// and each page, how many times it was opened ("views") and how many of those came from outside the site
// ("entries": no Referer from audial.com.br, a rough number of visits). Bots, link previews and prefetches don't count.
//
// Counts are kept in memory and written to the database every 15 minutes (a handful of commands per write, whatever
// the traffic), and when the server stops. If the database refuses, they stay in memory and are printed to the log.
"use strict";

const lib = require("./_lib");

const KEEP_DAYS = 400;
const FLUSH_EVERY = 15 * 60 * 1000;
const MAX_PATHS = 200; // a flood of made-up addresses can't grow this without limit (unknown pages are 404 anyway)

const BOTS = /bot|crawl|spider|slurp|preview|facebookexternalhit|embedly|discord|whatsapp|telegram|skype|slack|curl|wget|python|node-fetch|undici|axios|go-http|java\/|okhttp|headless|lighthouse|pagespeed|uptime|monitor|pingdom|statuscake|render/i;
const OWN = /^https?:\/\/(www\.)?(audial\.com\.br|audial-site\.onrender\.com)(\/|$)/i;

let pending = new Map(); // "YYYY-MM-DD" → Map(path → { v, e })

function today() {
  return new Intl.DateTimeFormat("en-CA", { timeZone: "America/Sao_Paulo", year: "numeric", month: "2-digit", day: "2-digit" }).format(new Date());
}

/** Called by server.js after it sent a page (an HTML file, status 200, GET). */
function count(req, pathname) {
  const ua = String(req.headers["user-agent"] || "");
  if (!ua || BOTS.test(ua)) return;
  const purpose = String(req.headers["sec-purpose"] || req.headers.purpose || "");
  if (/prefetch|prerender/i.test(purpose)) return;
  const day = today();
  let paths = pending.get(day);
  if (!paths) pending.set(day, (paths = new Map()));
  let c = paths.get(pathname);
  if (!c) {
    if (paths.size >= MAX_PATHS) return;
    paths.set(pathname, (c = { v: 0, e: 0 }));
  }
  c.v++;
  if (!OWN.test(String(req.headers.referer || ""))) c.e++;
}

async function flush() {
  if (pending.size === 0) return;
  const batch = pending;
  pending = new Map();
  const summary = [];
  try {
    for (const [day, paths] of batch) {
      const key = "visits:" + day;
      for (const [path, c] of paths) {
        await lib.redis("HINCRBY", key, "v:" + path, c.v);
        if (c.e) await lib.redis("HINCRBY", key, "e:" + path, c.e);
        summary.push(`${day} ${path} ${c.v}/${c.e}`);
      }
      await lib.redis("EXPIRE", key, KEEP_DAYS * 86400);
    }
  } catch (e) {
    // the database is down or over its quota: keep the counts for the next try, and leave a trace in the log
    for (const [day, paths] of batch) {
      let into = pending.get(day);
      if (!into) pending.set(day, (into = new Map()));
      for (const [path, c] of paths) {
        const t = into.get(path) || { v: 0, e: 0 };
        t.v += c.v;
        t.e += c.e;
        into.set(path, t);
      }
    }
    // never more than the last 40 days in memory
    const days = [...pending.keys()].sort();
    while (days.length > 40) pending.delete(days.shift());
    const lines = [];
    for (const [day, paths] of pending) for (const [path, c] of paths) lines.push(`${day} ${path} ${c.v}/${c.e}`);
    console.log("visitas (ainda não gravadas, " + (e && e.message ? e.message.slice(0, 60) : "erro") + "): " + lines.join(", "));
    return;
  }
  if (summary.length) console.log("visitas gravadas: " + summary.join(", "));
}

function start() {
  const timer = setInterval(() => flush().catch(() => {}), FLUSH_EVERY);
  timer.unref();
  // Render stops the server on every deploy and when it goes to sleep: write what was counted first
  for (const signal of ["SIGTERM", "SIGINT"]) {
    process.once(signal, () => {
      const done = () => process.exit(0);
      flush().then(done, done);
      setTimeout(done, 5000).unref();
    });
  }
}

/** For the panel: the last `days` days (oldest first), each with its total and its pages. */
async function report(days) {
  const n = Math.max(1, Math.min(90, Math.round(Number(days) || 30)));
  const out = [];
  const base = new Date(today() + "T12:00:00Z");
  for (let i = n - 1; i >= 0; i--) {
    const day = new Date(base.getTime() - i * 86400000).toISOString().slice(0, 10);
    const raw = (await lib.redis("HGETALL", "visits:" + day)) || [];
    const pages = {};
    for (let k = 0; k + 1 < raw.length; k += 2) {
      const field = String(raw[k]);
      const kind = field.slice(0, 2);
      const path = field.slice(2);
      const p = (pages[path] = pages[path] || { views: 0, entries: 0 });
      if (kind === "v:") p.views += Number(raw[k + 1]) || 0;
      else if (kind === "e:") p.entries += Number(raw[k + 1]) || 0;
    }
    // what this server counted and hasn't written yet
    const mem = pending.get(day);
    if (mem) {
      for (const [path, c] of mem) {
        const p = (pages[path] = pages[path] || { views: 0, entries: 0 });
        p.views += c.v;
        p.entries += c.e;
      }
    }
    let views = 0;
    let entries = 0;
    for (const p of Object.values(pages)) {
      views += p.views;
      entries += p.entries;
    }
    out.push({ day, views, entries, pages });
  }
  return out;
}

module.exports = { count, flush, start, report };
