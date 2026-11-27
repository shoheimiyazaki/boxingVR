using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Bots;

/// <summary>
/// 刀・ロングソード・棒を、その武器の型（<see cref="SwordForms"/>・<see cref="StaffForms"/> の手で作った型）で動かす組み立て（試合のボットも、デスクトップのプレイヤーの意図操作も
/// ここから技を取る。<c>CLAUDE.md</c> §1.5: 一つの組み方）。外部の動き（モーションキャプチャ）には頼らない。
/// 構え・間合い（物打ちが届く距離 <see cref="StrikeDrill.ReachOf"/>）・刃の長さ・握りの間隔・剣士のボット（<see cref="SwordsmanBot"/>。
/// 武器を落としたら拳 <see cref="DisarmedFallback.WithFists"/>）は Arena の <c>SwordLibraryLab.Bot</c> と同じ組み方。
/// </summary>
public static class FormSwordsman
{
    /// <summary>型で動かす武器か（刀・ロングソード・棒・バット。どれも両手持ち）。</summary>
    public static bool Handles(WeaponKind? weapon) => weapon is WeaponKind.Katana or WeaponKind.Longsword or WeaponKind.Staff or WeaponKind.Jo or WeaponKind.Bat;

    /// <summary>型で動かす武器か（両手持ちの <see cref="Handles"/> に、片手の剣 <see cref="WeaponKind.IronSword"/> を足したもの。片手剣の左手は盾（空いた手にはめた物）の経路を描く: <see cref="SwordShieldForms"/>）。</summary>
    public static bool HasForms(WeaponKind? weapon) => Handles(weapon) || weapon == WeaponKind.IronSword;

    private static readonly Lazy<IReadOnlyList<SwordMotion>> KatanaForms = new(SwordForms.Katana);
    private static readonly Lazy<IReadOnlyList<SwordMotion>> LongswordForms = new(SwordForms.Longsword);

    /// <summary>
    /// 打ちの型の一覧。刀は <see cref="SwordForms.Katana"/> の 7 つ（突き・袈裟・逆袈裟・抜き胴・斬り上げ・払い面・引き面）、
    /// ロングソードは <see cref="SwordForms.Longsword"/> の最初の「構えの型」を除いた 5 つの打ち（Oberhau・Zornhau・Zwerchhau・Unterhau・Stich）。
    /// </summary>
    public static IReadOnlyList<SwordMotion> Forms(WeaponKind weapon) => weapon switch
    {
        WeaponKind.Katana => KatanaForms.Value,
        WeaponKind.Longsword => [.. LongswordForms.Value.Skip(1)],
        WeaponKind.Staff or WeaponKind.Jo => StaffForms.Forms(), // 杖も今は六尺棒の型（杖道の型は未作成。VR の記録から作る: docs/VR_MOTION.md）
        WeaponKind.IronSword => SwordShieldForms.Forms(),
        WeaponKind.Bat => BatForms.Forms(),
        _ => throw new ArgumentOutOfRangeException(nameof(weapon), weapon, "刀・ロングソード・棒・片手剣・バットだけ。"),
    };

    /// <summary>
    /// 技の重み（<see cref="WeaponStyle.TechniqueWeights"/>）の鍵が、その武器の型の名前にあるか確かめる（綴りの誤りを黙って無視しない）。
    /// <paramref name="weights"/> が null か、武器が型で動かない（こんぼう）なら何もしない。
    /// </summary>
    public static void CheckTechniqueNames(WeaponKind? weapon, IReadOnlyDictionary<string, float>? weights)
    {
        if (weights is null || !HasForms(weapon))
        {
            return;
        }

        var names = Forms(weapon!.Value).Select(f => f.Name).ToList();
        foreach (var key in weights.Keys.Where(k => !names.Contains(k)))
        {
            throw new ArgumentOutOfRangeException(nameof(weights), key, $"技 '{key}' は {weapon} の型にありません（{string.Join(", ", names)}）。");
        }
    }

