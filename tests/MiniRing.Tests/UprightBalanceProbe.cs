using System.Globalization;
using System.Numerics;
using Aigis.Physics;
using MiniRing.Arena.Campaign;
using MiniRing.Bots;
using MiniRing.Characters;
using MiniRing.Desktop;

namespace MiniRing.Tests;

/// <summary>
/// 調査（2026-11-27）: 細い体・打たれた体の上体が傾いて転ぶ問題を、釣り合いの PD（加速度と重力に逆らう向きへ上体を傾ける）で直す前後で比べる。
/// 測り方は枝 worktree-agent-a0de9bd99533419bb の <c>CampaignBoxerProbe.AloneGrid</c> と同じ（床だけ・1 人・構えの手・20 秒）。
/// </summary>
[TestClass]
[TestCategory("Probe")]
public sealed class UprightBalanceProbe
{
    private const float Dt = 1f / 240f;

    private static float[] Grid(string variable, float[] fallback) =>
        Environment.GetEnvironmentVariable(variable) is { Length: > 0 } v
            ? [.. v.Split(',').Select(s => float.Parse(s, CultureInfo.InvariantCulture))]
            : fallback;

    private static (PhysicsWorld World, Fighter Fighter) Alone(BodyBuild build)
    {
        var world = new PhysicsWorld();
        world.AddStaticBox(new Vector3(0f, -1.408f, 0f), new Vector3(14.08f, 1.408f, 14.08f));
        var tuning = FighterTuning.Default with { Build = build };
        if (Environment.GetEnvironmentVariable("CAPTURE_SHARE") is { Length: > 0 } share)
        {
            tuning = tuning with { Footwork = tuning.Footwork with { CapturePlacementShare = float.Parse(share, CultureInfo.InvariantCulture) } };
        }

        var f = Fighter.Spawn(world, tuning, "f", Vector3.Zero, 0f);
        return (world, f);
    }

    private static FighterCommand Guard(Fighter f, Vector3 move = default, Vector3 step = default) =>
        new(move, Vector3.UnitZ, new HandCommand(AiControl.Guard(Hand.Left, f.ArmReach), false),
            new HandCommand(AiControl.Guard(Hand.Right, f.ArmReach), false), Step: step);

    /// <summary>胸の上向きの傾き [度]（鉛直から）と、前（+Z）へ倒れる成分の符号つき角 [度]。</summary>
    private static (float Tilt, float Pitch) ChestTilt(PhysicsWorld world, Fighter f)
    {
        var up = Vector3.Transform(Vector3.UnitY, world.GetState(f.Parts.Chest).Orientation);
        return (MathF.Acos(Math.Clamp(up.Y, -1f, 1f)) * 180f / MathF.PI, MathF.Atan2(up.Z, up.Y) * 180f / MathF.PI);
    }

