using System.Numerics;
using Aigis.Physics;
using MiniRing.Bots;

namespace MiniRing.Arena;

/// <summary>
/// 姿勢（立たせる力）のモデルの比べもの（docs/GRAPPLING.md §7）: 同じ場面を、旧の体・CoP の制限・連続の釣り合い（凸包・円）で回し、
/// 立つ・耐える・歩く・蹴る・投げる・打ち合うの各場面で、倒れた回数・浮いた時間（床に何も着いていないのに倒れていない）を数える。
/// </summary>
internal sealed class BalanceLab(int seed, Action<string> log)
{
    private const float Dt = 1f / 240f;
    private const float GameDt = 1f / 60f;

    public static IReadOnlyList<(string Name, FighterTuning Tuning)> Models { get; } =
    [
        ("old", FighterTuning.Default with { SupportFromGround = false }),
        ("cop", FighterTuning.Default with { SupportModel = SupportModel.Cop }),
        ("soft-poly", FighterTuning.Default with { SupportModel = SupportModel.Soft, SupportRegion = SupportRegion.Polygon }),
        ("soft-disk", FighterTuning.Default with { SupportModel = SupportModel.Soft, SupportRegion = SupportRegion.Disk }),
        ("softcop", FighterTuning.Default with { SupportModel = SupportModel.SoftCop }),
    ];

    private static readonly float[] PullWeights = [0.3f, 0.6f, 1f];
    private static readonly KickTechnique[] KickKinds = [KickTechnique.Front, KickTechnique.Middle, KickTechnique.Side];

    public void Run(IReadOnlyList<string> only)
    {
        log("| モデル | 立つ 3 秒: 動いた [cm] | 胸を引く 0.3 / 0.6 / 1.0 体重: 倒れた | 5 方向に歩く: 倒れた | 蹴り続ける 10 秒: 蹴った側が倒れた（前・回し・横） | 支え釣り込み足 2×10 秒: 投げ / 取り倒れ / 浮いた [s] | 試合 2×30 秒: ダウン / 浮いた [s] |");
        log("|---|---|---|---|---|---|---|");
        foreach (var (name, tuning) in Models.Where(m => only.Count == 0 || only.Contains(m.Name)))
        {
            var stand = Stand(tuning);
            var pulls = string.Join(" / ", PullWeights.Select(w => Pull(tuning, w) ? "倒" : "立"));
            var walks = Walks(tuning);
            var kicks = string.Join(" ", KickKinds.Select(k => Kicks(tuning, k)));
            var (throws, toriFalls, throwFloat) = Throws(tuning);
            var (downs, matchFloat) = Match(tuning);
            log($"| {name} | {stand * 100f:0.0} | {pulls} | {walks} | {kicks} | {throws} / {toriFalls} / {throwFloat:0.0} | {downs} / {matchFloat:0.0} |");
        }

        log("");
    }

    private static (PhysicsWorld World, Fighter Fighter, FighterCommand Idle) Spawn(FighterTuning tuning)
    {
        var world = new PhysicsWorld();
        world.AddStaticBox(new Vector3(0f, -0.5f, 0f), new Vector3(5f, 0.5f, 5f));
        var f = Fighter.Spawn(world, tuning, "f", Vector3.Zero, 0f);
        var idle = new FighterCommand(Vector3.Zero, Vector3.UnitZ,
            new HandCommand(AiControl.Guard(Hand.Left, f.ArmReach), false),
            new HandCommand(AiControl.Guard(Hand.Right, f.ArmReach), false));
        return (world, f, idle);
    }

    private static float Stand(FighterTuning tuning)
    {
        var (world, f, idle) = Spawn(tuning);
        using var _ = world;
        var start = f.PelvisPosition;
        for (var i = 0; i < (int)(3f / Dt); i++)
        {
            f.ApplyControl(idle, Dt);
            world.Step(Dt);
        }

        var moved = f.PelvisPosition - start;
        return f.State == FighterState.Standing ? new Vector2(moved.X, moved.Z).Length() : float.NaN;
    }

    private static bool Pull(FighterTuning tuning, float weights)
    {
        var (world, f, idle) = Spawn(tuning);
        using var _ = world;
        var fell = false;
        for (var i = 0; i < (int)(3f / Dt); i++)
        {
            f.ApplyControl(idle, Dt);
            if (i * Dt is > 1f and < 2f)
            {
                world.ApplyLinearImpulse(f.Parts.Chest, Vector3.UnitZ * (weights * f.TotalMass * -world.Gravity.Y * Dt));
            }

            world.Step(Dt);
            fell |= i * Dt > 1f && f.State != FighterState.Standing;
        }

        return fell;
    }

