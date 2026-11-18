// RUN/maai と RUN/league の生の出力 → RUN/data の CSV（レポートの datasets）。
//   techniques.csv  選手 × 技の統計（maai.json から）
//   curve.csv       選手 × 技 × 距離の帯（腕の長さの 0.1 倍）
//   range_bins.csv / range_summary.csv  距離の帯（2 cm）ごとの与えた・受けた / 秒、得意・不得意（maailab --ranges をそのまま写す）
//   matchups.csv    組ごと（両方向）の勝敗・勝ち点率・Wilson の 95% 区間・ダメージ・ダウン
//   fighters.csv    選手ごとの勝敗・区間・有利 / 不利な組の数
//   profiles.csv    選手ごとのまとめ（体格・勝ち点率・ダウン・得意 / 不得意の距離・いちばん効いた技）
// 使い方: node tools/roster-report/build-data.mjs <RUN> [roster フォルダ（既定 games/MiniRing/Characters/roster）]
import { readFileSync, copyFileSync } from 'node:fs';
import { join } from 'node:path';
import { layout, readCsv, writeCsv, fixed, ensureDir, readCharacter, wilson, usage } from './lib.mjs';

const [, , run, rosterDir = 'games/MiniRing/Characters/roster'] = process.argv;
if (!run) usage('usage: node tools/roster-report/build-data.mjs <RUN> [rosterDir]');
const L = layout(run);
ensureDir(L.data);

// ── 技ごと（maai.json） ──
const { meta, fighters } = JSON.parse(readFileSync(join(L.maai, 'maai.json'), 'utf8'));
const techRows = [];
const curveRows = [];
for (const f of fighters) {
  const r = f.ReachCm;
  for (const t of f.Techniques) {
    techRows.push([
      f.Id, f.Name, f.Style, f.Equipment, fixed(r, 1), t.Technique, t.Kind, t.Throws, t.Landed, fixed(t.HitRate), fixed(t.CleanRate), fixed(t.Share),
      fixed(t.LaunchCm.Mean, 1), fixed(t.LaunchCm.Median, 1), fixed(t.LaunchCm.Min, 1), fixed(t.LaunchCm.Max, 1), fixed(t.LaunchCm.P10, 1), fixed(t.LaunchCm.P90, 1), fixed(t.LaunchCm.Sd, 1), fixed(t.LaunchRatio.Median, 2),
      fixed(t.LandedLaunchCm.Median, 1), fixed(t.ImpactCm.Median, 1), fixed(t.PerHit.Mean, 2), fixed(t.PerHit.Median, 2), fixed(t.PerHit.Max, 2), fixed(t.PerHit.P90, 2), fixed(t.PerHit.Sd, 2), fixed(t.DamagePerThrow, 2), fixed(t.TotalDamage, 1),
      t.BestFrom === null ? '' : fixed(t.BestFrom * r, 1), t.BestTo === null ? '' : fixed(t.BestTo * r, 1), fixed(t.BestDamagePerThrow, 2),
    ]);
    for (const b of t.Curve) {
      curveRows.push([
        f.Id, t.Technique, t.Kind, fixed(b.From, 1), fixed(b.To, 1), fixed(b.From * r, 1), fixed(b.To * r, 1), fixed(((b.From + b.To) / 2) * r, 1),
        b.Throws, b.Landed, fixed(b.HitRate), fixed(b.CleanRate), fixed(b.DamagePerThrow, 2), fixed(b.PerHit.Mean, 2), fixed(b.PerHit.Median, 2), fixed(b.PerHit.Max, 2),
      ]);
    }
  }
}
writeCsv(join(L.data, 'techniques.csv'), ['fighter', 'name', 'style', 'equipment', 'reach_cm', 'technique', 'kind', 'throws', 'landed', 'hit_rate', 'clean_rate', 'share',
  'launch_mean_cm', 'launch_median_cm', 'launch_min_cm', 'launch_max_cm', 'launch_p10_cm', 'launch_p90_cm', 'launch_sd_cm', 'ratio_median',
  'landed_launch_median_cm', 'impact_median_cm', 'hit_mean', 'hit_median', 'hit_max', 'hit_p90', 'hit_sd', 'per_throw', 'total_damage',
  'best_from_cm', 'best_to_cm', 'best_per_throw'], techRows);
