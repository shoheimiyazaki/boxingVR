using System.Numerics;
using MiniRing.Weapons;

namespace MiniRing.Bots;

/// <summary>
/// 剣で戦うボット（右手に剣、左手はグローブで顔を守る）。間合いに応じて技を選ぶ:
/// <list type="bullet">
/// <item>次に出す技を 1 つ決め（狙い）、その技の間合いへ歩いて詰める（離れすぎなら踏み込み、近すぎなら下がる）。</item>
/// <item>今の間合いで出せる技（<see cref="SwordTechnique.Fits"/>）があれば出す: 狙った技の間合いに着いたらそれを、相手が狙いより内側へ
/// 詰めてきたら、その近さで出せる技を。</item>
/// <item>技を出し終えたら <see cref="Rest"/> 秒、いちばん遠い技の外（<see cref="StandOff"/>）へ下がる。下がる間も、相手が近い技の間合いまで
/// 詰めてきたら、その技で迎え撃つ。</item>
/// </list>
/// 技の選び方だけが乱数で、種が同じなら同じ試合になる。
/// </summary>
public sealed class SwordBot : IFighterControl
{
    private readonly IReadOnlyList<SwordTechnique> _techniques;
    private readonly Random _random;
    private readonly IReadOnlyList<float> _weights;
    private SwordTechnique? _plan;
    private SwordTechnique? _current;
    private float _time;
    private float _start;
    private float _restUntil;
    private float _plannedAt;

    /// <summary>狙った技の間合いに着けないまま、この秒数が過ぎたら狙いを決め直す（逃げる相手・詰まった間合い）。</summary>
    public const float PlanPatience = 1.5f;

    /// <param name="techniques">持っている技。</param>
    /// <param name="seed">技の選び方の乱数の種。</param>
    /// <param name="rest">技を出し終えてから次を狙うまでの間 [s]。</param>
    /// <param name="weights">技ごとの選ばれやすさ（null なら同じ。0 の技は出さない）。</param>
    public SwordBot(IReadOnlyList<SwordTechnique> techniques, int seed, float rest = 0.5f, IReadOnlyList<float>? weights = null)
    {
        ArgumentNullException.ThrowIfNull(techniques);
        if (techniques.Count == 0)
        {
            throw new ArgumentException("技が 1 つも無い。", nameof(techniques));
        }

        if (weights is not null && (weights.Count != techniques.Count || weights.Any(w => !(w >= 0f)) || weights.Sum() <= 0f))
        {
            throw new ArgumentException("選ばれやすさは技と同じ数の、0 以上の数（合計は正）。", nameof(weights));
        }

        _weights = weights ?? [.. techniques.Select(_ => 1f)];
        _techniques = techniques;
        _random = new Random(seed);
        Rest = rest;
        StandOff = techniques.Max(t => t.Range) + (2f * SwordTechnique.Window);
    }

    /// <summary>技を出した後に下がって待つ距離 [m]（いちばん遠い技の、少し外）。</summary>
    public float StandOff { get; }

    /// <summary>技を出し終えてから次を狙うまでの間 [s]。</summary>
    public float Rest { get; }

    /// <summary>今出している技（出していなければ null）。</summary>
    public SwordTechnique? Current => _current;

    /// <summary>これまでに出した技の名前（順に）。</summary>
    public List<string> Used { get; } = [];

    /// <summary>基本の技（<see cref="SwordTechnique.Basics"/>）で戦うボット。</summary>
    public static SwordBot Basic(int seed) => new(SwordTechnique.Basics, seed);

    public FighterCommand Read(in FighterSense sense, float dt)
    {
        _time += dt;
        var toOpponent = sense.OpponentChest - sense.Self.Origin;
        toOpponent.Y = 0f;
        var distance = toOpponent.Length();
        var face = distance > 1e-4f ? toOpponent / distance : Vector3.UnitZ;
        var reach = sense.ArmReach;
        var guard = new HandCommand(new Vector3(0.15f * reach, 0.1f * reach, 0.45f * reach), false);
        var stance = WeaponSwing.GuardCommand(reach, SwingFamily.Sword);
        if (!sense.OpponentAlive)
        {
            _current = null;
            return new FighterCommand(Vector3.Zero, face, guard, stance);
        }

        if (_current is { } technique)
        {
            var t = _time - _start;
            if (t < technique.Swing.Duration)
            {
                var (sword, twist, step) = technique.Swing.At(t, reach);
                return new FighterCommand(face * step, face, guard, sword, TwistBias: twist);
            }

            _current = null;
            _plan = null;
            _restUntil = _time + Rest;
        }

        var resting = _time < _restUntil;
        var closest = _techniques.Min(t => t.Range);
        // 相手が詰めてきた: 狙いより内側（下がっている間は、いちばん近い技の間合いまで）に入ったら、その近さで出せる技で迎え撃つ。
        var pressed = resting ? distance <= closest + SwordTechnique.Window : _plan is { } p && distance < p.Range - SwordTechnique.Window;
        if (!resting || pressed)
        {
            if (_plan is null || _time - _plannedAt > PlanPatience)
            {
                _plan = Pick(_techniques);
                _plannedAt = _time;
            }

            var fits = _techniques.Where((t, i) => t.Fits(distance) && _weights[i] > 0f).ToList();
            if (fits.Count > 0 && (pressed || fits.Contains(_plan)))
            {
                var chosen = fits.Contains(_plan) ? _plan : Pick(fits);
                _current = chosen;
                _start = _time;
                Used.Add(chosen.Name);
                var (sword, twist, step) = chosen.Swing.At(0f, reach);
                return new FighterCommand(face * step, face, guard, sword, TwistBias: twist);
            }
        }

        var want = resting ? StandOff : _plan!.Range;
        var move = distance > want + 0.02f ? face : distance < want - 0.02f ? -face : Vector3.Zero;
        return new FighterCommand(move, face, guard, stance);
    }

    /// <summary><paramref name="from"/> から、選ばれやすさに比べて 1 つ選ぶ（選ばれやすさがみな 0 なら同じ割合で）。</summary>
    private SwordTechnique Pick(IReadOnlyList<SwordTechnique> from)
    {
        var weights = from.Select(t => _weights[IndexOf(t)]).ToList();
        var total = weights.Sum();
        if (total <= 0f)
        {
            return from[_random.Next(from.Count)];
        }

        var x = (float)_random.NextDouble() * total;
        for (var i = 0; i < from.Count; i++)
        {
            x -= weights[i];
            if (x <= 0f)
            {
                return from[i];
            }
        }

        return from[^1];
    }

    private int IndexOf(SwordTechnique technique)
    {
        for (var i = 0; i < _techniques.Count; i++)
        {
            if (ReferenceEquals(_techniques[i], technique))
            {
                return i;
            }
        }

        return -1;
    }
}