    private static int Walks(FighterTuning tuning)
    {
        var falls = 0;
        foreach (var dir in new[] { Vector3.UnitZ, -Vector3.UnitZ, Vector3.UnitX, -Vector3.UnitX, Vector3.Normalize(new Vector3(1f, 0f, -1f)) })
        {
            var (world, f, idle) = Spawn(tuning);
            using var _ = world;
            var wasUp = true;
            for (var i = 0; i < (int)(4f / Dt); i++)
            {
                f.ApplyControl(i * Dt < 1f ? idle : idle with { MoveWorld = dir }, Dt);
                world.Step(Dt);
                var up = f.State == FighterState.Standing;
                falls += wasUp && !up ? 1 : 0;
                wasUp = up;
            }
        }

        return falls;
    }

    private int Kicks(FighterTuning tuning, KickTechnique kind)
    {
        var style = (BotCatalog.Karate with
        {
            Name = $"kick-{kind}",
            KickRate = 1f,
            KickWeights = new Dictionary<KickTechnique, float> { [kind] = 1f },
            Aggression = 1f,
        }).Validated();
        var corners = new BoxingCorners(style.Name, s => new BoxerBot(style, s), _ => new StandGuard(),
            PlayerWear: MiniRing.Weapons.HandWear.None, CpuWear: MiniRing.Weapons.HandWear.None);
        using var game = new BoxingGame(tuning, seed, corners);
        var falls = 0;
        var wasUp = true;
        for (var t = 0f; t < 10f; t += GameDt)
        {
            game.Step(default, GameDt);
            var up = game.Cpu.State == FighterState.Standing;
            falls += wasUp && !up ? 1 : 0;
            wasUp = up;
        }

        return falls;
    }

    private (int Throws, int ToriFalls, float Float) Throws(FighterTuning tuning)
    {
        var (throws, falls, floating) = (0, 0, 0f);
        for (var b = 0; b < 2; b++)
        {
            var book = GrappleBook.Jujutsu with { Rate = 50f };
            var corners = new BoxingCorners("jujutsu", s => new JujutsuBot(new Closer(), book, s, only: "sasae-tsurikomi-ashi"), _ => new StandGuard(),
                CpuWear: MiniRing.Weapons.HandWear.None);
            using var game = new BoxingGame(tuning, seed + b, corners);
            game.Thrown += (by, _) => throws += by == game.Cpu ? 1 : 0;
            var wasUp = true;
            for (var t = 0f; t < 10f; t += GameDt)
            {
                game.Step(default, GameDt);
                var up = game.Cpu.State == FighterState.Standing;
                falls += wasUp && !up ? 1 : 0;
                wasUp = up;
                floating += Floating(game) * GameDt;
            }
        }

        return (throws, falls, floating);
    }

    private (int Downs, float Float) Match(FighterTuning tuning)
    {
        var (downs, floating) = (0, 0f);
        for (var b = 0; b < 2; b++)
        {
            var corners = new BoxingCorners("swarmer", s => BotCatalog.Create("swarmer", s), s => BotCatalog.Create("champion", s));
            using var game = new BoxingGame(tuning, seed + b, corners);
            var wasUp = (true, true);
            for (var t = 0f; t < 30f; t += GameDt)
            {
                game.Step(default, GameDt);
                var up = (game.Player.State == FighterState.Standing, game.Cpu.State == FighterState.Standing);
                downs += (wasUp.Item1 && !up.Item1 ? 1 : 0) + (wasUp.Item2 && !up.Item2 ? 1 : 0);
                wasUp = up;
                floating += Floating(game) * GameDt;
            }
        }

        return (downs, floating);
    }

    /// <summary>浮いている人数: 床に何も着いていないのに、骨盤が立つ高さの半分より上にいる（跳ぶ技は無いので、浮いて見える）。</summary>
    private static int Floating(BoxingGame game)
    {
        var n = 0;
        foreach (var f in new[] { game.Player, game.Cpu })
        {
            var ground = BoxingGame.RingSurfaceHeight;
            if (f.SupportHull.Count == 0 && f.PelvisPosition.Y - ground > 0.5f * f.HoverHeight)
            {
                n++;
            }
        }

        return n;
    }
}