    /// <summary>構え（技を出していない間のフレーム）。刀は刀の型の始まり（中段）、ロングソードは構えの型の始まり（Vom Tag）。</summary>
    public static MotionFrame Guard(WeaponKind weapon) => weapon switch
    {
        WeaponKind.Katana => KatanaForms.Value[0].Frames[0],
        WeaponKind.Longsword => LongswordForms.Value[0].Frames[0],
        WeaponKind.Staff or WeaponKind.Jo => StaffForms.Guard(),
        WeaponKind.IronSword => SwordShieldForms.Guard(),
        WeaponKind.Bat => BatForms.Guard(),
        _ => throw new ArgumentOutOfRangeException(nameof(weapon), weapon, "刀・ロングソード・棒・片手剣・バットだけ。"),
    };

    /// <summary>両手の間隔 [腕の長さ]。</summary>
    public static float Spacing(WeaponKind weapon) => weapon switch
    {
        WeaponKind.Longsword => SwordForms.LongswordSpacing,
        WeaponKind.Staff or WeaponKind.Jo => StaffForms.Spacing,
        WeaponKind.Bat => BatForms.Spacing,
        WeaponKind.IronSword => 0f, // 片手: 左手は剣の握りでなく盾の位置（型のキーの Left）
        _ => SwordForms.KatanaSpacing,
    };

    /// <summary>剣の握り（右手）から先までの長さ [腕の長さ]（受けるとき、剣のどこで受けるか）。</summary>
    public static float BladeLength(WeaponKind weapon, float armReach) => (Weapon.TipOf(weapon).X - Weapon.GripOf(weapon).X) / armReach;

    /// <summary>右手から後ろの端までの長さ [腕の長さ]（両端を使う武器の受けで、体から離す線を後ろの端まで見る）。端が 1 つの武器は null。</summary>
    public static float? RearLength(WeaponKind weapon, float armReach) =>
        Weapon.IsRod(weapon) && Weapon.StrikeEndsX(weapon) is { Count: > 1 } ends ? (Weapon.GripOf(weapon).X - ends.Min()) / armReach : null;

    /// <summary>これより間合いが短い型は残さない [m]（打ち込まずその場で振る型は、剣士同士の間合いの技ではない。Arena の <c>SwordLibraryLab.MinReach</c> と同じ）。</summary>
    public const float MinReach = 0.930f;

    /// <summary>
    /// 型から作った技: 間合いは、その型の動きの中で物打ちが届く距離（<see cref="StrikeDrill.ReachOf"/>）。<see cref="MinReach"/> より短い型は除く。
    /// </summary>
    public static IReadOnlyList<MotionTechnique> Techniques(WeaponKind weapon, float armReach) => Techniques(weapon, armReach, MinReach);

    /// <summary><see cref="Techniques(WeaponKind, float)"/> の、間合いの下限 <paramref name="minReach"/> [m] を選べる版（武器の教科書の <see cref="WeaponStyle.MinReach"/>）。</summary>
    public static IReadOnlyList<MotionTechnique> Techniques(WeaponKind weapon, float armReach, float minReach) => Techniques(weapon, armReach, minReach, null);

    /// <summary>
    /// <see cref="Techniques(WeaponKind, float, float)"/> に、型ごとの経路の差分 <paramref name="tunes"/>（<see cref="WeaponStyle.Forms"/>）を掛けた版。
    /// 間合いは差分を掛けた後の型の動きから出し（<see cref="StrikeDrill.ReachOf"/>）、<see cref="FormTune.Reach"/> を足す。null か空なら教科書のまま。
    /// </summary>
    public static IReadOnlyList<MotionTechnique> Techniques(WeaponKind weapon, float armReach, float minReach, IReadOnlyDictionary<string, FormTune>? tunes) =>
    [
        .. Forms(weapon).Select(f => (Form: f, Tune: tunes?.GetValueOrDefault(f.Name) ?? FormTune.Identity))
            .Select(x => new MotionTechnique(x.Form.Name, weapon, x.Tune.Apply(x.Form), 1.408f, Skill: SwordFormSkills.For(weapon, x.Form.Name)))
            .Select(t => t with { Range = StrikeDrill.ReachOf(t, armReach) + (tunes?.GetValueOrDefault(t.Name)?.Reach ?? 0f), HeadFollow = tunes?.GetValueOrDefault(t.Name)?.Follow ?? 0f })
            .Where(t => t.Range >= minReach),
    ];

