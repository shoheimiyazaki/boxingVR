using MiniRing.Arena;
using MiniRing.Bots;
using MiniRing.Characters;
using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 調査（2026-11-27、物理 106 の釣り合いの傾き）: 全テストで落ちた「倒れた体が起き上がる」「剣の一撃」を、テストの種より多くの種で回し、
/// 揺れ（種で入れ替わる）か系統的な変化かを分ける。同じファイルを直す前の木（master）にも置いて比べる。
/// </summary>
[TestClass]
[TestCategory("Probe")]
public sealed class UprightSeedProbe
{
    private const float Dt = 1f / 240f;
    private static readonly float[] TipHz = [0f, 2f];
    private static readonly int[] RushSeeds = [3, 4, 5];
    private static readonly bool[] TwoHands = [true, false];
    private static readonly float[] AxeScales = [0.85f, 0.9f, 0.95f, 1f, 1.05f, 1.1f, 1.15f];

    /// <summary>生成直後から構えのまま 5 秒: 骨盤の水平の流れの時系列と、足の運びの歩数（FighterPhysicsTests.Standing と同じ場面）。</summary>
    [TestMethod]
    public void StandDrift()
    {
        var drifts = new List<float>();
        foreach (var yaw in new[] { 0f, 0.4f, 0.8f, 1.2f, 1.6f, 2.4f, 3.1f, 4.4f })
        {
            using var world = new Aigis.Physics.PhysicsWorld();
            world.AddStaticBox(new System.Numerics.Vector3(0f, -1.408f, 0f), new System.Numerics.Vector3(14.08f, 1.408f, 14.08f));
            var f = Fighter.Spawn(world, FighterTuning.Default, "f", System.Numerics.Vector3.Zero, yaw);
            var start = f.PelvisPosition;
            var cmd = new FighterCommand(System.Numerics.Vector3.Zero, f.Frame.Forward, new HandCommand(AiControl.Guard(Hand.Left, f.ArmReach), false), new HandCommand(AiControl.Guard(Hand.Right, f.ArmReach), false));
            for (var i = 0; i < (int)(5.035f * 240f); i++)
            {
                f.ApplyControl(cmd, Dt);
                world.Step(Dt);
            }

            var e = f.PelvisPosition - start;
            drifts.Add(MathF.Sqrt((e.X * e.X) + (e.Z * e.Z)) * 100f);
        }

        Console.WriteLine($"DRIFT cm: {string.Join(" ", drifts.Select(d => d.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)))} max {drifts.Max():0.0} mean {drifts.Average():0.0}");
    }

