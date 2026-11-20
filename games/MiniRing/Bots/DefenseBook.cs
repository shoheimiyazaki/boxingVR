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

    /// <summary>
    /// ストレートには、拳の<b>外側</b>（打ってくる腕の肩の側）へ頭を滑り込ませるか。偽なら拳から離れる側（内側）。
    /// 外側へ外すと、相手の腕が自分と相手の体の間に来て、相手の反対の手が届きにくい。フックやアッパーには使わない
    /// （フックの外側は拳の来る側そのもの）。
    /// </summary>
    public bool Outside { get; init; }

    /// <summary>
    /// 守りの間、拳を構えの頭の高さ（守りを始めた時の頭の位置）に残す度合い（0 = 頭と一緒に下がる … 1 = 構えの高さに残る）。
    /// 1 に近いほど、沈んだ頭は自分のブロック（上げた拳）の下に入る。
    /// </summary>
    public float GloveAnchor { get; init; }

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
    public float Duration { get; init; } = 0.5874f;

    /// <summary>足の前後（移動指令の前向き成分、負で下がる）。</summary>
    public float Move { get; init; }

    /// <summary>足の横（移動指令の側向き成分）。</summary>
    public float Step { get; init; }

    /// <summary>頭が目標へ寄る速さ [1/s]（大きいほど鋭く動く。体の軸の傾きとして伝わる）。</summary>
    public float Rate { get; init; } = 15.49f;

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

    /// <summary>
    /// 回避からのカウンター: 拳の種類ごとの打ち返し（<see cref="Combo"/> の名前。空なら守り方ごとの決まった返し）と、
    /// 守りの進み具合のどこで打ち始めるか（0..1。1 は守り終えてから、1 未満は回避しながら打つ）。カウンター研究で決める。
    /// </summary>
    public string CounterStraight { get; init; } = "";
    public string CounterHook { get; init; } = "";
    public string CounterUppercut { get; init; } = "";
    public float CounterStraightAt { get; init; } = 1f;
    public float CounterHookAt { get; init; } = 1f;
    public float CounterUppercutAt { get; init; } = 1f;

    /// <summary>その拳への打ち返しの型と打ち始め（型が空なら null = 守り方ごとの決まった返しを守り終えてから）。</summary>
    public (string Combo, float At)? CounterFor(PunchKind kind)
    {
        var (combo, at) = kind switch
        {
            PunchKind.Straight => (CounterStraight, CounterStraightAt),
            PunchKind.Hook => (CounterHook, CounterHookAt),
            PunchKind.Uppercut => (CounterUppercut, CounterUppercutAt),
            _ => ("", 1f),
        };
        return string.IsNullOrEmpty(combo) ? null : (combo, at);
    }

    public float Against(PunchKind kind) => kind switch
    {
        PunchKind.Straight => VsStraight,
        PunchKind.Hook => VsHook,
        PunchKind.Uppercut => VsUppercut,
        _ => VsBody,
    };

    public DefenseMove Validated()
    {
        var headX = DefenseRanges.HeadX.Clamp(HeadX, 0f);
        foreach (var combo in new[] { CounterStraight, CounterHook, CounterUppercut })
        {
            if (!string.IsNullOrEmpty(combo))
            {
                _ = Combo.Named(combo); // 知らない型は読む時点で断る
            }
        }

        return this with
        {
            CounterStraight = CounterStraight ?? "",
            CounterHook = CounterHook ?? "",
            CounterUppercut = CounterUppercut ?? "",
            CounterStraightAt = DefenseRanges.CounterAt.Clamp(CounterStraightAt, 1f),
            CounterHookAt = DefenseRanges.CounterAt.Clamp(CounterHookAt, 1f),
            CounterUppercutAt = DefenseRanges.CounterAt.Clamp(CounterUppercutAt, 1f),
            HeadX = headX,
            HeadY = DefenseRanges.HeadY.Clamp(HeadY, 0f),
            HeadZ = DefenseRanges.HeadZ.Clamp(HeadZ, 0f),
            EndX = Sweep ? DefenseRanges.HeadX.Clamp(EndX, 0f) : headX, // 横へ抜けない技では使わないので、始めと同じにそろえる
            Duration = DefenseRanges.Duration.Clamp(Duration, 0.5874f),
            Move = DefenseRanges.Move.Clamp(Move, 0f),
            Step = DefenseRanges.Step.Clamp(Step, 0f),
            Rate = DefenseRanges.Rate.Clamp(Rate, 15.49f),
            HandsUp = DefenseRanges.HandsUp.Clamp(HandsUp, 0f),
            GloveAnchor = DefenseRanges.GloveAnchor.Clamp(GloveAnchor, 0f),
            GloveWidth = DefenseRanges.GloveWidth.Clamp(GloveWidth, 0.1f),
            GloveHeight = DefenseRanges.GloveHeight.Clamp(GloveHeight, -0.03f),
            GloveForward = DefenseRanges.GloveForward.Clamp(GloveForward, 0.3f),
            BodyDrop = DefenseRanges.BodyDrop.Clamp(BodyDrop, 0.55f),
            TempleWidth = DefenseRanges.TempleWidth.Clamp(TempleWidth, 0.22f),
            VsStraight = DefenseRanges.Effect.Clamp(VsStraight, 0f),
            VsHook = DefenseRanges.Effect.Clamp(VsHook, 0f),
            VsUppercut = DefenseRanges.Effect.Clamp(VsUppercut, 0f),
            VsBody = DefenseRanges.Effect.Clamp(VsBody, 0f),
        };
    }
}

