using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Ionic.Zip;
using Ionic.Zlib;

namespace FlaMigrator {
    class Program {
        const string WorkingDirectory = @"C:\Users\kasjo.OLS\Desktop\Github\fla-migrator-temp\";
        const string OutputDirectory = @"C:\Users\kasjo.OLS\Desktop\Github\fla-migrator-result\";
        const string DataDirectory = @"C:\Users\kasjo.OLS\Desktop\Github\fla-migrator-data\";

        private static ConcurrentBag<Script> _codeSnippets = new ConcurrentBag<Script>();

        static void Main(string[] args) {

            var counter = 1;
            var allFiles = Directory.GetFiles(DataDirectory, "*.fla", SearchOption.AllDirectories);
            /*foreach(var file in allFiles) {
                Console.SetCursorPosition(0, 0);
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(new FileInfo(file).Name + "                                       ");

                Console.SetCursorPosition(0, 1);
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"File {counter++}/{allFiles.Length}                        ");

                DoWork(file);
            }*/

            Parallel.ForEach(allFiles, (file) => DoWork(file));
            

            Debugger.Break();


            foreach(var snippet in _codeSnippets) {
                File.AppendAllText(Path.Combine(WorkingDirectory, "scripts.txt"), "Instances: " + snippet.Locations.Count + Environment.NewLine + snippet.Code + Environment.NewLine + Environment.NewLine);
            }

            var files = _codeSnippets.SelectMany(s => s.Locations).GroupBy(s => {
                return s.File.Split('\\').FirstOrDefault(s2 => s2.EndsWith(".fla"));
            }, location => location.File);

            foreach(var file in files) {
                File.AppendAllText(Path.Combine(WorkingDirectory, "count.txt"), file.Key + " " + file.Count() + Environment.NewLine);
            }

            Debugger.Break();
        }

        private static void DoWork(string filename) {
            string targetPath;
            if(!UnzipFile(filename, out targetPath))
                return;

            var allFiles = Directory.GetFiles(targetPath, "*.xml", SearchOption.AllDirectories);
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

                var scripts = doc.Descendants(ns + "script").Select(s => new {
                    layer = s.Parent?.Parent?.Parent?.Parent?.Attribute("name")?.Value,
                    frame = s.Parent?.Parent?.Attribute("index")?.Value,
                    script = s.Value,
                    node = s
                });

                foreach(var script in scripts) {
                    var cleanedScript = CleanScript(script.script);

                    if(cleanedScript.StartsWith("on(") || cleanedScript.StartsWith("on ("))
                        continue;

                    script.node.Value = cleanedScript;

                    var snippet = _codeSnippets.FirstOrDefault(s => s.Code == cleanedScript);
                    
                    if(snippet == null) {
                        snippet = new Script { Code = cleanedScript };
                        _codeSnippets.Add(snippet);
                    }

                    snippet.Locations.Add(new Script.Location { Frame = script.frame, Layer = script.layer, File = file });
                }

                doc.Save(file);
            }

            Repack(filename);
        }

        private static Regex RemoveTrace = new Regex("trace\\(\"(?:.*?)\"\\)", RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static Regex FixStop = new Regex("(?<!this\\.)stop\\(\\);", RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static Regex FixPlay = new Regex("(?<!this\\.)play\\(\\);", RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static Regex FixCurrentFrame = new Regex("_currentframe", RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static Regex FixGotoAndPlay = new Regex("(?<!this\\.)(?<Prefix>mattan\\.|disp\\.)?gotoAndPlay\\((?<Frame>.*?)\\)(?=;)?", RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static Regex FixOnAction = new Regex("(?<Clip>[a-zA-Z0-9_]*?)\\.on(?<Action>.*?) = function\\s?\\(\\){(?<Body>.*?)}", RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static string CleanScript(string script) {

            script = script.Trim();
            script = script.Trim('/', '*');
            script = script.Trim();

            script = RemoveTrace.Replace(script, string.Empty);
            script = FixStop.Replace(script, "this.stop();");
            script = FixPlay.Replace(script, "this.play();");
            script = FixCurrentFrame.Replace(script, "this.currentframe");
            script = FixGotoAndPlay.Replace(script, match => "this." + match.Groups["Prefix"].Value + "gotoAndPlay(" + match.Groups["Frame"].Value +");");

            script = FixOnAction.Replace(script, match => @"this." + match.Groups["Clip"].Value + ".addEventListener('click', function() { " + match.Groups["Body"] + " }.bind(this));");

            script = script.Trim();
            return script;
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

    public class Script {
        public string Code { get; set; }
        public List<Location> Locations { get; set; } = new List<Location>();

        public class Location {
            public string File { get; set; }
            public string Frame { get; set; }
            public string Layer { get; set; }
        }
    }
}
