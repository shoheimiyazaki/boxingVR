using System.Numerics;
using MiniRing.Desktop;

namespace MiniRing.Tests;

[TestClass]
[TestCategory("slow")]
public sealed class MenuLoadingTests
{
    [TestMethod]
    public void LoadingMatch_DoesNotBlockPresentation_AndReportsFailure()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var shell = new MatchMenu("unused", Path.Combine(Path.GetTempPath(), "miniring-menu-test-settings.json"), loadPortraits: false);
        using var menu = new MenuGame(shell, _ =>
        {
            entered.Set();
            Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(10)));
            throw new InvalidOperationException("load failure");
        }, _ => throw new NotSupportedException());
        menu.StartMatch(new(MatchMode.Spectate, Characters.CharacterRoster.Get("konrad"), Characters.CharacterRoster.Get("sean")));
        try
        {
            Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(10)));
            var (instances, _) = menu.BuildRenderables();
            Assert.AreEqual(0, instances.Count);
            Assert.IsTrue(menu.IsLoadingMatch);
            Assert.IsTrue(menu.BuildOverlayText(Matrix4x4.Identity, new(960, 540)).Any(t => t.Text.Contains("準備", StringComparison.Ordinal)));
        }
        finally { release.Set(); }
        Assert.IsTrue(SpinWait.SpinUntil(() => { menu.PollLoading(); return !menu.IsLoadingMatch; }, TimeSpan.FromSeconds(10)));
        Assert.IsNull(menu.Presentation);
    }
}
