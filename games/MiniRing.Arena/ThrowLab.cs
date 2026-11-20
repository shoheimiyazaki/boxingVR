using System.Globalization;
using System.Numerics;
using MiniRing.Bots;

namespace MiniRing.Arena;

/// <summary>
/// 組み技のドリル（docs/GRAPPLING.md）: 柔術家が 1 つの技だけを、決まった受け（構えて立つだけ・名前のボット）にかけ続け、
/// 技ごとに「かけた回数・掴めなかった回数・投げた（掴んでいた相手が倒れた）回数・取りが自分で倒れた回数・受けの肘の靭帯の残り・
/// 肘にかかった力の最大（靭帯の降伏に対する比）」を数える。投げも関節技も、結果は物理と材料から出る（技の名前で分けない）。
/// </summary>
internal sealed class ThrowLab(int bouts, float seconds, int seed, Action<string> log, bool trace = false)
{
    private const float StepDt = 1f / 60f;

    public void Run(IReadOnlyList<string> techniques, IReadOnlyList<string> victims)
    {
        var book = GrappleBook.Jujutsu;
        log($"## 組み技のドリル（{DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}、PhysicsVersion {Fighter.PhysicsVersion}）");
        log("");
        log($"取り = 柔術家（組める間合いへ詰めて 1 つの技だけを繰り返す）、受け = 下の表の相手。{bouts} 本 × {seconds:0} 秒、種 {seed}〜。");
        log("`投げ` = 取りに掴まれていた（離されて 1.34 秒以内を含む）受けが倒れた回数（BoxingGame.Thrown）。`取り倒れ` = 取り自身が倒れた回数。");
        log("`肘の荷重` = 受けの肘の過伸展の力の最大 / 靭帯の降伏。`靭帯` = 受けの肘の靭帯の残りの最小（1 = 無傷）。");
        log("");
        log("| 技 | 受け | かけた | 掴めず | 投げ | 投げ/かけた | 取り倒れ | 肘の荷重 | 靭帯 | 受けの崩れの最大 [cm] | 受けが床に当たった頭 / 最大の速さ [m/s] / 最大の頭のねじれ [rad/s] | 受けの床の衝撃の合計 [J] | 受けの意識の最小 |");
        log("|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var name in techniques)
        {
            foreach (var victim in victims)
            {
                var (attempts, missed, throws, selfFalls, load, ligament, margin) = (0, 0, 0, 0, 0f, 1f, float.NegativeInfinity);
                var (heads, headSpeed, headTwist, canvasEnergy, minAware) = (0, 0f, 0f, 0f, 1f);
                for (var b = 0; b < bouts; b++)
                {
                    JujutsuBot? bot = null;
                    var corners = new BoxingCorners($"jujutsu-{name}",
                        s => bot = new JujutsuBot(new Closer(), book with { Rate = 29.8f }, s, only: name),
                        s => Victim(victim, s), CpuWear: MiniRing.Weapons.HandWear.None);
                    using var game = new BoxingGame(seed: seed + b, corners: corners);
                    var thrown = 0;
                    var toriDown = 0;
                    game.Thrown += (by, _) => thrown += by == game.Cpu ? 1 : 0;
                    game.Landed += impact =>
                    {
                        if (impact.Fighter != game.Player)
                        {
                            return;
                        }

                        canvasEnergy += impact.Energy;
                        if (impact.Part == BodyPartKind.Head)
                        {
                            heads++;
                            headSpeed = MathF.Max(headSpeed, impact.Speed);
                            headTwist = MathF.Max(headTwist, impact.HeadTwist);
                        }
                    };
                    var toriWasUp = true;
                    for (var t = 0f; t < seconds; t += StepDt)
                    {
                        game.Step(default, StepDt);
                        var uke = game.Player;
                        minAware = MathF.Min(minAware, uke.Consciousness);
                        foreach (var hand in (ReadOnlySpan<Hand>)[Hand.Left, Hand.Right])
                        {
                            load = MathF.Max(load, uke.ElbowLockTorque(hand) / MathF.Max(1e-6f, uke.ElbowLigamentYield));
                            ligament = MathF.Min(ligament, uke.ElbowLigament(hand));
                        }

                        if (uke.State == FighterState.Standing && float.IsFinite(uke.SupportMargin))
                        {
                            margin = MathF.Max(margin, uke.SupportMargin);
                        }

                        if (trace && ((int)MathF.Round(t / StepDt)) % 6 == 0)
                        {
                            var tori = game.Cpu;
                            var d = Vector3.Distance(tori.ChestPosition, uke.ChestPosition) / tori.ArmReach;
                            var feetY = MathF.Min(tori.AnklePosition(Hand.Left).Y, tori.AnklePosition(Hand.Right).Y);
                            Console.WriteLine($"t {t:0.00} d {d:0.00} tori {tori.State} up {tori.Uprightness:0.00} m {tori.SupportMargin * 100:0.0} pel {tori.PelvisPosition.Y * 100:0.0} feet {feetY * 100:0.0} hull {tori.SupportHull.Count} hold {tori.GripForce(Hand.Left):0}/{tori.GripForce(Hand.Right):0}N | uke {uke.State} up {uke.Uprightness:0.00} m {uke.SupportMargin * 100:0.0} | {bot?.Current?.Name ?? bot?.Waiting ?? "-"} gL {Vector3.Distance(tori.GloveCenter(Hand.Left), uke.WristPosition(Hand.Right)) / tori.ArmReach:0.00} gR {Vector3.Distance(tori.GloveCenter(Hand.Right), uke.ElbowPosition(Hand.Right)) / tori.ArmReach:0.00} elbow {uke.ElbowLockTorque(Hand.Right) / uke.ElbowLigamentYield:0.00}/{uke.ElbowLockTorque(Hand.Left) / uke.ElbowLigamentYield:0.00}");
                        }

                        var toriUp = game.Cpu.State == FighterState.Standing;
                        toriDown += toriWasUp && !toriUp ? 1 : 0;
                        toriWasUp = toriUp;
                    }

                    attempts += bot?.Attempts.GetValueOrDefault(name) ?? 0;
                    missed += bot?.Missed.GetValueOrDefault(name) ?? 0;
                    throws += thrown;
                    selfFalls += toriDown;
                }

                var rate = attempts - missed > 0 ? (float)throws / (attempts - missed) : 0f;
                log($"| {name} | {victim} | {attempts} | {missed} | {throws} | {rate:0.00} | {selfFalls} | {load:0.00} | {ligament:0.00} | {(float.IsFinite(margin) ? margin * 100f : 0f):0.0} | {heads} / {headSpeed:0.00} / {headTwist:0} | {canvasEnergy:0.00} | {minAware:0.00} |");
            }
        }

        log("");
        log("`投げ/かけた` は掴めた回数あたり。");
        log("");
    }

    internal static IFighterControl Victim(string name, int seed) => name switch
    {
        "stand" => new StandGuard(),
        _ => BotCatalog.Create(name, seed),
    };
}