/// <summary>
/// 防御技の値の範囲（<see cref="DefenseMove.Validated"/> が収める幅）。<c>Mutation.Move</c> も同じ表を参照する（範囲は 1 か所。
/// 今は両者の範囲が一致している）。
/// </summary>
public static class DefenseRanges
{
    /// <summary>頭の横（始め・終わり）。0.6 腕長は体幹を傾けきった量。</summary>
    public static ValueRange HeadX { get; } = new(nameof(DefenseMove.HeadX), -0.6f, 0.6f);

    public static ValueRange HeadY { get; } = new(nameof(DefenseMove.HeadY), -0.7f, 0.2f);

    public static ValueRange HeadZ { get; } = new(nameof(DefenseMove.HeadZ), -0.5f, 0.4f);

    /// <summary>守りを続ける時間 [s]。</summary>
    public static ValueRange Duration { get; } = new(nameof(DefenseMove.Duration), 0.2518f, 1.175f);

    public static ValueRange Move { get; } = new(nameof(DefenseMove.Move), -1f, 1f);

    public static ValueRange Step { get; } = new(nameof(DefenseMove.Step), -1f, 1f);

    public static ValueRange Rate { get; } = new(nameof(DefenseMove.Rate), 3.575f, 35.75f);

    public static ValueRange HandsUp { get; } = new(nameof(DefenseMove.HandsUp), 0f, 1f);

    public static ValueRange GloveAnchor { get; } = new(nameof(DefenseMove.GloveAnchor), 0f, 1f);

    public static ValueRange GloveWidth { get; } = new(nameof(DefenseMove.GloveWidth), 0f, 0.35f);

    public static ValueRange GloveHeight { get; } = new(nameof(DefenseMove.GloveHeight), -0.3f, 0.2f);

    public static ValueRange GloveForward { get; } = new(nameof(DefenseMove.GloveForward), 0.05f, 0.5f);

    public static ValueRange BodyDrop { get; } = new(nameof(DefenseMove.BodyDrop), 0.2f, 0.9f);

    public static ValueRange TempleWidth { get; } = new(nameof(DefenseMove.TempleWidth), 0f, 0.4f);

    /// <summary>拳の種類ごとの効き目（Vs*）。</summary>
    public static ValueRange Effect { get; } = new("Vs", 0f, 1f);

