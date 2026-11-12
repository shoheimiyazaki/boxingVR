using System.Numerics;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>調べもの: 立っている・ふらついた体の、部位ごとのマットからの高さ（箱のいちばん低い所）。</summary>
[TestClass]
public sealed class CanvasProbe
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void Clearances()
    {
        var still = BotCatalog.Boxer with { Attacks = false, DefenseRate = 0f, Prediction = 0f, Aggression = 0f };
        using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("still", s => new BoxerBot(still, s), s => new BoxerBot(still, s)));
        var f = game.Cpu;
        for (var t = 0f; t < 1.5f; t += 1f / 240f)
        {
            game.Step(default, 1f / 240f);
        }

        Report(game, f, "standing");
        for (var t = 0f; t < 0.6f; t += 1f / 240f)
        {
            f.Concussion = 0.71f;
            game.Step(default, 1f / 240f);
            if (t is > 0.1f and < 0.12f || t is > 0.28f and < 0.3f || t is > 0.5f and < 0.52f)
            {
                Report(game, f, $"dazed t={t:0.00}");
            }
        }
    }

    private void Report(BoxingGame game, Fighter f, string label)
    {
        var groundY = MiniRing.BoxingGame.RingSurfaceHeight;
        var parts = f.AllParts.Select(id =>
        {
            var s = game.World.GetState(id);
            var half = f.HalfExtentsOf(id);
            var r = Matrix4x4.CreateFromQuaternion(s.Orientation);
            var down = (MathF.Abs(r.M12) * half.X) + (MathF.Abs(r.M22) * half.Y) + (MathF.Abs(r.M32) * half.Z);
            return (Kind: f.KindOf(id), Gap: s.Position.Y - down - groundY);
        }).GroupBy(p => p.Kind).Select(g => $"{g.Key} {g.Min(p => p.Gap) * 1000f:0}mm");
        var gloves = string.Join(" ", new[] { Hand.Left, Hand.Right }.Select(h => $"glove {(f.GloveCenter(h).Y - f.GloveRadius - groundY) * 1000f:0}mm"));
        TestContext.WriteLine($"{label} [{f.State}] pelvis-surface {(f.PelvisPosition.Y - groundY) * 1000f:0}mm: {string.Join(", ", parts)}, {gloves}");
    }
}
