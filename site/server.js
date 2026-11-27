// Audial site server for hosts that run a plain Node process (Render, or any VPS): serves the static pages and the
// functions in api/ exactly as Vercel did (same handlers, same headers from vercel.json, clean URLs, no trailing
// slash). Vercel keeps working with vercel.json as before; this file is only used where `node server.js` runs.
//
//   PORT  port to listen on (Render sets it)
"use strict";

const http = require("http");
const fs = require("fs");
const path = require("path");

const visits = require("./api/_visits");

const ROOT = __dirname;
const PORT = Number(process.env.PORT) || 3000;
const BODY_LIMIT = 1024 * 1024; // the photo upload in /api/profile is the largest body (a small webp, base64)

// the header rules of vercel.json, in order; a later rule overrides the same header of an earlier one
const RULES = (JSON.parse(fs.readFileSync(path.join(ROOT, "vercel.json"), "utf8")).headers || []).map((rule) => ({
  test: new RegExp("^" + rule.source + "$"),
  headers: rule.headers,
}));

const TYPES = {
  ".html": "text/html; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".json": "application/json; charset=utf-8",
  ".svg": "image/svg+xml",
  ".png": "image/png",
  ".jpg": "image/jpeg",
  ".jpeg": "image/jpeg",
  ".webp": "image/webp",
  ".gif": "image/gif",
  ".ico": "image/x-icon",
  ".woff": "font/woff",
  ".woff2": "font/woff2",
  ".mp4": "video/mp4",
  ".webm": "video/webm",
  ".txt": "text/plain; charset=utf-8",
  ".xml": "application/xml; charset=utf-8",
};

// never served as files: the server's own code, the functions' source, dependencies, config
const PRIVATE = /^\/(api|node_modules)(\/|$)|^\/(server\.js|package(-lock)?\.json|vercel\.json)$|\/\./;

const functions = new Map();
function loadFunction(name) {
  if (!/^[a-z][a-z0-9-]*$/.test(name)) return null; // "_lib" and friends are helpers, not endpoints
  if (functions.has(name)) return functions.get(name);
  const file = path.join(ROOT, "api", name + ".js");
  const fn = fs.existsSync(file) ? require(file) : null;
  functions.set(name, fn);
  return fn;
}

function applyHeaders(res, pathname) {
  for (const rule of RULES) {
    if (rule.test.test(pathname)) {
      for (const h of rule.headers) res.setHeader(h.key, h.value);
    }
  }
}

function readBody(req) {
  return new Promise((resolve, reject) => {
    const chunks = [];
    let size = 0;
    req.on("data", (chunk) => {
      size += chunk.length;
      if (size > BODY_LIMIT) {
        reject(Object.assign(new Error("body too large"), { status: 413 }));
        req.destroy();
        return;
      }
      chunks.push(chunk);
    });
    req.on("end", () => resolve(Buffer.concat(chunks)));
    req.on("error", reject);
  });
}

async function runFunction(fn, req, res, url) {
  // what Vercel's Node runtime gives a function: the parsed body, the raw one, and the query
  const raw = req.method === "GET" || req.method === "HEAD" ? Buffer.alloc(0) : await readBody(req);
  req.rawBody = raw;
  const type = String(req.headers["content-type"] || "");
  if (raw.length === 0) req.body = undefined;
  else if (type.includes("application/json")) {
    try {
      req.body = JSON.parse(raw.toString("utf8"));
    } catch {
      req.body = raw.toString("utf8");
    }
  } else req.body = raw.toString("utf8");
  req.query = Object.fromEntries(url.searchParams);
  await fn(req, res);
}

function notFound(res) {
  res.statusCode = 404;
  res.setHeader("Content-Type", "text/plain; charset=utf-8");
  res.setHeader("Cache-Control", "no-store");
  res.end("404: página não encontrada");
}

function statFile(file) {
  try {
    const s = fs.statSync(file);
    return s.isFile() ? s : null;
  } catch {
    return null;
  }
}

