using System.Numerics;

namespace MiniRing.Break;

/// <summary>
/// 試合の始め・休み・試合の終わりの動作（エモート）の一覧（データ）。<see cref="BreakBook"/> の休みの動作と同じ作りで、どれも「手・頭・胴・体の向きの目標の経路」
/// （キーフレームの再生でも姿勢の書き込みでもない。CLAUDE.md §1.2）。値は腕の長さ（約 53 cm）を 1 とした体フレームのローカル
/// （肩の中心が原点、X はその手の外側が +、Y 上、Z 前。頭の基準の目標は頭の中心からのずれ。頭の中心は肩の中心の約 0.28 上、口は頭の中心の約 0.15 下・0.15 前）。
/// 動作はどれも同じ評価（<see cref="BreakRoutine"/>）を通り、勝ち負けの量は重みの <see cref="BreakWeights.Joy"/> だけに入る（動作の種類での場合分けではない。CLAUDE.md §1.3）。
/// </summary>
public static class BreakEmotes
{
    /// <summary>始め・終わりの動作の、目標へ移る時間と構えへ戻る時間 [s]（休みの動作より短く、4〜5 秒に収める）。</summary>
    private const float EL = 0.8392f;

    private const float ER = 0.7553f;

    private const BreakScene S = BreakScene.Start;
    private const BreakScene B = BreakScene.Break;
    private const BreakScene E = BreakScene.End;

    // 観客へ。
    public const string Wave = "wave";
    public const string WaveTwo = "wavetwo";
    public const string RaiseArms = "raisearms";
    public const string FistPump = "fistpump";
    public const string BlowKiss = "blowkiss";
    public const string PointCrowd = "pointcrowd";
    public const string Bow = "bow";
    public const string ChestBeat = "chestbeat";
    public const string HypeCrowd = "hypecrowd";
    public const string GutsPose = "gutspose";
    public const string RopePerch = "ropeperch";
    public const string Roar = "roar";
    public const string Flex = "flex";
    public const string PointUp = "pointup";
    public const string LookCrowd = "lookcrowd";

    // 体。
    public const string ShadowBox = "shadowbox";
    public const string Bounce = "bounce";
    public const string NeckCrack = "neckcrack";
    public const string GloveClap = "gloveclap";
    public const string MouthGuard = "mouthguard";
    public const string StretchArms = "stretcharms";
    public const string SideStretch = "sidestretch";
    public const string ForwardBend = "forwardbend";
    public const string FixGloves = "fixgloves";
    public const string WipeNose = "wipenose";
    public const string Yawn = "yawn";
    public const string Dance = "dance";

    // 気持ち・作法。
    public const string Selfie = "selfie";
    public const string Mirror = "mirror";
    public const string Prayer = "prayer";
    public const string CrossSelf = "crossself";
    public const string Salute = "salute";
    public const string Applause = "applause";
    public const string Handshake = "handshake";
    public const string LookSky = "looksky";
    public const string HeadInHands = "headinhands";
    public const string HandsOnKnees = "handsonknees";
    public const string SitDown = "sitdown";
    public const string WipeTears = "wipetears";
    public const string ShakeHead = "shakehead";
    public const string SlamFist = "slamfist";
    public const string TowelHead = "towelhead";

    // 飲み物。
    public const string SportsDrink = "sportsdrink";
    public const string Coffee = "coffee";
    public const string EnergyDrink = "energydrink";
    public const string Milk = "milk";
    public const string StrawDrink = "strawdrink";
    public const string Jelly = "jelly";
    public const string SpitWater = "spitwater";

    // 軽食。
    public const string Banana = "banana";
    public const string Onigiri = "onigiri";
    public const string Sandwich = "sandwich";
    public const string Chocolate = "chocolate";
    public const string Apple = "apple";
    public const string Donut = "donut";

    // 嗜好・音楽。
    public const string Cigarette = "cigarette";
    public const string Gum = "gum";
    public const string Headphones = "headphones";
    public const string Earbud = "earbud";
    public const string Hum = "hum";

    private static Vector3 V(float x, float y, float z) => new(x, y, z);

    private static Path3 Keys(params (float T, Vector3 P)[] keys) => new(keys);

    private static Path3 Hold(Vector3 p) => Path3.Hold(p);

    private static Path3 Around(Vector3 center, Vector3 amplitude, float cycles, Vector3? quadrature = null) => new([(0f, center)], new MiniRing.Break.Wave(amplitude, cycles, quadrature));

    private static Path3 Wavy(Path3 keys, Vector3 amplitude, float cycles) => keys with { Wave = new MiniRing.Break.Wave(amplitude, cycles) };

    private static HandPath Body(Path3 path) => new(HandAnchor.Body, path);

    private static HandPath Head(Path3 path) => new(HandAnchor.Head, path);

    private static readonly Path3 Zero = Path3.Still;

    /// <summary>口へ運ぶ物（ボトル・缶・食べ物）の持ち方: 口の側の端（局所 +X）が顔の方（後ろ・下）を向く。</summary>
    private static readonly PropHold ToMouth = new(V(-0.15f, -0.45f, -0.88f), V(0f, 1f, 0f));

    /// <summary>短い物（食べ物・たばこ）の持ち方: 口の側の端が顔の方（後ろ）を向き、やや下がる。</summary>
    private static readonly PropHold ToMouthFlat = new(V(-0.3f, -0.15f, -0.94f), V(0f, 1f, 0f));

    /// <summary>休みの腰に手を当てた構え（深呼吸と同じ）。</summary>
    private static readonly Vector3 OnHip = V(0.62f, -0.58f, 0.02f);

    /// <summary>
    /// 全動作（深呼吸・水を飲む…の休みの動作は <see cref="BreakBook"/> にある）。値の調整は <c>BreakActionProbe</c> で動作ごとのコマを見て決めた。
    /// </summary>
    public static IReadOnlyList<BreakAction> All()
    {
        var list = new List<BreakAction>();
        list.AddRange(CrowdActions());
        list.AddRange(BodyActions());
        list.AddRange(MannerActions());
        list.AddRange(DrinkActions());
        list.AddRange(SnackActions());
        list.AddRange(TasteActions());
        return list;
    }

