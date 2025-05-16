using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using DirectXTexNet;
using KCDTextureExporter.DDS;

namespace KCDTextureExporter
{
    public static class ImageConverter
    {
        public static void ConvertImage(
            string filePath,
            bool saveRawDDS,
            bool separateGlossMap,
            string outputPath = "",
            bool deleteSourceFiles = false,
            bool isOutputFolder = false)
        {
            // 1) Detect ID-map by suffix
            bool isIDMap = Path.GetFileNameWithoutExtension(filePath)
                               .EndsWith("_id", StringComparison.OrdinalIgnoreCase);

            // 2) Load DDS + optional alpha, collect temp mip filenames
            var (image, alpha, mipFiles, alphaMipFiles)
                = LoadGameDDS(filePath, saveRawDDS, deleteSourceFiles, outputPath, isOutputFolder);
            if (image == null)
                throw new InvalidOperationException("Failed to load DDS image.");

            // 3) Figure out formats
            var fmt = image.GetImage(0).Format;
            bool isNormal = fmt == DXGI_FORMAT.BC5_SNORM || fmt == DXGI_FORMAT.BC5_UNORM;
            bool isSRGB = TexHelper.Instance.IsSRGB(fmt);

            // 4) Decompress or convert to float RGBA for processing
            ScratchImage work = TexHelper.Instance.IsCompressed(fmt)
                ? image.Decompress(0, DXGI_FORMAT.R32G32B32A32_FLOAT)
                : (fmt != DXGI_FORMAT.R32G32B32A32_FLOAT
                    ? image.Convert(DXGI_FORMAT.R32G32B32A32_FLOAT, TEX_FILTER_FLAGS.DEFAULT, 0.5f)
                    : image);

            // 5) If normal map, reconstruct Z channel
            if (isNormal)
            {
                byte[] rec = ReconstructZ(GetPixelData(work), true);
                Marshal.Copy(rec, 0, work.GetImage(0).Pixels, rec.Length);
            }

            // 6) Handle alpha/gloss: split or merge
            if (alpha != null)
            {
                if (separateGlossMap)
                {
                    // extract gloss into its own TIFF
                    ScratchImage aImg = alpha;
                    var aFmt = aImg.GetImage(0, 0, 0).Format;
                    if (TexHelper.Instance.IsCompressed(aFmt))
                        aImg = aImg.Decompress(0, DXGI_FORMAT.R8_UNORM);
                    else if (aFmt != DXGI_FORMAT.R8_UNORM)
                        aImg = aImg.Convert(0, DXGI_FORMAT.R8_UNORM, TEX_FILTER_FLAGS.DEFAULT, 0.5f);

                    string dir = isOutputFolder
                        ? (string.IsNullOrEmpty(outputPath) ? Path.GetDirectoryName(filePath)! : outputPath)
                        : throw new Exception("Output must be a folder to separate gloss.");
                    string alphaPath = Path.Combine(dir, Path.GetFileNameWithoutExtension(filePath) + "_alpha.tif");
                    aImg.SaveToWICFile(0, WIC_FLAGS.NONE, TexHelper.Instance.GetWICCodec(WICCodecs.TIFF), alphaPath);
                    aImg.Dispose();
                }
                else
                {
                    // merge alpha back into float RGBA
                    ScratchImage aImg = alpha;
                    var aFmt = aImg.GetImage(0, 0, 0).Format;
                    if (TexHelper.Instance.IsCompressed(aFmt))
                        aImg = aImg.Decompress(0, DXGI_FORMAT.R32_FLOAT);
                    else if (aFmt != DXGI_FORMAT.R32_FLOAT)
                        aImg = aImg.Convert(0, DXGI_FORMAT.R32_FLOAT, TEX_FILTER_FLAGS.DEFAULT, 0.5f);

                    byte[] merged = MergeAlpha(GetPixelData(work), GetPixelData(aImg));
                    Marshal.Copy(merged, 0, work.GetImage(0).Pixels, merged.Length);
                    aImg.Dispose();
                }
                alpha.Dispose();
            }

            if (!isNormal)
            {
                if (isIDMap)
                {
                    byte[] q = QuantizeIDPixels(GetPixelData(work), isSRGB);
                    work = work.Convert(DXGI_FORMAT.R8G8B8A8_UNORM, TEX_FILTER_FLAGS.DEFAULT, 0.0f);
                    Marshal.Copy(q, 0, work.GetImage(0).Pixels, q.Length);
                }
                else
                {
                    work = work.Convert(
                        isSRGB ? DXGI_FORMAT.R8G8B8A8_UNORM_SRGB : DXGI_FORMAT.R8G8B8A8_UNORM,
                        TEX_FILTER_FLAGS.DEFAULT,
                        0.5f);
                }
            }

            // 8) Save final TIFF
            if (isOutputFolder)
            {
                string dir = string.IsNullOrEmpty(outputPath) ? Path.GetDirectoryName(filePath)! : outputPath;
                work.SaveToWICFile(0, WIC_FLAGS.NONE,
                    TexHelper.Instance.GetWICCodec(WICCodecs.TIFF),
                    Path.Combine(dir, Path.GetFileNameWithoutExtension(filePath) + ".tif"));
            }
            else
            {
                if (string.IsNullOrEmpty(outputPath))
                    throw new Exception("Incorrect output path.");
                work.SaveToWICFile(0, WIC_FLAGS.NONE,
                    TexHelper.Instance.GetWICCodec(WICCodecs.TIFF),
                    outputPath);
            }

            // 9) Cleanup
            image.Dispose();
            work.Dispose();

            if (deleteSourceFiles)
            {
                foreach (var f in mipFiles) if (File.Exists(f)) File.Delete(f);
                foreach (var f in alphaMipFiles) if (File.Exists(f)) File.Delete(f);
                if (!(saveRawDDS && isOutputFolder && string.IsNullOrEmpty(outputPath)))
                {
                    if (File.Exists(filePath)) File.Delete(filePath);
                    if (File.Exists(filePath + ".a")) File.Delete(filePath + ".a");
                }
            }
        }

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