    /// <summary>型の経路の差分の鍵が、その武器の型の名前にあるか確かめる（綴りの誤りを黙って無視しない）。</summary>
    public static void CheckFormNames(WeaponKind? weapon, IReadOnlyDictionary<string, FormTune>? tunes)
    {
        if (tunes is null || tunes.Count == 0)
        {
            return;
        }

        if (!HasForms(weapon))
        {
            throw new ArgumentOutOfRangeException(nameof(tunes), weapon, "型の経路の差分は、型で動かす武器だけが持てます。");
        }

        var names = Forms(weapon!.Value).Select(f => f.Name).ToList();
        foreach (var key in tunes.Keys.Where(k => !names.Contains(k)))
        {
            throw new ArgumentOutOfRangeException(nameof(tunes), key, $"型 '{key}' は {weapon} の型にありません（{string.Join(", ", names)}）。");
        }
    }

    /// <summary>
    /// 武器の型のうち <paramref name="vrForms"/>（型の名前 → 組み込みの VR の振りの名前）に書いた型の目標の経路を、VR の稽古の振りにした技の一覧
    /// （<see cref="VrMotionForms.ToSwordMotion"/>）。間合いは型と同じく、その動きで物打ちが届く距離で決め、<paramref name="minReach"/> より短い技は除く。
    /// </summary>
    public static IReadOnlyList<MotionTechnique> TechniquesWithVrForms(WeaponKind weapon, float armReach, float minReach, IReadOnlyDictionary<string, string> vrForms, FighterTuning tuning)
    {
        ArgumentNullException.ThrowIfNull(vrForms);
        ArgumentNullException.ThrowIfNull(tuning);
        var guard = Guard(weapon);
        return
        [
            .. Forms(weapon)
                .Select(f => vrForms.TryGetValue(f.Name, out var id)
                    ? VrMotionForms.ToSwordMotion(VrMotionForms.Load(id), f.Name, guard, tuning.WalkSpeed, armReach, SwordStance.Rate, tuning.MaxTorsoTwistRadians)
                    : f)
                .Select(m => new MotionTechnique(m.Name, weapon, m, 1.408f, Skill: SwordFormSkills.For(weapon, m.Name)))
                .Select(t => t with { Range = StrikeDrill.ReachOf(t, armReach) })
                .Where(t => t.Range >= minReach),
        ];
    }

    /// <summary>
    /// プレイヤーの弱・強の型。刀: 弱 = 突き（tsuki）・強 = 袈裟（kesa）。ロングソード: 弱 = Stich・強 = Oberhau。棒: 弱 = 突き（tsuki）・強 = 打ち下ろし（uchi-oroshi）。
    /// </summary>
    public static (SwordMotion Light, SwordMotion Heavy) PlayerForms(WeaponKind weapon)
    {
        var forms = Forms(weapon);
        var (light, heavy) = weapon switch
        {
            WeaponKind.Katana => ("tsuki", "kesa"),
            WeaponKind.Staff or WeaponKind.Jo => ("tsuki", "uchi-oroshi"),
            WeaponKind.IronSword => ("tsuki", "kiri-oroshi"),
            WeaponKind.Bat => ("poke-body", "chop"),
            _ => ("stich", "oberhau"),
        };
        return (forms.First(f => f.Name == light), forms.First(f => f.Name == heavy));
    }

    /// <summary>型 <paramref name="motion"/> の時刻 <paramref name="time"/>（動きの中の秒）の指令（ボットと同じ <see cref="MotionImitation.ToCommand"/>）。</summary>
    public static FighterCommand Command(SwordMotion motion, float time, in FighterSense sense, System.Numerics.Vector3 face, float walkSpeed, float maxTwist,
        System.Numerics.Vector3 extraMove = default)
        => MotionImitation.Command(motion, time, sense, face, SwordStance.Rate, walkSpeed, maxTwist, extraMove);

    /// <summary>構えたままの指令（歩かない）。</summary>
    public static FighterCommand GuardCommand(WeaponKind weapon, in FighterSense sense, System.Numerics.Vector3 face, float walkSpeed, float maxTwist)
        => MotionImitation.ToCommand(Guard(weapon), sense, face, 0f, walkSpeed, maxTwist);