    private static List<BreakAction> CrowdActions() =>
    [
        // 観客に手を振る（片手）: 手を肩の高さより上へ上げ、手首から先の代わりに手の目標を左右に振る。
        new(Wave, 4.7f, null,
            Body(Around(V(0.7f, 0.62f, 0.12f), V(0.2f, 0.08f, 0f), 3.5f)), null,
            Keys((0f, Vector3.Zero), (0.4f, V(0f, 0.03f, 0f)), (1f, V(0f, 0.02f, 0f))), Keys((0f, V(0f, 0f, 0f))), Keys((0f, Vector3.Zero)),
            new BreakWeights(1.1f, Joy: 0.8f), EL, ER, Scenes: S | B | E),

        // 両手を振る: 両手を頭の上で大きく左右に振る。
        new(WaveTwo, 4.7f, null,
            Body(Around(V(0.55f, 0.85f, 0.1f), V(0.22f, 0.06f, 0f), 3.5f)), Body(Around(V(0.55f, 0.85f, 0.1f), V(0.22f, 0.06f, 0f), 3.5f)),
            Keys((0f, V(0f, 0f, 0f)), (0.4f, V(0f, 0.05f, -0.03f)), (1f, V(0f, 0.04f, -0.02f))), Zero, Zero,
            new BreakWeights(0.7f, Joy: 1.5f, Hurt: -1f), EL, ER, Scenes: S | E),

        // 両手を挙げる: 両腕をまっすぐ上へ。頭を上・後ろへ反らし、胸を張る。
        new(RaiseArms, 4.364f, null,
            Body(Keys((0f, V(0.4f, 0.35f, 0.2f)), (0.3f, V(0.36f, 0.95f, 0.12f)), (1f, V(0.36f, 0.95f, 0.12f)))),
            Body(Keys((0f, V(0.4f, 0.35f, 0.2f)), (0.3f, V(0.36f, 0.95f, 0.12f)), (1f, V(0.36f, 0.95f, 0.12f)))),
            Keys((0f, Vector3.Zero), (0.3f, V(0f, 0.07f, -0.08f)), (0.9f, V(0f, 0.07f, -0.08f))), Zero, Zero,
            new BreakWeights(0.5f, Joy: 3f, Hurt: -1f), EL, ER, Scenes: S | E),

        // 拳を突き上げる: 構えから拳を高く突き上げ、小さく 3 度ためて突く。
        new(FistPump, 4.364f, null,
            Body(Wavy(Keys((0f, V(0.5f, 0.2f, 0.4f)), (0.25f, V(0.38f, 0.92f, 0.18f)), (1f, V(0.38f, 0.92f, 0.18f))), V(0f, 0.12f, 0f), 3f)), null,
            Keys((0f, Vector3.Zero), (0.3f, V(0f, 0.06f, -0.04f)), (0.9f, V(0f, 0.06f, -0.04f))), Zero, Zero,
            new BreakWeights(0.6f, Joy: 2.5f, Hurt: -1f), EL, ER, Scenes: S | E),

        // 投げキス: 手のひらを口へ当て、前へ大きく払う。頭を少し前へ傾け、払うとき観客の方へ体を向ける。
        new(BlowKiss, 4.7f, null,
            Head(Keys((0f, V(-0.05f, -0.35f, 0.5f)), (0.22f, V(0f, -0.14f, 0.35f)), (0.42f, V(0f, -0.14f, 0.35f)), (0.78f, V(0.95f, 0.02f, 0.85f)), (1f, V(0.9f, -0.1f, 0.8f)))), null,
            Keys((0f, Vector3.Zero), (0.25f, V(0f, -0.05f, 0.08f)), (0.45f, V(0f, -0.05f, 0.08f)), (0.8f, V(0f, 0.05f, -0.03f)), (1f, Vector3.Zero)), Zero,
            Keys((0f, Vector3.Zero), (0.5f, Vector3.Zero), (0.85f, V(0.35f, 0f, 0f)), (1f, V(0.35f, 0f, 0f))),
            new BreakWeights(0.5f, Joy: 1f), EL, ER, Scenes: S | E),

        // 観客を指さす: 手を横へ伸ばして、体ごとゆっくり左から右へ指していく。
        new(PointCrowd, 5.035f, null,
            Body(Keys((0f, V(0.3f, 0f, 0.5f)), (0.25f, V(0.85f, 0.45f, 0.6f)), (0.75f, V(0.9f, 0.42f, 0.55f)), (1f, V(0.9f, 0.4f, 0.5f)))), null,
            Keys((0f, Vector3.Zero), (0.3f, V(0f, 0.03f, 0.05f))), Keys((0f, Vector3.Zero)),
            Keys((0f, Vector3.Zero), (0.3f, V(-0.3f, 0f, 0f)), (0.75f, V(0.4f, 0f, 0f)), (1f, V(0.4f, 0f, 0f))),
            new BreakWeights(0.6f, Joy: 1f), EL, ER, Scenes: S | E),

        // お辞儀: 両手を体の前に下ろし、腰から体を折って頭を下げ、戻る（腰から折るヒップヒンジは頭の目標を前・下へ出す結果）。
        new(Bow, 4.7f, null,
            Body(Hold(V(0.4f, -0.75f, 0.2f))), Body(Hold(V(0.4f, -0.75f, 0.2f))),
            Keys((0f, Vector3.Zero), (0.35f, V(0f, -0.32f, 0.5f)), (0.65f, V(0f, -0.32f, 0.5f)), (1f, Vector3.Zero)), Zero, Zero,
            new BreakWeights(0.9f, Joy: 0.2f), EL, ER, Scenes: S | E),

        // 胸を叩く: 両方の拳で交互に自分の胸を叩く（拳が胸へ前後に往復）。頭を少し上へ。
        new(ChestBeat, 4.364f, null,
            Body(Around(V(0.18f, -0.08f, 0.5f), V(0f, 0f, 0.2f), 3f)), Body(Around(V(0.18f, -0.08f, 0.5f), V(0f, 0f, -0.2f), 3f)),
            Keys((0f, Vector3.Zero), (0.3f, V(0f, 0.05f, -0.03f)), (0.9f, V(0f, 0.05f, -0.03f))), Zero, Zero,
            new BreakWeights(0.5f, Joy: 2f), EL, ER, Scenes: S | E),

        // 耳に手を当てて歓声をあおる: 片手を耳へ、もう片手を前へ手招き。頭を耳の側へ傾け、体を観客の方へ向ける。
        new(HypeCrowd, 4.7f, null,
            Head(Keys((0f, V(0.5f, -0.2f, 0.3f)), (0.25f, V(0.3f, 0f, 0.05f)), (1f, V(0.3f, 0f, 0.05f)))),
            Body(Around(V(0.6f, 0.25f, 0.5f), V(0f, 0.12f, 0.1f), 3f)),
            Keys((0f, Vector3.Zero), (0.3f, V(0.06f, 0f, 0f)), (0.9f, V(0.06f, 0f, 0f))), Zero,
            Keys((0f, Vector3.Zero), (0.3f, V(0.3f, 0f, 0f)), (0.9f, V(0.3f, 0f, 0f))),
            new BreakWeights(0.6f, Joy: 1.2f), EL, ER, Scenes: S | E),

        // ガッツポーズ: 両拳を脇へ引きつけ、胸を張って頭を上げる（叫ぶように頭が小さく上下）。
        new(GutsPose, 4.364f, null,
            Body(Keys((0f, V(0.5f, -0.1f, 0.4f)), (0.25f, V(0.5f, 0.0f, 0.05f)), (1f, V(0.5f, 0.0f, 0.05f)))),
            Body(Keys((0f, V(0.5f, -0.1f, 0.4f)), (0.25f, V(0.5f, 0.0f, 0.05f)), (1f, V(0.5f, 0.0f, 0.05f)))),
            Wavy(Keys((0f, Vector3.Zero), (0.3f, V(0f, 0.08f, -0.06f)), (0.9f, V(0f, 0.08f, -0.06f))), V(0f, 0.04f, 0f), 3f), Zero, Zero,
            new BreakWeights(0.4f, Joy: 3f, Hurt: -1f), EL, ER, Scenes: S | E),

        // ロープに登る（届く範囲で）: 両腕を後ろ上へ伸ばして最上段のロープへ掛け、体を後ろへ反らす。
        new(RopePerch, 5.035f, null,
            Body(Keys((0f, V(0.6f, 0f, 0f)), (0.3f, V(0.92f, 0.7f, -0.4f)), (1f, V(0.92f, 0.7f, -0.4f)))),
            Body(Keys((0f, V(0.6f, 0f, 0f)), (0.3f, V(0.92f, 0.7f, -0.4f)), (1f, V(0.92f, 0.7f, -0.4f)))),
            Keys((0f, Vector3.Zero), (0.35f, V(0f, 0.06f, -0.22f)), (0.9f, V(0f, 0.06f, -0.22f))), Zero, Zero,
            new BreakWeights(0.3f, Joy: 1.5f), EL, ER, Scenes: S | E),

        // 雄叫び: 両腕を斜め上へ大きく広げ、頭を上げて叫ぶ（頭と腕が小さく震える）。
        new(Roar, 4.364f, null,
            Body(Wavy(Keys((0f, V(0.5f, 0.2f, 0.3f)), (0.25f, V(0.9f, 0.65f, 0.15f)), (1f, V(0.9f, 0.65f, 0.15f))), V(0.03f, 0.04f, 0f), 7f)),
            Body(Wavy(Keys((0f, V(0.5f, 0.2f, 0.3f)), (0.25f, V(0.9f, 0.65f, 0.15f)), (1f, V(0.9f, 0.65f, 0.15f))), V(0.03f, 0.04f, 0f), 7f)),
            Wavy(Keys((0f, Vector3.Zero), (0.3f, V(0f, 0.1f, -0.08f)), (0.9f, V(0f, 0.1f, -0.08f))), V(0.02f, 0.02f, 0f), 6f), Zero, Zero,
            new BreakWeights(0.2f, Joy: 3f, Hurt: -1f), EL, ER, Scenes: E),

        // 力こぶを見せる: 両腕を肩の高さで曲げて拳を頭の横へ（ダブルバイセップス）。胴を左右へひねって見せる。
        new(Flex, 4.7f, null,
            Body(Keys((0f, V(0.5f, 0.1f, 0.4f)), (0.3f, V(0.62f, 0.42f, 0.0f)), (1f, V(0.62f, 0.42f, 0.0f)))),
            Body(Keys((0f, V(0.5f, 0.1f, 0.4f)), (0.3f, V(0.62f, 0.42f, 0.0f)), (1f, V(0.62f, 0.42f, 0.0f)))),
            Keys((0f, Vector3.Zero), (0.3f, V(0f, 0.04f, 0.04f))), Around(Vector3.Zero, V(0.22f, 0f, 0f), 1f), Zero,
            new BreakWeights(0.3f, Joy: 1.5f), EL, ER, Scenes: S | E),

        // 指を天へ突き上げる（勝利の 1 本指）: 片腕をまっすぐ上へ、頭も天を向く。
        new(PointUp, 4.364f, null,
            Body(Keys((0f, V(0.45f, 0.2f, 0.3f)), (0.3f, V(0.32f, 0.97f, 0.05f)), (1f, V(0.32f, 0.97f, 0.05f)))), null,
            Keys((0f, Vector3.Zero), (0.3f, V(0f, 0.1f, -0.1f)), (0.9f, V(0f, 0.1f, -0.1f))), Zero, Zero,
            new BreakWeights(0.3f, Joy: 2f), EL, ER, Scenes: S | E),

        // 客席を見回す: 手は休みの構えのまま、体の向き・頭で観客席をゆっくり左右に見渡す。
        new(LookCrowd, 5.035f, null,
            Body(Hold(V(0.5f, -0.4f, 0.4f))), Body(Hold(V(0.5f, -0.4f, 0.4f))),
            Keys((0f, Vector3.Zero), (0.3f, V(0f, 0.04f, 0f))), Zero,
            Keys((0f, Vector3.Zero), (0.3f, V(0.8f, 0f, 0f)), (0.7f, V(-0.6f, 0f, 0f)), (1f, V(0f, 0f, 0f))),
            new BreakWeights(0.8f), EL, ER, Scenes: S),
    ];

