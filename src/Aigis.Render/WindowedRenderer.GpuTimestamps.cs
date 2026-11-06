using Silk.NET.Vulkan;

namespace Aigis.Render;

/// <summary>
/// GPU-side timing for <see cref="RenderPhaseProfiler"/> — a small timestamp query pool written at
/// pass boundaries and read back one frame later.
/// <para>
/// Exists because the CPU breakdown can only say "the fence wait was 28 ms". That names the GPU as
/// the bottleneck but not the pass, and the three candidates (shadow depth passes, opaque fill,
/// blended overdraw) call for completely different fixes. These queries are what turn the guess into
/// a number.
/// </para>
/// <para>
/// <b>Entirely inert unless <c>AIGIS_PROFILE_RENDER=1</c></b>: no pool is created, no
/// <c>vkCmdWriteTimestamp</c> is recorded, and every method here returns immediately. Timestamps are
/// also skipped when the device reports a <c>timestampPeriod</c> of 0 (no support), in which case
/// the report says so rather than printing zeros.
/// </para>
/// </summary>
public sealed unsafe partial class WindowedRenderer
{
    /// <summary>Timestamps per frame: start, after shadows, after opaque, after transparent, after
    /// the finishing pass, after overlay. One more than the number of
    /// <see cref="RenderPhaseProfiler.GpuPhase"/> values, because N phases need N+1 boundaries.</summary>
    private static readonly uint GpuTimestampCount = (uint)Enum.GetValues<RenderPhaseProfiler.GpuPhase>().Length + 1;

    private QueryPool _gpuQueryPool;
    private float _gpuTimestampPeriodNs;
    private bool _gpuTimestampsReady;

    /// <summary>True once a frame's timestamps have been recorded and can be read back.</summary>
    private bool _gpuTimestampsPending;

    private void EnsureGpuTimestamps()
    {
        if (_gpuTimestampsReady || !RenderPhaseProfiler.Enabled)
        {
            return;
        }

        _gpuTimestampsReady = true;
        _vk.GetPhysicalDeviceProperties(_physicalDevice, out var properties);
        _gpuTimestampPeriodNs = properties.Limits.TimestampPeriod;
        if (_gpuTimestampPeriodNs <= 0f)
        {
            return; // No timestamp support — RenderPhaseProfiler prints "(no GPU timestamps)".
        }

        var info = new QueryPoolCreateInfo
        {
            SType = StructureType.QueryPoolCreateInfo,
            QueryType = QueryType.Timestamp,
            QueryCount = GpuTimestampCount,
        };
        if (_vk.CreateQueryPool(_device, in info, null, out _gpuQueryPool) != Result.Success)
        {
            _gpuTimestampPeriodNs = 0f; // Diagnostics must never take a window down.
        }
    }

    /// <summary>Resets the pool for a new frame. Must be recorded OUTSIDE a render pass, so this is
    /// called right after <c>vkBeginCommandBuffer</c>.</summary>
    private void BeginGpuTimestamps(CommandBuffer cmd)
    {
        EnsureGpuTimestamps();
        if (_gpuTimestampPeriodNs <= 0f)
        {
            return;
        }

        _vk.CmdResetQueryPool(cmd, _gpuQueryPool, 0, GpuTimestampCount);
        _vk.CmdWriteTimestamp(cmd, PipelineStageFlags.TopOfPipeBit, _gpuQueryPool, 0);
    }

    /// <summary>Closes <paramref name="phase"/> on the GPU timeline.</summary>
    private void WriteGpuTimestamp(CommandBuffer cmd, RenderPhaseProfiler.GpuPhase phase)
    {
        if (_gpuTimestampPeriodNs <= 0f)
        {
            return;
        }

        _vk.CmdWriteTimestamp(cmd, PipelineStageFlags.BottomOfPipeBit, _gpuQueryPool, (uint)phase + 1);
        if (phase == RenderPhaseProfiler.GpuPhase.Overlay)
        {
            _gpuTimestampsPending = true;
        }
    }

    /// <summary>Reads back the last recorded frame's timestamps into <see cref="RenderPhases"/>.
    /// Called after the in-flight fence has been waited on, so the results are already available and
    /// this never blocks.</summary>
    private void ReadGpuTimestamps()
    {
        if (_gpuTimestampPeriodNs <= 0f || !_gpuTimestampsPending)
        {
            return;
        }

        _gpuTimestampsPending = false;
        var results = stackalloc ulong[(int)GpuTimestampCount];
        var status = _vk.GetQueryPoolResults(
            _device, _gpuQueryPool, 0, GpuTimestampCount,
            GpuTimestampCount * sizeof(ulong), results, sizeof(ulong), QueryResultFlags.Result64Bit);
        if (status != Result.Success)
        {
            return;
        }

        for (var phase = 0; phase < (int)GpuTimestampCount - 1; phase++)
        {
            var ticks = results[phase + 1] - results[phase];
            RenderPhases.MarkGpu((RenderPhaseProfiler.GpuPhase)phase, ticks * _gpuTimestampPeriodNs / 1_000_000d);
        }

        RenderPhases.EndGpuFrame();
    }

    private void DestroyGpuTimestamps()
    {
        if (_gpuQueryPool.Handle != 0)
        {
            _vk.DestroyQueryPool(_device, _gpuQueryPool, null);
            _gpuQueryPool = default;
        }
    }
}
