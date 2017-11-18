using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System.Xml;
using System.Threading;
using System.Configuration;

namespace WorkflowParsing
{
    public class WfParsing
    {
        private void readJson(string filePath, ref List<string> filePathArr)
        {
            StreamReader sr = null;
            try
            {
                sr = File.OpenText(filePath);
            }
            catch (Exception e)
            {
                throw (e);
            }
            string jsonStr = "";
            string line = null;
            while ((line = sr.ReadLine()) != null)
            {
                if (!Regex.IsMatch(line, @"\s*//") && !Regex.IsMatch(line, @"\s*\n"))
                {
                    jsonStr += line;
                }
            }
            sr.Close();
            try
            {
                JObject dstJson = JObject.Parse(jsonStr);
                foreach (string path in dstJson["xamlList"])
                {
                    filePathArr.Add(path);
                }
            }
            catch (Exception e)
            {
                throw (e);
            }
        }
        public void readFolder(string folderPath, ref List<string> filePathArr)
        {
            DirectoryInfo di = new DirectoryInfo(folderPath);
            FileInfo[] fiArr = di.GetFiles();
            foreach (FileInfo fi in fiArr)
            {
                filePathArr.Add(fi.FullName);
            }
        }
        public void readXamlFile(ref List<string> filePathArr, string outCsFolderPath)
        {
            foreach (string path in filePathArr)
            {
                XamlParsing xp = new XamlParsing();
                string outCodeStr = "";
                var xDoc = new XmlDocument();
                string fileFmt = path.Split('.').Last();
                if (fileFmt != "xaml") // 判断源代码文件格式是否为xaml
                {
                    Console.Write("Error: Wrong format - \"" + fileFmt + "\"");
                    continue;
                }
                Console.WriteLine("Parsing: " + path);
                try
                {
                    xDoc.Load(path);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                    Console.ReadKey();
                    return;
                }

                xp.parseXamlToCs(xDoc, ref outCodeStr);
                if (!Directory.Exists(outCsFolderPath))
                {
                    try
                    {
                        Directory.CreateDirectory(outCsFolderPath);
                    }
                    catch (Exception)
                    {
                        Console.WriteLine("Error: Creating output folder failed");
                        return;
                    }
                }
                try
                {
                    string srcFileName = path.Replace("\\", "/").Split('/').Last();
                    string dstFileName = srcFileName.Substring(0, srcFileName.IndexOf("xaml")) + "cs";
                    FileStream fs = File.Open(outCsFolderPath + "\\" + dstFileName, FileMode.Create);
                    StreamWriter sw = new StreamWriter(fs);
                    sw.Write(outCodeStr);
                    sw.Flush();
                    sw.Close();
                }
                catch (Exception)
                {
                    {
                        Console.WriteLine("Error: Writing cs failed");
                    }
                }
            }
        }
        static public void waitTime(int dueInSeconds)
        { 
            Console.WriteLine();
            int top = Console.CursorTop, left = Console.CursorLeft;
            Console.WriteLine("Pause: Exit in " + dueInSeconds + " seconds if with no input".PadRight(60));
            DateTime dueTime = DateTime.Now + TimeSpan.FromSeconds(dueInSeconds);

            int timerTop = Console.CursorTop, timerLeft = Console.CursorLeft;
            Thread t = new Thread(new ThreadStart(() =>
            {
                while (true)
                {
                    TimeSpan remaining = dueTime - DateTime.Now;
                    if (remaining.TotalSeconds <= 0) Environment.Exit(0);
                    Console.SetCursorPosition(timerLeft, timerTop);
                    Console.Write(string.Format("Remaining seconds: {0,5}", (int)remaining.TotalSeconds));
                    Thread.Sleep(1000);
                }
            }));
            t.Start();
            Console.ReadKey();
            t.Abort();
            Console.SetCursorPosition(left, top);
            Console.WriteLine("Countdown timer is stopped".PadRight(60));
            Console.WriteLine("".PadRight(60));
            Console.WriteLine("Pause: Press any key to exit...".PadRight(60));
        }
        static void Main(string[] args)
        {
            WfParsing wfParsing = new WfParsing();
            //string xamlListPath = args.Length > 0 ? args[0] : "..\\..\\..\\BOC.Workflow.UnitTest\\WF\\Center";
            string xamlListPath = args.Length > 0 ? args[0] : ConfigurationManager.AppSettings.Get("XamlListPath");
            List<string> filePathArr = new List<string>();
            if (Directory.Exists(xamlListPath))
            {
                wfParsing.readFolder(xamlListPath, ref filePathArr);
            }
            else if (File.Exists(xamlListPath))
            {
                wfParsing.readJson(xamlListPath, ref filePathArr);
            }
            else
            {
                Console.WriteLine("Error: Invalid directory - " + xamlListPath);
                return;
            }
            string outCsFolderPath = args.Length > 1 ? args[1] : ConfigurationManager.AppSettings.Get("CsFolderPath");
            wfParsing.readXamlFile(ref filePathArr, outCsFolderPath);
            int dueInSeconds = int.Parse(ConfigurationManager.AppSettings.Get("WaitTime"));
            waitTime(dueInSeconds);
            Console.ReadKey();
        }
    }
}
