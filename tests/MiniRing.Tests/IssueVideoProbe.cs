using System.Globalization;
using System.Numerics;
using MiniRing.Arena;
using MiniRing.Bots;
using MiniRing.Characters;
using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 撮影（BASE_BODY_PLAN §10.9）: 既知の未解決の問題ごとに、問題が見て分かる短い動画（音あり）を <c>media_tmp/basebody_v2/issues/</c> に撮る。
/// 通常の実行からは外す（Probe）。<c>dotnet test --filter "FullyQualifiedName~IssueVideoProbe.Claw"</c> のように 1 本ずつ。
/// </summary>
[TestClass]
[TestCategory("Probe")]
public sealed class IssueVideoProbe
{
    private const int Width = 960;
    private const int Height = 540;
    private const int Fps = 30;

    private static string OutPath(string name)
    {
        var dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "media_tmp", "basebody_v2", "issues"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, name + ".mp4");
    }

    private static void Make(string name, string title, Func<Action<BoxingGame, float, string?, string?>, string> run, Func<BoxingGame, (Vector3 Eye, Vector3 Look, Vector3 Up)>? camera)
    {
        var path = OutPath(name);
        new SparVideo(Width, Height, Fps).MakeCustom(title, run, camera, path, Console.Out);
        File.Delete(Path.ChangeExtension(path, ".wav"));
        Console.WriteLine($"VIDEO {path}");
    }

    private static string Inv(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    private static float Tilt(BoxingGame game, int part) => MathF.Acos(Math.Clamp(Vector3.Transform(Vector3.UnitY, game.World.GetState(part).Orientation).Y, -1f, 1f)) * 180f / MathF.PI;

    private static float Yaw(BoxingGame game, int part) => BodyFrame.YawOf(Vector3.Transform(Vector3.UnitZ, game.World.GetState(part).Orientation), 0f) * 180f / MathF.PI;

    /// <summary>鎌: 目標の向きを出し続けて 3 秒。刃の向きのずれ [度] を出す。前から手元へ寄る。</summary>
    [TestMethod]
    public void Claw()
    {
        var cases = new (string Name, Vector3 Root, Vector3 Aim, Vector3 Normal)[]
        {
            ("横向き（level across）", new(-0.6f, 0.3f, 0.7f), new(1f, 0f, 0f), new(0f, 1f, 0f)),
            ("上向き（up）", new(-0.5f, 0.3f, 0.4f), new(0f, 1f, 0.3f), new(0f, 0f, 1f)),
        };
        var rootLocal = Vector3.Zero;
        Make("claw", "鎌の向きが目標から外れ、前腕と鎌が揺れ続ける", step =>
        {
            var offset = 0f;
            var summary = new List<string>();
            foreach (var (name, root, aim, normal) in cases)
            {
                var creature = CharacterRoster.Get("galva");
                var target = CharacterRoster.Get("marcus");
                var corners = BotCatalog.Corners("boxer") with
                {
                    PlayerBody = creature.Build, PlayerWear = creature.Wear, PlayerLook = creature.Look, PlayerFaceSpec = creature.Face,
                    CpuBody = target.Build, CpuWear = HandWear.None, CpuLook = target.Look, CpuFaceSpec = target.Face,
                    ExternalBlue = true, Referee = false, Ring = RingLayout.Boxing with { StartGap = 4f, FromCorners = false },
                };
                using var game = new BoxingGame(new FighterTuning(), seed: 1, corners: corners);
                var f = game.Player;
                rootLocal = root * f.ArmReach;
                var want = new ClawSegment(rootLocal, rootLocal + (Vector3.Normalize(aim) * ClawSegment.Length), normal);
                var command = new FighterCommand(Vector3.Zero, Vector3.UnitZ, ClawStroke.Guard(Hand.Left, f.ArmReach).ToCommand(Hand.Left), want.ToCommand(Hand.Right), ClawBot.Stance(f.ArmReach));
                var blue = new FighterCommand(Vector3.Zero, -Vector3.UnitZ, new HandCommand(AiControl.Guard(Hand.Left, 0.7f), false), new HandCommand(AiControl.Guard(Hand.Right, 0.7f), false));
                var worst = 0f;
                const float dt = 1f / 240f;
                for (var i = 0; i < 240 * 3; i++)
                {
                    game.Step(new BoxingInput(command, Blue: blue), dt);
                    var claw = game.GloveOf(f, Hand.Right)!;
                    var state = game.World.GetState(claw.Body.BodyId);
                    var self = game.PlayerSense.Self;
                    var gotRoot = self.WorldToLocal(state.Position + Vector3.Transform(Weapon.GripOf(claw.Kind), state.Orientation));
                    var gotTip = self.WorldToLocal(state.Position + Vector3.Transform(Weapon.GripOf(claw.Kind) + SickleClawShape.Edge(Hand.Right), state.Orientation));
                    var angle = MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(want.Tip - want.Root), Vector3.Normalize(gotTip - gotRoot)), -1f, 1f)) * 180f / MathF.PI;
                    worst = MathF.Max(worst, angle);
                    step(game, offset + (i * dt), Inv($"刃の向きのずれ {angle:0} 度（許し 15 度）  鎌の回り {state.AngularVelocity.Length():0.0} rad/s"), name);
                }

                summary.Add(Inv($"{name}: worst {worst:0.0} deg"));
                offset += 3f;
            }

            return string.Join("; ", summary);
        }, game => (game.Player.Frame.LocalToWorld(rootLocal + new Vector3(-0.35f, 0.15f, 1.15f)), game.Player.Frame.LocalToWorld(rootLocal + new Vector3(0f, -0.02f, 0.1f)), Vector3.UnitY));
    }

    /// <summary>止まった HMD（一人称・刀）で、2.5 秒から 8 秒、足を踏み直し続ける。足元へ寄る。</summary>
    [TestMethod]
    public void VrStill()
    {
        Make("vrstill", "止まった HMD なのに、足が踏み直し続ける", step =>
        {
            const float dt = 1f / 240f;
            using var game = new BoxingGame(FighterTuning.Default with { PlayerFatigue = false }, corners: SwordTraining.Corners(WeaponKind.Katana, 2.535f)) { HidePlayerHead = true };
            var mapper = new VrPlayerMapper(weaponMode: true) { GripHolds = true, TwoHand = VrTwoHand.Right, FirstPerson = true, HeldWeapon = WeaponKind.Katana };
            var head = new Vector3(0f, 1.62f, 0f);
            var raw = new VrRawFrame(head, Quaternion.Identity, new Vector3(-0.35f, 1.0f, -0.1f), new Vector3(0.15f, 1.25f, -0.4f), true, true, Vector2.Zero, Vector2.Zero, 1f, 1f, false, true, Quaternion.Identity, Quaternion.Identity);
            var fw = game.Player.FootworkState;
            var (stepsAt, startPelvis) = (-1, Vector3.Zero);
            for (var i = 0; i < 240 * 10.5f; i++)
            {
                game.Step(mapper.Read(raw, game.PlayerSense, dt), dt);
                var t = i * dt;
                if (t < 2.5f)
                {
                    continue;
                }

                if (stepsAt < 0)
                {
                    (stepsAt, startPelvis) = (fw.Steps, game.Player.PelvisPosition);
                }

                var p = game.Player.PelvisPosition;
                step(game, t - 2.5f, Inv($"止まった HMD（入力は何も変わらない）  {t - 2.5f:0.0} s"), Inv($"足を踏み直した回数 {fw.Steps - stepsAt}  骨盤のずれ {Vector2.Distance(new(p.X, p.Z), new(startPelvis.X, startPelvis.Z)) * 100f:0} cm"));
            }

            return Inv($"steps {fw.Steps - stepsAt} in 8 s");
        }, game =>
        {
            var p = game.Player.PelvisPosition;
            return (p + new Vector3(1.7f, -0.1f, 0.7f), p + new Vector3(0f, -0.4f, 0f), Vector3.UnitY);
        });
    }

    /// <summary>踵の連打の踏む脚を替える所で、攻め手（青）が転ぶ試合を探して、転ぶ前後を撮る。</summary>
    [TestMethod]
    public void Heel()
    {
        int? seed = null;
        var fellAt = 0f;
        foreach (var s in Enumerable.Range(1, 12))
        {
            float? fell = null;
            var (_, _, _, falls) = HeelStompTests.SwitchRun(s, null, (g, t) =>
            {
                if (fell is null && t > 3f && g.Cpu.State != FighterState.Standing)
                {
                    fell = t;
                }
            });
            Console.WriteLine($"scan seed {s}: falls {falls} fellAt {fell}");
            if (falls > 0 && fell is { } at)
            {
                (seed, fellAt) = (s, at);
                break;
            }
        }

        Assert.IsNotNull(seed, "転ぶ種が見つからない");
        var from = MathF.Max(0f, fellAt - 3.5f);
        Make("heel", Inv($"かかとの連打: 踏む脚を替えて、攻め手（青）が転ぶ（種 {seed}）"), step =>
        {
            var (_, after, _, falls) = HeelStompTests.SwitchRun(seed.Value, null, (g, t) =>
            {
                if (t < from || t > fellAt + 3f)
                {
                    return;
                }

                var ground = (g.CpuControl as GroundStriker)?.Ground;
                step(g, t - from, "倒れた相手（赤）をかかとで踏み続ける攻め（青）",
                    Inv($"踏む足 {ground?.CurrentFoot}  替えた回数 {ground?.FootSwitches}  脚の蓄え 左 {g.Cpu.LegOxygen(Hand.Left):0.00} 右 {g.Cpu.LegOxygen(Hand.Right):0.00}  {(g.Cpu.State == FighterState.Standing ? "立っている" : "転んだ")}"));
            });
            return Inv($"seed {seed} switched {after} falls {falls} fellAt {fellAt:0.00}");
        }, game =>
        {
            var c = game.Cpu.PelvisPosition;
            var v = game.Player.PelvisPosition;
            var mid = Vector3.Lerp(c, v, 0.5f);
            return (mid + new Vector3(2.0f, 1.5f, 0.4f), mid + new Vector3(0f, -0.1f, 0f), Vector3.UnitY);
        });
    }

    private sealed class SeriesControl : IFighterControl
    {
        private float _t;
        private static readonly Vector3 LeftGuard = new(0.14f, 0.05f, 0.27f);
        private static readonly Vector3 RightGuard = new(-0.14f, 0.05f, 0.24f);

        /// <summary>今打っている発（0 = 構え、1〜3）。</summary>
        public int Punch { get; private set; }

        public FighterCommand Read(in FighterSense sense, float dt)
        {
            _t += dt;
            var face = sense.OpponentChest - sense.Self.Origin;
            face.Y = 0f;
            var forward = face.LengthSquared() > 1e-6f ? Vector3.Normalize(face) : Vector3.UnitZ;
            // 構えて 1 秒 → 荷重 0.3 秒 → 3 発（右・右・右、投げる 0.13 s・戻し 0.06 s）。JointPunchSeriesTests.RunSeries と同じ指令。
            const float start = 1f;
            const float load = 0.30f;
            const float throwSeconds = JointPunchSeriesTests.ThrowSeconds;
            const float recover = 0.06f;
            var left = LeftGuard;
            var right = RightGuard;
            var local = _t - start - load;
            Punch = 0;
            if (_t >= start && local < 0f)
            {
                left.Z = 0.35f;
                right.Z = 0.13f;
            }
            else if (local >= 0f)
            {
                var k = (int)(local / (throwSeconds + recover));
                var within = local - (k * (throwSeconds + recover));
                if (k < 3 && within < throwSeconds)
                {
                    var u = within / throwSeconds;
                    var smooth = u * u * (3f - (2f * u));
                    var from = k == 0 ? 0.13f : RightGuard.Z;
                    right.Z = from + ((0.72f - from) * smooth);
                    Punch = k + 1;
                }
            }

            return new FighterCommand(Vector3.Zero, forward, new HandCommand(left, false), new HandCommand(right, false));
        }
    }

    /// <summary>同じ拳の 3 連打（JointPunchSeries）を斜め前から。骨盤・胸の向き [度] を出す。1/6 のスロー。</summary>
    [TestMethod]
    public void Series()
    {
        const float slow = 6f;
        Make("series", "同じ拳を 3 発: 発ごとに腰の回りがばらつく（この試合の場では 18 → 35 → 9 度。1/6 のスロー）", step =>
        {
            const float dt = 1f / 240f;
            var control = new SeriesControl();
            var tuning = FighterTuning.MomentumPunch with { StaminaFatigue = false, PlayerFatigue = false };
            using var game = new BoxingGame(tuning, 1, new BoxingCorners("series", _ => new StandGuard(), _ => control, Referee: false) { Ring = RingLayout.Boxing with { StartGap = 4f, FromCorners = false } });
            var f = game.Player;
            var sweep = new float[4];
            var onset = new float[4];
            var startYaw = 0f;
            var last = 0;
            for (var i = 0; i < 240 * 3.2f; i++)
            {
                game.Step(default, dt);
                var t = i * dt;
                var pelvis = Yaw(game, f.Parts.Pelvis);
                var chest = Yaw(game, f.Parts.Chest);
                if (control.Punch != last)
                {
                    last = control.Punch;
                    startYaw = pelvis;
                    if (last > 0)
                    {
                        onset[last] = pelvis;
                    }
                }

                if (last > 0)
                {
                    sweep[last] = MathF.Max(sweep[last], MathF.Abs(pelvis - startYaw));
                }

                if (t < 1.2f || t > 2.1f)
                {
                    continue;
                }

                var tv = (t - 1.2f) * slow;
                step(game, tv, Inv($"{(control.Punch > 0 ? $"{control.Punch} 発目" : "構え・戻し")}   骨盤の向き {pelvis:0.0} 度  胸の向き {chest:0.0} 度"),
                    Inv($"骨盤の振り 1 発目 {sweep[1]:0.0} / 2 発目 {sweep[2]:0.0} / 3 発目 {sweep[3]:0.0} 度"));
            }

            return Inv($"pelvis sweep {sweep[1]:0.0}/{sweep[2]:0.0}/{sweep[3]:0.0} onset {onset[1]:0.0}/{onset[2]:0.0}/{onset[3]:0.0}");
        }, game =>
        {
            var frame = game.Player.Frame;
            var p = game.Player.PelvisPosition;
            return (frame.LocalToWorld(frame.WorldToLocal(p) + new Vector3(-1.5f, 0.5f, 1.3f)), p + new Vector3(0f, 0.2f, 0f), Vector3.UnitY);
        });
    }

    /// <summary>konrad の斬り下ろし: 教科書と教育（oberhau）を、教育のほうが倒せない相手へ。</summary>
    [TestMethod]
    public void Konrad()
    {
        const float slow = 1.5f;
        var who = CharacterRoster.Get("konrad");
        var educated = who.WeaponStyle!;
        var textbook = WeaponCatalog.Textbook(who.Weapon!.Value);
        var tune = educated.Forms!["oberhau"];
        string? pick = null;
        var best = int.MinValue;
        foreach (var v in new[] { "standard", "konrad", "ramon", "joao", "dmitri", "heavy" })
        {
            var gap = 0;
            foreach (var seed in new[] { 11, 12 })
            {
                var a = SwordPractice.Swing(who, textbook, SwordPractice.Technique(who, "oberhau", FormTune.Identity), seed, SwordPractice.Victim(v));
                var b = SwordPractice.Swing(who, educated, SwordPractice.Technique(who, "oberhau", tune), seed, SwordPractice.Victim(v));
                gap += (a.Incapacitated ? 1 : 0) - (b.Incapacitated ? 1 : 0);
                Console.WriteLine($"scan {v} seed {seed}: textbook {a.Incapacitated} (tip {a.TipPeak:0.0}, self {a.SelfCells}) educated {b.Incapacitated} (tip {b.TipPeak:0.0}, self {b.SelfCells})");
            }

            if (gap > best)
            {
                (best, pick) = (gap, v);
            }
        }

        Make("konrad", Inv($"konrad の斬り下ろし（相手 {pick}）: 教科書 → 教育（oberhau）、1/{slow:0} のスロー"), step =>
        {
            var offset = 0f;
            var lines = new List<string>();
            foreach (var (label, style, t) in new[] { ("教科書の斬り下ろし", textbook, FormTune.Identity), ("教育の斬り下ろし（oberhau）", educated, tune) })
            {
                var end = 0f;
                var o = SwordPractice.Swing(who, style, SwordPractice.Technique(who, "oberhau", t), 11, SwordPractice.Victim(pick!), (g, time, _) =>
                {
                    if (time > 3f)
                    {
                        return;
                    }

                    end = time;
                    step(g, offset + (time * slow), label, Inv($"相手 {pick}"));
                });
                lines.Add(Inv($"{label}: incapacitated {o.Incapacitated} tip {o.TipPeak:0.0} impact {o.ImpactSpeed:0.0} self {o.SelfCells}"));
                offset += (end * slow) + 1f;
            }

            return string.Join("; ", lines);
        }, null);
    }

    /// <summary>HybridMatch: swarmer（青）が頭を下げる守りで前へ折れる所（今は直っている）。1/3 のスロー。</summary>
    [TestMethod]
    public void Duck()
    {
        const float slow = 3f;
        Make("duck", "ハイブリッドの試合: swarmer（青）が頭を下げる守り（胸の傾きと骨盤の傾きを表示、1/3 のスロー）", step =>
        {
            var corners = new BoxingCorners("hybrid", s => BotCatalog.Create("swarmer", s), s => BotCatalog.Create("champion", s), VoxelGloves: true);
            using var game = new BoxingGame(FighterTuning.HybridPunch, 1, corners);
            var maxChest = 0f;
            for (var frame = 1; frame <= 180; frame++)
            {
                game.Step(default, 1f / 60f);
                var chest = Tilt(game, game.Cpu.Parts.Chest);
                maxChest = MathF.Max(maxChest, chest);
                step(game, frame / 60f * slow, "赤: champion",
                    Inv($"青: swarmer  胸の傾き {chest:0} 度（最大 {maxChest:0}）骨盤の傾き {Tilt(game, game.Cpu.Parts.Pelvis):0} 度  倒れる線は胸 66 度"));
            }

            return Inv($"max chest {maxChest:0.0}");
        }, game =>
        {
            var c = game.Cpu.ChestPosition;
            var p = game.Player.ChestPosition;
            return (Vector3.Lerp(c, p, 0.5f) + new Vector3(2.3f, 0.2f, 0f), Vector3.Lerp(c, p, 0.5f) + new Vector3(0f, -0.1f, 0f), Vector3.UnitY);
        });
    }
}