    /// <summary>身長の比 × 太さの格子: 1 人で 20 秒立たせ、胸の傾きの平均・最大と倒れたか。</summary>
    [TestMethod]
    public void AloneGrid()
    {
        var fell = 0;
        var cells = 0;
        var sumMean = 0f;
        foreach (var h in Grid("GRID_H", [0.9f, 1.0f, 1.25f]))
        {
            foreach (var m in Grid("GRID_M", [0.6f, 0.75f, 0.9f, 1.0f, 1.2f]))
            {
                var (world, f) = Alone(new BodyBuild(h, 1f, 1f, m));
                using var _ = world;
                var cmd = Guard(f);
                var (sum, max, fellAt, n) = (0f, 0f, -1f, 0);
                for (var i = 0; i < 240 * 20; i++)
                {
                    f.ApplyControl(cmd, Dt);
                    world.Step(Dt);
                    var (tilt, _) = ChestTilt(world, f);
                    if (f.State == FighterState.Standing)
                    {
                        sum += tilt;
                        n++;
                        max = MathF.Max(max, tilt);
                    }

                    if (fellAt < 0f && f.State != FighterState.Standing)
                    {
                        fellAt = i * Dt;
                    }
                }

                cells++;
                fell += fellAt >= 0f ? 1 : 0;
                sumMean += sum / Math.Max(1, n);
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"ALONE H{h:0.00} M{m:0.00} mass {f.TotalMass:0.0} chestMean {sum / Math.Max(1, n):0.0} chestMax {max:0.0} fell {fellAt:0.00} {(fellAt >= 0f ? f.LastFallCause : "")}"));
            }
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"ALONE SUMMARY cells {cells} fell {fell} meanOfMeans {sumMean / cells:0.0}"));
    }

    /// <summary>移動とステップ: 前へ 1.2 秒歩いて止まる・前へステップ・後ろへステップ。胸の前傾（前 +）の時系列の山と、止まった後の戻り。</summary>
    [TestMethod]
    public void MoveAndStep()
    {
        foreach (var m in Grid("GRID_M", [0.75f, 1.0f]))
        {
            foreach (var (name, phase) in new (string, Func<float, (Vector3 Move, Vector3 Step)>)[]
            {
                ("walk", t => (t is >= 1f and < 2.2f ? Vector3.UnitZ : Vector3.Zero, Vector3.Zero)),
                ("back", t => (t is >= 1f and < 2.2f ? -Vector3.UnitZ : Vector3.Zero, Vector3.Zero)),
                ("stepF", t => (Vector3.Zero, t is >= 1f and < 1.1f ? Vector3.UnitZ : Vector3.Zero)),
                ("stepB", t => (Vector3.Zero, t is >= 1f and < 1.1f ? -Vector3.UnitZ : Vector3.Zero)),
            })
            {
                var (world, f) = Alone(new BodyBuild(1f, 1f, 1f, m));
                using var _ = world;
                var (minP, maxP, settle, fellAt) = (0f, 0f, -1f, -1f);
                var trace = new List<string>();
                var end = name.StartsWith("step", StringComparison.Ordinal) ? 1.1f : 2.2f;
                for (var i = 0; i < 240 * 5; i++)
                {
                    var t = i * Dt;
                    var (move, step) = phase(t);
                    f.ApplyControl(Guard(f, move, step), Dt);
                    world.Step(Dt);
                    var (tilt, pitch) = ChestTilt(world, f);
                    if (t >= 0.9f)
                    {
                        minP = MathF.Min(minP, pitch);
                        maxP = MathF.Max(maxP, pitch);
                    }

                    if (t > end && tilt > 8f)
                    {
                        settle = t - end;
                    }

                    if (fellAt < 0f && f.State != FighterState.Standing)
                    {
                        fellAt = t;
                    }

                    if (i % 24 == 0 && t is >= 0.9f and < 3.5f)
                    {
                        trace.Add(string.Create(CultureInfo.InvariantCulture, $"{t:0.0}:{pitch:0}"));
                    }
                }

                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"MOVE M{m:0.00} {name,-5} pitch min {minP:0.0} max {maxP:0.0} settle(>8deg) {settle:0.00}s fell {fellAt:0.00}  {string.Join(' ', trace)}"));
            }
        }
    }

    /// <summary>胸への水平の力積（打たれた）: 強さを振って、最大の傾き・戻るまでの時間・倒れたか。弱い打撃で堪え、強い打撃では倒れる。</summary>
    [TestMethod]
    public void Shove()
    {
        foreach (var m in Grid("GRID_M", [0.75f, 1.0f]))
        {
            foreach (var dir in new[] { -Vector3.UnitZ, Vector3.UnitX })
            {
                foreach (var dv in Grid("SHOVE", [0.5f, 1f, 1.5f, 2f, 3f, 4f]))
                {
                    var (world, f) = Alone(new BodyBuild(1f, 1f, 1f, m));
                    using var _ = world;
                    var cmd = Guard(f);
                    var (max, back, fellAt) = (0f, -1f, -1f);
                    var hitAt = 1.5f;
                    var steps0 = -1;
                    var (against, along) = (0f, 0f);
                    var start = f.PelvisPosition;
                    for (var i = 0; i < 240 * 5; i++)
                    {
                        var t = i * Dt;
                        if (i == (int)(hitAt / Dt))
                        {
                            steps0 = f.FootworkState.Steps;
                            start = f.PelvisPosition;
                            // 胸の速さを dv [m/s] だけ変える力積（胸の質量 × dv）。SHOVE_ALL=1 なら体のすべての部位の速さを dv だけ変える（体ごと押された）。
                            foreach (var id in Environment.GetEnvironmentVariable("SHOVE_ALL") == "1" ? f.AllParts : [f.Parts.Chest])
                            {
                                world.ApplyLinearImpulse(id, dir * (world.GetMass(id) * dv));
                            }
                        }

                        f.ApplyControl(cmd, Dt);
                        world.Step(Dt);
                        var (tilt, _) = ChestTilt(world, f);
                        if (t > hitAt)
                        {
                            max = MathF.Max(max, tilt);
                            var push = Vector2.Dot(f.BalanceAccel, new Vector2(dir.X, dir.Z));
                            (against, along) = (MathF.Min(against, push), MathF.Max(along, push));
                            if (tilt > 10f)
                            {
                                back = t - hitAt;
                            }

                        }

                        if (fellAt < 0f && f.State != FighterState.Standing)
                        {
                            fellAt = t - hitAt;
                        }
                    }

                    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                        $"SHOVE M{m:0.00} dir {(dir.Z < 0f ? "back" : "side"),-4} dv {dv:0.0} maxTilt {max:0.0} back(<10deg) {back:0.00}s steps {f.FootworkState.Steps - steps0} travel {Vector3.Distance(f.PelvisPosition, start) * 100:0}cm lean against {against:0.0} along {along:0.0} fell {fellAt:0.00} {(fellAt >= 0f ? f.LastFallCause : "")} state {f.State}"));
                }
            }
        }
    }

    private static FighterCharacter Made(string type, int heightCm, int? weightKg = null)
    {
        var draft = CharacterMaker.NewDraft(type, heightCm: heightCm) with { Id = "probe-boxer", Name = "新人", Country = "japan", Order = 1, Textbook = "outboxer" };
        var (character, _) = CharacterMaker.ToFile(draft);
        var boxer = new CampaignBoxer(character, CampaignCareer.Empty);
        return (weightKg is { } w ? boxer.ChangeWeight(w) : boxer).Character;
    }

    /// <summary>試合の中で、作ったボクサー（175 cm・75 kg）が何もしなくても倒れないか（相手は打たない・動かない）。</summary>
    [TestMethod]
    public void CampaignIdle()
    {
        foreach (var (name, red) in new[] { ("mine175/75", Made("standard", 175, 75)), ("std145", Made("standard", 145)) })
        {
            foreach (var seed in new[] { 3, 4 })
            {
                const float dt = 1f / 60f;
                var still = BotCatalog.Boxer with { Attacks = false, DefenseRate = 0f, Prediction = 0f, Aggression = 0f };
                var selection = new MatchSelection(MatchMode.Single, red, CharacterRoster.Get("vikram"));
                var corners = selection.Corners with { Cpu = s => new BoxerBot(still, s) };
                var control = LocalVersusControl.RedControl(corners);
                using var game = new BoxingGame(seed: seed, corners: corners);
                var falls = 0;
                var was = FighterState.Standing;
                var minUp = 1f;
                for (var t = 0f; t < 40f; t += dt)
                {
                    var cmd = control.Read(default, game.PlayerSense, dt);
                    game.Step(new BoxingInput(cmd), dt);
                    var f = game.Player;
                    minUp = MathF.Min(minUp, f.Uprightness);
                    if (f.State != was)
                    {
                        falls += was == FighterState.Standing ? 1 : 0;
                        was = f.State;
                    }
                }

                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"CAMPAIGN {name,-11} seed {seed} falls {falls} minUp {minUp:0.00} mass {game.Player.TotalMass:0.0}"));
            }
        }
    }
}
