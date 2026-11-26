using System.Globalization;
using System.Numerics;
using MiniRing.Arena;
using MiniRing.Bots;
using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 調査用（docs/plans/FORM_DECAY_STUDY.md）: 「フォームが崩れる」を数で測る。
/// <list type="bullet">
/// <item>剣（刀・ロングソード）: 型を構えて立つ相手へ 1 回ずつ出し、手の目標と実際の拳のずれ、刃の向きの目標と実際の刃の軸のずれ、
/// 目標の経路そのものの刃筋と実際の刃筋（刃先の動く向きが刃の面から外れた角）、刃先の速さ、体幹の傾き、転倒、スタミナの減りを、
/// 体の側の候補（手首の強さ・固さ、腕のサーボの速さ、肩・肘のトルク）を変えた版と並べる。</item>
/// <item>回し蹴り: 練習の前の差分（人の動きの手本のまま）と練習の後の差分（組み込みの tune.json）で、膝の目標と実際の膝のずれ・体幹の傾き・
/// 軸足の滑り・倒れた・当たった所を比べる。</item>
/// </list>
/// --logger "console;verbosity=detailed" で表を出す。
/// </summary>
[TestClass]
[TestCategory("Probe")]
public sealed class FormFidelityProbe
{
    private const float Dt = 1f / 240f;

    private static string F(double v, string format = "0.0") => v.ToString(format, CultureInfo.InvariantCulture);

    private static float Deg(float radians) => radians * 180f / MathF.PI;

