namespace MiniRing.Bots;

/// <summary>
/// 名前で選べるボットの一覧。<c>legacy</c> は最初の単純な CPU（<see cref="AiControl"/>）で、改善の基準線として残す。
/// ほかはボクシングの代表的なスタイル。改善（docs/BOT_PDCA.md）の結果は <c>champion</c> に反映する。
/// </summary>
public static class BotCatalog
{
    public const string Legacy = "legacy";

    /// <summary>ゲームの CPU に使うボット。</summary>
    public const string DefaultCpu = "champion";

    /// <summary>バランス型（ボクサーパンチャー）: ジャブで組み立てて 1-2-3、守りも一通り。</summary>
    public static BotStyle Boxer { get; } = new() { Name = "boxer", Telegraph = 0.08f, Prediction = 0.5f, EdgeRange = 0.3f };

    /// <summary>アウトボクサー: 遠い間合いからジャブ、打ったら離れる、回る、スウェーとバックステップで外す。</summary>
    public static BotStyle OutBoxer { get; } = new()
    {
        Name = "outboxer",
        Aim = new(Temple: 0.5f, GuardArm: 0.6f),
        Telegraph = 0.06f, Prediction = 0.6f, EdgeRange = 0.7f,
        RangePress = 0.5f, RangeGuard = 0.6f, // 間合いの層（R3）: 外から打つので打って解消は弱め、入られたら構えを上げる
        Aggression = 0.45f,
        AttackRange = 1.02f,
        NeutralRange = 1.5f,
        EngageRange = 1.75f,
        ComboWeights = new Dictionary<string, float> { ["1"] = 4f, ["1-1"] = 2f, ["1-2"] = 3f, ["1-1-2"] = 1f, ["f-1-2"] = 1f, ["1b-2"] = 0.5f },
        ExitAfterCombo = 0.85f,
        Circling = 0.75f,
        HeadMovement = 0.3f,
        GuardHeight = 0.4f,
        DefenseRate = 0.65f,
        BlockWeight = 0.5f, DuckWeight = 0.2f, SlipWeight = 0.6f, SwayWeight = 1f, StepBackWeight = 1.2f,
        CounterRate = 0.45f,
        ClinchRate = 0.1f,
        ThrowRate = 0f,
    };

    /// <summary>
    /// インファイター（スウォーマー）: 頭を振りながら詰め、胴とフックの連打。攻めの値は最初の手書きのまま、守りは回避編で磨いた
    /// 回避技（ウィービング・ダッキング・ローリング・インサイドスリップなど）と、回避しながら打つカウンターの表を持つ。
    /// 回避の間合いで戦うスタイルなので、磨いた回避がいちばん効いた（勝率 6% → 14%、元の版に 6-1-3。docs/BOT_PDCA.md 回避編）。
    /// 値が多いので JSON（Bots/swarmer.json）を組み込んで読む。
    /// </summary>
    public static BotStyle Swarmer { get; } = LoadEmbedded("MiniRing.Bots.swarmer.json");

    /// <summary>カウンターパンチャー: 手数は少なく、守り切ってから打ち返す。</summary>
    public static BotStyle Counter { get; } = new()
    {
        Name = "counter",
        Aim = new(Chin: 2f, ExposedBrain: 2f),
        Telegraph = 0.06f, Prediction = 0.9f, EdgeRange = 0.6f,
        RangePress = 0.5f, RangeGuard = 0.8f, // 間合いの層（R3）: 待って返すので打って解消は弱め、構えは厚く
        Aggression = 0.2f,
        NeutralRange = 1.25f,
        ComboWeights = new Dictionary<string, float> { ["1"] = 3f, ["1-2"] = 2f, ["f-2"] = 2f, ["f-3"] = 1f },
        ExitAfterCombo = 0.5f,
        Circling = 0.5f,
        HeadMovement = 0.5f,
        GuardHeight = 0.7f,
        DefenseRate = 0.8f,
        BlockWeight = 0.6f, DuckWeight = 0.8f, SlipWeight = 1.3f, SwayWeight = 1f, StepBackWeight = 0.4f,
        CounterRate = 0.95f,
        ClinchRate = 0.2f,
        ThrowRate = 0f,
    };

