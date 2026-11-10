using System.Numerics;
using Aigis.Render.Voxels;
using Silk.NET.Vulkan;

namespace Aigis.Render;

/// <summary>
/// The headless half of the voxel-volume draw (<see cref="VoxelVolumeDraw"/>): the surface of each volume is built on the
/// GPU from its 3D texture by the shared <see cref="VoxelVolumePipeline"/>. Uploads run before the render pass; the draw
/// runs with the opaque scene.
/// </summary>
public sealed unsafe partial class HeadlessRenderer
{
    private VoxelVolumePipeline? _voxels;

    /// <summary>Voxel volumes the last frame left out (more than the pipeline holds). Zero on every healthy frame.</summary>
    public int VoxelVolumesDroppedLastFrame => _voxels?.DroppedLastFrame ?? 0;

    private void PrepareVoxels(IReadOnlyList<VoxelVolumeDraw> voxels)
    {
        if (voxels.Count == 0)
        {
            return;
        }

        _voxels ??= new VoxelVolumePipeline(_vk, _physicalDevice, _device, _queue, _commandPool);
        _voxels.PrepareUploads(voxels, _boxRenderPass, dynamicViewport: false, _width, _height);
    }

    private void DrawVoxels(CommandBuffer cmd, IReadOnlyList<VoxelVolumeDraw> voxels, Matrix4x4 viewProjection)
        => _voxels?.Draw(cmd, voxels, viewProjection);

    private void DisposeVoxelResources() => _voxels?.Dispose();
}
