#!/usr/bin/env bash
# Publica a pasta site/ no GitHub Pages (audial.com.br) pela branch gh-pages: só os arquivos estáticos,
# sem a API antiga, o painel e os arquivos do servidor. Uso: bash tools/publicar-site.sh
set -euo pipefail
raiz="$(cd "$(dirname "$0")/.." && pwd)"
tmp="$(mktemp -d)"
trap 'git -C "$raiz" worktree remove --force "$tmp" 2>/dev/null || true' EXIT
git -C "$raiz" fetch -q origin gh-pages 2>/dev/null || true
if git -C "$raiz" rev-parse -q --verify origin/gh-pages >/dev/null; then
  git -C "$raiz" worktree add -q -B gh-pages "$tmp" origin/gh-pages
else
  git -C "$raiz" worktree add -q --detach "$tmp"
  git -C "$tmp" checkout -q --orphan gh-pages
fi
find "$tmp" -mindepth 1 -maxdepth 1 ! -name .git -exec rm -rf {} +
( cd "$raiz/site" && tar -cf - \
    --exclude='./api' --exclude='./node_modules' --exclude='./server.js' --exclude='./package*.json' \
    --exclude='./vercel.json' --exclude='./Dockerfile' --exclude='./docker-compose*' --exclude='./.dockerignore' \
    --exclude='./painel.html' --exclude='./assets/painel.js' --exclude='./.env*' --exclude='*.pem' --exclude='./.vercel' . ) \
  | tar -xf - -C "$tmp"
echo "audial.com.br" > "$tmp/CNAME"
touch "$tmp/.nojekyll"
git -C "$tmp" add -A
if git -C "$tmp" diff --cached --quiet; then echo "Nada mudou no site."; exit 0; fi
git -C "$tmp" -c user.name=NightXXT -c user.email=51969031+NightXXT@users.noreply.github.com \
  commit -q -m "Site: $(git -C "$raiz" log -1 --format=%h -- site)"
git -C "$tmp" push -q origin gh-pages
echo "Publicado na gh-pages."
