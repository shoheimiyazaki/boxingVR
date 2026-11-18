namespace MiniRing.Arena;

/// <summary>体の図の部位（本人から見た左右。図は正面を向いて立つので、画面の左が本人の右）。</summary>
internal enum FigurePart
{
    Head,
    Chest,
    Waist,
    RightUpperArm,
    RightForearm,
    LeftUpperArm,
    LeftForearm,
    RightThigh,
    RightShin,
    LeftThigh,
    LeftShin,
}

/// <summary>体の図の 1 部位の見えている状態。</summary>
/// <param name="Lost">削れて失われた肉の割合（0..1。見えている欠損の量）。</param>
/// <param name="Broken">骨が折れている（肉だけでつながっている）。</param>
/// <param name="Gone">切り落とされた（この部位とそこから先が無い）。</param>
internal readonly record struct FigurePartState(float Lost, bool Broken, bool Gone);

/// <summary>
/// ある選手の体の、見えている状態（図に描く物）。削れた量・折れた骨・失った部位・潰れた目。意識・臓器・スタミナのような
/// 見えない量は持たない（CLAUDE.md §1.7: 見えている体から）。
/// </summary>
internal sealed class BodyFigure
{
    private readonly Dictionary<FigurePart, FigurePartState> _parts;

    public BodyFigure(IReadOnlyDictionary<FigurePart, FigurePartState> parts, float leftSight, float rightSight, bool dead)
    {
        _parts = new Dictionary<FigurePart, FigurePartState>(parts);
        LeftSight = leftSight;
        RightSight = rightSight;
        Dead = dead;
    }

    /// <summary>左目の見える度合い（1 = 無傷、0 = 潰れた）。</summary>
    public float LeftSight { get; }

    /// <summary>右目の見える度合い。</summary>
    public float RightSight { get; }

    /// <summary>死んでいる。</summary>
    public bool Dead { get; }

    public FigurePartState this[FigurePart part] => _parts.GetValueOrDefault(part);

    /// <summary>ある部位の状態だけ替えた新しい図（元の図は変えない）。</summary>
    public BodyFigure With(FigurePart part, FigurePartState state)
    {
        var parts = new Dictionary<FigurePart, FigurePartState>(_parts) { [part] = state };
        return new BodyFigure(parts, LeftSight, RightSight, Dead);
    }

    /// <summary>死んでいる（<see cref="Dead"/>）ことだけ替えた新しい図。</summary>
    public BodyFigure WithDead(bool dead) => new(_parts, LeftSight, RightSight, dead);

    /// <summary>傷がまったく見えない（全部位が無傷・両目が見える）。</summary>
    public bool Unharmed => _parts.Values.All(p => p is { Gone: false, Broken: false, Lost: < 0.05f }) && LeftSight >= 0.9f && RightSight >= 0.9f;

    /// <summary>図の部位の呼び名（本人の左右つき）。</summary>
    public static string Name(FigurePart part) => part switch
    {
        FigurePart.Head => "頭",
        FigurePart.Chest => "胸",
        FigurePart.Waist => "腰",
        FigurePart.RightUpperArm => "右上腕",
        FigurePart.RightForearm => "右前腕",
        FigurePart.LeftUpperArm => "左上腕",
        FigurePart.LeftForearm => "左前腕",
        FigurePart.RightThigh => "右太もも",
        FigurePart.RightShin => "右すね",
        FigurePart.LeftThigh => "左太もも",
        _ => "左すね",
    };

    /// <summary>
    /// 見えている傷を短い言葉に（「左前腕 骨折」「右上腕から先を失った」「右目が潰れた」「胸 肉が3割削れた」）。重い傷から順に。
    /// 切り落とされた部位から先の部位は、先の部位の傷として重ねて言わない。
    /// </summary>
    public IReadOnlyList<string> Wounds()
    {
        var gone = new List<string>();
        var broken = new List<string>();
        var carved = new List<(float Lost, string Text)>();
        foreach (var part in Enum.GetValues<FigurePart>())
        {
            var s = this[part];
            if (s.Gone)
            {
                if (!IsBeyondGone(part))
                {
                    gone.Add(part == FigurePart.Head ? "首から先を失った" : $"{Name(part)}から先を失った");
                }
            }
            else if (s.Broken)
            {
                broken.Add($"{Name(part)} 骨折");
            }
            else if (s.Lost >= 0.25f)
            {
                carved.Add((s.Lost, $"{Name(part)} 肉が{Tenths(s.Lost)}削れた"));
            }
        }

        var eyes = new List<string>();
        foreach (var (side, sight) in new[] { ("左", LeftSight), ("右", RightSight) })
        {
            if (sight <= 0.01f)
            {
                eyes.Add($"{side}目が潰れた");
            }
            else if (sight < 0.5f)
            {
                eyes.Add($"{side}目がほとんど見えない");
            }
        }

        return [.. gone, .. broken, .. eyes, .. carved.OrderByDescending(c => c.Lost).Select(c => c.Text)];
    }

