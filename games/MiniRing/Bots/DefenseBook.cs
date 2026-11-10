namespace MiniRing.Bots;

/// <summary>
/// 1 つの防御技の動き（長さは腕の長さ単位、体フレームのローカル）。値はすべて float で JSON に読み書きでき、
/// 防御ドリル（<c>DefenseDrill</c>）で技ごとに最適化する。
/// <para>
/// 「側」は来る拳から離れる向き（拳が自分の左から来れば右）。頭の X・足の横移動はこの側を正とする。
/// </para>
/// </summary>
public sealed record DefenseMove
{
    /// <summary>
    /// 横へ抜ける動き（ウィービング）か。真なら頭は <see cref="HeadX"/> から <see cref="EndX"/> へ U 字に沈みながら動き、
    /// 偽なら守りの間ずっと (<see cref="HeadX"/>, <see cref="HeadY"/>, <see cref="HeadZ"/>) に留まる（<see cref="EndX"/> は使わない）。
    /// 以前は「EndX と HeadX が違えば U 字」と推し量っていたため、最適化で値がわずかに揺れただけで全技が U 字になっていた（レビュー指摘）。
    /// </summary>
    public bool Sweep { get; init; }

    /// <summary>守りの始めの頭の横（側の向き）。</summary>
    public float HeadX { get; init; }

    /// <summary>
    /// 頭の沈み（負で下）。ウィービングでは軌道の途中で最も深くなる量（U 字）、それ以外は守りの間の高さ。
    /// </summary>
    public float HeadY { get; init; }

    /// <summary>頭の前後（正で前・負で後ろへ反る）。</summary>
    public float HeadZ { get; init; }

    /// <summary>守りの終わりの頭の横（側の向き）。<see cref="Sweep"/> のときだけ使う。</summary>
    public float EndX { get; init; }

    /// <summary>守りを続ける時間 [s]（拳が向かってくる間は延ばす）。</summary>
    public float Duration { get; init; } = 0.35f;

    /// <summary>足の前後（移動指令の前向き成分、負で下がる）。</summary>
    public float Move { get; init; }

    /// <summary>足の横（移動指令の側向き成分）。</summary>
    public float Step { get; init; }

    /// <summary>頭が目標へ寄る速さ [1/s]（大きいほど鋭く動く。体の軸の傾きとして伝わる）。</summary>
    public float Rate { get; init; } = 26f;

    /// <summary>両拳を顔の前へ上げる度合い（0 = 構えのまま … 1 = 顔を覆う）。ブロックは常に 1。</summary>
    public float HandsUp { get; init; }

    /// <summary>顔を覆う拳の幅（左右の拳の間隔の半分）。</summary>
    public float GloveWidth { get; init; } = 0.1f;

    /// <summary>顔を覆う拳の高さ（頭の中心から）。</summary>
    public float GloveHeight { get; init; } = -0.03f;

    /// <summary>顔を覆う拳の前（頭の中心から）。</summary>
    public float GloveForward { get; init; } = 0.3f;

    /// <summary>胴への拳には、拳をこれだけ下げて肘で脇腹を締める（ブロック）。</summary>
    public float BodyDrop { get; init; } = 0.55f;

    /// <summary>フックの来る側の拳を、こめかみを覆うようにここまで外へ開く。</summary>
    public float TempleWidth { get; init; } = 0.22f;

    /// <summary>パンチの種類ごとの効き目（0 = 使わない）。守りを選ぶときの重みに掛ける。防御ドリルの結果から決める。</summary>
    public float VsStraight { get; init; }
    public float VsHook { get; init; }
    public float VsUppercut { get; init; }
    public float VsBody { get; init; }

    public float Against(PunchKind kind) => kind switch
    {
        PunchKind.Straight => VsStraight,
        PunchKind.Hook => VsHook,
        PunchKind.Uppercut => VsUppercut,
        _ => VsBody,
    };

    public DefenseMove Validated()
    {
        static float F(float v, float min, float max, float fallback) => Math.Clamp(float.IsFinite(v) ? v : fallback, min, max);
        var headX = F(HeadX, -0.6f, 0.6f, 0f);
        return this with
        {
            HeadX = headX,
            HeadY = F(HeadY, -0.7f, 0.2f, 0f),
            HeadZ = F(HeadZ, -0.5f, 0.4f, 0f),
            EndX = Sweep ? F(EndX, -0.6f, 0.6f, 0f) : headX, // 横へ抜けない技では使わないので、始めと同じにそろえる
            Duration = F(Duration, 0.15f, 0.7f, 0.35f),
            Move = F(Move, -1f, 1f, 0f),
            Step = F(Step, -1f, 1f, 0f),
            Rate = F(Rate, 6f, 60f, 26f),
            HandsUp = F(HandsUp, 0f, 1f, 0f),
            GloveWidth = F(GloveWidth, 0f, 0.35f, 0.1f),
            GloveHeight = F(GloveHeight, -0.3f, 0.2f, -0.03f),
            GloveForward = F(GloveForward, 0.05f, 0.5f, 0.3f),
            BodyDrop = F(BodyDrop, 0.2f, 0.9f, 0.55f),
            TempleWidth = F(TempleWidth, 0f, 0.4f, 0.22f),
            VsStraight = F(VsStraight, 0f, 1f, 0f),
            VsHook = F(VsHook, 0f, 1f, 0f),
            VsUppercut = F(VsUppercut, 0f, 1f, 0f),
            VsBody = F(VsBody, 0f, 1f, 0f),
        };
    }
}

