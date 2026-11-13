打撃音の集約、ダメージに応じた音量、形状・支持に応じた金属音の減衰を実装しました。

- 12秒の発音元：グローブ99→47声、メリケンサック234→76声
- メリケンサックの金属音：−20 dB減衰まで0.66→0.02秒
- 全テスト436件成功。レビュー指摘も修正済み
- 物理変更・コミット・プッシュなし

音付き比較動画（前半BEFORE／後半AFTER）：

- [グローブ](docs/video/sound_v6_glove_compare.mp4)
- [メリケンサック](docs/video/sound_v6_knuckles_compare.mp4)
- [日本刀](docs/video/sound_v6_sword_compare.mp4)

詳細は [docs/SOUND.md](docs/SOUND.md)。波形と映像は確認済みですが、聴覚での自然さ・実機出力は未確認です。ライブ再生には約60〜83 msの集約待ちが残ります。