writeCsv(join(L.data, 'curve.csv'), ['fighter', 'technique', 'kind', 'ratio_from', 'ratio_to', 'from_cm', 'to_cm', 'mid_cm', 'throws', 'landed', 'hit_rate', 'clean_rate', 'per_throw', 'hit_mean', 'hit_median', 'hit_max'], curveRows);
copyFileSync(join(L.maai, 'ranges_bins.csv'), join(L.data, 'range_bins.csv'));
copyFileSync(join(L.maai, 'ranges_summary.csv'), join(L.data, 'range_summary.csv'));

// ── 総当たり（league.csv: 1 試合 1 行） ──
const bouts = readCsv(join(L.league, 'league.csv'));
const pair = new Map();
const get = (a, b) => {
  const k = `${a}|${b}`;
  if (!pair.has(k)) pair.set(k, { me: a, opp: b, n: 0, w: 0, l: 0, d: 0, ko: 0, kod: 0, dmg: 0, dmgTaken: 0, redN: 0, redW: 0 });
  return pair.get(k);
};
for (const r of bouts) {
  const w = +r.winner;
  for (const [me, opp, sign, myDmg, opDmg, myKd, opKd, red] of [
    [r.red, r.blue, 1, +r.red_damage, +r.blue_damage, +r.red_knockdowns, +r.blue_knockdowns, true],
    [r.blue, r.red, -1, +r.blue_damage, +r.red_damage, +r.blue_knockdowns, +r.red_knockdowns, false],
  ]) {
    const p = get(me, opp);
    const res = w * sign;
    p.n++; p.w += res > 0 ? 1 : 0; p.l += res < 0 ? 1 : 0; p.d += res === 0 ? 1 : 0;
    p.dmg += myDmg; p.dmgTaken += opDmg; p.ko += myKd; p.kod += opKd;
    if (red) { p.redN++; p.redW += res > 0 ? 1 : res === 0 ? 0.5 : 0; }
  }
}
const matchRows = [];
const tot = new Map();
for (const p of pair.values()) {
  const s = (p.w + 0.5 * p.d) / p.n;
  const [lo, hi] = wilson(s, p.n);
  const verdict = lo > 0.5 ? 'favored' : hi < 0.5 ? 'unfavored' : 'even';
  matchRows.push([p.me, p.opp, p.n, p.w, p.l, p.d, fixed(s), fixed(lo), fixed(hi), verdict, fixed(p.dmg / p.n, 1), fixed(p.dmgTaken / p.n, 1), fixed(p.ko / p.n, 2), fixed(p.kod / p.n, 2), fixed(p.redW / p.redN)]);
  const t = tot.get(p.me) ?? { n: 0, w: 0, l: 0, d: 0, dmg: 0, dmgTaken: 0, favored: 0, unfavored: 0, ko: 0, kod: 0, pairs: 0 };
  t.n += p.n; t.w += p.w; t.l += p.l; t.d += p.d; t.dmg += p.dmg; t.dmgTaken += p.dmgTaken; t.ko += p.ko / p.n; t.kod += p.kod / p.n; t.pairs++;
  t.favored += verdict === 'favored' ? 1 : 0; t.unfavored += verdict === 'unfavored' ? 1 : 0;
  tot.set(p.me, t);
}
writeCsv(join(L.data, 'matchups.csv'), ['fighter', 'opponent', 'bouts', 'wins', 'losses', 'draws', 'score', 'ci_low', 'ci_high', 'verdict', 'dmg_per_bout', 'dmg_taken_per_bout', 'knockdowns_per_bout', 'knockdowns_taken_per_bout', 'score_as_red'], matchRows);