    private static List<BreakAction> BodyActions() =>
    [
        // シャドーボクシング: 両手が交互に前へ出入りし、胴が左右へひねる。頭が小さく上下。
        new(ShadowBox, 5.035f, null,
            Body(Around(V(0.35f, 0.12f, 0.6f), V(0.04f, 0f, 0.38f), 3f)), Body(Around(V(0.35f, 0.12f, 0.6f), V(0.04f, 0f, -0.38f), 3f)),
            Around(Vector3.Zero, V(0.03f, 0.04f, 0f), 6f), Around(Vector3.Zero, V(0.25f, 0f, 0f), 3f), Zero,
            new BreakWeights(0.9f, Tired: -0.5f), EL, ER, Scenes: S | B),

        // 軽く弾む: 構えた拳を保ち、体が膝で小さく沈んで浮く（頭の上下の周期）。
        new(Bounce, 4.7f, null,
            Body(Hold(V(0.5f, 0.0f, 0.55f))), Body(Hold(V(0.5f, 0.0f, 0.55f))),
            Around(Vector3.Zero, V(0f, 0.09f, 0f), 7f), Zero, Zero,
            new BreakWeights(0.9f, Tired: -0.8f), EL, ER, Scenes: S | B),

        // 首を鳴らす: 片手を頬へ当て、頭を横へゆっくり倒して反対へ（首を左右に倒す）。
        new(NeckCrack, 4.7f, null,
            Head(Keys((0f, V(0.5f, -0.2f, 0.2f)), (0.25f, V(0.3f, -0.03f, 0.1f)), (1f, V(0.3f, -0.03f, 0.1f)))), null,
            Keys((0f, Vector3.Zero), (0.3f, V(-0.12f, 0f, 0f)), (0.55f, V(0.12f, 0f, 0f)), (0.85f, V(-0.1f, 0f, 0f)), (1f, Vector3.Zero)), Zero, Zero,
            new BreakWeights(0.7f), EL, ER, Scenes: S | B),

        // 拳を打ち合わせる: 両方のグローブを胸の前で 3 度ぶつける（離す・合わせる）。
        new(GloveClap, 4.364f, null,
            Body(Around(V(0.3f, -0.05f, 0.5f), V(0.17f, 0f, 0f), 3f)), Body(Around(V(0.3f, -0.05f, 0.5f), V(0.17f, 0f, 0f), 3f)),
            Keys((0f, Vector3.Zero), (0.3f, V(0f, -0.04f, 0.06f))), Zero, Zero,
            new BreakWeights(0.8f, Joy: 0.5f), EL, ER, Scenes: S | B),

        // マウスピースを入れる: グローブを口へ運んで押し込み、噛みしめる（頭が小さく前へ・顎を引く）。
        new(MouthGuard, 4.7f, PropKind.MouthGuard,
            Head(Keys((0f, V(-0.05f, -0.35f, 0.5f)), (0.25f, V(0f, -0.13f, 0.36f)), (0.7f, V(0f, -0.13f, 0.36f)), (1f, V(-0.1f, -0.35f, 0.5f)))), null,
            Keys((0f, Vector3.Zero), (0.3f, V(0f, -0.05f, 0.1f)), (0.9f, V(0f, -0.05f, 0.08f))), Zero, Zero,
            new BreakWeights(0.9f, Joy: 0f), EL, ER, Scenes: S, Hold: new PropHold(V(-0.1f, 0.2f, -0.97f), V(0f, 1f, 0f))),

        // 腕を上へ伸ばす: 両手を頭の上で組み、背伸びして頭を後ろへ反らせる。
        new(StretchArms, 5.035f, null,
            Body(Keys((0f, V(0.5f, 0.2f, 0.3f)), (0.3f, V(0.18f, 1f, 0.05f)), (1f, V(0.18f, 1f, 0.05f)))),
            Body(Keys((0f, V(0.5f, 0.2f, 0.3f)), (0.3f, V(0.18f, 1f, 0.05f)), (1f, V(0.18f, 1f, 0.05f)))),
            Keys((0f, Vector3.Zero), (0.3f, V(0f, 0.08f, -0.1f)), (0.9f, V(0f, 0.08f, -0.1f))), Zero, Zero,
            new BreakWeights(0.7f, Tired: 0.6f), EL, ER, Scenes: S | B),

        // 体側を伸ばす: 片腕を頭の上へ高く、体を反対側へ倒す（頭の目標が横へ）。もう片方の手は腰。
        new(SideStretch, 5.035f, null,
            Body(Keys((0f, V(0.5f, 0.2f, 0.3f)), (0.3f, V(0.05f, 1f, 0.05f)), (1f, V(0.05f, 1f, 0.05f)))), Body(Hold(OnHip)),
            Keys((0f, Vector3.Zero), (0.35f, V(-0.4f, -0.08f, 0f)), (0.9f, V(-0.4f, -0.08f, 0f))), Keys((0f, Vector3.Zero), (0.35f, V(-0.25f, 0f, 0f)), (0.9f, V(-0.25f, 0f, 0f))), Zero,
            new BreakWeights(0.6f, Tired: 0.5f), EL, ER, Scenes: S | B),

        // 前屈: 両手を床の方へ垂らし、腰から体を前へ折る（頭の目標を前・下へ大きく）。
        new(ForwardBend, 5.035f, null,
            Body(Keys((0f, V(0.5f, -0.4f, 0.3f)), (0.35f, V(0.3f, -1f, 0.45f)), (0.9f, V(0.3f, -1f, 0.45f)))),
            Body(Keys((0f, V(0.5f, -0.4f, 0.3f)), (0.35f, V(0.3f, -1f, 0.45f)), (0.9f, V(0.3f, -1f, 0.45f)))),
            Keys((0f, Vector3.Zero), (0.4f, V(0f, -0.5f, 0.6f)), (0.85f, V(0f, -0.5f, 0.6f))), Zero, Zero,
            new BreakWeights(0.4f, Tired: 0.8f), EL, ER, Scenes: S | B),

        // グローブを直す: 片手の拳でもう片方のグローブを引っ張って締め直す（両手が胸の前で組み合い、小さく引く）。
        new(FixGloves, 4.7f, null,
            Body(Around(V(0.2f, -0.2f, 0.55f), V(0.05f, 0f, 0.1f), 3f)), Body(Hold(V(0.22f, -0.22f, 0.5f))),
            Keys((0f, Vector3.Zero), (0.3f, V(0f, -0.1f, 0.1f)), (0.9f, V(0f, -0.1f, 0.1f))), Zero, Zero,
            new BreakWeights(0.6f), EL, ER, Scenes: S | B),

        // 鼻をこする: グローブの甲で鼻の下を横にこすり、頭を少し前へ（2 往復）。
        new(WipeNose, 4.028f, null,
            Head(Wavy(Keys((0f, V(0f, -0.4f, 0.5f)), (0.25f, V(0.05f, -0.1f, 0.4f)), (0.8f, V(0.05f, -0.1f, 0.4f)), (1f, V(0f, -0.4f, 0.5f))), V(0.1f, 0f, 0f), 2f)), null,
            Keys((0f, Vector3.Zero), (0.3f, V(0f, -0.05f, 0.1f)), (0.85f, V(0f, -0.05f, 0.1f))), Zero, Zero,
            new BreakWeights(0.3f), EL, ER, Scenes: S | B),

        // あくび: 片手を口へ当て、頭を後ろ・上へ大きく反らす。
        new(Yawn, 5.707f, null,
            Head(Keys((0f, V(0.2f, -0.45f, 0.4f)), (0.3f, V(-0.02f, -0.18f, 0.3f)), (0.8f, V(-0.02f, -0.18f, 0.3f)), (1f, V(0.2f, -0.45f, 0.4f)))), null,
            Keys((0f, Vector3.Zero), (0.3f, V(0f, 0.1f, -0.12f)), (0.75f, V(0f, 0.1f, -0.12f)), (1f, Vector3.Zero)), Zero, Zero,
            new BreakWeights(0.2f, Tired: 2.5f, Hurt: 1f), 1.343f, 1.175f, Scenes: B),

        // ダンス: 腰を中心に胴が左右へひねり、腕が交互に上下に揺れ、頭が拍子を取る。
        new(Dance, 5.035f, null,
            Body(Around(V(0.62f, 0.2f, 0.3f), V(0.1f, 0.25f, 0.1f), 4f)), Body(Around(V(0.62f, 0.2f, 0.3f), V(0.1f, -0.25f, 0.1f), 4f)),
            Around(Vector3.Zero, V(0.05f, 0.06f, 0f), 8f), Around(Vector3.Zero, V(0.3f, 0f, 0f), 4f), Around(Vector3.Zero, V(0.3f, 0f, 0f), 2f),
            new BreakWeights(0.3f, Joy: 0.8f), EL, ER, Scenes: S | E),
    ];

