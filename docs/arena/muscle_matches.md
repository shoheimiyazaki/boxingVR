# 拳を引く筋肉の打ち合い（2026-11-20、PhysicsVersion 65、ブランチ claude/hill-muscle）

（測ったときの版の番号は 63。統合役の指定で 65 に付け直した: 62・63 は master、64 は neck-muscle。confirm.jsonl の versions は測ったときの "physics 63" のまま）

測り方: `fistmatch --seeds 4-15 --seconds 60`（champion:swarmer と counter:slugger、レフェリーあり）。当たった瞬間の拳 = 当たった歩の始めの拳の速さ、打ち出しの最高速 = 技の打ち出しから終わりまでの拳の速さの最大、光が出る = ダメージ 3 超、倒れた = 立っている状態から倒れた状態になった回数（1 秒以内に打たれていないものが「打たれずに」）、ダウン = 審判が数えた倒れ。赤の勝ち = 与えたダメージが多い。技ごとの表の「-」は技を出していない間の当たり（構え・押し合い）。
「62 相当」= 枝の元の体、「拳 1 点」= 07d5fa6（レビューの直し前）、「関節ごと」= 採る形。

| 種 | 版 | 当たり | 当たった瞬間の拳 p50 / p90 [m/s] | 当たる速さ p50 | 打ち出しの最高速 p50 / p90 | 乗る質量 p50 [kg] | エネルギー平均 [J] | ダメージ平均 | 光が出る割合 | 光の平均 | あざ（1 人・1 試合） | 倒れた回数 | 打たれずに倒れた | ダウン | champion:swarmer 赤の勝ち（ダメージ差） | counter:slugger 赤の勝ち（ダメージ差） |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 4〜7 | 62相当（前） | 912 | 0.97 / 2.78 | 1.87 | 2.45 / 3.96 | 0.49 | 1.38 | 2.67 | 28 % | 0.073 | 188 | 55 | 12 | 38 | 4/4（+48） | 0/4（-58） |
| 4〜7 | 65 拳1点（最初の版） | 682 | 0.90 / 2.34 | 1.60 | 2.21 / 3.14 | 0.65 | 1.25 | 3.04 | 30 % | 0.093 | 150 | 48 | 7 | 43 | 4/4（+87） | 0/4（-43） |
| 4〜7 | 65 関節ごと（採る版） | 802 | 0.87 / 2.30 | 1.45 | 2.11 / 3.09 | 0.60 | 0.91 | 2.54 | 24 % | 0.062 | 159 | 46 | 13 | 39 | 4/4（+41） | 0/4（-44） |
| 8〜15 | 62相当（前） | 1619 | 1.07 / 3.13 | 1.93 | 2.74 / 4.08 | 0.49 | 1.41 | 2.80 | 29 % | 0.079 | 202 | 81 | 14 | 69 | 8/8（+69） | 0/8（-56） |
| 8〜15 | 65 拳1点（最初の版） | 1573 | 0.85 / 2.33 | 1.56 | 2.14 / 3.08 | 0.63 | 1.12 | 2.86 | 28 % | 0.083 | 220 | 95 | 16 | 78 | 8/8（+66） | 0/8（-62） |
| 8〜15 | 65 関節ごと（採る版） | 1530 | 0.86 / 2.36 | 1.45 | 2.21 / 3.16 | 0.60 | 0.98 | 2.65 | 26 % | 0.069 | 154 | 86 | 21 | 76 | 7/8（+47） | 0/8（-41） |

