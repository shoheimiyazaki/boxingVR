using System.Numerics;
using MiniRing.Arena;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// リング脇のカメラ（<see cref="TvDirector.Ringside"/>）: 1 カットの間は回り込む側を変えない。毎コマ「相手の胸から遠い側」を選び直していたので、
/// 相手が動くたびに左右が入れ替わり、選手を正面寄りに映す絵が 0.2〜0.6 秒ごとに飛んだ（2026-11-18、ユーザー: 正面のカメラがパカパカ入れ替わる）。
/// </summary>
[TestClass]
public sealed class RingsideCameraTests
{
    private const int Fps = 30;

    [TestMethod]
    public void TheCameraStaysOnOneSideForTheWholeShot()
    {
        using var game = new BoxingGame(seed: 2, corners: BotCatalog.Corners("boxer"));
        var director = new TvDirector(1280, 720, Fps, 1f / 240f, "red", "blue");
        var previous = director.Ringside(game, game.Player, 0f).Eye;
        var largest = 0f;
        for (var frame = 1; frame < 201; frame++) // 約 6.7 秒（人形の単位の 4 秒）
        {
            game.StepStage(1f / Fps, StagePose.Corners); // 2 人がコーナーへ歩く: 相手の位置が動き続ける
            var eye = director.Ringside(game, game.Player, frame / (float)Fps).Eye;
            largest = MathF.Max(largest, Vector3.Distance(eye, previous));
            previous = eye;
        }

        // 側が入れ替わると、目の位置は 1 コマで選手の大きさの数倍（10 cm 以上）飛ぶ。選手と一緒に動くだけなら 1 コマ数 cm（Eye は表示の空間の m なので換算しない）。
        Assert.IsLessThan(0.05f, largest, $"the ringside eye jumped {largest:0.000} m in one frame");
    }

    [TestMethod]
    public void ANewShotChoosesItsSideAgain()
    {
        using var game = new BoxingGame(seed: 2, corners: BotCatalog.Corners("boxer"));
        var held = new TvDirector(1280, 720, Fps, 1f / 240f, "red", "blue");
        var fresh = new TvDirector(1280, 720, Fps, 1f / 240f, "red", "blue");
        _ = held.Ringside(game, game.Player, 0f);
        _ = held.Ringside(game, game.Player, 2f);
        for (var frame = 0; frame < 101; frame++) // 約 3.4 秒
        {
            game.StepStage(1f / Fps, StagePose.Corners);
        }

        // 経過時間が 0 に戻った（新しいカット）ら、いまの位置関係で選び直す: 初めて撮る監督と同じ絵になる。
        Assert.AreEqual(fresh.Ringside(game, game.Player, 0f).Eye, held.Ringside(game, game.Player, 0f).Eye);
    }
}
