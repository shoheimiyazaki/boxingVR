using System.Text.Json;

namespace MiniRing.Bots;

/// <summary>
/// ボットの戦い方（調整できる値の全部）。値は不変で、変えるときは <c>with</c> で新しいスタイルを作る。
/// <para>
/// <b>人並みの制約</b>: 反応時間・守りの成功率・手の速さには上限・下限がある（<see cref="Validated"/>）。
/// 改善（PDCA）は「反射神経を超人にする」ではなく、技の選び方・間合い・守りの選び方で強くならなければならない。
/// </para>
/// </summary>
public sealed record BotStyle
{
    /// <summary>反応時間の下限 [s]（人の目と手の反応のおおよその下限。人形の単位の 0.12 s を相似で換算）。</summary>
    public const float MinReactionTime = 0.2014f;

    /// <summary>飛んでくる拳に守りを出せる確率の上限（読み違い・見落としがある）。</summary>
    public const float MaxDefenseRate = 0.85f;

    /// <summary>手の速さの倍率の上限（経路の時間を縮める。物理の目標速度の上限もある）。</summary>
    public const float MaxPunchSpeed = 1.15f;

    public string Name { get; init; } = "boxer";

    /// <summary>右が前（サウスポー）。</summary>
    public bool Southpaw { get; init; }

    // ── 攻め ──

    /// <summary>手数（0 = 待つ … 1 = 休まず攻める）。攻撃の間隔を決める。</summary>
    public float Aggression { get; init; } = 0.5f;

    /// <summary>
    /// ペース配分（docs/LEARNING_PLAN.md の A1）: 自分のスタミナ（<see cref="FighterSense.SelfStamina"/>）がこれを下回ると、手数をなめらかに落とす。
    /// スタミナ 0 で手数は <c>Aggression × (1 − PaceDrop)</c>、攻撃の間隔はそのぶん空き、空いた間に心肺がスタミナを戻す。
    /// </summary>
    public float PaceKnee { get; init; } = 0.7f;

    /// <summary>
    /// スタミナが尽きたときに手数を落とす割合（0 = 配分しない … 1 = 尽きたら攻めない）。既定は 0: 今のスタミナは腕の力にしか出ず、減っても
    /// 拳の数・当たり・ダメージがほとんど変わらないので（A0）、配分しても手数が減るだけで得が無い（A2、docs/LEARNING_PLAN.md）。
    /// </summary>
    public float PaceDrop { get; init; }

    /// <summary>この蓄えより少ないほど休息を優先。空で 1、この値で 0 へなめらかに変え、相手の届く範囲の外へ出る。</summary>
    public float RestBelow { get; init; } = 0.5f;

    /// <summary>
    /// 休みたい度合いが 1 のときに残す手数の割合（2026-11-17）。スタミナは臨界パワー（基準体格で 9 W）より下の仕事なら戻るので、休む間も
    /// 足を使い（回り込みは休みで減らさない。この値に依らない）、軽く打ち続けられる。0 は休む間は打たない（以前の形。以前は回り込みも止めて棒立ちが目立った）。
    /// </summary>
    public float RestTempo { get; init; } = 0.35f;

    /// <summary>
    /// 拳の狙いを相手の動きの先へ延ばす割合（0 = 見えた所へ打つ … 1 = 見えるまでの遅れ・溜め・拳が届くまでの時間ぶん、見えた体（胴）の速さで先へ延ばす。
    /// 頭が体の上で揺れる分は延ばさない: AttackObservation）。
    /// 読みの上手さ（判断の値）。
    /// </summary>
    public float AimLead { get; init; } = 1f;

    /// <summary>
    /// 構えの手の肘の締め（0 = 肘を外へ逃がす … 1 = 脇を締めて肘を脇腹の前へ。<see cref="HandCommand.Tuck"/>）。胴へ来る拳を守る間は 1。
    /// 胴への打撃は臓器の打撲で息が詰まってスタミナを失う（1 発 17 J ほど）が、肘・グローブで受ければ 2 J ほど（StaminaMatchProbe、2026-11-17）。
    /// </summary>
    public float ElbowTuck { get; init; } = 0.5f;

    // ── 間合いの層（docs/LEARNING_PLAN.md の R、RangeSense）──

    /// <summary>相手の拳が届くとみなす、相手の肩から自分の頭までの距離 [腕の長さ]（踏み込み・体の傾きを含む）。危険度の物差し。</summary>
    public float RangeThreat { get; init; } = 1.2f;

    /// <summary>危険度に応じて構えを上げる度合い（0 = 上げない … 1 = 危険度 1 で目の高さまで顔を覆う）。</summary>
    public float RangeGuard { get; init; }

    /// <summary>
    /// 疲れたときに構えを下げる量 [腕の長さ]（スタミナ 0 のとき。0 = 下げない）。両手を上げて保つにも酸素が要るので、息が上がったら節約して手を下げる
    /// （2026-11-22、慢性の疲れの見た目。入力元の判断であって、体の強さを変えるものではない）。下げた手は頭を守りにくい。
    /// </summary>
    public float TiredGuardDrop { get; init; } = 0.25f;

    /// <summary>危険度に応じて頭を大きく・細かく動かす度合い（的を絞らせない。<see cref="HeadMovement"/> に足す）。</summary>
    public float RangeHeadMove { get; init; }

    /// <summary>危険度 1 の所にいる間、当てずっぽうで回避を入れる頻度 [回/s]（来る拳を見ずに、来ると思って動く）。</summary>
    public float RangeGuess { get; init; }

    /// <summary>
    /// 自分から打っていない間、危険度に応じて外へ出る強さ（足運びの前後の指令に足す、0..1）。危ない所に居続けた時間（さらされ）が長いほど
    /// 強く出る（危険度 × (1 + さらされ / <see cref="RangeSense.ExposureScale"/>)）。
    /// </summary>
    public float RangeExit { get; init; }

