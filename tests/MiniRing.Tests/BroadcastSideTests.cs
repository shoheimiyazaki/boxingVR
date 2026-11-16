using System.Numerics;
using MiniRing.Audio;

namespace MiniRing.Tests;

/// <summary>
/// 中継の部位の名前の左右（2026-11-16、ユーザーの指摘「左腕が切断されているときに右腕と言っている」）。
/// Aigis の剛体の名前は −X 側が "Left" で、本人から見ると右になる。中継は本人から見た左右で言う。
/// </summary>
[TestClass]
public sealed class BroadcastSideTests
{
    [TestMethod]
    public void PartName_SaysTheFightersOwnSide()
    {
        using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("stand", _ => new StandGuard(), _ => new StandGuard()));
        var f = game.Player;

        Assert.AreEqual("左上腕", Broadcast.PartName(f, f.UpperArmId(Hand.Left)), "左手（Hand.Left）の腕は左上腕");
        Assert.AreEqual("左前腕", Broadcast.PartName(f, f.ForearmId(Hand.Left)));
        Assert.AreEqual("右上腕", Broadcast.PartName(f, f.UpperArmId(Hand.Right)));
        Assert.AreEqual("右前腕", Broadcast.PartName(f, f.ForearmId(Hand.Right)));

        // 体の向きから見ても: 本人の左は、前（胸の +Z）と上（+Y）から決まる左側（右手系で 上 × 前）。
        var chest = game.World.GetState(f.Parts.Chest);
        var forward = Vector3.Transform(Vector3.UnitZ, chest.Orientation);
        var left = Vector3.Cross(Vector3.UnitY, forward);
        var leftArm = game.World.GetState(f.UpperArmId(Hand.Left)).Position - chest.Position;
        Assert.IsTrue(Vector3.Dot(leftArm, left) > 0f, "左上腕は体の左側にある");
    }
}
