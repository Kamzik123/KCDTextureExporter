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
using KCDTextureExporter;
using DirectXTexNet;

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

        public static void ConvertImageStatic(string filePath, bool saveRawDDS, bool separateGlossMap, string outputPath, bool deleteSourceFiles, bool isOutputFolder)
        {
            ImageConverter.ConvertImage(filePath, saveRawDDS, separateGlossMap, outputPath, deleteSourceFiles, isOutputFolder);
        }

        public MainWindow()
        {
            InitializeComponent();

            TexHelper.LoadInstance();

            ReadSettingsFile();
        }

        private async void Button_Convert_Click(object sender, RoutedEventArgs e)
        {
            string inputPath = TextBox_Input.Text.Trim();
            bool isRecursive = CheckBox_Recursive.IsChecked == true;
            bool isFolder = Directory.Exists(inputPath);
            bool isFileDDS = File.Exists(inputPath) && inputPath.EndsWith(".dds", StringComparison.OrdinalIgnoreCase);

            if (!isFolder && !isFileDDS)
                throw new Exception("Input path invalid or no .dds files found.");

            // Non-recursive, check DDS exists first.
            if (isFolder && !isRecursive)
            {
                if (!Directory.EnumerateFiles(inputPath, "*.dds", SearchOption.TopDirectoryOnly).Any())
                    throw new Exception("No .dds files found in the input folder.");
            }

            Button_Convert.IsEnabled = false;

            if (isFolder)
            {
                var tasks = BatchProcessFiles( inputPath, TextBox_Output.Text, CheckBox_SaveRawDDS.IsChecked == true, CheckBox_SeparateGlossMap.IsChecked == true, CheckBox_DeleteSourceFiles.IsChecked == true, isRecursive);

                if (!tasks.Any())
                    throw new Exception("No .dds files found in the input folder or its subfolders.");

                await Task.WhenAll(tasks);
            }
            else // single file
            {
                ConvertImageStatic( inputPath, CheckBox_SaveRawDDS.IsChecked == true, CheckBox_SeparateGlossMap.IsChecked == true, TextBox_Output.Text, CheckBox_DeleteSourceFiles.IsChecked == true, !TextBox_Output.Text.EndsWith(".tif", StringComparison.OrdinalIgnoreCase));
            }

            FlashWindow();
            Button_Convert.IsEnabled = true;
        }

        public List<Task> BatchProcessFiles(string inputFolder, string outputFolder, bool saveRawDDS, bool separateGlossMap, bool deleteSourceFiles, bool recursive)
        {
            var option = recursive
                ? SearchOption.AllDirectories
                : SearchOption.TopDirectoryOnly;

            // Find all .dds files including sub folders
            var ddsFiles = Directory.EnumerateFiles(inputFolder, "*.dds", option).ToList();

            if (!ddsFiles.Any())
                throw new Exception("No .dds files were found in the selected input folder (or its subfolders).");

            var tasks = new List<Task>();

            foreach (var file in ddsFiles)
            {
                if (recursive)
                {
                    // Compute the sub-path under inputFolder
                    var subDir = Path.GetRelativePath(
                        inputFolder,
                        Path.GetDirectoryName(file) ?? string.Empty);

                    // Build the matching folder under outputFolder
                    var destDir = Path.Combine(outputFolder, subDir);
                    Directory.CreateDirectory(destDir);

                    tasks.Add(Task.Run(() =>
                        ConvertImageStatic(file, saveRawDDS, separateGlossMap, destDir, deleteSourceFiles,true)
                    ));
                }
                else
                {
                    // Non-recursive
                    tasks.Add(Task.Run(() =>
                        ConvertImageStatic(file, saveRawDDS, separateGlossMap, outputFolder, deleteSourceFiles, true)
                    ));
                }
            }

            return tasks;
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