    /// <summary>
    /// 剣士のボット（試合用）。技の一覧 <paramref name="techniques"/> と構え <paramref name="guard"/> で戦い、武器を落としたら拳で戦う。
    /// </summary>
    public static DisarmedFallback Bot(WeaponKind weapon, IReadOnlyList<MotionTechnique> techniques, MotionFrame guard, int seed, float armReach, FighterTuning tuning,
        SwordDefense defense, HitAndAway? hitAndAway = null, AimTuning? aim = null, bool autoAim = false, SelfClearance? clearance = null, SwingPacing? pacing = null, float offhandRadius = 0f) =>
        DisarmedFallback.WithFists(new SwordsmanBot([.. techniques], guard, seed, SwordStance.Rate, tuning: tuning, defense: defense,
            bladeLength: BladeLength(weapon, armReach), spacing: Spacing(weapon), opponentReach: 3f, hitAndAway: hitAndAway, aim: aim)
        {
            AutoAim = autoAim,
            Clearance = clearance,
            Pacing = pacing,
            OffhandRadius = offhandRadius,
        }, seed);

    /// <summary>試合の既定の剣士: 型の技、型の構え、採った守り（<see cref="SwordDefense.Adopted"/>）。</summary>
    public static DisarmedFallback Bot(WeaponKind weapon, int seed, float armReach, FighterTuning tuning) =>
        Bot(weapon, seed, armReach, tuning, WeaponCatalog.Textbook(weapon));

    /// <summary>
    /// 武器の教科書（とキャラの教育を重ねた <see cref="WeaponStyle"/>）で動く試合の剣士。値は全部 <paramref name="style"/> から（コードに数字を埋めない）。
    /// 教科書が今までの既定と同じ値のとき、試合はビット単位で同じ。
    /// <paramref name="offhandRadius"/> は空いた手の物（盾・グローブ）が面を広げる半径 [腕の長さ]（<see cref="SwordsmanBot.OffhandRadius"/>）。
    /// <paramref name="techniques"/> は技の一覧の差し替え（VR の記録から作った型を試すとき: <see cref="VrMotionForms"/>、Arena の <c>vrforms</c>）。null なら武器の型。
    /// </summary>
    public static DisarmedFallback Bot(WeaponKind weapon, int seed, float armReach, FighterTuning tuning, WeaponStyle style, float offhandRadius = 0f,
        IReadOnlyList<MotionTechnique>? techniques = null)
    {
        ArgumentNullException.ThrowIfNull(style);
        CheckTechniqueNames(weapon, style.TechniqueWeights);
        CheckTechniqueNames(weapon, style.VrForms?.ToDictionary(p => p.Key, _ => 0f));
        CheckFormNames(weapon, style.Forms);
        var minReach = style.InnerMinReach ?? style.MinReach;
        // VR の振り（VrForms）に差し替えた型は VR の経路のまま、ほかの型は教科書に型ごとの差分（Forms）を当てる。
        techniques ??= style.VrForms is { Count: > 0 } vrForms ? TechniquesWithVrForms(weapon, armReach, minReach, vrForms, tuning) : Techniques(weapon, armReach, minReach, style.Forms);
        var bot = new SwordsmanBot([.. techniques], Guard(weapon), seed, SwordStance.Rate, rest: style.Rest, tuning: tuning,
            defense: style.Defense, bladeLength: BladeLength(weapon, armReach), spacing: Spacing(weapon), opponentReach: 3f, hitAndAway: style.HitAndAway, aim: style.Aim)
        {
            AutoAim = style.AutoAim,
            Clearance = style.Clearance,
            Pacing = style.Pacing,
            Weights = style.TechniqueWeights,
            PlanMinReach = style.InnerMinReach is null ? 0f : style.MinReach,
            RearLength = RearLength(weapon, armReach),
            OffhandRadius = offhandRadius,
            BladeCut = style.BladeCut,
        };
        return new DisarmedFallback(bot, BotCatalog.Create(DisarmedFallback.FistStyle, seed), style.KeepReach);
    }
}
