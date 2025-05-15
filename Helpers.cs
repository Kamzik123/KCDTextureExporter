
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Numerics;
using DirectXTexNet;
using KCDTextureExporter.DDS;

namespace KCDTextureExporter
{
    public static class Helpers
    {
        public static (ScratchImage? image, ScratchImage? alpha, List<string> mipFiles, List<string> alphaMipFiles)
            LoadGameDDS(string ddsFilePath,
                        bool saveRawDDS = false,
                        bool deleteSourceFiles = false,
                        string outputPath = "",
                        bool isOutputFolder = false)
        {
            ScratchImage? image = null;
            ScratchImage? alpha = null;
            var mipFiles = new List<string>();
            var alphaMipFiles = new List<string>();
            var mips = new List<byte[]>();
            var alphaMips = new List<byte[]>();

            for (int i = 1; i < 64; i++)
            {
                var path = ddsFilePath + "." + i;
                if (!File.Exists(path)) break;
                mips.Insert(0, File.ReadAllBytes(path));
                mipFiles.Add(path);
            }

            for (int i = 1; i < 64; i++)
            {
                var path = ddsFilePath + "." + i + "a";
                if (!File.Exists(path)) break;
                alphaMips.Insert(0, File.ReadAllBytes(path));
                alphaMipFiles.Add(path);
            }

            var ddsFile = new DDSFile(ddsFilePath, false);
            DDSFile? aDDSFile = File.Exists(ddsFilePath + ".a")
                                 ? new DDSFile(ddsFilePath + ".a", true)
                                 : null;

            // merge all mip bytes + main DDS
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                foreach (var b in mips) bw.Write(b);
                bw.Write(ddsFile.Data!);
                ddsFile.Data = ms.ToArray();
            }

            // optionally save raw DDS
            if (saveRawDDS)
            {
                string target = isOutputFolder
                    ? Path.Combine(
                        string.IsNullOrEmpty(outputPath)
                          ? Path.GetDirectoryName(ddsFilePath)!
                          : outputPath,
                        Path.GetFileNameWithoutExtension(ddsFilePath) + ".dds")
                    : outputPath;

                if (string.IsNullOrEmpty(target))
                    throw new Exception("Incorrect output path.");

                ddsFile.Write(target);
            }

            // alpha file
            if (aDDSFile != null)
            {
                using (var ms = new MemoryStream())
                using (var bw = new BinaryWriter(ms))
                {
                    foreach (var b in alphaMips) bw.Write(b);
                    bw.Write(aDDSFile.Data!);
                    aDDSFile.Data = ms.ToArray();
                }

                using (var ms = new MemoryStream(aDDSFile.Write()))
                {
                    var ptr = GCHandle.Alloc(ms.ToArray(), GCHandleType.Pinned);
                    try
                    {
                        alpha = TexHelper.Instance.LoadFromDDSMemory(
                                   ptr.AddrOfPinnedObject(),
                                   (int)ms.Length,
                                   DDS_FLAGS.ALLOW_LARGE_FILES);
                    }
                    finally { ptr.Free(); }
                }
            }

            // final color image
            using (var ms = new MemoryStream(ddsFile.Write()))
            {
                var ptr = GCHandle.Alloc(ms.ToArray(), GCHandleType.Pinned);
                try
                {
                    image = TexHelper.Instance.LoadFromDDSMemory(
                                ptr.AddrOfPinnedObject(),
                                (int)ms.Length,
                                DDS_FLAGS.ALLOW_LARGE_FILES);
                }
                finally { ptr.Free(); }
            }

            return (image, alpha, mipFiles, alphaMipFiles);
        }

        public static byte[] GetPixelData(ScratchImage img)
        {
            int size = (int)img.GetPixelsSize();
            var data = new byte[size];
            Marshal.Copy(img.GetPixels(), data, 0, size);
            return data;
        }

        public static byte[] ReconstructZ(byte[] pixelData, bool pack)
        {
            var vectors = new List<Vector2>();
            using (var ms = new MemoryStream(pixelData))
            using (var br = new BinaryReader(ms))
            {
                while (ms.Position < ms.Length)
                {
                    vectors.Add(new Vector2(br.ReadSingle(), br.ReadSingle()));
                    ms.Position += 4;
                }
            }

            using (var ms = new MemoryStream(pixelData))
            using (var bw = new BinaryWriter(ms))
            {
                for (int i = 0; i < vectors.Count; i++)
                {
                    var v = vectors[i];
                    float z = MathF.Sqrt(1 - Vector2.Dot(v, v));
                    // write X,Y,Z,1.0f
                    bw.Write(pack
                        ? MathF.Pow((v.Y + 1) / 2, 2.2f)
                        : v.Y);
                    bw.Write(pack
                        ? MathF.Pow((v.X + 1) / 2, 2.2f)
                        : v.X);
                    bw.Write(pack
                        ? MathF.Pow((z + 1) / 2, 2.2f)
                        : z);
                    bw.Write(1.0f);
                }
                return ms.ToArray();
            }
        }

        public static byte[] MergeAlpha(byte[] colorData, byte[] alphaData)
        {
            var colors = new List<Vector4>();
            using (var ms = new MemoryStream(colorData))
            using (var br = new BinaryReader(ms))
            {
                while (ms.Position < ms.Length)
                    colors.Add(new Vector4(
                        br.ReadSingle(),
                        br.ReadSingle(),
                        br.ReadSingle(),
                        br.ReadSingle()));
            }

            var alphas = new List<float>();
            using (var ms = new MemoryStream(alphaData))
            using (var br = new BinaryReader(ms))
                while (ms.Position < ms.Length)
                    alphas.Add(br.ReadSingle());

            using (var ms = new MemoryStream(colorData))
            using (var bw = new BinaryWriter(ms))
            {
                for (int i = 0; i < colors.Count; i++)
                {
                    bw.Write(colors[i].X);
                    bw.Write(colors[i].Y);
                    bw.Write(colors[i].Z);
                    bw.Write(alphas[i]);
                }
                return ms.ToArray();
            }
        }

        public static byte[] QuantizeIDPixels(byte[] pixels, bool isSRGB)
        {
            using (var inMs = new MemoryStream(pixels))
            using (var br = new BinaryReader(inMs))
            using (var outMs = new MemoryStream())
            using (var bw = new BinaryWriter(outMs))
            {
                while (inMs.Position < inMs.Length)
                {
                    float r = br.ReadSingle();
                    float g = br.ReadSingle();
                    float b = br.ReadSingle();
                    float a = br.ReadSingle();

                    if (isSRGB)
                    {
                        r = MathF.Pow(r, 1 / 2.2f);
                        g = MathF.Pow(g, 1 / 2.2f);
                        b = MathF.Pow(b, 1 / 2.2f);
                        bw.Write((byte)MathF.Ceiling(r * 255));
                        bw.Write((byte)MathF.Ceiling(g * 255));
                        bw.Write((byte)MathF.Ceiling(b * 255));
                        bw.Write((byte)MathF.Floor(a * 255));
                    }
                    else
                    {
                        bw.Write((byte)MathF.Floor(r * 255));
                        bw.Write((byte)MathF.Floor(g * 255));
                        bw.Write((byte)MathF.Floor(b * 255));
                        bw.Write((byte)MathF.Floor(a * 255));
                    }
                }
                return outMs.ToArray();
            }
        }
    }
}
