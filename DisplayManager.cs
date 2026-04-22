using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SwitchDisplayPlugin;

internal static class DisplayManager
{
    private const int EnumCurrentSettings = -1;
    private const int DmPosition = 0x00000020;
    private const int DmPelsWidth = 0x00080000;
    private const int DmPelsHeight = 0x00100000;
    private const int DisplayDeviceAttachedToDesktop = 0x00000001;
    private const int DisplayDeviceMirroringDriver = 0x00000008;
    private const int DisplayDevicePrimaryDevice = 0x00000004;
    private const int CdsUpdateRegistry = 0x00000001;
    private const int CdsNoReset = 0x10000000;
    private const int CdsSetPrimary = 0x00000010;
    private const int DispChangeSuccessful = 0;
    private const int ErrorSuccess = 0;
    private const int ErrorInsufficientBuffer = 122;
    private const uint QdcOnlyActivePaths = 0x00000002;
    private const uint SdcUseSuppliedDisplayConfig = 0x00000020;
    private const uint SdcApply = 0x00000080;
    private const uint SdcSaveToDatabase = 0x00000200;
    private const uint SdcAllowChanges = 0x00000400;
    private const uint DisplayConfigModeInfoTypeSource = 1;
    private const uint DisplayConfigDeviceInfoGetSourceName = 1;
    private const uint DisplayConfigPathModeIdxInvalid = 0xffffffff;

    public static List<DisplayInfo> GetDisplays()
    {
        var displays = new List<DisplayInfo>();

        for (var adapterIndex = 0u; ; adapterIndex++)
        {
            var adapter = DisplayDevice.Create();
            if (!EnumDisplayDevices(null, adapterIndex, ref adapter, 0))
            {
                break;
            }

            if (!IsUsableDisplayAdapter(adapter))
            {
                continue;
            }

            var mode = DevMode.Create();
            if (!EnumDisplaySettings(adapter.DeviceName, EnumCurrentSettings, ref mode))
            {
                continue;
            }

            var monitorName = GetMonitorName(adapter.DeviceName);
            var displayNumber = displays.Count + 1;

            displays.Add(new DisplayInfo(
                displayNumber,
                adapter.DeviceName,
                $"Display {displayNumber}",
                string.IsNullOrWhiteSpace(monitorName) ? adapter.DeviceString : monitorName,
                mode.PositionX,
                mode.PositionY,
                mode.PelsWidth,
                mode.PelsHeight,
                (adapter.StateFlags & DisplayDevicePrimaryDevice) == DisplayDevicePrimaryDevice));
        }

        return displays;
    }

    public static DisplayChangeResult SetPrimaryDisplay(string deviceName)
    {
        var displays = GetDisplays();
        var target = displays.FirstOrDefault(display => display.DeviceName.Equals(deviceName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Display '{deviceName}' was not found.");

        PluginLog.Write("Before change: " + FormatDisplays(displays));

        if (target.IsPrimary)
        {
            return new DisplayChangeResult(target, GetDisplays(), "The selected display is already primary.");
        }

        SetPrimaryDisplayUsingDisplayConfig(displays, target);

        var updatedDisplays = GetDisplays();
        PluginLog.Write("After change: " + FormatDisplays(updatedDisplays));
        var updatedTarget = updatedDisplays.FirstOrDefault(display => display.DeviceName.Equals(target.DeviceName, StringComparison.OrdinalIgnoreCase));
        if (updatedTarget?.IsPrimary != true)
        {
            throw new InvalidOperationException("Windows accepted the display change but the selected display did not become primary.");
        }

        return new DisplayChangeResult(updatedTarget, updatedDisplays, "The selected display is now primary.");
    }

    private static void SetPrimaryDisplayUsingDisplayConfig(IReadOnlyList<DisplayInfo> displays, DisplayInfo target)
    {
        var displayByDeviceName = displays.ToDictionary(display => display.DeviceName, StringComparer.OrdinalIgnoreCase);
        var result = GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out var pathCount, out var modeCount);
        if (result != ErrorSuccess)
        {
            throw new Win32Exception(result, $"GetDisplayConfigBufferSizes failed with code {result}.");
        }

        DisplayConfigPathInfo[] paths;
        DisplayConfigModeInfo[] modes;

        while (true)
        {
            paths = new DisplayConfigPathInfo[pathCount];
            modes = new DisplayConfigModeInfo[modeCount];

            result = QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, nint.Zero);
            if (result == ErrorSuccess)
            {
                break;
            }

            if (result != ErrorInsufficientBuffer)
            {
                throw new Win32Exception(result, $"QueryDisplayConfig failed with code {result}.");
            }

            result = GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out pathCount, out modeCount);
            if (result != ErrorSuccess)
            {
                throw new Win32Exception(result, $"GetDisplayConfigBufferSizes failed with code {result}.");
            }
        }