    /// <summary>
    /// 普段の立ち位置を、相手の拳の届かない所（危険度 0: 相手の届く長さ <see cref="RangeThreat"/> ＋ 境目の幅の外）へ寄せる度合い
    /// （0 = <see cref="NeutralRange"/> のまま … 1 = 届かない所に立ち、打つときだけ入る）。
    /// </summary>
    public float RangeKeepOut { get; init; }

    /// <summary>
    /// 危ない所に何もせず居続けることを、打って解消する強さ（0 = しない）。危険度 × (1 + さらされ / <see cref="RangeSense.ExposureScale"/>) × この値だけ、次の拳までの間（攻撃の間隔）を
    /// 早く縮める。下がる（<see cref="RangeExit"/>・<see cref="RangeKeepOut"/>）と違って消極的にならない（R3: 下がる版は試合がつまらなかった）。
    /// </summary>
    public float RangePress { get; init; }

    // ── 体格を読む層（BuildRead。見えている寸法の比から連続に出す。体格が同じなら効かない: docs/BOT_PDCA.md「体格に応じた戦い方」）──
    // 既定値の根拠（2026-11-16、docs/arena/build_tactics_*.md、拳のキャラ 9 人。層を入れた 1 人 対 入れない全員、40 秒）: 立ち位置（BuildStandOff 0.8・BuildInside 0.5）は、
    // 長い腕が自分だけ届く帯にいる割合を 22→35 %（marcus）、短い腕が相手だけ届く帯にいる割合を 24→15 %（akira）に動かし、使っていない種 7 で 56.6 %（144 試合）、種 1 で 48.6 %（72 試合）。
    // 守りの重み（BuildDuck 0.5）・胴の重み（BuildAimBody 2）は点に出ない（種 1 で 46.5 %、種 7 で 53.8 %）が害も無いので入れた（見た目の道理: 低い頭は潜り、高い相手は胴が近い）。
    // 打ち合う気（BuildPush）は点に出ず（種 1: 0.3 で 46.5 %・0.6 で 50.7 %、種 7: 0.4 を足して 51.7 %）、重い体はスタミナが先に尽きる（champion の marcus は平均 0.22）ので手数を増やすほど不利になりうる: 既定は 0（機構は残す）。

    /// <summary>
    /// 立ち位置の腕の長さの読み方（0 = 自分の腕だけで測る（従来）… 1 = 相手の腕で測る）。普段の立ち位置・打ちに入る間合い・休む間合いを
    /// ρ^(−これ) 倍する（ρ = 自分の腕 / 相手の腕）。長い腕は相手の届かない所の端（自分だけ届く帯）へ詰め、短い腕は相手だけが届く帯にいつかず、
    /// 相手の届く所の外から打ちに入る。
    /// </summary>
    public float BuildStandOff { get; init; } = 0.8f;

    /// <summary>
    /// 腕が相手より短いとき、打つ間合い（<see cref="AttackRange"/>）を ρ^これ 倍して相手の腕の内側へ深く入る（0 = 従来）。長い方は変えない（min(ρ, 1)）。
    /// </summary>
    public float BuildInside { get; init; } = 0.5f;

    /// <summary>
    /// 重さの比（H³M² の比 μ = 自分 / 相手、<see cref="BodyBuild.MassRatio"/>）で打ち合う気を変える強さ（0 = 従来）。攻めの間隔が exp(これ × ln μ) 倍の速さで縮み、
    /// 打ったあとに離れる確率が exp(−これ × ln μ) 倍になる（重い方は押し負けず前で打ち合い、軽い方は打って離れる）。
    /// </summary>
    public float BuildPush { get; init; }

    /// <summary>
    /// 頭の高さの比（自分 / 相手の身長）で守りの選び方の重みを変える強さ（0 = 従来）。自分の頭が低いほど潜る守り（ダッキング・ウィービング・ロール）の重みが
    /// exp(−これ × ln η) 倍、高いほど引く守り（スウェー・ステップバック）の重みが exp(これ × ln η) 倍（η = 自分の身長 / 相手の身長）。守りの経路は変えない。
    /// </summary>
    public float BuildDuck { get; init; } = 0.5f;

    /// <summary>
    /// 相手が自分より背が高いほど、胴（肝臓・みぞおち・心臓）を狙う重みを足す強さ（0 = 従来）。足す重みの合計 = これ × max(0, −ln η)（真ん中の重みは 1）。
    /// 狙いの位置そのものは今の見た位置から出す。
    /// </summary>
    public float BuildAimBody { get; init; } = 2f;

    // ── 弱った相手を詰める層（HurtRead: 相手の構えの下がりと自分の手応えから開き 0..1 を読み、立ち回りを連続に変える。0 なら効かない）──
    // 既定値の根拠（2026-11-16、rosterleague、拳のキャラ 8 人・各組 2 試合 × 60 秒、層を入れた 1 人 対 入れない全員: docs/arena/finish_*.md）: 基準では小柄な swarmer（ramon）が
    // 相手を弱らせた間（相手の Faintness > 0.2）に 1 試合 20 秒もありながら、その間に与えた量は 8.0/秒（大きい 2 人は 16〜23/秒）で、腕の長さの 1.49 倍の所に立っていた。
    // Tempo 1.5・Press 1.5・Spend 0.6 で、使った種 1 の平均の勝率 50 → 58 %、使っていない種 7 で 50 → 57 %（ramon 29 → 50 %・akira 14 → 36 %・yoandry 50 → 71 %）。
    // 時間切れの近づきから今いる所で打つ案（v2）は種 1 で 58 %・種 7 で 55 %（ramon 18 %）で採らなかった。

    /// <summary>構えの下がり（頭と両拳の高さの差 ÷ 相手の腕の長さ）が普段よりこれだけ下がってから開きと読み始める（測った健康な揺らぎの上）。</summary>
    public float HurtSagStart { get; init; } = 0.06f;