    /// <summary>刀の構え（BladeTipTests と同じ場面）: 切っ先のずれを、手の位置のずれと、それを除いた向きのずれに分ける。時系列も。</summary>
    [TestMethod]
    public void KatanaGuard()
    {
        foreach (var (hz, two) in TipHz.SelectMany(h => TwoHands.Select(t => (h, t))))
        {
            var seed = 1;
            using var game = new BoxingGame(SwordStance.Tuning with { BladeTipHz = hz }, seed: seed, corners: new BoxingCorners("stand", _ => new StandGuard(), _ => new KatanaGuardOnly(),
                PlayerWeapon: WeaponKind.Katana, PlayerTwoHanded: two));
            var (tipSum, handSum, n) = (0f, 0f, 0);
            var errSum = System.Numerics.Vector3.Zero;
            var (pelYaw, chestYaw, swordYaw) = (0f, 0f, 0f);
            var line = new List<string>();
            for (var i = 0; i < 240 * 4; i++)
            {
                game.Step(default, 1f / 240f);
                var p = game.Player;
                if (i > 240 && p.BladeTipWorld(Hand.Right) is { } tip && p.BladeTipTargetWorld(Hand.Right) is { } goal)
                {
                    var hand = System.Numerics.Vector3.Distance(p.GloveCenter(Hand.Right), p.HandTargetWorld(Hand.Right));
                    tipSum += System.Numerics.Vector3.Distance(tip, goal);
                    var dv = System.Numerics.Vector3.Transform(tip - goal, System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitY, -p.Frame.YawRadians));
                    errSum += dv;
                    float YawOf(int id) { var fw = System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitZ, game.World.GetState(id).Orientation); return MathF.Atan2(fw.X, fw.Z); }
                    float Wrap(float a) => MathF.Atan2(MathF.Sin(a), MathF.Cos(a));
                    pelYaw += Wrap(YawOf(p.Parts.Pelvis) - p.Frame.YawRadians);
                    chestYaw += Wrap(YawOf(p.Parts.Chest) - p.Frame.YawRadians);
                    var w = game.World.GetState(game.WeaponOf(p)!.Body.BodyId).Orientation;
                    var wf = System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitX, w);
                    swordYaw += Wrap(MathF.Atan2(wf.X, wf.Z) - p.Frame.YawRadians);
                    handSum += hand;
                    n++;
                    if (i % 48 == 0 || i % 48 == 1)
                    {
                        var up = System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitY, game.World.GetState(p.Parts.Chest).Orientation);
                        line.Add($"{i / 240f:0.0}:tip {System.Numerics.Vector3.Distance(tip, goal) * 100:0.0} hand {hand * 100:0.0} chestUp {up.Y:0.000} pelW {game.World.GetState(p.Parts.Pelvis).AngularVelocity.Y:0.00} chestW {game.World.GetState(p.Parts.Chest).AngularVelocity.Y:0.00}");
                    }
                }
            }

            if (Environment.GetEnvironmentVariable("KATANA_LINE") != "1") { line.Clear(); }
            Console.WriteLine($"KATANA hz {hz} twoHanded {two}: yaw pel/chest/sword {pelYaw / n * 57.3f:0.0}/{chestYaw / n * 57.3f:0.0}/{swordYaw / n * 57.3f:0.0} meanErr side/up/fwd {errSum.X / n * 100:0.0}/{errSum.Y / n * 100:0.0}/{errSum.Z / n * 100:0.0} tip {tipSum / n * 100:0.0} cm hand {handSum / n * 100:0.0} cm | {string.Join(" | ", line)}");
        }
    }

    private sealed class KatanaGuardOnly : IFighterControl
    {
        public FighterCommand Read(in FighterSense sense, float dt)
        {
            var face = sense.OpponentChest - sense.Self.Origin;
            face.Y = 0f;
            face = face.LengthSquared() > 1e-6f ? System.Numerics.Vector3.Normalize(face) : System.Numerics.Vector3.UnitZ;
            return FormSwordsman.GuardCommand(WeaponKind.Katana, sense, face, FighterTuning.Default.WalkSpeed, FighterTuning.Default.MaxTorsoTwistRadians);
        }
    }

    /// <summary>構えて立つだけ 20 秒: 部位ごとの酸素（胴・腕・脚）と血の酸素・スタミナ（震えが酸素を使っていたか）。</summary>
    [TestMethod]
    public void IdleOxygen()
    {
        using var world = new Aigis.Physics.PhysicsWorld();
        world.AddStaticBox(new System.Numerics.Vector3(0f, -1.408f, 0f), new System.Numerics.Vector3(14.08f, 1.408f, 14.08f));
        var f = Fighter.Spawn(world, FighterTuning.Default, "f", System.Numerics.Vector3.Zero, 0f);
        var cmd = new FighterCommand(System.Numerics.Vector3.Zero, f.Frame.Forward, new HandCommand(AiControl.Guard(Hand.Left, f.ArmReach), false), new HandCommand(AiControl.Guard(Hand.Right, f.ArmReach), false));
        var line = new List<string>();
        for (var i = 0; i < 240 * 20; i++)
        {
            f.ApplyControl(cmd, Dt);
            world.Step(Dt);
            if (i % (240 * 4) == 0 || i == (240 * 20) - 1)
            {
                line.Add($"{i / 240f:0}s trunk {f.OxygenOf(MuscleRegion.Trunk):0.000} armR {f.OxygenOf(MuscleRegion.RightArm):0.000} legL {f.OxygenOf(MuscleRegion.LeftLeg):0.000} blood {f.BloodOxygen:0.000} stam {f.Stamina:0.000}");
            }
        }

        Console.WriteLine("IDLEO2 " + string.Join(" | ", line));
    }

    /// <summary>StaminaRushTests の連打（種 3）: 5 秒ごとの終わりの部位ごとの酸素。</summary>
    [TestMethod]
    public void RushOxygen()
    {
        const float dt = 1f / 60f;
        var corners = BoxingCorners.Default with { Player = _ => new IntentControl(), Cpu = _ => new IntentControl() };
        using var game = new BoxingGame(FighterTuning.Default with { ConcussionSpin = float.PositiveInfinity, CoreToneFloor = 1f }, seed: 3, corners: corners);
        var control = (IntentControl)game.PlayerControl!;
        ((IntentControl)game.CpuControl!).Input = new FighterIntent(System.Numerics.Vector2.Zero, Evade: true);
        var (rushing, tap, next) = (0f, 0f, 5f);
        var line = new List<string>();
        for (var clock = 0f; rushing < 20f && clock < 60f; clock += dt)
        {
            var standing = game.Player.State == FighterState.Standing && game.Cpu.State == FighterState.Standing;
            var phase = tap / 0.2014f;
            var index = (int)MathF.Floor(phase);
            var down = standing && phase - index < 0.67f;
            control.Input = new FighterIntent(System.Numerics.Vector2.Zero, Light: down && index % 2 == 0, Heavy: down && index % 2 == 1);
            game.Step(new BoxingInput(default), dt);
            if (!standing) { continue; }
            tap += dt;
            rushing += dt;
            if (rushing >= next)
            {
                var f = game.Player;
                line.Add($"{next:0}s trunk {f.OxygenOf(MuscleRegion.Trunk):0.00} armL {f.OxygenOf(MuscleRegion.LeftArm):0.00} armR {f.OxygenOf(MuscleRegion.RightArm):0.00} legs {f.OxygenOf(MuscleRegion.LeftLeg):0.00}/{f.OxygenOf(MuscleRegion.RightLeg):0.00} blood {f.BloodOxygen:0.00}");
                next += 5f;
            }
        }

        Console.WriteLine("RUSHO2 " + string.Join(" | ", line));
    }

    /// <summary>連打の 4 つの窓（種 3・4・5 の平均）を、筋肉 1 kg あたりの酸素の蓄えを振って。</summary>
    [TestMethod]
    public void RushStore()
    {
        foreach (var store in (Environment.GetEnvironmentVariable("STORES") ?? "1300,1200,1100").Split(',').Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)))
        {
            var runs = RushSeeds.Select(seed => StaminaRushTests.RushWindows(20f, tuning: FighterTuning.Default with { OxygenStorePerKg = store }, seed: seed)).ToArray();
            var mean = Enumerable.Range(0, runs[0].Length).Select(i => runs.Average(r => r[i])).ToArray();
            Console.WriteLine(System.FormattableString.Invariant($"RUSHSTORE {store}: {mean[0]:F2} {mean[1]:F2} {mean[2]:F2} {mean[3]:F2}  ratio1 {mean[1] / mean[0]:F3} ratio3 {mean[3] / mean[0]:F3}"));
        }
    }

    /// <summary>RoundRulesTests.DownCount と同じ: 相手の脳しんとうを 0.95 にして、倒れるまでの骨盤の高さ・傾き・張り・状態。</summary>
    [TestMethod]
    public void LimpFall()
    {
        using var game = new BoxingGame(seed: 1);
        game.Step(default, 1f / 60f);
        game.Cpu.Concussion = 0.95f;
        var line = new List<string>();
        for (var t = 0f; t < 6f; t += 1f / 60f)
        {
            game.Step(default, 1f / 60f);
            var f = game.Cpu;
            if ((int)(t * 60f) % 15 == 0)
            {
                line.Add($"{t:0.00} {f.State} pel {(f.PelvisPosition.Y - BoxingGame.RingSurfaceHeight) * 100:0} up {f.Uprightness:0.00} tone {f.Tone:0.00} cons {f.Consciousness:0.00} faint {f.FaintTime:0.0}");
            }
        }

        Console.WriteLine("LIMP " + string.Join(" | ", line));
    }

    /// <summary>起き上がりの 1 種（GETUP_SEED、既定 4）: 状態の移り変わり・脳しんとうが増えた瞬間（頭のねじれ）・意識・張り。</summary>
    [TestMethod]
    public void GetUpTrace()
    {
        foreach (var (seed, store) in new[] { (18, 1300f), (18, 1200f), (9, 1300f), (9, 1200f) })
        {
        var still = BotCatalog.Boxer with { Attacks = false, DefenseRate = 0f, Prediction = 0f, Aggression = 0f };
        var game = new BoxingGame(FighterTuning.Default with { OxygenStorePerKg = store }, seed: seed, corners: new BoxingCorners("still", s => new BoxerBot(still, s), s => new BoxerBot(still, s)));
        for (var t0 = 0f; t0 < 2.518f; t0 += Dt) { game.Step(default, Dt); }
        var cpu = game.Cpu;
        using (game)
        {
            DamageModelTests.Hit(game, cpu, DamageModelTests.Chin(cpu), mass: 12f, speed: 3.692f);
            var (state, conc) = (cpu.State, cpu.Concussion);
            var line = new List<string>();
            for (var t = 0f; t < 26.85f; t += Dt)
            {
                game.Step(default, Dt);
                if (cpu.State != state || cpu.Concussion > conc + 0.05f)
                {
                    line.Add($"{t:0.00} {cpu.State} conc {cpu.Concussion:0.00} twist {cpu.LastHeadTwist:0.0} cons {cpu.Consciousness:0.00} tone {cpu.Tone:0.00} pel {(cpu.PelvisPosition.Y - BoxingGame.RingSurfaceHeight) * 100:0}");
                }

                (state, conc) = (cpu.State, cpu.Concussion);
            }

            Console.WriteLine($"GETUPTRACE seed {seed} store {store}: end {cpu.State} cons {cpu.Consciousness:0.00} | " + string.Join(" | ", line.Take(12)));
        }
        }
    }

    /// <summary>倒れて 2 秒の間の、頭が床を打った回数（脳しんとうが 0.05 以上増えた歩）と、2 秒での脳しんとう。18 種。</summary>
    [TestMethod]
    public void FallHeadHits()
    {
        var rows = new List<string>();
        foreach (var seed in Enumerable.Range(1, 18))
        {
            var (game, cpu) = DamageModelTests.NewGame(seed);
            using (game)
            {
                DamageModelTests.Hit(game, cpu, DamageModelTests.Chin(cpu), mass: 12f, speed: 3.692f);
                var (hits, conc, maxTwist) = (0, cpu.Concussion, 0f);
                for (var t = 0f; t < 2f; t += Dt)
                {
                    game.Step(default, Dt);
                    if (cpu.Concussion > conc + 0.05f) { hits++; maxTwist = MathF.Max(maxTwist, cpu.LastHeadTwist); }
                    conc = cpu.Concussion;
                }

                rows.Add($"{seed}:{hits}x/{conc:0.0}/{maxTwist:0}");
            }
        }

        Console.WriteLine("FALLHITS " + string.Join(" ", rows));
    }

    /// <summary>かかと落とし（右・左）を技の長さ 0.85〜1.15 で: 当たりのエネルギーの最大と、拳の速さ・質量（KickContactTests.Run）。</summary>
    [TestMethod]
    public void AxeEnergy()
    {
        foreach (var side in new[] { Hand.Right, Hand.Left })
        {
            var row = new List<string>();
            foreach (var scale in AxeScales)
            {
                var r = KickContactTests.Run(KickTechnique.Axe, side, durationScale: scale);
                row.Add($"{scale:0.00}:{(r.Energies is { Count: > 0 } e ? e.Max() : 0f):0}J/{r.MaxSpeed:0.0}m/s");
            }

            Console.WriteLine($"AXE {side} " + string.Join(" ", row));
        }
    }

    /// <summary>HeelStompTests の足の替え（種 1〜18）: 始まった数・転ばなかった数。</summary>
    [TestMethod]
    public void HeelSwitch()
    {
        var (started, kept) = (0, 0);
        var rows = new List<string>();
        foreach (var seed in Enumerable.Range(1, 18))
        {
            var (first, after, hits, falls) = HeelStompTests.SwitchRun(seed, null);
            if (first is null) { continue; }
            started++;
            kept += falls == 0 ? 1 : 0;
            rows.Add($"{seed}:{(after is null ? "noswitch" : "sw")}/{hits}/{falls}");
        }

        Console.WriteLine($"HEEL started {started} kept {kept} | " + string.Join(" ", rows));
    }

    [TestMethod]
    public void GetUp()
    {
        var (risen, down, n, stuck) = (0, 0, 0, 0);
        foreach (var seed in Enumerable.Range(1, 36))
        {
            var (game, cpu) = DamageModelTests.NewGame(seed);
            using (game)
            {
                DamageModelTests.Hit(game, cpu, DamageModelTests.Chin(cpu), mass: 12f, speed: 3.692f);
                var (downAt, upAt) = (-1f, -1f);
                for (var t = 0f; t < 26.85f; t += Dt)
                {
                    game.Step(default, Dt);
                    if (downAt < 0f && cpu.State == FighterState.Down)
                    {
                        downAt = t;
                    }

                    if (downAt >= 0f && upAt < 0f && cpu.State == FighterState.Standing)
                    {
                        upAt = t;
                    }
                }

                n++;
                down += downAt >= 0f ? 1 : 0;
                risen += upAt > 0f ? 1 : 0;
                stuck += cpu.State == FighterState.GettingUp ? 1 : 0;
                Console.WriteLine($"GETUP seed {seed}: down {downAt:0.00} up {upAt:0.00} cons {cpu.Consciousness:0.00} end {cpu.State}");
            }
        }

        Console.WriteLine($"GETUP SUMMARY down {down}/{n} risen {risen}/{n} stuck {stuck}");
    }

    /// <summary>腰の強さが右ストレートに乗るか: 間合い 0.9〜1.2（13 本）× 種 1〜3 の合計で、標準と腰の弱い体（60 N·m）の溜めなしの右のエネルギー。</summary>
    [TestMethod]
    public void HipCross()
    {
        var standard = FighterTuning.Default;
        var weak = standard with { PelvisRotatorTorque = 60f };
        var (strong, weakSum, n) = (0f, 0f, 0);
        foreach (var seed in new[] { 1, 2, 3 })
        {
            for (var i = 0; i <= 12; i++)
            {
                var range = 0.9f + (0.025f * i);
                strong += PunchWeightTests.Cross(standard, 0.06f, seed, range);
                weakSum += PunchWeightTests.Cross(weak, 0.06f, seed, range);
                n++;
            }
        }

        Console.WriteLine($"HIPCROSS {n} punches: standard {strong:0} J, weak hips {weakSum:0} J, ratio {strong / weakSum:0.000}");
    }

    /// <summary>jin の面・エドガーの切り下ろし（教育）を 5 人の受け手へ、種 11: 脳しんとうが増えた時刻と頭のねじれ・受け手の状態と胸の傾き。</summary>
    [TestMethod]
    public void SwordTrace()
    {
        foreach (var (id, form) in new[] { ("jin", "shomen"), ("edgar", "kiri-oroshi") })
        {
            var who = CharacterRoster.Get(id);
            var educated = who.WeaponStyle!;
            var tune = educated.Forms![form];
            foreach (var v in new[] { "standard", "konrad", "ramon", "joao", "dmitri" })
            {
                var last = 0f;
                var log = new List<string>();
                var o = SwordPractice.Swing(who, educated, SwordPractice.Technique(who, form, tune), 11, SwordPractice.Victim(v), (game, t, active) =>
                {
                    var foe = game.Cpu;
                    if (foe.Concussion > last + 0.02f)
                    {
                        var up = System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitY, game.World.GetState(foe.Parts.Chest).Orientation);
                        log.Add($"t {t:0.00} conc {foe.Concussion:0.00} twist {foe.LastHeadTwist:0.0} state {foe.State} chestUp {up.Y:0.00} headY {foe.HeadPosition.Y:0.000}");
                    }

                    last = foe.Concussion;
                });
                Console.WriteLine($"TRACE {id} {form} {v}: incap {o.Incapacitated} cEnd {o.ConsciousnessEnd:0.00} cMin {o.ConsciousnessMin:0.00} impact {o.ImpactSpeed:0.0} | {string.Join(" | ", log)}");
            }
        }
    }

    [TestMethod]
    public void SwordOneBlow()
    {
        string[] victims = ["standard", "konrad", "ramon", "joao", "dmitri"];
        foreach (var id in new[] { "jin", "edgar", "konrad" })
        {
            var who = CharacterRoster.Get(id);
            var educated = who.WeaponStyle!;
            var textbook = WeaponCatalog.Textbook(who.Weapon!.Value);
            foreach (var (form, tune) in educated.Forms!)
            {
                var (tb, ed, tbSpeed, edSpeed, n) = (0, 0, 0f, 0f, 0);
                foreach (var seed in Enumerable.Range(11, 6))
                {
                    foreach (var v in victims)
                    {
                        var b = SwordPractice.Swing(who, textbook, SwordPractice.Technique(who, form, FormTune.Identity), seed, SwordPractice.Victim(v));
                        var a = SwordPractice.Swing(who, educated, SwordPractice.Technique(who, form, tune), seed, SwordPractice.Victim(v));
                        tb += b.Incapacitated ? 1 : 0;
                        ed += a.Incapacitated ? 1 : 0;
                        tbSpeed += b.ImpactSpeed;
                        edSpeed += a.ImpactSpeed;
                        n++;
                    }
                }

                Console.WriteLine($"SWORD {id} {form}: textbook {tb}/{n} at {tbSpeed / n:0.0} m/s, educated {ed}/{n} at {edSpeed / n:0.0} m/s");
            }
        }
    }
}