    private static float Angle(Vector3 a, Vector3 b) => Deg(MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(a), Vector3.Normalize(b)), -1f, 1f)));

    /// <summary>刃筋のずれ [度]: 速度 <paramref name="velocity"/> が、向き <paramref name="orientation"/> の刃の面（ローカル XY）から外れた角（motioncard・motionaudit と同じ式: MotionMetrics.EdgeOffPlaneDegrees）。</summary>
    private static float OffPlane(Vector3 velocity, Quaternion orientation) => MiniRing.Arena.MotionTools.MotionMetrics.EdgeOffPlaneDegrees(velocity, orientation);

    /// <summary>指令を覚えておく入力元（剣士のボットをそのまま包む）。</summary>
    private sealed class Recorder(IFighterControl inner) : IFighterControl
    {
        public FighterCommand Last { get; private set; }

        public FighterCommand Read(in FighterSense sense, float dt) => Last = inner.Read(sense, dt);
    }

    internal sealed record SwingStats(string Technique, float HandRms, float HandMax, float AxisErrMean, float AxisErrAtPeak, float TargetOffPlane, float OffPlaneAtPeak,
        float TargetTipPeak, float TipPeak, float TrunkTiltMax, bool Fell, float StaminaUsed, int Cells, int SelfCells, float StrikeOffPlane, float StrikeAxisErr, float StrikeTipSpeed, float TipErrorRms = float.NaN, float StrikeTipError = float.NaN);

    /// <summary>型を 1 回出して測る（StrikeDrill と同じ組み方: 構えて立つ相手へ、間合いまで詰めてから 1 回）。</summary>
    internal static SwingStats Swing(MotionTechnique technique, MotionFrame guard, FighterTuning tuning, float? massScale = null, SwingPacing? pacing = null)
    {
        var bot = new SwordsmanBot([technique], guard, seed: 1, SwordStance.Rate, rest: 1000f, exact: true, tuning: tuning) { Pacing = pacing };
        var recorder = new Recorder(bot);
        using var game = new BoxingGame(tuning, seed: 1, corners: new BoxingCorners("stand", _ => new StandGuard(), _ => recorder, PlayerWeapon: technique.Weapon, PlayerTwoHanded: true, PlayerWear: HandWear.Glove));
        var me = game.Player;
        var sword = game.WeaponOf(me)!;
        if (massScale is { } k)
        {
            // 質量を差し替える（握る拳の質量を含む剛体の今の質量の k 倍。慣性は形の箱から: 比べる版はどれもこの道を通す。×1 も）。
            var mass = game.World.GetMass(sword.Body.BodyId) * k;
            game.World.MakeKinematic(sword.Body.BodyId);
            game.World.MakeDynamic(sword.Body.BodyId, mass);
        }

        var strike = new List<(float TargetSpeed, float Off, float Axis, float Speed, float TipError)>();
        double tipSq = 0;
        var tipSamples = 0;
        var tip = Weapon.TipOf(sword.Kind);
        var grip = Weapon.GripOf(sword.Kind);
        var victimStart = game.Cpu.Voxels!.Parts.Sum(p => p.Body.Grid.FilledCount);
        var ownStart = me.Voxels!.Parts.Sum(p => p.Body.Grid.FilledCount);
        double handSq = 0;
        var handMax = 0f;
        double axisSum = 0;
        var samples = 0;
        var started = false;
        var finished = false;
        var staminaStart = 0f;
        var trunkMax = 0f;
        var fell = false;
        var tipPeak = 0f;
        var axisAtPeak = float.NaN;
        var offAtPeak = float.NaN;
        var targetTipPeak = 0f;
        var targetOffAtPeak = float.NaN;
        Vector3? lastTargetTip = null;
        var targetSpeedNow = 0f;
        Quaternion lastTargetOrientation = Quaternion.Identity;
        for (var t = 0f; t < 13.43f && !finished; t += Dt)
        {
            game.Step(default, Dt);
            var active = bot.Current is not null;
            if (active && !started)
            {
                started = true;
                staminaStart = me.Stamina;
            }
            else if (!active && started)
            {
                finished = true;
                break;
            }

            if (!active)
            {
                continue;
            }

            var command = recorder.Last;
            var hand = command.Right.Orientation is not null ? Hand.Right : Hand.Left;
            var hc = hand == Hand.Right ? command.Right : command.Left;
            var frame = me.Frame;
            var handError = Vector3.Distance(me.HandTargetWorld(hand), me.GloveCenter(hand));
            handSq += handError * handError;
            handMax = MathF.Max(handMax, handError);
            var state = game.World.GetState(sword.Body.BodyId);
            var actualAxis = Vector3.Transform(Vector3.UnitX, state.Orientation);
            var offset = Vector3.Transform(tip, state.Orientation);
            var tipVelocity = state.LinearVelocity + Vector3.Cross(state.AngularVelocity, offset);
            var axisErr = float.NaN;
            if (hc.Orientation is { } q)
            {
                var targetOrientation = Quaternion.Concatenate(q, frame.Rotation);
                axisErr = Angle(Vector3.Transform(Vector3.UnitX, targetOrientation), actualAxis);
                axisSum += axisErr;
                var targetTip = me.HandTargetWorld(hand) + Vector3.Transform(tip - grip, targetOrientation);
                if (lastTargetTip is { } previous)
                {
                    var v = (targetTip - previous) / Dt;
                    targetSpeedNow = v.Length();
                    if (v.Length() > targetTipPeak)
                    {
                        targetTipPeak = v.Length();
                        targetOffAtPeak = OffPlane(v, lastTargetOrientation);
                    }
                }

                lastTargetTip = targetTip;
                lastTargetOrientation = targetOrientation;
            }

            samples++;
            var tipError = me.BladeTipWorld(hand) is { } tipNow && me.BladeTipTargetWorld(hand) is { } tipGoal ? Vector3.Distance(tipNow, tipGoal) : float.NaN;
            if (!float.IsNaN(tipError))
            {
                tipSq += tipError * tipError;
                tipSamples++;
            }

            strike.Add((targetSpeedNow, OffPlane(tipVelocity, state.Orientation), axisErr, tipVelocity.Length(), tipError));
            if (tipVelocity.Length() > tipPeak)
            {
                tipPeak = tipVelocity.Length();
                axisAtPeak = axisErr;
                offAtPeak = OffPlane(tipVelocity, state.Orientation);
            }

            trunkMax = MathF.Max(trunkMax, Angle(me.ChestPosition - me.PelvisPosition, Vector3.UnitY));
            fell |= me.State != FighterState.Standing;
        }

        // 打ちの区間: 目標の刃先が、その型の目標の最速の半分より速く動いている歩（構えから型の始めへ移る跳びを除くため、最初の 0.1 s は数えない）。
        var window = strike.Skip(24).Where(s => s.TargetSpeed >= 0.5f * targetTipPeak && !float.IsNaN(s.Off)).ToList();
        var weight = window.Sum(s => s.Speed);
        var strikeOff = weight > 0f ? window.Sum(s => s.Off * s.Speed) / weight : float.NaN;
        var strikeAxis = window.Count > 0 ? window.Where(s => !float.IsNaN(s.Axis)).Select(s => s.Axis).DefaultIfEmpty(float.NaN).Average() : float.NaN;
        var strikeSpeed = window.Count > 0 ? window.Max(s => s.Speed) : float.NaN;
        var strikeTip = window.Where(s => !float.IsNaN(s.TipError)).Select(s => s.TipError).DefaultIfEmpty(float.NaN).Average();
        return new SwingStats(technique.Name, samples == 0 ? float.NaN : 100f * MathF.Sqrt((float)(handSq / samples)), 100f * handMax,
            samples == 0 ? float.NaN : (float)(axisSum / samples), axisAtPeak, targetOffAtPeak, offAtPeak, targetTipPeak, tipPeak, trunkMax, fell,
            started ? staminaStart - me.Stamina : float.NaN,
            victimStart - game.Cpu.Voxels!.Parts.Sum(p => p.Body.Grid.FilledCount), ownStart - me.Voxels!.Parts.Sum(p => p.Body.Grid.FilledCount), strikeOff, strikeAxis, strikeSpeed, tipSamples == 0 ? float.NaN : 100f * MathF.Sqrt((float)(tipSq / tipSamples)), 100f * strikeTip);
    }

    private static IEnumerable<(string Name, FighterTuning Tuning, float? MassScale, bool Pacing)> BodyVariants()
    {
        var b = SwordStance.Tuning;
        yield return ("切っ先の PD なし", b with { BladeTipHz = 0f }, null, false);
        yield return ("今の体（切っ先の PD 2 Hz・1.5）", b, null, false);
        yield return ("切っ先の PD 4 Hz・1.5", b with { BladeTipHz = 4f }, null, false);
        yield return ("切っ先の PD 8 Hz・1", b with { BladeTipHz = 8f, BladeTipDampingRatio = 1f }, null, false);
        yield return ("切っ先の PD 4 Hz・3", b with { BladeTipDampingRatio = 3f }, null, false);
        yield return ("切っ先の PD 2 Hz・4", b with { BladeTipHz = 2f, BladeTipDampingRatio = 4f }, null, false);
        yield return ("技の時計（教科書の Pacing）", b, null, true);
    }

    [TestMethod]
    [TestCategory("Probe")]
    public void SwordFormsFollowTheirPath()
    {
        Console.WriteLine($"武器の質量: 刀 {F(Weapon.MassOf(WeaponKind.Katana), "0.000")} kg、ロングソード {F(Weapon.MassOf(WeaponKind.Longsword), "0.000")} kg、片手剣 {F(Weapon.MassOf(WeaponKind.IronSword), "0.000")} kg、鎌の爪 {F(Weapon.MassOf(WeaponKind.SickleClaw), "0.000")} kg");
        foreach (var kind in new[] { WeaponKind.Katana, WeaponKind.Longsword })
        {
            Console.WriteLine($"刃の長さ（握り→切っ先）{kind}: {F(100f * (Weapon.TipOf(kind).X - Weapon.GripOf(kind).X))} cm");
        }

        float armReach;
        using (var game = new BoxingGame(SwordStance.Tuning))
        {
            armReach = game.Player.ArmReach;
        }

        var t = SwordStance.Tuning;
        Console.WriteLine($"はめる物の手首: {F(t.WristServoHz, "0.00")} Hz・{F(t.WristMaxTorque * t.TorqueScale)} N·m・減衰 {F(t.WristDampingRatio)}、柄を握る手首: {F(t.GripWristServoHz)} Hz・{F(t.GripWristMaxTorque * t.TorqueScale)} N·m、腕のサーボの速さ {F(t.ArmServoMaxSpeed)} rad/s、TorqueScale {F(t.TorqueScale, "0.000")}");
        foreach (var weapon in new[] { WeaponKind.Katana, WeaponKind.Longsword })
        {
            var techniques = FormSwordsman.Techniques(weapon, armReach, 0f);
            var guard = FormSwordsman.Guard(weapon);
            Console.WriteLine();
            Console.WriteLine($"## {weapon}（{techniques.Count} 型）");
            Console.WriteLine("| 体 | 手のずれ RMS / 最大 [cm] | 刃の軸のずれ 平均 / 最速の時 [°] | 刃筋 目標の経路 / 実際 [°] | 刃先の最速 目標 / 実際 [m/s] | 体幹の傾き最大 [°] | 転倒 | スタミナ減 | 相手のセル | 自傷 | 打ちの区間: 刃筋 / 軸のずれ [°] / 刃先の最速 [m/s] | 切っ先のずれ RMS / 打ちの区間 [cm] |");
            Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
            var pacing = WeaponCatalog.Textbook(weapon).Pacing;
            foreach (var (name, tuning, massScale, paced) in BodyVariants())
            {
                var rows = new SwingStats[techniques.Count];
                Parallel.For(0, techniques.Count, i => rows[i] = Swing(techniques[i], guard, tuning, massScale, paced ? pacing : null));
                static double M(IEnumerable<float> xs) => xs.Where(x => !float.IsNaN(x)).DefaultIfEmpty(float.NaN).Average();
                Console.WriteLine($"| {name} | {F(M(rows.Select(r => r.HandRms)))} / {F(M(rows.Select(r => r.HandMax)))} | {F(M(rows.Select(r => r.AxisErrMean)))} / {F(M(rows.Select(r => r.AxisErrAtPeak)))} | {F(M(rows.Select(r => r.TargetOffPlane)))} / {F(M(rows.Select(r => r.OffPlaneAtPeak)))} | {F(M(rows.Select(r => r.TargetTipPeak)))} / {F(M(rows.Select(r => r.TipPeak)))} | {F(M(rows.Select(r => r.TrunkTiltMax)))} | {rows.Count(r => r.Fell)}/{rows.Length} | {F(M(rows.Select(r => r.StaminaUsed)), "0.000")} | {F(rows.Average(r => r.Cells), "0")} | {F(rows.Average(r => r.SelfCells), "0")} | {F(M(rows.Select(r => r.StrikeOffPlane)))} / {F(M(rows.Select(r => r.StrikeAxisErr)))} / {F(M(rows.Select(r => r.StrikeTipSpeed)))} | {F(M(rows.Select(r => r.TipErrorRms)))} / {F(M(rows.Select(r => r.StrikeTipError)))} |");
                if (name.StartsWith("今の体", StringComparison.Ordinal))
                {
                    foreach (var r in rows)
                    {
                        Console.WriteLine($"|   {r.Technique} | {F(r.HandRms)} / {F(r.HandMax)} | {F(r.AxisErrMean)} / {F(r.AxisErrAtPeak)} | {F(r.TargetOffPlane)} / {F(r.OffPlaneAtPeak)} | {F(r.TargetTipPeak)} / {F(r.TipPeak)} | {F(r.TrunkTiltMax)} | {(r.Fell ? "○" : "")} | {F(r.StaminaUsed, "0.000")} | {r.Cells} | {r.SelfCells} | {F(r.StrikeOffPlane)} / {F(r.StrikeAxisErr)} / {F(r.StrikeTipSpeed)} |");
                    }
                }
            }
        }
    }

    internal sealed record KickStats(float KneeRms, float TrunkTiltMax, float SupportSlide, float PelvisDrop, KneePractice.Outcome Outcome);

    private static KickStats Kick(KickShin.Height h, bool rear, KickTune tune, KickShin.Condition c)
    {
        var side = rear ? Hand.Right : Hand.Left;
        var support = rear ? Hand.Left : Hand.Right;
        double kneeSq = 0;
        var samples = 0;
        var trunk = 0f;
        Vector3? supportStart = null;
        var slide = 0f;
        float? pelvisStart = null;
        var pelvisMin = float.MaxValue;
        var spec = KickSpec.Of(h.Technique) with { Tunes = new KickTunes(tune, tune) };
        var outcome = KickMocap.Doll(c.Attacker == "standard" ? BodyBuild.Standard : Characters.CharacterRoster.Get(c.Attacker).Build, spec, side, tune.Standoff, tune.Seconds, c.Seed,
            (game, kneeTarget, time) =>
            {
                var me = game.Player;
                if (time > tune.Seconds)
                {
                    return;
                }

                if (kneeTarget is { } k)
                {
                    var e = Vector3.Distance(k, me.KneePosition(side));
                    kneeSq += e * e;
                    samples++;
                }

                trunk = MathF.Max(trunk, Angle(me.ChestPosition - me.PelvisPosition, Vector3.UnitY));
                var foot = me.AnklePosition(support) with { Y = 0f };
                supportStart ??= foot;
                slide = MathF.Max(slide, Vector3.Distance(foot, supportStart.Value));
                pelvisStart ??= me.PelvisPosition.Y;
                pelvisMin = MathF.Min(pelvisMin, me.PelvisPosition.Y);
            }, null, c.Victim == "standard" ? BodyBuild.Standard : Characters.CharacterRoster.Get(c.Victim).Build);
        return new KickStats(samples == 0 ? float.NaN : 100f * MathF.Sqrt((float)(kneeSq / samples)), trunk, 100f * slide, 100f * ((pelvisStart ?? 0f) - pelvisMin), outcome);
    }

    [TestMethod]
    [TestCategory("Probe")]
    public void RoundhouseBeforeAndAfterPractice()
    {
        var conditions = KickShin.TuneConditions.Concat(KickShin.ConfirmConditions).ToArray();
        Console.WriteLine("| 高さ | 脚 | 差分 | 間合い・長さ・方位・仰角・膝・足首 | 膝のずれ RMS [cm] | 体幹の傾き最大 [°] | 軸足の滑り [cm] | 腰の沈み [cm] | 倒れた | 当たった | 狙った所 | すねの帯 | ダメージ | 点 |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var h in KickShin.Heights)
        {
            var tunes = KickTunes.Load(h.File);
            foreach (var rear in new[] { true, false })
            {
                var trained = rear ? tunes.Rear : tunes.Front;
                foreach (var (label, tune) in new[] { ("練習の前", KickShin.Baseline(h)), ("練習の後", trained) })
                {
                    var rows = new KickStats[conditions.Length];
                    Parallel.For(0, conditions.Length, i => rows[i] = Kick(h, rear, tune, conditions[i]));
                    var o = rows.Select(r => r.Outcome).ToArray();
                    Console.WriteLine($"| {h.Name} | {(rear ? "後ろ" : "前")} | {label} | {F(tune.Standoff, "0.00")}・{F(tune.Seconds, "0.00")}・{F(Deg(tune.AzimuthShift), "0")}°・{F(Deg(tune.ElevationShift), "0")}°・{F(tune.BendShift, "0.00")}・{F(tune.FootPitch, "0.00")} | {F(rows.Average(r => r.KneeRms))} | {F(rows.Average(r => r.TrunkTiltMax))} | {F(rows.Average(r => r.SupportSlide))} | {F(rows.Average(r => r.PelvisDrop))} | {o.Count(x => x.Fell)}/{o.Length} | {o.Count(x => x.Landed)}/{o.Length} | {o.Count(x => x.Landed && h.Zones.Contains(x.Zone))} | {o.Count(x => KickShin.InBand(h, x))} | {F(o.Average(x => x.Damage))} | {F(o.Average(x => KickShin.Score(h, x)))} |");
                }
            }
        }
    }

    [TestMethod]
    [TestCategory("Probe")]
    public void ClawGridPlaneVersusRemoved() => ClawLab.Report(Console.Out);

    [TestMethod]
    [TestCategory("Probe")]
    public void WeaponMassBreakdown()
    {
        foreach (var kind in new[] { WeaponKind.Katana, WeaponKind.Longsword, WeaponKind.IronSword, WeaponKind.WoodenClub, WeaponKind.Bat, WeaponKind.Staff, WeaponKind.Jo, WeaponKind.Tonfa, WeaponKind.Shield, WeaponKind.SickleClaw })
        {
            var g = Weapon.Build(kind);
            var parts = Enumerable.Range(0, g.Palette.Count).Where(m => g.Remaining((byte)m) > 0)
                .Select(m => $"{g.Palette[(byte)m].Name} {g.Remaining((byte)m) * g.CellVolume * g.Palette[(byte)m].MechanicsOrDefault.Density:0.000} kg");
            Console.WriteLine($"{kind}: {Weapon.MassOf(kind):0.000} kg（{string.Join("、", parts)}）セル {g.CellSize.X * 1000f:0.0}×{g.CellSize.Y * 1000f:0.0}×{g.CellSize.Z * 1000f:0.0} mm、格子 {g.Dimensions}、長さ {2f * g.HalfExtents.X * 100f:0.0} cm");
            // 刃・棒の断面（X の 1/4・1/2・3/4 の所）: 埋まったセルの Y の数と Z の数の最大
            foreach (var share in new[] { 0.6f, 0.75f, 0.9f })
            {
                var x = (int)(share * g.Dimensions.X);
                var ys = new HashSet<int>();
                var zMax = 0;
                for (var y = 0; y < g.Dimensions.Y; y++)
                {
                    var zs = 0;
                    for (var z = 0; z < g.Dimensions.Z; z++)
                    {
                        if (g.At(x, y, z) != 0)
                        {
                            zs++;
                            ys.Add(y);
                        }
                    }

                    zMax = Math.Max(zMax, zs);
                }

                Console.WriteLine($"   X {share:0.00}: 幅 {ys.Count} セル（{ys.Count * g.CellSize.Y * 1000f:0} mm）・厚さ最大 {zMax} セル（{zMax * g.CellSize.Z * 1000f:0.0} mm）");
            }
        }
    }

    [TestMethod]
    [TestCategory("Probe")]
    public void ShieldSweepWithTipControl()
    {
        float armReach;
        using (var game = new BoxingGame())
        {
            armReach = game.Player.ArmReach;
        }

        var form = SwordShieldForms.Forms().First(f => f.Name == "yoko-barai");
        var technique = new MotionTechnique(form.Name, WeaponKind.IronSword, form, 1.408f);
        technique = technique with { Range = StrikeDrill.ReachOf(technique, armReach) };
        foreach (var (label, t) in new[] { ("3 Hz・1.5", FighterTuning.Default with { BladeTipHz = 3f, BladeTipDampingRatio = 1.5f }), ("2 Hz・2.5", FighterTuning.Default with { BladeTipHz = 2f, BladeTipDampingRatio = 2.5f }) })
        {
            foreach (var shift in new[] { -0.1f, 0f, 0.1f })
            {
                var r = StrikeDrill.Run(technique with { Range = technique.Range + shift }, FormSwordsman.Guard(WeaponKind.IronSword), SwordStance.Rate, t, wear: HandWear.Shield);
                Console.WriteLine($"{label} 間合い {technique.Range + shift:0.00}: セル {r.Cells}、最初 {r.FirstPart}、自傷 {r.SelfCells}");
            }
        }
    }
}