```
== 62相当（前）
  -              hits=1056 fist p50=0.50 p90=1.64 energy=0.96 flash=15%
  BodyCross      hits= 124 fist p50=2.34 p90=3.99 energy=2.33 flash=40%
  BodyJab        hits= 119 fist p50=1.68 p90=3.59 energy=1.91 flash=39%
  Cross          hits= 287 fist p50=1.59 p90=2.94 energy=1.64 flash=41%
  Feint          hits=  20 fist p50=0.48 p90=1.05 energy=0.56 flash=10%
  Jab            hits= 333 fist p50=1.39 p90=3.01 energy=1.48 flash=38%
  LeadBodyHook   hits= 151 fist p50=2.14 p90=3.73 energy=2.34 flash=37%
  LeadHook       hits= 224 fist p50=1.50 p90=3.10 energy=1.34 flash=33%
  LeadUppercut   hits=   8 fist p50=1.89 p90=3.11 energy=2.26 flash=63%
  Overhand       hits=  47 fist p50=1.42 p90=2.54 energy=0.84 flash=53%
  RearBodyHook   hits=  39 fist p50=3.13 p90=4.31 energy=2.59 flash=31%
  RearHook       hits=  59 fist p50=1.49 p90=2.93 energy=1.21 flash=36%
  RearUppercut   hits=  64 fist p50=1.94 p90=3.78 energy=2.46 flash=30%
== 65 拳1点（最初の版）
  -              hits= 982 fist p50=0.51 p90=1.45 energy=0.86 flash=16%
  BodyCross      hits=  99 fist p50=1.79 p90=3.11 energy=2.26 flash=57%
  BodyJab        hits= 141 fist p50=1.42 p90=2.76 energy=1.38 flash=38%
  Cross          hits= 246 fist p50=1.38 p90=2.52 energy=1.54 flash=45%
  Feint          hits=  11 fist p50=0.56 p90=0.80 energy=0.81 flash=9%
  Jab            hits= 287 fist p50=1.23 p90=2.31 energy=1.07 flash=34%
  LeadBodyHook   hits= 130 fist p50=1.64 p90=2.70 energy=1.71 flash=39%
  LeadHook       hits= 173 fist p50=1.01 p90=2.18 energy=0.94 flash=29%
  LeadUppercut   hits=   9 fist p50=0.95 p90=2.85 energy=1.70 flash=56%
  Overhand       hits=  48 fist p50=1.27 p90=2.12 energy=1.08 flash=38%
  RearBodyHook   hits=  29 fist p50=1.69 p90=2.71 energy=1.95 flash=28%
  RearHook       hits=  38 fist p50=0.84 p90=2.04 energy=1.11 flash=45%
  RearUppercut   hits=  62 fist p50=1.28 p90=2.74 energy=1.60 flash=39%
== 65 関節ごと（採る版）
  -              hits= 995 fist p50=0.48 p90=1.41 energy=0.70 flash=13%
  BodyCross      hits= 116 fist p50=1.77 p90=2.93 energy=1.53 flash=46%
  BodyJab        hits= 128 fist p50=1.82 p90=3.13 energy=1.51 flash=35%
  Cross          hits= 260 fist p50=1.42 p90=2.43 energy=1.30 flash=45%
  Feint          hits=  27 fist p50=0.70 p90=1.13 energy=0.66 flash=4%
  Jab            hits= 295 fist p50=1.09 p90=2.28 energy=0.97 flash=29%
  LeadBodyHook   hits= 140 fist p50=1.69 p90=2.66 energy=1.19 flash=30%
  LeadHook       hits= 177 fist p50=1.19 p90=2.19 energy=0.82 flash=31%
  LeadUppercut   hits=   9 fist p50=1.12 p90=2.03 energy=1.09 flash=56%
  Overhand       hits=  38 fist p50=1.21 p90=1.89 energy=0.78 flash=37%
  RearBodyHook   hits=  31 fist p50=2.33 p90=3.02 energy=1.10 flash=23%
  RearHook       hits=  44 fist p50=1.13 p90=2.39 energy=0.85 flash=25%
  RearUppercut   hits=  72 fist p50=1.42 p90=2.57 energy=1.38 flash=26%
```


比べ直し（使っていない種 8〜15 × 2 組 = 16 試合、試合ごとの対。`docs/arena/confirm.jsonl` に 3 行）:

