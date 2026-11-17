// ダッシュボードに上げる前に、手元でページを描いて確かめる。ダッシュボードの実行環境（dash.data・dash.draw・dash.calc・色と字の
// トークン）を小さな代わり（preview/mock.js・preview/tokens.css）で置き換え、RUN/data の CSV と d3（cdnjs）で描く。
// http://localhost:<port>/ で開く（内蔵ブラウザは file:// と 127.0.0.1 を開けないので localhost で）。タブの題が "ERR:<数>" になり、
// 描画で投げた例外は window.__errors に入る。?only=<section id,...> で、その節だけを描く（長いページの画面写しが止まるとき）。
// 使い方: node tools/roster-report/preview.mjs <RUN> [port（既定 8765）]
import { readFileSync, writeFileSync } from 'node:fs';
import { createServer } from 'node:http';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { layout, usage } from './lib.mjs';

const [, , run, portArg = '8765'] = process.argv;
if (!run) usage('usage: node tools/roster-report/preview.mjs <RUN> [port]');
const here = dirname(fileURLToPath(import.meta.url));
const L = layout(run);
const datasets = ['profiles', 'matchups', 'techniques', 'curve', 'range_bins', 'range_summary'];

const build = (only) => {
  const raw = Object.fromEntries(datasets.map((k) => [k, readFileSync(join(L.data, `${k}.csv`), 'utf8')]));
  let page = readFileSync(join(L.page, 'index.html'), 'utf8');
  page = page.replace(/<script src="([^"]+)"><\/script>/g, (m, n) => `<script>${readFileSync(join(L.page, n), 'utf8')}</script>`);
  if (only) {
    const keep = new Set(only.split(','));
    page = page.replace(/<(section|header|details) id="([^"]+)"[\s\S]*?<\/\1>/g, (m, tag, id) => (keep.has(id) ? m : ''));
  }
  return '<!doctype html><meta charset="utf-8">'
    + `<style>${readFileSync(join(here, 'preview', 'tokens.css'), 'utf8')}</style>`
    + '<script src="https://cdnjs.cloudflare.com/ajax/libs/d3/7.8.5/d3.min.js"></script>'
    + `<script>window.RAW = ${JSON.stringify(raw).replace(/</g, '\\u003c')};</script>`
    + `<script>${readFileSync(join(here, 'preview', 'mock.js'), 'utf8')}</script>`
    + `<div id="dash-root">${page}</div>`;
};

writeFileSync(join(L.page, 'preview.html'), build(null));
const port = Number(portArg);
createServer((req, res) => {
  const only = new URL(req.url, 'http://localhost').searchParams.get('only');
  try {
    res.writeHead(200, { 'content-type': 'text/html; charset=utf-8' });
    res.end(build(only));
  } catch (e) {
    res.writeHead(500, { 'content-type': 'text/plain; charset=utf-8' });
    res.end(String(e.stack));
  }
}).listen(port, '127.0.0.1', () => console.log(`preview: http://localhost:${port}/  (${join(L.page, 'preview.html')} にも書いた)`));
