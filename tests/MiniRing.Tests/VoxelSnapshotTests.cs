using System.Numerics;
using Aigis.Foundation.Voxels;
using Aigis.Render;
using Aigis.Render.Voxels;

namespace MiniRing.Tests;

[TestClass]
public sealed class VoxelSnapshotTests
{
    private static VoxelGrid Grid() => new(new VoxelPalette([
        new("empty", VoxelTissue.Empty, 0f, false, Vector4.Zero, Vector4.Zero),
        new("solid", VoxelTissue.Solid, 1f, false, Vector4.One, Vector4.One)]),
        new Vector3(0.01f), (3, 2, 2), (_, _) => 1);

    [TestMethod]
    public void Capture_OwnsOldBytesAndCachesUnchangedRevision()
    {
        var volume = VoxelVolume.For(Grid());
        var producer = new VoxelSnapshotProducer();
        var old = producer.Capture([new(volume, Matrix4x4.Identity)]);
        volume.Rgba[0] = 0;
        var sameRevision = producer.Capture([new(volume, Matrix4x4.CreateTranslation(1, 2, 3))]);
        var consumer = new VoxelSnapshotConsumer();
        var originalDraws = consumer.Resolve(old);
        Assert.AreSame(originalDraws, consumer.Resolve(old));
        var resolved = originalDraws[0];
        Assert.AreEqual(255, resolved.Volume.Rgba[0]);
        Assert.AreNotSame(volume, resolved.Volume);
        var laterDraws = consumer.Resolve(sameRevision);
        Assert.AreNotSame(originalDraws, laterDraws);
        Assert.AreSame(laterDraws, consumer.Resolve(sameRevision));
        var later = laterDraws[0];
        Assert.AreSame(resolved.Volume, later.Volume);
        Assert.AreEqual(255, later.Volume.Rgba[0]);
        Assert.AreEqual(Matrix4x4.CreateTranslation(1, 2, 3), later.CellToWorld);
    }

    [TestMethod]
    public void Resolve_SkippedColorRevisionsKeepIdentityAndLatestBytes()
    {
        var grid = Grid();
        var volume = VoxelVolume.For(grid);
        var producer = new VoxelSnapshotProducer();
        var consumer = new VoxelSnapshotConsumer();
        var old = producer.Capture([new(volume, Matrix4x4.Identity)]);
        var rendered = consumer.Resolve(old)[0].Volume;
        var revision = rendered.Revision;
        volume.Sync(grid, 1, (_, color, x, _, _) => x == 0 ? Vector4.UnitW : color);
        _ = producer.Capture([new(volume, Matrix4x4.Identity)]);
        volume.Sync(grid, 2, (_, color, x, _, _) => x == 2 ? Vector4.UnitW : color);
        var current = producer.Capture([new(volume, Matrix4x4.Identity)]);
        Assert.AreSame(rendered, consumer.Resolve(current)[0].Volume);
        CollectionAssert.AreEqual(volume.Rgba, rendered.Rgba);
        Assert.AreEqual(revision + 1, rendered.Revision);
        Assert.AreEqual(rendered.Revision, consumer.Resolve(current)[0].Volume.Revision);
        var independent = new VoxelSnapshotConsumer().Resolve(old)[0].Volume;
        Assert.AreEqual(255, independent.Rgba[8]);
    }

    [TestMethod]
    public void Resolve_NewSourceAtSameRevisionGetsIndependentVolume()
    {
        var producer = new VoxelSnapshotProducer();
        var consumer = new VoxelSnapshotConsumer();
        var first = consumer.Resolve(producer.Capture([new(VoxelVolume.For(Grid()), Matrix4x4.Identity)]))[0].Volume;
        var second = consumer.Resolve(producer.Capture([new(VoxelVolume.For(Grid()), Matrix4x4.Identity)]))[0].Volume;
        Assert.AreNotSame(first, second);
        Assert.AreEqual(0, consumer.Resolve(producer.Capture([])).Count);
    }

    [TestMethod]
    public void Resolve_SparseColorChangeProducesUploadBoundsAndGapFallsBackToFull()
    {
        var grid = Grid();
        var source = VoxelVolume.For(grid);
        var producer = new VoxelSnapshotProducer();
        var consumer = new VoxelSnapshotConsumer();
        var rendered = consumer.Resolve(producer.Capture([new(source, Matrix4x4.Identity)]))[0].Volume;
        var uploaded = rendered.Revision;
        source.Sync(grid, 1, (_, color, x, y, z) => x == 1 && y == 0 && z == 1 ? Vector4.UnitW : color);
        consumer.Resolve(producer.Capture([new(source, Matrix4x4.Identity)]));
        Assert.AreEqual(new VoxelUploadBounds(1, 0, 1, 1, 1, 1), rendered.GetUploadBounds(uploaded));
        Assert.AreEqual(new VoxelUploadBounds(0, 0, 0, 3, 2, 2), rendered.GetUploadBounds(-1));
        source.Sync(grid, 2, (_, color, x, y, z) => x == 2 && y == 1 && z == 0 ? Vector4.UnitW : color);
        consumer.Resolve(producer.Capture([new(source, Matrix4x4.Identity)]));
        Assert.AreEqual(new VoxelUploadBounds(0, 0, 0, 3, 2, 2), rendered.GetUploadBounds(uploaded));
    }

    [TestMethod]
    [TestCategory("visual")]
    public void SparseGpuUpload_MatchesFreshFullUpload()
    {
        var grid = Grid();
        var source = VoxelVolume.For(grid);
        var producer = new VoxelSnapshotProducer();
        var consumer = new VoxelSnapshotConsumer();
        using var renderer = new HeadlessRenderer(64, 64);
        var view = Camera.ViewProjection(new Vector3(6, 5, 7), new Vector3(1.5f, 1f, 1f),
            Vector3.UnitY, MathF.PI / 4f, 1f, 0.1f, 30f);
        var initial = renderer.RenderPrimitiveScene(Vector4.Zero, view, [], [],
            voxels: consumer.Resolve(producer.Capture([new(source, Matrix4x4.Identity)])));
        source.Sync(grid, 1, (_, color, x, y, z) => x == 1 && y == 1 && z == 1 ? Vector4.UnitW : color);
        var sparse = renderer.RenderPrimitiveScene(Vector4.Zero, view, [], [],
            voxels: consumer.Resolve(producer.Capture([new(source, Matrix4x4.Identity)])));
        var full = renderer.RenderPrimitiveScene(Vector4.Zero, view, [], [], voxels: [new(source, Matrix4x4.Identity)]);
        Assert.IsFalse(initial.AsSpan().SequenceEqual(sparse), "The changed texel must be visible to exercise the upload.");
        CollectionAssert.AreEqual(full, sparse);
    }
}
