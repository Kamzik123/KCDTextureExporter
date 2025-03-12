using System.IO;

namespace KCDTextureExporter.DDS
{
    public class DDSFile
    {
        public static uint Magic = 0x20534444;
        public bool TrimmedMagic = false;
        public Header Header { get; set; } = new();
        public byte[]? Data { get; set; }
        public DDSFile(bool _trimmedMagic)
        {
            TrimmedMagic = _trimmedMagic;
        }

        public DDSFile(string fileName, bool _trimmedMagic)
        {
            TrimmedMagic = _trimmedMagic;
            Read(fileName);
        }

        public DDSFile(Stream stream, bool _trimmedMagic)
        {
            TrimmedMagic = _trimmedMagic;
            Read(stream);
        }

        public DDSFile(BinaryReader br, bool _trimmedMagic)
        {
            TrimmedMagic = _trimmedMagic;
            Read(br);
        }

        public void Read(string fileName)
        {
            using (MemoryStream ms = new(File.ReadAllBytes(fileName)))
            {
                Read(ms);
            }
        }

        public void Read(Stream stream)
        {
            using (BinaryReader br = new(stream))
            {
                Read(br);
            }
        }

        public void Read(BinaryReader br)
        {
            if (!TrimmedMagic)
            {
                uint _magic = br.ReadUInt32();

                if (_magic != Magic)
                {
                    throw new Exception("Not a DDS file.");
                }
            }

            Header = new(br);
            Data = br.ReadBytes((int)(br.BaseStream.Length - br.BaseStream.Position));
        }

        public void Write(string fileName)
        {
            using (MemoryStream ms = new())
            {
                Write(ms);

                File.WriteAllBytes(fileName, ms.ToArray());
            }
        }

        public void Write(Stream stream)
        {
            using (BinaryWriter bw = new(stream))
            {
                Write(bw);
            }
        }

        public byte[] Write()
        {
            using (MemoryStream ms = new())
            {
                Write(ms);

                return ms.ToArray();
            }
        }

        public void Write(BinaryWriter bw)
        {
            bw.Write(Magic);
            Header.Write(bw);
            bw.Write(Data!);
        }
    }
}
