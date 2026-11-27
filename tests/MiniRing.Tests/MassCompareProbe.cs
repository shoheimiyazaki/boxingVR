namespace MiniRing.Tests;

/// <summary>部位ごとの質量・慣性（主軸 3 つ）・箱の寸法を、master と基準の体の枝で同じ形式に出す（docs/plans/BASE_BODY_PLAN.md §10.5）。</summary>
[TestClass]
[TestCategory("Probe")]
public sealed class MassCompareProbe
{
    [TestMethod]
    [DataRow("base", 1f, 1f, 1f, 1f)]
    [DataRow("light(akira)", 0.95f, 1f, 1f, 1.05f)]
    [DataRow("standard(jin)", 1f, 1f, 1f, 0.95f)]
    [DataRow("heavy(dmitri)", 1.15f, 1f, 1f, 1.2f)]
    public void Table(string name, float h, float a, float l, float m)
    {
        var build = new BodyBuild(h, a, l, m);
        using var game = new BoxingGame(seed: 1, corners: MiniRing.Bots.BotCatalog.Corners("boxer") with { PlayerBody = build, Referee = false });
        var f = game.Player;
        var world = game.World;
        var seen = new HashSet<string>();
        var total = 0f;
        foreach (var id in f.AllParts)
        {
            var kind = f.KindOf(id).ToString();
            var hx = f.HalfExtentsOf(id) * 200f;
            var mass = world.GetMass(id);
            var i = world.GetLocalInertiaDiagonal(id);
            total += mass;
            if (!seen.Add(kind))
            {
                continue;
            }

            Console.WriteLine($"TBL|{name}|{kind}|{hx.X:0.0}|{hx.Y:0.0}|{hx.Z:0.0}|{mass:0.0000}|{i.X:0.000000}|{i.Y:0.000000}|{i.Z:0.000000}");
        }
        Console.WriteLine($"TBL|{name}|TOTAL|0|0|0|{total:0.0000}|0|0|0");
    }
}
