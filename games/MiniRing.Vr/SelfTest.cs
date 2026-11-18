using System.Numerics;
using Aigis.Foundation;
using Aigis.Render;
using Aigis.Render.Voxels;
using System.Diagnostics;
using Silk.NET.Vulkan;

namespace MiniRing.Vr;

/// <summary>
/// HMD なしで VR 描画経路を確かめる（<c>miniring-vr --selftest</c>）。
/// XR ランタイムの代わりに自前で Vulkan デバイスと R8G8B8A8_SRGB 画像（= XR スワップチェーン画像の代役）を作り、
/// 外部デバイスモードの HeadlessRenderer でそこへ描いて読み戻す。通常の HeadlessRenderer の出力と
/// バイト単位で一致すれば、外部デバイスでの初期化・GPU コピー・レイアウト遷移が正しい。
/// </summary>
internal static unsafe class SelfTest
{
    private const uint Width = 640;
    private const uint Height = 360;

    /// <summary>Measure the real two-eye external-device render path without an OpenXR runtime or VSync.</summary>
    public static int Profile(bool katana, string outputDirectory)
    {
        var corners = katana ? BoxingCorners.Default with
        {
            RedBotStyle = Bots.BotCatalog.Boxer,
            PlayerWeapon = Weapons.WeaponKind.Katana,
            CpuWeapon = Weapons.WeaponKind.Katana,
            PlayerTwoHanded = true,
            CpuTwoHanded = true,
            UseWeaponBots = true,
        } : BoxingCorners.Default;
        using var game = new BoxingGame(seed: 3, corners: corners);
        var capture = new VrSceneCapture();
        var scene = capture.Capture(game);
        using var context = VulkanContext.CreateInstance([]);
        var vk = Vk.GetApi();
        uint count = 0;
        CheckProfile(vk.EnumeratePhysicalDevices(context.Instance, ref count, null));
        if (count == 0) { throw new InvalidOperationException("VR profiling requires a Vulkan GPU."); }
        var devices = new PhysicalDevice[count];
        fixed (PhysicalDevice* p = devices) { CheckProfile(vk.EnumeratePhysicalDevices(context.Instance, ref count, p)); }
        context.CreateDevice(devices[0], []);
        using var leftTarget = new SrgbTarget(vk, context);
        using var rightTarget = new SrgbTarget(vk, context);
        using var renderer = new HeadlessRenderer(Width, Height, context.External);
        using var simulation = new VrSimulation(scene, (_, dt) =>
        {
            game.Step(default, dt);
            return capture.Capture(game);
        });
        var consumer = new VoxelSnapshotConsumer();
        var eyes = new[] { new Vector3(-0.032f, 1.55f, 0.05f), new Vector3(0.032f, 1.55f, 0.05f) };
        var targets = new[] { leftTarget, rightTarget };
        var aim = game.Layout.Center with { Y = BoxingGame.RingSurfaceHeight + 0.15f };
        var views = eyes.Select(eye => Camera.OffCenterViewProjection(Matrix4x4.CreateLookAt(eye, aim, Vector3.UnitY),
            new FrustumWindow(-0.6f, 0.6f, -0.34f, 0.34f), 0.02f, 50f)).ToArray();
        var work = new List<double>();
        var timer = Stopwatch.StartNew();
        var frame = 0;
        simulation.Submit(default, 1f / 90f);
        while (timer.Elapsed.TotalSeconds < 12d || frame < 40)
        {
            if (frame == 30) { timer.Restart(); }
            var start = Stopwatch.GetTimestamp();
            scene = simulation.Latest;
            var voxels = consumer.Resolve(scene.VoxelSnapshot!);
            for (var eye = 0; eye < 2; eye++)
            {
                renderer.RenderPrimitiveSceneToImage(targets[eye].Image.Handle, ArenaLook.Background, views[eye],
                    scene.Instances, scene.Lines, scene.Lighting, scene.Shadow, eyes[eye], voxels, scene.Particles,
                    ArenaLook.Finish(vignette: 0f));
            }
            _ = simulation.DrainHaptics();
            var milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (frame++ >= 30) { work.Add(milliseconds); }
        }
        work.Sort();
        double Percentile(double fraction) => work[Math.Clamp((int)Math.Ceiling(work.Count * fraction) - 1, 0, work.Count - 1)];
        var report = $"[vr headless profile] {(katana ? "katana" : "normal")}, two eyes {Width}x{Height}, no VSync, "
            + $"frames {work.Count}, fps {1000d / work.Average():F1}, render p95 {Percentile(0.95):F2} ms, "
            + $"p99 {Percentile(0.99):F2} ms, max {work[^1]:F2} ms, physics max {simulation.MaximumStepMilliseconds:F2} ms, steps {simulation.CompletedSteps}";
        Console.WriteLine(report);
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(Path.Combine(outputDirectory, katana ? "vr_profile_katana.txt" : "vr_profile_normal.txt"), report);
        for (var eye = 0; eye < 2; eye++)
        {
            PngWriter.WriteRgba(Path.Combine(outputDirectory, $"vr_profile_{(katana ? "katana" : "normal")}_eye{eye}.png"),
                (int)Width, (int)Height, targets[eye].ReadBack());
        }
        return 0;
    }

