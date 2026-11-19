// 名簿の拳の選手の教育（education.style.Rhythm）に、間の表（相手の拳の後のどの時に打てば得か）を書き込む。
// 使い方（リポジトリの根で）: node tools/roster-report/fill-rhythm.mjs <rhythmlab の --csv> [--shrink 100]
//
// - 表の出どころは miniring-arena rhythmlab の after_opponent_contact（相手の拳の出始めからの時間 × 自分が打ち出すまでにその拳が触れていたか）。
//   1 打の損得 = (与えたダメージ − 打ち出しから 0.5 秒に返されたダメージ) / 打った数。
// - 帯は 0.05 秒の帯をまとめて FROM の区切りにする（数の少ない帯の揺らぎを減らす）。
// - 選手ごとの値は、拳の選手全員の値へ縮める: (n × 自分 + shrink × 全員) / (n + shrink)（n はその帯で自分が打った数）。
// - 剣・棒・棍棒の選手（roster の weapon が有る）は書かない（剣士・こんぼうのボットはこの層を使わない）。
// - 既に書いてある Rhythm の Weight・Seize は残す（無ければ 1・1）。education.style の 1 行（"Rhythm": {...},）だけを書き換える・足す。
//   fill-gameplan.mjs が style のほかの値を 1 行の JSON で書くのと同じ形。何度回しても同じ結果になる。
import fs from 'node:fs';
import path from 'node:path';

const args = process.argv.slice(2);
const shrinkIndex = args.indexOf('--shrink');
const shrink = shrinkIndex >= 0 ? Number(args[shrinkIndex + 1]) : 100;
const positional = args.filter((a, i) => !a.startsWith('--') && (shrinkIndex < 0 || i !== shrinkIndex + 1));
const csvPath = positional[0];
if (!csvPath) throw new Error('rhythmlab の --csv を指定してください');
const rosterDir = 'games/MiniRing/Characters/roster';

// まとめる帯の始め [s]（0.05 秒の帯の番号 = FROM / 0.05）。最後の帯は 1.2 秒まで。
const FROM = [0, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.8, 1.0];
const QUIET_BIN = 24;
const round3 = v => Math.round(v * 1000) / 1000;

function readRows(file) {
  const lines = fs.readFileSync(file, 'utf8').split(/\r?\n/).filter(l => l.trim().length > 0);
  const header = lines[0].split(',');
  const col = name => {
    const i = header.indexOf(name);
    if (i < 0) throw new Error(`${file}: 列 ${name} がありません`);
    return i;
  };
  const c = Object.fromEntries(['fighter', 'curve', 'category', 'bin', 'dealt', 'throws', 'countered'].map(n => [n, col(n)]));
  return lines.slice(1).map(l => l.split(',')).filter(f => f[c.curve] === 'after_opponent_contact').map(f => ({
    fighter: f[c.fighter], category: f[c.category], bin: Number(f[c.bin]),
    dealt: Number(f[c.dealt]), throws: Number(f[c.throws]), countered: Number(f[c.countered]),
  }));
}

function groupOf(bin) {
  if (bin === QUIET_BIN) return 'quiet';
  for (let g = FROM.length - 1; g >= 0; g--) if (bin * 0.05 >= FROM[g] - 1e-6) return g;
  return 0;
}

function sums(rows, filter) {
  const s = new Map();
  for (const r of rows.filter(filter)) {
    const key = `${r.category}|${groupOf(r.bin)}`;
    const v = s.get(key) ?? { n: 0, net: 0 };
    v.n += r.throws;
    v.net += r.dealt - r.countered;
    s.set(key, v);
  }
  return s;
}

const roster = fs.readdirSync(rosterDir).filter(f => f.endsWith('.json')).sort().map(f => {
  const full = path.join(rosterDir, f);
  const text = fs.readFileSync(full, 'utf8');
  return { full, text, json: JSON.parse(text) };
});
const fists = new Set(roster.filter(r => !r.json.body?.weapon && !r.json.weapon).map(r => r.json.id));
const rows = readRows(csvPath);
const pool = sums(rows, r => fists.has(r.fighter));
const mean = (s, key) => { const v = s.get(key); return v && v.n > 0 ? v.net / v.n : 0; };

let changed = 0;
for (const { full, text, json } of roster) {
  if (!fists.has(json.id)) continue;
  const own = sums(rows, r => r.fighter === json.id);
  const value = (cat, g) => {
    const key = `${cat}|${g}`;
    const n = own.get(key)?.n ?? 0;
    return round3(((n * mean(own, key)) + (shrink * mean(pool, key))) / (n + shrink));
  };
  const old = json.education?.style?.Rhythm ?? {};
  const rhythm = {
    Weight: typeof old.Weight === 'number' ? old.Weight : 1,
    Seize: typeof old.Seize === 'number' ? old.Seize : 1,
    Quiet: value('none', 'quiet'),
    Bins: FROM.map((from, g) => ({ From: from, None: value('none', g), Guard: value('guard', g), Clean: value('clean', g) })),
  };
  const nl = text.includes('\r\n') ? '\r\n' : '\n';
  const line = `      "Rhythm": ${JSON.stringify(rhythm)},`;
  const lines = text.split(nl);
  const at = lines.findIndex(l => l.startsWith('      "Rhythm":'));
  if (at >= 0) {
    lines[at] = line;
  } else {
    const style = lines.findIndex(l => l === '    "style": {');
    if (style < 0) throw new Error(`${full}: "style": { の行が見つかりません`);
    lines.splice(style + 1, 0, line);
  }

  const next = lines.join(nl);
  const check = JSON.parse(next);
  if (JSON.stringify(check.education.style.Rhythm) !== JSON.stringify(rhythm)) throw new Error(`${full}: 書き戻しが一致しません`);
  if (next !== text) {
    fs.writeFileSync(full, next);
    changed++;
  }

  console.log(`${json.id}: 静か ${rhythm.Quiet}、入った直後 ${rhythm.Bins[0].Clean}、守りの上の 0.2 秒 ${rhythm.Bins[2].Guard}`);
}

console.log(`書き換えたファイル: ${changed}`);
