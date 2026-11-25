using Aigis.Physics.Voxels;

namespace MiniRing.Tests;

/// <summary>
/// 測定用に変える試合の既定（サブステップの速さ・武器の掃き直しの回数・接触の刻み）は、変えた流れとその子にだけ効き、
/// 並行して走る別のテスト・試合には漏れない（Probe が既定を変えている間に、並列のテストが既定の物理で走れるように）。
/// </summary>
[TestClass]
public sealed class SimulationDefaultsIsolationTests
{
    [TestMethod]
    public async Task AChangedDefault_IsSeenByItsOwnFlowAndChildren_NotByAFlowAlreadyRunning()
    {
        using var changed = new ManualResetEventSlim();
        using var checkedOther = new ManualResetEventSlim();
        var other = Task.Run(() =>
        {
            changed.Wait(TimeSpan.FromSeconds(10));
            var seen = (BoxingGame.DefaultSubstepRate, BoxingGame.DefaultMaxContactPasses, VoxelContact.SampleSpacingScale);
            checkedOther.Set();
            return seen;
        });

        var mine = await Task.Run(async () =>
        {
            BoxingGame.DefaultSubstepRate = 120f;
            BoxingGame.DefaultMaxContactPasses = 1;
            VoxelContact.SampleSpacingScale = 0.5f;
            changed.Set();
            checkedOther.Wait(TimeSpan.FromSeconds(10));
            // A task started after the change (as the arena's parallel matches are) sees it.
            return await Task.Run(() => (BoxingGame.DefaultSubstepRate, BoxingGame.DefaultMaxContactPasses, VoxelContact.SampleSpacingScale));
        });

        Assert.AreEqual((120f, 1, 0.5f), mine);
        Assert.AreEqual((240f, 2, 1f), await other);
        Assert.AreEqual(240f, BoxingGame.DefaultSubstepRate, "the test's own flow is untouched");
    }
}
