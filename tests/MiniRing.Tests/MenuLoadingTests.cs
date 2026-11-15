using System.Numerics;
using MiniRing.Bots;
using MiniRing.Desktop;
using MiniRing.Weapons;

namespace MiniRing.Tests;

[TestClass]
public sealed class MenuLoadingTests
{
    [TestMethod]
    public void LoadingMatch_DoesNotBlockPresentation_AndReportsFailure()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var menu = new MenuGame(new MatchMenu(HandWear.Glove, "unused"), _ =>
        {
            entered.Set();
            Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(10)));
            throw new InvalidOperationException("load failure");
        }, _ => throw new NotSupportedException());
        menu.StartMatch(new(MatchMode.Spectate, BotCatalog.Boxer, BotCatalog.Boxer, HandWear.Glove, HandWear.Glove));
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
