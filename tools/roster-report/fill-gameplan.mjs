// 名簿の各キャラの教育（education.style.GamePlan.Own）に、間合いの測定の表（得意な距離・不得意な距離）を書き込む。
// 使い方（リポジトリの根で）: node tools/roster-report/fill-gameplan.mjs [docs/arena/maai_roster_ranges_bins.csv] [--min-seconds 30]
//
// - 表の出どころは miniring-arena maailab --ranges の ranges_bins.csv（2 cm の帯ごとの 1 秒あたりの与えた・受けたダメージ）。
//   その帯に全試合で min-seconds（既定 30 秒）以上いた帯だけを使う（短い帯は雑音。docs/ROSTER_REPORT.md §1.3 の得意・不得意と同じ境）。
// - 値は小数 3 桁に丸める。距離は帯の真ん中 mid_cm。
// - 既に書いてある GamePlan の性格（Weight・Boldness・Caution・Patience・Pressure）と Foe は残す。無ければ既定（Weight 1、ほか 1・8 秒・1）。
// - education の行だけを書き換え、ファイルのほかの部分（look の詰めた書き方など）は触らない。何度回しても同じ結果になる。
import fs from 'node:fs';
import path from 'node:path';

const args = process.argv.slice(2);
const minIndex = args.indexOf('--min-seconds');
const minSeconds = minIndex >= 0 ? Number(args[minIndex + 1]) : 30;
const positional = args.filter((a, i) => !a.startsWith('--') && (minIndex < 0 || i !== minIndex + 1));
const csvPath = positional[0] ?? 'docs/arena/maai_roster_ranges_bins.csv';
const rosterDir = 'games/MiniRing/Characters/roster';

const DEFAULTS = { Weight: 1, Boldness: 1, Caution: 1, Patience: 8, Pressure: 1 };

const round3 = v => Math.round(v * 1000) / 1000;

function readBins(file) {
  const lines = fs.readFileSync(file, 'utf8').split(/\r?\n/).filter(l => l.trim().length > 0);
  const header = lines[0].split(',');
  const col = name => {
    const i = header.indexOf(name);
    if (i < 0) throw new Error(`${file}: 列 ${name} がありません`);
    return i;
  };
  const [cFighter, cMid, cSeconds, cDealt, cTaken] = ['fighter', 'mid_cm', 'seconds', 'dealt_per_s', 'taken_per_s'].map(col);
  const byFighter = new Map();
  for (const line of lines.slice(1)) {
    const f = line.split(',');
    if (Number(f[cSeconds]) < minSeconds) continue;
    const bin = { Cm: Number(f[cMid]), Dealt: round3(Number(f[cDealt])), Taken: round3(Number(f[cTaken])) };
    if (![bin.Cm, bin.Dealt, bin.Taken].every(Number.isFinite)) throw new Error(`${file}: 数でない値: ${line}`);
    if (!byFighter.has(f[cFighter])) byFighter.set(f[cFighter], []);
    byFighter.get(f[cFighter]).push(bin);
  }
  for (const bins of byFighter.values()) bins.sort((a, b) => a.Cm - b.Cm);
  return byFighter;
}

const bin = b => `{ "Cm": ${b.Cm}, "Dealt": ${b.Dealt}, "Taken": ${b.Taken} }`;

function table(name, bins, indent) {
  if (!bins || bins.length === 0) return `${indent}"${name}": []`;
  return `${indent}"${name}": [\n${bins.map(b => `${indent}  ${bin(b)}`).join(',\n')}\n${indent}]`;
}

function educationText(education, nl) {
  const style = education.style ?? {};
  const plan = style.GamePlan;
  const others = Object.entries(style).filter(([k]) => k !== 'GamePlan');
  const head = Object.keys(DEFAULTS).map(k => `"${k}": ${plan[k]}`).join(', ');
  const planText = [
    '      "GamePlan": {',
    `        ${head},`,
    `${table('Own', plan.Own, '        ')}${plan.Foe && plan.Foe.length > 0 ? ',' : ''}`,
    ...(plan.Foe && plan.Foe.length > 0 ? [table('Foe', plan.Foe, '        ')] : []),
    '      }',
  ].join('\n');
  const styleText = ['    "style": {', ...others.map(([k, v]) => `      "${k}": ${JSON.stringify(v)},`), planText, '    },'].join('\n');
  const text = [
    '  "education": {',
    styleText,
    `    "vs": ${JSON.stringify(education.vs ?? {})},`,
    `    "weaponBot": ${JSON.stringify(education.weaponBot ?? null)}`,
    '  },',
  ].join('\n');
  return text.split('\n').join(nl);
}

const bins = readBins(csvPath);
let changed = 0;
for (const file of fs.readdirSync(rosterDir).filter(f => f.endsWith('.json')).sort()) {
  const full = path.join(rosterDir, file);
  const text = fs.readFileSync(full, 'utf8');
  const nl = text.includes('\r\n') ? '\r\n' : '\n';
  const json = JSON.parse(text);
  const own = bins.get(json.id);
  if (!own || own.length === 0) {
    console.error(`${json.id}: 表に ${minSeconds} 秒以上の帯がありません（書き換えない）`);
    continue;
  }

  const education = json.education ?? { style: {}, vs: {}, weaponBot: null };
  const old = education.style?.GamePlan ?? {};
  const plan = { ...DEFAULTS };
  for (const k of Object.keys(DEFAULTS)) if (typeof old[k] === 'number') plan[k] = old[k];
  plan.Own = own;
  if (Array.isArray(old.Foe) && old.Foe.length > 0) plan.Foe = old.Foe;
  education.style = { ...(education.style ?? {}), GamePlan: plan };

  // education の値（"education": から "record": の前まで）を置き換える。
  const start = text.indexOf('  "education":');
  const end = text.indexOf('  "record":');
  if (start < 0 || end < start) throw new Error(`${file}: education と record の行が見つかりません`);
  const next = text.slice(0, start) + educationText(education, nl) + nl + text.slice(end);
  const check = JSON.parse(next);
  if (JSON.stringify(check.education.style.GamePlan.Own) !== JSON.stringify(own)) throw new Error(`${file}: 書き戻しが一致しません`);
  if (next !== text) {
    fs.writeFileSync(full, next);
    changed++;
  }

  console.log(`${json.id}: ${own.length} 帯（${own[0].Cm}〜${own[own.length - 1].Cm} cm）`);
}

console.log(`書き換えたファイル: ${changed}`);
