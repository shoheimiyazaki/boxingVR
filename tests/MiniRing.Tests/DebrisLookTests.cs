using System.Numerics;
using Aigis.Render;
using Aigis.Render.Voxels;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 飛び散るボクセルの見え方: 欠片は自分の色のまま、自分の色の光の尾を引く（尾の長さは材質で）。金属と金属がぶつかると火花。
/// </summary>
[TestClass]
public sealed class DebrisLookTests
{
    private static List<ParticleInstance> Trails(Vector4 color, float trail, bool ownColor)
    {
        var debris = new VoxelDebris(capacity: 4);
        debris.Spawn(Vector3.Zero, new Vector3(1.678f, 0f, 0f), Quaternion.Identity, new Vector3(0.01408f), Vector3.Zero, color, heat: 0.5f, trail: trail, ownColor: ownColor);
        var list = new List<ParticleInstance>();
        debris.WriteTrails(list);
        return list;
    }

    [TestMethod]
    public void IronOnIron_ThrowsSparks_WoodOnIronDoesNot()
    {
        static int Glowing(WeaponKind second)
        {
            using var game = new BoxingGame(seed: 1);
            var at = new Vector3(0f, BoxingGame.RingSurfaceHeight + 1.127f, 0f);
            // 寝かせた剣へ、上から刃（鉄）か棍棒（木）を速く当てる。
            game.AddWeapon(WeaponKind.IronSword, at, Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2f));
            var striker = game.AddWeapon(second, at + new Vector3(0.0845f, 0.0845f, 0f), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0f));
            game.World.SetLinearVelocity(striker.Body.BodyId, new Vector3(0f, -6.714f, 0f));
            var before = Count(game);
            for (var i = 0; i < 20; i++)
            {
                game.Step(default, 1f / 240f);
            }

            return Count(game) - before;
        }

        static int Count(BoxingGame game)
        {
            var list = new List<PrimitiveInstance>();
            game.Debris.WriteInstances(list);
            return list.Count(p => p.Material == MaterialKind.Emissive);
        }

        Assert.IsGreaterThan(3, Glowing(WeaponKind.IronSword), "steel on steel sparks");
        Assert.AreEqual(0, Glowing(WeaponKind.WoodenClub), "wood on steel does not");
    }

    [TestMethod]
    public void ABladeCut_SpraysTheFleshTheWayTheBladeSwept()
    {
        // 刃で前腕を横から斬る: 飛んだ欠片と血は、刃の進んだ向きへ一筋にしぶく（当たった所から見て、刃の進む向きへ）。
        var trial = WeaponDrills.Trials.Single(t => t.Name == "sword-edge");
        using var game = WeaponDrills.StillMatch();
        var shot = trial.Setup(game);
        WeaponDrills.Run(trial, game, shot, 13, 1f / 60f);
        var list = new List<PrimitiveInstance>();
        game.Debris.WriteInstances(list);
        var offsets = list.Select(p => p.Model.Translation - shot.Focus).Where(o => o.Length() > 0.05634f).ToList();
        Assert.IsNotEmpty(offsets, "the cut ejects debris away from the impact");
        var along = offsets.Average(o => Vector3.Dot(Vector3.Normalize(o), shot.Heading));
        Assert.IsGreaterThan(0.5f, along, $"they fly on along the cut (mean cosine {along:0.00})");
    }
}