            // collect color mips
            for (int i = 1; i < 64; i++)
            {
                var p = ddsFilePath + "." + i;
                if (!File.Exists(p)) break;
                mips.Insert(0, File.ReadAllBytes(p));
                mipFiles.Add(p);
            }

            // collect alpha mips
            for (int i = 1; i < 64; i++)
            {
                var p = ddsFilePath + "." + i + "a";
                if (!File.Exists(p)) break;
                alphaMips.Insert(0, File.ReadAllBytes(p));
                alphaMipFiles.Add(p);
            }

            // read DDSFile (color)
            var ddsFile = new DDSFile(ddsFilePath, false);
            DDSFile? alphaDDS = File.Exists(ddsFilePath + ".a")
                               ? new DDSFile(ddsFilePath + ".a", true)
                               : null;

            // merge color mips + main DDS
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                foreach (var b in mips) bw.Write(b);
                bw.Write(ddsFile.Data!);
                ddsFile.Data = ms.ToArray();
            }

            int expectedSize = ComputePixelDataSize(
            ddsFile.Header.GetPixelFormat(),
            ddsFile.Header.Width,
            ddsFile.Header.Height,
            ddsFile.Header.MipMapCount);
                    if (ddsFile.Data.Length < expectedSize)
                        throw new Exception("Failed to load all necessary MIP levels.");

            if (saveRawDDS)
            {
                string tgt = isOutputFolder
                    ? Path.Combine(string.IsNullOrEmpty(outputPath) ? Path.GetDirectoryName(ddsFilePath)! : outputPath,
                                  Path.GetFileNameWithoutExtension(ddsFilePath) + ".dds")
                    : outputPath;
                if (string.IsNullOrEmpty(tgt)) throw new Exception("Incorrect output path.");
                ddsFile.Write(tgt);
            }

            // read alpha DDS if present
            if (alphaDDS != null)
            {
                using (var ms = new MemoryStream())
                using (var bw = new BinaryWriter(ms))
                {
                    foreach (var b in alphaMips) bw.Write(b);
                    bw.Write(alphaDDS.Data!);
                    alphaDDS.Data = ms.ToArray();
                }

                int alphaExpectedSize = ComputePixelDataSize(
                alphaDDS.Header.GetPixelFormat(),
                alphaDDS.Header.Width,
                alphaDDS.Header.Height,
                alphaDDS.Header.MipMapCount);
                    if (alphaDDS.Data.Length < alphaExpectedSize)
                        throw new Exception("Failed to load all necessary alpha MIP levels.");

                var data = alphaDDS.Write();
                var h = GCHandle.Alloc(data, GCHandleType.Pinned);
                try
                {
                    alpha = TexHelper.Instance.LoadFromDDSMemory(
                                h.AddrOfPinnedObject(),
                                data.Length,
                                DDS_FLAGS.ALLOW_LARGE_FILES);
                }
                finally { h.Free(); }
            }