/// <summary>来る拳の種類（防御の選び分けに使う）。</summary>
public enum PunchKind
{
    Straight,
    Hook,
    Uppercut,
    Body,
}

/// <summary>
/// 防御技の一覧（技 → 動き）。<see cref="Default"/> は最初の手書きの値で、防御ドリルで最適化した値は
/// <see cref="BotCatalog"/> のスタイルが持つ。
/// </summary>
public sealed record DefenseBook
{
    public DefenseMove Block { get; init; } = new()
    {
        Duration = 0.35f, HandsUp = 1f,
        VsStraight = 1f, VsHook = 1f, VsUppercut = 1f, VsBody = 1f,
    };

    public DefenseMove Duck { get; init; } = new()
    {
        HeadY = -0.5f, HeadZ = 0.18f, Duration = 0.4f,
        VsStraight = 1f, VsHook = 1f,
    };

    /// <summary>ウィービング: 頭を U 字に沈めながら反対側へ抜ける（フックの下をくぐる）。</summary>
    public DefenseMove Weave { get; init; } = new()
    {
        // 真ん中から沈み、来る拳と反対の側へ抜ける（拳の方へ頭を寄せてから沈むと、自分から当たりに行く）。
        Sweep = true, HeadX = 0f, EndX = 0.3f, HeadY = -0.45f, HeadZ = 0.12f, Duration = 0.45f,
        VsHook = 1f, VsStraight = 0.5f,
    };

    public DefenseMove Slip { get; init; } = new()
    {
        HeadX = 0.42f, EndX = 0.42f, HeadY = -0.1f, HeadZ = 0.08f, Duration = 0.3f,
        VsStraight = 1f,
    };

    public DefenseMove Sway { get; init; } = new()
    {
        HeadY = 0.02f, HeadZ = -0.38f, Move = -0.4f, Duration = 0.35f,
        VsStraight = 1f, VsHook = 1f, VsUppercut = 1f,
    };

    public DefenseMove StepBack { get; init; } = new()
    {
        HeadZ = -0.12f, Move = -1f, Duration = 0.4f,
        VsStraight = 1f, VsHook = 1f, VsUppercut = 1f, VsBody = 1f,
    };

    /// <summary>サイドステップ: 足で横へ出て拳の線から外れる（角度を取る）。</summary>
    public DefenseMove SideStep { get; init; } = new()
    {
        HeadX = 0.15f, EndX = 0.15f, Step = 1f, Move = -0.2f, Duration = 0.35f,
        VsStraight = 1f, VsHook = 0.5f, VsUppercut = 0.5f, VsBody = 1f,
    };

    public static DefenseBook Default { get; } = new();

    public DefenseMove Of(Defense defense) => defense switch
    {
        Defense.Block => Block,
        Defense.Duck => Duck,
        Defense.Weave => Weave,
        Defense.Slip => Slip,
        Defense.Sway => Sway,
        Defense.StepBack => StepBack,
        Defense.SideStep => SideStep,
        _ => throw new ArgumentOutOfRangeException(nameof(defense), defense, null),
    };

    /// <summary>1 つの技の動きだけ差し替えた本。</summary>
    public DefenseBook With(Defense defense, DefenseMove move) => defense switch
    {
        Defense.Block => this with { Block = move },
        Defense.Duck => this with { Duck = move },
        Defense.Weave => this with { Weave = move },
        Defense.Slip => this with { Slip = move },
        Defense.Sway => this with { Sway = move },
        Defense.StepBack => this with { StepBack = move },
        Defense.SideStep => this with { SideStep = move },
        _ => throw new ArgumentOutOfRangeException(nameof(defense), defense, null),
    };

    /// <summary>防御技の全部（<see cref="Defense.None"/> を除く）。</summary>
    public static IReadOnlyList<Defense> Techniques { get; } = [.. Enum.GetValues<Defense>().Where(d => d != Defense.None)];

    public DefenseBook Validated()
    {
        var book = this;
        foreach (var d in Techniques)
        {
            book = book.With(d, (book.Of(d) ?? DefenseBook.Default.Of(d)).Validated());
        }

        return book with { Block = book.Block with { HandsUp = 1f } };
    }
}
