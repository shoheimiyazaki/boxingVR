using System.Numerics;
using System.Text;
using Silk.NET.Core.Native;
using Silk.NET.OpenXR;
using Silk.NET.OpenXR.Extensions.KHR;
using Vk = Silk.NET.Vulkan;

namespace MiniRing.Vr;

/// <summary>1 フレーム分のトラッキング（ワールド座標）。</summary>
internal readonly record struct XrInputFrame(
    Vector3 HeadPosition, Quaternion HeadOrientation,
    Vector3 LeftHand, Vector3 RightHand, bool LeftTracked, bool RightTracked,
    Vector2 LeftStick, Vector2 RightStick, float LeftGrip, float RightGrip,
    bool CalibratePressed, bool Focused);

/// <summary>1 つの目の描画指示。</summary>
internal readonly record struct XrEyeView(int Eye, Matrix4x4 View, Aigis.Render.FrustumWindow Fov, Vector3 Position, ulong TargetImage);

/// <summary>
/// OpenXR セッション（iTVision 3 の VulkanRenderer.Vr / VrAttach / VrInput から、描画と HUD を外して移植）。
/// <list type="bullet">
/// <item>XR_KHR_vulkan_enable（v1）: ランタイムが求める拡張でアプリが Vulkan を作る（<see cref="VulkanContext"/>）。</item>
/// <item>Stage 空間（床 y=0）。初回の頭の向きで空間を作り直し、前方を固定する（iTVision RecenterAppSpace）。</item>
/// <item>目ごとのスワップチェーン（R8G8B8A8_SRGB 優先）。描画は呼び出し側が <see cref="XrEyeView.TargetImage"/> へ書く。</item>
/// <item>操作: grip 姿勢・スティック・squeeze・A/X・振動（iTVision に無かった A/X と振動を追加）。</item>
/// </list>
/// <para><b>座標系</b>: OpenXR は −Z 前方。ゲームは +Z 前方（プレイヤーはリング＝+Z を向いて立つ）なので、
/// XR 空間 → ワールドは Y 軸 180° 回転（<see cref="WorldFromXr"/>）。</para>
/// </summary>
internal sealed unsafe class XrSession : IDisposable
{
    private const long FormatR8G8B8A8Srgb = 43;   // VK_FORMAT_R8G8B8A8_SRGB
    private const long FormatR8G8B8A8Unorm = 37;  // VK_FORMAT_R8G8B8A8_UNORM

