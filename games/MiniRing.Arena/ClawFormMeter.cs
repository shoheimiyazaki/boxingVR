using System.Numerics;
using MiniRing.Weapons;

namespace MiniRing.Arena;

/// <summary>
/// 鎌の振りの形（2026-11-25、ガルヴァの全身で振る練習。<see cref="HuntLab"/> の <c>--goal form</c>）。どれも連続な物理の量で、振りを区間に切らない
/// （閾値を持たない）。速さ・刃筋は速さの 2 乗で重みを付けた平均にして、構えて止まっている歩・戻りの遅い歩が効かないようにする。
/// </summary>
/// <param name="Strokes">振った数（ボットの <c>claw-sweep</c>）。</param>
/// <param name="WorkJoules">攻め手の筋肉がした仕事の合計 [J]（全部位。スタミナの消費の元）。</param>
/// <param name="Imbalance">部位ごとの仕事 / 部位の筋の蓄えの大きさ、の最大 / 平均 − 1（全身で均等なら 0）。</param>
/// <param name="JerkRms">鎌の先端の加加速度の RMS [m/s³]（左右の鎌の、毎歩の値）。</param>
/// <param name="Speed">先端の速さ [m/s]（Σv³ / Σv²: 速く動いている間の速さ）。</param>
/// <param name="EdgeDegrees">刃筋 [°]: 先端の速度が刃の面から外れた角（速さの 2 乗の重み付きの平均）。</param>
/// <param name="Removed">受け手の体から失われたボクセルの数。</param>
/// <param name="Regions">部位ごとの仕事の割合（左腕・右腕・左脚・胴・右脚の順、合計 1）。</param>
internal sealed record ClawForm(int Strokes, double WorkJoules, double Imbalance, double JerkRms, double Speed, double EdgeDegrees, int Removed, double[] Regions)
{
    /// <summary>人の満タンのスタミナに当たる仕事 [J]（<see cref="FighterTuning.StaminaEndurance"/>）。エイリアンの心肺は仮に極端な値（docs/DEBT.md）なので、スタミナの割合でなく、した仕事を人の蓄えで数える。</summary>
    public static readonly double HumanTankJoules = new FighterTuning().StaminaEndurance;

    /// <summary>1 振りあたりの仕事（人の満タン = 1）。</summary>
    public double TanksPerStroke => WorkJoules / HumanTankJoules / Math.Max(1, Strokes);

    /// <summary>形の代価（<see cref="FormCost.Of"/>）。</summary>
    public double Cost => FormCost.Of(0d, 0d, EdgeDegrees, TanksPerStroke, Imbalance, JerkRms, Speed);

    /// <summary>1 振りあたりの点: 削ったセル / 60（剣のドリルの 1 点 = 60 セル、clawlab と同じ）/ 振った数 − 形の代価。</summary>
    public double Points => (Removed / 60d / Math.Max(1, Strokes)) - Cost;
}

/// <summary>1 回の狩りの間、攻め手の鎌の先端と体の仕事を毎歩数える（<see cref="ClawForm"/>）。</summary>
internal sealed class ClawFormMeter
{
    private readonly Fighter _hunter;
    private readonly float[] _work0 = new float[MuscleRegions.Count];
    private readonly Vector3?[][] _velocity = [new Vector3?[2], new Vector3?[2]];
    private double _jerk2;
    private int _jerkSamples;
    private double _v2;
    private double _v3;
    private double _edge;

    public ClawFormMeter(Fighter hunter)
    {
        _hunter = hunter;
        for (var r = 0; r < MuscleRegions.Count; r++)
        {
            _work0[r] = hunter.RegionWork((MuscleRegion)r);
        }
    }

    /// <summary>1 歩の後に呼ぶ（<paramref name="dt"/> はその歩の長さ）。</summary>
    public void Sample(BoxingGame game, float dt)
    {
        foreach (var hand in new[] { Hand.Left, Hand.Right })
        {
            var history = _velocity[(int)hand];
            if (game.GloveOf(_hunter, hand) is not { } claw)
            {
                history[0] = history[1] = null;
                continue;
            }

            var state = game.World.GetState(claw.Body.BodyId);
            var tip = state.Position + Vector3.Transform(Weapon.GripOf(claw.Kind) + SickleClawShape.Edge(hand), state.Orientation);
            var velocity = state.LinearVelocity + Vector3.Cross(state.AngularVelocity, tip - state.Position);
            if (history[0] is { } v1 && history[1] is { } v2)
            {
                var jerk = (velocity - (2f * v1) + v2) / (dt * dt);
                _jerk2 += jerk.LengthSquared();
                _jerkSamples++;
            }

            history[1] = history[0];
            history[0] = velocity;
            var speed2 = (double)velocity.LengthSquared();
            if (speed2 < 1e-8)
            {
                continue;
            }

            var speed = Math.Sqrt(speed2);
            var normal = Vector3.Normalize(Vector3.Transform(Vector3.UnitY, state.Orientation));
            var off = Math.Asin(Math.Clamp(Math.Abs(Vector3.Dot(velocity, normal)) / speed, 0d, 1d)) * 180d / Math.PI;
            _v2 += speed2;
            _v3 += speed2 * speed;
            _edge += speed2 * off;
        }
    }

    public ClawForm Finish(int strokes, int removed)
    {
        var work = new double[MuscleRegions.Count];
        var load = new double[MuscleRegions.Count];
        for (var r = 0; r < MuscleRegions.Count; r++)
        {
            work[r] = Math.Max(0d, _hunter.RegionWork((MuscleRegion)r) - _work0[r]);
            var capacity = _hunter.OxygenBudgetOf((MuscleRegion)r).Capacity;
            load[r] = capacity > 1e-6f ? work[r] / capacity : 0d;
        }

        var total = work.Sum();
        var mean = load.Average();
        var imbalance = mean > 0d ? (load.Max() / mean) - 1d : 0d;
        return new ClawForm(strokes, total, imbalance, _jerkSamples > 0 ? Math.Sqrt(_jerk2 / _jerkSamples) : 0d,
            _v2 > 0d ? _v3 / _v2 : 0d, _v2 > 0d ? _edge / _v2 : 0d, removed,
            [.. work.Select(w => total > 0d ? w / total : 0d)]);
    }
}
