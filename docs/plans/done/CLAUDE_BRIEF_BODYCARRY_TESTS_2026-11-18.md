# 指示書: 体の運び（PhysicsVersion 45）で外れた 13 個のテストの原因調査と測り直し（2026-11-18）

## 目的

拳・蹴り・肘・握った武器が運ぶ運動量を、運動量が湧かない形に直した（PhysicsVersion 44・45、`docs/arena/rush_physics.md` を先に読む）。その結果、下の 13 個のテストが落ちている。
1 つずつ **原因を測って確かめ**、どちらかにする:

- (a) **物理として正しい変化**（主張が古い水増しの式に頼っていた・始めの状態がずれてぎりぎりの値が外れた）→ テストの期待値・台本を直し、**なぜ直したかを数字付きでテストのコメントに書く**。主張そのもの（何を確かめるテストか）は残す。ただ緩めるのは不可。主張を守れる形（種を足して平均で見る、台本の間合いを取り直す、比べの相手を同じ条件にそろえる等）を先に探す。
- (b) **本当の退行**（今回の変更の書き間違い・物理として起きるべきことが起きない）→ 直す前に止めて、報告に書く（直してよいのは、明らかな書き間違いだけ）。

## 軸（CLAUDE.md §1）

- §1.3: 種類での分岐・説明できない閾値を足さない。
- §1.5: 拳・蹴り・肘・武器は同じ式（`BoxingGame.BodyFactor`・`PunchMechanics.ClosingSpeed`・`LimbDrive`・`BodyCarry`）。
- §1.6: 決定的。
- **物理の式・調整値（`PunchMechanics`・`BoxingGame*.cs`・`Fighter.cs`・`FighterTuning.cs`）は変えない**。変えたくなったら、理由と測った数字を報告に書いて止める。

## 今回の変更（読んでおく所）

- `PunchMechanics.ClosingSpeed`（当たる速さ = 相対速度）、`LimbDrive`（押し出し）、`BodyCarry`（付け根の速さ ÷ 当たる速さ）。
- `BoxingGame.BodyFactor`・`PreStepVelocityAt`・`PreStepBodyVelocity`、`BoxingGame.Weapons.cs` の `Punch`・`ScoreKick`・`ScoreElbow`・握った武器の体の運動量（`carried` の所）・`MassBehind`・`KickMassBehind`・`ElbowMassBehind`。
- `FighterTuning.ConcussionGain` 0.000035 → 0.0035。
- 調べものの道具: `tests/MiniRing.Tests/RushPhysicsProbe.cs`・`PunchAnatomyProbe.cs`・`ConcussionCalibrationProbe.cs`。直す前の版（PhysicsVersion 43）の写しが `C:/Users/smiya/AppData/Local/Temp/claude/2be00268f7924743/35cca1a6-0824-4106-9ec3-a473fc652081/scratchpad/baseline` にある（git の外。テストを写して回すと前の数字が取れる。そこへ Probe を足してよい）。

## やること（この順）

1. `Punch_CounterDamage_IsPositiveWithCountersAndZeroWithout`（CmaTuneTests）: 打ち返さない受け手が 10〜17 点（打ち返す受け手 47〜99）。打ち返さない受け手の点はどの接触から来るか（構えた拳に攻め手が突っ込む・押し出し 0 で腕だけ）を確かめる。物理として正しければ、主張を「打ち返す受け手は打ち返さない受け手より桁で（何倍）大きい」の形で数字に合わせる。
2. `AKnockedDownFighter_GetsUpOnceHisHeadClears`: 起き上がりが早すぎる（downAt + 1.5 > upAt）。ダウンの原因（意識か倒れただけか）と意識の深さを測る。
3. `Punches_AreAimedWhenThrown_SoAnEarlySlipBeatsThem`（open.HeadRate > 0.5）・`PredictedInsideSlip_AvoidsALoadedRightCross_ThatReactionCannot`: 守りのドリル。始めの状態・拳の当たり方のどこが変わったか。
4. `AlternatingRush_FullForTenSeconds_ThenFadesOverTheNextTen`（StaminaRushTests）: 始めの 10 秒の拳の最高速度が 2.8 m/s を割る。押し合いが打撃にならず相手が押し下げられないので間合いが詰まり、拳が加速しきれないのでは、を確かめる。スタミナの主張（10 秒全力・次の 10 秒で落ちる）は残す。
5. 肘打ち 2 個（`EveryElbow_LandsAsAHit_ThroughTheFistImpactPath`・`AFasterElbow_CarriesMoreEnergy`、ElbowContactTests）: 始めの間合いが 1.10 → 1.13 倍に変わって届かない（版 44 から）。台本の寄り方で、肘の間合いに入れる（KickContactTests で試した「止まった後に間合いを取り直す」は蹴りでは要らなかった。肘で要るか測る）。
6. 武器（握った武器が運ぶ体の運動量が、付け根（肩）の速さの分だけになって小さくなった）:
   - `AClubBlow_CrushesMoreOfTheBody_ThanAPunchCarvesAtMost`（潰したセル > MaxCarve × 0.35）
   - `NoReferee_WeaponBotWoundsADownedOpponent`（Longsword・WoodenClub）
   - `ASwordSweepingAtTheGuard_MeetsTheWoodOfTheStick_BeforeTheFleshOfTheForearm`（TonfaTests）
   - `TechniqueWeights_DecideWhichFormsTheSwordsmanThrows`（WeaponStyleTests、used が空）
   - `TheForwardForms_HitWithTheFrontEnd_AndGaeshiAlsoWithTheRear`（BojutsuTests）
   それぞれ、版 43 の写しと今で、運んだ運動量・削ったセル・当たり方を並べる。武器の打撃の強さが全体に下がって主張が崩れたのなら、それは (a) ではなく **調整の判断が要る所**なので、数字を報告に書いて止める（`WeaponCarveScale` 等を自分で変えない）。

## 触ってよいファイル

- `tests/MiniRing.Tests/` の上の 13 個のテストのファイルと Probe（新しい Probe を足してよい）。
- 報告 `docs/plans/CLAUDE_REPORT_BODYCARRY_TESTS.md`。
- それ以外（ゲームのコード・調整値・文書）は触らない。

## 完了の条件

- 13 個それぞれについて、報告に: 原因（測った数字、版 43 と今）・(a)/(b)/調整の判断が要る・したこと。
- (a) にしたテストが通る。
- 全テスト `dotnet test tests/MiniRing.Tests -c Release --filter "FullyQualifiedName!~Probe"` の結果を報告に貼る（落ちているものは、上のどれかとして説明されている）。
- コミットしない。git stash を使わない。

## 版

上げない（物理・判断を変えないので）。