    /// <summary>構えの下がりが <see cref="HurtSagStart"/> からこれだけ増えると開き 1（意識が薄れると普段より 0.17〜0.23 下がる。HurtCueProbe）。</summary>
    public float HurtSagSpan { get; init; } = 0.12f;

    /// <summary>手応えの尺度（当てたダメージの和。これで開き 63 %。重い一撃 15 前後が 2 発で 6 割）。</summary>
    public float HurtFeltScale { get; init; } = 30f;

    /// <summary>手応えを忘れる時定数 [s]。</summary>
    public float HurtMemory { get; init; } = 4.196f;

    /// <summary>開きに応じて攻めの間隔を縮める強さ（間隔の縮む速さが 1 + これ × 開き 倍）。0 = 従来。</summary>
    public float FinishTempo { get; init; } = 1.5f;

    /// <summary>
    /// 開きに応じて詰める強さ: 普段の立ち位置を打つ間合いへ（開き × これ の割合で、1 まで）寄せ、危ない所から出る動き（<see cref="RangeExit"/>）を
    /// （1 − 開き × これ）倍に弱める。0 = 従来。
    /// </summary>
    public float FinishPress { get; init; } = 1.5f;

    /// <summary>開きに応じて休みを後回しにする強さ（休む度合いを 1 − 開き × これ 倍）。蓄えが無ければ体は動かない（物理が上限）。0 = 従来。</summary>
    public float FinishSpend { get; init; } = 0.6f;

    /// <summary>
    /// 開きの読みに、相手の見える息の上がり（<see cref="FighterSense.OpponentWinded"/>、<see cref="TiredLook"/> が描く量）をどれだけ足すか（0..2）。
    /// 開き = 1 − (1 − 構え)(1 − 手応え)(1 − これ × 息)(1 − <see cref="HurtInjury"/> × 傷み)。0 = 従来（2026-11-24 に足した）。
    /// </summary>
    public float HurtWinded { get; init; }

    /// <summary>開きの読みに、相手の見えている傷み（<see cref="FighterSense.OpponentInjury"/>: 折れて垂れた腕・削れた筋）をどれだけ足すか（0..2）。0 = 従来。</summary>
    public float HurtInjury { get; init; }

    /// <summary>
    /// フィニッシュのコンビネーション（型の名前 → 重み、キャラの教育）。開きが大きいほど、普段の表（<see cref="ComboWeights"/>）でなくこの表から選ぶ
    /// （割合 = 開き × <see cref="FinishComboShare"/>。場合分けでなく、開きに比例した混ぜ方）。空なら普段の表だけ。
    /// </summary>
    public IReadOnlyDictionary<string, float> FinishCombos { get; init; } = new Dictionary<string, float>();

    /// <summary>フィニッシュの表から選ぶ割合の上限（開き 1 のとき、0..1）。打ち終えて離れる・回り込む割合も（1 − 開き × これ）倍に減らす。0 = 従来。</summary>
    public float FinishComboShare { get; init; }

    /// <summary>開きに応じて連打の拳と拳の間を詰める強さ（間 × (1 − 開き × これ)、0..0.8）。最初の拳の溜めは変えない（威力は体のバネから）。0 = 従来。</summary>
    public float FinishGap { get; init; }

    // ── 学ぶ（docs/LEARNING_PLAN.md の B1）──

    /// <summary>守りの効き目を経験で覚え、選び方を変えるか（<see cref="DefenseMemory"/>）。</summary>
    public bool LearnDefense { get; init; }

    /// <summary>防御ドリルで測った効き目（事前分布）を、何回ぶんの経験として信じるか。大きいほど経験で変わりにくい（慎重・頑固）。</summary>
    public float LearnPrior { get; init; } = 4f;

    /// <summary>守りの報酬の物差し [ダメージ]: 守りの間にこれだけ受けると報酬は 1/e。拳 1 発の被ダメージはおよそ 10〜15（2026-11-12 の総当たり）。</summary>
    public float LearnDamageScale { get; init; } = 10f;

    /// <summary>打つときに踏み込む胸と胸の距離（腕の長さ倍）。</summary>
    public float AttackRange { get; init; } = 0.95f;

    /// <summary>普段の胸と胸の距離（腕の長さ倍）。</summary>
    public float NeutralRange { get; init; } = 1.3f;

    /// <summary>攻撃を始めてよい距離（腕の長さ倍）。これより遠ければまず詰める。</summary>
    public float EngageRange { get; init; } = 1.6f;

    /// <summary>手の速さの倍率（経路の時間の逆数）。</summary>
    public float PunchSpeed { get; init; } = 1f;

    /// <summary>コンビネーションの重み（<see cref="Combo.Name"/> → 重み）。載っていない型は使わない。</summary>
    public IReadOnlyDictionary<string, float> ComboWeights { get; init; } = new Dictionary<string, float>
    {
        ["1"] = 2f, ["1-2"] = 3f, ["1-1-2"] = 1f, ["1-2-3"] = 2f, ["2-3-2"] = 1f, ["1b-2"] = 1f, ["3b-3"] = 1f, ["6-3"] = 1f, ["f-2"] = 0.5f,
    };

    /// <summary>
    /// 相手の構えを読む度合い（0..1）。両拳が顔の前にあればストレートを胴へ切り替え、頭を打つときは拳の無い側へ狙いをずらす。
    /// </summary>
    public float ReadGuard { get; init; }

    /// <summary>自分から攻めるか（偽なら守りとカウンターだけ。防御ドリルの受け手に使う）。</summary>
    public bool Attacks { get; init; } = true;

    /// <summary>コンビネーションを打ち終えたあと、間合いの外へ下がる確率。</summary>
    public float ExitAfterCombo { get; init; } = 0.4f;

    /// <summary>コンビネーションを打ち終えたあと、横へ回り込んで角度を取る（ピボット）確率。</summary>
    public float PivotAfterCombo { get; init; }