    private static readonly Quaternion WorldFromXr = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI);

    private readonly XR _xr = XR.GetApi();
    private readonly Swapchain[] _swapchains = new Swapchain[2];
    private readonly ulong[][] _images = new ulong[2][];
    private readonly ulong[] _handPaths = new ulong[2];
    private readonly Space[] _handSpaces = new Space[2];
    private Silk.NET.OpenXR.Instance _instance;
    private Session _session;
    private Space _appSpace;
    private ReferenceSpaceType _appSpaceType;
    private ulong _systemId;
    private KhrVulkanEnable _vkEnable = null!;
    private ActionSet _actionSet;
    private Silk.NET.OpenXR.Action _poseAction;
    private Silk.NET.OpenXR.Action _squeezeAction;
    private Silk.NET.OpenXR.Action _stickAction;
    private Silk.NET.OpenXR.Action _calibrateAction;
    private Silk.NET.OpenXR.Action _hapticAction;
    private SessionState _state = SessionState.Unknown;
    private bool _recenterPending = true;
    private bool _exitRequestedByApp;
    private long _lastDisplayTime;

    private XrSession()
    {
    }

    public VulkanContext Vulkan { get; private set; } = null!;
    public uint EyeWidth { get; private set; }
    public uint EyeHeight { get; private set; }
    public bool IsSrgb { get; private set; }
    public bool Running { get; private set; }
    public bool ExitRequested { get; private set; }
    public float DisplayHz { get; private set; } = 90f;
    public string StateName => _state.ToString();

    /// <summary>ランタイム接続 → Vulkan 作成 → セッション・空間・操作・スワップチェーン。失敗は理由付き例外。</summary>
    public static XrSession Create(float renderScale)
    {
        var s = new XrSession();
        try
        {
            s.CreateInstance();
            s.GetSystem();
            if (!s._xr.TryGetInstanceExtension(null, s._instance, out s._vkEnable))
            {
                throw new InvalidOperationException("XR_KHR_vulkan_enable is unavailable.");
            }

            var requirements = new GraphicsRequirementsVulkanKHR { Type = StructureType.GraphicsRequirementsVulkanKhr };
            Check(s._vkEnable.GetVulkanGraphicsRequirements(s._instance, s._systemId, &requirements), "xrGetVulkanGraphicsRequirementsKHR");
            Console.WriteLine($"[xr] runtime Vulkan API range {Version(requirements.MinApiVersionSupported)} .. {Version(requirements.MaxApiVersionSupported)} (we use 1.1)");

            s.Vulkan = VulkanContext.CreateInstance(s.VulkanExtensions(device: false));
            VkHandle physical;
            Check(s._vkEnable.GetVulkanGraphicsDevice(s._instance, s._systemId, new VkHandle(s.Vulkan.Instance.Handle), &physical), "xrGetVulkanGraphicsDeviceKHR");
            s.Vulkan.CreateDevice(new Vk.PhysicalDevice(physical.Handle), s.VulkanExtensions(device: true));

            s.CreateSession();
            s.CreateAppSpace();
            s.CreateActions();
            s.CreateSwapchains(renderScale);
            return s;
        }
        catch
        {
            s.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 1 フレーム: イベント処理 → xrWaitFrame → xrBeginFrame →（描画すべきなら）入力取得・各目の描画 → xrEndFrame。
    /// <paramref name="update"/> は入力と経過時間でゲームを進め、<paramref name="renderEye"/> は目ごとに画像へ描く。
    /// <para>BeginFrame の後で何が起きても（例外・無効な姿勢）、取得した画像は解放し EndFrame は必ず呼ぶ
    /// （フレームの開始と終了が対にならないとランタイムが止まる）。</para>
    /// </summary>
    public void RunFrame(Action<XrInputFrame, float> update, Action<XrEyeView> renderEye)
    {
        PollEvents();
        if (!Running)
        {
            Thread.Sleep(10);
            return;
        }

        var waitInfo = new FrameWaitInfo { Type = StructureType.FrameWaitInfo };
        var frameState = new FrameState { Type = StructureType.FrameState };
        Check(_xr.WaitFrame(_session, &waitInfo, &frameState), "xrWaitFrame");
        if (frameState.PredictedDisplayPeriod > 0)
        {
            DisplayHz = 1e9f / frameState.PredictedDisplayPeriod;
        }

        // ゲーム時間は表示時刻の差分で進める（ASW 等で 45Hz に落ちても実時間どおり）。初回・異常値は 1 周期で代用。
        var dt = _lastDisplayTime > 0 ? (frameState.PredictedDisplayTime - _lastDisplayTime) * 1e-9f : 0f;
        _lastDisplayTime = frameState.PredictedDisplayTime;
        dt = dt is > 0f and < 1f ? Math.Clamp(dt, 1f / 240f, 1f / 30f) : 1f / MathF.Max(30f, DisplayHz);

        var beginInfo = new FrameBeginInfo { Type = StructureType.FrameBeginInfo };
        Check(_xr.BeginFrame(_session, &beginInfo), "xrBeginFrame"); // XR_FRAME_DISCARDED も成功扱い

        var projectionViews = stackalloc CompositionLayerProjectionView[2];
        var rendered = false;
        try
        {
            if (frameState.ShouldRender != 0)
            {
                rendered = RenderViews(frameState.PredictedDisplayTime, dt, update, renderEye, projectionViews);
            }
        }
        finally
        {
            var layer = new CompositionLayerProjection
            {
                Type = StructureType.CompositionLayerProjection,
                Space = _appSpace,
                ViewCount = 2,
                Views = projectionViews,
            };
            var layerPointer = (CompositionLayerBaseHeader*)&layer;
            var endInfo = new FrameEndInfo
            {
                Type = StructureType.FrameEndInfo,
                DisplayTime = frameState.PredictedDisplayTime,
                EnvironmentBlendMode = EnvironmentBlendMode.Opaque,
                LayerCount = rendered ? 1u : 0u,
                Layers = rendered ? &layerPointer : null,
            };
            Check(_xr.EndFrame(_session, &endInfo), "xrEndFrame");
        }
    }

    /// <summary>ランタイムにセッション終了を頼む。STOPPING → EndSession → EXITING はイベントで進む（<see cref="PollEvents"/>）。</summary>
    public void RequestExit()
    {
        if (Running && !_exitRequestedByApp)
        {
            _exitRequestedByApp = true;
            _ = _xr.RequestExitSession(_session);
        }
    }

    private bool RenderViews(long displayTime, float dt, Action<XrInputFrame, float> update, Action<XrEyeView> renderEye, CompositionLayerProjectionView* projectionViews)
    {
        var views = stackalloc View[2];
        views[0].Type = StructureType.View;
        views[1].Type = StructureType.View;
        var locate = new ViewLocateInfo
        {
            Type = StructureType.ViewLocateInfo,
            ViewConfigurationType = ViewConfigurationType.PrimaryStereo,
            DisplayTime = displayTime,
            Space = _appSpace,
        };
        var viewState = new ViewState { Type = StructureType.ViewState };
        uint viewCount = 0;
        Check(_xr.LocateView(_session, &locate, &viewState, 2, &viewCount, views), "xrLocateViews");

        // 起動直後・トラッキング喪失時は姿勢や FOV が無効なことがある。その間は何も出さない（0 レイヤー）。
        const ViewStateFlags valid = ViewStateFlags.OrientationValidBit | ViewStateFlags.PositionValidBit;
        if ((viewState.ViewStateFlags & valid) != valid || viewCount < 2)
        {
            return false;
        }

        var headXr = Vector3.Lerp(ToVector(views[0].Pose.Position), ToVector(views[1].Pose.Position), 0.5f);
        var headRotXr = Quaternion.Slerp(ToQuaternion(views[0].Pose.Orientation), ToQuaternion(views[1].Pose.Orientation), 0.5f);
        if (_recenterPending)
        {
            // 空間を作り直したフレームは、古い空間で求めた姿勢と新しい空間が混ざるので丸ごと捨てる。
            Recenter(headXr, headRotXr);
            return false;
        }

        var windows = new Aigis.Render.FrustumWindow[2];
        for (var eye = 0; eye < 2; eye++)
        {
            var fov = views[eye].Fov;
            windows[eye] = new Aigis.Render.FrustumWindow(MathF.Tan(fov.AngleLeft), MathF.Tan(fov.AngleRight), MathF.Tan(fov.AngleDown), MathF.Tan(fov.AngleUp));
            if (!windows[eye].IsValid)
            {
                return false;
            }
        }

        update(ReadInput(displayTime, headXr, headRotXr), dt);

        for (var eye = 0; eye < 2; eye++)
        {
            uint index = 0;
            var acquire = new SwapchainImageAcquireInfo { Type = StructureType.SwapchainImageAcquireInfo };
            Check(_xr.AcquireSwapchainImage(_swapchains[eye], &acquire, &index), "xrAcquireSwapchainImage");
            try
            {
                var wait = new SwapchainImageWaitInfo { Type = StructureType.SwapchainImageWaitInfo, Timeout = long.MaxValue };
                Check(_xr.WaitSwapchainImage(_swapchains[eye], &wait), "xrWaitSwapchainImage");
                var pose = views[eye].Pose;
                var position = ToWorld(ToVector(pose.Position));
                var orientation = Quaternion.Concatenate(ToQuaternion(pose.Orientation), WorldFromXr);
                Matrix4x4.Invert(Matrix4x4.CreateFromQuaternion(orientation) * Matrix4x4.CreateTranslation(position), out var view);
                renderEye(new XrEyeView(eye, view, windows[eye], position, _images[eye][index]));
            }
            finally
            {
                // 取得した画像は例外時も必ず返す（返さないと次の Acquire が失敗し続ける）。
                var release = new SwapchainImageReleaseInfo { Type = StructureType.SwapchainImageReleaseInfo };
                _ = _xr.ReleaseSwapchainImage(_swapchains[eye], &release);
            }

            projectionViews[eye] = new CompositionLayerProjectionView
            {
                Type = StructureType.CompositionLayerProjectionView,
                Pose = views[eye].Pose,
                Fov = views[eye].Fov,
                SubImage = new SwapchainSubImage
                {
                    Swapchain = _swapchains[eye],
                    ImageArrayIndex = 0,
                    ImageRect = new Rect2Di(new Offset2Di(0, 0), new Extent2Di((int)EyeWidth, (int)EyeHeight)),
                },
            };
        }

        return true;
    }

    /// <summary>コントローラを振動させる（失敗しても無害）。</summary>
    public void Haptic(Hand hand, float amplitude, float seconds)
    {
        if (!Running)
        {
            return;
        }

        var info = new HapticActionInfo { Type = StructureType.HapticActionInfo, Action = _hapticAction, SubactionPath = _handPaths[(int)hand] };
        var vibration = new HapticVibration
        {
            Type = StructureType.HapticVibration,
            Amplitude = Math.Clamp(amplitude, 0f, 1f),
            Duration = (long)(seconds * 1e9),
            Frequency = 0f, // ランタイム既定
        };
        _ = _xr.ApplyHapticFeedback(_session, &info, (HapticBaseHeader*)&vibration);
    }

    private XrInputFrame ReadInput(long displayTime, Vector3 headXr, Quaternion headRotXr)
    {
        var active = new ActiveActionSet { ActionSet = _actionSet, SubactionPath = 0 };
        var sync = new ActionsSyncInfo { Type = StructureType.ActionsSyncInfo, CountActiveActionSets = 1, ActiveActionSets = &active };
        // 非 Success = 入力フォーカス無し（iTVision の知見: そのまま掴みを解除させる）。
        var focused = _xr.SyncAction(_session, &sync) == Result.Success;

        var hands = new Vector3[2];
        var tracked = new bool[2];
        var sticks = new Vector2[2];
        var grips = new float[2];
        var calibrate = false;
        for (var h = 0; h < 2; h++)
        {
            var location = new SpaceLocation { Type = StructureType.SpaceLocation };
            _ = _xr.LocateSpace(_handSpaces[h], _appSpace, displayTime, &location);
            const SpaceLocationFlags valid = SpaceLocationFlags.PositionValidBit | SpaceLocationFlags.OrientationValidBit;
            tracked[h] = focused && (location.LocationFlags & valid) == valid;
            hands[h] = ToWorld(ToVector(location.Pose.Position));

            var get = new ActionStateGetInfo { Type = StructureType.ActionStateGetInfo, SubactionPath = _handPaths[h] };
            get.Action = _squeezeAction;
            var squeeze = new ActionStateFloat { Type = StructureType.ActionStateFloat };
            grips[h] = focused && _xr.GetActionStateFloat(_session, &get, &squeeze) == Result.Success && squeeze.IsActive != 0 ? squeeze.CurrentState : 0f;

            get.Action = _stickAction;
            var stick = new ActionStateVector2f { Type = StructureType.ActionStateVector2f };
            sticks[h] = focused && _xr.GetActionStateVector2(_session, &get, &stick) == Result.Success && stick.IsActive != 0
                ? new Vector2(stick.CurrentState.X, stick.CurrentState.Y)
                : Vector2.Zero;

            get.Action = _calibrateAction;
            var button = new ActionStateBoolean { Type = StructureType.ActionStateBoolean };
            calibrate |= focused && _xr.GetActionStateBoolean(_session, &get, &button) == Result.Success
                && button.IsActive != 0 && button.ChangedSinceLastSync != 0 && button.CurrentState != 0;
        }

        return new XrInputFrame(ToWorld(headXr), Quaternion.Concatenate(headRotXr, WorldFromXr),
            hands[0], hands[1], tracked[0], tracked[1], sticks[0], sticks[1], grips[0], grips[1], calibrate, focused);
    }

    private static Vector3 ToWorld(Vector3 xr) => Vector3.Transform(xr, WorldFromXr);

    private void PollEvents()
    {
        while (true)
        {
            var buffer = new EventDataBuffer { Type = StructureType.EventDataBuffer };
            if (_xr.PollEvent(_instance, &buffer) != Result.Success)
            {
                return;
            }

            if (buffer.Type == StructureType.EventDataSessionStateChanged)
            {
                _state = ((EventDataSessionStateChanged*)&buffer)->State;
                Console.WriteLine($"[xr] session state -> {_state}");
                if (_state == SessionState.Ready && !Running)
                {
                    var begin = new SessionBeginInfo { Type = StructureType.SessionBeginInfo, PrimaryViewConfigurationType = ViewConfigurationType.PrimaryStereo };
                    Vulkan.WaitIdle();
                    Running = _xr.BeginSession(_session, &begin) == Result.Success;
                }
                else if (_state == SessionState.Stopping && Running)
                {
                    Vulkan.WaitIdle();
                    _ = _xr.EndSession(_session);
                    Running = false;
                }

                ExitRequested |= _state is SessionState.Exiting or SessionState.LossPending;
            }
            else if (buffer.Type == StructureType.EventDataInstanceLossPending)
            {
                ExitRequested = true;
            }
            else if (buffer.Type == StructureType.EventDataReferenceSpaceChangePending)
            {
                _recenterPending = true; // システム側で再センタリングされた → リングが前に来るよう合わせ直す
            }
        }
    }

    private void CreateInstance()
    {
        var appInfo = new ApplicationInfo { ApiVersion = 1UL << 48 }; // XR_MAKE_VERSION(1,0,0)（iTVision と同じ）
        WriteFixed(appInfo.ApplicationName, 128, "MiniRing");
        WriteFixed(appInfo.EngineName, 128, "Aigis");
        string[] extensions = ["XR_KHR_vulkan_enable"];
        var names = (byte**)SilkMarshal.StringArrayToPtr(extensions);
        try
        {
            var info = new InstanceCreateInfo
            {
                Type = StructureType.InstanceCreateInfo,
                ApplicationInfo = appInfo,
                EnabledExtensionCount = (uint)extensions.Length,
                EnabledExtensionNames = names,
            };
            var instance = default(Silk.NET.OpenXR.Instance);
            Check(_xr.CreateInstance(&info, &instance), "xrCreateInstance (is an OpenXR runtime — SteamVR / Oculus / PICO — installed and active?)");
            _instance = instance;
        }
        finally
        {
            SilkMarshal.Free((nint)names);
        }
    }

    private void GetSystem()
    {
        var info = new SystemGetInfo { Type = StructureType.SystemGetInfo, FormFactor = FormFactor.HeadMountedDisplay };
        ulong id = 0;
        Check(_xr.GetSystem(_instance, &info, &id), "xrGetSystem (is the headset connected?)");
        _systemId = id;
    }

    private string[] VulkanExtensions(bool device)
    {
        uint count = 0;
        if (device)
        {
            _ = _vkEnable.GetVulkanDeviceExtension(_instance, _systemId, 0, &count, (byte*)null);
        }
        else
        {
            _ = _vkEnable.GetVulkanInstanceExtension(_instance, _systemId, 0, &count, (byte*)null);
        }

        if (count == 0)
        {
            return [];
        }

        var buffer = new byte[count];
        fixed (byte* p = buffer)
        {
            if (device)
            {
                Check(_vkEnable.GetVulkanDeviceExtension(_instance, _systemId, count, &count, p), "xrGetVulkanDeviceExtensionsKHR");
            }
            else
            {
                Check(_vkEnable.GetVulkanInstanceExtension(_instance, _systemId, count, &count, p), "xrGetVulkanInstanceExtensionsKHR");
            }
        }

        var length = Array.IndexOf(buffer, (byte)0);
        return Encoding.UTF8.GetString(buffer, 0, length < 0 ? (int)count : length).Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    private void CreateSession()
    {
        var binding = new GraphicsBindingVulkanKHR
        {
            Type = StructureType.GraphicsBindingVulkanKhr,
            Instance = new VkHandle(Vulkan.Instance.Handle),
            PhysicalDevice = new VkHandle(Vulkan.PhysicalDevice.Handle),
            Device = new VkHandle(Vulkan.Device.Handle),
            QueueFamilyIndex = Vulkan.QueueFamily,
            QueueIndex = 0,
        };
        var info = new SessionCreateInfo { Type = StructureType.SessionCreateInfo, Next = &binding, SystemId = _systemId };
        var session = default(Session);
        Check(_xr.CreateSession(_instance, &info, &session), "xrCreateSession");
        _session = session;
    }

    private void CreateAppSpace()
    {
        foreach (var type in new[] { ReferenceSpaceType.Stage, ReferenceSpaceType.Local })
        {
            var info = new ReferenceSpaceCreateInfo
            {
                Type = StructureType.ReferenceSpaceCreateInfo,
                ReferenceSpaceType = type,
                PoseInReferenceSpace = new Posef(new Quaternionf(0f, 0f, 0f, 1f), default),
            };
            var space = default(Space);
            if (_xr.CreateReferenceSpace(_session, &info, &space) == Result.Success)
            {
                _appSpace = space;
                _appSpaceType = type;
                return;
            }
        }

        throw new InvalidOperationException("xrCreateReferenceSpace failed for both STAGE and LOCAL.");
    }

    /// <summary>初回の頭の向き・位置を基準に空間を作り直す（iTVision RecenterAppSpace）: 頭の真下が原点、顔の向きが −Z。</summary>
    private void Recenter(Vector3 headXr, Quaternion headRotXr)
    {
        var forward = Vector3.Transform(-Vector3.UnitZ, headRotXr);
        if (MathF.Abs(forward.X) < 1e-4f && MathF.Abs(forward.Z) < 1e-4f)
        {
            return;
        }

        // 空間の −Z を頭の向きへ: yaw = atan2(−fx, −fz)。
        var yaw = MathF.Atan2(-forward.X, -forward.Z);
        var (sin, cos) = MathF.SinCos(yaw * 0.5f);
        var info = new ReferenceSpaceCreateInfo
        {
            Type = StructureType.ReferenceSpaceCreateInfo,
            ReferenceSpaceType = _appSpaceType,
            PoseInReferenceSpace = new Posef(new Quaternionf(0f, sin, 0f, cos), new Vector3f(headXr.X, _appSpaceType == ReferenceSpaceType.Stage ? 0f : headXr.Y - 1.6f, headXr.Z)),
        };
        var space = default(Space);
        if (_xr.CreateReferenceSpace(_session, &info, &space) == Result.Success)
        {
            _ = _xr.DestroySpace(_appSpace);
            _appSpace = space;
            _recenterPending = false;
            Console.WriteLine("[xr] recentered: you now face the ring");
        }
    }

    private void CreateActions()
    {
        var setInfo = new ActionSetCreateInfo { Type = StructureType.ActionSetCreateInfo };
        WriteFixed(setInfo.ActionSetName, 64, "gameplay");
        WriteFixed(setInfo.LocalizedActionSetName, 128, "Gameplay");
        var set = default(ActionSet);
        Check(_xr.CreateActionSet(_instance, &setInfo, &set), "xrCreateActionSet");
        _actionSet = set;
        _handPaths[0] = Path("/user/hand/left");
        _handPaths[1] = Path("/user/hand/right");

        _poseAction = CreateAction("hand_pose", "Hand Pose", ActionType.PoseInput);
        _squeezeAction = CreateAction("squeeze", "Grab", ActionType.FloatInput);
        _stickAction = CreateAction("thumbstick", "Move", ActionType.Vector2fInput);
        _calibrateAction = CreateAction("calibrate", "Calibrate Height", ActionType.BooleanInput);
        _hapticAction = CreateAction("haptic", "Haptic", ActionType.VibrationOutput);

        // Quest(Touch)・Index は iTVision と同じ。A/X と振動を追加。
        Suggest("/interaction_profiles/oculus/touch_controller",
        [
            (_poseAction, "/user/hand/left/input/grip/pose"), (_poseAction, "/user/hand/right/input/grip/pose"),
            (_squeezeAction, "/user/hand/left/input/squeeze/value"), (_squeezeAction, "/user/hand/right/input/squeeze/value"),
            (_stickAction, "/user/hand/left/input/thumbstick"), (_stickAction, "/user/hand/right/input/thumbstick"),
            (_calibrateAction, "/user/hand/left/input/x/click"), (_calibrateAction, "/user/hand/right/input/a/click"),
            (_hapticAction, "/user/hand/left/output/haptic"), (_hapticAction, "/user/hand/right/output/haptic"),
        ]);
        Suggest("/interaction_profiles/valve/index_controller",
        [
            (_poseAction, "/user/hand/left/input/grip/pose"), (_poseAction, "/user/hand/right/input/grip/pose"),
            (_squeezeAction, "/user/hand/left/input/squeeze/value"), (_squeezeAction, "/user/hand/right/input/squeeze/value"),
            (_stickAction, "/user/hand/left/input/thumbstick"), (_stickAction, "/user/hand/right/input/thumbstick"),
            (_calibrateAction, "/user/hand/left/input/a/click"), (_calibrateAction, "/user/hand/right/input/a/click"),
            (_hapticAction, "/user/hand/left/output/haptic"), (_hapticAction, "/user/hand/right/output/haptic"),
        ]);
        Suggest("/interaction_profiles/khr/simple_controller",
        [
            (_poseAction, "/user/hand/left/input/grip/pose"), (_poseAction, "/user/hand/right/input/grip/pose"),
            (_squeezeAction, "/user/hand/left/input/select/click"), (_squeezeAction, "/user/hand/right/input/select/click"),
            (_calibrateAction, "/user/hand/left/input/menu/click"), (_calibrateAction, "/user/hand/right/input/menu/click"),
            (_hapticAction, "/user/hand/left/output/haptic"), (_hapticAction, "/user/hand/right/output/haptic"),
        ]);

        var attachSet = _actionSet;
        var attach = new SessionActionSetsAttachInfo { Type = StructureType.SessionActionSetsAttachInfo, CountActionSets = 1, ActionSets = &attachSet };
        Check(_xr.AttachSessionActionSets(_session, &attach), "xrAttachSessionActionSets");

        for (var h = 0; h < 2; h++)
        {
            var spaceInfo = new ActionSpaceCreateInfo
            {
                Type = StructureType.ActionSpaceCreateInfo,
                Action = _poseAction,
                SubactionPath = _handPaths[h],
                PoseInActionSpace = new Posef(new Quaternionf(0f, 0f, 0f, 1f), default),
            };
            var space = default(Space);
            Check(_xr.CreateActionSpace(_session, &spaceInfo, &space), "xrCreateActionSpace");
            _handSpaces[h] = space;
        }
    }

    private Silk.NET.OpenXR.Action CreateAction(string name, string localized, ActionType type)
    {
        fixed (ulong* paths = _handPaths)
        {
            var info = new ActionCreateInfo { Type = StructureType.ActionCreateInfo, ActionType = type, CountSubactionPaths = 2, SubactionPaths = paths };
            WriteFixed(info.ActionName, 64, name);
            WriteFixed(info.LocalizedActionName, 128, localized);
            var action = default(Silk.NET.OpenXR.Action);
            Check(_xr.CreateAction(_actionSet, &info, &action), $"xrCreateAction({name})");
            return action;
        }
    }

    private void Suggest(string profile, (Silk.NET.OpenXR.Action Action, string Path)[] bindings)
    {
        var suggested = new ActionSuggestedBinding[bindings.Length];
        for (var i = 0; i < bindings.Length; i++)
        {
            suggested[i] = new ActionSuggestedBinding { Action = bindings[i].Action, Binding = Path(bindings[i].Path) };
        }

        fixed (ActionSuggestedBinding* p = suggested)
        {
            var info = new InteractionProfileSuggestedBinding
            {
                Type = StructureType.InteractionProfileSuggestedBinding,
                InteractionProfile = Path(profile),
                CountSuggestedBindings = (uint)suggested.Length,
                SuggestedBindings = p,
            };
            var result = _xr.SuggestInteractionProfileBinding(_instance, &info);
            if (result != Result.Success)
            {
                Console.WriteLine($"[xr] binding for {profile} rejected: {result}"); // 失敗しても他のプロファイルで動く
            }
        }
    }

    private void CreateSwapchains(float renderScale)
    {
        uint viewCount = 0;
        _ = _xr.EnumerateViewConfigurationView(_instance, _systemId, ViewConfigurationType.PrimaryStereo, 0, &viewCount, null);
        var views = new ViewConfigurationView[viewCount];
        for (var i = 0; i < views.Length; i++)
        {
            views[i].Type = StructureType.ViewConfigurationView;
        }

        fixed (ViewConfigurationView* p = views)
        {
            Check(_xr.EnumerateViewConfigurationView(_instance, _systemId, ViewConfigurationType.PrimaryStereo, viewCount, &viewCount, p), "xrEnumerateViewConfigurationViews");
        }

        EyeWidth = Math.Min(views[0].MaxImageRectWidth, Math.Max(256u, (uint)(views[0].RecommendedImageRectWidth * renderScale)));
        EyeHeight = Math.Min(views[0].MaxImageRectHeight, Math.Max(256u, (uint)(views[0].RecommendedImageRectHeight * renderScale)));

        uint formatCount = 0;
        _ = _xr.EnumerateSwapchainFormats(_session, 0, &formatCount, null);
        var formats = new long[formatCount];
        fixed (long* p = formats)
        {
            Check(_xr.EnumerateSwapchainFormats(_session, formatCount, &formatCount, p), "xrEnumerateSwapchainFormats");
        }

        // HeadlessRenderer は R8G8B8A8_UNORM にガンマ済みの値を描き、ビット単位でコピーする。SRGB が正、UNORM は妥協。
        long format;
        if (formats.Contains(FormatR8G8B8A8Srgb))
        {
            format = FormatR8G8B8A8Srgb;
            IsSrgb = true;
        }
        else if (formats.Contains(FormatR8G8B8A8Unorm))
        {
            format = FormatR8G8B8A8Unorm;
            Console.WriteLine("[xr] runtime offers no R8G8B8A8_SRGB; using UNORM (colours will look washed out)");
        }
        else
        {
            throw new InvalidOperationException($"The runtime offers no R8G8B8A8 eye format (offered: {string.Join(", ", formats)}).");
        }

        for (var eye = 0; eye < 2; eye++)
        {
            var info = new SwapchainCreateInfo
            {
                Type = StructureType.SwapchainCreateInfo,
                UsageFlags = SwapchainUsageFlags.ColorAttachmentBit | SwapchainUsageFlags.TransferDstBit,
                Format = format,
                SampleCount = 1,
                Width = EyeWidth,
                Height = EyeHeight,
                FaceCount = 1,
                ArraySize = 1,
                MipCount = 1,
            };
            var swapchain = default(Swapchain);
            Check(_xr.CreateSwapchain(_session, &info, &swapchain), "xrCreateSwapchain");
            _swapchains[eye] = swapchain;

            uint imageCount = 0;
            _ = _xr.EnumerateSwapchainImages(swapchain, 0, &imageCount, null);
            var images = new SwapchainImageVulkanKHR[imageCount];
            for (var i = 0; i < images.Length; i++)
            {
                images[i].Type = StructureType.SwapchainImageVulkanKhr;
            }

            fixed (SwapchainImageVulkanKHR* p = images)
            {
                Check(_xr.EnumerateSwapchainImages(swapchain, imageCount, &imageCount, (SwapchainImageBaseHeader*)p), "xrEnumerateSwapchainImages");
            }

            _images[eye] = images.Select(i => i.Image).ToArray();
        }
    }

    private ulong Path(string path)
    {
        var p = (byte*)SilkMarshal.StringToPtr(path);
        try
        {
            ulong result = 0;
            Check(_xr.StringToPath(_instance, p, &result), $"xrStringToPath({path})");
            return result;
        }
        finally
        {
            SilkMarshal.Free((nint)p);
        }
    }

    private static void WriteFixed(byte* destination, int capacity, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var length = Math.Min(bytes.Length, capacity - 1);
        for (var i = 0; i < length; i++)
        {
            destination[i] = bytes[i];
        }

        destination[length] = 0;
    }

    private static string Version(ulong v) => $"{v >> 48}.{(v >> 32) & 0xFFFF}";

    private static Vector3 ToVector(Vector3f v) => new(v.X, v.Y, v.Z);

    private static Quaternion ToQuaternion(Quaternionf q) => new(q.X, q.Y, q.Z, q.W);

    // XR_SUCCEEDED: 0 以上は成功（XR_SESSION_LOSS_PENDING・XR_FRAME_DISCARDED などの正の値も含む）。
    private static void Check(Result result, string what)
    {
        if ((int)result < 0)
        {
            throw new InvalidOperationException($"{what} failed: {result}");
        }
    }

    public void Dispose()
    {
        Vulkan?.WaitIdle();
        foreach (var swapchain in _swapchains)
        {
            if (swapchain.Handle != 0)
            {
                _ = _xr.DestroySwapchain(swapchain);
            }
        }

        foreach (var space in _handSpaces)
        {
            if (space.Handle != 0)
            {
                _ = _xr.DestroySpace(space);
            }
        }

        if (_appSpace.Handle != 0)
        {
            _ = _xr.DestroySpace(_appSpace);
        }

        if (_actionSet.Handle != 0)
        {
            _ = _xr.DestroyActionSet(_actionSet);
        }

        if (_session.Handle != 0)
        {
            _ = _xr.DestroySession(_session);
        }

        // セッション破棄後にデバイスを壊す（ランタイムがデバイスを使い終えてから）。
        Vulkan?.Dispose();
        if (_instance.Handle != 0)
        {
            _ = _xr.DestroyInstance(_instance);
        }

        _xr.Dispose();
    }
}
