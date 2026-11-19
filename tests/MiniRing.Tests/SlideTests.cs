using System.Numerics;
using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 何も入力しないとき（空の入力・ほぼ静止した VR の姿勢）、体が床の上を流れていかない（ユーザー報告: VR の刀の稽古で、何も入力しなくても滑っていく）。
/// 原因は足の運びの記憶: 着いた足の「置いた所」を毎歩実際の足へ合わせていたので足首を引く力が留める力にならず、もう一方の足を踏み出す反作用で
/// 着いた足が滑り、構えの直しの踏み出しが止まらなかった（30 秒で 40〜80 cm、170〜260 歩）。直した後は 4 cm 以内・数歩。
/// </summary>
[TestClass]
public sealed class SlideTests
{
    private const float Dt = 1f / 240f;
    private const float Seconds = 20f;

    private sealed class IdleFoe : IFighterControl
    {
        public FighterCommand Read(in FighterSense sense, float dt)
        {
            var face = sense.OpponentChest - sense.Self.Origin;
            face.Y = 0f;
            return new FighterCommand(Vector3.Zero, face, new HandCommand(new Vector3(0.1f, 0f, 0.15f), false), new HandCommand(new Vector3(-0.1f, 0f, 0.15f), false));
        }
    }

    private static readonly Quaternion Facing = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI);

    // 人の寸法の VR の姿勢: 頭と両手が同じ向きに sway [m] だけゆっくりゆれる（立っている人の自然なゆれ）。headYaw は頭が相手の向きからそれる角。
    private static VrRawFrame Raw(float time, float headYaw, float sway)
    {
        var look = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI + headYaw);
        var s = sway * MathF.Sin(2f * MathF.PI * 0.3f * time);
        var c = sway * MathF.Cos(2f * MathF.PI * 0.23f * time);
        return new VrRawFrame(new Vector3(s, 1.58f + (0.5f * c), c), look,
            new Vector3(0.12f + s, 1.25f, 0.25f + c), new Vector3(-0.12f + s, 1.30f, 0.30f + c), true, true,
            Vector2.Zero, Vector2.Zero, 0f, 0f, false, true, Facing, Facing, 0f, 0f);
    }

    private static (float Drift, int Steps) Run(WeaponKind? weapon, bool vr, float headYaw, float sway)
    {
        var corners = weapon is { } w
            ? SwordTraining.Corners(w, 0.9f)
            : new BoxingCorners("idle", _ => new IdleFoe(), Referee: false, BreakActions: false, Ring: RingLayout.DrillBoxing);
        using var game = new BoxingGame(FighterTuning.Default with { PlayerFatigue = false }, corners: corners);
        var mapper = new VrPlayerMapper(weaponMode: weapon is not null);
        var start = Flat(game.Player.PelvisPosition);
        for (var i = 1; i <= (int)(Seconds / Dt); i++)
        {
            game.Step(vr ? mapper.Read(Raw(i * Dt, headYaw, sway), game.PlayerSense, Dt) : default, Dt);
        }

        return ((Flat(game.Player.PelvisPosition) - start).Length(), game.Player.Footwork.Steps);
    }

    private static Vector3 Flat(Vector3 v) => new(v.X, 0f, v.Z);

    [TestMethod]
    public void KatanaStandingStillWithAVrPoseDoesNotSlide()
    {
        var (drift, steps) = Run(WeaponKind.Katana, vr: true, headYaw: 0f, sway: 0f);
        Assert.IsLessThan(0.08f, drift, $"静止した VR の姿勢（刀）で骨盤が流れた: {drift * 100f:0.0} cm");
        Assert.IsLessThan(12, steps, $"構えの踏み直しが止まらない: {steps} 歩");
    }

    [TestMethod]
    public void KatanaWithTwoCentimetreHeadSwayDoesNotSlide()
    {
        var (drift, steps) = Run(WeaponKind.Katana, vr: true, headYaw: 0f, sway: 0.02f);
        Assert.IsLessThan(0.08f, drift, $"頭が 2 cm ゆれるだけで骨盤が流れた: {drift * 100f:0.0} cm（直す前は 20 秒で 45 cm）");
        Assert.IsLessThan(12, steps, $"構えの踏み直しが止まらない: {steps} 歩（直す前は 170 歩前後）");
    }

    [TestMethod]
    public void KatanaLookingAwayFromTheOpponentDoesNotSlide()
    {
        // 頭が相手から 40 度それている（手は同じ位置）: 胴がひねられた構えのまま止まっていられる。
        var (drift, steps) = Run(WeaponKind.Katana, vr: true, headYaw: 0.7f, sway: 0f);
        Assert.IsLessThan(0.08f, drift, $"胴をひねった構えで骨盤が流れた: {drift * 100f:0.0} cm（直す前は 20 秒で 29 cm）");
        Assert.IsLessThan(12, steps, $"構えの踏み直しが止まらない: {steps} 歩");
    }

    [TestMethod]
    public void BareHandsWithHeadSwayDoNotSlide()
    {
        var (drift, steps) = Run(null, vr: true, headYaw: 0f, sway: 0.02f);
        Assert.IsLessThan(0.08f, drift, $"素手の VR で頭が 2 cm ゆれるだけで骨盤が流れた: {drift * 100f:0.0} cm（直す前は 20 秒で 55 cm）");
        Assert.IsLessThan(12, steps, $"構えの踏み直しが止まらない: {steps} 歩");
    }
}
