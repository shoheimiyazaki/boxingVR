namespace MiniRing.Bots;

/// <summary>
/// 武器で戦う操作元に、武器を落としたら拳で戦う操作元をつなぐ。<see cref="FighterSense.SelfArmed"/> が false になったら（握っていた腕が
/// 落ちた・武器を放した）、それからは拳のボット（<see cref="Fists"/>）に代わる（落とした武器は拾わない）。
/// </summary>
public sealed class DisarmedFallback(IFighterControl armed, IFighterControl fists) : IFighterControl
{
    /// <summary>武器で戦う操作元。</summary>
    public IFighterControl Armed { get; } = armed ?? throw new ArgumentNullException(nameof(armed));

    /// <summary>武器を落とした後の、拳で戦う操作元。</summary>
    public IFighterControl Fists { get; } = fists ?? throw new ArgumentNullException(nameof(fists));

    /// <summary>武器を落として、拳で戦っている。</summary>
    public bool Disarmed { get; private set; }

    /// <summary>拳で戦うときに使う、拳のボットの名前。</summary>
    public const string FistStyle = "champion";

    /// <summary>武器のボット <paramref name="armed"/> に、拳の最強ボットを控えさせる。</summary>
    public static DisarmedFallback WithFists(IFighterControl armed, int seed) => new(armed, BotCatalog.Create(FistStyle, seed));

    public FighterCommand Read(in FighterSense sense, float dt)
    {
        Disarmed |= sense.SelfArmed == false;
        return Disarmed ? Fists.Read(sense, dt) : Armed.Read(sense, dt);
    }
}