const ranked = [...tot].sort((a, b) => (b[1].w + 0.5 * b[1].d) / b[1].n - (a[1].w + 0.5 * a[1].d) / a[1].n);
const gearName = { glove: 'グローブ', none: '素手', tonfa: 'トンファー', katar: 'カタール', gauntlet: '籠手', knuckles: 'メリケンサック', shield: '盾' };
const weaponName = { katana: '日本刀', longsword: 'ロングソード', staff: '六尺棒', jo: '杖', club: 'こんぼう', ironSword: '片手剣' };
const fighterRows = [];
const profileRows = [];
const ranges = readCsv(join(L.data, 'range_summary.csv'));
const techs = readCsv(join(L.data, 'techniques.csv'));
for (const [id, t] of ranked) {
  const j = readCharacter(rosterDir, id);
  const b = j.body.build;
  const s = (t.w + 0.5 * t.d) / t.n;
  const [lo, hi] = wilson(s, t.n);
  const weapon = j.body.weapon ?? '';
  const wear = j.body.wear ?? 'glove';
  fighterRows.push([id, j.name, j.tagline, j.textbook, weapon, wear, t.n, t.w, t.l, t.d, fixed(s), fixed(lo), fixed(hi), t.favored, t.unfavored, fixed(t.dmg / t.n, 1), fixed(t.dmgTaken / t.n, 1)]);
  const r = ranges.find((x) => x.fighter === id) ?? {};
  const equipment = weapon ? (weaponName[weapon] ?? weapon) + (wear === 'shield' ? 'と盾' : '') : (gearName[wear] ?? wear);
  const top = techs.filter((x) => x.fighter === id && x.kind !== 'other' && +x.throws >= 30).sort((a, c) => +c.per_throw - +a.per_throw)[0];
  const bestMid = (+r.best_from_cm + +r.best_to_cm) / 2;
  profileRows.push([id, j.name, j.hudName, j.tagline, j.textbook, equipment, weapon ? 1 : 0, b.height, b.arms, b.muscle, fixed(b.height ** 3 * b.muscle ** 2, 3), r.reach_cm,
    fixed(s), fixed(lo), fixed(hi), t.w, t.l, t.d, fixed(t.ko / t.pairs, 3), fixed(t.kod / t.pairs, 3), fixed(t.dmg / t.n, 1), fixed(t.dmgTaken / t.n, 1),
    r.median_cm, r.best_from_cm, r.best_to_cm, r.best_net_per_s, r.worst_from_cm, r.worst_to_cm, r.worst_net_per_s, fixed(bestMid - +r.median_cm, 1), top?.technique ?? '', top?.per_throw ?? '']);
}
writeCsv(join(L.data, 'fighters.csv'), ['fighter', 'name', 'tagline', 'textbook', 'weapon', 'wear', 'bouts', 'wins', 'losses', 'draws', 'score', 'ci_low', 'ci_high', 'favored_matchups', 'unfavored_matchups', 'dmg_per_bout', 'dmg_taken_per_bout'], fighterRows);
writeCsv(join(L.data, 'profiles.csv'), ['fighter', 'name', 'hud', 'tagline', 'textbook', 'equipment', 'armed', 'height', 'arms', 'muscle', 'mass', 'reach_cm', 'score', 'ci_low', 'ci_high', 'wins', 'losses', 'draws',
  'kd_per_bout', 'kd_taken_per_bout', 'dmg_per_bout', 'taken_per_bout', 'median_cm', 'best_from_cm', 'best_to_cm', 'best_net_per_s', 'worst_from_cm', 'worst_to_cm', 'worst_net_per_s', 'best_gap_cm', 'top_technique', 'top_per_throw'], profileRows);

const redScore = bouts.reduce((a, r) => a + (+r.winner > 0 ? 1 : +r.winner === 0 ? 0.5 : 0), 0) / bouts.length;
console.log(JSON.stringify({ maai: meta, league: { bouts: bouts.length, pairs: pair.size / 2, redScore: +redScore.toFixed(3), draws: bouts.filter((r) => r.winner === '0').length }, fighters: ranked.length }));
