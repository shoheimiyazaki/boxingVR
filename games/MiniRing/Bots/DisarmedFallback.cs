namespace MiniRing.Bots;

/// <summary>
/// 武器で戦う操作元に、武器を失ったら拳で戦う操作元をつなぐ。<see cref="FighterSense.SelfArmed"/> が false になったら（握っていた腕が
/// 落ちた・武器を放した）、それからは拳のボット（<see cref="Fists"/>）に代わる（落とした武器は拾わない）。
/// <para>
/// 折れた武器の判断（2026-11-13）: 武器が折れて短くなったら（<see cref="WeaponSense.Lost"/>）、残った長さ（握る所から先の端まで）を見る。
/// <see cref="KeepReach"/>（腕の長さの割合）以上残っていれば、短いまま使う（武器のボットは失った長さだけ間合いを詰める）。それより短ければ
/// 役に立たないので、両手を放して（<see cref="HandCommand.LetGo"/>）捨て、素手で戦う。放した手は素手の拳になる。
/// </para>
/// </summary>
/// <param name="armed">武器で戦う操作元。</param>
/// <param name="fists">武器を失った後の、拳で戦う操作元。</param>
/// <param name="keepReach">
/// 折れた武器を使い続ける、残った長さの下限 [腕の長さ]。既定 0.35: 拳を握ったまま突き出すより、腕の 1/3 ほども先へ届かない柄だけの武器は、
/// 間合いを広げず、空いた拳（グローブの無い素手でも、拳 2 つで打てる・守れる）より役に立たない、という判断の値（物理ではなくボットの判断）。
/// </param>
public sealed class DisarmedFallback(IFighterControl armed, IFighterControl fists, float keepReach = DisarmedFallback.DefaultKeepReach) : IFighterControl
{
    /// <summary><see cref="KeepReach"/> の既定値 [腕の長さ]。</summary>
    public const float DefaultKeepReach = 0.35f;

    /// <summary>武器で戦う操作元。</summary>
    public IFighterControl Armed { get; } = armed ?? throw new ArgumentNullException(nameof(armed));

    /// <summary>武器を失った後の、拳で戦う操作元。</summary>
    public IFighterControl Fists { get; } = fists ?? throw new ArgumentNullException(nameof(fists));

    /// <summary>折れた武器を使い続ける、残った長さの下限 [腕の長さ]。</summary>
    public float KeepReach { get; } = keepReach is >= 0f and <= 3f ? keepReach : throw new ArgumentOutOfRangeException(nameof(keepReach));

    /// <summary>武器を失って（または捨てて）、拳で戦っている。</summary>
    public bool Disarmed { get; private set; }

    /// <summary>折れて役に立たない武器を、自分で捨てた。</summary>
    public bool Discarded { get; private set; }

    /// <summary>折れて短くなった武器を、そのまま使っている。</summary>
    public bool FightsWithBrokenWeapon { get; private set; }

    /// <summary>拳で戦うときに使う、拳のボットの名前。</summary>
    public const string FistStyle = "champion";

    /// <summary>武器のボット <paramref name="armed"/> に、拳の最強ボットを控えさせる。</summary>
    public static DisarmedFallback WithFists(IFighterControl armed, int seed) => new(armed, BotCatalog.Create(FistStyle, seed));

    public FighterCommand Read(in FighterSense sense, float dt)
    {
        Disarmed |= sense.SelfArmed == false;
        if (Disarmed)
        {
            return Fists.Read(sense, dt);
        }

        var weapon = sense.SelfWeapon;
        var command = Armed.Read(sense, dt);
        if (!weapon.Held || weapon.Lost <= 0f)
        {
            return command;
        }

        if (weapon.Reach >= KeepReach * sense.ArmReach)
        {
            FightsWithBrokenWeapon = true;
            return command;
        }

        // 役に立たない: 捨てる（両手とも放す。次の歩から握っていないので拳で戦う）。
        Discarded = true;
        FightsWithBrokenWeapon = false;
        return command with { Left = command.Left with { LetGo = true }, Right = command.Right with { LetGo = true } };
    }
}
