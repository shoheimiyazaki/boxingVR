using System.Numerics;
using Aigis.Render.Voxels;
using Silk.NET.Vulkan;

namespace Aigis.Render;

/// <summary><c>HeadlessRenderer.Voxels.cs</c>'s live-window counterpart: the same <see cref="VoxelVolumePipeline"/>.</summary>
public sealed unsafe partial class WindowedRenderer
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
        _voxels.PrepareUploads(voxels, _renderPass, dynamicViewport: true, _swapchainExtent.Width, _swapchainExtent.Height);
    }

    private void DrawVoxels(CommandBuffer cmd, IReadOnlyList<VoxelVolumeDraw> voxels, Matrix4x4 viewProjection)
        => _voxels?.Draw(cmd, voxels, viewProjection);

    private void DisposeVoxelResources() => _voxels?.Dispose();
}
