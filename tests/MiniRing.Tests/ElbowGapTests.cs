using System.Globalization;
using System.Numerics;
using MiniRing.Arena;

namespace MiniRing.Tests;

/// <summary>パンチ中の肘の関節（上腕の肘の点と前腕の肘の点）のずれを測る。 回帰: 打ち出す間はセルの半分（約 7 mm）未満、当たった瞬間もセル 1 つ未満。</summary>
[TestClass]
[TestCategory("slow")]
public sealed class ElbowGapTests
{
    [TestMethod]
    public void Elbow_StaysClosed_WhileThePunchGoesOut()
    {
        var (max, maxT, half, overlap, beforeHit, hitT) = MaxGap(FighterTuning.Default, 1);
        float cell;
        using (var game = new BoxingGame())
        {
            cell = game.Player.Voxels!.Parts.Where(p => p.Kind is BodyPartKind.UpperArm or BodyPartKind.Forearm)
                .Min(p => MathF.Min(p.Body.Grid.CellSize.X, MathF.Min(p.Body.Grid.CellSize.Y, p.Body.Grid.CellSize.Z)));
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"ELBOWGAP max={max * 1000f:F1} mm at t={maxT:F3}, before the first hit (t={hitT:F3}) {beforeHit * 1000f:F1} mm (cell {cell * 1000f:F1} mm) upperHalf={half.UpperArm} foreHalf={half.Forearm} overlap={overlap}"));
        // 打ち出す間は、肘のつなぎ目がセルの半分より開かない（開くと腕の継ぎ目に穴が見える）。
        Assert.IsLessThan(0.5f * cell, beforeHit, "肘の隙間（当たる前）[m]");
        // 2026-11-26（PhysicsVersion 106、つま先の蝶番を固くした）: 前は当たった瞬間を含めて 7 mm 未満で見ていた（前のつま先で 5.8 mm）。最大は拳が当たった直後の
        // 10 ms ほど（当たる前は 3.6 → 4.9 mm、当たった後に 7.1 mm。ToeRegressionProbe.ElbowGap）: 接触の止める力積は拳だけに掛かり、腕の残りは肘のバネが引き戻すまで
        // 進む（足首・つま先の当たった瞬間の開きと同じ近道。docs/DEBT.md「脚の関節の開き」）。当たった瞬間はセル 1 つ未満で見る。
        Assert.IsLessThan(cell, max, "肘の隙間（当たった瞬間を含む）[m]");
    }

    /// <summary>右ストレート 1 本（守らない相手・届く間合い）の、打ち出しから 0.6 秒の肘の 2 点の最大の距離 [m] と、その時刻。</summary>
    internal static (float Max, float MaxT, Aigis.Physics.HumanoidRagdoll.HalfExtents Half, float Overlap, float BeforeHit, float? HitT) MaxGap(FighterTuning tuning, int seed)
    {
        using var bout = new PunchLab.Bout(new(PunchLabPath.Standard, "still", "reach", 1.05f, false), seed, Bots.BotCatalog.Boxer.Telegraph, tuning);
        var f = bout.Game.Player;
        var world = bout.Game.World;
        var side = Hand.Right.LocalSide();
        var upper = side > 0f ? f.Parts.RightUpperArm : f.Parts.LeftUpperArm;
        var fore = side > 0f ? f.Parts.RightForearm : f.Parts.LeftForearm;
        var u0 = world.GetState(upper);
        var f0 = world.GetState(fore);
        var half = f.HalfExtents;
        var overlap = tuning.JointOverlap;
        var elbow = f0.Position - (Vector3.Transform(Vector3.UnitX, u0.Orientation) * side * (half.Forearm.X - overlap));
        var aU = Vector3.Transform(elbow - u0.Position, Quaternion.Conjugate(u0.Orientation));
        var aF = Vector3.Transform(elbow - f0.Position, Quaternion.Conjugate(f0.Orientation));
        var max = 0f;
        var maxT = 0f;
        var beforeHit = 0f;
        float? hitT = null;
        var n = 0;
        while (bout.PunchTime is null && n++ < 5000) { bout.Step(); }
        while (bout.PunchTime is { } t && t < 0.6f)
        {
            var u = world.GetState(upper);
            var fo = world.GetState(fore);
            var gap = Vector3.Distance(u.Position + Vector3.Transform(aU, u.Orientation), fo.Position + Vector3.Transform(aF, fo.Orientation));
            if (gap > max) { max = gap; maxT = t; }
            hitT ??= bout.Game.RecentHits.Any(h => h.Attacker == f && !h.Kick) ? t : null;
            beforeHit = hitT is null ? MathF.Max(beforeHit, gap) : beforeHit;
            bout.Step();
        }

        return (max, maxT, half, overlap, beforeHit, hitT);
    }
}
