using System.IO;
using DirectXTexNet;

namespace KCDTextureExporter.DDS
{
    public class PixelFormat
    {
        public int Size { get; set; } = 32; //Always 32
        public int Flags { get; set; }
        public int FourCC { get; set; }
        public int RGBBitCount { get; set; }
        public int RBitMask { get; set; }
        public int GBitMask { get; set; }
        public int BBitMask { get; set; }
        public int ABitMask { get; set; }
        public PixelFormat()
        {

        }

        public PixelFormat(BinaryReader br)
        {
            Read(br);
        }

        public void Read(BinaryReader br)
        {
            int size = br.ReadInt32();

            if (size != Size)
            {
                throw new Exception("Error reading DDS PixelFormat.");
            }

            Flags = br.ReadInt32();
            FourCC = br.ReadInt32();
            RGBBitCount = br.ReadInt32();
            RBitMask = br.ReadInt32();
            GBitMask = br.ReadInt32();
            BBitMask = br.ReadInt32();
            ABitMask = br.ReadInt32();
        }

        public void Write(BinaryWriter bw)
        {
            bw.Write(Size);
            bw.Write(Flags);
            bw.Write(FourCC);
            bw.Write(RGBBitCount);
            bw.Write(RBitMask);
            bw.Write(GBitMask);
            bw.Write(BBitMask);
            bw.Write(ABitMask);
        }

        public DXGI_FORMAT GetPixelFormat()
        {
            switch (FourCC)
            {
                case 0x31545844:
                    return DXGI_FORMAT.BC1_UNORM;

                case 0x32545844:
                case 0x33545844:
                    return DXGI_FORMAT.BC2_UNORM;

                case 0x34545844:
                case 0x35545844:
                    return DXGI_FORMAT.BC3_UNORM;
            }

            return DXGI_FORMAT.UNKNOWN;
        }
    }
}
