# roster-report

名簿の総当たり（組ごとの勝率）と、技ごとの間合い・得意 / 不得意の距離を測って、ゲームの画像入りのレポート（claude.ai の Data 型ダッシュボード）にする道具。

**手順・定義・考察の書き方は [docs/ROSTER_REPORT.md](../../docs/ROSTER_REPORT.md)。** 短い流れ:

```bash
RUN=media_tmp/roster-$(date +%Y-%m-%d)
bash tools/roster-report/run-raytrek.sh $RUN          # raytrek で総当たり + 間合い（約 2 時間、バックグラウンドで）
bash tools/roster-report/shots.sh $RUN                # 肖像と試合のコマ（このデスクトップ、約 15 分）
node tools/roster-report/build-data.mjs $RUN          # → $RUN/data/*.csv
node tools/roster-report/analyze.mjs $RUN             # 考察の材料
# $RUN/shots/sheet_*.png を見て $RUN/choices.json を書く
node tools/roster-report/build-images.mjs $RUN $RUN/choices.json
# $RUN/findings.html（考察）を書く（examples/2026-11-18/findings.html が形）
node tools/roster-report/build-page.mjs $RUN
node tools/roster-report/preview.mjs $RUN             # http://localhost:8765/ で確認
# 公開: docs/ROSTER_REPORT.md §2.1（make-writes.mjs）
```