function sendFile(req, res, file, stat) {
  const etag = `W/"${stat.size.toString(16)}-${Math.floor(stat.mtimeMs).toString(16)}"`;
  res.setHeader("Content-Type", TYPES[path.extname(file).toLowerCase()] || "application/octet-stream");
  res.setHeader("ETag", etag);
  res.setHeader("Accept-Ranges", "bytes");
  if (!res.hasHeader("Cache-Control")) res.setHeader("Cache-Control", "public, max-age=0, must-revalidate");
  if (req.headers["if-none-match"] === etag) {
    res.statusCode = 304;
    return res.end();
  }
  // byte ranges, so the video can be seeked (and plays at all on Safari)
  const range = /^bytes=(\d*)-(\d*)$/.exec(String(req.headers.range || ""));
  let start = 0;
  let end = stat.size - 1;
  if (range && (range[1] || range[2])) {
    if (range[1]) {
      start = Number(range[1]);
      if (range[2]) end = Math.min(end, Number(range[2]));
    } else {
      start = Math.max(0, stat.size - Number(range[2]));
    }
    if (start > end || start >= stat.size) {
      res.statusCode = 416;
      res.setHeader("Content-Range", `bytes */${stat.size}`);
      return res.end();
    }
    res.statusCode = 206;
    res.setHeader("Content-Range", `bytes ${start}-${end}/${stat.size}`);
  } else {
    res.statusCode = 200;
  }
  res.setHeader("Content-Length", end - start + 1);
  if (req.method === "HEAD") return res.end();
  fs.createReadStream(file, { start, end }).pipe(res);
}

function redirect(res, location) {
  res.statusCode = 308;
  res.setHeader("Location", location);
  res.end();
}

const server = http.createServer(async (req, res) => {
  try {
    const host = String(req.headers.host || "");
    const url = new URL(req.url, "http://" + (host || "localhost"));
    // www.audial.com.br → audial.com.br, as on Vercel
    if (/^www\./i.test(host)) {
      return redirect(res, "https://" + host.slice(4) + url.pathname + url.search);
    }
    let pathname;
    try {
      pathname = decodeURIComponent(url.pathname);
    } catch {
      return notFound(res);
    }
    if (pathname.includes("\0") || pathname.includes("..")) return notFound(res);
    applyHeaders(res, pathname);

    if (pathname.startsWith("/api/")) {
      const fn = loadFunction(pathname.slice(5));
      if (!fn) return notFound(res);
      return await runFunction(fn, req, res, url);
    }
    if (req.method !== "GET" && req.method !== "HEAD") {
      res.statusCode = 405;
      res.setHeader("Allow", "GET, HEAD");
      return res.end();
    }
    // no trailing slash, and no ".html" in the address (cleanUrls)
    if (pathname.length > 1 && pathname.endsWith("/")) return redirect(res, pathname.replace(/\/+$/, "") + url.search);
    if (pathname.endsWith(".html")) {
      const clean = pathname === "/index.html" ? "/" : pathname.slice(0, -5);
      return redirect(res, clean + url.search);
    }
    if (PRIVATE.test(pathname)) return notFound(res);
    // the installer lives in the GitHub releases (free bandwidth): this always points to the newest one
    if (pathname === "/baixar") {
      // before the launch (1 December 2026, Brasília) the link goes to the countdown, not to a release that isn't there yet
      if (Date.now() < Date.parse("2026-12-01T00:00:00-03:00")) {
        res.statusCode = 302;
        res.setHeader("Location", "/#baixar");
        res.setHeader("Cache-Control", "no-store");
        return res.end();
      }
      res.statusCode = 302;
      res.setHeader("Location", "https://github.com/NightXXT/Audial/releases/latest/download/AudialSetup.exe");
      res.setHeader("Cache-Control", "no-store");
      return res.end();
    }

    const candidates = pathname === "/" ? ["index.html"] : [pathname.slice(1), pathname.slice(1) + ".html"];
    for (const rel of candidates) {
      const file = path.join(ROOT, rel);
      if (!file.startsWith(ROOT + path.sep)) return notFound(res);
      const stat = statFile(file);
      if (stat) {
        if (req.method === "GET" && file.endsWith(".html")) visits.count(req, pathname);
        return sendFile(req, res, file, stat);
      }
    }
    return notFound(res);
  } catch (e) {
    console.error(e);
    if (!res.headersSent) {
      res.statusCode = e && e.status === 413 ? 413 : 500;
      res.setHeader("Content-Type", "application/json; charset=utf-8");
      res.end(JSON.stringify({ status: "error", message: "Erro no servidor." }));
    } else {
      res.end();
    }
  }
});

server.keepAliveTimeout = 65000; // above the host's proxy timeout, so kept-alive connections are not cut mid-request
visits.start();
server.listen(PORT, () => console.log(`Audial site on :${PORT}`));
