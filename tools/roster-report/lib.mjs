// 総当たりレポートの道具が共有する小物（CSV の読み書き・作業フォルダの配置）。手順は docs/ROSTER_REPORT.md。
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { join } from 'node:path';

/** 作業フォルダ（RUN）の中の決まった場所。 */
export const layout = (run) => ({
  maai: join(run, 'maai'), // maailab の出力: maai.md・maai.json・maai.csv・ranges_bins.csv・ranges_summary.csv
  league: join(run, 'league'), // rosterleague の出力: league.md・league.csv
  data: join(run, 'data'), // build-data.mjs の出力（レポートの datasets）
  portraits: join(run, 'portraits'), // miniring-arena portraits の出力
  shots: join(run, 'shots'), // shots.sh の出力（試合の動画とコマ）
  page: join(run, 'page'), // build-images.mjs・build-page.mjs の出力（レポートのページ）
});

/** 引用符・引用符の二重化に対応した CSV の読み込み（値はすべて文字列）。 */
export const readCsv = (path) => {
  const lines = readFileSync(path, 'utf8').trim().split(/\r?\n/);
  const head = splitLine(lines[0]);
  return lines.slice(1).map((l) => Object.fromEntries(splitLine(l).map((v, i) => [head[i], v])));
};

const splitLine = (line) => {
  const out = [];
  let cell = '';
  let quoted = false;
  for (let i = 0; i < line.length; i++) {
    const ch = line[i];
    if (ch === '"') {
      if (quoted && line[i + 1] === '"') { cell += '"'; i++; continue; }
      quoted = !quoted;
      continue;
    }
    if (ch === ',' && !quoted) { out.push(cell); cell = ''; continue; }
    cell += ch;
  }
  out.push(cell);
  return out;
};

/** CSV の 1 セル（, " 改行を含めば引用符で囲む）。 */
export const cell = (v) => {
  const s = v === null || v === undefined || (typeof v === 'number' && !Number.isFinite(v)) ? '' : String(v);
  return /[",\n]/.test(s) ? `"${s.replaceAll('"', '""')}"` : s;
};

export const writeCsv = (path, header, rows) => {
  writeFileSync(path, [header.join(','), ...rows.map((r) => r.map(cell).join(','))].join('\n') + '\n');
};

export const fixed = (v, d = 4) => (v === null || v === undefined || v === 'NaN' || Number.isNaN(Number(v)) ? '' : Number(v).toFixed(d));

export const ensureDir = (dir) => mkdirSync(dir, { recursive: true });

/** 名簿のキャラのファイル（games/MiniRing/Characters/roster/<id>.json）。 */
export const readCharacter = (rosterDir, id) => JSON.parse(readFileSync(join(rosterDir, `${id}.json`), 'utf8'));

/** 勝ち点の割合 p（n 試合）の Wilson の 95% 区間。 */
export const wilson = (p, n, z = 1.96) => {
  if (n === 0) return [NaN, NaN];
  const d = 1 + (z * z) / n;
  const c = (p + (z * z) / (2 * n)) / d;
  const h = (z * Math.sqrt((p * (1 - p)) / n + (z * z) / (4 * n * n))) / d;
  return [c - h, c + h];
};

/** ピアソンの相関。 */
export const pearson = (a, b) => {
  const n = a.length;
  const ma = a.reduce((x, y) => x + y, 0) / n;
  const mb = b.reduce((x, y) => x + y, 0) / n;
  let s = 0; let sa = 0; let sb = 0;
  for (let i = 0; i < n; i++) { s += (a[i] - ma) * (b[i] - mb); sa += (a[i] - ma) ** 2; sb += (b[i] - mb) ** 2; }
  return s / Math.sqrt(sa * sb);
};

export const usage = (text) => { console.error(text); process.exit(2); };
