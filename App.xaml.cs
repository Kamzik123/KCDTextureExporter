using System.Configuration;
using System.Data;
using System.IO;
using System.Windows;
using DirectXTexNet;

namespace KCDTextureExporter
{
    public partial class App : Application
    {
        private void OnStartup(object sender, StartupEventArgs e)
        {
            string[] args = e.Args;
            //Check for args being passed first.
            if (args.Contains("--input") && args.Contains("--output"))
            {
                string inputPath = GetArgValue(args, "--input");
                string outputPath = GetArgValue(args, "--output");

                bool saveRaw = args.Contains("--saveRaw");
                bool separateGloss = args.Contains("--separateGloss");
                bool deleteSrc = args.Contains("--deleteSource");
                bool recursive = args.Contains("--recursive");

                bool isOutputFolder = !outputPath.EndsWith(".tif", StringComparison.InvariantCultureIgnoreCase);

                TexHelper.LoadInstance();

                if (Directory.Exists(inputPath))
                {
                    var ddsFiles = Directory.EnumerateFiles(inputPath, "*.dds", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

                    foreach (var file in ddsFiles)
                    {
                        KCDTextureExporter.MainWindow.ConvertImageStatic(file, saveRaw, separateGloss, outputPath, deleteSrc, true);
                    }
                }
                else
                {
                    KCDTextureExporter.MainWindow.ConvertImageStatic(inputPath, saveRaw, separateGloss, outputPath, deleteSrc, isOutputFolder);
                }

                Shutdown();
                return;
            }

            // Launch UI if no CLI args
            new MainWindow().Show();
        }

        private static string GetArgValue(string[] args, string key)
        {
            int index = Array.IndexOf(args, key);
            if (index >= 0 && index < args.Length - 1)
                return args[index + 1];

            return "";
        }
    }
}