    private static List<BreakAction> MannerActions() =>
    [
        // 自撮り: スマホを顔の前に腕を伸ばして構え、頭を少し傾け、もう片方の手でピースを出す。
        new(Selfie, 5.371f, PropKind.Phone,
            Head(Keys((0f, V(-0.1f, -0.1f, 0.6f)), (0.3f, V(-0.08f, 0.05f, 0.95f)), (0.9f, V(-0.08f, 0.05f, 0.95f)), (1f, V(-0.1f, -0.1f, 0.6f)))),
            Head(Keys((0f, V(0.5f, -0.5f, 0.3f)), (0.3f, V(0.45f, 0.18f, 0.35f)), (0.9f, V(0.45f, 0.18f, 0.35f)))),
            Keys((0f, Vector3.Zero), (0.3f, V(0.05f, 0.02f, 0f)), (0.9f, V(0.05f, 0.02f, 0f))), Zero, Zero,
            new BreakWeights(0.7f, Joy: 0.6f), EL, ER, Scenes: S | B | E, Hold: new PropHold(V(0f, 1f, 0f), V(0f, -0.05f, -1f))),

        // 鏡（スマホのカメラ）で顔を見る: スマホを顔の前に構えて、頭を左右へ振って顔を確かめ、もう片方の手で頬をなでる。
        new(Mirror, 6.714f, PropKind.Phone,
            Head(Keys((0f, V(-0.1f, -0.3f, 0.6f)), (0.25f, V(-0.1f, -0.02f, 0.6f)), (0.9f, V(-0.1f, -0.02f, 0.6f)), (1f, V(-0.1f, -0.3f, 0.6f)))),
            Head(Around(V(0.35f, -0.1f, 0.2f), V(0.03f, 0.03f, 0f), 3f)),
            Keys((0f, Vector3.Zero), (0.3f, V(0f, 0f, 0.05f))), Zero,
            Keys((0f, Vector3.Zero), (0.3f, Vector3.Zero), (0.45f, V(0.25f, 0f, 0f)), (0.7f, V(-0.25f, 0f, 0f)), (0.9f, Vector3.Zero)),
            new BreakWeights(0.5f), Scenes: S | B, Hold: new PropHold(V(0f, 1f, 0f), V(0f, -0.1f, -1f))),

        // 祈る: 両手を顔の前で合わせ、頭を前へ垂れる。
        new(Prayer, 5.035f, null,
            Head(Keys((0f, V(0.4f, -0.4f, 0.4f)), (0.25f, V(0.14f, -0.18f, 0.45f)), (1f, V(0.14f, -0.18f, 0.45f)))),
            Head(Keys((0f, V(0.4f, -0.4f, 0.4f)), (0.25f, V(0.14f, -0.18f, 0.45f)), (1f, V(0.14f, -0.18f, 0.45f)))),
            Keys((0f, Vector3.Zero), (0.3f, V(0f, -0.14f, 0.18f)), (0.9f, V(0f, -0.14f, 0.18f))), Zero, Zero,
            new BreakWeights(0.6f, Joy: 0f), EL, ER, Scenes: S | E),

        // 十字を切る: 手を額・胸・左肩・右肩へ順に運ぶ（頭の基準の経路）。頭は軽く下を向く。
        new(CrossSelf, 5.371f, null,
            Head(Keys((0f, V(0.3f, -0.1f, 0.4f)), (0.2f, V(0.02f, 0.12f, 0.42f)), (0.4f, V(0.04f, -0.8f, 0.36f)), (0.62f, V(-0.4f, -0.55f, 0.34f)), (0.84f, V(0.45f, -0.55f, 0.34f)), (1f, V(0.3f, -0.2f, 0.4f)))), null,
            Keys((0f, Vector3.Zero), (0.25f, V(0f, -0.08f, 0.1f)), (0.9f, V(0f, -0.08f, 0.1f))), Zero, Zero,
            new BreakWeights(0.5f, Joy: 0f), EL, ER, Scenes: S | E),

        // 敬礼: 片手を手のひらを下にして額の横へ（眉の上）、頭は上げたまま、すぐ前へ払う。
        new(Salute, 4.028f, null,
            Head(Keys((0f, V(0.5f, -0.3f, 0.3f)), (0.25f, V(0.2f, 0.14f, 0.38f)), (0.7f, V(0.2f, 0.14f, 0.38f)), (1f, V(0.7f, -0.2f, 0.6f)))), null,
            Keys((0f, Vector3.Zero), (0.3f, V(0f, 0.04f, -0.03f)), (0.7f, V(0f, 0.04f, -0.03f))), Zero, Zero,
            new BreakWeights(0.5f), EL, ER, Scenes: S | E),

        // 相手（と観客）に拍手: 胸の前で両手を打ち合わせる（拍手の周期）。頭を軽くうなずく。
        new(Applause, 5.035f, null,
            Body(Around(V(0.3f, 0.0f, 0.45f), V(0.17f, 0f, 0f), 5f)), Body(Around(V(0.3f, 0.0f, 0.45f), V(0.17f, 0f, 0f), 5f)),
            Around(Vector3.Zero, V(0f, -0.03f, 0.04f), 2f), Zero, Zero,
            new BreakWeights(0.7f, Joy: 0.3f), EL, ER, Scenes: E | S),

        // 相手の方へ歩いて手を差し出す: 相手の胸の方へ歩み寄り、手を握手の高さへ出す（相手が立っているときだけ）。
        new(Handshake, 6.714f, null,
            Body(Keys((0f, V(0.4f, -0.2f, 0.4f)), (0.35f, V(0.1f, 0f, 0.95f)), (1f, V(0.1f, 0f, 0.95f)))), null,
            Keys((0f, Vector3.Zero), (0.3f, V(0f, -0.04f, 0.08f)), (0.9f, V(0f, -0.04f, 0.08f))), Zero, Zero,
            new BreakWeights(0.9f, Joy: 0f), 1.678f, 1.007f, Scenes: E, Stride: 0.7f, Standoff: 1.127f, NeedsPartner: true),

        // 天を仰ぐ: 手を下げて、頭を後ろへ大きく反らせて天井を見る。
        new(LookSky, 5.035f, null,
            Body(Hold(V(0.4f, -0.7f, 0.15f))), Body(Hold(V(0.4f, -0.7f, 0.15f))),
            Keys((0f, Vector3.Zero), (0.35f, V(0f, 0.14f, -0.2f)), (0.9f, V(0f, 0.14f, -0.2f))), Zero, Zero,
            new BreakWeights(0.3f, Joy: -2f, Hurt: 0.5f), EL, ER, Scenes: E),

        // 頭を抱える: 両手を頭の横・上へ当て、頭を前へうなだれる。
        new(HeadInHands, 5.371f, null,
            Head(Keys((0f, V(0.5f, -0.2f, 0.2f)), (0.25f, V(0.32f, 0.12f, 0.0f)), (1f, V(0.32f, 0.12f, 0.0f)))),
            Head(Keys((0f, V(0.5f, -0.2f, 0.2f)), (0.25f, V(0.32f, 0.12f, 0.0f)), (1f, V(0.32f, 0.12f, 0.0f)))),
            Wavy(Keys((0f, Vector3.Zero), (0.3f, V(0f, -0.2f, 0.3f)), (0.9f, V(0f, -0.2f, 0.3f))), V(0.03f, 0f, 0f), 2f), Zero, Zero,
            new BreakWeights(0.2f, Joy: -3f, Hurt: 0.5f), EL, ER, Scenes: E),

        // 膝に手をつく: 両手を膝の方へ下ろし、腰から前へ折れて息をつく（頭が前・下、息で小さく上下）。
        new(HandsOnKnees, 5.707f, null,
            Body(Keys((0f, V(0.4f, -0.5f, 0.2f)), (0.3f, V(0.32f, -1f, 0.4f)), (0.9f, V(0.32f, -1f, 0.4f)))),
            Body(Keys((0f, V(0.4f, -0.5f, 0.2f)), (0.3f, V(0.32f, -1f, 0.4f)), (0.9f, V(0.32f, -1f, 0.4f)))),
            Wavy(Keys((0f, Vector3.Zero), (0.35f, V(0f, -0.45f, 0.5f)), (0.9f, V(0f, -0.45f, 0.5f))), V(0f, 0.04f, 0f), 4f), Zero, Zero,
            new BreakWeights(0.3f, Tired: 4f, Joy: -1.5f), EL, ER, Scenes: E | B),

        // 座り込む（届く範囲で）: 腰を落として深く沈み、両手を腿へ下ろして肩を落とす。
        new(SitDown, 5.707f, null,
            Body(Keys((0f, V(0.4f, -0.5f, 0.2f)), (0.35f, V(0.45f, -1f, 0.3f)), (0.9f, V(0.45f, -1f, 0.3f)))),
            Body(Keys((0f, V(0.4f, -0.5f, 0.2f)), (0.35f, V(0.45f, -1f, 0.3f)), (0.9f, V(0.45f, -1f, 0.3f)))),
            Keys((0f, Vector3.Zero), (0.4f, V(0f, -0.95f, 0.15f)), (0.85f, V(0f, -0.95f, 0.15f))), Zero, Zero,
            new BreakWeights(0.1f, Tired: 3f, Hurt: 2f, Joy: -2f), EL, ER, Scenes: E),

        // 涙をぬぐう: 片手の甲を目へ当ててぬぐい、うつむく。
        new(WipeTears, 5.035f, null,
            Head(Wavy(Keys((0f, V(0.5f, -0.2f, 0.2f)), (0.25f, V(0.18f, 0.06f, 0.32f)), (0.85f, V(0.18f, 0.06f, 0.32f))), V(0.06f, 0f, 0f), 3f)), null,
            Keys((0f, Vector3.Zero), (0.3f, V(0f, -0.14f, 0.15f)), (0.9f, V(0f, -0.14f, 0.15f))), Zero, Zero,
            new BreakWeights(0.2f, Joy: -2f), EL, ER, Scenes: E),

        // 首を振る: 両手を腰に当て、頭を横へ大きく振って悔しがる（首の左右の目標）。
        new(ShakeHead, 4.7f, null,
            Body(Hold(OnHip)), Body(Hold(OnHip)),
            Wavy(Keys((0f, Vector3.Zero), (0.3f, V(0f, -0.06f, 0.05f)), (0.9f, V(0f, -0.06f, 0.05f))), V(0.14f, 0f, 0f), 3f), Zero, Zero,
            new BreakWeights(0.2f, Joy: -2f), EL, ER, Scenes: E),

        // 拳を打ちつける: 片手の拳を頭の上から振り下ろして体の前の空を叩く（2 度）。頭を前へ。悔しさ・怒り。
        new(SlamFist, 4.7f, null,
            Body(Keys((0f, V(0.5f, 0.1f, 0.3f)), (0.2f, V(0.4f, 0.75f, 0.2f)), (0.35f, V(0.35f, -0.7f, 0.5f)), (0.55f, V(0.4f, 0.75f, 0.2f)), (0.7f, V(0.35f, -0.7f, 0.5f)), (1f, V(0.5f, -0.3f, 0.4f)))), null,
            Keys((0f, Vector3.Zero), (0.3f, V(0f, -0.1f, 0.15f))), Keys((0f, Vector3.Zero), (0.35f, V(0.3f, 0f, 0f)), (0.7f, V(0.3f, 0f, 0f))), Zero,
            new BreakWeights(0.1f, Joy: -2f, Tired: 0.5f), EL, ER, Scenes: E),

        // タオルを頭にかぶる: タオルを頭の上へ運んでかぶり、顔をうずめるようにうつむく。
        new(TowelHead, 5.371f, PropKind.Towel,
            Head(Keys((0f, V(0.5f, -0.2f, 0.3f)), (0.3f, V(0.05f, 0.45f, 0.05f)), (1f, V(0.05f, 0.45f, 0.05f)))), null,
            Keys((0f, Vector3.Zero), (0.3f, V(0f, -0.14f, 0.18f)), (0.9f, V(0f, -0.14f, 0.18f))), Zero, Zero,
            new BreakWeights(0.3f, Tired: 1f, Joy: -1.5f), EL, ER, Scenes: E | B, Hold: new PropHold(V(1f, 0f, 0f), V(0f, 1f, 0f))),
    ];

