using NvAPIWrapper.Display;
using System.Collections.ObjectModel;
using System.Linq;

namespace novideo_srgb
{
    public enum GammaTarget : int
    {
        Srgb = 0,
        Bt1886,
        CustomAbsolute,
        CustomRelative,
        Lstar
    }

    public enum PrimariesSource : int
    {
        Edid = 0,
        Profile
    }

    public class MainViewModel
    {
        public unsafe static void UpdateMonitors(NovideoSrgbApplyInfo* info)
        {
            var monitors = new ObservableCollection<MonitorData>();
            var win_displays = WindowsDisplayAPI.Display.GetDisplays();
            foreach (var display in Display.GetDisplays())
            {
                var display_path = win_displays.First(win_display => win_display.DisplayName == display.Name).DevicePath;
                var monitor = new MonitorData(
                    display,
                    display_path,
                    info->enable_clamp,
                    info->color_space_target,
                    (PrimariesSource)info->primaries_source,
                    info->profile_path != null ? new string(info->profile_path) : "",
                    info->calibrate_gamma,
                    (GammaTarget)info->gamma_target,
                    info->gamma_value,
                    info->black_output_offset,
                    info->disable_optimization
                );

                monitors.Add(monitor);
            }

            foreach (var monitor in monitors)
            {
                monitor.UpdateClamp();
            }
        }
    }
}
