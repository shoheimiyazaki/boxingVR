using System.Numerics;
using MiniRing.Arena;

namespace MiniRing.Tests;

/// <summary>
/// 中継のリプレイのカメラ（<see cref="TvDirector.Replay"/>）の決まり（2026-11-24、ユーザー: 腕が折れる瞬間が手前の腕に隠れる・リプレイをさらに遅く）。
/// 見た目だけで、試合の計算には響かない。
/// </summary>
[TestClass]
public sealed class ReplayCameraTests
{
    [TestMethod]
    public void ReplaySide_IsTheSideTheHitLandedOn()
    {
        // 打った側から打たれた側へ +X。横は ±Z。
        var axis = Vector3.UnitX;
        var plusZ = TvDirector.ReplaySide(axis, new Vector3(0.05f, 0.3f, 0.12f));
        var minusZ = TvDirector.ReplaySide(axis, new Vector3(0.05f, -0.2f, -0.12f));

        Assert.IsTrue(plusZ.Z > 0.99f, $"+Z の側に当たったのに {plusZ}");
        Assert.IsTrue(minusZ.Z < -0.99f, $"−Z の側に当たったのに {minusZ}");
        Assert.AreEqual(0f, plusZ.Y, 1e-6f, "カメラの側は水平");
    }

    [TestMethod]
    public void ReplaySide_KeepsTheUsualSide_ForAHitOnTheCentreLine()
    {
        var axis = Vector3.Normalize(new Vector3(1f, 0f, 1f));
        var usual = Vector3.Cross(axis, Vector3.UnitY);

        var side = TvDirector.ReplaySide(axis, axis * 0.1f);

        Assert.IsTrue(Vector3.Distance(usual, side) < 1e-5f, $"正面の当たりは今までの側 {usual}、実際は {side}");
    }

    [TestMethod]
    public void Replay_IsSlowerThanHalfSpeed()
    {
        // 30 fps・1 歩 1/60 s: 1 歩を 2 コマで 0.25 倍。リプレイの映像の長さは、試合の長さ（前 + 後）の 4 倍ほど。
        var director = new TvDirector(1280, 720, 30, 1f / 60f, "赤", "青");
        var gameSeconds = TvDirector.ReplayBefore + TvDirector.ReplayAfter;

        Assert.IsTrue(director.ReplaySeconds >= gameSeconds * 3.9f, $"映像 {director.ReplaySeconds:0.00} s ÷ 試合 {gameSeconds:0.00} s");
    }

    [TestMethod]
    public void InsideCage_PullsTheEyeInsideTheNarrowCageAndRaisesIt()
    {
        var cage = RingLayout.Cage;
        var outside = cage.Center + new Vector3(0f, 1f, cage.Size); // 金網の外、檻の直径ぶん離れた所
        var eye = TvDirector.InsideCage(cage, outside);
        var flat = new Vector2(eye.X - cage.Center.X, eye.Z - cage.Center.Z);

        Assert.IsTrue(flat.Length() < cage.Half, $"目が金網の外: 中心から {flat.Length():0.00} m（辺まで {cage.Half:0.00} m）");
        Assert.IsTrue(eye.Y > outside.Y, "寄せたぶん高くなる");
    }

    [TestMethod]
    public void InsideCage_LeavesTheEyeAlone_InsideTheCageOrInARopedRing()
    {
        var inside = RingLayout.Cage.Center + new Vector3(0.3f, 1f, 0.2f);
        var ropedOutside = RingLayout.Boxing.Center + new Vector3(0f, 1f, RingLayout.Boxing.Size);

        Assert.AreEqual(inside, TvDirector.InsideCage(RingLayout.Cage, inside));
        Assert.AreEqual(ropedOutside, TvDirector.InsideCage(RingLayout.Boxing, ropedOutside));
    }
}