    private static void CheckProfile(Result result)
    {
        if (result != Result.Success) { throw new InvalidOperationException($"Vulkan profiling call failed: {result}"); }
    }

    /// <summary>
    /// HMD なしで一人称（<c>--first-person</c>）の目の描画を確かめる（<c>--first-person-selftest</c>）: 稽古の世界（katana、正眼の相手）で、
    /// 止まった HMD と両手（グリップを押して構える）を写像（①: 右のコントローラが両手）へ数秒流し、身長の合わせで決めた変換を掛けた目の視点から 2 枚描く
    /// （自分の頭を描かない = 実際の描画 / 描く = 比較用）。<c>.visualcheck/vr_first_person.png</c>・<c>vr_first_person_headvisible.png</c>。
    /// </summary>
    public static int FirstPerson(string outputDirectory)
    {
        var tuning = FighterTuning.Default with { PlayerFatigue = false };
        using var game = new BoxingGame(tuning, corners: MiniRing.Motion.SwordTraining.Corners(Weapons.WeaponKind.Katana, 0.9f)) { HidePlayerHead = true };
        var mapper = new MiniRing.Motion.VrPlayerMapper(weaponMode: true)
        {
            GripHolds = true,
            TwoHand = MiniRing.Motion.VrTwoHand.Right,
            FirstPerson = true,
            HeldWeapon = Weapons.WeaponKind.Katana,
        };
        // 人の寸法（VR の空間: 頭は −Z を向く）。右手は胸の前、刀は前を向く（コントローラの前 = −Z）。左手は体の横（右の手が決めるので位置は使われない）。
        var head = new Vector3(0f, 1.62f, 0f);
        var raw = new MiniRing.Motion.VrRawFrame(head, Quaternion.Identity,
            new Vector3(-0.15f, 1.1f, -0.2f), new Vector3(0.15f, 1.25f, -0.4f), true, true,
            Vector2.Zero, Vector2.Zero, 1f, 1f, false, true, Quaternion.Identity, Quaternion.Identity);
        const float dt = 1f / 240f;
        for (var i = 0; i < 480; i++)
        {
            game.Step(mapper.Read(raw, game.PlayerSense, dt), dt);
        }

        var placement = mapper.Placement ?? throw new InvalidOperationException("The first-person placement was not set by the calibration frame.");
        var eyePosition = head;
        Matrix4x4.Invert(Matrix4x4.CreateFromQuaternion(Quaternion.Identity) * Matrix4x4.CreateTranslation(eyePosition), out var vrView);
        var view = placement.View(vrView);
        var worldEye = placement.Point(eyePosition);
        var window = new FrustumWindow(-1.0f, 1.0f, -0.563f, 0.563f);
        var viewProjection = Camera.OffCenterViewProjection(view, window, 0.02f * placement.Scale, 50f);
        Directory.CreateDirectory(outputDirectory);
        Console.WriteLine($"[first-person selftest] scale {placement.Scale:0.000}, puppet head {game.PlayerSense.SelfHead}, camera {worldEye}, hmd target offset {(worldEye - game.PlayerSense.SelfHead).Length() / game.PlayerSense.ArmReach:0.00} arm reaches");
        foreach (var (hide, name) in new[] { (true, "vr_first_person.png"), (false, "vr_first_person_headvisible.png") })
        {
            game.HidePlayerHead = hide;
            var (instances, lines) = game.BuildRenderables();
            var voxels = game.BuildVoxelVolumes();
            using var reference = new HeadlessRenderer(960, 540);
            var pixels = reference.RenderPrimitiveScene(ArenaLook.Background, viewProjection, instances, lines, particles: game.BuildParticles(),
                lighting: game.BuildLighting(), shadow: game.BuildShadow(), cameraPosition: worldEye, voxels: voxels);
            PngWriter.WriteRgba(Path.Combine(outputDirectory, name), 960, 540, pixels);
        }

        return 0;
    }

