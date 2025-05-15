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
                    ConvertImageStatic(TextBox_Input.Text, (bool)CheckBox_SaveRawDDS.IsChecked!, (bool)CheckBox_SeparateGlossMap.IsChecked!, TextBox_Output.Text, (bool)CheckBox_DeleteSourceFiles.IsChecked!, IsOutputFolder);
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
                        ConvertImageStatic(file, saveRawDDS, separateGlossMap, outputFolder, deleteSourceFiles, true);
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