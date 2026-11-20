using System.Numerics;
using MiniRing.Arena;
using MiniRing.Characters;

namespace MiniRing.Tests;

/// <summary>
/// 調査用（2026-11-19）: 試合の中で拳が肩の後ろへ大きく回る（腕が逆向きに伸びて見える）ことがどれだけ起きるか、そのとき目標はどこにあったか。
/// 「後ろ」= 体の向き（<see cref="Fighter.Frame"/>）で、肩より拳が後ろにある量（腕の長さ単位）。
/// </summary>
[TestClass]
[TestCategory("Probe")]
public sealed class ArmBehindProbe
{
    private static float Back(Fighter f, Vector3 point, Hand hand)
    {
        var frame = f.Frame;
        return Vector3.Dot(f.ShoulderPosition(hand) - point, frame.Forward) / f.ArmReach;
    }

    private static float Up(Fighter f, Vector3 point, Hand hand) => (point.Y - f.ShoulderPosition(hand).Y) / f.ArmReach;

    [TestMethod]
    public void Probe_Seed7_MarcusVsDmitri_Trace()
    {
        var red = CharacterRoster.Get("marcus");
        var blue = CharacterRoster.Get("dmitri");
        MaaiLab.RunBout(red, blue, 7, 13.43f, referee: true, onStep: (game, now) =>
        {
            if (now < 10.91f || now > 13.09f || ((int)MathF.Round(now / MaaiLab.StepDt) % 6) != 0)
            {
                return;
            }

            var f = game.Cpu;
            foreach (var hand in new[] { Hand.Left, Hand.Right })
            {
                var glove = f.GloveCenter(hand);
                var target = f.HandTargetWorld(hand);
                Console.WriteLine($"t={now:F3} {hand,-5} gloveBack={Back(f, glove, hand),6:F2} gloveUp={Up(f, glove, hand),6:F2} targetBack={Back(f, target, hand),6:F2} targetUp={Up(f, target, hand),6:F2} hits={game.RecentHits.Count} state={f.State}");
            }
        });
    }

    [TestMethod]
    public void Probe_HowOften_TheGloveGoesBehind()
    {
        var ids = new[] { "marcus", "dmitri", "ramon", "yoandry", "vikram", "tsuyoshi", "albert", "gaston" };
        var pairs = ids.SelectMany((a, i) => ids.Skip(i + 1).Select(b => (a, b))).ToList();
        var results = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.ForEach(pairs, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, pair =>
        {
            var (a, b) = pair;
            var events = new List<string>();
            var inEvent = new bool[4];
            MaaiLab.RunBout(CharacterRoster.Get(a), CharacterRoster.Get(b), 11, 67.14f, referee: true, onStep: (game, now) =>
            {
                var k = 0;
                foreach (var f in new[] { game.Player, game.Cpu })
                {
                    foreach (var hand in new[] { Hand.Left, Hand.Right })
                    {
                        var back = Back(f, f.GloveCenter(hand), hand);
                        var on = f.State == FighterState.Standing && back > 0.35f;
                        if (on && !inEvent[k])
                        {
                            var tb = Back(f, f.HandTargetWorld(hand), hand);
                            var other = f == game.Player ? game.Cpu : game.Player;
                            var toOther = other.ChestPosition - f.ChestPosition;
                            toOther.Y = 0f;
                            var dist = toOther.Length();
                            var yawErr = MathF.Atan2(Vector3.Dot(Vector3.Cross(f.Frame.Forward, Vector3.Normalize(toOther)), Vector3.UnitY), Vector3.Dot(f.Frame.Forward, Vector3.Normalize(toOther))) * 180f / MathF.PI;
                            var cmd = f.LastCommand.For(hand).TargetLocal / f.ArmReach;
                            var face = f.LastCommand.FaceDirection;
                            var faceErr = face.LengthSquared() > 1e-6f ? MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(new Vector3(face.X, 0f, face.Z)), f.Frame.Forward), -1f, 1f)) * 180f / MathF.PI : float.NaN;
                            var bot = (f == game.Player ? game.PlayerControl : game.CpuControl) is MiniRing.Bots.BoxerBot bb ? (bb.ProbeState.Attacking ? "atk" : bb.ProbeState.Defending ? "def" : "-") : "?";
                            events.Add($"{(f == game.Player ? a : b)} {hand} t={now:F2} gloveBack={back:F2} up={Up(f, f.GloveCenter(hand), hand):F2} targetBack={tb:F2} cmd=({cmd.X:F2},{cmd.Y:F2},{cmd.Z:F2}) yawToOpp={yawErr:F0} faceVsFrame={faceErr:F0} dist={dist:F2} twist={f.LastCommand.TwistBias:F2} bot={bot} cmdBehind={cmd.Z < 0f} lowHand={cmd.Z >= 0f && Up(f, f.GloveCenter(hand), hand) < -0.5f}");
                        }

                        inEvent[k++] = on;
                    }
                }
            });
            results.Add($"{a} vs {b}: {events.Count}" + string.Concat(events.Select(e => "\n   " + e)));
        });
        foreach (var r in results.Order())
        {
            Console.WriteLine(r);
        }

        var all = results.SelectMany(r => r.Split('\n').Skip(1)).ToList();
        var commanded = all.Count(e => e.Contains("cmdBehind=True"));
        var low = all.Count(e => e.Contains("lowHand=True"));
        Console.WriteLine($"SUMMARY events={all.Count} commandedBehind={commanded} lowHand={low} other={all.Count - commanded - low}");
    }
}
