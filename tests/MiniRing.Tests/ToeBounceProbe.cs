using System.Numerics;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>調査用: 蹴りの間と後に、つま先がかかと側に対して蝶番の軸まわりにどれだけ揺れるか。</summary>
[TestClass]
public sealed class ToeBounceProbe
{
    [TestMethod]
    public void ToeAngle_DuringAndAfterARoundhouse()
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
            var series = new List<(float T, float A0, float A1)>();
            for (var i = 0; i < (int)((duration + 0.8f) / dt); i++)
            {
                var t = i * dt;
                var sense = game.PlayerSense;
                var leg = t <= duration ? spec.Evaluate(side, t, duration, rest, sense.Self.WorldToLocal(aimWorld), reach, sense.SelfLeg(side)) : (LegCommand?)null;
                game.Step(new BoxingInput(Cmd(leg, t <= duration ? spec.FaceYaw(side, t, duration) : 0f, t <= duration ? spec.TorsoTwist(side, t, duration) : 0f)), dt);
                series.Add((t, ToeAngle(game, attacker, 0), ToeAngle(game, attacker, 1)));
            }

            for (var foot = 0; foot < 2; foot++)
            {
                var a = series.Select(s => foot == 0 ? s.A0 : s.A1).ToArray();
                // 揺れ: 角速度の向きが 3° 以上の振れで反転した回数と、2 階差分の大きさ
                var reversals = 0;
                var lastExtreme = a[0];
                var dir = 0;
                for (var i = 1; i < a.Length; i++)
                {
                    var d = MathF.Sign(a[i] - a[i - 1]);
                    if (d != 0 && d != dir && MathF.Abs(a[i - 1] - lastExtreme) > 3f)
                    {
                        reversals++;
                        lastExtreme = a[i - 1];
                        dir = d;
                    }
                    else if (dir == 0)
                    {
                        dir = d;
                    }
                }

                Console.WriteLine($"{side} kick, foot {foot}: min {a.Min():F1} max {a.Max():F1} deg, reversals>3deg {reversals}");
                Console.WriteLine("  " + string.Join(" ", a.Where((_, i) => i % 8 == 0).Select(x => x.ToString("F0", System.Globalization.CultureInfo.InvariantCulture))));
            }
        }
    }

    [TestMethod]
    public void FootCells_SideAndTopSlices()
    {
        Console.WriteLine("shipped overrides: " + string.Join(",", MiniRing.Voxels.BoxerVoxelOverrides.Shipped?.Kinds ?? []));
        using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("still", _ => new StandGuard()));
        foreach (var id in new[] { game.Player.HeelId(0), game.Player.ToeId(0) })
        {
            var grid = game.Player.Voxels!.PartOf(id)!.Body.Grid;
            var (nx, ny, nz) = grid.Dimensions;
            Console.WriteLine($"part {id} dims {nx}x{ny}x{nz}");
            Console.WriteLine("side (rows = y top→bottom, cols = z back→front), middle x:");
            for (var y = ny - 1; y >= 0; y--)
            {
                Console.WriteLine("  " + string.Concat(Enumerable.Range(0, nz).Select(z => grid.At(nx / 2, y, z) == 0 ? '.' : '#')));
            }

            Console.WriteLine("top (rows = z front→back, cols = x), bottom y:");
            for (var z = nz - 1; z >= 0; z--)
            {
                Console.WriteLine("  " + string.Concat(Enumerable.Range(0, nx).Select(x => grid.At(x, 0, z) == 0 ? '.' : '#')));
            }

            Console.WriteLine("front (rows = y, cols = x), middle z:");
            for (var y = ny - 1; y >= 0; y--)
            {
                Console.WriteLine("  " + string.Concat(Enumerable.Range(0, nx).Select(x => grid.At(x, y, nz / 2) == 0 ? '.' : '#')));
            }
        }
    }

    /// <summary>組み込みの boxer.vox の足とつま先だけを、今の形（BoxerShapes.Heel・Toe）から作り直した .vox を書く（voxcheck --install で組み込む）。</summary>
    [TestMethod]
    public void WriteVoxWithRegeneratedToe()
    {
        var tuning = new FighterTuning();
        var current = MiniRing.Voxels.BoxerVoxelModel.Parts(tuning, MiniRing.Voxels.BoxerVoxelOverrides.Shipped);
        var generated = MiniRing.Voxels.BoxerVoxelModel.Parts(tuning, null);
        var mixed = current.ToDictionary(p => p.Key, p => p.Key is BodyPartKind.Toe or BodyPartKind.Foot ? generated[p.Key] : p.Value);
        var path = Environment.GetEnvironmentVariable("TOE_VOX_OUT") ?? Path.Combine(Path.GetTempPath(), "boxer_toe.vox");
        using (var stream = File.Create(path))
        {
            MiniRing.Voxels.MagicaVox.Write(stream, MiniRing.Voxels.BoxerVoxelModel.ToVox(mixed));
        }

        Console.WriteLine("wrote " + path);
    }

    private static float ToeAngle(BoxingGame game, Fighter f, int foot)
    {
        var heel = game.World.GetState(f.HeelId(foot)).Orientation;
        var toe = game.World.GetState(f.ToeId(foot)).Orientation;
        var rel = Quaternion.Inverse(heel) * toe;
        return 2f * MathF.Atan2(rel.X, rel.W) * 180f / MathF.PI;
    }
}
