using System.Numerics;
using Aigis.Render;

namespace MiniRing.Tests;

[TestClass]
public sealed class PunchCueTests
{
    [TestMethod]
    public void FirmGuardContactGetsAVisibleCueDespiteLowScoringDamage()
    {
        using var game = new BoxingGame();
        var arm = game.Cpu.Parts.LeftForearm;
        var point = game.World.GetState(arm).Position;
        var hit = new HitEvent(game.Player, Hand.Left, game.Cpu, HitZone.Guard, point,
            Speed: 5f, ImpactMass: 6f, BodyShare: 0f, Damage: 2f);
        game.ApplyHit(hit, arm, Vector3.UnitZ * hit.Speed, carve: false);

        var particles = new List<ParticleInstance>();
        game.Impacts.WriteParticles(particles);

        Assert.AreEqual(hit, game.RecentHits.Single());
        Assert.IsTrue(particles.Any(particle => particle.Blend == ParticleBlend.Alpha),
            "A firm guard hit must have a legible foreground cue at this damage.");
    }
}
