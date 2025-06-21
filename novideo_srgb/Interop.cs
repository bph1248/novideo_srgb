using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace novideo_srgb
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct NovideoSrgbApplyInfo
    {
        public bool enable_clamp;
        public int color_space_target;
        public int primaries_source;
        public char* profile_path;
        public bool calibrate_gamma;
        public int gamma_target;
        public double gamma_value;
        public double black_output_offset;
        public bool disable_optimization;
    }

    public static class Interop
    {
        [UnmanagedCallersOnly(EntryPoint = "novideo_srgb_apply")]
        public unsafe static int NovideoSrgbApply(NovideoSrgbApplyInfo* info)
        {
            try
            {
                MainViewModel.UpdateMonitors(info);

                return 42;
            }
            catch (Exception e)
            {
                string dll_path = Assembly.GetExecutingAssembly().Location;
                string dll_dir = Path.GetDirectoryName(dll_path)!;

                string log_path = Path.Combine(dll_dir, "novideo_srgb.log");

                File.WriteAllText(log_path, e.ToString());

                return -1;
            }
        }
    }
}
