using DirectXTexNet;
using System.IO;

namespace KCDTextureExporter.DDS
{
    public class Header
    {
        public int Size { get; set; } = 124; //Always 124
        public int Flags { get; set; }
        public int Height { get; set; }
        public int Width { get; set; }
        public int PitchOrLinearSize { get; set; }
        public int Depth { get; set; }
        public int MipMapCount { get; set; }
        public int[] Reserved1 { get; set; } = new int[11];
        public PixelFormat ddspf { get; set; } = new();
        public int Caps { get; set; }
        public int Caps2 { get; set; }
        public int Caps3 { get; set; }
        public int Caps4 { get; set; }
        public int Reserved2 { get; set; }
        public ExtendedHeader extendedHeader { get; set; } = new();
        public Header()
        {

        }

        public Header(BinaryReader br)
        {
            Read(br);
        }

        public void Read(BinaryReader br)
        {
            int size = br.ReadInt32();

            if (size != Size)
            {
                throw new Exception("Error reading DDS Header.");
            }

            Flags = br.ReadInt32();
            Height = br.ReadInt32();
            Width = br.ReadInt32();
            PitchOrLinearSize = br.ReadInt32();
            Depth = br.ReadInt32();
            MipMapCount = br.ReadInt32();

            for (int i = 0; i < Reserved1.Length; i++)
            {
                Reserved1[i] = br.ReadInt32();
            }

            ddspf = new(br);
            Caps = br.ReadInt32();
            Caps2 = br.ReadInt32();
            Caps3 = br.ReadInt32();
            Caps4 = br.ReadInt32();
            Reserved2 = br.ReadInt32();

            if (ddspf.FourCC == 0x30315844)
            {
                extendedHeader = new(br);
            }
        }

        public void Write(BinaryWriter bw)
        {
            bw.Write(Size);
            bw.Write(Flags);
            bw.Write(Height);
            bw.Write(Width);
            bw.Write(PitchOrLinearSize);
            bw.Write(Depth);
            bw.Write(MipMapCount);

            foreach (var val in Reserved1)
            {
                bw.Write(val);
            }

            ddspf.Write(bw);
            bw.Write(Caps);
            bw.Write(Caps2);
            bw.Write(Caps3);
            bw.Write(Caps4);
            bw.Write(Reserved2);

            if (ddspf.FourCC == 0x30315844)
            {
                extendedHeader.Write(bw);
            }
        }

        public DXGI_FORMAT GetPixelFormat()
        {
            if (ddspf.FourCC == 0x30315844)
            {
                return extendedHeader.dxgiFormat;
            }

            return ddspf.GetPixelFormat();
        }
    }
}
