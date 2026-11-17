// ダッシュボード（Artifact の Data 型）へ書く文書の一覧を作る（ArtifactData の batch の writes にそのまま渡す）。
// 先に RUN/data の 6 つの CSV を Artifact の publish（asset: true）で上げ、返った /_blob/<id> を assets.json に
// { "profiles": "/_blob/…", "matchups": "/_blob/…", "techniques": …, "curve": …, "range_bins": …, "range_summary": … } と書く。
// 既にある文書を書き換えるときは、読んだ version を versions.json（{ "datasets/profiles": 1, "files/index.html": 3, … }）で渡す
// （if_version が無い既存文書への書き込みは拒まれる）。
// 使い方: node tools/roster-report/make-writes.mjs <RUN> <assets.json> [versions.json] > writes.json
import { readFileSync, readdirSync, existsSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { layout, usage } from './lib.mjs';

const [, , run, assetsPath, versionsPath] = process.argv;
if (!run || !assetsPath) usage('usage: node tools/roster-report/make-writes.mjs <RUN> <assets.json> [versions.json]');
const L = layout(run);
const assets = JSON.parse(readFileSync(assetsPath, 'utf8'));
const versions = versionsPath && existsSync(versionsPath) ? JSON.parse(readFileSync(versionsPath, 'utf8')) : {};
const { meta } = JSON.parse(readFileSync(join(L.maai, 'maai.json'), 'utf8'));
const commit = existsSync(join(run, 'commit.txt')) ? readFileSync(join(run, 'commit.txt'), 'utf8').trim() : '';
const at = new Date().toISOString();
const where = `PhysicsVersion ${meta.physics}・LogicVersion ${meta.logic}${commit ? `、コミット ${commit}` : ''}`;

const datasets = {
  profiles: ['選手ごとのまとめ', `総当たりの成績・ダウン、間合いの測定の得意・不得意の距離、体格（名簿のファイル）を選手ごとに並べた表（${where}）。`],
  matchups: ['組ごとの勝ち点率', `総当たりの組ごと（両方向）の勝敗・勝ち点率・95% 区間・ダメージ・ダウン（${where}）。`],
  techniques: ['技ごとの間合いとダメージの統計', `間合いの測定（maailab、${meta.boutCount} 試合・${meta.throws} 打）を選手 × 技ごとにまとめた表（${where}）。`],
  curve: ['距離の帯ごとの技のダメージ', '同じ打ちを、打ち出した距離の帯（腕の長さの 0.1 倍ごと）に分けて数えた表。'],
  range_bins: ['距離ごとの与えた・受けた（/秒）', '両方が立っている間の胸と胸の距離を 2 cm の帯に分け、選手ごとにいた時間・1 秒あたりの与えた・受けたダメージを数えた表。'],
  range_summary: ['得意・不得意の距離', '選手ごとに、差し引き（与えた − 受けた）/ 秒が最大・最小の距離の帯（30 秒以上いた帯だけ）。'],
};

const pin = (key) => (versions[key] ? { if_version: versions[key] } : {});
const writes = [{ op: 'set', collection: 'dash', doc_id: 'meta', data: { title: `${readFileSync(join(L.page, 'index.html'), 'utf8').match(/<h1>([^<]+)<\/h1>/)[1]}`, lang: 'ja-JP' }, ...pin('dash/meta') }];
for (const [id, [title, description]] of Object.entries(datasets)) {
  if (!assets[id]) throw new Error(`assets.json に ${id} の /_blob がありません`);
  writes.push({
    op: 'set', collection: 'datasets', doc_id: id, ...pin(`datasets/${id}`),
    data: { title, description, source: { kind: 'file', url: assets[id], name: `${id}.csv`, format: 'csv' }, updated: { at, by: 'Claude' }, fixed: true },
  });
}
const files = readdirSync(L.page).filter((f) => f.startsWith('doc_') && f.endsWith('.json'));
for (const f of files) {
  const name = f.slice(4, -5);
  writes.push({ op: 'set', collection: 'files', doc_id: name, file_path: resolve(L.page, f), ...pin(`files/${name}`) });
}
console.log(JSON.stringify(writes, null, 2));
