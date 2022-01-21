﻿using System;
using System.IO;

namespace novideo_srgb
{
    public class ICCMatrixProfile
    {
        public Matrix matrix = Matrix.Zero3x3();
        public ToneCurve[] trcs = new ToneCurve[3];
        public ToneCurve[] vcgt;

        private ICCMatrixProfile()
        {
        }

        public static ICCMatrixProfile FromFile(string path)
        {
            var result = new ICCMatrixProfile();

            using (var reader = new ICCBinaryReader(File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read)))
            {
                var stream = reader.BaseStream;

                {
                    stream.Seek(0x24, SeekOrigin.Begin);
                    var magic = new string(reader.ReadChars(4));
                    if (magic != "acsp")
                    {
                        throw new ICCProfileException("Not an ICC profile");
                    }
                }

                {
                    stream.Seek(0xC, SeekOrigin.Begin);
                    var type = new string(reader.ReadChars(4));
                    if (type != "mntr")
                    {
                        throw new ICCProfileException("Not a display device profile");
                    }
                }

                {
                    stream.Seek(0x10, SeekOrigin.Begin);
                    var spaces = new string(reader.ReadChars(8));
                    if (spaces != "RGB XYZ ")
                    {
                        throw new ICCProfileException("Not an RGB profile with XYZ PCS");
                    }
                }

                stream.Seek(0x80, SeekOrigin.Begin);

                var tagCount = reader.ReadUInt32();

                var seenTags = 0;

                var useCLUT = false;
                for (uint i = 0; i < tagCount; i++)
                {
                    stream.Seek(0x80 + 4 + 12 * i, SeekOrigin.Begin);
                    var tagSig = new string(reader.ReadChars(4));

                    var offset = reader.ReadUInt32();
                    var size = reader.ReadUInt32();

                    reader.BaseStream.Seek(offset, SeekOrigin.Begin);

                    var index = Array.IndexOf(new[] { 'r', 'g', 'b' }, tagSig[0]);

                    if (tagSig == "A2B1")
                    {
                        useCLUT = true;
                        var typeSig = new string(reader.ReadChars(4));
                        if (typeSig != "mft2")
                        {
                            throw new ICCProfileException(tagSig + " is not of lut16Type");
                        }

                        reader.ReadUInt32();

                        var inputChannels = reader.ReadByte();
                        if (inputChannels != 3)
                        {
                            throw new ICCProfileException(tagSig + " must have 3 input channels");
                        }

                        var outputChannels = reader.ReadByte();
                        if (outputChannels != 3)
                        {
                            throw new ICCProfileException(tagSig + " must have 3 output channels");
                        }

                        var lutPoints = reader.ReadByte();

                        reader.ReadByte();
                        for (var j = 0; j < 9; j++)
                        {
                            reader.ReadS15Fixed16();
                        }

                        var inputEntries = reader.ReadUInt16();
                        var outputEntries = reader.ReadUInt16();

                        var input = new ushort[3, inputEntries];
                        for (var j = 0; j < 3; j++)
                        {
                            for (var k = 0; k < inputEntries; k++)
                            {
                                input[j, k] = reader.ReadUInt16();
                            }
                        }

                        var pos = reader.BaseStream.Position;
                        var primaries = new Matrix[3];
                        {
                            var primaryIndex = lutPoints - 1;
                            for (var j = 0; j < 3; j++)
                            {
                                var primary = Matrix.Zero3x1();
                                primaries[2 - j] = primary;

                                reader.BaseStream.Seek(pos + 2 * 3 * primaryIndex, SeekOrigin.Begin);
                                for (var k = 0; k < 3; k++)
                                {
                                    primary[k, 0] = reader.ReadCIEXYZ();
                                }

                                primaryIndex *= lutPoints;
                            }
                        }
                        var grayscale = new LutToneCurve[3];
                        {
                            var tables = new ushort[3][];
                            for (var j = 0; j < 3; j++)
                            {
                                tables[j] = new ushort[lutPoints];
                            }

                            for (var j = 0; j < lutPoints; j++)
                            {
                                reader.BaseStream.Seek(pos + 2 * 3 * j * (lutPoints * lutPoints + lutPoints + 1),
                                    SeekOrigin.Begin);
                                for (var k = 0; k < 3; k++)
                                {
                                    tables[k][j] = reader.ReadUInt16();
                                }
                            }

                            for (var j = 0; j < 3; j++)
                            {
                                grayscale[j] = new LutToneCurve(tables[j], 32768);
                            }
                        }

                        var black = Matrix.FromValues(new[,]
                        {
                            { grayscale[0].SampleAt(0) }, { grayscale[1].SampleAt(0) }, { grayscale[2].SampleAt(0) }
                        });

                        var Mprime = Matrix.Zero3x3();
                        for (var j = 0; j < 3; j++)
                        {
                            var purePrimary = primaries[j] - black;
                            for (var k = 0; k < 3; k++)
                            {
                                Mprime[k, j] = purePrimary[k, 0] / purePrimary[1, 0];
                            }
                        }

                        var M = Mprime * Matrix.FromDiagonal(Mprime.Inverse() * Colorimetry.D50);
                        var Minv = M.Inverse();
                        result.matrix = M;

                        var output = new LutToneCurve[3];
                        for (var j = 0; j < 3; j++)
                        {
                            var table = new ushort[outputEntries];
                            for (var k = 0; k < outputEntries; k++)
                            {
                                table[k] = reader.ReadUInt16();
                            }

                            output[j] = new LutToneCurve(table);
                        }

                        var trcs = new double[3][];
                        for (var j = 0; j < 3; j++)
                        {
                            trcs[j] = new double[inputEntries];
                        }

                        for (var j = 0; j < inputEntries - 1; j++)
                        {
                            var values = Matrix.Zero3x1();
                            for (var k = 0; k < 3; k++)
                            {
                                values[k, 0] = output[k]
                                    .SampleAt(grayscale[k].SampleAt((double)input[k, j] / ushort.MaxValue));
                            }

                            var toneResponse = Minv * values;
                            for (var k = 0; k < 3; k++)
                            {
                                trcs[k][j] = Math.Max(toneResponse[k, 0], 0);
                            }
                        }

                        for (var j = 0; j < 3; j++)
                        {
                            trcs[j][inputEntries - 1] = 1;
                            result.trcs[j] = new DoubleToneCurve(trcs[j]);
                        }
                    }
                    else if (tagSig.EndsWith("TRC") && !useCLUT)
                    {
                        var typeSig = new string(reader.ReadChars(4));
                        if (typeSig != "curv")
                        {
                            throw new ICCProfileException(tagSig + " is not of curveType");
                        }

                        reader.ReadUInt32();

                        var numEntries = reader.ReadUInt32();

                        ToneCurve curve;
                        if (numEntries == 1)
                        {
                            var gamma = reader.ReadU8Fixed8();
                            curve = new GammaToneCurve(gamma);
                        }
                        else
                        {
                            var entries = new ushort[numEntries];
                            for (uint j = 0; j < numEntries; j++)
                            {
                                entries[j] = reader.ReadUInt16();
                            }

                            curve = new LutToneCurve(entries);
                        }

                        result.trcs[index] = curve;

                        seenTags++;
                    }
                    else if (tagSig.EndsWith("XYZ") && !useCLUT)
                    {
                        reader.ReadUInt32();
                        reader.ReadUInt32();

                        for (var j = 0; j < 3; j++)
                        {
                            result.matrix[j, index] = reader.ReadS15Fixed16();
                        }

                        seenTags++;
                    }
                    else if (tagSig == "vcgt")
                    {
                        reader.ReadChars(4);
                        reader.ReadUInt32();
                        var type = reader.ReadUInt32();
                        if (type != 0) throw new ICCProfileException("Only VCGT type 0 is supported");

                        var numChannels = reader.ReadUInt16();
                        var numEntries = reader.ReadUInt16();
                        var entrySize = reader.ReadUInt16();

                        if (numChannels != 3) throw new ICCProfileException("Only VCGT with 3 channels is supported");

                        result.vcgt = new ToneCurve[3];
                        for (var j = 0; j < 3; j++)
                        {
                            var values = new ushort[numEntries];
                            for (var k = 0; k < numEntries; k++)
                            {
                                switch (entrySize)
                                {
                                    case 1:
                                        values[k] = (ushort)(reader.ReadByte() * ushort.MaxValue / byte.MaxValue);
                                        break;
                                    case 2:
                                        values[k] = reader.ReadUInt16();
                                        break;
                                    default:
                                        throw new ICCProfileException("Only 8 and 16 bit VCGT is supported");
                                }
                            }

                            result.vcgt[j] = new LutToneCurve(values);
                        }
                    }
                }

                if (seenTags != 6)
                {
                    throw new ICCProfileException("Missing required tags for curves + matrix profile");
                }

                if (!useCLUT)
                {
                    result.matrix = Colorimetry.XYZScaleToD50(result.matrix);
                }
            }

            return result;
        }
    }
}