    /// <summary>口へ運んで飲む動作（水を飲む <see cref="BreakBook.Drink"/> と同じ経路の組み立て）: 手を口へ運び、頭を反らし、口を離して下ろす。<paramref name="sips"/> 回に分けて飲む。</summary>
    private static BreakAction Sip(string name, PropKind prop, PropHold hold, float seconds, BreakWeights weights, BreakScene scenes, int sips = 1, float lean = 0.14f, float reach = 0.5f)
    {
        var hand = Keys((0f, V(-0.1f, -0.2f, reach)), (0.25f, V(-0.1f, -0.1f, reach)), (0.4f, V(-0.05f, 0.1f, reach - 0.05f)), (0.8f, V(-0.05f, 0.1f, reach - 0.05f)), (1f, V(-0.1f, -0.2f, reach)));
        var head = Keys((0f, Vector3.Zero), (0.3f, Vector3.Zero), (0.5f, V(0f, 0.08f, -lean)), (0.8f, V(0f, 0.08f, -lean)), (0.95f, Vector3.Zero));
        if (sips > 1)
        {
            head = Wavy(head, V(0f, 0.02f, -0.04f), sips);
        }

        return new BreakAction(name, seconds, prop, Head(hand), null, head, Zero, Zero, weights, Hold: hold, Scenes: scenes);
    }

