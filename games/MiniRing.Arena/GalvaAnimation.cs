using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Aigis.Foundation;
using Aigis.Render;
using Aigis.Render.Voxels;

namespace MiniRing.Arena;

internal static class GalvaAnimation
{
    private static readonly Vector4 Background = new(0.055f, 0.075f, 0.091f, 1f);
    private static readonly Vector4 White = new(0.89f, 0.94f, 0.96f, 1f);
    private static readonly Vector4 Accent = new(0.35f, 0.84f, 0.80f, 1f);

    public static int Command(Options options)
    {
        var selection = options.List("only", GalvaClips.All.Select(clip => clip.Id).ToArray());
        var clips = selection.Select(id => GalvaClips.All.FirstOrDefault(clip => clip.Id == id)
            ?? throw new ArgumentException($"Unknown Galva clip: {id}")).ToArray();
        var outDir = Path.GetFullPath(options.Get("out") ?? ".visualcheck/galva-motion");
        Directory.CreateDirectory(outDir);
        var rig = GalvaRig.Create();
        const int bvhFps = 60;
        foreach (var clip in clips)
        {
            File.WriteAllText(Path.Combine(outDir, clip.Id + ".bvh"), GalvaBvh.Write(rig, clip, bvhFps));
        }

        var render = options.Get("render") ?? "on";
        if (render != "on" && render != "off")
        {
            throw new ArgumentException("--render requires on or off.");
        }
        if (render == "on")
        {
            Video(rig, clips, outDir, options.Int("fps", 24, min: 10), options.Get("bones") == "on");
        }
        Console.WriteLine(outDir);
        return 0;
    }

    private static void Video(GalvaRig rig, GalvaClip[] clips, string outDir, int fps, bool bones)
    {
        const int width = 960;
        const int height = 540;
        using var renderer = new HeadlessRenderer(width, height);
        var file = Path.Combine(outDir, bones ? "galva-bones.mp4" : "galva-motion.mp4");
        var start = new ProcessStartInfo("ffmpeg") { RedirectStandardInput = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "-y", "-loglevel", "error", "-f", "rawvideo", "-pix_fmt", "rgba", "-s", $"{width}x{height}",
            "-r", fps.ToString(CultureInfo.InvariantCulture), "-i", "-", "-c:v", "libx264", "-preset", "fast", "-pix_fmt", "yuv420p", "-movflags", "+faststart", file })
        {
            start.ArgumentList.Add(argument);
        }
        using var encoder = Process.Start(start) ?? throw new IOException("Could not start ffmpeg.");
        try
        {
            var draws = new VoxelVolumeDraw[rig.Bones.Count];
            var lines = new List<DebugLine>();
            for (var c = 0; c < clips.Length; c++)
            {
                var clip = clips[c];
                var frames = (int)MathF.Ceiling(clip.Seconds * fps);
                for (var frame = 0; frame <= frames; frame++)
                {
                    var time = clip.Seconds * frame / frames;
                    var pose = rig.Sample(clip, time);
                    lines.Clear();
                    var advance = clip.Walk ? pose[0].At.Z : 0f;
                    for (var g = -8; g <= 16; g++)
                    {
                        const float cell = 0.25f;
                        var color = new Vector4(0.20f, 0.28f, 0.32f, 1f);
                        lines.Add(new DebugLine(new Vector3(-2f, 0f, g * cell), new Vector3(2f, 0f, g * cell), color));
                        if (g <= 8)
                        {
                            lines.Add(new DebugLine(new Vector3(g * cell, 0f, -2f), new Vector3(g * cell, 0f, 4f), color));
                        }
                    }
                    for (var i = 0; i < draws.Length; i++)
                    {
                        var bone = rig.Bones[i];
                        draws[i] = bone.Draw with { CellToWorld = bone.Draw.CellToWorld * pose[i].Matrix };
                        if (bones && bone.Parent >= 0)
                        {
                            lines.Add(new DebugLine(pose[bone.Parent].At, pose[i].At, Accent));
                            if (bone.Name.StartsWith("Tip", StringComparison.Ordinal) || bone.Name.StartsWith("Claw", StringComparison.Ordinal))
                            {
                                lines.Add(new DebugLine(pose[i].At, pose[i].At + Vector3.Transform(bone.End - bone.Bind, pose[i].Rotation), White));
                            }
                        }
                    }
                    var overhead = clip.Id == "cleave";
                    var center = new Vector3(0f, overhead ? 1.10f : 0.83f, advance + 0.14f);
                    var side = clip.Id is "sweep" or "foreleg-thrust" ? -1f : 1f;
                    var eye = center + (clip.Walk ? new Vector3(2.5f, 0.85f, 1.75f) : new Vector3(side * 2.45f, 0.92f, 2.0f));
                    var fov = MathF.PI / (overhead ? 3.4f : 4f);
                    var vp = Camera.ViewProjection(eye, center, Vector3.UnitY, fov, (float)width / height, 0.02f, 25f);
                    ScreenTextDraw[] caption =
                    [
                        new($"GALVA     {c + 1:00} / {clips.Length:00}", new Vector2(30f, 34f), 22f, Accent, TextAnchor.Left),
                        new(clip.Title, new Vector2(30f, 69f), 30f, White, TextAnchor.Left),
                    ];
                    var rgba = renderer.RenderPrimitiveScene(Background, vp, [], lines, caption, lighting: SceneLighting.Default,
                        cameraPosition: eye, voxels: bones ? [] : draws);
                    encoder.StandardInput.BaseStream.Write(rgba);
                    if (frame == (int)(frames * (clip.Walk ? 0.28f : 0.50f)))
                    {
                        PngWriter.WriteRgba(Path.Combine(outDir, clip.Id + "-frame.png"), width, height, rgba);
                    }
                }
                Console.WriteLine(clip.Title);
            }
        }
        finally
        {
            encoder.StandardInput.Close();
            encoder.WaitForExit();
        }
        if (encoder.ExitCode != 0)
        {
            throw new IOException($"ffmpeg exited with code {encoder.ExitCode}.");
        }
    }
}
