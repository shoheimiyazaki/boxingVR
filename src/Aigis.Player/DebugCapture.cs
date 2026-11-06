using System.Numerics;
using System.Text.Json;
using Aigis.Foundation;
using Aigis.Physics;
using Aigis.Render;

namespace Aigis.Player;

/// <summary>
/// The human-triggered half of interactive AI debugging: a hotkey in
/// <c>Aigis.Player</c>/<c>games/*</c> writes the exact same PNG+state-JSON shape a human
/// would otherwise have to describe verbally — "look at this" — so Claude can read it back
/// after the fact. Reuses the identical <see cref="HeadlessRenderer"/>+<see cref="PngWriter"/>
/// pattern already proven by <c>AgentHost.Screenshot</c>: no new Vulkan/coordinate code, just
/// the live scene's own <c>BuildRenderables()</c> output and current camera rendered
/// off-screen. This is the gap TASK-027 flagged — "Claude cannot verify a live window's
/// pixel content itself" — closed without touching the swapchain at all.
/// </summary>
public static class DebugCapture
{
    /// <summary>Renders <paramref name="renderables"/> off-screen with
    /// <paramref name="viewProjection"/> and writes a PNG + a state JSON side-car (same body
    /// fields as the agent's <c>observe</c> verb) to <paramref name="outputDirectory"/>.
    /// Returns the written PNG path. Optional <c>particles</c> are this frame's billboards, so a
    /// snapshot taken mid-effect actually shows the effect rather than the scene without it.
    /// Optional <c>lighting</c>/<c>shadow</c> (TASK-150 re-review LOW finding: previously always
    /// defaulted, so an F-key debug snapshot could show a brighter/shadowless room than the same
    /// frame in the live window) let the caller pass the exact same values it hands the live
    /// renderer, so the snapshot matches what a human is actually seeing on screen.</summary>
    public static string Capture(
        PhysicsWorld world,
        (IReadOnlyList<PrimitiveInstance> Instances, IReadOnlyList<DebugLine> Lines) renderables,
        Matrix4x4 viewProjection,
        string outputDirectory,
        string label,
        uint width = 1024,
        uint height = 768,
        IReadOnlyList<ParticleInstance>? particles = null,
        SceneLighting? lighting = null,
        ShadowMapConfig? shadow = null,
        Vector3? cameraPosition = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        Directory.CreateDirectory(outputDirectory);
        var basePath = Path.Combine(outputDirectory, $"{label}-{world.StepCount}");

        using var renderer = new HeadlessRenderer(width, height);
        var pixels = renderer.RenderPrimitiveScene(
            new Vector4(0.10f, 0.11f, 0.14f, 1f), viewProjection, renderables.Instances, renderables.Lines, text: null, particles,
            lighting, shadow, cameraPosition);
        var pngPath = basePath + ".png";
        PngWriter.WriteRgba(pngPath, (int)width, (int)height, pixels);

        var bodies = world.SnapshotDynamics().Select(body => new Dictionary<string, object?>
        {
            ["id"] = body.Id,
            ["position"] = new[] { body.Position.X, body.Position.Y, body.Position.Z },
            ["orientation"] = new[] { body.Orientation.X, body.Orientation.Y, body.Orientation.Z, body.Orientation.W },
            ["linearVelocity"] = new[] { body.LinearVelocity.X, body.LinearVelocity.Y, body.LinearVelocity.Z },
            ["awake"] = body.Awake,
            ["settled"] = body.IsSettled(),
        }).ToArray();

        var state = new Dictionary<string, object?>
        {
            ["stepCount"] = world.StepCount,
            ["bodies"] = bodies,
        };
        File.WriteAllText(basePath + ".json", JsonSerializer.Serialize(state));

        return pngPath;
    }
}
