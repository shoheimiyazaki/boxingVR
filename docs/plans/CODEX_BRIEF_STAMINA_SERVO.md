# 指示書: サーボの仕事の数え方を直す（何もしなくてもスタミナが減る）

## 症状（ユーザーの報告と、Claude の測定）

- ラウンドの始め（生成直後）に、入力なしでもスタミナが 100 % → 66〜79 % へ落ち、心肺の戻りが 4〜5 秒止まる。
- 剣・刀を持つと、入力なしで 10 秒でほぼ 0 % まで落ち、そのまま戻らない（`Vigor` が 0.5 前後に張り付く）。

両者とも入力なしの `IntentControl`、90 秒の測定:

| 持つ物 | 10 秒 | 20 秒以降 | サーボの仕事（静止中） | 戻り |
|---|---|---|---|---|
| グローブ | 92.8 % | 100 % | 0.41 W | 10.4 W |
| 鉄の剣 | 2.7 % | 0.8 % | 1.08 W | 1.29 W |
| 刀（両手） | 2.3 % | 0.6 % | 1.10 W | 1.21 W |
| 棍棒 | 41.7 % | 100 %（50 秒） | 0.92 W | 2.51 W |

## 原因

`games/MiniRing/Fighter.Stamina.cs` の `UpdateStamina` が、解いた後のサーボの仕事を
`min(Reserved, 力積 × 解く前の相対角速度 + 0.5 × 力積² × 最大の逆慣性)` で精算している。
2 乗の項は取り置き（`ReserveServo`）のための安全側の上限で、静止して武器を支えるだけ（角速度がほぼ 0、力積は重さを支える分）でも力積² に比例した「仕事」を数える。
生成直後に姿勢が落ち着くときは力積が大きく 16 J（蓄え 90 J の 18 %）になる。
この見かけの仕事率が `RecoveryPower = 15 W × exp(−(仕事率 / 0.7 W)²)` を潰し、戻りが止まる（武器を持つと静止でも 1 W を超えるので戻りがほぼ 0 のまま）。

## 決めた直し方（これ以外に広げない）

1. **精算（`UpdateStamina`）を実際の速さで数える**: サーボ 1 つの仕事 = `min(Reserved, 力積 × max(解く前の相対角速度, 解いた後の相対角速度))`。
   2 乗の項を精算から外す。解いた後の相対角速度は `UpdateStamina` の時点の `|ω_a − ω_b|`。そのために `ServoWork` に 2 つの体の id を持たせる。
   - 物理の根拠: 拘束の力積 J がした仕事は J ·（前後の速度の平均）以下。向きは取れない（`GetConstraintImpulseMagnitude` は大きさだけ）ので、前後の速さの大きい方を掛けて安全側にする。止まった体を加速した分は「後の速さ」に出るので、v = 0 を抜け道にはできない。
2. **取り置き（`ReserveServo`）はそのまま**: 解く前は後の速さが分からないので、2 乗の上限で取り置くのは正しい。
3. 場合分け（生成直後だけ数えない・武器を持つ時だけ割り引く など）は入れない（CLAUDE.md §1.3）。調整値も足さない。

## 完了の条件

1. 新しいテスト（`tests/MiniRing.Tests/` の新しいファイル、名前に Probe を入れない）: `BoxingGame` で両者とも入力なしの `IntentControl`（`BoxingCorners.Default with { Player = _ => new IntentControl(), Cpu = _ => new IntentControl(), PlayerWeapon = ..., PlayerTwoHanded = ..., UseWeaponBots = true }`）、60 Hz で `Step(new BoxingInput(default), 1f/60f)`:
   - グローブ・鉄の剣・刀（両手）のそれぞれで、60 秒後のスタミナが 0.95 以上。
   - 開始から 5 秒の間の最低のスタミナを測って報告し、測った値から余裕を持った下限をテストにする（物理の説明をコメントに）。
   重いので 1 本のテストに詰めすぎない（各 60 秒の試合 3 つ）。
2. 既存のスタミナのテスト（`StaminaBudgetTests`・`StaminaRuleTests`・`StaminaRestDecisionTests` ほか）が通ること。特に
   `EmptyReserve_RestingRecoversButRepeatedPunchesDoNot`（連打で 10 % 未満）と `EmptyReserve_TenthPunchLosesSpeedAndImpactEnergy_WhileFullReserveDoesNot` は、直しで連打の消費が減って落ちる恐れがある。
   **落ちたらテストを緩めず、測った数字（連打 10 秒後のスタミナ、10 発目の速さ・エネルギー）を報告して止まる**（調整は Claude が決める）。
3. `Fighter.PhysicsVersion` を 30 に上げ、履歴の先頭に 1 行（「30: サーボの仕事の精算を力積 × 前後の相対角速度の大きい方にし、2 乗の上限は取り置きだけに使う」）。
4. `FighterTuning` の `StaminaCapacity` の上のコメント「空から構えて 10 秒休むと約 72 % 戻る」を測り直して直す（`EmptyReserve_RestingRecovers...` の rest の値）。
5. 文書: `CLAUDE.md` §8「スタミナと臓器の効き方」の「サーボは力積の大きさ・相対角速度・最大逆慣性による安全側の仕事推定」の文を、新しい数え方（取り置きは上限、精算は力積 × 前後の速さの大きい方）に直す。`docs/LEARNING_PLAN.md` にスタミナの仕組みの記述があれば合わせる。
6. 全テスト: `dotnet test tests/MiniRing.Tests --filter "FullyQualifiedName!~Probe"`。`ReplayTests` も含めて通ること。README のテスト件数を合わせる。

## 作業の場所と決まり

- **この worktree（`stamina-wt`）だけで作業する**。本体の作業ツリー（`E:\repos\ClaudeProjects\myvrgame1`）には触らない（別のセッションが編集中）。
- コミットしない。終わったら、変えたファイルの一覧・テストの結果（件数・落ちたもの）・測った数字を報告する。
- このマシンは RAM が少ない。テストは 1 つずつ流す（並列に 2 つ流さない）。