    /// <summary>相手が踏み込んできたとき、ジャブで止めて間合いを保つ（ストップジャブ）確率。</summary>
    public float StopJab { get; init; }

    /// <summary>掴んで投げに行く確率（近距離の攻撃のうち）。</summary>
    public float ThrowRate { get; init; } = 0.05f;

    // ── 足と頭 ──

    /// <summary>
    /// ロープを背負わない度合い（0..1）。背中側のロープが近いとき、下がる動きをこの割合だけ空いている横への動きに替える。
    /// </summary>
    public float RopeEscape { get; init; }

    /// <summary>相手の周りを回る強さ（0..1）。</summary>
    public float Circling { get; init; } = 0.4f;

    /// <summary>
    /// ステップ（脚のバネで一気に詰める・下がる。<see cref="FighterCommand.Step"/>）を使う好み（0..1、2026-11-18）。行きたい間合いとのずれが大きく、足運びの前後の指令が
    /// 振り切れている（<see cref="BoxerBot.StepFrom"/> を超える）ほど、その割合で脚のバネを放つ。どれだけ進むか・いつ次を蹴れるかは体（バネの貯まり具合）が決める。
    /// 既定は 0（使わない）: 判断の既定はリーグで比べてから変える（docs/BOT_PDCA.md）。スタイル JSON・教育で入れる。
    /// </summary>
    public float StepUse { get; init; }

    /// <summary>何もしていないときの頭の振り（ボブ・ウィーブ）の大きさ（0..1）。</summary>
    public float HeadMovement { get; init; } = 0.4f;

    /// <summary>構えの高さ（0 = 低い構え（胸）… 1 = あご … <see cref="MaxGuardHeight"/> = 目の高さで顔を覆う）。</summary>
    public float GuardHeight { get; init; } = 0.5f;

    /// <summary>構えの高さの上限（あごの高さを 1 として、拳が目の高さに来る）。</summary>
    public const float MaxGuardHeight = 1.5f;

    /// <summary>溜めの上限 [s]（ボットはこの時間で引ききる）。</summary>
    public const float MaxTelegraph = 0.3357f;

    /// <summary>
    /// 打つ前の溜め（予備動作）[s]（0 = 溜めなし、上限 <see cref="MaxTelegraph"/>）。連打の最初の拳の前に、打つ手の肩を後ろへ引き、頭を引いて膝を沈め
    /// （体のバネを縮める）、そのあと拳が出る。打ち出すと縮めたバネの反発と体の動きが拳に移る（場合分けせず物理で: 長く溜めるほど深く引けて、拳が速く重い）。
    /// 体は拳より先に動くので、相手はそれを読んで避ける。狙いは溜めの始めに決まる。長い溜めは威力が出る代わりに読まれやすく、時間もかかる。
    /// </summary>
    public float Telegraph { get; init; }

    /// <summary>構えの拳の左右の開き（1 = 標準 … 小さいほど両拳を顔の前で寄せる）。</summary>
    public float GuardWidth { get; init; } = 1f;

    /// <summary>構えの拳の前への出し（1 = 標準 … 小さいほど拳を顔に近づける）。</summary>
    public float GuardReach { get; init; } = 1f;

    // ── 守り ──

    /// <summary>拳が向かってくるのに気づいてから守りを出すまで [s]。</summary>
    public float ReactionTime { get; init; } = 0.3021f;

    /// <summary>
    /// 動き出しを読む度合い（0..1）。0 は拳が速く向かってきてから気づく（近い間合いでは反応時間が間に合わない）。
    /// 1 に近いほど、拳が相手の体に対して動き出した早い段階で気づく。代わりにフェイントや構えの揺れにも反応しやすい。
    /// 反応時間（<see cref="ReactionTime"/>）は気づいてから数える。
    /// </summary>
    public float Anticipation { get; init; }

    /// <summary>
    /// 予測で避ける度合い（0..1、0 = 予測しない）。試合の中で相手の間合い・リズム・打つ前の体の動き・癖を覚え
    /// （<see cref="OpponentRead"/>）、拳が出る前に「まもなく来る」と見積もった時に守りを始める。大きいほど低い確からしさでも
    /// 動き出す（早く避けられるが、フェイントや頭の振りにも釣られ、空振りの守りで自分の攻めを止める）。
    /// </summary>
    public float Prediction { get; init; }

    /// <summary>
    /// 予測で動き出すのに要る、体の手がかり（ひねり・前傾）の証拠の強さ（尤度比の対数、0..3）。0 なら体が「打たない」側を示していなければ動く。
    /// 大きいほど、間合いやリズムだけでは動かず、打つ前の体の動きを見てから動く（早すぎる予測で短い回避が拳より先に終わるのを防ぐ）。
    /// </summary>
    public float BodyCue { get; init; }

    /// <summary>
    /// 狙いどころの重み（<see cref="AimWeights"/>: あご・こめかみ・露出した脳・肝臓・みぞおち・心臓・ガードの腕）。拳を出すたびに、
    /// その拳で打てる狙いどころから重みの比で選び、無理のない範囲（<see cref="Targeting"/>）で狙いの点を動かす。null なら真ん中だけ。
    /// </summary>
    public AimWeights? Aim { get; init; }

    /// <summary>リングネーム（中継・実況で呼ぶ名前）。null なら名前から決まる名前（<see cref="BotNames.Generate"/>）。顔もこれから決まる。</summary>
    public string? RingName { get; init; }

    /// <summary>
    /// 相手の届く間合いのすぐ外に立つ度合い（0..1）。覚えた相手の打ち始めの間合いの少し外を普段の間合いにし、
    /// 相手に踏み込ませる（踏み込みは読める手がかりになり、その間に避ける）。
    /// </summary>
    public float EdgeRange { get; init; }

