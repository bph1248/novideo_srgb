using EDIDParser;
using NvAPIWrapper.Display;
using System;

namespace novideo_srgb
{
    public class MonitorData
    {
        public uint DisplayId { get; }
        public bool EnableClamp { get; }
        public Colorimetry.ColorSpace ColorSpaceTarget { get; }
        public PrimariesSource PrimariesSource { get; }
        public EDID Edid { get; }
        public Colorimetry.ColorSpace EdidColorSpace { get; }
        public string ProfilePath { get; }
        public bool CalibrateGamma { get; }
        public GammaTarget GammaTarget { get; }
        public double GammaValue { get; }
        public double BlackOutputOffset { get; }
        public bool DisableOptimization { get; }

        public MonitorData(Display display, string display_path)
        {
            DisplayId = display.DisplayDevice.DisplayId;
            Edid = Novideo.GetEDID(display_path, display);

            var coords = Edid.DisplayParameters.ChromaticityCoordinates;
            EdidColorSpace = new Colorimetry.ColorSpace
            {
                Red = new Colorimetry.Point { X = Math.Round(coords.RedX, 3), Y = Math.Round(coords.RedY, 3) },
                Green = new Colorimetry.Point { X = Math.Round(coords.GreenX, 3), Y = Math.Round(coords.GreenY, 3) },
                Blue = new Colorimetry.Point { X = Math.Round(coords.BlueX, 3), Y = Math.Round(coords.BlueY, 3) },
                White = Colorimetry.D65
            };
        }

        public MonitorData(
            Display display,
            string display_path,
            bool enable_clamp,
            int color_space_target,
            PrimariesSource primaries_source,
            string profile_path,
            bool calibrate_gamma,
            GammaTarget gamma_target,
            double gamma_value,
            double black_output_offset,
            bool disable_optimization
        ) : this(display, display_path)
        {
            EnableClamp = enable_clamp;
            ColorSpaceTarget = Colorimetry.ColorSpaces[color_space_target];
            PrimariesSource = primaries_source;
            ProfilePath = profile_path;
            CalibrateGamma = calibrate_gamma;
            GammaTarget = gamma_target;
            GammaValue = gamma_value;
            BlackOutputOffset = black_output_offset;
            DisableOptimization = disable_optimization;
        }

        public void UpdateClamp()
        {
            if (!EnableClamp)
            {
                Novideo.DisableColorSpaceConversion(DisplayId);

                return;
            }

            switch (PrimariesSource)
            {
                case PrimariesSource.Edid:
                    Novideo.SetColorSpaceConversion(DisplayId, Colorimetry.RGBToRGB(ColorSpaceTarget, EdidColorSpace));

                    return;
                case PrimariesSource.Profile:
                    var profile = ICCMatrixProfile.FromFile(ProfilePath);

                    if (CalibrateGamma)
                    {
                        var trcBlack = Matrix.FromValues(new[,]
                            {
                                { profile.trcs[0].SampleAt(0) },
                                { profile.trcs[1].SampleAt(0) },
                                { profile.trcs[2].SampleAt(0) }
                            }
                        );
                        var black = (profile.matrix * trcBlack)[1];

                        ToneCurve tone_curve = GammaTarget switch
                        {
                            GammaTarget.Srgb => new SrgbEOTF(black),
                            GammaTarget.Bt1886 => new GammaToneCurve(2.4, black, 0),
                            GammaTarget.CustomAbsolute => new GammaToneCurve(GammaValue, black, BlackOutputOffset / 100),
                            GammaTarget.CustomRelative => new GammaToneCurve(GammaValue, black, BlackOutputOffset / 100, true),
                            GammaTarget.Lstar => new LstarEOTF(black),
                            _ => throw new NotSupportedException("Unsupported gamma target: " + GammaTarget)
                        };

                        Novideo.SetColorSpaceConversion(DisplayId, profile, ColorSpaceTarget, tone_curve, DisableOptimization);
                    }
                    else
                    {
                        Novideo.SetColorSpaceConversion(DisplayId, profile, ColorSpaceTarget);
                    }

                    break;
                default:
                    throw new NotSupportedException("Unsupported primaries source: " + PrimariesSource);
            }
        }
    }
}
