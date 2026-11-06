using System.Numerics;
using Aigis.Foundation;
using Aigis.Physics;
using Aigis.Platform;
using Aigis.Player;
using Aigis.Render;
using Silk.NET.Input;

// The first human-playable Aigis scene: the same ball-socket pendulum as
// samples/Sample.Pendulum, live in a window — orbit the camera with the left mouse
// button, zoom with the scroll wheel, Space to pause/resume, F9 to save a debug snapshot
// (PNG+state JSON) for an AI to inspect, Escape or the window's close button to quit.
// See docs/plans/human-play.md.

var profiler = new StartupProfiler();
profiler.Mark("process start");

Console.WriteLine($"{EngineInfo.Name} {EngineInfo.Version} — Aigis.Player (pendulum)");
Console.WriteLine("Drag with left mouse to orbit, scroll to zoom, Space to pause, F9 for a debug snapshot, Escape to quit.");
if (StartupProfiler.Enabled)
{
    Console.WriteLine("(AIGIS_PROFILE_STARTUP=1 — will report timings and exit after the first rendered frame.)");
}

const float length = 1f;
const float angleDeg = 30f;
const float bobRadius = 0.08f;

using var world = new PhysicsWorld();
var anchor = world.AddKinematicBox(Vector3.Zero, new Vector3(0.04f));
var angle = angleDeg * MathF.PI / 180f;
var initial = new Vector3(MathF.Sin(angle) * length, -MathF.Cos(angle) * length, 0f);
var bob = world.AddDynamicSphere(initial, bobRadius, 1f);
world.AddBallSocket(anchor, bob, Vector3.Zero, -initial, frequency: 60f);
profiler.Mark("PhysicsWorld constructed");

// TASK-159: a textured screen-space overlay, drawn in the corner — the demo host's proof that the
// sampled-texture HUD path works on the WINDOWED renderer, not only headless. Built ONCE here, not
// per frame, because the renderer caches the GPU upload by TextureData instance identity (see that
// type's doc comment); a per-frame instance would re-upload every frame and eventually throw.
// Four distinguishable quadrants rather than a flat colour so a UV/Y-flip regression is visible to
// anyone who runs `dotnet run --project src/Aigis.Player` and glances at the corner.
var hudBadge = TextureData.Quadrants(
    32,
    32,
    new Vector4(0.90f, 0.25f, 0.20f, 1f),
    new Vector4(0.30f, 0.80f, 0.35f, 1f),
    new Vector4(0.25f, 0.45f, 0.90f, 1f),
    new Vector4(0.95f, 0.95f, 0.95f, 1f));

var camera = new OrbitCamera(initialDistance: length + 2f) { Target = new Vector3(0f, -length * 0.5f, 0f) };
var loop = new FixedStepLoop(FixedStep.Default.Dt);
var spaceWasDown = false;
var reportedFirstFrame = false;
var f9WasDown = false;
var debugCaptureRequested = false;

using var window = PlatformWindow.Open(PlatformWindowOptions.Default with { Title = "Aigis.Player — pendulum" });
profiler.Mark("PlatformWindow.Open returned");
WindowedRenderer? renderer = null;

window.Loaded += () =>
{
    profiler.Mark("window Loaded event fired");
    renderer = new WindowedRenderer(window.RawWindow);
    profiler.Mark("WindowedRenderer constructed (Vulkan instance/device/swapchain)");
};
window.Resized += _ => renderer?.NotifyFramebufferResized();

window.Updated += realDeltaSeconds =>
{
    var input = window.Input;

    if (input.IsKeyDown(Key.Escape))
    {
        window.Close();
        return;
    }

    if (input.IsMouseButtonDown(MouseButton.Left))
    {
        camera.ApplyDrag(input.MouseDelta);
    }

    camera.ApplyScroll(input.ScrollDelta);

    if (input.IsKeyDown(Key.Space) && !spaceWasDown)
    {
        loop.Paused = !loop.Paused;
    }

    spaceWasDown = input.IsKeyDown(Key.Space);

    var f9IsDown = input.IsKeyDown(Key.F9);
    if (f9IsDown && !f9WasDown)
    {
        debugCaptureRequested = true;
    }

    f9WasDown = f9IsDown;

    var steps = loop.Advance(realDeltaSeconds);
    for (var i = 0; i < steps; i++)
    {
        world.Step(FixedStep.Default.Dt);
    }

    input.EndFrame();
};

window.Rendered += _ =>
{
    if (renderer is null)
    {
        return;
    }

    var anchorState = world.GetState(anchor);
    var bobState = world.GetState(bob);
    PrimitiveInstance[] instances =
    [
        new(PrimitiveKind.Cube, Matrix4x4.CreateScale(0.08f) * Matrix4x4.CreateTranslation(anchorState.Position),
            Vector3.Zero, new Vector4(0.85f, 0.85f, 0.85f, 1f)),
        new(PrimitiveKind.Sphere, Matrix4x4.CreateTranslation(bobState.Position),
            new Vector3(bobRadius, 0f, 0f), new Vector4(0.86f, 0.32f, 0.24f, 1f)),
    ];
    DebugLine[] lines = [new(anchorState.Position, bobState.Position, new Vector4(0.9f, 0.85f, 0.2f, 1f))];

    var size = window.RawWindow.Size;
    var aspect = size.Y == 0 ? 1f : (float)size.X / size.Y;
    var viewProjection = camera.ViewProjection(aspect);

    if (debugCaptureRequested)
    {
        debugCaptureRequested = false;
        var snapshotPath = DebugCapture.Capture(world, (instances, lines), viewProjection, Path.Combine(".visualcheck", "debug"), "player-pendulum");
        Console.WriteLine($"Debug snapshot saved: {snapshotPath}");
    }

    var presented = renderer.RenderPrimitiveSceneAndPresent(
        new Vector4(0.10f, 0.11f, 0.14f, 1f),
        viewProjection,
        instances,
        lines,
        // Centre (40,40) with a 48px size = a 16px inset from the top-left corner, in the overlay's
        // pixel convention (origin top-left, Y down, centre-anchored) — so it stays put as the
        // window resizes rather than drifting with the aspect ratio.
        images: [new OverlayImageDraw(hudBadge, new Vector2(40f, 40f), new Vector2(48f, 48f), Vector4.One)]);

    if (presented && !reportedFirstFrame)
    {
        reportedFirstFrame = true;
        profiler.Mark("first frame presented (includes first-time shader/pipeline compile)");
        if (StartupProfiler.Enabled)
        {
            profiler.Report();
            window.Close();
        }
    }
};

window.Run();
renderer?.Dispose();