    /// <summary>
    /// 避ける拳の届く範囲（腕の長さ倍、0 = 見ない）。相手の肩から自分の頭までがこれより遠い拳は、届かないとみて避けない。
    /// </summary>
    public float EvadeReach { get; init; }

    /// <summary>気づいた拳に守りを出す確率。</summary>
    public float DefenseRate { get; init; } = 0.55f;

    /// <summary>キャラの顔の部位の寸法（null ならリングネームから決まる顔 <see cref="Voxels.FaceParts.For"/>）。</summary>
    public Voxels.FaceParts? Face { get; init; }

    /// <summary>キャラの体格（身長・腕・脚・筋肉の太さ。null なら基準体格）。</summary>
    public BodyBuild? Build { get; init; }

    /// <summary>
    /// 組み技の教科書の名前（<see cref="GrappleBook.Named"/>、例 <c>jujutsu</c>）。null なら組み技を使わない（今までのボット）。
    /// あれば、このスタイルのボットを <see cref="JujutsuBot"/> が包み、組める間合いで技をかける。
    /// </summary>
    public string? Grapple { get; init; }

    /// <summary>Probability of selecting a kick. Zero preserves the old random sequence.</summary>
    public float KickRate { get; init; }

    public float ClawDriveSeconds { get; init; } = 0.31f;

    public float ClawWindup { get; init; } = 1f;

    /// <summary>
    /// 四足の鎌の斬り払いで、狙う所を選ぶ重み（教育の値: 技の選び方。2026-11-25）。首の継ぎ目・相手の腕・胴の継ぎ目の 3 つを、重みの比で順に回す
    /// （重みを毎回足して、いちばん溜まった狙いを選び、合計を引く: 乱数を使わない決まった順）。三つとも 1（既定）なら 首 → 腕 → 胴 の繰り返し（今までの順）。
    /// 0 の狙いは選ばない。全部 0 なら既定と同じ。
    /// </summary>
    public float ClawAimNeck { get; init; } = 1f;

    /// <summary>鎌で相手の腕を狙う重み（<see cref="ClawAimNeck"/>）。</summary>
    public float ClawAimArm { get; init; } = 1f;

    /// <summary>鎌で相手の胴の継ぎ目を狙う重み（<see cref="ClawAimNeck"/>）。</summary>
    public float ClawAimTorso { get; init; } = 1f;

    /// <summary>
    /// 鎌の間合い: 腕の長さに足す、鎌の長さの割合（教育の値: 間合いの取り方。2026-11-25）。この距離（胸まで）より遠ければ寄り、届けば斬る。
    /// 小さいほど深く踏み込んで鎌の根元の側で斬り、大きいほど遠くから先で斬る。負なら腕の長さより内側まで踏み込む（引き斬りで刃を相手の向こうへ掛けるため）。既定 0.56（それまでの値）。
    /// </summary>
    public float ClawReach { get; init; } = 0.56f;

    /// <summary>
    /// 鎌の斬り払いの合間 [s]（教育の値: 技の拍子。2026-11-25）: 両腕の鎌が構えへ戻ってから、次を振り始めるまで待つ時間。待つ間も間合いは詰める。
    /// 振り続けると脚・腕の酸素が尽きて（スタミナ）振りが遅く弱くなるので、息を戻す間との釣り合い。既定 0（それまでどおり、届けばすぐ振る）。
    /// </summary>
    public float ClawRest { get; init; }

    /// <summary>
    /// 鎌の振りのうち、引き斬り（<see cref="Weapons.ClawStroke.Draw"/>: 狙いの点の向こうへ刃を掛けて手前へ引く）にする割合 0..1（教育の値: 技の選び方。2026-11-25）。
    /// 割合を毎回足して 1 を超えたら引き斬り（乱数を使わない決まった順）。既定 0（今までどおり全部を振り抜く）。
    /// </summary>
    public float ClawDraw { get; init; }

    /// <summary>
    /// 鎌の狙いの点を上へずらす量 [腕の長さ]（教育の値: 技の経路。2026-11-25）。重い刃は振る間に腕ごと下へ外れる（huntlab: 首を狙った振りが胴に入った）ので、
    /// 外れる分だけ上を狙う（人が覚える狙いの癖と同じ）。既定 0。
    /// </summary>
    public float ClawAimLift { get; init; }

    /// <summary>
    /// 鎌の振りのうち、円柱を垂直に断つ経路（<see cref="Weapons.ClawStroke.Cylinder"/>: 狙いの点を中心、相手の体の軸を軸とする円柱を、軸に垂直な面で振り抜く）
    /// にする割合 0..1（教育の値: 技の選び方。2026-11-25）。引き斬り（<see cref="ClawDraw"/>）より先に選ぶ。既定 0。
    /// </summary>
    public float ClawCut { get; init; }

    /// <summary>円柱を断つ経路で、刃の上の斬る点（刃元から弦の割合）。既定 0.6。</summary>
    public float ClawCutPoint { get; init; } = 0.6f;

    /// <summary>円柱を断つ経路で、中心から向こうへ振り抜く距離 [腕の長さ]。既定 0.5。</summary>
    public float ClawCutAfter { get; init; } = 0.5f;
    /// <summary>Kick goal cycle duration in seconds.</summary>
    public float KickDuration { get; init; } = 1.175f;
    /// <summary>
    /// 蹴りを選ぶとき、その蹴りが届く間合い（<see cref="KickSpec.Standoff"/>）と今の間合いの合い具合で重みを連続に変える強さ（0 なら今までどおり
    /// 重みだけで選ぶ）。合わない間合いの蹴りほど exp(−強さ × (ずれ / 0.15)²) で選ばれにくくなる。種類での場合分けではなく、見える距離（腕の長さの倍数）から。
    /// </summary>
    public float KickRangeFit { get; init; }

