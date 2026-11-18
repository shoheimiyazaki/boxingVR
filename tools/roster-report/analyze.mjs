// 考察の材料を出す: 勝ち点率と各量の相関、武器 対 素手、得意な距離といつもいる距離のずれ、はっきり出た相性。
// レポートの「考察」の文はこの出力を見て書く（数字は書き写さず、ページの drawing / calc で出せる物はそちらで出す）。
// 使い方: node tools/roster-report/analyze.mjs <RUN>
import { join } from 'node:path';
import { layout, readCsv, pearson, usage } from './lib.mjs';

const [, , run] = process.argv;
if (!run) usage('usage: node tools/roster-report/analyze.mjs <RUN>');
const L = layout(run);
const P = readCsv(join(L.data, 'profiles.csv'));
const M = readCsv(join(L.data, 'matchups.csv'));
const T = readCsv(join(L.data, 'techniques.csv'));
const n = (v) => +v;

console.log('## 選手（勝ち点率の順）');
console.table(P.map((r) => ({ id: r.fighter, score: r.score, ci: `${(+r.ci_low).toFixed(2)}-${(+r.ci_high).toFixed(2)}`, armed: r.armed, mass: r.mass, reach: r.reach_cm, kd: r.kd_per_bout, kdTaken: r.kd_taken_per_bout, dmg: r.dmg_per_bout, taken: r.taken_per_bout, median: r.median_cm, best: `${r.best_from_cm}-${r.best_to_cm}`, worst: `${r.worst_from_cm}-${r.worst_to_cm}`, gap: r.best_gap_cm })));

const sc = P.map((r) => n(r.score));
const fist = P.filter((r) => r.armed !== '1');
console.log('\n## 勝ち点率との相関（全員）');
for (const k of ['kd_per_bout', 'kd_taken_per_bout', 'dmg_per_bout', 'taken_per_bout', 'mass', 'reach_cm', 'height', 'muscle', 'median_cm', 'best_net_per_s']) {
  console.log(`${k.padEnd(20)} ${pearson(sc, P.map((r) => n(r[k]))).toFixed(2)}`);
}
console.log(`\n## 素手・はめる物だけ（${fist.length} 人）`);
for (const k of ['mass', 'reach_cm', 'height', 'muscle']) console.log(`${k.padEnd(20)} ${pearson(fist.map((r) => n(r.score)), fist.map((r) => n(r[k]))).toFixed(2)}`);

const armed = new Set(P.filter((p) => p.armed === '1').map((p) => p.fighter));
let pts = 0; let bouts = 0;
for (const m of M) if (armed.has(m.fighter) && !armed.has(m.opponent)) { pts += n(m.wins) + 0.5 * n(m.draws); bouts += n(m.bouts); }
console.log(`\n## 武器 対 素手: 勝ち点率 ${(pts / bouts).toFixed(3)}（${bouts} 試合）`);

console.log('\n## 得意な距離といつもいる距離のずれ（得意の帯の真ん中 − いつもいる距離 [cm]。正 = もっと遠くが得意）');
for (const r of P) console.log(`${r.fighter.padEnd(10)} ${r.armed === '1' ? '武器' : '素手'} 得意 ${r.best_from_cm}-${r.best_to_cm} いつも ${r.median_cm} ずれ ${r.best_gap_cm}`);

const clear = M.filter((m) => m.verdict !== 'even');
console.log(`\n## はっきり出た組: ${clear.filter((m) => m.verdict === 'favored').length} / ${M.length / 2}`);
for (const r of P.slice(0, 5)) {
  const lost = M.filter((m) => m.fighter === r.fighter && m.verdict === 'unfavored').map((m) => `${m.opponent} ${(+m.score * 100).toFixed(0)}%`);
  console.log(`${r.fighter} が負け越す相手: ${lost.join(', ') || 'なし'}`);
}

console.log('\n## 選手ごとのいちばん効いた技（打ち 30 以上、1 打あたり）');
for (const r of P) {
  const top = T.filter((t) => t.fighter === r.fighter && t.kind !== 'other' && n(t.throws) >= 30).sort((a, b) => n(b.per_throw) - n(a.per_throw)).slice(0, 3);
  console.log(`${r.fighter.padEnd(10)} ${top.map((t) => `${t.technique} ${t.per_throw}（当たり ${(n(t.hit_rate) * 100).toFixed(0)}%）`).join(' / ')}`);
}
