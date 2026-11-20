using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using LitJson;

namespace ustcori_crack
{
    public class Utiles
    {
        public static string TableDataRandomize(string tableStr,ref bool changed)
        {
            string[] strArray1 = tableStr.Split(';');
            string[][] strArray2 = new string[strArray1.Length][];
            bool hasEmpty = false;
            Random rd = new Random();
            for (int i = 0; i < strArray1.Length; ++i)
            {
                if (strArray1[i].Contains("(") && strArray1[i].Contains(")"))
                {
                    string str = strArray1[i].Substring(1, strArray1[i].Length - 2);
                    strArray2[i] = str.Split(',');
                    double a;
                    for (int j = 0; j < strArray2[i].Length; ++j)
                    {
                        if (string.IsNullOrEmpty(strArray2[i][j]) || strArray2[i][j] == "*" || (double.TryParse(strArray2[i][j], out a) && a == 0))
                        {
                            strArray2[i][j] = ((double)rd.Next(0, 1000) / 10).ToString();
                            hasEmpty = true;
                        }
                    }
                }
            }
            if (hasEmpty)
            {
                string ans = "";
                for (int i = 0; i < strArray2.Length; ++i)
                {
                    ans += "(" + string.Join(",", strArray2[i]) + (i < strArray2.Length - 1 ? ");" : ")");
                }
                changed = true;
                return ans;
            }
            else
            {
                changed = false;
                return tableStr;
            }
        }

        public static XmlNode CreateNewNode(XmlNode orignNode,string nodeName, string InnerText)
        {
            XmlDocument doc = orignNode.OwnerDocument;
            XmlNode n = doc.CreateElement(nodeName);
            n.InnerText = InnerText;
            return n;
        }
        /// <summary>
        /// 本次运行实际使用的解包根目录。NewSiteFlow.Unpack 探测成功后会写这里，
        /// LoadXml 优先用它，避免“换了个可写目录却还去 %TEMP%\lab 找配置”。
        /// </summary>
        public static string LabRoot;

        public static XmlDocument LoadXml(string filePath = null)
        {
            XmlDocument xmlDocument = new XmlDocument();
            if (filePath == null)
            {
                try
                {
                    string root = LabRoot ?? (Environment.GetEnvironmentVariable("TEMP") + "\\lab");
                    // 实验数据一定由“正在运行的那个 Main.exe”写出来，所以先按可执行文件定位，
                    // 再退回目录枚举。原版只认 lab\ 与 lab\4\，遇到 lab\3\ 会读错文件。
                    var bases = new List<string>();
                    string[] execCandidates = { "\\Main.exe", "\\4\\Main.exe", "\\3\\Main.exe" };
                    foreach (string rel in execCandidates)
                    {
                        string exePath = root + rel;
                        if (!File.Exists(exePath)) continue;
                        string dir = Path.GetDirectoryName(exePath);
                        if (!string.IsNullOrEmpty(dir) && !bases.Contains(dir)) bases.Add(dir);
                    }
                    foreach (string dir in new[] { root, root + "\\4", root + "\\3", root + "\\5", root + "\\main" })
                        if (!bases.Contains(dir)) bases.Add(dir);

                    foreach (string dir in bases)
                    {
                        string simExp = dir + "\\SimExpConfig\\SimExpConfig";
                        if (File.Exists(simExp + ".bin") || File.Exists(simExp + ".xml")) { filePath = simExp; break; }
                        string exam = dir + "\\SimExpConfig\\ExamConfig";
                        if (File.Exists(exam + ".bin") || File.Exists(exam + ".xml")) { filePath = exam; break; }
                        string expExam = dir + "\\ExpConfig\\ExamConfig";
                        if (File.Exists(expExam + ".bin") || File.Exists(expExam + ".xml")) { filePath = expExam; break; }
                        string streaming = dir + "\\Main_Data\\StreamingAssets\\ExamConfig";
                        if (File.Exists(streaming + ".bin") || File.Exists(streaming + ".xml")) { filePath = streaming; break; }
                    }
                    if (filePath == null)
                    {
                        Console.WriteLine("未在 " + root + " 下找到 ExamConfig/SimExpConfig 实验配置。");
                        return xmlDocument;
                    }
                    Console.WriteLine("实验配置：" + filePath);

                    if (File.Exists(filePath + ".bin"))
                    {
                        FileStream fs = new FileStream(filePath + ".bin", FileMode.Open);
                        byte[] zipdata = new byte[fs.Length];
                        fs.Read(zipdata, 0, zipdata.Length);
                        fs.Close();
                        byte[] a = JsonReader.Read(zipdata, "haohaoxuexi,tiantianxiangshang.");
                        MemoryStream byteStream = new MemoryStream(a);
                        XmlTextReader xmlTextReader = new XmlTextReader(byteStream);
                        xmlDocument.Load(xmlTextReader);
                    }
                    else
                    {
                        xmlDocument.Load(filePath + ".xml");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("读取实验数据失败：" + ex.Message + ex.StackTrace);
                }
            }
            else
            {
                xmlDocument.Load(filePath);
            }
            return xmlDocument;
        }

        /// <summary>
        /// 把实验数据改写成满分。真正的求解逻辑在 FullScoreSolver 里，
        /// 这里只保留原有调用点（FakeHall/FakeHall2/NewSiteFlow 都调它）。
        /// </summary>
        public static XmlDocument ModifyExperiment(XmlDocument xmlDocument)
        {
            FullScoreReport report = FullScoreSolver.Apply(xmlDocument, FullScoreSolver.Mode.Solve);
            Console.WriteLine("满分求解：卷面 " + Up(report.PaperTotal) + " 分，可得 " + Up(report.Achievable) + " 分");
            if (!report.IsFullScore)
            {
                foreach (string w in report.Warnings) Console.WriteLine("  ! " + w);
            }
            return xmlDocument;
        }

        private static string Up(double v)
        {
            return Math.Round(v, 6).ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