    /// <summary>
    /// 攻めるとき、拳・蹴りの代わりに肘打ち（<see cref="ElbowSpec"/>）を選ぶ気（0..1）。0 なら肘を使わない（乱数の消費も今までと同じ）。
    /// 実際に選ぶ確率は、この値 × 今の間合いが肘の届く間合いに合う度合い（<see cref="ElbowRangeFit"/>）。
    /// </summary>
    public float ElbowRate { get; init; }

    /// <summary>肘を選ぶとき、今の間合いと肘の届く間合い（<see cref="ElbowSpec.Standoff"/>）の合い具合で重みを連続に変える強さ（<see cref="KickRangeFit"/> と同じ式。0 なら間合いを見ない）。</summary>
    public float ElbowRangeFit { get; init; }

    /// <summary>肘の種類ごとの好み（相対の重み）。</summary>
    public IReadOnlyDictionary<ElbowTechnique, float> ElbowWeights { get; init; } = new Dictionary<ElbowTechnique, float>();

    /// <summary>休みの間の過ごし方の好み（動作の名前 → 選ばれやすさへ掛ける倍率。<c>Break.BreakBook</c> の動作名。無い名前は 1。入力の側の個性であって体の強さではない）。</summary>
    public IReadOnlyDictionary<string, float> BreakLikes { get; init; } = new Dictionary<string, float>();

    /// <summary>
    /// 蹴る前に体重を軸足（前足）へ移す間 [s]（教育の値: 技の時間の経路。0 なら今までどおり、脚を上げ始めるのと同時に移す）。
    /// この間は脚を上げず、頭を <see cref="KickLeanForward"/>・<see cref="KickLeanSide"/> だけ軸足の上へ寄せる（体重のかけ方の指令）。
    /// 体がそれを追って重心が軸足に乗ってから、蹴る脚を上げる（docs/TAEKWONDO.md「軸足に乗って蹴る」）。
    /// </summary>
    public float KickShift { get; init; }

    /// <summary>
    /// 倒れた相手への攻めの技の経路（<see cref="GroundTuning"/>: 鉄槌・真下への突き・踏みつけ・斬り下ろしの寸法）。教科書・教育の値（技の経路）で、
    /// null なら <see cref="GroundTuning.Default"/>。groundlab の練習の結果をここへ書く。
    /// </summary>
    public GroundTuning? Ground { get; init; }

    /// <summary>
    /// バトルロイヤル（3 人以上）で狙う相手を選ぶ重み（<see cref="RivalWeights"/>。教科書・教育の値）。null なら <see cref="RivalWeights.Default"/>。
    /// 2 人の試合では使わない（相手は 1 人）。
    /// </summary>
    public RivalWeights? Rival { get; init; }

    /// <summary>蹴りの間、頭を前（相手の方、腕の長さの倍数）へ寄せる量。軸足は前足なので、前へ寄せるほど体重が軸足に乗る。</summary>
    public float KickLeanForward { get; init; }

    /// <summary>蹴りの間、頭を前足の側（腕の長さの倍数）へ寄せる量。</summary>
    public float KickLeanSide { get; init; }

    /// <summary>
    /// 倒れた相手を攻められる試合（レフェリーなし）で、倒れた相手へ打ち下ろす気（0..1）。立つ位置へ着いて届く間合いに沈めたとき、打ち始めを決める割合
    /// （0 なら倒れた相手へ手を出さない。レフェリーのいる試合では使わない）。
    /// </summary>
    public float GroundDrive { get; init; } = 0.8f;

    /// <summary>倒れた相手への打ち下ろしの溜め [s]（振りかぶったまま構えて体のバネを縮める時間。<see cref="MaxTelegraph"/> まで）。</summary>
    public float GroundLoad { get; init; } = 0.08392f;

    /// <summary>倒れた相手への攻めのうち、振り下ろす系（鉄槌・斬り下ろし・棍棒の打ち下ろし）を選ぶ割合（残りは突き下ろす系。棍棒は振り下ろす系だけ）。</summary>
    public float GroundCut { get; init; } = 0.5f;

    /// <summary>武器を握っていないとき、足の技（踏みつけ・蹴り）を選ぶ割合。既定は空手の教科書と同じ。</summary>
    public float GroundStomp { get; init; } = 0.6f;

    /// <summary>足の技のうち、倒れた相手を蹴る（<see cref="GroundTuning.Kick"/>）割合（残りは踏みつけ）。既定 0（蹴らない。2026-11-24 に足した）。</summary>
    public float GroundKick { get; init; }
    /// <summary>足で踏むときに、かかとの踏み下ろしの連打（<see cref="GroundTuning.HeelStomp"/>）を選ぶ割合 0..1（残りは足裏の踏みつけ）。</summary>
    public float GroundHeel { get; init; } = 1f;

    /// <summary>倒れた相手への打ち下ろしの合間 [s]（次を決めるまで）。</summary>
    public float GroundRest { get; init; } = 0.5874f;
    public SwordDefense Parry { get; init; } = SwordDefense.Parrying with { ThreatSpeed = 0.4767f, Margin = 0.4f, Clearance = 0.18f };
    public IReadOnlyDictionary<KickTechnique, float> KickWeights { get; init; } = new Dictionary<KickTechnique, float>();
    public float ParryWeight { get; init; }
    public IReadOnlyDictionary<HandShape, float> HandShapeWeights { get; init; } = new Dictionary<HandShape, float>();

    public float BlockWeight { get; init; } = 1f;
    public float DuckWeight { get; init; } = 0.5f;
    public float SlipWeight { get; init; } = 0.5f;
    public float SwayWeight { get; init; } = 0.5f;
    public float StepBackWeight { get; init; } = 0.5f;
    public float WeaveWeight { get; init; }
    public float SideStepWeight { get; init; }
    public float SlipInWeight { get; init; }
    public float RollWeight { get; init; }
    /// <summary>拳の横を手で押して軌道を外す好み。既存のボクサーは使わない。</summary>