        var updatedSources = 0;
        for (var pathIndex = 0; pathIndex < pathCount; pathIndex++)
        {
            ref var sourceInfo = ref paths[pathIndex].SourceInfo;
            var deviceName = GetGdiDeviceName(sourceInfo.AdapterId, sourceInfo.Id);
            if (!displayByDeviceName.TryGetValue(deviceName, out var display))
            {
                PluginLog.Write($"DisplayConfig skipped unknown source: {deviceName}.");
                continue;
            }

            var modeIndex = GetSourceModeIndex(sourceInfo, modes, modeCount);
            if (modeIndex < 0)
            {
                PluginLog.Write($"DisplayConfig skipped source without mode: {deviceName}.");
                continue;
            }

            modes[modeIndex].SourceMode.Position.X = display.X - target.X;
            modes[modeIndex].SourceMode.Position.Y = display.Y - target.Y;
            updatedSources++;

            PluginLog.Write($"DisplayConfig staged: {deviceName}, pos={modes[modeIndex].SourceMode.Position.X},{modes[modeIndex].SourceMode.Position.Y}.");
        }

        if (updatedSources == 0)
        {
            throw new InvalidOperationException("No active DisplayConfig sources matched the current displays.");
        }

        result = SetDisplayConfig(
            pathCount,
            paths,
            modeCount,
            modes,
            SdcUseSuppliedDisplayConfig | SdcApply | SdcSaveToDatabase | SdcAllowChanges);

