using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Aigis.Physics;
using Aigis.Render;
using Aigis.Render.Voxels;
using MiniRing.Weapons;

namespace MiniRing.Tests;

public sealed partial class SoloPunchSpeedProbe
{
    private sealed class SwingVideo(string path, Fighter fighter, IReadOnlyList<Weapon> gloves) : IDisposable
    {
        private const int Width = 1280;
        private const int Height = 720;
        private static readonly Vector4 Gold = new(1f, 0.79f, 0.38f, 1f);
        private static readonly Vector3 Center = new(0f, 1.02f, 0.55f);
        private static readonly PrimitiveInstance Floor = new(PrimitiveKind.Cube,
            Matrix4x4.CreateScale(9f, 0.04f, 9f) * Matrix4x4.CreateTranslation(0f, -0.02f, 0f),
            Vector3.Zero, new Vector4(0.3f, 0.36f, 0.42f, 1f));
        private readonly HeadlessRenderer _renderer = new(Width, Height);
        private readonly Process _encoder = OpenEncoder(path);
        private static readonly Vector4 HipColor = new(0.38f, 0.9f, 1f, 1f);

        private static Process OpenEncoder(string path)
        {
            var start = new ProcessStartInfo("ffmpeg") { RedirectStandardInput = true, UseShellExecute = false };
            string[] arguments = ["-y", "-loglevel", "error", "-f", "rawvideo", "-pix_fmt", "rgba",
                "-s", "1280x720", "-r", "30", "-i", "-", "-c:v", "libx264", "-pix_fmt", "yuv420p",
                "-crf", "17", "-movflags", "+faststart", path];
            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            return Process.Start(start) ?? throw new IOException("Failed to start ffmpeg.");
        }

        public void Write(PhysicsWorld world, Vector3 aim, float time, float speed, float peak, float pelvisYaw, float chestYaw)
        {
            var instances = new List<PrimitiveInstance> { Floor };
            var voxels = new List<VoxelVolumeDraw>();
            voxels.AddRange(fighter.Voxels!.Layers.Select(part => part.Draw()));
            foreach (var glove in gloves)
            {
                glove.AddDraws(voxels);
            }

            DebugLine Axis(int part, float degrees, Vector4 color)
            {
                var origin = world.GetState(part).Position;
                var radians = degrees * MathF.PI / 180f;
                var direction = new Vector3(MathF.Sin(radians), 0f, MathF.Cos(radians));
                return new DebugLine(origin, origin + direction * 0.48f, color);
            }

            DebugLine[] axes = [Axis(fighter.Parts.Pelvis, pelvisYaw, HipColor), Axis(fighter.Parts.Chest, chestYaw, Gold)];
            instances.Add(new PrimitiveInstance(PrimitiveKind.Sphere, Matrix4x4.CreateTranslation(aim),
                new Vector3(0.014f, 0f, 0f), Gold, MaterialKind.XRayFill));
            var status = time < 0f ? "溜め" : "打ち出し";
            ScreenTextDraw[] labels =
            [
                new("右ストレート   対戦相手なし", new Vector2(44f, 44f), 32f, Vector4.One, TextAnchor.Left),
                new(string.Create(CultureInfo.InvariantCulture, $"拳速 {speed:F2} m/s    最高 {peak:F2} m/s"), new Vector2(44f, 92f), 26f, Gold, TextAnchor.Left),
                new(string.Create(CultureInfo.InvariantCulture, $"腰 {pelvisYaw:+0.0;-0.0;0.0}°"), new Vector2(44f, 137f), 24f, HipColor, TextAnchor.Left),
                new(string.Create(CultureInfo.InvariantCulture, $"肩 {chestYaw:+0.0;-0.0;0.0}°"), new Vector2(205f, 137f), 24f, Gold, TextAnchor.Left),
                new(string.Create(CultureInfo.InvariantCulture, $"{status}  {time:+0.000;-0.000;0.000} 秒   |   1/8 スロー"), new Vector2(44f, 662f), 22f, Vector4.One, TextAnchor.Left),
            ];
            var eye = Center + new Vector3(-2.6f, 2.05f, 1.3f);
            var vp = Camera.ViewProjection(eye,
                Center, Vector3.UnitY, MathF.PI / 4f, (float)Width / Height, 0.02f, 30f);
            var bytes = _renderer.RenderPrimitiveScene(ArenaLook.Background, vp, instances, axes, labels,
                lighting: ArenaLook.Lighting(Vector3.Zero, 4f, 0f), cameraPosition: eye, voxels: voxels);
            _encoder.StandardInput.BaseStream.Write(bytes);
        }

        public void Dispose()
        {
            try
            {
                _encoder.StandardInput.Close();
                _encoder.WaitForExit();
                if (_encoder.ExitCode != 0)
                {
                    throw new IOException($"ffmpeg exited with code {_encoder.ExitCode}.");
                }
            }
            finally
            {
                _encoder.Dispose();
                _renderer.Dispose();
            }
        }
    }
}