    private static List<BreakAction> DrinkActions() =>
    [
        // スポーツドリンク: ボトルをあおるように、水より頭を大きく反らして飲む。
        Sip(SportsDrink, PropKind.SportsBottle, ToMouth, 9.231f, new BreakWeights(1f, Tired: 3f, Hurt: 0.5f), B | S, lean: 0.18f),

        // コーヒー（紙コップ）: 少しずつ 3 回に分けてすする（頭の小さな上下）。
        Sip(Coffee, PropKind.Cup, ToMouth, 10.07f, new BreakWeights(0.8f, Tired: 0.5f), B | S, sips: 3, lean: 0.07f),

        // エナジードリンク（缶）: 缶を大きく傾けて一気に飲む。
        Sip(EnergyDrink, PropKind.Can, ToMouth, 8.392f, new BreakWeights(0.8f, Tired: 2.5f), B | S, lean: 0.2f),

        // 牛乳パック: パックの口を口へ当てて飲む。
        Sip(Milk, PropKind.Carton, ToMouth, 9.231f, new BreakWeights(0.7f, Tired: 1f), B | S, lean: 0.12f),

        // ストローで飲む: 紙パックのストローをくわえ、頭を少し前へ傾けて吸う（頭が小さく前後）。
        new BreakAction(StrawDrink, 9.231f, PropKind.JuiceBox,
            Head(Keys((0f, V(-0.1f, -0.3f, 0.5f)), (0.25f, V(-0.08f, -0.22f, 0.4f)), (0.85f, V(-0.08f, -0.22f, 0.4f)), (1f, V(-0.1f, -0.3f, 0.5f)))), null,
            Wavy(Keys((0f, Vector3.Zero), (0.3f, V(0f, -0.04f, 0.08f)), (0.85f, V(0f, -0.04f, 0.08f))), V(0f, 0f, 0.025f), 3f), Zero, Zero,
            new BreakWeights(0.6f, Tired: 0.5f), Scenes: B | S, Hold: ToMouth),

        // ゼリー飲料: パウチの飲み口をくわえて吸い、握ってしぼる（頭を後ろへ少し）。
        new BreakAction(Jelly, 8.392f, PropKind.JellyPouch,
            Head(Wavy(Keys((0f, V(-0.1f, -0.2f, 0.5f)), (0.3f, V(-0.06f, 0.0f, 0.42f)), (0.8f, V(-0.06f, 0.0f, 0.42f)), (1f, V(-0.1f, -0.2f, 0.5f))), V(0f, 0.02f, 0f), 3f)), null,
            Keys((0f, Vector3.Zero), (0.3f, Vector3.Zero), (0.5f, V(0f, 0.06f, -0.1f)), (0.8f, V(0f, 0.06f, -0.1f)), (0.95f, Vector3.Zero)), Zero, Zero,
            new BreakWeights(0.7f, Tired: 2f), Scenes: B | S, Hold: ToMouth),

        // 口に含んで吐く: ボトルの水を口に含み、頭を前へ折ってリングの外へ霧のように吐く（口から飛沫）。
        new BreakAction(SpitWater, 10.41f, PropKind.Bottle,
            Head(Keys((0f, V(-0.1f, -0.2f, 0.5f)), (0.2f, V(-0.1f, -0.1f, 0.5f)), (0.35f, V(-0.05f, 0.1f, 0.45f)), (0.5f, V(-0.05f, 0.1f, 0.45f)), (0.7f, V(-0.2f, -0.35f, 0.55f)), (1f, V(-0.1f, -0.2f, 0.5f)))), null,
            Keys((0f, Vector3.Zero), (0.3f, Vector3.Zero), (0.45f, V(0f, 0.08f, -0.12f)), (0.55f, V(0f, 0.08f, -0.12f)), (0.7f, V(0f, -0.1f, 0.25f)), (0.85f, V(0f, -0.1f, 0.25f)), (1f, Vector3.Zero)), Zero, Zero,
            new BreakWeights(0.7f, Tired: 1.5f), Scenes: B, Hold: new PropHold(V(-0.15f, -0.45f, -0.88f), V(0f, 1f, 0f)), Vapor: new BreakVapor(VaporKind.Spit, 0.66f, 0.72f)),
    ];