    /// <summary>防御技それぞれの動き（頭・足・拳）。防御ドリルで最適化した値を持つ。</summary>
    public DefenseBook Defenses { get; init; } = DefenseBook.Default;

    /// <summary>拳の技表（技ごとの経路・時間・体の使い方）。JSON に書いていない技・値は既定（<see cref="PunchBook.Default"/>）になる。</summary>
    public PunchBook Punches { get; init; } = PunchBook.Default;

    /// <summary>守り切ったあとにすぐ打ち返す（カウンター）確率。</summary>
    public float CounterRate { get; init; } = 0.5f;

    /// <summary>続けて打たれたときに組み付いて（クリンチ）止める確率。</summary>
    public float ClinchRate { get; init; } = 0.2f;

    /// <summary>
    /// 戦略の層（<see cref="Bots.GamePlan"/>、2026-11-18）: 自分と相手の得意な間合いの表・性格・急ぐ度合い。null（教科書の既定）なら層なしで、
    /// 判断はビット単位で今までと同じ。キャラの教育（<c>education.style.GamePlan</c>）で入れ、相手の表（<see cref="GamePlanStyle.Foe"/>）は試合の相手のキャラから入る
    /// （<see cref="Characters.FighterCharacter.Resolve(string)"/>）。
    /// </summary>
    public GamePlanStyle? GamePlan { get; init; }

    [System.Text.Json.Serialization.JsonIgnore]
    public Hand Lead => Southpaw ? Hand.Right : Hand.Left;

    public float Weight(Defense defense) => defense switch
    {
        Defense.Block => BlockWeight,
        Defense.Parry => ParryWeight,
        Defense.Duck => DuckWeight,
        Defense.Slip => SlipWeight,
        Defense.Sway => SwayWeight,
        Defense.StepBack => StepBackWeight,
        Defense.Weave => WeaveWeight,
        Defense.SideStep => SideStepWeight,
        Defense.SlipIn => SlipInWeight,
        Defense.Roll => RollWeight,
        _ => 0f,
    };

