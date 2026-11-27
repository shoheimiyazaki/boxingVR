using System.Numerics;
using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>調べ物（BASE_BODY_PLAN §10.8）: 止まった HMD で足が踏み直し続ける。踏み直しの閾値と構えを寄せる不感帯を変えて、歩数と骨盤の後半の流れを見る。</summary>
[TestClass]
[TestCategory("Probe")]
public sealed class StepTriggerProbe
{
    [TestMethod]
    [DataRow(0.09f, 0.08f)]
    [DataRow(0.12f, 0.08f)]
    [DataRow(0.09f, 0.14f)]
    [DataRow(0.14f, 0.14f)]
    public void StillHmd(float threshold, float deadband)
    {
        var tuning = FighterTuning.Default with { PlayerFatigue = false };
        tuning = tuning with { Footwork = tuning.Footwork with { StepThreshold = threshold, CaptureDeadband = deadband } };
        var game = new BoxingGame(tuning, corners: SwordTraining.Corners(WeaponKind.Katana, 2.535f)) { HidePlayerHead = true };
        var mapper = new VrPlayerMapper(weaponMode: true) { GripHolds = true, TwoHand = VrTwoHand.Right, FirstPerson = true, HeldWeapon = WeaponKind.Katana };
        var head = new Vector3(0f, 1.62f, 0f);
        var raw = new VrRawFrame(head, Quaternion.Identity, new Vector3(-0.35f, 1.0f, -0.1f), new Vector3(0.15f, 1.25f, -0.4f), true, true, Vector2.Zero, Vector2.Zero, 1f, 1f, false, true, Quaternion.Identity, Quaternion.Identity);
        var fw = game.Player.FootworkState;
        Vector3 start = default, middle = default;
        var stepsAt = new List<int>();
        for (var i = 0; i < 240 * 13; i++)
        {
            game.Step(mapper.Read(raw, game.PlayerSense, 1f / 240f), 1f / 240f);
            if (i == 604) { start = game.Player.PelvisPosition; }
            if (i == 604 + 1611) { middle = game.Player.PelvisPosition; }
            if (i % 240 == 239) { stepsAt.Add(fw.Steps); }
        }

        var end = game.Player.PelvisPosition;
        Console.WriteLine(FormattableString.Invariant($"STEPS threshold {threshold} deadband {deadband}: steps/s {string.Join(",", stepsAt)} drift {Vector2.Distance(new(start.X, start.Z), new(end.X, end.Z)) * 100:0.0} cm later half {Vector2.Distance(new(middle.X, middle.Z), new(end.X, end.Z)) * 100:0.0} cm"));
    }
}