    /// <summary>スラッガー: 守りは薄く、大きなフック・アッパー・オーバーハンドで倒しに行く。</summary>
    public static BotStyle Slugger { get; } = new()
    {
        Name = "slugger",
        Aim = new(Chin: 1f, Temple: 1f, ExposedBrain: 4f),
        Telegraph = 0.12f, Prediction = 0.2f, EdgeRange = 0f,
        RangePress = 2f, RangeGuard = 0.3f, // 間合いの層（R3）: 前へ出て打ち合う。居続けたら強く打って解消、構えは薄め
        Aggression = 0.7f,
        AttackRange = 0.9f,
        NeutralRange = 1.2f,
        PunchSpeed = 0.9f,
        ComboWeights = new Dictionary<string, float> { ["1-oh"] = 3f, ["3-4"] = 3f, ["6-3"] = 2f, ["2-3-2"] = 2f, ["4b-3"] = 1f, ["1-2"] = 1f },
        ExitAfterCombo = 0.15f,
        Circling = 0.15f,
        HeadMovement = 0.2f,
        GuardHeight = 0.5f,
        DefenseRate = 0.3f,
        BlockWeight = 1f, DuckWeight = 0.3f, SlipWeight = 0.1f, SwayWeight = 0.1f, StepBackWeight = 0.2f,
        CounterRate = 0.3f,
        ClinchRate = 0.05f,
        ThrowRate = 0.1f,
    };

    /// <summary>
    /// ジャブ連打のアウトボクサー（docs/BOT_PDCA.md のサイクル 5 で outboxer から自動調整した tuned5）。総当たりの勝率がいちばん高いが、
    /// 技はほぼジャブとストレートだけ。物理ではジャブだけが相手の構えの外を通るため、強さだけを求めるとこうなる。
    /// </summary>
    public static BotStyle Jabber { get; } = new()
    {
        Name = "jabber",
        Aim = new(Chin: 0.5f, GuardArm: 1.5f),
        Telegraph = 0.04f, Prediction = 0.4f, EdgeRange = 0.5f,
        RangePress = 1f, RangeGuard = 0.4f, // 間合いの層（R3）: 手数で解消、構えは中くらい
        Aggression = 0.69988924f,
        AttackRange = 1.2508752f,
        NeutralRange = 1.627571f,
        EngageRange = 1.8138998f,
        PunchSpeed = 1.15f,
        ComboWeights = new Dictionary<string, float> { ["1-1"] = 20.36468f, ["1-2"] = 4.931102f, ["1-1-2"] = 1f, ["1b-2"] = 1.0986178f, ["f-2"] = 0.5544842f, ["5-2"] = 1f, ["1-2-3b"] = 1f, ["f-3"] = 1f },
        ExitAfterCombo = 0.90590143f,
        ThrowRate = 0f,
        Circling = 0.7468302f,
        HeadMovement = 0.24064939f,
        GuardHeight = 0.8652824f,
        ReactionTime = 0.17016897f,
        DefenseRate = 0.42078748f,
        BlockWeight = 0.42193776f, DuckWeight = 0.18083662f, SlipWeight = 0.6066941f, SwayWeight = 1.1851302f, StepBackWeight = 0.85980237f,
        CounterRate = 0.49122202f,
        ClinchRate = 0.20167097f,
    };

