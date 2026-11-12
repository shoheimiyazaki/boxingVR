using System.Numerics;
using MiniRing.Weapons;

namespace MiniRing.Bots;

/// <summary>
/// 棍棒で戦うボット（右手に棍棒、左手はグローブで顔を守る）。打って離れる: 拳の届かない所（<see cref="StandOff"/>）で構えて待ち、
/// 打つときだけ間合い（<see cref="Range"/>: 胸と胸の水平の距離）まで踏み込んで、持っている振り（<see cref="WeaponSwing"/>）から 1 つを
/// 選んで振る。振り終えたら <see cref="Rest"/> 秒下がって、また待つ。
/// 振りの選び方だけが乱数で、種が同じなら同じ試合になる。
/// </summary>
public sealed class ClubBot : IFighterControl
{
    private readonly IReadOnlyList<WeaponSwing> _swings;
    private readonly Random _random;
    private readonly SwingFamily _stance;
    private readonly bool _exact;
    private WeaponSwing? _swing;
    private float _time;
    private float _swingStart;
    private float _restUntil;

    /// <param name="swings">持っている振り（1 振りごとに、この中から乱数で選ぶ）。</param>
    /// <param name="seed">振りの選び方の乱数の種。</param>
    /// <param name="range">振り始める間合い [m]。</param>
    /// <param name="rest">振り終えてから次を探すまでの間 [s]。</param>
    /// <param name="standOff">打たない間に構えて待つ距離 [m]。</param>
    /// <param name="exact">振るのを間合いが <see cref="Range"/> ± 3 cm に入ったときだけにする（近すぎれば下がってから振る）。
    /// false なら間合いより近ければすぐ振る。</param>
    public ClubBot(IReadOnlyList<WeaponSwing> swings, int seed, float range = 0.36f, float rest = 0.25f, float standOff = 0.48f, bool exact = false)
    {
        _exact = exact;
        ArgumentNullException.ThrowIfNull(swings);
        if (swings.Count == 0)
        {
            throw new ArgumentException("振りが 1 つも無い。", nameof(swings));
        }

        _swings = swings;
        _stance = swings[0].Family;
        _random = new Random(seed);
        Range = range;
        Rest = rest;
        StandOff = MathF.Max(range, standOff);
    }

    /// <summary>打たない間に構えて待つ距離 [m]（相手の拳の届かない所）。</summary>
    public float StandOff { get; }

    /// <summary>振り始める間合い [m]（胸と胸の水平の距離）。</summary>
    public float Range { get; }

    /// <summary>振り終えてから次を探すまでの間 [s]。</summary>
    public float Rest { get; }

    /// <summary>今振っている振り（構えて詰めている間は null）。</summary>
    public WeaponSwing? Current => _swing;

    /// <summary>研究の出発点の 3 つの振り（縦・横・突き）で戦うボット。</summary>
    public static ClubBot Basic(int seed) => new([.. OneHanded.Select(WeaponSwing.Default)], seed, rest: 0.6f);

    /// <summary>片手で振る型（縦・横・突き）。両手のバットの振りとは握り方が違うので、同じボットには混ぜない。</summary>
    public static IReadOnlyList<SwingFamily> OneHanded { get; } = [SwingFamily.Chop, SwingFamily.Side, SwingFamily.Thrust];

    public FighterCommand Read(in FighterSense sense, float dt)
    {
        _time += dt;
        var toOpponent = sense.OpponentChest - sense.Self.Origin;
        toOpponent.Y = 0f;
        var distance = toOpponent.Length();
        var face = distance > 1e-4f ? toOpponent / distance : Vector3.UnitZ;
        // 武器が折れて短くなったら、失った長さだけ相手が遠いとみなす（短いまま使う: その分だけ間合いを詰めて振る）。
        distance += sense.SelfWeapon.Lost;
        var reach = sense.ArmReach;
        var guard = new HandCommand(new Vector3(0.15f * reach, 0.1f * reach, 0.45f * reach), false);
        if (!sense.OpponentAlive)
        {
            _swing = null;
            return new FighterCommand(Vector3.Zero, face, guard, WeaponSwing.GuardCommand(reach, _stance));
        }

        if (_swing is { } swing)
        {
            var t = _time - _swingStart;
            if (t >= swing.Duration)
            {
                _swing = null;
                _restUntil = _time + Rest;
            }
            else
            {
                var (club, twist, step) = swing.At(t, reach);
                return new FighterCommand(face * step, face, guard, club, TwistBias: twist);
            }
        }

        // 打った後は下がって待ち、待ち終えたら踏み込んで、間合いに入ったら振る。
        var resting = _time < _restUntil;
        var want = resting ? StandOff : Range;
        var move = distance > want + 0.03f ? face : distance < want - 0.03f ? -face : Vector3.Zero;
        if (!resting && distance <= Range + 0.03f && (!_exact || distance >= Range - 0.03f))
        {
            _swing = _swings[_random.Next(_swings.Count)];
            _swingStart = _time;
        }

        return new FighterCommand(move, face, guard, WeaponSwing.GuardCommand(reach, _stance));
    }
}
