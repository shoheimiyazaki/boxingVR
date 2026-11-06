using Aigis.Render;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace MiniRing.Vr;

/// <summary>
/// OpenXR が要求する拡張を有効にした Vulkan インスタンス／デバイス。Aigis の HeadlessRenderer は
/// 自分でデバイスを作るが、XR のスワップチェーン画像は「セッションに渡したデバイス」でしか扱えないため、
/// ここで作って <see cref="ExternalVulkanDevice"/> として渡す（iTVision の XR_KHR_vulkan_enable 方式）。
/// </summary>
internal sealed unsafe class VulkanContext : IDisposable
{
    private readonly Vk _vk = Vk.GetApi();

    private VulkanContext()
    {
    }

    public Instance Instance { get; private set; }
    public PhysicalDevice PhysicalDevice { get; private set; }
    public Device Device { get; private set; }
    public uint QueueFamily { get; private set; }

    public ExternalVulkanDevice External => new((nint)Instance.Handle, (nint)PhysicalDevice.Handle, (nint)Device.Handle, QueueFamily, 0);

    /// <summary>インスタンスだけ作る（物理デバイスは XR ランタイムが指名するので、その後 <see cref="CreateDevice"/>）。</summary>
    public static VulkanContext CreateInstance(IReadOnlyList<string> instanceExtensions)
    {
        var context = new VulkanContext();
        var appName = (byte*)SilkMarshal.StringToPtr("MiniRing");
        var names = (byte**)SilkMarshal.StringArrayToPtr(instanceExtensions.ToArray());
        try
        {
            var appInfo = new ApplicationInfo
            {
                SType = StructureType.ApplicationInfo,
                PApplicationName = appName,
                ApiVersion = Vk.Version11, // Aigis の HeadlessRenderer と同じ
            };
            var info = new InstanceCreateInfo
            {
                SType = StructureType.InstanceCreateInfo,
                PApplicationInfo = &appInfo,
                EnabledExtensionCount = (uint)instanceExtensions.Count,
                PpEnabledExtensionNames = names,
            };
            Check(context._vk.CreateInstance(in info, null, out var instance), "vkCreateInstance");
            context.Instance = instance;
            return context;
        }
        finally
        {
            SilkMarshal.Free((nint)appName);
            SilkMarshal.Free((nint)names);
        }
    }

    public void CreateDevice(PhysicalDevice physicalDevice, IReadOnlyList<string> deviceExtensions)
    {
        PhysicalDevice = physicalDevice;
        uint familyCount = 0;
        _vk.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, ref familyCount, null);
        var families = new QueueFamilyProperties[familyCount];
        fixed (QueueFamilyProperties* f = families)
        {
            _vk.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, ref familyCount, f);
        }

        var family = Array.FindIndex(families, p => (p.QueueFlags & QueueFlags.GraphicsBit) != 0);
        if (family < 0)
        {
            throw new InvalidOperationException("The headset's GPU has no graphics queue.");
        }

        QueueFamily = (uint)family;
        var priority = 1f;
        var queueInfo = new DeviceQueueCreateInfo
        {
            SType = StructureType.DeviceQueueCreateInfo,
            QueueFamilyIndex = QueueFamily,
            QueueCount = 1,
            PQueuePriorities = &priority,
        };
        var names = (byte**)SilkMarshal.StringArrayToPtr(deviceExtensions.ToArray());
        try
        {
            var info = new DeviceCreateInfo
            {
                SType = StructureType.DeviceCreateInfo,
                QueueCreateInfoCount = 1,
                PQueueCreateInfos = &queueInfo,
                EnabledExtensionCount = (uint)deviceExtensions.Count,
                PpEnabledExtensionNames = names,
            };
            Check(_vk.CreateDevice(physicalDevice, in info, null, out var device), "vkCreateDevice");
            Device = device;
        }
        finally
        {
            SilkMarshal.Free((nint)names);
        }
    }

    public void WaitIdle()
    {
        if (Device.Handle != 0)
        {
            _vk.DeviceWaitIdle(Device);
        }
    }

    private static void Check(Result result, string what)
    {
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"{what} failed: {result}");
        }
    }

    public void Dispose()
    {
        if (Device.Handle != 0)
        {
            _vk.DeviceWaitIdle(Device);
            _vk.DestroyDevice(Device, null);
            Device = default;
        }

        if (Instance.Handle != 0)
        {
            _vk.DestroyInstance(Instance, null);
            Instance = default;
        }

        _vk.Dispose();
    }
}