    /// <summary>カウンターを打ち始める守りの進み具合。</summary>
    public static ValueRange CounterAt { get; } = new("CounterAt", 0.2f, 1f);
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
        Duration = 0.5874f, HandsUp = 1f,
        VsStraight = 1f, VsHook = 1f, VsUppercut = 1f, VsBody = 1f,
    };

    public DefenseMove Duck { get; init; } = new()
    {
        HeadY = -0.5f, HeadZ = 0.18f, Duration = 0.6714f,
        VsStraight = 1f, VsHook = 1f,
    };

    /// <summary>ウィービング: 頭を U 字に沈めながら反対側へ抜ける（フックの下をくぐる）。</summary>
    public DefenseMove Weave { get; init; } = new()
    {
        // 真ん中から沈み、来る拳と反対の側へ抜ける（拳の方へ頭を寄せてから沈むと、自分から当たりに行く）。
        Sweep = true, HeadX = 0f, EndX = 0.3f, HeadY = -0.45f, HeadZ = 0.12f, Duration = 0.7553f,
        VsHook = 1f, VsStraight = 0.5f,
    };

    public DefenseMove Slip { get; init; } = new()
    {
        HeadX = 0.42f, EndX = 0.42f, HeadY = -0.1f, HeadZ = 0.08f, Duration = 0.5035f,
        VsStraight = 1f,
    };

    public DefenseMove Sway { get; init; } = new()
    {
        HeadY = 0.02f, HeadZ = -0.38f, Move = -0.4f, Duration = 0.5874f,
        VsStraight = 1f, VsHook = 1f, VsUppercut = 1f,
    };

    public DefenseMove StepBack { get; init; } = new()
    {
        HeadZ = -0.12f, Move = -1f, Duration = 0.6714f,
        VsStraight = 1f, VsHook = 1f, VsUppercut = 1f, VsBody = 1f,
    };

    /// <summary>サイドステップ: 足で横へ出て拳の線から外れる（角度を取る）。</summary>
    public DefenseMove SideStep { get; init; } = new()
    {
        HeadX = 0.15f, EndX = 0.15f, Step = 1f, Move = -0.2f, Duration = 0.5874f,
        VsStraight = 1f, VsHook = 0.5f, VsUppercut = 0.5f, VsBody = 1f,
    };

    /// <summary>インサイドスリップ: スリップしながら半歩踏み込む（外した拳の内側で打ち返す距離に入る）。</summary>
    public DefenseMove SlipIn { get; init; } = new()
    {
        HeadX = 0.38f, EndX = 0.38f, HeadY = -0.15f, HeadZ = 0.15f, Move = 0.4f, Duration = 0.5035f,
        VsStraight = 1f,
    };

    /// <summary>ローリング: 真ん中から、来る拳の進む向き（拳から離れる側）へ沈みながら頭を流す。</summary>
    public DefenseMove Roll { get; init; } = new()
    {
        Sweep = true, HeadX = 0f, EndX = 0.45f, HeadY = -0.3f, HeadZ = -0.05f, Move = -0.2f, Duration = 0.5874f, HandsUp = 0.5f,
        VsHook = 1f, VsStraight = 0.5f,
    };

    public DefenseMove Parry { get; init; } = new()
    {
        Duration = 0.5874f,
        // karatelab（docs/arena/gen12_karatelab_parry_v3_forearm.md）の測定: パリーは来る拳の弧（フック）の頭への当たりを 0.92〜0.95 → 0.73〜0.78 に減らすが、
        // ストレート・ジャブの直線は変えられない（1.00 → 1.00）。効く相手の重みを高くする。
        VsStraight = 0.15f, VsBody = 0.3f, VsHook = 1f,
        CounterStraight = "2", CounterStraightAt = 0.7f,
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
        Defense.SlipIn => SlipIn,
        Defense.Roll => Roll,
        Defense.Parry => Parry,
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
        Defense.SlipIn => this with { SlipIn = move },
        Defense.Roll => this with { Roll = move },
        Defense.Parry => this with { Parry = move },
        _ => throw new ArgumentOutOfRangeException(nameof(defense), defense, null),
    };

    /// <summary>回避技（拳で受けるブロックを除く、頭と足で外す技）。</summary>
    public static IReadOnlyList<Defense> Evasions { get; } = [.. Enum.GetValues<Defense>().Where(d => d is not (Defense.None or Defense.Block or Defense.Parry))];

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
