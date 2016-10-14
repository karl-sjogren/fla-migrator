using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Ionic.Zip;
using Ionic.Zlib;

namespace FlaMigrator {
    class Program {
        const string WorkingDirectory = @"C:\Users\kasjo.OLS\Desktop\Github\fla-migrator-temp\";
        const string OutputDirectory = @"C:\Users\kasjo.OLS\Desktop\Github\fla-migrator-result\";
        const string DataDirectory = @"C:\Users\kasjo.OLS\Desktop\Github\fla-migrator-data\";
        
        static void Main(string[] args) {
            var allFiles = Directory.GetFiles(DataDirectory, "*.fla", SearchOption.AllDirectories);

            //allFiles = allFiles.Take(15).ToArray();
            Parallel.ForEach(allFiles, DoWork);
        
            Debugger.Break();
        }

        private static void DoWork(string filename) {
            string targetPath;
            if(!UnzipFile(filename, out targetPath))
                return;

            var allFiles = Directory.GetFiles(targetPath, "*.xml", SearchOption.AllDirectories);

            try {
                File.Copy("backToMenu_overlay.xml", Path.Combine(targetPath, "LIBRARY", "backToMenu_overlay.xml"));
                File.Copy("replay_overlay.xml", Path.Combine(targetPath, "LIBRARY", "replay_overlay.xml"));
                File.Copy("toMenu_overlay.xml", Path.Combine(targetPath, "LIBRARY", "toMenu_overlay.xml"));
            } catch(Exception) {
            }

            foreach(var file in allFiles) {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(new FileInfo(file).Name + "                                       ");

                XDocument doc;
                try {
                    doc = XDocument.Load(file);
                } catch(Exception) {
                    continue;
                }

                XNamespace ns = "http://ns.adobe.com/xfl/2008/";

                var files = new[] { "direkt till menyn", "till huvudmenyn", "spela igen" };

                foreach(var fileToGetOverlay in files) {
                    if(new FileInfo(file).Name.Contains(fileToGetOverlay)) {
                        var clickLayerXml = File.ReadAllText(fileToGetOverlay + ".xml");
                        var clickLayer = XDocument.Parse(clickLayerXml);
                        var layers = doc.Descendants(ns + "layers").First();
                        layers.AddFirst(clickLayer.Root);
                        var layer = layers.Descendants("DOMLayer").First();
                        layer.Attributes("xmlns").Remove();
                    }
                }

                if(new FileInfo(file).Name == "DOMDocument.xml") {
                    /*
                     
                              <Include href="backToMenu_overlay.xml" itemIcon="1" loadImmediate="false" itemID="57f7a979-00000290" lastModified="1475848569"/>
                              <Include href="replay_overlay.xml" itemIcon="1" loadImmediate="false" itemID="57f7a9c5-000002a0" lastModified="1475848645"/>
                              <Include href="toMenu_overlay.xml" itemIcon="1" loadImmediate="false" itemID="57f7a9a1-0000029a" lastModified="1475848609"/>
                     */

                    var symbols = doc.Descendants(ns + "symbols").First();
                    symbols.Add(new XElement(ns + "Include",
                        new XAttribute("href", "backToMenu_overlay.xml"),
                        new XAttribute("itemIcon", "1"),
                        new XAttribute("loadImmediate", "false"),
                        new XAttribute("itemID", "57f7a979-00000290"),
                        new XAttribute("lastModified", "1475848569")
                    ));
                    symbols.Add(new XElement(ns + "Include",
                        new XAttribute("href", "replay_overlay.xml"),
                        new XAttribute("itemIcon", "1"),
                        new XAttribute("loadImmediate", "false"),
                        new XAttribute("itemID", "57f7a979-00000290"),
                        new XAttribute("lastModified", "1475848569")
                    ));
                    symbols.Add(new XElement(ns + "Include",
                        new XAttribute("href", "toMenu_overlay.xml"),
                        new XAttribute("itemIcon", "1"),
                        new XAttribute("loadImmediate", "false"),
                        new XAttribute("itemID", "57f7a979-00000290"),
                        new XAttribute("lastModified", "1475848569")
                    ));
                }

                var settings = new XmlWriterSettings {
                    Encoding = Encoding.UTF8,
                    Indent = true,
                    IndentChars = "  ",
                    NewLineHandling = NewLineHandling.Entitize
                };

                using(var writer = XmlWriter.Create(file, settings)) {
                    doc.Save(writer);
                }

                var badXml = File.ReadAllText(file);
                badXml = badXml.Replace("xmlns=\"\"", string.Empty);
                File.WriteAllText(file, badXml);
            }

            Repack(filename);
        }
        
        private static bool UnzipFile(string filename, out string targetPath) {
            var fi = new FileInfo(filename);
            if(fi.Attributes.HasFlag(FileAttributes.Hidden)) {
                targetPath = null;
                return false;
            }

            targetPath = fi.FullName.Substring(DataDirectory.Length);

            targetPath = Path.Combine(WorkingDirectory, targetPath);

            if(!Directory.Exists(targetPath))
                Directory.CreateDirectory(targetPath);

            try {
                var zip = new ZipFile(filename);

                zip.ExtractAll(targetPath, ExtractExistingFileAction.OverwriteSilently);
            } catch(Exception) {
                return false;
            }
            return true;
        }

        private static void Repack(string filename) {
            var fi = new FileInfo(filename);

            var targetPath = fi.Directory.FullName.Substring(DataDirectory.Length);
            targetPath = Path.Combine(OutputDirectory, targetPath);


            var sourcePath = fi.FullName.Substring(DataDirectory.Length);

            sourcePath = Path.Combine(WorkingDirectory, sourcePath);

            if(!Directory.Exists(targetPath))
                Directory.CreateDirectory(targetPath);

            var targetFile = Path.Combine(targetPath, fi.Name);

            if(File.Exists(targetFile))
                File.Delete(targetFile);

            var newZip = new ZipFile(targetFile, Encoding.UTF8);
            newZip.CompressionLevel = CompressionLevel.None;
            newZip.AddDirectory(sourcePath, string.Empty);
            newZip.Save();
        }
    }
}