- 比べ直し（65関節ごと − 62相当、種 8-15 × 組、試合ごとの対）拳 1 発のエネルギーの平均 [J]: n=16 平均の差 -0.414 ± 0.053（95% 区間 -0.527〜-0.301）符号検定 p=0.0000（+0/-16/=0）ρ=0.64 → B が上
- 比べ直し（65関節ごと − 62相当、種 8-15 × 組、試合ごとの対）光が出る割合 [%]: n=16 平均の差 -2.252 ± 1.306（95% 区間 -5.035〜0.531）符号検定 p=0.2101（+5/-11/=0）ρ=0.62 → 差なし
- 比べ直し（65関節ごと − 62相当、種 8-15 × 組、試合ごとの対）打ち出しの最高速 p50 [m/s]: n=16 平均の差 -0.546 ± 0.061（95% 区間 -0.676〜-0.416）符号検定 p=0.0000（+0/-16/=0）ρ=0.75 → B が上

## 残った問題（全テスト、tower32、1288 件中 1255 通過・23 落ち・10 実行しない。チェックサムを取り直した後）

枝の元（punch-path）で既に落ちていたのは TonfaTests.ScriptedStraightPunch と BuildReadTests の 2 件。この版で新しく落ちるのは 21 件。テストは緩めていない。

| まとまり | テスト | 中身 | 見立て |
|---|---|---|---|
| 拳が遅くなった | StaminaRushTests（連打の始め 10 秒の拳 2.11 < 2.2 m/s）、StaminaBudgetTests（空の腕の 1 発目が満タンの 0.70 倍 > 0.65） | Hill の速さの関係で、疲れていない拳がもう速さで頭打ち。疲れで落ちる割合が小さく見える | 物理として説明できる。採るなら期待値を測り直す |
| 一撃で倒れる | EvasionBasicsTests（守らない相手が 1 発目で倒れる、8 種とも） | あごに当たった後も届かない点へ全力の指令で押し続け、頭が回る（punchlab の頭の奥 5・10 cm で頭の回転 7 → 31 rad/s） | 物理の結果だが、ゲームとして強すぎる。打ち抜いた後に指令を戻す経路（技の側）か、当たってからの筋の反射が要る |
| 蹴り・肘 | KickShinTests 2 件、KickContactTests（かかと落としの後に立っていられない）、ElbowContactTests（突き上げの肘が当たらない） | 蹴る間の腕（釣り合い）・肘の経路が変わった | 調べていない |
| 武器 | SwordTests・JointBridgeTests 2 件・BojutsuTests（返し）・ShieldTests（自傷 183 > 150）・WeaponGraspTests・TonfaTests（剣を受ける木） | 武器の振りは速さの関係で遅くなり、斬れる量が減る（guardlab の受け手の死 25 → 6: `docs/arena/muscle_guardlab.md`） | 物理の結果。武器の型は前の体で決めた値なので、採るなら型の見直しが要る |
| 体の動き | StepTests（ステップ 0.431 > 0.4 m）、BootSnagTests（斜めに歩いて 3 秒止まる）、LegLossTests 2 件（腰が 0.2 mm 上がる）、GrapplingTests（崩しが 2.5 cm < 5 cm）、BotTests（屈んでいる割合 31 % > 30 %）、GroundStrikeTests（倒れた相手へ 10 秒で当てる種 1/3 < 2/3） | 止まった腕の力を内力にしたので、腕で体を支える・引く近道の力が変わった（`BodyBorrowSpeed` の近道を外すと BootSnag・LegLoss・Stick は通る） | 前の値が近道の力の上で決めた値。採るなら一つずつ見直す |

連打（RushPhysicsProbe.RushVersusPatientOutcome、種 1〜4 × 30 秒）: 前へ押しながらの弱の連打はダウンを 8 奪い 1 奪われる、溜めた強を 1 発ずつは 3 奪い 7 奪われる（連打が強い形はこの版でも残る）。
