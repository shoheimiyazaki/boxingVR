using System.Numerics;
using MiniRing.Bots;
using MiniRing.Characters;

namespace MiniRing.Tests;

/// <summary>
/// コーナーに立つ所は、体と持ち物（武器・盾・はめる物）を上から見た輪郭から出す（<see cref="RingLayout.CornerFor"/>）。
/// 檻の 120° の角に一律の距離で立たせていたとき、生まれた時点で武器・盾が金網に食い込み、5 ラウンド動けなかった（2026-11-24）。
/// </summary>
[TestClass]
public sealed class CornerClearanceTests
{
    private static readonly RingLayout[] Arenas = [RingLayout.Cage, RingLayout.WeaponsCage, RingLayout.Boxing, RingLayout.Weapons];

    [TestMethod]
    public void CornerFor_KeepsEveryOutlinePointInside_AndAWiderOutlineStandsFurtherFromTheCorner()
    {
        Vector2[] narrow = [new(0.1f, 0.1f), new(-0.1f, -0.1f), new(0.1f, -0.1f), new(-0.1f, 0.1f)];
        Vector2[] wide = [.. narrow.Select(q => q * 6f)];
        foreach (var layout in Arenas)
        {
            foreach (var red in new[] { true, false })
            {
                var near = layout.RestCorner(red, narrow);
                var far = layout.RestCorner(red, wide);
                foreach (var (stand, outline) in new[] { (near, narrow), (far, wide) })
                {
                    var closest = outline.Min(q => layout.EdgeDistance(stand + new Vector3(q.X, 0f, q.Y)));
                    Assert.AreEqual(layout.BoundaryInset + RingLayout.CornerClearance, closest, 1e-4f, $"{layout.Shape} {layout.Size} m red={red}: the outline just clears the edge");
                }

                Assert.IsGreaterThan(Flat(near, layout.Center), Flat(layout.RestCorner(red, []), layout.Center) + 1e-4f, "a point stands deepest in the corner");
                Assert.IsGreaterThan(Flat(far, layout.Center), Flat(near, layout.Center), "a wider outline stands further from the corner");
            }
        }
    }

    [TestMethod]
    [TestCategory("slow")]
    public void EveryRosterLoadout_StartsInItsCorner_WithoutTouchingTheRopesOrTheCage_OrTheOpponent()
    {
        var loadouts = CharacterRoster.All.Where(c => c.Weapon is not null || c.Wear is not (Weapons.HandWear.Glove or Weapons.HandWear.None)).ToList();
        Assert.IsGreaterThanOrEqualTo(5, loadouts.Count, "the roster has armed fighters to check");
        foreach (var layout in Arenas)
        {
            foreach (var c in loadouts)
            {
                using var game = new BoxingGame(seed: 1, corners: new BoxingCorners(c.Id, _ => new StandGuard(), _ => new StandGuard(),
                    CpuWear: c.Wear, PlayerWear: c.Wear, PlayerBody: c.Build, CpuBody: c.Build,
                    CpuWeapon: c.Weapon, PlayerWeapon: c.Weapon, CpuTwoHanded: FormSwordsman.Handles(c.Weapon), PlayerTwoHanded: FormSwordsman.Handles(c.Weapon),
                    Ring: layout.AtCorners()));
                var red = game.FootprintOf(game.Player);
                var blue = game.FootprintOf(game.Cpu);
                Assert.IsNotEmpty(red);
                if (!FitsInside(layout, red))
                {
                    // 生成した姿勢の輪郭が場の向かい合う面の間（隙間を両側に残した幅）より広ければ、立てる所は無く、CornerFor は中心寄りに立たせる
                    // （RingLayout.CornerFor の注釈）。四足のガルヴァ（T 字の長い腕と倍に延ばした鎌、輪郭の幅 3.65 m）は素手の檻（向かい合う辺の間 3.66 m）に
                    // 収まらない。これは場より大きい体という幾何の事実で、隙間の式の誤りではないので、収まる場だけで隙間を確かめる。
                    Assert.IsTrue(c.Build.FourLegged, $"{c.Id} in the {layout.Shape} {layout.Size} m: only the four-legged body is wider than an arena");
                    continue;
                }

                foreach (var (fighter, outline) in new[] { (game.Player, red), (game.Cpu, blue) })
                {
                    var closest = outline.Min(q => layout.EdgeDistance(fighter.PelvisPosition + new Vector3(q.X, 0f, q.Y)));
                    Assert.IsGreaterThan(layout.BoundaryInset, closest, $"{c.Id} in the {layout.Shape} {layout.Size} m ({fighter.Name}): the body and what it holds start clear of the edge");
                }

                // 2 人の輪郭は、2 人を結ぶ線の上で重ならない（向き合って生まれる）。
                var across = (game.Cpu.PelvisPosition - game.Player.PelvisPosition) with { Y = 0f };
                var gap = across.Length();
                var axis = new Vector2(across.X, across.Z) / gap;
                var reach = red.Max(q => Vector2.Dot(q, axis)) + blue.Max(q => -Vector2.Dot(q, axis));
                Console.WriteLine($"{c.Id,-8} {layout.Shape,-11} {layout.Size:0.00} m: pelvis gap {gap:0.00} m, outlines reach {reach:0.00} m toward each other");
                Assert.IsGreaterThan(reach, gap, $"{c.Id} in the {layout.Shape} {layout.Size} m: the two outlines start apart");
            }
        }
    }

    /// <summary>輪郭 <paramref name="outline"/> の、場のどの面の法線の向きの幅も、向かい合う面の間から両側の縁の厚みと隙間を引いた幅に収まる。</summary>
    private static bool FitsInside(RingLayout layout, IReadOnlyList<Vector2> outline)
    {
        var room = layout.Size - (2f * (layout.BoundaryInset + RingLayout.CornerClearance));
        float[] normals = layout.IsCage ? [30f, 90f, 150f] : [0f, 90f];
        return normals.All(deg =>
        {
            var n = new Vector2(MathF.Cos(deg * MathF.PI / 180f), MathF.Sin(deg * MathF.PI / 180f));
            return outline.Max(q => Vector2.Dot(n, q)) - outline.Min(q => Vector2.Dot(n, q)) <= room;
        });
    }

    private static float Flat(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();
}
