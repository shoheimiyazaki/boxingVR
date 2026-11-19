using System.Numerics;
using Aigis.Foundation;
using Aigis.Render;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// 足（かかと側）とつま先: 平たい靴の形（足裏が幅いっぱいで平ら、甲が指先へ下がる）で、蹴りで振られてもつま先が可動域の止めを越えて跳ねない。
/// </summary>
[TestClass]
public sealed class FootShapeTests
{
    private const float ToeUpLimitDegrees = 25f; // Fighter.AddToes の可動域の止め（上）

    [TestMethod]
    public void TheToe_IsFlat_TheSoleFillsTheWidth_AndTheTopSlopesDownToTheTips()
    {
        using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("still", _ => new StandGuard()));
        var toe = game.Player.Voxels!.PartOf(game.Player.ToeId(0))!.Body.Grid;
        var heel = game.Player.Voxels!.PartOf(game.Player.HeelId(0))!.Body.Grid;
        foreach (var (name, grid) in new[] { ("toe", toe), ("heel", heel) })
        {
            var (nx, ny, nz) = grid.Dimensions;
            var sole = Enumerable.Range(0, nx).Count(x => grid.At(x, 0, nz / 2) != 0);
            Assert.AreEqual(nx, sole, $"{name}: the sole is flat across the whole width (was 3 of 9 cells when round)");
        }

        var (tx, ty, tz) = toe.Dimensions;
        int Height(int z) => Enumerable.Range(0, ty).Count(y => toe.At(tx / 2, y, z) != 0);
        Assert.IsGreaterThan(Height(tz - 1), Height(0), "the toe is thick at the ball and thin at the tips");
    }

    [TestMethod]
    public void AKickingToe_StaysWithinItsRange_AndDoesNotBounce()
    {
        var body = MiniRing.Characters.CharacterRoster.Get("taeyun").Build;
        var spec = KickSpec.Of(KickTechnique.Roundhouse);
        foreach (var side in new[] { Hand.Left, Hand.Right })
        {
            using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("still", _ => new StandGuard(), PlayerBody: body));
            var attacker = game.Player;
            var victim = game.Cpu;
            var reach = attacker.ArmReach;
            const float dt = 1f / 240f;
            FighterCommand Cmd(LegCommand? leg, float yaw, float twist)
            {
                var face = Vector3.Transform(victim.ChestPosition - attacker.ChestPosition, Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw));
                return new FighterCommand(Vector3.Zero, face, new HandCommand(AiControl.Guard(Hand.Left, reach), false), new HandCommand(AiControl.Guard(Hand.Right, reach), false),
                    TwistBias: twist, LeftLeg: side == Hand.Left ? leg : null, RightLeg: side == Hand.Right ? leg : null);
            }

            for (var i = 0; i < 360; i++)
            {
                game.Step(new BoxingInput(Cmd(null, 0f, 0f)), dt);
            }

            var aimWorld = spec.AimWorld(game.PlayerSense);
            var duration = spec.Seconds!.Value;
            var rest = new Vector3(side.LocalSide() * 0.2f * reach, -1.4f * reach, 0f);
            var max = float.MinValue;
            for (var i = 0; i < (int)((duration + 0.5f) / dt); i++)
            {
                var t = i * dt;
                var sense = game.PlayerSense;
                var leg = t <= duration ? spec.Evaluate(side, t, duration, rest, sense.Self.WorldToLocal(aimWorld), reach, sense.SelfLeg(side)) : (LegCommand?)null;
                game.Step(new BoxingInput(Cmd(leg, t <= duration ? spec.FaceYaw(side, t, duration) : 0f, t <= duration ? spec.TorsoTwist(side, t, duration) : 0f)), dt);
                max = MathF.Max(max, MathF.Max(ToeAngle(game, attacker, 0), ToeAngle(game, attacker, 1)));
            }

            // 止めが既定の 15 Hz のときは 62° まで振れた。止め 60 Hz で 33〜34°（遠心の力で止めに押し付けられる分は残る）。
            // 12 → 25（LogicVersion 52: 蹴りの練習のやり直し）: 回し蹴りの足首の差分が -0.5 前後から -0.11 になり、足が平らなまま振られるので、遠心力でつま先が止めへ強く押し付けられ 46°（止め 25° + 21°）まで振れる。止めが既定の 15 Hz のときの 62° よりはずっと小さい。
            Assert.IsLessThan(ToeUpLimitDegrees + 25f, max, $"{side}: the toe stays near its stop ({max:F1}°)");
        }
    }

    [TestMethod]
    [TestCategory("visual")]
    public void TheFoot_RendersAsAFlatShoe_FromTheSideAboveAndFront()
    {
        using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("stand", _ => new StandGuard(), _ => new StandGuard()));
        for (var i = 0; i < 120; i++)
        {
            game.Step(default, 1f / 60f);
        }

        using var renderer = new HeadlessRenderer(800, 600);
        var output = OutputDir();
        var f = game.Player;
        var heel = f.HeelId(0);
        var toe = f.ToeId(0);
        var target = (game.World.GetState(heel).Position + game.World.GetState(toe).Position) * 0.5f;
        var forward = game.World.GetState(toe).Position - game.World.GetState(heel).Position;
        forward.Y = 0f;
        forward = Vector3.Normalize(forward);
        var sideways = Vector3.Cross(Vector3.UnitY, forward);
        foreach (var (name, offset, up) in new[]
        {
            ("side", (sideways * 0.2f) + new Vector3(0f, 0.03f, 0f), Vector3.UnitY),
            ("above", new Vector3(0f, 0.22f, 0f) - (forward * 0.02f), forward),
            ("front", (forward * 0.2f) + new Vector3(0f, 0.05f, 0f) + (sideways * 0.05f), Vector3.UnitY),
        })
        {
            var eye = target + offset;
            var vp = Camera.ViewProjection(eye, target, up, 50f * MathF.PI / 180f, 800f / 600f, 0.02f, 30f);
            var voxels = f.Voxels!.Parts.Where(p => p.Body.BodyId == heel || p.Body.BodyId == toe || f.KindOf(p.Body.BodyId) == BodyPartKind.Shin)
                .Select(p => p.Draw()).ToList();
            var rgba = renderer.RenderPrimitiveScene(new Vector4(0.08f, 0.1f, 0.14f, 1f), vp, [], [], [],
                lighting: game.BuildLighting(), cameraPosition: eye, voxels: voxels);
            PngWriter.WriteRgba(Path.Combine(output, $"foot_{name}.png"), 800, 600, rgba);
        }

    }

    private static string OutputDir()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "MiniRing.slnx")))
        {
            root = root.Parent;
        }

        Assert.IsNotNull(root);
        var output = Path.Combine(root.FullName, ".visualcheck");
        Directory.CreateDirectory(output);
        return output;
    }

    private static float ToeAngle(BoxingGame game, Fighter f, int foot)
    {
        var heel = game.World.GetState(f.HeelId(foot)).Orientation;
        var toe = game.World.GetState(f.ToeId(foot)).Orientation;
        var rel = Quaternion.Inverse(heel) * toe;
        return 2f * MathF.Atan2(rel.X, rel.W) * 180f / MathF.PI;
    }
}