    /// <summary>人並みの制約と値域に収めたスタイル。未知のコンビネーション名があれば例外。</summary>
    public BotStyle Validated()
    {
        // JSON で null を書かれても落ちないよう、既定へ戻す（壊れたリプレイの見出しは InvalidDataException として報告される）。
        var comboWeights = ComboWeights ?? new Dictionary<string, float>();
        var name = string.IsNullOrWhiteSpace(Name) ? "boxer" : Name;
        foreach (var combo in comboWeights.Keys)
        {
            _ = Combo.Named(combo);
        }

        static float Unit(float v) => float.IsFinite(v) ? Math.Clamp(v, 0f, 1f) : 0f;
        static float NonNegative(float v) => float.IsFinite(v) ? MathF.Max(0f, v) : 0f;
        static float Range(float v, float min, float max, float fallback) => Math.Clamp(float.IsFinite(v) ? v : fallback, min, max);
        var attack = Range(AttackRange, 0.7f, 1.3f, 0.95f);
        var neutral = Range(NeutralRange, attack, 1.9f, 1.3f);
        return this with
        {
            Aggression = Unit(Aggression),
            PaceKnee = Range(PaceKnee, 0.05f, 1f, 0.7f),
            PaceDrop = Unit(PaceDrop),
            RestBelow = Range(RestBelow, 0.01f, 1f, 0.5f),
            RestTempo = Range(RestTempo, 0f, 1f, 0.35f),
            AimLead = Range(AimLead, 0f, 1.5f, 1f),
            ElbowTuck = Range(ElbowTuck, 0f, 1f, 0.5f),
            RangeThreat = Range(RangeThreat, 0.5f, 2.5f, 1.2f),
            RangeGuard = Unit(RangeGuard),
            TiredGuardDrop = Range(TiredGuardDrop, 0f, 1f, 0.25f),
            RangeHeadMove = Unit(RangeHeadMove),
            RangeGuess = Range(RangeGuess, 0f, 5.958f, 0f),
            RangeExit = Unit(RangeExit),
            RangeKeepOut = Unit(RangeKeepOut),
            RangePress = Range(RangePress, 0f, 10f, 0f),
            BuildStandOff = Range(BuildStandOff, 0f, 1.5f, 0f),
            BuildInside = Range(BuildInside, 0f, 2f, 0f),
            BuildPush = Range(BuildPush, 0f, 1f, 0f),
            BuildDuck = Range(BuildDuck, 0f, 2f, 0f),
            BuildAimBody = Range(BuildAimBody, 0f, 4f, 0f),
            HurtSagStart = Range(HurtSagStart, 0f, 0.5f, 0.06f),
            HurtSagSpan = Range(HurtSagSpan, 0.01f, 1f, 0.12f),
            HurtFeltScale = Range(HurtFeltScale, 1f, 500f, 30f),
            HurtMemory = Range(HurtMemory, 0.1678f, 33.57f, 4.196f),
            FinishTempo = Range(FinishTempo, 0f, 5f, 1.5f),
            FinishPress = Range(FinishPress, 0f, 2f, 1.5f),
            FinishSpend = Range(FinishSpend, 0f, 1f, 0.6f),
            HurtWinded = Range(HurtWinded, 0f, 2f, 0f),
            HurtInjury = Range(HurtInjury, 0f, 2f, 0f),
            FinishCombos = (FinishCombos ?? new Dictionary<string, float>()).Where(p => Combo.All.Any(c => c.Name == p.Key)).ToDictionary(p => p.Key, p => NonNegative(p.Value)),
            FinishComboShare = Unit(FinishComboShare),
            FinishGap = Range(FinishGap, 0f, 0.8f, 0f),
            LearnPrior = Range(LearnPrior, 0.5f, 100f, 4f),
            LearnDamageScale = Range(LearnDamageScale, 0.5f, 100f, 10f),
            AttackRange = attack,
            NeutralRange = neutral,
            EngageRange = Range(EngageRange, neutral, 2.2f, 1.6f),
            PunchSpeed = Range(PunchSpeed, 0.8f, MaxPunchSpeed, 1f),
            Name = name,
            ComboWeights = comboWeights.ToDictionary(p => p.Key, p => NonNegative(p.Value)),
            ReadGuard = Unit(ReadGuard),
            ExitAfterCombo = Unit(ExitAfterCombo),
            PivotAfterCombo = Unit(PivotAfterCombo),
            StopJab = Unit(StopJab),
            ThrowRate = Unit(ThrowRate),
            Circling = Unit(Circling),
            StepUse = Unit(StepUse),
            RopeEscape = Unit(RopeEscape),
            HeadMovement = Unit(HeadMovement),
            GuardHeight = Range(GuardHeight, 0f, MaxGuardHeight, 0.5f),
            GuardWidth = Range(GuardWidth, 0.2f, 1.5f, 1f),
            Telegraph = Range(Telegraph, 0f, MaxTelegraph, 0f),
            GuardReach = Range(GuardReach, 0.3f, 1.5f, 1f),
            ReactionTime = Range(ReactionTime, MinReactionTime, 0.8392f, 0.8392f),
            DefenseRate = Range(DefenseRate, 0f, MaxDefenseRate, 0f),
            Face = Face?.Validate(),
            Build = Build?.Validate(),
            KickRate = Unit(KickRate),
            ClawDriveSeconds = Range(ClawDriveSeconds, 0.08f, 0.65f, 0.31f),
            ClawWindup = Range(ClawWindup, 0.45f, 1.5f, 1f),
            ClawAimNeck = Range(ClawAimNeck, 0f, 10f, 1f),
            ClawAimArm = Range(ClawAimArm, 0f, 10f, 1f),
            ClawAimTorso = Range(ClawAimTorso, 0f, 10f, 1f),
            ClawReach = Range(ClawReach, -0.8f, 1f, 0.56f),
            ClawRest = Range(ClawRest, 0f, 3f, 0f),
            ClawDraw = Unit(ClawDraw),
            ClawAimLift = Range(ClawAimLift, -0.5f, 0.8f, 0f),
            ClawCut = Unit(ClawCut),
            ClawCutPoint = Range(ClawCutPoint, 0.1f, 1f, 0.6f),
            ClawCutAfter = Range(ClawCutAfter, 0f, 1.5f, 0.5f),
            KickDuration = Range(KickDuration, 0.6714f, 2.014f, 1.175f),
            KickRangeFit = Range(KickRangeFit, 0f, 4f, 0f),
            BreakLikes = (BreakLikes ?? new Dictionary<string, float>()).ToDictionary(p => p.Key, p => NonNegative(p.Value)),
            ElbowRate = Unit(ElbowRate),
            ElbowRangeFit = Range(ElbowRangeFit, 0f, 4f, 0f),
            ElbowWeights = (ElbowWeights ?? new Dictionary<ElbowTechnique, float>()).Where(p => Enum.IsDefined(p.Key)).ToDictionary(p => p.Key, p => NonNegative(p.Value)),
            KickShift = Range(KickShift, 0f, 1.007f, 0f),
            Ground = Ground?.Validated(),
            Rival = Rival?.Validated(),
            KickLeanForward = Range(KickLeanForward, -0.4f, 0.4f, 0f),
            KickLeanSide = Range(KickLeanSide, -0.4f, 0.4f, 0f),
            GroundDrive = Unit(GroundDrive),
            GroundLoad = Range(GroundLoad, 0f, MaxTelegraph, 0.08392f),
            GroundCut = Unit(GroundCut),
            GroundStomp = Unit(GroundStomp),
            GroundKick = Unit(GroundKick),
            GroundHeel = Unit(GroundHeel),
            GroundRest = Range(GroundRest, 0.1678f, 5.035f, 0.5874f),
            Parry = Parry ?? SwordDefense.Parrying,
            KickWeights = (KickWeights ?? new Dictionary<KickTechnique, float>()).Where(p => Enum.IsDefined(p.Key)).ToDictionary(p => p.Key, p => NonNegative(p.Value)),
            HandShapeWeights = (HandShapeWeights ?? new Dictionary<HandShape, float>()).Where(p => Enum.IsDefined(p.Key)).ToDictionary(p => p.Key, p => NonNegative(p.Value)),
            ParryWeight = NonNegative(ParryWeight),
            BlockWeight = NonNegative(BlockWeight),
            DuckWeight = NonNegative(DuckWeight),
            SlipWeight = NonNegative(SlipWeight),
            SwayWeight = NonNegative(SwayWeight),
            StepBackWeight = NonNegative(StepBackWeight),
            WeaveWeight = NonNegative(WeaveWeight),
            SideStepWeight = NonNegative(SideStepWeight),
            SlipInWeight = NonNegative(SlipInWeight),
            RollWeight = NonNegative(RollWeight),
            Defenses = (Defenses ?? DefenseBook.Default).Validated(),
            Punches = (Punches ?? PunchBook.Default).Validated(),
            CounterRate = Unit(CounterRate),
            Anticipation = Unit(Anticipation),
            Prediction = Unit(Prediction),
            BodyCue = Range(BodyCue, 0f, 3f, 0f),
            Aim = Aim?.Validated(),
            EdgeRange = Unit(EdgeRange),
            EvadeReach = float.IsFinite(EvadeReach) && EvadeReach > 0f ? Math.Clamp(EvadeReach, 0.8f, 2f) : 0f,
            ClinchRate = Unit(ClinchRate),
            GamePlan = GamePlan?.Validated(),
        };
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static BotStyle FromJson(string json)
        => (JsonSerializer.Deserialize<BotStyle>(json, JsonOptions) ?? throw new JsonException("空のスタイルです。")).Validated();

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
}
