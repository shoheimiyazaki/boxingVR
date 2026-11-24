using System.Numerics;
using MiniRing.Bots;
using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>VR の記録を剣士の型にする（<see cref="VrMotionForms"/>）: 目標の経路として、構えから始まり構えへ戻り、記録の刀の向き・刃の向きをそのまま運ぶ。</summary>
[TestClass]
public sealed class VrMotionFormsTests
{
    private const float Rate = 80f;

    /// <summary>
    /// 合成の記録: 刀を立てて構え（右手は胸の前）、<paramref name="peak"/> 秒に右手を前下へ速く振り下ろす（刀の向きが上から前へ回る）。歩きはしない。
    /// </summary>
    private static VrMotionClip Swing(float duration, float peak)
    {
        var frames = new List<VrMotionFrame>();
        for (var i = 0; i * (1f / Rate) <= duration; i++)
        {
            var t = i / Rate;
            var u = Math.Clamp((t - (peak - 0.1f)) / 0.2f, 0f, 1f);
            var s = u * u * (3f - (2f * u));
            var right = Vector3.Lerp(new Vector3(-0.1f, 0.2f, 0.4f), new Vector3(-0.1f, -0.4f, 0.9f), s);
            var pitch = float.Lerp(-MathF.PI / 2f, 0.3f, s); // ローカル X（刀の向き）を上から前下へ: X 軸を Y まわりに −90° で前（+Z）、Z まわりのピッチで上下
            var q = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI / 2f) * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -pitch));
            var left = right - (Vector3.Transform(Vector3.UnitX, q) * 0.4f);
            frames.Add(new VrMotionFrame(t, default, Quaternion.Identity, default, Quaternion.Identity, true, default, Quaternion.Identity, true,
                0f, 0f, 1f, 1f, default, default, left, right, q, q, false, false, true, true, default, default));
        }

        return new VrMotionClip(new VrMotionHeader(1.7f, 0.612f, 0.53f, 1f, "Katana", "test"), frames);
    }

    private static SwordMotion Convert(VrMotionClip clip) =>
        VrMotionForms.ToSwordMotion(clip, "test", FormSwordsman.Guard(WeaponKind.Katana), FighterTuning.Default.WalkSpeed, 0.53f, SwordStance.Rate, FighterTuning.Default.MaxTorsoTwistRadians);

    [TestMethod]
    public void StrikePeakIsTheFastestMomentOfTheTip()
    {
        var clip = Swing(3f, 1.8f);
        Assert.AreEqual(1.8f, VrMotionForms.StrikePeak(clip), 0.08f);
    }

    [TestMethod]
    public void FormStartsAndEndsInTheGuard()
    {
        var motion = Convert(Swing(3f, 1.8f));
        var guard = FormSwordsman.Guard(WeaponKind.Katana);
        Assert.AreEqual(0f, Vector3.Distance(motion.Frames[0].Right, guard.Right), 1e-5f);
        Assert.AreEqual(0f, Vector3.Distance(motion.Frames[^1].Right, guard.Right), 1e-4f);
        Assert.AreEqual(1f, Vector3.Dot(motion.Frames[^1].Blade, guard.Blade), 1e-4f);
        // 長さ: 構えの保ち + つなぎ + 記録の区間（山の前後）+ 戻り
        var expected = VrMotionForms.HoldGuard + VrMotionForms.BlendIn + VrMotionForms.Before + VrMotionForms.After + VrMotionForms.BlendOut;
        Assert.AreEqual(expected, motion.Duration, 0.05f);
    }

    [TestMethod]
    public void BladeAndEdgeCarryTheRecordedWeaponOrientation()
    {
        var clip = Swing(3f, 1.8f);
        var motion = Convert(clip);
        var peak = VrMotionForms.StrikePeak(clip);
        // 型の中の、記録の山に当たる時刻（構えの保ち + つなぎ + 山の前）
        var atPeak = motion.Sample(VrMotionForms.HoldGuard + VrMotionForms.BlendIn + VrMotionForms.Before);
        var recorded = clip.Sample(peak).RightWeaponOrientation!.Value;
        // MotionFrame.Orientation（ローカル X = 刀、ローカル −Y = 刃）が記録の回転と同じ（q と −q は同じ向き）
        Assert.AreEqual(1f, MathF.Abs(Quaternion.Dot(atPeak.Orientation, recorded)), 0.02f);
        Assert.AreEqual(0f, Vector3.Distance(atPeak.Right, clip.Sample(peak).RightTarget), 0.02f);
    }
}
