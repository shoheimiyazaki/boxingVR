using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Aigis.Render;

/// <summary>
/// Opt-in CPU-side breakdown of ONE windowed frame's render phases — the missing middle term between
/// <c>Aigis.Player.FrameProfiler</c> (which says the whole frame took 32 ms) and
/// <c>tools/Aigis.Benchmark --frame-timings</c> (which says the game's own per-frame work was 4 ms).
/// Without it, the remaining 28 ms is unattributed and every optimization is a guess.
/// <para>
/// <b>Read the first phase first.</b> <see cref="Phase.WaitAndAcquire"/> is the CPU sitting on the
/// previous frame's fence, i.e. the GPU. A frame whose time is nearly all in that phase is
/// GPU-bound and no amount of cheaper command recording will move it; a frame whose time is in the
/// record phases is CPU-bound on submission and the draw-call count is the lever.
/// </para>
/// <para>
/// Gated by <c>AIGIS_PROFILE_RENDER=1</c> so it costs nothing during normal play — not even a
/// <see cref="Stopwatch"/> read (<see cref="Enabled"/> is checked before every measurement). The
/// report is returned as a string rather than printed because this is a library (hard rule 2); the
/// host prints it, alongside its own <c>AIGIS_PROFILE_FRAMES</c> block.
/// </para>
/// </summary>
public sealed class RenderPhaseProfiler
{
    /// <summary>The phases of a recorded frame, in the order they happen.</summary>
    public enum Phase
    {
        /// <summary>Uploading the textures a frame samples but does not own per-frame: overlay images,
        /// the irradiance volume and the world decal sheets.
        /// <para>Its own phase because it happens BEFORE the frame's fence wait and render pass (the
        /// copies and image barriers are illegal inside one), so until 2026-09-05 none of it appeared in
        /// this breakdown at all — a stain sheet is megabytes, its upload ends in a fence wait, and a
        /// frame that paid for one showed up only as unattributed time between the phases. That is the
        /// shape of a mid-round hitch, so it needs a name.</para></summary>
        PrepareTextures,

        /// <summary>Waiting on the in-flight fence + acquiring the swapchain image — the GPU, and
        /// (under vsync) the display, seen from the CPU.</summary>
        WaitAndAcquire,

        /// <summary>Packing and uploading per-frame buffers: instances, lines, particles, lighting.</summary>
        Upload,

        /// <summary>Recording the shadow depth passes.</summary>
        RecordShadow,

        /// <summary>Recording the main scene pass (primitives, decals, particles, lines).</summary>
        RecordScene,

        /// <summary>Recording the screen-space overlays (UI shapes, images, glyph quads).</summary>
        RecordOverlay,

        /// <summary>Ending the command buffer, submitting and presenting.</summary>
        SubmitPresent,
    }

    /// <summary>
    /// The GPU's own share of a frame, read back from Vulkan timestamp queries — the answer to the
    /// question <see cref="Phase.WaitAndAcquire"/> raises but cannot answer ("the GPU took 28 ms, but
    /// doing WHAT?"). One frame behind, because the results are only read once the fence says that
    /// frame finished.
    /// </summary>
    public enum GpuPhase
    {
        /// <summary>The sun + fixture shadow depth passes.</summary>
        Shadow,

        /// <summary>The main pass's opaque primitives.</summary>
        Primitives,

        /// <summary>Decals, particles and debug lines — the blended, overdraw-heavy part of the
        /// main pass.</summary>
        Transparent,

        /// <summary>The fullscreen finishing pass (<see cref="PostProcessSettings"/> — defocus, AO,
        /// bloom). Zero on a frame without one. Split out of <see cref="Overlay"/> on 2026-10-14: it
        /// is a per-PIXEL cost that scales with the window, unlike the HUD it used to be counted with,
        /// and on the four-AI Level4 round it was most of what that line reported.</summary>
        PostProcess,

        /// <summary>The screen-space overlays (UI shapes, images, glyph quads).</summary>
        Overlay,
    }

    private static readonly string[] PhaseNames = Enum.GetNames<Phase>();
    private static readonly string[] GpuPhaseNames = Enum.GetNames<GpuPhase>();

