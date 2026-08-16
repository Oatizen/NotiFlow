using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;
using NotiFlow.Models;

namespace NotiFlow.Services
{
    /// <summary>
    /// 系统显示器检测与管理服务。
    /// </summary>
    public static class ScreenService
    {
        public static event Action? DisplaySettingsChanged;

        static ScreenService()
        {
            try
            {
                SystemEvents.DisplaySettingsChanged += (_, _) =>
                {
                    DisplaySettingsChanged?.Invoke();
                };
            }
            catch { }
        }

        /// <summary>
        /// 获取当前系统连接的所有显示器列表，并与本地已保存的用户自定义顺序和启用状态合并。
        /// </summary>
        public static List<MonitorSettingItemDto> GetMergedMonitors(List<MonitorSettingItemDto>? savedPreferences)
        {
            var rawScreens = Screen.AllScreens;
            var currentScreens = new List<MonitorSettingItemDto>();

            for (int i = 0; i < rawScreens.Length; i++)
            {
                var s = rawScreens[i];
                string name = GetFriendlyName(s.DeviceName, i, s.Primary);
                string resolution = $"{s.Bounds.Width} × {s.Bounds.Height}";

                currentScreens.Add(new MonitorSettingItemDto
                {
                    DeviceName = s.DeviceName,
                    DisplayName = name,
                    ResolutionText = resolution,
                    X = s.Bounds.X,
                    Y = s.Bounds.Y,
                    Width = s.Bounds.Width,
                    Height = s.Bounds.Height,
                    IsPrimary = s.Primary,
                    IsEnabled = true
                });
            }

            if (savedPreferences == null || savedPreferences.Count == 0)
            {
                for (int i = 0; i < currentScreens.Count; i++)
                {
                    currentScreens[i].DisplayOrder = i + 1;
                }
                return currentScreens;
            }

            var mergedList = new List<MonitorSettingItemDto>();
            var matchedDeviceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. 按照用户之前保存的顺序加入当前仍然在线的显示器
            foreach (var saved in savedPreferences)
            {
                var live = currentScreens.FirstOrDefault(s => string.Equals(s.DeviceName, saved.DeviceName, StringComparison.OrdinalIgnoreCase));
                if (live != null && !matchedDeviceNames.Contains(live.DeviceName))
                {
                    live.IsEnabled = saved.IsEnabled;
                    mergedList.Add(live);
                    matchedDeviceNames.Add(live.DeviceName);
                }
            }

            // 2. 将新检测到的显示器追加到末尾
            foreach (var live in currentScreens)
            {
                if (!matchedDeviceNames.Contains(live.DeviceName))
                {
                    mergedList.Add(live);
                    matchedDeviceNames.Add(live.DeviceName);
                }
            }

            // 3. 重新校准 DisplayOrder 编号
            for (int i = 0; i < mergedList.Count; i++)
            {
                mergedList[i].DisplayOrder = i + 1;
            }

            return mergedList;
        }

        private static string GetFriendlyName(string deviceName, int index, bool isPrimary)
        {
            try
            {
                uint pathCount, modeCount;
                if (NativeMethods.GetDisplayConfigBufferSizes(NativeMethods.QDC_ONLY_ACTIVE_PATHS, out pathCount, out modeCount) == 0)
                {
                    var paths = new NativeMethods.DISPLAYCONFIG_PATH_INFO[pathCount];
                    var modes = new NativeMethods.DISPLAYCONFIG_MODE_INFO[modeCount];
                    if (NativeMethods.QueryDisplayConfig(NativeMethods.QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) == 0)
                    {
                        for (int i = 0; i < pathCount; i++)
                        {
                            var src = new NativeMethods.DISPLAYCONFIG_SOURCE_DEVICE_NAME
                            {
                                type = NativeMethods.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,
                                size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                                adapterId = paths[i].sourceInfo.adapterId,
                                id = paths[i].sourceInfo.id
                            };

                            if (NativeMethods.DisplayConfigGetDeviceInfo(ref src) == 0)
                            {
                                if (string.Equals(src.viewGdiDeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
                                {
                                    var target = new NativeMethods.DISPLAYCONFIG_TARGET_DEVICE_NAME
                                    {
                                        type = NativeMethods.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
                                        size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
                                        adapterId = paths[i].targetInfo.adapterId,
                                        id = paths[i].targetInfo.id
                                    };

                                    if (NativeMethods.DisplayConfigGetDeviceInfo(ref target) == 0)
                                    {
                                        if (!string.IsNullOrWhiteSpace(target.monitorFriendlyDeviceName))
                                        {
                                            return target.monitorFriendlyDeviceName.Trim();
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            return isPrimary ? $"显示器 {index + 1} (主显示器)" : $"显示器 {index + 1}";
        }
    }
}
