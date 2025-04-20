using DirectXTexNet;
using KCDTextureExporter.DDS;
using Microsoft.Win32;
using System.Configuration;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using System.Xml;
using System.Windows.Interop;

namespace KCDTextureExporter
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

        [StructLayout(LayoutKind.Sequential)]
        private struct FLASHWINFO
        {
            public uint cbSize;
            public IntPtr hwnd;
            public uint dwFlags;
            public uint uCount;
            public uint dwTimeout;
        }

        private const uint FLASHW_ALL = 0x3;
        private const uint FLASHW_TIMERNOFG = 0xC;

        public MainWindow()
        {
            InitializeComponent();

            TexHelper.LoadInstance();

            ReadSettingsFile();
        }

        private void Button_Convert_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                bool IsInputFolder = false;
                bool IsOutputFolder = false;

                if (Directory.Exists(TextBox_Input.Text))
                {
                    IsInputFolder = true;
                }

                if (Directory.Exists(TextBox_Output.Text))
                {
                    IsOutputFolder = true;
                }

                if (!IsOutputFolder)
                {
                    if (!string.IsNullOrEmpty(TextBox_Output.Text) && !TextBox_Output.Text.EndsWith(".dds", StringComparison.InvariantCultureIgnoreCase))
                    {
                        IsOutputFolder = true;

                        if (!Directory.Exists(TextBox_Output.Text))
                        {
                            Directory.CreateDirectory(TextBox_Output.Text);
                        }
                    }
                }

                if (string.IsNullOrEmpty(TextBox_Output.Text))
                {
                    IsOutputFolder = true;
                }

                if (IsInputFolder && !IsOutputFolder)
                {
                    throw new Exception("When an input folder is specified, you must specify an output folder instead of a file.");
                }

                if (IsInputFolder)
                {
                    var ddsFiles = Directory.EnumerateFiles(TextBox_Input.Text, "*.dds", (bool)CheckBox_Recursive.IsChecked! ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

                    if (!ddsFiles.Any())
                    {
                        throw new Exception("No .dds files were found in the selected input folder.");
                    }

                    Button_Convert.IsEnabled = false;

                    List<Task> tasks = BatchProcessFiles(TextBox_Input.Text, TextBox_Output.Text, (bool)CheckBox_SaveRawDDS.IsChecked!, (bool)CheckBox_SeparateGlossMap.IsChecked!, (bool)CheckBox_DeleteSourceFiles.IsChecked!, (bool)CheckBox_Recursive.IsChecked!);

                    Task.WhenAll(tasks).ContinueWith(tasks =>
                    {
                        Button_Convert.Dispatcher.Invoke(() =>
                        {
                            Button_Convert.IsEnabled = true;
                        });

                        this.Dispatcher.Invoke(() =>
                        {
                            FlashWindow();
                        });
                    });
                }
                else
                {
                    ConvertImage(TextBox_Input.Text, (bool)CheckBox_SaveRawDDS.IsChecked!, (bool)CheckBox_SeparateGlossMap.IsChecked!, TextBox_Output.Text, (bool)CheckBox_DeleteSourceFiles.IsChecked!, IsOutputFolder);
                }

                GC.Collect();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error");
            }
        }

        public List<Task> BatchProcessFiles(string inputFolder, string outputFolder, bool saveRawDDS, bool separateGlossMap, bool deleteSourceFiles, bool recursive)
        {
            List<Task> tasks = new();

            foreach (var file in Directory.EnumerateFiles(inputFolder, "*.dds"))
            {
                try
                {
                    tasks.Add(Task.Run(() =>
                    {
                        ConvertImage(file, saveRawDDS, separateGlossMap, outputFolder, deleteSourceFiles, true);
                        GC.Collect();
                    }));
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to convert {Path.GetFileName(file)}, it will be skipped.\nError: {ex.Message}", "Error");
                    continue;
                }
            }

            if (recursive)
            {
                foreach (var dir in Directory.EnumerateDirectories(inputFolder))
                {
                    string newOutputPath = "";

                    if (!string.IsNullOrEmpty(outputFolder))
                    {
                        newOutputPath = Path.Combine(outputFolder, Path.GetFileNameWithoutExtension(dir)!);

                        if (!Directory.Exists(newOutputPath))
                        {
                            Directory.CreateDirectory(newOutputPath);
                        }
                    }

                    tasks.AddRange(BatchProcessFiles(dir, newOutputPath, saveRawDDS, separateGlossMap, deleteSourceFiles, recursive));
                }
            }

            return tasks;
        }

        public void ConvertImage(string filePath, bool saveRawDDS, bool separateGlossMap, string outputPath = "", bool deleteSourceFiles = false, bool isOutputFolder = false)
        {
            bool isNormalMap = false;
            bool isSRGB = false;
            bool isIDMap = Path.GetFileNameWithoutExtension(filePath).EndsWith("_id");

            (ScratchImage? image, ScratchImage? alpha, List<string> mipFiles, List<string> alphaMipFiles) dds = LoadGameDDS(filePath, saveRawDDS, deleteSourceFiles, outputPath, isOutputFolder);

            if (dds.image == null)
            {
                throw new Exception("Failed to load DDS image.");
            }

            var format = dds.image.GetImage(0).Format;

            isNormalMap = format == DXGI_FORMAT.BC5_SNORM || format == DXGI_FORMAT.BC5_UNORM;

            isSRGB = TexHelper.Instance.IsSRGB(format);

            ScratchImage decompressedImage = dds.image;

            if (TexHelper.Instance.IsCompressed(format))
            {
                decompressedImage = dds.image.Decompress(0, DXGI_FORMAT.R32G32B32A32_FLOAT);
            }
            else if (format != DXGI_FORMAT.R32G32B32A32_FLOAT)
            {
                decompressedImage = decompressedImage.Convert(DXGI_FORMAT.R32G32B32A32_FLOAT, TEX_FILTER_FLAGS.DEFAULT, 0.5f);
            }

            if (isNormalMap)
            {
                byte[] reconstructed = ReconstructZ(GetPixelData(decompressedImage), true);

                Marshal.Copy(reconstructed, 0, decompressedImage.GetImage(0).Pixels, reconstructed.Length);
            }

            if (dds.alpha != null)
            {
                if (separateGlossMap)
                {
                    ScratchImage decompressedAlpha = dds.alpha!;

                    if (TexHelper.Instance.IsCompressed(dds.alpha!.GetImage(0, 0, 0).Format))
                    {
                        decompressedAlpha = dds.alpha.Decompress(0, DXGI_FORMAT.R8_UNORM);
                    }
                    else if (dds.alpha!.GetImage(0, 0, 0).Format != DXGI_FORMAT.R8_UNORM)
                    {
                        decompressedAlpha = dds.alpha.Convert(0, DXGI_FORMAT.R8_UNORM, TEX_FILTER_FLAGS.DEFAULT, 0.5f);
                    }

                    if (isOutputFolder)
                    {
                        string dir = string.IsNullOrEmpty(outputPath) ? Path.GetDirectoryName(filePath)! : outputPath;

                        decompressedAlpha.SaveToWICFile(0, WIC_FLAGS.NONE, TexHelper.Instance.GetWICCodec(WICCodecs.TIFF), Path.Combine(dir, Path.GetFileNameWithoutExtension(filePath) + "_alpha.tif"));
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(outputPath))
                        {
                            throw new Exception("Incorrect output path.");
                        }

                        decompressedAlpha.SaveToWICFile(0, WIC_FLAGS.NONE, TexHelper.Instance.GetWICCodec(WICCodecs.TIFF), Path.Combine(Path.GetDirectoryName(outputPath)!, Path.GetFileNameWithoutExtension(outputPath) + "_alpha.tif"));
                    }

                    decompressedAlpha.Dispose();
                }
                else
                {
                    ScratchImage decompressedAlpha = dds.alpha!;

                    if (TexHelper.Instance.IsCompressed(dds.alpha!.GetImage(0, 0, 0).Format))
                    {
                        decompressedAlpha = decompressedAlpha.Decompress(0, DXGI_FORMAT.R32_FLOAT);
                    }
                    else if (dds.alpha!.GetImage(0, 0, 0).Format != DXGI_FORMAT.R32_FLOAT)
                    {
                        decompressedAlpha = decompressedAlpha.Convert(0, DXGI_FORMAT.R32_FLOAT, TEX_FILTER_FLAGS.DEFAULT, 0.5f);
                    }

                    byte[] merged = MergeAlpha(GetPixelData(decompressedImage), GetPixelData(decompressedAlpha));

                    Marshal.Copy(merged, 0, decompressedImage.GetImage(0).Pixels, merged.Length);

                    decompressedAlpha.Dispose();
                }

                dds.alpha.Dispose();
            }

            if (!isNormalMap) // Extending color space tends to cause issues, so we clamp it to 8 bits
            {
                if (isIDMap)
                {
                    byte[] quantizedData = QuantizeIDPixels(GetPixelData(decompressedImage), isSRGB);

                    decompressedImage = decompressedImage.Convert(DXGI_FORMAT.R8G8B8A8_UNORM, TEX_FILTER_FLAGS.DEFAULT, 0.0f);

                    Marshal.Copy(quantizedData, 0, decompressedImage.GetImage(0).Pixels, quantizedData.Length);
                }
                else
                {
                    decompressedImage = decompressedImage.Convert(isSRGB ? DXGI_FORMAT.R8G8B8A8_UNORM_SRGB : DXGI_FORMAT.R8G8B8A8_UNORM, TEX_FILTER_FLAGS.DEFAULT, 0.5f);
                }
            }

            if (isOutputFolder)
            {
                string dir = string.IsNullOrEmpty(outputPath) ? Path.GetDirectoryName(filePath)! : outputPath;

                decompressedImage.SaveToWICFile(0, WIC_FLAGS.NONE, TexHelper.Instance.GetWICCodec(WICCodecs.TIFF), Path.Combine(dir, Path.GetFileNameWithoutExtension(filePath) + ".tif"));
            }
            else
            {
                if (string.IsNullOrEmpty(outputPath))
                {
                    throw new Exception("Incorrect output path.");
                }

                decompressedImage.SaveToWICFile(0, WIC_FLAGS.NONE, TexHelper.Instance.GetWICCodec(WICCodecs.TIFF), outputPath);
            }

            dds.image.Dispose();
            decompressedImage.Dispose();

            if (deleteSourceFiles)
            {
                foreach (var file in dds.mipFiles)
                {
                    if (File.Exists(file))
                    {
                        File.Delete(file);
                    }
                }

                foreach (var file in dds.alphaMipFiles)
                {
                    if (File.Exists(file))
                    {
                        File.Delete(file);
                    }
                }

                if (!(saveRawDDS && isOutputFolder && string.IsNullOrEmpty(outputPath)))
                {
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                    }

                    if (File.Exists(filePath + ".a"))
                    {
                        File.Delete(filePath + ".a");
                    }
                }
            }
        }

        public byte[] QuantizeIDPixels(byte[] decompressedPixels, bool isSRGB)
        {
            using (MemoryStream ms = new(decompressedPixels))
            {
                using (BinaryReader br = new(ms))
                {
                    using (MemoryStream ws = new())
                    {
                        using (BinaryWriter bw = new(ws))
                        {
                            while (br.BaseStream.Position != br.BaseStream.Length)
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

                                    bw.Write((byte)MathF.Ceiling(r * 255.0f));
                                    bw.Write((byte)MathF.Ceiling(g * 255.0f));
                                    bw.Write((byte)MathF.Ceiling(b * 255.0f));
                                    bw.Write((byte)MathF.Floor(a * 255.0f));
                                }
                                else
                                {
                                    bw.Write((byte)MathF.Floor(r * 255.0f)); // ID map complains when we use rounding instead of flooring
                                    bw.Write((byte)MathF.Floor(g * 255.0f));
                                    bw.Write((byte)MathF.Floor(b * 255.0f));
                                    bw.Write((byte)MathF.Floor(a * 255.0f));
                                }
                                
                            }
                        }

                        return ws.ToArray();
                    }
                }
            }
        }

        public (ScratchImage?, ScratchImage?, List<string>, List<string>) LoadGameDDS(string ddsFilePath, bool saveRawDDS = false, bool deleteSourceFiles = false, string outputPath = "", bool isOutputFolder = false)
        {
            ScratchImage? image = null;
            ScratchImage? alpha = null;
            List<string> mipFiles = new();
            List<string> alphaMipFiles = new();

            List<byte[]> mips = new();
            List<byte[]> alphaMips = new();

            for (int i = 1; i < 64; i++)
            {
                string path = ddsFilePath + "." + i;

                if (!File.Exists(path))
                {
                    break;
                }

                mips.Insert(0, File.ReadAllBytes(path));
                mipFiles.Add(path);
            }

            for (int i = 1; i < 64; i++)
            {
                string path = ddsFilePath + "." + i + "a";

                if (!File.Exists(path))
                {
                    break;
                }

                alphaMips.Insert(0, File.ReadAllBytes(path));
                alphaMipFiles.Add(path);
            }

            DDSFile ddsFile = new(ddsFilePath, false);
            DDSFile? alphaDDSFile = null;

            if (File.Exists(ddsFilePath + ".a"))
            {
                alphaDDSFile = new(ddsFilePath + ".a", true);
            }

            using (MemoryStream ms = new())
            {
                using (BinaryWriter bw = new(ms))
                {
                    foreach (var data in mips)
                    {
                        bw.Write(data);
                    }

                    bw.Write(ddsFile.Data!);
                }

                ddsFile.Data = ms.ToArray();

                if (ddsFile.Data.Length < ComputePixelDataSize(ddsFile.Header.GetPixelFormat(), ddsFile.Header.Width, ddsFile.Header.Height, ddsFile.Header.MipMapCount))
                {
                    throw new Exception("Failed to load all necessary MIPs.");
                }

                if (saveRawDDS)
                {
                    if (isOutputFolder)
                    {
                        string dir = string.IsNullOrEmpty(outputPath) ? Path.GetDirectoryName(ddsFilePath)! : outputPath;
                        string path = Path.Combine(dir, Path.GetFileNameWithoutExtension(ddsFilePath) + ".dds");

                        if (!File.Exists(path) || deleteSourceFiles)
                        {
                            ddsFile.Write(path);
                        }
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(outputPath))
                        {
                            throw new Exception("Incorrect output path.");
                        }

                        ddsFile.Write(outputPath);
                    }
                }
            }

            if (alphaDDSFile != null)
            {
                using (MemoryStream ms = new())
                {
                    using (BinaryWriter bw = new(ms))
                    {
                        foreach (var data in alphaMips)
                        {
                            bw.Write(data);
                        }

                        bw.Write(alphaDDSFile.Data!);
                    }

                    alphaDDSFile.Data = ms.ToArray();

                    if (alphaDDSFile.Data.Length != ComputePixelDataSize(alphaDDSFile.Header.GetPixelFormat(), alphaDDSFile.Header.Width, alphaDDSFile.Header.Height, alphaDDSFile.Header.MipMapCount))
                    {
                        throw new Exception("Failed to load all necessary alpha channel MIPs.");
                    }

                    if (saveRawDDS)
                    {
                        if (isOutputFolder)
                        {
                            string dir = string.IsNullOrEmpty(outputPath) ? Path.GetDirectoryName(ddsFilePath)! : outputPath;
                            string path = Path.Combine(dir, Path.GetFileNameWithoutExtension(ddsFilePath) + "_alpha.dds");

                            if (!File.Exists(path) || deleteSourceFiles)
                            {
                                alphaDDSFile.Write(path);
                            }
                        }
                        else
                        {
                            if (string.IsNullOrEmpty(outputPath))
                            {
                                throw new Exception("Incorrect output path.");
                            }

                            string path = Path.Combine(Path.GetDirectoryName(outputPath)!, Path.GetFileNameWithoutExtension(outputPath) + "_alpha.dds");

                            alphaDDSFile.Write(path);
                        }
                    }
                }

                byte[] alphaData = alphaDDSFile.Write();

                GCHandle alphaHandle = GCHandle.Alloc(alphaData, GCHandleType.Pinned);

                try
                {
                    IntPtr alphaPtr = alphaHandle.AddrOfPinnedObject();

                    alpha = TexHelper.Instance.LoadFromDDSMemory(alphaPtr, alphaData.Length, DDS_FLAGS.ALLOW_LARGE_FILES);
                }
                finally
                {
                    alphaHandle.Free();
                }
            }

            byte[] imageData = ddsFile.Write();

            GCHandle imageHandle = GCHandle.Alloc(imageData, GCHandleType.Pinned);

            try
            {
                IntPtr imagePtr = imageHandle.AddrOfPinnedObject();

                image = TexHelper.Instance.LoadFromDDSMemory(imagePtr, imageData.Length, DDS_FLAGS.ALLOW_LARGE_FILES);
            }
            finally
            {
                imageHandle.Free();
            }

            return (image, alpha, mipFiles, alphaMipFiles);
        }

        public byte[] GetPixelData(ScratchImage image)
        {
            int size = (int)image.GetPixelsSize();
            byte[] data = new byte[size];
            Marshal.Copy(image.GetPixels(), data, 0, size);

            return data;
        }

        public MemoryStream GetPixelsStream(ScratchImage image)
        {
            return new MemoryStream(GetPixelData(image));
        }

        private int ComputePixelDataSize(DXGI_FORMAT format, int width, int height, int mipMapCount)
        {
            int bits = TexHelper.Instance.BitsPerPixel(format);

            int baseSize = (width * height * bits);
            int totalSize = baseSize;

            for (int i = 1; i < mipMapCount; i++)
            {
                baseSize /= 4;
                totalSize += baseSize;
            }

            return totalSize /= 8;
        }

        public byte[] ReconstructZ(byte[] pixelData, bool pack)
        {
            var vectors = new List<Vector2>();
            var alphas = new List<float>();

            using (MemoryStream ms = new(pixelData))
            {
                using (BinaryReader br = new(ms))
                {
                    while (br.BaseStream.Position != br.BaseStream.Length)
                    {
                        vectors.Add(new(br.ReadSingle(), br.ReadSingle()));
                        br.BaseStream.Position += 4;
                        alphas.Add(br.ReadSingle());
                    }
                }
            }

            using (MemoryStream ms = new(pixelData))
            {
                using (BinaryWriter bw = new(ms))
                {
                    for (int i = 0; i < vectors.Count; i++)
                    {
                        Vector2 vector = vectors[i];

                        float z = MathF.Sqrt(1.0f - Vector2.Dot(vector, vector));

                        bw.Write(pack ? MathF.Pow((vector.Y + 1.0f) / 2.0f, 2.2f) : vector.Y);
                        bw.Write(pack ? MathF.Pow((vector.X + 1.0f) / 2.0f, 2.2f) : vector.X);
                        bw.Write(pack ? MathF.Pow((z + 1.0f) / 2.0f, 2.2f) : z);
                        bw.Write(1.0f);
                    }
                }

                return ms.ToArray();
            }
        }

        public byte[] MergeAlpha(byte[] pixelData, byte[] alphaPixelData)
        {
            var vectors = new List<Vector4>();
            var alphas = new List<float>();

            using (MemoryStream ms = new(pixelData))
            {
                using (BinaryReader br = new(ms))
                {
                    while (br.BaseStream.Position != br.BaseStream.Length)
                    {
                        vectors.Add(new(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle()));
                    }
                }
            }

            using (MemoryStream ms = new(alphaPixelData))
            {
                using (BinaryReader br = new(ms))
                {
                    while (br.BaseStream.Position != br.BaseStream.Length)
                    {
                        alphas.Add(br.ReadSingle());
                    }
                }
            }

            using (MemoryStream ms = new(pixelData))
            {
                using (BinaryWriter bw = new(ms))
                {
                    for (int i = 0; i < vectors.Count; i++)
                    {
                        bw.Write(vectors[i].X);
                        bw.Write(vectors[i].Y);
                        bw.Write(vectors[i].Z);
                        bw.Write(alphas[i]);
                    }
                }

                return ms.ToArray();
            }
        }

        private void Button_InputPicker_Click(object sender, RoutedEventArgs e)
        {
            OpenFolderDialog dialog = new OpenFolderDialog();
            dialog.Multiselect = false;
            dialog.Title = "Input folder";

            if ((bool)dialog.ShowDialog()!)
            {
                TextBox_Input.Text = dialog.FolderName;
            }
        }

        private void Button_OutputPicker_Click(object sender, RoutedEventArgs e)
        {
            OpenFolderDialog dialog = new OpenFolderDialog();
            dialog.Multiselect = false;
            dialog.Title = "Output folder";

            if ((bool)dialog.ShowDialog()!)
            {
                TextBox_Output.Text = dialog.FolderName;
            }
        }

        private void TextBox_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);

                if (files.Length > 0)
                {
                    TextBox? textBox = sender as TextBox;

                    textBox!.Text = files[0];
                }
            }
        }

        private void TextBox_PreviewDragOver(object sender, DragEventArgs e) => e.Handled = true;

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e) => WriteSettingsFile();

        private void FlashWindow()
        {
            WindowInteropHelper wih = new(this);

            FLASHWINFO fwi = new FLASHWINFO
            {
                hwnd = wih.Handle,
                dwFlags = FLASHW_ALL | FLASHW_TIMERNOFG,
                uCount = uint.MaxValue,
                dwTimeout = 0
            };

            fwi.cbSize = Convert.ToUInt32(Marshal.SizeOf(fwi));
            FlashWindowEx(ref fwi);
        }

        //Disgustang xml reader/writer

        public void ReadSettingsFile()
        {
            if (!File.Exists("Settings.xml"))
            {
                WriteSettingsFile();

                return;
            }

            XmlReaderSettings settings = new();

            try
            {
                using (XmlReader reader = XmlReader.Create("Settings.xml", settings))
                {

                    while (reader.Read())
                    {
                        switch (reader.NodeType)
                        {
                            case XmlNodeType.Element:
                                switch (reader.Name.ToLower())
                                {
                                    case "settings":
                                        while (reader.Read())
                                        {
                                            if (reader.NodeType == XmlNodeType.Element)
                                            {
                                                switch (reader.Name.ToLower())
                                                {
                                                    case "value":
                                                        ReadPropertyFromXml(reader);
                                                        break;
                                                }
                                            }
                                            else if (reader.NodeType == XmlNodeType.EndElement)
                                            {
                                                if (reader.Name.ToLower() == "settings")
                                                {
                                                    break;
                                                }
                                            }
                                        }

                                        break;
                                }
                                break;
                        }
                    }
                }

                if (!((bool)CheckBox_RememberPaths.IsChecked!))
                {
                    TextBox_Input.Text = "";
                    TextBox_Output.Text = "";
                }
            }
            catch (Exception ex)
            {
               MessageBox.Show(ex.Message, "Error reading settings file!");

                if (File.Exists("Settings.xml"))
                {
                    File.Delete("Settings.xml");
                }

                WriteSettingsFile();

                return;
            }
        }

        public void WriteSettingsFile()
        {
            XElement MainElement = new("Settings", new XAttribute("Date", DateTime.Now.ToString("G")));

            XElement Element = new("Value", new XAttribute("Name", "SeparateGlossMap"), new XAttribute("Type", typeof(bool).Name));
            Element.Value = ((bool)CheckBox_SeparateGlossMap.IsChecked!).ToString();

            MainElement.Add(Element);

            Element = new("Value", new XAttribute("Name", "SaveRawDDS"), new XAttribute("Type", typeof(bool).Name));
            Element.Value = ((bool)CheckBox_SaveRawDDS.IsChecked!).ToString();

            MainElement.Add(Element);

            Element = new("Value", new XAttribute("Name", "RememberPaths"), new XAttribute("Type", typeof(bool).Name));
            Element.Value = ((bool)CheckBox_RememberPaths.IsChecked!).ToString();

            MainElement.Add(Element);

            Element = new("Value", new XAttribute("Name", "DeleteSourceFiles"), new XAttribute("Type", typeof(bool).Name));
            Element.Value = ((bool)CheckBox_DeleteSourceFiles.IsChecked!).ToString();

            MainElement.Add(Element);

            Element = new("Value", new XAttribute("Name", "Recursive"), new XAttribute("Type", typeof(bool).Name));
            Element.Value = ((bool)CheckBox_Recursive.IsChecked!).ToString();

            MainElement.Add(Element);

            Element = new("Value", new XAttribute("Name", "InputPath"), new XAttribute("Type", typeof(string).Name));
            Element.Value = TextBox_Input.Text;

            MainElement.Add(Element);

            Element = new("Value", new XAttribute("Name", "OutputPath"), new XAttribute("Type", typeof(string).Name));
            Element.Value = TextBox_Output.Text;

            MainElement.Add(Element);

            XmlWriterSettings settings = new();
            settings.Indent = true;
            settings.IndentChars = "    ";
            settings.Encoding = Encoding.Unicode;

            using (XmlWriter writer = XmlWriter.Create("Settings.xml", settings))
            {
                MainElement.Save(writer);
            }
        }

        private void ReadPropertyFromXml(XmlReader reader)
        {
            string name = reader.GetAttribute("Name")!;
            string type = reader.GetAttribute("Type")!;
            reader.Read();

            switch (type.ToLower())
            {
                case "string":
                    switch (name)
                    {
                        case "InputPath":
                            TextBox_Input.Text = reader.Value;
                            break;

                        case "OutputPath":
                            TextBox_Output.Text = reader.Value;
                            break;
                    }
                    break;

                case "boolean":
                    switch (name)
                    {
                        case "SeparateGlossMap":
                            CheckBox_SeparateGlossMap.IsChecked = XmlConvert.ToBoolean(reader.Value.ToLower());
                            break;

                        case "SaveRawDDS":
                            CheckBox_SaveRawDDS.IsChecked = XmlConvert.ToBoolean(reader.Value.ToLower());
                            break;

                        case "RememberPaths":
                            CheckBox_RememberPaths.IsChecked = XmlConvert.ToBoolean(reader.Value.ToLower());
                            break;

                        case "DeleteSourceFiles":
                            CheckBox_DeleteSourceFiles.IsChecked = XmlConvert.ToBoolean(reader.Value.ToLower());
                            break;

                        case "Recursive":
                            CheckBox_Recursive.IsChecked = XmlConvert.ToBoolean(reader.Value.ToLower());
                            break;
                    }
                    break;
            }
        }
    }
}