    /// <summary>Whether this process was asked for a render-phase breakdown.</summary>
    public static bool Enabled { get; } =
        string.Equals(Environment.GetEnvironmentVariable("AIGIS_PROFILE_RENDER"), "1", StringComparison.Ordinal);

    private readonly double[] _totalMs = new double[PhaseNames.Length];
    private readonly double[] _worstMs = new double[PhaseNames.Length];
    private readonly double[] _gpuTotalMs = new double[GpuPhaseNames.Length];
    private readonly double[] _gpuWorstMs = new double[GpuPhaseNames.Length];
    private int _gpuFrames;
    private long _mark;

    /// <summary>How many frames have been recorded.</summary>
    public int Frames { get; private set; }

    /// <summary>Starts the stopwatch for the next phase. Call once at the top of a frame.</summary>
    public void Begin()
    {
        if (Enabled)
        {
            _mark = Stopwatch.GetTimestamp();
        }
    }

    /// <summary>Closes <paramref name="phase"/> and opens the next one.</summary>
    public void Mark(Phase phase)
    {
        if (!Enabled)
        {
            return;
        }

        var now = Stopwatch.GetTimestamp();
        var ms = (now - _mark) * 1000d / Stopwatch.Frequency;
        _mark = now;
        _totalMs[(int)phase] += ms;
        if (ms > _worstMs[(int)phase])
        {
            _worstMs[(int)phase] = ms;
        }
    }

    /// <summary>Counts one completed frame. Call after the last <see cref="Mark"/>.</summary>
    public void EndFrame()
    {
        if (Enabled)
        {
            Frames++;
        }
    }

    /// <summary>Records one GPU phase's duration for the frame that just finished on the GPU.</summary>
    public void MarkGpu(GpuPhase phase, double milliseconds)
    {
        _gpuTotalMs[(int)phase] += milliseconds;
        if (milliseconds > _gpuWorstMs[(int)phase])
        {
            _gpuWorstMs[(int)phase] = milliseconds;
        }
    }

    /// <summary>Counts one frame whose GPU timestamps were read back.</summary>
    public void EndGpuFrame() => _gpuFrames++;

    /// <summary>Forgets everything recorded so far — the host calls this once the steady state has
    /// been reached, so first-frame pipeline compiles are not averaged into the report.</summary>
    public void Reset()
    {
        Array.Clear(_totalMs);
        Array.Clear(_worstMs);
        Array.Clear(_gpuTotalMs);
        Array.Clear(_gpuWorstMs);
        Frames = 0;
        _gpuFrames = 0;
    }

    /// <summary>The breakdown as printable lines, or an empty string when disabled/never used.</summary>
    public string Report()
    {
        if (!Enabled || Frames == 0)
        {
            return string.Empty;
        }

        var report = new StringBuilder();
        report.AppendLine("--- AIGIS_PROFILE_RENDER (CPU render phases) ---");
        var total = 0d;
        for (var i = 0; i < PhaseNames.Length; i++)
        {
            total += _totalMs[i] / Frames;
            report.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"  {PhaseNames[i],-16} mean={_totalMs[i] / Frames,7:F2} ms  worst={_worstMs[i],7:F2} ms"));
        }

        report.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"  {"TOTAL",-16} mean={total,7:F2} ms over {Frames} frames"));

        if (_gpuFrames == 0)
        {
            // No timestamp support on this device (or the very first frames) — the CPU table above
            // still stands on its own, so say so rather than printing a table of zeros.
            report.Append("  (no GPU timestamps)");
            return report.ToString();
        }

        report.AppendLine("  -- GPU (timestamp queries) --");
        var gpuTotal = 0d;
        for (var i = 0; i < GpuPhaseNames.Length; i++)
        {
            gpuTotal += _gpuTotalMs[i] / _gpuFrames;
            report.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"  {GpuPhaseNames[i],-16} mean={_gpuTotalMs[i] / _gpuFrames,7:F2} ms  worst={_gpuWorstMs[i],7:F2} ms"));
        }

        report.Append(string.Create(CultureInfo.InvariantCulture,
            $"  {"GPU TOTAL",-16} mean={gpuTotal,7:F2} ms over {_gpuFrames} frames"));
        return report.ToString();
    }
}