    public static int Run(string outputDirectory)
    {
        using var game = new BoxingGame(seed: 3);
        for (var i = 0; i < 60; i++)
        {
            game.Step(new BoxingInput(default), 1f / 60f);
        }

        var (instances, lines) = game.BuildRenderables();
        var voxels = game.BuildVoxelVolumes();
        var eye = new Vector3(0f, 1.55f, 0.05f);
        var target = BoxingGame.RingCenter with { Y = BoxingGame.RingSurfaceHeight + 0.15f };
        var view = Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY);
        var window = new FrustumWindow(-0.6f, 0.6f, -0.34f, 0.34f);
        var viewProjection = Camera.OffCenterViewProjection(view, window, 0.02f, 50f);
        var clear = new Vector4(0.10f, 0.11f, 0.14f, 1f);

        byte[] expected;
        using (var reference = new HeadlessRenderer(Width, Height))
        {
            expected = reference.RenderPrimitiveScene(clear, viewProjection, instances, lines, cameraPosition: eye, voxels: voxels);
        }

        using var context = VulkanContext.CreateInstance([]);
        var vk = Vk.GetApi();
        uint count = 0;
        vk.EnumeratePhysicalDevices(context.Instance, ref count, null);
        var devices = new PhysicalDevice[count];
        fixed (PhysicalDevice* p = devices)
        {
            vk.EnumeratePhysicalDevices(context.Instance, ref count, p);
        }

        context.CreateDevice(devices[0], []);
        byte[] actual;
        using (var target2 = new SrgbTarget(vk, context))
        using (var renderer = new HeadlessRenderer(Width, Height, context.External))
        {
            renderer.RenderPrimitiveSceneToImage(target2.Image.Handle, clear, viewProjection, instances, lines, cameraPosition: eye, voxels: voxels);
            actual = target2.ReadBack();
        }