        PluginLog.Write($"SetDisplayConfig apply: result={result}.");
        if (result != ErrorSuccess)
        {
            throw new Win32Exception(result, $"SetDisplayConfig failed with code {result}.");
        }
    }

    private static int GetSourceModeIndex(DisplayConfigPathSourceInfo sourceInfo, DisplayConfigModeInfo[] modes, uint modeCount)
    {
        if (sourceInfo.ModeInfoIdx != DisplayConfigPathModeIdxInvalid
            && sourceInfo.ModeInfoIdx < modeCount
            && modes[sourceInfo.ModeInfoIdx].InfoType == DisplayConfigModeInfoTypeSource)
        {
            return (int)sourceInfo.ModeInfoIdx;
        }

        for (var modeIndex = 0; modeIndex < modeCount; modeIndex++)
        {
            if (modes[modeIndex].InfoType == DisplayConfigModeInfoTypeSource
                && modes[modeIndex].AdapterId.Equals(sourceInfo.AdapterId)
                && modes[modeIndex].Id == sourceInfo.Id)
            {
                return modeIndex;
            }
        }

        return -1;
    }

    private static string GetGdiDeviceName(Luid adapterId, uint sourceId)
    {
        var deviceName = DisplayConfigSourceDeviceName.Create(adapterId, sourceId);
        var result = DisplayConfigGetDeviceInfo(ref deviceName);
        if (result != ErrorSuccess)
        {
            throw new Win32Exception(result, $"DisplayConfigGetDeviceInfo failed with code {result}.");
        }

        return deviceName.ViewGdiDeviceName;
    }

    private static string FormatDisplays(IEnumerable<DisplayInfo> displays)
    {
        return string.Join(" | ", displays.Select(display =>
            $"{display.DisplayName} {display.DeviceName} primary={display.IsPrimary} pos={display.X},{display.Y} size={display.Width}x{display.Height}"));
    }

    private static bool IsUsableDisplayAdapter(DisplayDevice adapter)
    {
        var attached = (adapter.StateFlags & DisplayDeviceAttachedToDesktop) == DisplayDeviceAttachedToDesktop;
        var mirror = (adapter.StateFlags & DisplayDeviceMirroringDriver) == DisplayDeviceMirroringDriver;
        return attached && !mirror;
    }

    private static string GetMonitorName(string adapterName)
    {
        for (var monitorIndex = 0u; ; monitorIndex++)
        {
            var monitor = DisplayDevice.Create();
            if (!EnumDisplayDevices(adapterName, monitorIndex, ref monitor, 0))
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(monitor.DeviceString))
            {
                return monitor.DeviceString;
            }
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DisplayDevice lpDisplayDevice, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DevMode lpDevMode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsEx(string? lpszDeviceName, ref DevMode lpDevMode, nint hwnd, int dwFlags, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsEx(string? lpszDeviceName, nint lpDevMode, nint hwnd, int dwFlags, nint lParam);

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(
        uint flags,
        ref uint numPathArrayElements,
        [Out] DisplayConfigPathInfo[] pathInfoArray,
        ref uint numModeInfoArrayElements,
        [Out] DisplayConfigModeInfo[] modeInfoArray,
        nint currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int SetDisplayConfig(
        uint numPathArrayElements,
        [In] DisplayConfigPathInfo[] pathArray,
        uint numModeInfoArrayElements,
        [In] DisplayConfigModeInfo[] modeInfoArray,
        uint flags);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigSourceDeviceName requestPacket);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Cb;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        public int StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;

        public static DisplayDevice Create()
        {
            return new DisplayDevice
            {
                Cb = Marshal.SizeOf<DisplayDevice>(),
                DeviceName = string.Empty,
                DeviceString = string.Empty,
                DeviceId = string.Empty,
                DeviceKey = string.Empty
            };
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        public short SpecVersion;
        public short DriverVersion;
        public short Size;
        public short DriverExtra;
        public int Fields;
        public int PositionX;
        public int PositionY;
        public int DisplayOrientation;
        public int DisplayFixedOutput;
        public short Color;
        public short Duplex;
        public short YResolution;
        public short TTOption;
        public short Collate;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string FormName;

        public short LogPixels;
        public int BitsPerPel;
        public int PelsWidth;
        public int PelsHeight;
        public int DisplayFlags;
        public int DisplayFrequency;
        public int ICMMethod;
        public int ICMIntent;
        public int MediaType;
        public int DitherType;
        public int Reserved1;
        public int Reserved2;
        public int PanningWidth;
        public int PanningHeight;

        public static DevMode Create()
        {
            return new DevMode
            {
                Size = (short)Marshal.SizeOf<DevMode>(),
                DeviceName = string.Empty,
                FormName = string.Empty
            };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointL
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfig2DRegion
    {
        public uint Cx;
        public uint Cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigRational
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathInfo
    {
        public DisplayConfigPathSourceInfo SourceInfo;
        public DisplayConfigPathTargetInfo TargetInfo;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathSourceInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathTargetInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint OutputTechnology;
        public uint Rotation;
        public uint Scaling;
        public DisplayConfigRational RefreshRate;
        public uint ScanLineOrdering;

        [MarshalAs(UnmanagedType.Bool)]
        public bool TargetAvailable;

        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct DisplayConfigModeInfo
    {
        [FieldOffset(0)]
        public uint InfoType;

        [FieldOffset(4)]
        public uint Id;

        [FieldOffset(8)]
        public Luid AdapterId;

        [FieldOffset(16)]
        public DisplayConfigTargetMode TargetMode;

        [FieldOffset(16)]
        public DisplayConfigSourceMode SourceMode;

        [FieldOffset(16)]
        public DisplayConfigDesktopImageInfo DesktopImageInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigSourceMode
    {
        public uint Width;
        public uint Height;
        public uint PixelFormat;
        public PointL Position;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigTargetMode
    {
        public DisplayConfigVideoSignalInfo TargetVideoSignalInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigVideoSignalInfo
    {
        public ulong PixelRate;
        public DisplayConfigRational HSyncFreq;
        public DisplayConfigRational VSyncFreq;
        public DisplayConfig2DRegion ActiveSize;
        public DisplayConfig2DRegion TotalSize;
        public uint VideoStandard;
        public uint ScanLineOrdering;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigDesktopImageInfo
    {
        public PointL PathSourceSize;
        public Rect DesktopImageRegion;
        public Rect DesktopImageClip;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayConfigDeviceInfoHeader
    {
        public uint Type;
        public uint Size;
        public Luid AdapterId;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayConfigSourceDeviceName
    {
        public DisplayConfigDeviceInfoHeader Header;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string ViewGdiDeviceName;

        public static DisplayConfigSourceDeviceName Create(Luid adapterId, uint sourceId)
        {
            return new DisplayConfigSourceDeviceName
            {
                Header = new DisplayConfigDeviceInfoHeader
                {
                    Type = DisplayConfigDeviceInfoGetSourceName,
                    Size = (uint)Marshal.SizeOf<DisplayConfigSourceDeviceName>(),
                    AdapterId = adapterId,
                    Id = sourceId
                },
                ViewGdiDeviceName = string.Empty
            };
        }
    }
}
