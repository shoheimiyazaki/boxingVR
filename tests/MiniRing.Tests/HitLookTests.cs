namespace MiniRing.Tests;

/// <summary>当たりの光・音の尺度（<see cref="HitLook"/>）: ダメージに対して連続・単調で、軽い当たりは控えめ、重い当たりは最大へ。</summary>
[TestClass]
public sealed class HitLookTests
{
    [TestMethod]
    public void Strength_IsContinuousAndMonotoneFromNothingToFull()
    {
        Assert.AreEqual(0f, HitLook.Strength(0f));
        Assert.AreEqual(0f, HitLook.Strength(HitLook.FloorDamage));
        Assert.AreEqual(1f, HitLook.Strength(HitLook.FullDamage), 1e-5f);
        var last = 0f;
        for (var d = 0f; d <= HitLook.FullDamage + 5f; d += 0.25f)
        {
            var s = HitLook.Strength(d);
            Assert.IsGreaterThanOrEqualTo(last, s, $"damage {d}");
            Assert.IsLessThan(last + 0.2f, s, $"no jump at damage {d}");
            last = s;
        }
    }

    [TestMethod]
    public void Loudness_GrowsWithDamageAndLightBlowsStayQuiet()
    {
        Assert.AreEqual(0f, HitLook.Loudness(0f));
        Assert.AreEqual(1f, HitLook.Loudness(HitLook.FullDamage));
        Assert.IsLessThan(HitLook.Loudness(8f), HitLook.Loudness(2f));
        Assert.IsLessThan(0.15f, HitLook.Loudness(2f), "a graze is still modest");
    }

    [TestMethod]
    public void AnEffectiveBlow_LightsAndSoundsClearly()
    {
        // 今の拳の当たりの上の側（ダメージ 5〜8）は、光も音もはっきり出る。
        Assert.IsGreaterThan(0.4f, HitLook.Strength(5f));
        Assert.IsGreaterThan(0.25f, HitLook.Loudness(5f));
    }
}