    /// <summary>手の食べ物を口へ運んでかじる（かじるたびに手が小さく上下、頭が噛む）。</summary>
    private static BreakAction Bite(string name, PropKind prop, PropHold hold, BreakWeights weights, BreakScene scenes, float seconds = 9.231f, float reach = 0.36f, float bites = 3f)
    {
        var hand = Head(Wavy(Keys((0f, V(-0.1f, -0.3f, 0.5f)), (0.25f, V(-0.08f, -0.14f, reach)), (0.85f, V(-0.08f, -0.14f, reach)), (1f, V(-0.1f, -0.3f, 0.5f))), V(0f, 0.025f, 0.03f), bites));
        var head = Wavy(Keys((0f, Vector3.Zero), (0.3f, V(0f, -0.03f, 0.06f)), (0.9f, V(0f, -0.03f, 0.06f))), V(0f, 0.015f, 0.015f), bites * 2f);
        return new BreakAction(name, seconds, prop, hand, null, head, Zero, Zero, weights, Hold: hold, Scenes: scenes);
    }

    private static List<BreakAction> SnackActions() =>
    [
        Bite(Banana, PropKind.Banana, ToMouthFlat, new BreakWeights(0.8f, Tired: 1.5f), B | S),
        Bite(Onigiri, PropKind.Onigiri, ToMouthFlat, new BreakWeights(0.7f, Tired: 2f), B),
        Bite(Sandwich, PropKind.Sandwich, ToMouthFlat, new BreakWeights(0.7f, Tired: 1.5f), B),
        Bite(Chocolate, PropKind.Chocolate, ToMouthFlat, new BreakWeights(0.8f, Tired: 1f), B | S),
        Bite(Apple, PropKind.Apple, new PropHold(V(-0.3f, 0f, -0.95f), V(0f, 1f, 0f)), new BreakWeights(0.7f, Tired: 0.8f), B | S, reach: 0.34f),
        Bite(Donut, PropKind.Donut, new PropHold(V(-0.3f, 0f, -0.95f), V(0f, 0f, 1f)), new BreakWeights(0.6f, Tired: 1.2f), B, reach: 0.34f),
    ];

