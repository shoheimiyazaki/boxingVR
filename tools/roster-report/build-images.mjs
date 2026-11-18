// 選んだコマ（RUN/shots/<id>_<秒>.png）と顔の肖像（RUN/portraits/<id>_face.png）→ RUN/page/images<N>.js。
// ページは window.rpImages[id] = { shot, face }（data: URL）を読む。ダッシュボードの 1 文書は 256 KB までなので 200 KB ごとに分ける。
// choices.json は { "<id>": "<秒>", ... }（shots.sh の一覧を見て、その選手がよく見えるコマを選ぶ）。書いていない選手は 7.5 秒。
// 使い方: node tools/roster-report/build-images.mjs <RUN> [choices.json]
import { readFileSync, writeFileSync, readdirSync, existsSync, statSync, rmSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { join } from 'node:path';
import { layout, ensureDir, usage } from './lib.mjs';

const [, , run, choicesPath] = process.argv;
if (!run) usage('usage: node tools/roster-report/build-images.mjs <RUN> [choices.json]');
const L = layout(run);
const choices = choicesPath ? JSON.parse(readFileSync(choicesPath, 'utf8')) : {};
const small = join(L.shots, 'small');
ensureDir(small);
ensureDir(L.page);
for (const f of readdirSync(L.page).filter((f) => /^images\d+\.js$/.test(f))) rmSync(join(L.page, f));

const ids = readdirSync(L.portraits).filter((f) => f.endsWith('_face.png')).map((f) => f.replace('_face.png', '')).sort();
const entries = [];
for (const id of ids) {
  const t = choices[id] ?? '7.5';
  const src = join(L.shots, `${id}_${t}.png`);
  const image = { face: `data:image/png;base64,${readFileSync(join(L.portraits, `${id}_face.png`)).toString('base64')}` };
  if (existsSync(src)) {
    const dst = join(small, `${id}.jpg`);
    execFileSync('ffmpeg', ['-loglevel', 'error', '-y', '-i', src, '-vf', 'scale=480:-2', '-q:v', '6', dst]);
    image.shot = `data:image/jpeg;base64,${readFileSync(dst).toString('base64')}`;
  } else {
    console.warn(`${id}: ${src} が無いので試合のコマは入れない`);
  }
  entries.push(`window.rpImages[${JSON.stringify(id)}] = ${JSON.stringify(image)};`);
}

const files = [];
let cur = [];
let size = 0;
for (const e of entries) {
  if (size + e.length > 200000 && cur.length) { files.push(cur); cur = []; size = 0; }
  cur.push(e);
  size += e.length;
}
if (cur.length) files.push(cur);
files.forEach((lines, i) => writeFileSync(join(L.page, `images${i + 1}.js`), `window.rpImages = window.rpImages || {};\n${lines.join('\n')}\n`));
for (let i = 0; i < files.length; i++) console.log(`images${i + 1}.js: ${files[i].length} 人, ${statSync(join(L.page, `images${i + 1}.js`)).size} bytes`);