            var imgData = ddsFile.Write();
            var h2 = GCHandle.Alloc(imgData, GCHandleType.Pinned);
            try
            {
                image = TexHelper.Instance.LoadFromDDSMemory(
                        h2.AddrOfPinnedObject(),
                        imgData.Length,
                        DDS_FLAGS.ALLOW_LARGE_FILES);
            }
            finally { h2.Free(); }

            return (image, alpha, mipFiles, alphaMipFiles);
        }

        public static byte[] GetPixelData(ScratchImage img)
        {
            int size = (int)img.GetPixelsSize();
            var buf = new byte[size];
            Marshal.Copy(img.GetPixels(), buf, 0, size);
            return buf;
        }

        public static byte[] ReconstructZ(byte[] pixelData, bool pack)
        {
            var vectors = new List<Vector2>();
            // read only when at least 16 bytes remain (4 X + 4 Y + 4 Z + 4 A)
            using (var ms = new MemoryStream(pixelData))
            using (var br = new BinaryReader(ms))
                while (ms.Position + 16 <= ms.Length)
                {
                    float x = br.ReadSingle();
                    float y = br.ReadSingle();
                    vectors.Add(new Vector2(x, y));
                    ms.Position += 8; // skip old Z and A
                }

            // write into a fresh, expandable buffer of the same size
            byte[] outData = new byte[pixelData.Length];
            using (var ms = new MemoryStream(outData))
            using (var bw = new BinaryWriter(ms))
                foreach (var v in vectors)
                {
                    float z = MathF.Sqrt(MathF.Max(0, 1 - Vector2.Dot(v, v)));
                    bw.Write(pack ? MathF.Pow((v.Y + 1) / 2, 2.2f) : v.Y);
                    bw.Write(pack ? MathF.Pow((v.X + 1) / 2, 2.2f) : v.X);
                    bw.Write(pack ? MathF.Pow((z + 1) / 2, 2.2f) : z);
                    bw.Write(1.0f);
                }

            return outData;
        }

        public static byte[] MergeAlpha(byte[] pixelData, byte[] alphaPixelData)
        {
            var colors = new List<Vector4>();
            using (var ms = new MemoryStream(pixelData))
            using (var br = new BinaryReader(ms))
                while (ms.Position < ms.Length)
                    colors.Add(new Vector4(
                        br.ReadSingle(),
                        br.ReadSingle(),
                        br.ReadSingle(),
                        br.ReadSingle()));

            var alphas = new List<float>();
            using (var ms = new MemoryStream(alphaPixelData))
            using (var br = new BinaryReader(ms))
                while (ms.Position < ms.Length)
                    alphas.Add(br.ReadSingle());

            using (var ms = new MemoryStream(pixelData))
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
            using var inMs = new MemoryStream(pixels);
            using var br = new BinaryReader(inMs);
            using var outMs = new MemoryStream();
            using var bw = new BinaryWriter(outMs);

            while (inMs.Position < inMs.Length)
            {
                float r = br.ReadSingle();
                float g = br.ReadSingle();
                float b = br.ReadSingle();
                float a = br.ReadSingle();

                if (isSRGB)
                {
                    r = MathF.Pow(r, 1.0f / 2.2f);
                    g = MathF.Pow(g, 1.0f / 2.2f);
                    b = MathF.Pow(b, 1.0f / 2.2f);
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

        private static int ComputePixelDataSize(DXGI_FORMAT fmt, int w, int h, int mipCount)
        {
            int bits = TexHelper.Instance.BitsPerPixel(fmt);
            int size = w * h * bits;
            int total = size;
            for (int i = 1; i < mipCount; i++)
            {
                size /= 4;
                total += size;
            }
            return total / 8;
        }
    }
}
