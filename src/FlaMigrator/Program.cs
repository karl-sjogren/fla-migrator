using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using Ionic.Zip;
using Ionic.Zlib;
using Newtonsoft.Json;

namespace FlaMigrator {
    class Program {
        const string WorkingDirectory = @"C:\Users\Karl-Johan\Desktop\fla-migrator\fla-migrator-temp\";
        const string OutputDirectory = @"C:\Users\Karl-Johan\Desktop\fla-migrator\fla-migrator-result\";
        const string DataDirectory = @"C:\Users\Karl-Johan\Desktop\fla-migrator\fla-migrator-data\";

        private static ConcurrentBag<Script> _codeSnippets = new ConcurrentBag<Script>();
        private static ConcurrentBag<TimelineLocation> _timelineLocations = new ConcurrentBag<TimelineLocation>();

        static void Main(string[] args) {
            var allFiles = Directory.GetFiles(DataDirectory, "*.fla", SearchOption.AllDirectories);
            
            //allFiles = allFiles.Take(15).ToArray();
            Parallel.ForEach(allFiles, DoWork);
            
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

            var timelineResult = _timelineLocations.Select(x => x).ToDictionary(x => x.File, x => x.Frames);

            var json = JsonConvert.SerializeObject(timelineResult);
            File.WriteAllText(Path.Combine(WorkingDirectory, "timeline.json"), json, Encoding.UTF8);

            Debugger.Break();
        }