    private static List<BreakAction> TasteActions() =>
    [
        // たばこを吸う: たばこを口へ運んでくわえ、息を吸い込み（頭を後ろへ）、手を離して煙を吐く。煙は口から出る描画（見た目だけ）。
        new(Cigarette, 11.08f, PropKind.Cigarette,
            Head(Keys((0f, V(-0.1f, -0.3f, 0.5f)), (0.2f, V(-0.04f, -0.14f, 0.36f)), (0.45f, V(-0.04f, -0.14f, 0.36f)), (0.58f, V(0.25f, -0.4f, 0.4f)), (0.9f, V(0.25f, -0.4f, 0.4f)), (1f, V(-0.1f, -0.3f, 0.5f)))), null,
            Keys((0f, Vector3.Zero), (0.2f, V(0f, -0.04f, 0.06f)), (0.45f, V(0f, 0.04f, -0.06f)), (0.6f, V(0f, 0.12f, -0.14f)), (0.85f, V(0f, 0.06f, -0.08f)), (1f, Vector3.Zero)), Zero, Zero,
            new BreakWeights(0.2f, Tired: 0.3f), Scenes: B, Hold: new PropHold(V(-0.3f, -0.15f, -0.94f), V(0f, 1f, 0f)), Vapor: new BreakVapor(VaporKind.Smoke, 0.5f, 0.62f)),

        // ガムを噛む: 手を口へ当ててガムを入れ、そのあと口を閉じたまま頭が小さく前後・上下に噛む（咀嚼の周期）。
        new(Gum, 8.392f, null,
            Head(Keys((0f, V(0.3f, -0.4f, 0.4f)), (0.15f, V(-0.02f, -0.16f, 0.38f)), (0.3f, V(0.3f, -0.4f, 0.4f)))), Body(Hold(OnHip)),
            Wavy(Keys((0f, Vector3.Zero), (0.4f, V(0f, 0.03f, 0.03f))), V(0.012f, 0.025f, 0.025f), 12f), Around(Vector3.Zero, V(0.04f, 0f, 0f), 2f), Zero,
            new BreakWeights(0.6f, Tired: -0.3f), Scenes: B | S),

        // ヘッドホンをして首でリズムを取る: 片手で耳の当てを押さえ（付ける）、そのあと頭が拍子に合わせて縦・横に揺れる。
        new(Headphones, 9.231f, PropKind.Headphones,
            Head(Keys((0f, V(0.5f, -0.3f, 0.2f)), (0.15f, V(0.36f, -0.02f, 0.02f)), (0.3f, V(0.36f, -0.02f, 0.02f)), (0.4f, V(0.5f, -0.45f, 0.3f)), (1f, V(0.5f, -0.45f, 0.3f)))),
            Body(Around(V(0.55f, -0.5f, 0.3f), V(0f, 0.03f, 0.05f), 5f)),
            Wavy(Keys((0f, Vector3.Zero), (0.4f, Vector3.Zero), (0.5f, V(0.02f, 0f, 0f))), V(0.04f, 0.05f, 0f), 6f), Around(Vector3.Zero, V(0.12f, 0f, 0f), 3f), Zero,
            new BreakWeights(0.5f, Tired: -0.2f), Scenes: B | S),

        // イヤホンを耳に入れる: 片手を耳へ運んで入れ、手を耳に添えて聴き入り、頭が小さくリズムを取る。
        new(Earbud, 8.392f, PropKind.Earbud,
            Head(Keys((0f, V(0.5f, -0.3f, 0.2f)), (0.2f, V(0.3f, -0.03f, 0.04f)), (0.9f, V(0.3f, -0.03f, 0.04f)), (1f, V(0.5f, -0.3f, 0.2f)))), null,
            Wavy(Keys((0f, Vector3.Zero), (0.3f, V(0.04f, 0f, 0f))), V(0f, 0.04f, 0.015f), 5f), Zero, Zero,
            new BreakWeights(0.5f), Scenes: B | S, Hold: new PropHold(V(-0.9f, -0.3f, 0.05f), V(0f, 1f, 0f), V(-0.008f, 0f, 0f))),

        // 口ずさむ: 手は構えのまま、頭が左右・前後にゆったり揺れ、手が指で拍子を取る（口ずさむような頭の動き）。
        new(Hum, 8.392f, null,
            Body(Around(V(0.55f, -0.45f, 0.3f), V(0f, 0.04f, 0.05f), 4f)), null,
            Wavy(Keys((0f, Vector3.Zero), (0.3f, Vector3.Zero)), V(0.07f, 0.03f, 0f), 3f), Around(Vector3.Zero, V(0.1f, 0f, 0f), 1.5f), Zero,
            new BreakWeights(0.6f, Tired: -0.2f), Scenes: B | S),
    ];
}