    /// <summary>その部位が、切り落とされた別の部位から先にある（腕・脚の根元側が切れていれば先の部位は言わない）。</summary>
    private bool IsBeyondGone(FigurePart part) => part switch
    {
        FigurePart.RightForearm => this[FigurePart.RightUpperArm].Gone,
        FigurePart.LeftForearm => this[FigurePart.LeftUpperArm].Gone,
        FigurePart.RightShin => this[FigurePart.RightThigh].Gone,
        FigurePart.LeftShin => this[FigurePart.LeftThigh].Gone,
        _ => false,
    };

    private static string Tenths(float fraction) => fraction >= 0.95f ? "ほぼすべて" : fraction is >= 0.45f and < 0.55f ? "半分"
        : (int)MathF.Round(fraction * 10f) is var n && n <= 0 ? "1割近く" : $"{n}割";
}

/// <summary>
/// 試合の体の図を作る道具。部位の折れ・切断は中継の記録（<see cref="MiniRing.Audio.Broadcast"/>）が出来事から覚えているので、それを読む。
/// 削れた量は体のボクセルの残り（<see cref="MiniRing.Voxels.BoxerBody.Muscle"/>）、目は <see cref="MiniRing.Voxels.BoxerBody.Vision"/>。
/// </summary>
/// <param name="limbOf">その選手のその部位（剛体 id）の状態（"骨折" / "切断" / null）。</param>
internal sealed class ShowBodies(Func<Fighter, int, string?> limbOf)
{
    /// <summary>何も覚えていない（中継の記録が無い）読み手。</summary>
    public static ShowBodies None { get; } = new((_, _) => null);

    /// <summary>いまの体の図。</summary>
    public BodyFigure Of(Fighter fighter)
    {
        ArgumentNullException.ThrowIfNull(fighter);
        var p = fighter.Parts;
        // 剛体の名前の Left/Right は本人の右/左（Broadcast.PartName の注）。図は本人の右・左で持つ。
        (FigurePart Part, int Body)[] map =
        [
            (FigurePart.Head, p.Head), (FigurePart.Chest, p.Chest), (FigurePart.Waist, p.Pelvis),
            (FigurePart.RightUpperArm, p.LeftUpperArm), (FigurePart.RightForearm, p.LeftForearm),
            (FigurePart.LeftUpperArm, p.RightUpperArm), (FigurePart.LeftForearm, p.RightForearm),
            (FigurePart.RightThigh, p.LeftThigh), (FigurePart.RightShin, p.LeftShin),
            (FigurePart.LeftThigh, p.RightThigh), (FigurePart.LeftShin, p.RightShin),
        ];
        var states = new Dictionary<FigurePart, FigurePartState>();
        foreach (var (part, body) in map)
        {
            var limb = limbOf(fighter, body);
            var lost = fighter.Voxels is { } voxels ? Math.Clamp(1f - voxels.Muscle(body), 0f, 1f) : 0f;
            states[part] = new FigurePartState(lost, limb == "骨折", limb == "切断");
        }

        // 切り落とされた部位から先は無い。
        foreach (var (root, beyond) in new[] { (FigurePart.RightUpperArm, FigurePart.RightForearm), (FigurePart.LeftUpperArm, FigurePart.LeftForearm), (FigurePart.RightThigh, FigurePart.RightShin), (FigurePart.LeftThigh, FigurePart.LeftShin) })
        {
            if (states[root].Gone)
            {
                states[beyond] = states[beyond] with { Gone = true };
            }
        }

        if (fighter.DeathCause == "neck")
        {
            states[FigurePart.Head] = states[FigurePart.Head] with { Gone = true };
        }

        var vision = fighter.Voxels?.Vision ?? VisionState.Healthy;
        return new BodyFigure(states, vision.Left, vision.Right, fighter.IsDead);
    }
}
