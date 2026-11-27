using System.Numerics;
using Aigis.Physics;

namespace MiniRing.Tests;

/// <summary>
/// 速く動くと足だけ取り残される（脚の関節の位置の拘束が伸びて足が脛から離れる）不具合が残っていないかの調べ（2026-11-26、ユーザーの依頼「以前の物理で早く動くと足だけ残る不具合がありました」）。
/// 意図操作（<see cref="IntentControl"/>）でステップ・ステップの連続・全速の歩き・切り返しを入れ、骨盤の水平の速さの区間ごとに、股・膝・足首の開き（<see cref="Fighter.JointGap"/>）の最大を出す。
/// </summary>
[TestClass]
[TestCategory("Probe")]
public sealed class FastMoveGapProbe
{
    private const float Dt = 1f / 60f;

    private static readonly HumanoidRagdoll.RagdollJoint[] Legs =
    [
        HumanoidRagdoll.RagdollJoint.LeftHip, HumanoidRagdoll.RagdollJoint.RightHip, HumanoidRagdoll.RagdollJoint.LeftKnee,
        HumanoidRagdoll.RagdollJoint.RightKnee, HumanoidRagdoll.RagdollJoint.LeftAnkle, HumanoidRagdoll.RagdollJoint.RightAnkle,
    ];

    [TestMethod]
    public void LegJointGaps_ByPelvisSpeed()
    {
        var bins = new[] { 0.5f, 1f, 1.5f, 2f, 3f, 4f, 99f };
        var maxGap = new float[bins.Length];
        var maxJoint = new string[bins.Length];
        var count = new int[bins.Length];
        var topSpeed = 0f;
        var worst = (Gap: 0f, Joint: "", Time: 0f, Speed: 0f, Scene: "");
        var dirs = new (string Name, Vector2 D)[] { ("fwd", new(0, 1)), ("back", new(0, -1)), ("left", new(-1, 0)), ("right", new(1, 0)) };
        foreach (var seed in new[] { 7, 8 })
        {
            foreach (var (name, d) in dirs)
            {
                using var game = new BoxingGame(seed: seed, corners: BoxingCorners.Default with { ExternalBlue = true, Ring = new RingLayout(10f, 4f), Referee = false, BreakActions = false });
                var control = new IntentControl();
                var t = 0f;
                var last = game.Player.PelvisPosition;
                void Hold(FighterIntent intent, float seconds)
                {
                    for (var i = 0; i < (int)MathF.Round(seconds / Dt); i++)
                    {
                        control.Input = intent;
                        var command = control.Read(game.PlayerSense, Dt);
                        var cpu = game.Cpu;
                        var idle = new FighterCommand(Vector3.Zero, game.Player.PelvisPosition - cpu.PelvisPosition,
                            new HandCommand(AiControl.Guard(Hand.Left, cpu.ArmReach), false), new HandCommand(AiControl.Guard(Hand.Right, cpu.ArmReach), false));
                        game.Step(new BoxingInput(command, Blue: idle), Dt);
                        t += Dt;
                        var p = game.Player.PelvisPosition;
                        var v = new Vector2(p.X - last.X, p.Z - last.Z).Length() / Dt;
                        last = p;
                        if (game.Player.State != FighterState.Standing || t < 1f)
                        {
                            continue;
                        }

                        topSpeed = MathF.Max(topSpeed, v);
                        var b = Array.FindIndex(bins, x => v < x);
                        count[b]++;
                        foreach (var j in Legs)
                        {
                            var g = game.Player.JointGap(j) ?? 0f;
                            if (g > maxGap[b])
                            {
                                maxGap[b] = g;
                                maxJoint[b] = j.ToString();
                            }

                            if (g > worst.Gap)
                            {
                                worst = (g, j.ToString(), t, v, $"{name} seed {seed}");
                            }
                        }
                    }
                }

                Hold(default, 2f);
                for (var k = 0; k < 4; k++)
                {
                    Hold(new FighterIntent(d, Step: true), 2 * Dt); // ステップ（脚のバネで一気に）
                    Hold(new FighterIntent(d), 0.25f);
                }

                Hold(new FighterIntent(d), 1.2f); // 全速の歩き
                Hold(new FighterIntent(-d, Step: true), 2 * Dt); // 逆へステップで切り返す
                Hold(new FighterIntent(-d), 0.8f);
                Hold(new FighterIntent(d, Step: true), 2 * Dt);
                Hold(default, 0.8f);
            }
        }

        Console.WriteLine($"骨盤の水平の速さの最大 {topSpeed:0.00} m/s");
        var lo = 0f;
        for (var i = 0; i < bins.Length; i++)
        {
            if (count[i] > 0)
            {
                Console.WriteLine($"{lo:0.0}-{bins[i]:0.0} m/s  frames {count[i],5}  脚の関節の最大の開き {maxGap[i] * 100f:0.0} cm ({maxJoint[i]})");
            }

            lo = bins[i];
        }

        Console.WriteLine($"最悪 {worst.Gap * 100f:0.0} cm {worst.Joint} {worst.Scene} t={worst.Time:0.00} 速さ {worst.Speed:0.00} m/s");
    }
}
