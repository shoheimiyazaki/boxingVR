// 選手の資料（fighters.md）に入れる「本日のプログラムとこの試合に懸かるもの」の節。
// 撮影のときに中継の記録へ書いた節（events.programBriefing。TvProgram.Briefing が作る: 懸かるもの・王座・勝者が得るもの・済んだ試合）をそのまま使う。
// 前に撮った記録（その節が無い）は、札のこの試合の行（stake・next・belt）から短く作る。
// 懸かるものを渡さないと、王座決定戦のメインで実況が一度も王座に触れなかった（2026-11-19 の WVC の番組）。
'use strict';

/** 節の行（末尾に空行）。何も懸かっていなければ空の配列。 */
function stakesOf(events, card, key) {
  if (events && typeof events.programBriefing === 'string' && events.programBriefing.trim()) {
    return [...events.programBriefing.replace(/\r/g, '').trimEnd().split('\n'), ''];
  }
  const index = /^b(\d+)$/.exec(key || '');
  const bout = index && card && Array.isArray(card.bouts) ? card.bouts[Number(index[1]) - 1] : null;
  if (!bout) return [];
  const lines = [];
  if (bout.stake) lines.push(`- この試合に懸かるもの: ${bout.stake}`);
  if (bout.belt) lines.push('- 王座（チャンピオンベルト）が懸かった試合。勝った方がベルトを巻く');
  if (bout.next) lines.push(`- 勝った方が得るもの: ${bout.next}`);
  return lines.length ? ['## この試合に懸かるもの', '', ...lines, ''] : [];
}

module.exports = { stakesOf };
