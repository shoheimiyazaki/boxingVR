using System.Numerics;
using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 何も入力しないとき（空の入力・ほぼ静止した VR の姿勢）、体が床の上を流れていかない（ユーザー報告: VR の刀の稽古で、何も入力しなくても滑っていく）。
/// 原因は足の運びの記憶: 着いた足の「置いた所」を毎歩実際の足へ合わせていたので足首を引く力が留める力にならず、もう一方の足を踏み出す反作用で
/// 着いた足が滑り、構えの直しの踏み出しが止まらなかった（人形の単位で 30 秒で 40〜80 cm、170〜260 歩）。直した後は 4 cm 以内・数歩（人形の単位。人の大きさで 11 cm）。VR の姿勢（頭・手のゆれ）は人の実物の寸法なので換算しない。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class SlideTests
{
    private const float Dt = 1f / 240f;
    private const float Seconds = 33.57f;

    private sealed class IdleFoe : IFighterControl
    {
        public FighterCommand Read(in FighterSense sense, float dt)
        {
            var face = sense.OpponentChest - sense.Self.Origin;
            face.Y = 0f;
            return new FighterCommand(Vector3.Zero, face, new HandCommand(new Vector3(0.2817f, 0f, 0.4225f), false), new HandCommand(new Vector3(-0.2817f, 0f, 0.4225f), false));
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

    private static (float Drift, int Steps) Run(WeaponKind? weapon, bool vr, float headYaw, float sway, float warmupSeconds = 0f)
    {
        var corners = weapon is { } w
            ? SwordTraining.Corners(w, 2.535f)
            : new BoxingCorners("idle", _ => new IdleFoe(), Referee: false, BreakActions: false, Ring: RingLayout.DrillBoxing);
        using var game = new BoxingGame(FighterTuning.Default with { PlayerFatigue = false }, corners: corners);
        var mapper = new VrPlayerMapper(weaponMode: weapon is not null);
        var warmupSteps = (int)(warmupSeconds / Dt);
        var start = Flat(game.Player.PelvisPosition);
        var initialFootsteps = game.Player.Footwork.Steps;
        for (var i = 1; i <= (int)(Seconds / Dt); i++)
        {
            game.Step(vr ? mapper.Read(Raw(i * Dt, headYaw, sway), game.PlayerSense, Dt) : default, Dt);
            if (i == warmupSteps)
            {
                initialFootsteps = game.Player.Footwork.Steps;
                start = Flat(game.Player.PelvisPosition);
            }
        }

        return ((Flat(game.Player.PelvisPosition) - start).Length(), game.Player.Footwork.Steps - initialFootsteps);
    }

    private static Vector3 Flat(Vector3 v) => new(v.X, 0f, v.Z);

    /// <summary>
    /// 刀の VR の構えは、流れも踏み直しも、生成の構えから VR の姿勢へ移り終えた後（5 秒）から数える（2026-11-25、PhysicsVersion 105。
    /// docs/plans/FORM_DECAY_STUDY.md §9）。刀を実物の重さ（0.61 → 1.08 kg）にすると、生成の T 字の腕の先から VR の手の位置へ刀を運ぶ最初の 1.6 秒に、
    /// その反作用で 4〜8 歩踏み直して骨盤が 17〜28 cm 動く（前の刀では 10 cm 前後）。その後は 4〜11 秒ごとの踏み直しで ±3 cm ずつ行き来するだけで、
    /// 流れ続けはしない（このテストが守る、直す前の 30 秒で 45 cm・170 歩の流れではない）。入力の始めから数えると、最初の移り変わりに、窓の終わり（33.57 秒）の
    /// 直前に最後の 1 歩が入るか（±3 cm・1 歩）が足されるだけで値が上限（22.54 cm・12 歩）をまたいで飛んだ（22.55 cm・12 歩）。上限は変えない。
    /// </summary>
    private const float VrSettleSeconds = 5f;

    [TestMethod]
    public void KatanaStandingStillWithAVrPoseDoesNotSlide()
    {
        var (drift, steps) = Run(WeaponKind.Katana, vr: true, headYaw: 0f, sway: 0f, warmupSeconds: VrSettleSeconds);
        Assert.IsLessThan(0.2254f, drift, $"静止した VR の姿勢（刀）で骨盤が流れた: {drift * 100f:0.0} cm");
        Assert.IsLessThan(12, steps, $"構えの踏み直しが止まらない: {steps} 歩");
    }

    [TestMethod]
    public void KatanaWithTwoCentimetreHeadSwayDoesNotSlide()
    {
        var (drift, steps) = Run(WeaponKind.Katana, vr: true, headYaw: 0f, sway: 0.02f, warmupSeconds: VrSettleSeconds);
        Assert.IsLessThan(0.2254f, drift, $"頭が 2 cm ゆれるだけで骨盤が流れた: {drift * 100f:0.0} cm（直す前は 20 秒で 45 cm）");
        Assert.IsLessThan(12, steps, $"構えの踏み直しが止まらない: {steps} 歩（直す前は 170 歩前後）");
    }

    [TestMethod]
    public void KatanaLookingAwayFromTheOpponentDoesNotSlide()
    {
        // 頭が相手から 40 度それている（手は同じ位置）: 胴がひねられた構えのまま止まっていられる。
        // 流れも踏み直しも、向きの変化と VR の姿勢へ追いついた後で数える（VrSettleSeconds）。
        var (drift, steps) = Run(WeaponKind.Katana, vr: true, headYaw: 0.7f, sway: 0f, warmupSeconds: VrSettleSeconds);
        Assert.IsLessThan(0.2254f, drift, $"胴をひねった構えで骨盤が流れた: {drift * 100f:0.0} cm（直す前は 20 秒で 29 cm）");
        Assert.IsLessThan(12, steps, $"構えの踏み直しが止まらない: {steps} 歩");
    }

    [TestMethod]
    public void BareHandsWithHeadSwayDoNotSlide()
    {
        var (drift, steps) = Run(null, vr: true, headYaw: 0f, sway: 0.02f);
        Assert.IsLessThan(0.2254f, drift, $"素手の VR で頭が 2 cm ゆれるだけで骨盤が流れた: {drift * 100f:0.0} cm（直す前は 20 秒で 55 cm）");
        Assert.IsLessThan(12, steps, $"構えの踏み直しが止まらない: {steps} 歩");
    }
}
