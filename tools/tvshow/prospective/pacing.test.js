// node --test tools/tvshow/prospective/pacing.test.js
'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const path = require('path');
const pacing = require('./pacing');

const config = JSON.parse(fs.readFileSync(path.join(__dirname, 'config.json'), 'utf8'));

test('2 秒以上の無音を見つける（台詞の間・番組の頭・終わり）', () => {
  const lines = [
    { start: 0.5, end: 3.0 },
    { start: 3.2, end: 6.0 }, // 0.2 秒の間は無音に数えない
    { start: 8.5, end: 10.0 }, // 6.0〜8.5 の 2.5 秒
    { start: 9.0, end: 9.5, overlap: true }, // 重ねた感嘆は数えない
  ];
  const found = pacing.findSilences(lines, 13.0, 2.0);
  assert.deepEqual(found.map((s) => [s.from, s.to]), [[6.0, 8.5], [10.0, 13.0]]);
  assert.equal(pacing.findSilences(lines, 11.0, 2.0).length, 1); // 終わりの 1 秒は短い
  assert.deepEqual(pacing.findSilences([], 3, 2).map((s) => s.seconds), [3]); // 誰も話さない番組は全部が無音
});

test('重なった台詞は無音を作らない', () => {
  const lines = [{ start: 0, end: 5 }, { start: 1, end: 2 }, { start: 5.5, end: 8 }];
  assert.equal(pacing.findSilences(lines, 8, 2).length, 0);
});

test('無音の警告は「ここで N 秒の無音があります」と事実だけを言い、話題を挙げない', () => {
  const [gap] = pacing.findSilences([{ start: 0, end: 1 }, { start: 3.4, end: 5 }], 5, 2);
  const warning = pacing.silenceWarning(gap);
  assert.match(warning, /ここで 2\.4 秒の無音があります/);
  assert.match(warning, /pass はしない/);
  // 誘導しない（docs/LLM_CONTENT.md §1）: 話題の例・順位を書かない
  assert.doesNotMatch(warning, /例|話題|経歴|戦績|体格|背景|作戦|因縁|大事/);
});

test('config の閾値は 2 秒以内で警告する（声が 2 秒より前に出る）', () => {
  assert.ok(config.maxSilenceSeconds <= 2.0);
  assert.ok(config.silenceSeconds < config.maxSilenceSeconds);
});

test('席の説明と問いに、話題の例・大事さの札を入れない', () => {
  const run = fs.readFileSync(path.join(__dirname, 'run.js'), 'utf8');
  assert.doesNotMatch(run, /大事な順の目安|大事さの札|fillTopics|話題を探して/);
});
