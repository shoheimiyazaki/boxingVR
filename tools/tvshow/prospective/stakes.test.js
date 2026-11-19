// node --test tools/tvshow/prospective/stakes.test.js
'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { stakesOf } = require('./stakes');

test('中継の記録に書いた節があれば、そのまま使う', () => {
  const events = { programBriefing: '## 本日のプログラムとこの試合に懸かるもの\r\n\r\n- この試合に懸かるもの: WVC 世界王座決定戦\r\n' };
  assert.deepEqual(stakesOf(events, { bouts: [] }, 'b2'), [
    '## 本日のプログラムとこの試合に懸かるもの', '', '- この試合に懸かるもの: WVC 世界王座決定戦', '',
  ]);
});

test('節の無い前の記録は、札のこの試合の行（stake・belt・next）から作る', () => {
  const card = { bouts: [
    { red: 'albert', blue: 'ramon', stake: '勝者は王座挑戦者の候補へ' },
    { red: 'marcus', blue: 'dmitri', stake: 'WVC 世界王座決定戦', next: 'WVC 世界王座獲得', belt: true },
  ] };
  const lines = stakesOf({}, card, 'b2');
  assert.equal(lines[0], '## この試合に懸かるもの');
  assert.ok(lines.includes('- この試合に懸かるもの: WVC 世界王座決定戦'));
  assert.ok(lines.some((l) => l.includes('チャンピオンベルト')));
  assert.ok(lines.includes('- 勝った方が得るもの: WVC 世界王座獲得'));
  assert.ok(!stakesOf({}, card, 'b1').some((l) => l.includes('ベルト')), '王座の懸からない試合にベルトを書かない');
});

test('何も懸かっていなければ何も書かない', () => {
  assert.deepEqual(stakesOf({}, { bouts: [{ red: 'a', blue: 'b' }] }, 'b1'), []);
  assert.deepEqual(stakesOf({}, { bouts: [] }, 'b3'), []);
});
