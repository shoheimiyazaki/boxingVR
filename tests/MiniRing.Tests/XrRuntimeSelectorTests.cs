using MiniRing.Vr;

namespace MiniRing.Tests;

[TestClass]
public sealed class XrRuntimeSelectorTests
{
    private const string Meta = @"C:\Program Files\Oculus\Support\oculus-runtime\oculus_openxr_64.json";
    private const string VirtualDesktop = @"C:\Program Files\Virtual Desktop Streamer\OpenXR\virtualdesktop-openxr.json";
    private const string SteamVr = @"C:\Program Files (x86)\Steam\steamapps\common\SteamVR\steamxr_win64.json";

    [TestMethod]
    public void Classify_RecognisesTheKnownRuntimes()
    {
        Assert.AreEqual(XrRuntimeKind.Meta, XrRuntimeSelector.Classify(Meta));
        Assert.AreEqual(XrRuntimeKind.VirtualDesktop, XrRuntimeSelector.Classify(VirtualDesktop));
        Assert.AreEqual(XrRuntimeKind.SteamVr, XrRuntimeSelector.Classify(SteamVr));
        Assert.AreEqual(XrRuntimeKind.Other, XrRuntimeSelector.Classify(@"C:\Windows\System32\MixedRealityRuntime.json"));
    }

    [TestMethod]
    public void Auto_PrefersMeta_EvenWhenSteamVrIsListedFirst()
    {
        var choice = XrRuntimeSelector.Choose("auto", [SteamVr, VirtualDesktop, Meta], null);

        Assert.AreEqual(Meta, choice.Manifest);
        Assert.AreEqual(XrRuntimeKind.Meta, choice.Kind);
        Assert.IsNull(choice.Error);
    }

    [TestMethod]
    public void Auto_FallsBackToVirtualDesktop()
    {
        var choice = XrRuntimeSelector.Choose("auto", [SteamVr, VirtualDesktop], null);

        Assert.AreEqual(VirtualDesktop, choice.Manifest);
    }

    [TestMethod]
    public void Auto_NeverSilentlyUsesSteamVr()
    {
        var choice = XrRuntimeSelector.Choose("auto", [SteamVr], null);

        Assert.IsNull(choice.Manifest);
        Assert.IsNotNull(choice.Error);
        StringAssert.Contains(choice.Error, "Meta Quest Link");
        StringAssert.Contains(choice.Error, "--runtime steamvr");
    }

    [TestMethod]
    public void ExplicitRequests_AndUserOverride()
    {
        Assert.AreEqual(SteamVr, XrRuntimeSelector.Choose("steamvr", [SteamVr, Meta], null).Manifest);
        Assert.IsNull(XrRuntimeSelector.Choose("system", [SteamVr], null).Manifest);
        Assert.IsNull(XrRuntimeSelector.Choose("system", [SteamVr], null).Error);
        Assert.IsNotNull(XrRuntimeSelector.Choose("meta", [SteamVr], null).Error);
        Assert.AreEqual(@"D:\custom.json", XrRuntimeSelector.Choose(@"D:\custom.json", [], null).Manifest);
        Assert.AreEqual(VirtualDesktop, XrRuntimeSelector.Choose("auto", [Meta], VirtualDesktop).Manifest);
        Assert.IsNotNull(XrRuntimeSelector.Choose("bogus", [Meta], null).Error);
    }
}
