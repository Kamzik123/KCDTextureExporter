using System.IO;
using DirectXTexNet;

namespace KCDTextureExporter.DDS
{
    public class ExtendedHeader
    {
        public DXGI_FORMAT dxgiFormat { get; set; } = DXGI_FORMAT.UNKNOWN;
        public uint resourceDimension { get; set; }
        public uint miscFlag { get; set; }
        public uint arraySize { get; set; }
        public uint miscFlags2 { get; set; }
        public ExtendedHeader()
        {

        }

        public ExtendedHeader(BinaryReader br)
        {
            Read(br);
        }

        public void Read(BinaryReader br)
        {
            dxgiFormat = (DXGI_FORMAT)br.ReadInt32();
            resourceDimension = br.ReadUInt32();
            miscFlag = br.ReadUInt32();
            arraySize = br.ReadUInt32();
            miscFlags2 = br.ReadUInt32();
        }

        public void Write(BinaryWriter bw)
        {
            bw.Write((int)dxgiFormat);
            bw.Write(resourceDimension);
            bw.Write(miscFlag);
            bw.Write(arraySize);
            bw.Write(miscFlags2);
        }
    }
}