        Directory.CreateDirectory(outputDirectory);
        PngWriter.WriteRgba(Path.Combine(outputDirectory, "selftest_reference.png"), (int)Width, (int)Height, expected);
        PngWriter.WriteRgba(Path.Combine(outputDirectory, "selftest_xr_target.png"), (int)Width, (int)Height, actual);
        var differing = expected.Zip(actual).Count(pair => pair.First != pair.Second);
        Console.WriteLine($"[selftest] {differing} of {expected.Length} bytes differ (written to {outputDirectory})");
        return differing == 0 ? 0 : 2;
    }

    /// <summary>XR スワップチェーン画像の代役: SRGB・COLOR_ATTACHMENT_OPTIMAL で渡し、描画後に CPU へ読み戻す。</summary>
    private sealed class SrgbTarget : IDisposable
    {
        private readonly Vk _vk;
        private readonly VulkanContext _context;
        private readonly DeviceMemory _memory;
        private readonly Silk.NET.Vulkan.Buffer _buffer;
        private readonly DeviceMemory _bufferMemory;
        private readonly CommandPool _pool;
        private readonly Queue _queue;

        public SrgbTarget(Vk vk, VulkanContext context)
        {
            _vk = vk;
            _context = context;
            var device = context.Device;
            var info = new ImageCreateInfo
            {
                SType = StructureType.ImageCreateInfo,
                ImageType = ImageType.Type2D,
                Format = Format.R8G8B8A8Srgb,
                Extent = new Extent3D(Width, Height, 1),
                MipLevels = 1,
                ArrayLayers = 1,
                Samples = SampleCountFlags.Count1Bit,
                Tiling = ImageTiling.Optimal,
                Usage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferDstBit | ImageUsageFlags.TransferSrcBit,
                InitialLayout = ImageLayout.Undefined,
            };
            Check(vk.CreateImage(device, in info, null, out var image));
            Image = image;
            vk.GetImageMemoryRequirements(device, image, out var requirements);
            _memory = Allocate(requirements, MemoryPropertyFlags.DeviceLocalBit);
            Check(vk.BindImageMemory(device, image, _memory, 0));

            var bufferInfo = new BufferCreateInfo { SType = StructureType.BufferCreateInfo, Size = Width * Height * 4, Usage = BufferUsageFlags.TransferDstBit };
            Check(vk.CreateBuffer(device, in bufferInfo, null, out _buffer));
            vk.GetBufferMemoryRequirements(device, _buffer, out var bufferRequirements);
            _bufferMemory = Allocate(bufferRequirements, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
            Check(vk.BindBufferMemory(device, _buffer, _bufferMemory, 0));

            var poolInfo = new CommandPoolCreateInfo { SType = StructureType.CommandPoolCreateInfo, QueueFamilyIndex = context.QueueFamily };
            Check(vk.CreateCommandPool(device, in poolInfo, null, out _pool));
            vk.GetDeviceQueue(device, context.QueueFamily, 0, out _queue);

            // OpenXR が acquire 時に渡すのと同じ COLOR_ATTACHMENT_OPTIMAL にしておく。
            Submit(cmd => Barrier(cmd, ImageLayout.Undefined, ImageLayout.ColorAttachmentOptimal));
        }

        public Image Image { get; }

        public byte[] ReadBack()
        {
            Submit(cmd =>
            {
                Barrier(cmd, ImageLayout.ColorAttachmentOptimal, ImageLayout.TransferSrcOptimal);
                var region = new BufferImageCopy
                {
                    ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
                    ImageExtent = new Extent3D(Width, Height, 1),
                };
                _vk.CmdCopyImageToBuffer(cmd, Image, ImageLayout.TransferSrcOptimal, _buffer, 1, in region);
            });
            void* mapped = null;
            Check(_vk.MapMemory(_context.Device, _bufferMemory, 0, Width * Height * 4, 0, ref mapped));
            var pixels = new Span<byte>(mapped, (int)(Width * Height * 4)).ToArray();
            _vk.UnmapMemory(_context.Device, _bufferMemory);
            return pixels;
        }

        private void Barrier(CommandBuffer cmd, ImageLayout from, ImageLayout to)
        {
            var barrier = new ImageMemoryBarrier
            {
                SType = StructureType.ImageMemoryBarrier,
                SrcAccessMask = AccessFlags.MemoryWriteBit,
                DstAccessMask = AccessFlags.MemoryReadBit | AccessFlags.MemoryWriteBit,
                OldLayout = from,
                NewLayout = to,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                Image = Image,
                SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
            };
            _vk.CmdPipelineBarrier(cmd, PipelineStageFlags.AllCommandsBit, PipelineStageFlags.AllCommandsBit, 0, 0, null, 0, null, 1, in barrier);
        }

        private void Submit(Action<CommandBuffer> record)
        {
            var alloc = new CommandBufferAllocateInfo { SType = StructureType.CommandBufferAllocateInfo, CommandPool = _pool, Level = CommandBufferLevel.Primary, CommandBufferCount = 1 };
            Check(_vk.AllocateCommandBuffers(_context.Device, in alloc, out var cmd));
            var begin = new CommandBufferBeginInfo { SType = StructureType.CommandBufferBeginInfo, Flags = CommandBufferUsageFlags.OneTimeSubmitBit };
            Check(_vk.BeginCommandBuffer(cmd, in begin));
            record(cmd);
            Check(_vk.EndCommandBuffer(cmd));
            var submit = new SubmitInfo { SType = StructureType.SubmitInfo, CommandBufferCount = 1, PCommandBuffers = &cmd };
            Check(_vk.QueueSubmit(_queue, 1, in submit, default));
            Check(_vk.QueueWaitIdle(_queue));
            _vk.FreeCommandBuffers(_context.Device, _pool, 1, in cmd);
        }

        private DeviceMemory Allocate(MemoryRequirements requirements, MemoryPropertyFlags flags)
        {
            _vk.GetPhysicalDeviceMemoryProperties(_context.PhysicalDevice, out var properties);
            for (var i = 0; i < properties.MemoryTypeCount; i++)
            {
                if ((requirements.MemoryTypeBits & (1u << i)) != 0 && (properties.MemoryTypes[i].PropertyFlags & flags) == flags)
                {
                    var info = new MemoryAllocateInfo { SType = StructureType.MemoryAllocateInfo, AllocationSize = requirements.Size, MemoryTypeIndex = (uint)i };
                    Check(_vk.AllocateMemory(_context.Device, in info, null, out var memory));
                    return memory;
                }
            }

            throw new InvalidOperationException("No suitable memory type.");
        }

        private static void Check(Result result)
        {
            if (result != Result.Success)
            {
                throw new InvalidOperationException($"Vulkan call failed: {result}");
            }
        }

        public void Dispose()
        {
            var device = _context.Device;
            _vk.DeviceWaitIdle(device);
            _vk.DestroyCommandPool(device, _pool, null);
            _vk.DestroyBuffer(device, _buffer, null);
            _vk.FreeMemory(device, _bufferMemory, null);
            _vk.DestroyImage(device, Image, null);
            _vk.FreeMemory(device, _memory, null);
        }
    }
}
