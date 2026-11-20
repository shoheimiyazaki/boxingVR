using System.Numerics;
using MiniRing.Characters;

namespace MiniRing.Tests;

/// <summary>
/// 生成で握らせた武器が握った本人の体に重なって置かれても、その重なりで自分を削らない（一度離れるまで当たらない）。
/// 赤 daichi（六尺棒）対 青 sean が、テレビ中継の助走（生成直後の 1 秒）の間に自分の棒で首を折り、開始 0 秒で決着していた（2026-11-20）。
/// </summary>
[TestClass]
public sealed class SpawnWeaponOverlapTests
{
    /// <summary>助走の間に、構えへ移る刃が自分の体をかすめて削ってよいセルの数（生成の重なりを当てていたときの 100〜280 よりずっと小さい）。</summary>
    private const int MaxGrazeCells = 10;

    [TestMethod]
    public void OrientedBoxesOverlapOnlyWhenTheirShadowsMeetOnEveryAxis()
    {
        var half = new Vector3(0.5f);
        Assert.IsTrue(OrientedBox.Overlaps(Vector3.Zero, Quaternion.Identity, half, new Vector3(0.9f, 0f, 0f), Quaternion.Identity, half), "0.9 m 離れた 1 m の箱は重なる");
        Assert.IsFalse(OrientedBox.Overlaps(Vector3.Zero, Quaternion.Identity, half, new Vector3(1.1f, 0f, 0f), Quaternion.Identity, half), "1.1 m 離れた 1 m の箱は重ならない");

        // 45° 回した箱の角は、軸に沿った箱より遠くへ届く（半分の対角 0.707 m）: 中心が 1.15 m なら届き、1.25 m なら届かない。
        var turned = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 4f);
        Assert.IsTrue(OrientedBox.Overlaps(Vector3.Zero, Quaternion.Identity, half, new Vector3(1.15f, 0f, 0f), turned, half));
        Assert.IsFalse(OrientedBox.Overlaps(Vector3.Zero, Quaternion.Identity, half, new Vector3(1.25f, 0f, 0f), turned, half));

        // 細長い棒（六尺棒のような）が箱の上を斜めに横切る: 箱の 3 軸では分けられず、軸どうしの外積で分ける所。
        var rod = new Vector3(1f, 0.02f, 0.02f);
        var diagonal = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4f) * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 4f);
        Assert.IsTrue(OrientedBox.Overlaps(Vector3.Zero, Quaternion.Identity, half, Vector3.Zero, diagonal, rod), "中心を通る棒は重なる");
        Assert.IsFalse(OrientedBox.Overlaps(Vector3.Zero, Quaternion.Identity, half, new Vector3(0f, 1.3f, 0f), diagonal, rod), "箱の上 1.3 m の棒は重ならない");
    }

    [TestMethod]
    [TestCategory("slow")]
    public void TwoHandedWeaponsDoNotCarveTheirHolderAtSpawn()
    {
        foreach (var (redId, blueId) in new[] { ("daichi", "sean"), ("sean", "daichi"), ("jin", "sean"), ("konrad", "sean") })
        {
            using var game = TvGame(redId, blueId, seed: 1);
            // 助走（コンストラクタの中で約 1.7 秒）の間の接触・骨折は、試合の最初の歩まで消されずに残っている。
            // 生成の重なりを当てていたときは六尺棒が胸・頭を 100〜280 セル削った。構えへ下ろす途中で刀が胸をかすめる（jin: 2 セル）のは、
            // 離れた後の本物の接触なので数に許す。
            var carved = game.SelfTouches.Sum(t => t.Removed);
            Assert.IsTrue(carved <= MaxGrazeCells, $"{redId} 対 {blueId}:生成した武器が握った本人の体を {carved} セル削った（{string.Join(", ", game.SelfTouches.Where(t => t.Removed > 0).Select(t => $"{t.Part} {t.Removed} at {t.Point}"))}）");
            Assert.AreEqual(0, game.BrokenBones.Count, $"{redId} 対 {blueId}: 助走の間に骨が折れた（{string.Join(", ", game.BrokenBones.Select(b => b.Part))}）");
            Assert.IsFalse(game.Player.IsDead || game.Cpu.IsDead, $"{redId} 対 {blueId}: 助走の間に死んだ");

            for (var i = 0; i < 201 && !game.MatchOver; i++)
            {
                game.Step(default, 1f / 60f);
            }

            Assert.IsFalse(game.MatchOver, $"{redId} 対 {blueId}: 始まって 3.4 秒で決着した（{game.Message}）");

            // 免除は一度離れるまで: 打ち合ううちに全部の組が離れ、自分の体にも当たるようになる（人形の単位で刀は 8 秒、ロングソードは 10 秒で 0 だった）。
            for (var i = 0; i < 1007 && !game.MatchOver && game.SpawnOverlapCount > 0; i++)
            {
                game.Step(default, 1f / 60f);
            }

            Assert.AreEqual(0, game.SpawnOverlapCount, $"{redId} 対 {blueId}: 生成の重なりの免除が約 20 秒たっても残っている");
        }
    }

    /// <summary>テレビ中継の下見（tvscout）と同じ作り方の VFC の試合（名簿のキャラ・コーナーから・檻・助走約 1.7 秒）。</summary>
    private static BoxingGame TvGame(string redId, string blueId, int seed)
    {
        var red = CharacterRoster.Get(redId);
        var blue = CharacterRoster.Get(blueId);
        var org = Arena.Organization.Of("vfc");
        var redContender = Arena.RosterLeague.ContenderOf(red, blue.Id, null);
        var blueContender = Arena.RosterLeague.ContenderOf(blue, red.Id, null);
        var corners = Arena.TvShowRules.Stage(Arena.VideoMaker.WithCharacters(
            Arena.VideoMaker.Corners(redContender, blueContender, seed, red.Wear, blue.Wear, red.Weapon, blue.Weapon,
                s => blueContender.Create(s), s => redContender.Create(s), true), red, blue), org);
        return new BoxingGame(seed: seed, corners: corners, rules: Arena.TvShowRules.Rules(1, 16.78f, org));
    }
}
