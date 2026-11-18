// プロスペクティブ制の無音の見つけ方と警告（run.js から使う。node --test tools/tvshow/prospective/pacing.test.js で確かめる）。
//
// 台詞と台詞の間（と番組の頭・終わり）で、誰の声も無い区間を見つける。maxSilenceSeconds 秒以上あれば、台本を組む側が
// LLM に「ここで N 秒の無音があります」と警告して、埋める台詞を書かせる。何を話すか（話題・大事さ・面白さ）は話し手が
// 決める: 警告に話題の例や順位は書かない（docs/LLM_CONTENT.md §1「誘導しない」、2026-11-19 ユーザーの指定）。
'use strict';

/**
 * 声の無い区間 [{from, to, seconds}]（minSeconds 秒以上だけ）。lines は {start, end}（重ねた感嘆 overlap は数えない）。
 * 番組の頭（0 から最初の台詞）と終わり（最後の台詞から length）も数える。
 */
function findSilences(lines, length, minSeconds) {
  const spans = lines.filter((l) => !l.overlap && l.end > l.start).map((l) => [l.start, l.end]).sort((a, b) => a[0] - b[0]);
  const out = [];
  let at = 0;
  for (const [s, e] of spans) {
    if (s - at >= minSeconds - 1e-9) out.push({ from: at, to: s, seconds: s - at });
    at = Math.max(at, e);
  }
  if (length - at >= minSeconds - 1e-9) out.push({ from: at, to: length, seconds: length - at });
  return out;
}

/** 無音の警告（LLM への問いに入れる文）。事実（どこに何秒の無音があるか）と、埋める台詞を話すことだけ。 */
function silenceWarning(silence) {
  const s = silence.seconds.toFixed(1);
  return `警告: ここで ${s} 秒の無音があります（${silence.from.toFixed(1)}〜${silence.to.toFixed(1)} 秒、誰も話していません）。この間を埋める台詞を話してください（pass はしない）。`;
}

module.exports = { findSilences, silenceWarning };
