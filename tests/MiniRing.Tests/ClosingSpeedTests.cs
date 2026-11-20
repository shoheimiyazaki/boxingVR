using System.Numerics;

namespace MiniRing.Tests;

/// <summary>
/// 打撃の速さは、打つ点と打たれる点の相対速度の、当たる向きの成分（2026-11-17）。前は打った側の拳の速さそのものだったので、
/// 押されて下がる相手に拳が付いたまま前へ歩くだけで約 1 m/s の打撃に数えられ（前へ出ながらの連打の当たりの 5〜9 割）、
/// 逆に前へ出てくる相手へのカウンターには、相手が近づく速さが足されなかった（RushPhysicsProbe）。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class ClosingSpeedTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void ClosingSpeed_IsTheRelativeVelocityAlongTheStrike()
    {
        var into = Vector3.UnitZ;
        Assert.AreEqual(2f, PunchMechanics.ClosingSpeed(new Vector3(0f, 0f, 2f), Vector3.Zero, into), 1e-6f, "止まった相手には拳の速さそのもの");
        Assert.AreEqual(0f, PunchMechanics.ClosingSpeed(new Vector3(0f, 0f, 1f), new Vector3(0f, 0f, 1f), into), 1e-6f, "同じ速さで下がる相手には当たる速さが無い");
        Assert.AreEqual(3f, PunchMechanics.ClosingSpeed(new Vector3(0f, 0f, 2f), new Vector3(0f, 0f, -1f), into), 1e-6f, "近づいてくる相手には、近づく速さが足される");
        Assert.AreEqual(0f, PunchMechanics.ClosingSpeed(new Vector3(0f, 0f, 1f), new Vector3(0f, 0f, 3f), into), 1e-6f, "離れていく速さの方が大きければ 0（負にしない）");
        Assert.AreEqual(1f, PunchMechanics.ClosingSpeed(new Vector3(5f, 0f, 1f), Vector3.Zero, into), 1e-6f, "当たる向きの成分だけ");
    }

    /// <summary>
    /// 構えたまま前へ歩き続け、ガードを固めた相手に拳を押し付けるだけでは、押している間は打撃に数えない（押す拳と押される相手が一緒に動く）。
    /// 再発防止: 前は拳の速さ（歩く速さ＋押し合いの揺れ）だけで数え、押し続けると 0.18 秒ごとに当たりが出た。
    /// 歩いて最初にガードへぶつかる所（始めの 2 秒）は、歩く速さで閉じる本当の衝突なので数えない範囲から外す
    /// （構えた腕に体の質量が乗る近道は CLAUDE.md §8「拳に乗る体の質量」）。
    /// </summary>
    [TestMethod]
    public void WalkingIntoAGuardWithoutPunching_ScoresNoHitsWhilePushing()
    {
        var corners = BoxingCorners.Default with { Player = _ => new IntentControl(), Cpu = _ => new IntentControl() };
        using var game = new BoxingGame(seed: 5, corners: corners);
        var player = (IntentControl)game.PlayerControl!;
        var cpu = (IntentControl)game.CpuControl;
        const float dt = 1f / 60f;
        var hits = 0;
        var damage = 0f;
        for (var i = 0; i < 8 * 60; i++)
        {
            player.Input = new FighterIntent(new Vector2(0f, 1f));
            cpu.Input = new FighterIntent(Vector2.Zero, Evade: true);
            game.Step(new BoxingInput(default), dt);
            foreach (var hit in game.RecentHits.Where(h => h.Attacker == game.Player))
            {
                TestContext.WriteLine($"  t {i * dt:0.00} {hit.Zone} {hit.Hand} speed {hit.Speed:0.00} m/s, {hit.ImpactMass:0.0} kg, {hit.Damage:0.0} 点");
                if (i * dt >= 2f)
                {
                    hits++;
                    damage += hit.Damage;
                }
            }
        }

        TestContext.WriteLine($"押している間（2 秒から）の当たり {hits} 回、{damage:0.0} 点");
        Assert.AreEqual(0, hits, "拳を出さずに押し付け続けるだけでは打撃に数えない");
    }
}
