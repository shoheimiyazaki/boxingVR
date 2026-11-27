namespace MiniRing.Tests;

/// <summary>
/// 基準の体の筋の値の置き直し（<see cref="StandardBodyStrength"/>）は、今の基準の体の「関節が支える質量 × てこの長さ」と前の基準の体の比と同じ。
/// 基準の体の寸法・ボクセルを変えたら、この比べが外れて置き直しの値を直すことを知らせる。
/// </summary>
[TestClass]
public sealed class StandardBodyStrengthTests
{
    [TestMethod]
    public void TheJointStrengths_KeepTheirRatioToTheLoadTheyCarry()
    {
        var t = FighterTuning.Default;
        var m = t.Masses;
        var h = t.HalfExtents;
        var glove = Weapons.Weapon.MassOf(Weapons.WeaponKind.Glove);
        var foot = m.Foot + t.ToeMass;
        var arm = m.UpperArm + m.Forearm;
        var aboveHip = m.Pelvis + m.Chest + m.Head + (2f * arm);
        var aboveKnee = aboveHip + (2f * m.Thigh);
        var total = aboveKnee + (2f * (m.Shin + foot));
        var footLength = 2f * h.Foot.Z / (1f - FootGeometry.ToeShare);
        var cases = new (string Name, float New, float Old, float Placed)[]
        {
            ("股", aboveHip * ((2f * h.Pelvis.Y) + h.Chest.Y), 41.6f * 0.36f, StandardBodyStrength.Hip),
            ("膝", aboveKnee * 2f * h.Thigh.Y, 57.6f * 0.40f, StandardBodyStrength.Knee),
            ("足首", (total - (2f * foot)) * 2f * (h.Shin.Y + h.Thigh.Y), 65.6f * 0.78f, StandardBodyStrength.Ankle),
            ("つま先", total * footLength, 67.6f * 0.26f, StandardBodyStrength.Toe),
            ("胴", (m.Chest + m.Head + (2f * arm)) * 2f * h.Chest.Y, 31.6f * 0.36f, StandardBodyStrength.Trunk),
            ("首", m.Head * h.Head.Y, 5f * 0.096f, StandardBodyStrength.Neck),
            ("肩", (arm + glove) * 2f * (h.UpperArm.X + h.Forearm.X), 3.78f * 0.590f, StandardBodyStrength.Shoulder),
            ("肘", (m.Forearm + glove) * 2f * h.Forearm.X, 1.78f * 0.283f, StandardBodyStrength.Elbow),
        };
        foreach (var (name, now, before, placed) in cases)
        {
            Assert.AreEqual(now / before, placed, 0.03f, $"{name}: 質量 × てこの比 {now / before:0.000}");
        }

        // 筋の量（酸素の蓄え・血管）は最大トルクに比例する（断面 × 筋の長さ、筋の長さ ∝ てこ）: トルクの比 ÷ 部位の質量の比。
        var leg = m.Thigh + m.Shin + foot;
        var muscles = new (string Name, float Expected, float Placed)[]
        {
            ("腕", StandardBodyStrength.Shoulder / (arm / 3.30f), StandardBodyStrength.ArmMuscle),
            ("胴", StandardBodyStrength.Trunk / ((m.Pelvis + m.Chest) / 30f), StandardBodyStrength.TrunkMuscle),
            ("脚", (StandardBodyStrength.Hip + StandardBodyStrength.Knee + StandardBodyStrength.Ankle) / 3f / (leg / 13f), StandardBodyStrength.LegMuscle),
        };
        foreach (var (name, expected, placed) in muscles)
        {
            Assert.AreEqual(expected, placed, 0.03f, $"{name}の筋の量: トルクの比 ÷ 質量の比 {expected:0.000}");
        }
    }
}
