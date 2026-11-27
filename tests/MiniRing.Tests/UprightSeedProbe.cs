using MiniRing.Arena;
using MiniRing.Bots;
using MiniRing.Characters;
using MiniRing.Motion;

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

    [TestMethod]
    public void GetUp()
    {
        var (risen, down, n) = (0, 0, 0);
        foreach (var seed in Enumerable.Range(1, 18))
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
                Console.WriteLine($"GETUP seed {seed}: down {downAt:0.00} up {upAt:0.00} cons {cpu.Consciousness:0.00}");
            }
        }

        Console.WriteLine($"GETUP SUMMARY down {down}/{n} risen {risen}/{n}");
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