    /// <summary>
    /// 1 代目の champion（docs/BOT_PDCA.md のサイクル 7 で boxer から自動調整した tuned7）。手数を最大にした攻めのボクサーで、
    /// 1-2 と 1-2-3 を軸に、アッパーからのフックも混ぜる。守りは手書きのままで、動き出しも読まない。
    /// 防御編で <see cref="Champion"/>（2 代目）に代わった。比べる基準として残す。
    /// </summary>
    public static BotStyle ChampionV1 { get; } = new()
    {
        Name = "champion-v1",
        Telegraph = 0.06f, Prediction = 0f, EdgeRange = 0f,
        RangePress = 1f, RangeGuard = 0.6f, // 間合いの層（R3）
        Aggression = 1f,
        AttackRange = 1.001754f,
        NeutralRange = 1.3007601f,
        EngageRange = 1.546155f,
        PunchSpeed = 1.15f,
        ComboWeights = new Dictionary<string, float>
        {
            ["1"] = 2f, ["1-2"] = 9.522364f, ["1-1-2"] = 2.3676612f, ["1-2-3"] = 4.60275f, ["1b-2"] = 0.36766908f,
            ["6-3"] = 0.6971435f, ["f-2"] = 0.5f, ["1-1"] = 1f, ["1-2-3-2"] = 1f, ["5-2"] = 1f,
        },
        ReadGuard = 0.051482067f,
        ExitAfterCombo = 0.15900455f,
        ThrowRate = 0f,
        Circling = 0.24455619f,
        HeadMovement = 0.47342592f,
        GuardHeight = 0.63381124f,
        ReactionTime = 0.20958243f,
        DefenseRate = 0.65155447f,
        BlockWeight = 0.9744193f, DuckWeight = 0.3500253f, SlipWeight = 0.6630055f, SwayWeight = 0.44248554f, StepBackWeight = 0.21706587f,
        CounterRate = 0.76079005f,
        ClinchRate = 0.3508795f,
    };

    /// <summary>
    /// ゲームの CPU（2 代目）。攻めは <see cref="ChampionV1"/> のまま、防御技を防御ドリルで技ごとに最適化し（<see cref="DefenseBook"/>）、
    /// 守りと間合いの値（動き出しの読み・守る率・技の好み・ストップジャブ・ピボット・距離）を実戦で調整した（docs/BOT_PDCA.md 防御編）。
    /// 腕の長さ 1.3 倍の外から打ち、踏み込まれたらストップジャブ、打ったら出る間合いの名手。総当たりで 70-0-0（1 代目にも
    /// <see cref="Jabber"/> にも 10 戦全勝）、被ダメージは 1 代目より 39% 少ない。値が多いので JSON（Bots/champion.json）を組み込んで読む。
    /// </summary>
    public static BotStyle Champion { get; } = LoadEmbedded("MiniRing.Bots.champion.json");

    public static IReadOnlyList<BotStyle> Styles { get; } = [Boxer, OutBoxer, Swarmer, Counter, Slugger, Jabber, ChampionV1, Champion];

    private static BotStyle LoadEmbedded(string resource)
    {
        using var stream = typeof(BotCatalog).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"組み込みのスタイル {resource} が見つかりません（MiniRing.csproj の EmbeddedResource）。");
        using var reader = new StreamReader(stream);
        return BotStyle.FromJson(reader.ReadToEnd());
    }

    public static IEnumerable<string> Names => Styles.Select(s => s.Name).Prepend(Legacy);

    public static BotStyle Style(string name) => Styles.FirstOrDefault(s => s.Name == name)
        ?? throw new ArgumentException($"ボット '{name}' はありません（{string.Join(", ", Names)}）。", nameof(name));

    /// <summary>名前のボットを作る（<c>legacy</c> は <see cref="AiControl"/>）。</summary>
    public static IFighterControl Create(string name, int seed)
        => name == Legacy ? new AiControl(seed, 0.5f) : new BoxerBot(Style(name), seed);

    /// <summary>CPU 側を名前のボットにした両コーナー（プレイヤー側は人）。</summary>
    public static BoxingCorners Corners(string cpuName)
    {
        ArgumentException.ThrowIfNullOrEmpty(cpuName);
        return cpuName == Legacy ? new BoxingCorners(Legacy, seed => new AiControl(seed, 0.5f)) : Corners(Style(cpuName));
    }

    /// <summary>CPU 側をこのスタイルのボットにした両コーナー。スタイルの JSON も持たせる（リプレイ用）。</summary>
    public static BoxingCorners Corners(BotStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        var validated = style.Validated();
        return new BoxingCorners(validated.Name, seed => new BoxerBot(validated, seed), CpuStyleJson: validated.ToJson());
    }
}
