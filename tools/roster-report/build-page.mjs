// レポートのページを組む: page/index.html の {{…}} を RUN の数字で埋め、<!-- findings --> に RUN/findings.html（考察の文）を、
// <!-- images --> に images<N>.js の読み込みを入れて RUN/page/ に書く。あわせて、ダッシュボードの store に書く形
// （{ "text": … } の JSON、RUN/page/doc_<名前>.json）も作る。
// 使い方: node tools/roster-report/build-page.mjs <RUN> [相手 id（shots.sh と同じ、既定 gaston）]
import { readFileSync, writeFileSync, readdirSync, existsSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { layout, readCsv, ensureDir, usage } from './lib.mjs';

const [, , run, shotOpponent = 'gaston'] = process.argv;
if (!run) usage('usage: node tools/roster-report/build-page.mjs <RUN> [shotOpponent]');
const here = dirname(fileURLToPath(import.meta.url));
const L = layout(run);
ensureDir(L.page);

const { meta } = JSON.parse(readFileSync(join(L.maai, 'maai.json'), 'utf8'));
const profiles = readCsv(join(L.data, 'profiles.csv'));
const matchups = readCsv(join(L.data, 'matchups.csv'));
const leagueBouts = readCsv(join(L.league, 'league.csv')).length;
const perPair = Math.round(matchups.reduce((a, m) => a + +m.bouts, 0) / matchups.length);
const fmt = (v) => Number(v).toLocaleString('en-US');
const values = {
  FIGHTERS: profiles.length,
  UNARMED: profiles.filter((p) => p.armed !== '1').length,
  BOUTS_PER_PAIR: perPair,
  BOUTS_PER_FIGHTER: perPair * (profiles.length - 1),
  LEAGUE_BOUTS: fmt(leagueBouts),
  MAAI_BOUTS: fmt(meta.boutCount),
  THROWS: fmt(meta.throws),
  PHYSICS: meta.physics,
  LOGIC: meta.logic,
  SECONDS: meta.seconds,
  CI_HALF: Math.round(100 * 1.96 * Math.sqrt(0.25 / perPair)),
  SHOT_OPPONENT: shotOpponent,
};

const findingsPath = join(run, 'findings.html');
if (!existsSync(findingsPath)) {
  console.error(`${findingsPath} がありません。analyze.mjs の出力を見て考察を書いてください（形は tools/roster-report/examples/2026-11-18/findings.html）。`);
  process.exit(1);
}

let html = readFileSync(join(here, 'page', 'index.html'), 'utf8');
html = html.replace('<!-- findings -->\n', readFileSync(findingsPath, 'utf8'));
const images = readdirSync(L.page).filter((f) => /^images\d+\.js$/.test(f)).sort((a, b) => parseInt(a.slice(6), 10) - parseInt(b.slice(6), 10));
html = html.replace('<!-- images -->\n', images.map((f) => `<script src="${f}"></script>\n`).join(''));
html = html.replace(/\{\{(\w+)\}\}/g, (m, k) => {
  if (!(k in values)) throw new Error(`知らない置き換え ${m}`);
  return String(values[k]);
});
writeFileSync(join(L.page, 'index.html'), html);
writeFileSync(join(L.page, 'report.js'), readFileSync(join(here, 'page', 'report.js'), 'utf8'));

// ダッシュボードの store の files/<名前> へ書く文書（ArtifactData の set に file_path で渡す）。
for (const name of ['index.html', 'report.js', ...images]) {
  const text = readFileSync(join(L.page, name), 'utf8');
  if (text.length > 250000) throw new Error(`${name} が 1 文書の上限（256 KB）に近すぎます`);
  writeFileSync(join(L.page, `doc_${name}.json`), JSON.stringify({ text }));
}
console.log(JSON.stringify({ ...values, files: ['index.html', 'report.js', ...images] }));
