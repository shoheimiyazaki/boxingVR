using System.Text.Json.Nodes;
using MiniRing.Bots;
using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 型の経路の差分（<see cref="FormTune"/>・<see cref="WeaponStyle.Forms"/>、教育の側）。差分が無ければ教科書の型そのもの、
/// 時計の速さは同じ道を短い時間でなぞる、知らない型の名前・範囲の外は断る。
/// </summary>
[TestClass]
public sealed class FormTuneTests
{
    [TestMethod]
    public void NoTune_LeavesTheTextbookFormsAsTheyAre()
    {
        var kesa = FormSwordsman.Forms(WeaponKind.Katana).First(f => f.Name == "kesa");
        Assert.AreSame(kesa, FormTune.Identity.Apply(kesa));
        var plain = FormSwordsman.Techniques(WeaponKind.Katana, 0.6f, 0f);
        var empty = FormSwordsman.Techniques(WeaponKind.Katana, 0.6f, 0f, new Dictionary<string, FormTune>());
        CollectionAssert.AreEqual(plain.Select(t => (t.Name, t.Range)).ToList(), empty.Select(t => (t.Name, t.Range)).ToList());
    }

    [TestMethod]
    public void Tempo_TracesTheSamePathInLessTime_AndStepsAsFar()
    {
        var kesa = FormSwordsman.Forms(WeaponKind.Katana).First(f => f.Name == "kesa");
        var fast = new FormTune(Tempo: 2f).Apply(kesa);
        Assert.AreEqual(kesa.Duration / 2f, fast.Duration, 1e-5f);
        foreach (var t in new[] { 0.1f, 0.4f, 0.9f })
        {
            Assert.AreEqual(0f, Vector3Distance(kesa.Sample(t).Right, fast.Sample(t / 2f).Right), 1e-5f, "same hand path");
        }

        // 腰の歩く速さ × 時間 = 踏み込む距離は同じ。
        var stepSlow = kesa.Frames.Aggregate(System.Numerics.Vector3.Zero, (s, f) => s + (f.Velocity * kesa.FrameTime));
        var stepFast = fast.Frames.Aggregate(System.Numerics.Vector3.Zero, (s, f) => s + (f.Velocity * fast.FrameTime));
        Assert.AreEqual(0f, Vector3Distance(stepSlow, stepFast), 1e-4f);
    }

    [TestMethod]
    public void Reach_ShiftsTheRangeOfThatFormOnly()
    {
        var plain = FormSwordsman.Techniques(WeaponKind.Katana, 0.6f, 0f);
        var tuned = FormSwordsman.Techniques(WeaponKind.Katana, 0.6f, 0f, new Dictionary<string, FormTune> { ["kesa"] = new(Reach: -0.1f) });
        foreach (var (a, b) in plain.Zip(tuned))
        {
            Assert.AreEqual(a.Range + (a.Name == "kesa" ? -0.1f : 0f), b.Range, 1e-5f, a.Name);
        }
    }

    [TestMethod]
    public void Education_CarriesFormTunes_AndRefusesUnknownFormsOrOutOfRangeValues()
    {
        var educated = WeaponEducation.Resolve(WeaponKind.Katana, JsonNode.Parse("{ \"Forms\": { \"kesa\": { \"Tempo\": 1.3, \"Lift\": 0.1 } } }")!.AsObject())!;
        Assert.AreEqual(1.3f, educated.Forms!["kesa"].Tempo);
        Assert.AreEqual(1f, educated.Forms["kesa"].Twist, "values not written stay the identity");
        Assert.ThrowsExactly<InvalidDataException>(() => WeaponEducation.Resolve(WeaponKind.Katana, JsonNode.Parse("{ \"Forms\": { \"kesa\": { \"Tempo\": 9 } } }")!.AsObject()));
        var typo = educated with { Forms = new Dictionary<string, FormTune> { ["kessa"] = new(Tempo: 1.2f) } };
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => FormSwordsman.Bot(WeaponKind.Katana, 1, 0.6f, FighterTuning.Default, typo));
    }

    private static float Vector3Distance(System.Numerics.Vector3 a, System.Numerics.Vector3 b) => System.Numerics.Vector3.Distance(a, b);
}