        private static void DoWork(string filename) {
            string targetPath;
            if(!UnzipFile(filename, out targetPath))
                return;

            var allFiles = Directory.GetFiles(targetPath, "*.xml", SearchOption.AllDirectories);
            var menuFiles = Enumerable.Range(10, 10).Select(i => $"Symbol {i}.xml").ToArray();

            var hasMenuFiles = false;
            foreach(var file in allFiles) {
                if(new FileInfo(filename).Name.Contains("meny") && menuFiles.Contains(new FileInfo(file).Name)) {
                    hasMenuFiles = true;
                }
            }

            if(hasMenuFiles) {
                try {
                    File.Copy("overlay.xml", Path.Combine(targetPath, "LIBRARY", "overlay.xml"));
                } catch(Exception) {

                }
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

                var scripts = doc.Descendants(ns + "script").Select(s => new {
                    layer = s.Parent?.Parent?.Parent?.Parent?.Attribute("name")?.Value,
                    frame = s.Parent?.Parent?.Attribute("index")?.Value,
                    script = s.Value,
                    node = s
                });

                foreach(var script in scripts) {
                    var cleanedScript = CleanScript(script.script, file.EndsWith("DOMDocument.xml"));

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

                if(new FileInfo(filename).Name.Contains("meny") && menuFiles.Contains(new FileInfo(file).Name)) {
                    var clickLayerXml = File.ReadAllText("MenuClickLayer.xml");

                    var match = Regex.Match(new FileInfo(file).Name, "\\d(\\d)");
                    var symbolNr = match.Groups[1].Value;
                    if(symbolNr != "0") {
                        clickLayerXml = clickLayerXml.Replace("{{episode}}", symbolNr);
                        var clickLayer = XDocument.Parse(clickLayerXml);
                        var layers = doc.Descendants(ns + "layers").First();
                        layers.AddFirst(clickLayer.Root);
                        var layer = layers.Descendants("DOMLayer").First();
                        layer.Attributes("xmlns").Remove();
                    }
                }

                if(new FileInfo(file).Name == "DOMDocument.xml" && hasMenuFiles) {
                    // <Include href="overlay.xml" itemIcon="0" loadImmediate="false" itemID="575ef3e7-00007243" lastModified="1465841355"/>

                    var symbols = doc.Descendants(ns + "symbols").First();
                    symbols.Add(new XElement(ns + "Include",
                        new XAttribute("href", "overlay.xml"),
                        new XAttribute("itemIcon", "0"),
                        new XAttribute("loadImmediate", "false"),
                        new XAttribute("itemID", "575ef3e7-00007243"),
                        new XAttribute("lastModified", "1465841355")
                    ));
                }

                if(new FileInfo(file).Name == "DOMDocument.xml") {
                    //<DOMSymbolInstance libraryItemName="till huvudmenyn" name="backBTN" symbolType="button">
                    var backButtons = doc.Descendants(ns + "DOMSymbolInstance").Where(n => n.Attribute("name")?.Value == "backBTN").ToArray();
                    foreach(var backButton in backButtons) {
                        var frame = backButton.Parent.Parent;
                        var script = XDocument.Load("BackButtonScript.xml");
                        frame.AddFirst(script.Root);
                    }

                    // <DOMFrame index="3519" duration="348" keyMode="9728" soundName="Lina 4.aiff" soundZoomLevel="65535">

                    var timelineLocations = doc.Descendants(ns + "DOMFrame").Where(n => !string.IsNullOrWhiteSpace(n.Attribute("soundName")?.Value)).Select(n=>n.Attribute("index").Value).ToList();
                    if(timelineLocations.Any()) { 
                        _timelineLocations.Add(new TimelineLocation {
                            File = new FileInfo(filename).Name,
                            Frames = timelineLocations
                        });
                    }
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

        private static Regex RemoveTrace = new Regex("trace\\(\"(?:.*?)\"\\)", RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static Regex FixStop = new Regex("(?<!this\\.)stop\\(\\);", RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static Regex FixPlay = new Regex("(?<!this\\.)play\\(\\);", RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static Regex FixCurrentFrame = new Regex("_currentframe", RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static Regex FixGotoAndPlay = new Regex("(?<!this\\.)(?<Prefix>mattan\\.|disp\\.)?gotoAndPlay\\((?<Frame>.*?)\\)(?=;)?", RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static Regex FixOnAction = new Regex("(?<Clip>[a-zA-Z0-9_]*?)\\.on(?<Action>.*?) = function\\s?\\(\\){(?<Body>.*?)}", RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static Regex CheckForCurrentFramePlusOne = new Regex("this.currentframe\\s?\\+\\s?1", RegexOptions.CultureInvariant | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static string CleanScript(string script, bool isOnTimeline) {

            script = script.Trim();
            script = script.Trim('/', '*');
            script = script.Trim();

            if(isOnTimeline) {
                script = RemoveTrace.Replace(script, string.Empty);
                script = FixStop.Replace(script, "this.stop(); window.pauseSound();");
                script = FixPlay.Replace(script, "this.play(); window.resumeSound();");
                script = FixCurrentFrame.Replace(script, "this.currentframe");
                script = FixGotoAndPlay.Replace(script, match => "window.pauseSound(); this." + match.Groups["Prefix"].Value + "gotoAndPlay(" + match.Groups["Frame"].Value + ");");

                //script = FixOnAction.Replace(script, match => @"this." + match.Groups["Clip"].Value + ".addEventListener('click', function() { " + match.Groups["Body"] + " }.bind(this));");
                script = FixOnAction.Replace(script, match => {
                    if(CheckForCurrentFramePlusOne.IsMatch(match.Groups["Body"].Value)) {
                        var body = match.Groups["Body"].Value;
                        body = body.Replace("window.pauseSound();", string.Empty);

                        return @"this." + match.Groups["Clip"].Value + ".addEventListener('click', function() { window.resumeSound(); " + body + " }.bind(this));";
                    }

                    return @"this." + match.Groups["Clip"].Value + ".addEventListener('click', function() { " + match.Groups["Body"] + " }.bind(this));";
                });
            } else {
                script = RemoveTrace.Replace(script, string.Empty);
                script = FixStop.Replace(script, "this.stop();");
                script = FixPlay.Replace(script, "this.play();");
                script = FixCurrentFrame.Replace(script, "this.currentframe");
                script = FixGotoAndPlay.Replace(script, match => "this." + match.Groups["Prefix"].Value + "gotoAndPlay(" + match.Groups["Frame"].Value + ");");

                script = FixOnAction.Replace(script, match => @"this." + match.Groups["Clip"].Value + ".addEventListener('click', function() { " + match.Groups["Body"] + " }.bind(this));");
            }

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

    public class TimelineLocation {
        public string File { get; set; }
        public List<string> Frames { get; set; }
    }
}
