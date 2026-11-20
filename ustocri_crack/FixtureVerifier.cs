using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ustcori_crack
{
    /// <summary>
    /// 校验仿真服务端捕获到的提交数据。
    ///
    /// 服务端只负责原样存档，真正的判据在这里：用 LitJson 解开 Content，
    /// 确认它就是本地求解出的那份 XML；再逐条检查 FileName / LabName / Key /
    /// RecordID 的形状。这样「提交链路是否正确」有了可复现的判据，
    /// 而不是靠肉眼看着像。
    /// </summary>
    internal static class FixtureVerifier
    {
        public static int Run(string archivePath, string expectedPath)
        {
            var checks = new List<string>();
            int failed = 0;

            Action<string, bool, string> check = (name, ok, detail) =>
            {
                if (!ok) failed++;
                Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + name + (string.IsNullOrEmpty(detail) ? "" : "   " + detail));
            };

            JArray captured = JArray.Parse(File.ReadAllText(archivePath, Encoding.UTF8));
            Console.WriteLine("捕获请求数：" + captured.Count);

            var byPath = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
            foreach (JToken item in captured)
            {
                string path = (string)item["path"] ?? "";
                string tail = path.Substring(path.LastIndexOf('/') + 1);
                byPath[tail] = (JObject)item["form"];
                Console.WriteLine("  " + path);
            }

            check("四个接口都被调用到",
                byPath.ContainsKey("FindByLABID") && byPath.ContainsKey("ucControlMethod")
                && byPath.ContainsKey("SetLabTimeRecordStart") && byPath.ContainsKey("UpdateRecord"),
                string.Join(",", byPath.Keys));

            JObject update;
            if (!byPath.TryGetValue("UpdateRecord", out update))
            {
                Console.WriteLine("没有捕获到 UpdateRecord，后续检查跳过。");
                return 1;
            }

            foreach (string field in new[] { "UserID", "LabID", "LabName", "RecordID", "FileName", "Content", "Key" })
                check("UpdateRecord 含 " + field, update[field] != null && ((string)update[field]).Length > 0,
                    Truncate((string)update[field], 50));

            string fileName = (string)update["FileName"] ?? "";
            check("FileName 形如 UserID_yyyyMMddHHmmss.xml",
                Regex.IsMatch(fileName, @"^\d+_\d{14}\.xml$"), fileName);

            string labName = (string)update["LabName"] ?? "";
            check("LabName 为 实验名.课程ID.模式 三段式",
                Regex.IsMatch(labName, @"^.+\.-?\d+\.\d+$"), labName);

            string key = (string)update["Key"] ?? "";
            check("Key 长度在 8..128", key.Length >= 8 && key.Length <= 128, key.Length.ToString(CultureInfo.InvariantCulture));

            // 核心：解开 Content，和本地求解结果逐字节比对
            byte[] content;
            try
            {
                content = Convert.FromBase64String((string)update["Content"]);
                check("Content 是合法 base64", true, content.Length + " 字节");
            }
            catch (Exception ex)
            {
                check("Content 是合法 base64", false, ex.Message);
                return 1;
            }

            string decrypted;
            try
            {
                byte[] plain = LitJson.JsonReader.Read(content, "haohaoxuexi,tiantianxiangshang.");
                decrypted = Encoding.UTF8.GetString(plain);
                check("Content 能用官方密钥解开", true, plain.Length + " 字节明文");
            }
            catch (Exception ex)
            {
                check("Content 能用官方密钥解开", false, ex.GetType().Name + ": " + ex.Message);
                return 1;
            }

            check("解密结果是 XML", decrypted.TrimStart().StartsWith("<", StringComparison.Ordinal), Truncate(decrypted, 60));

            var doc = new XmlDocument();
            try
            {
                doc.LoadXml(decrypted);
                check("XML 可解析且根节点为 ExamPaper", doc.DocumentElement != null && doc.DocumentElement.Name == "ExamPaper",
                    doc.DocumentElement == null ? "(空)" : doc.DocumentElement.Name);
            }
            catch (Exception ex)
            {
                check("XML 可解析且根节点为 ExamPaper", false, ex.Message);
                return 1;
            }

            // 关键判据：服务端收到的就是本地求解的那份数据
            if (!string.IsNullOrEmpty(expectedPath) && File.Exists(expectedPath))
            {
                var expected = new XmlDocument();
                expected.Load(expectedPath);

                double serverSide = new PaperReviewer().ReviewOpXml(doc);
                double localSide = new PaperReviewer().ReviewOpXml(expected);
                check("服务端收到的数据复算得分 == 本地求解得分",
                    Math.Abs(serverSide - localSide) < 0.01,
                    "服务端 " + serverSide.ToString("0.##", CultureInfo.InvariantCulture)
                    + " / 本地 " + localSide.ToString("0.##", CultureInfo.InvariantCulture));

                check("服务端收到的数据达到卷面满分",
                    Math.Abs(serverSide - 100) < 0.01, serverSide.ToString("0.##", CultureInfo.InvariantCulture));

                check("两份 XML 的 RealResult 完全一致",
                    RealResults(doc) == RealResults(expected),
                    RealResults(doc).Length + " 字符");
            }
            else
            {
                Console.WriteLine("  （未提供本地求解结果，跳过逐字节比对）");
            }

            Console.WriteLine();
            Console.WriteLine(failed == 0 ? "提交链路校验通过。" : "有 " + failed + " 项未通过。");
            return failed == 0 ? 0 : 1;
        }

        private static string RealResults(XmlDocument doc)
        {
            var sb = new StringBuilder();
            XmlNodeList nodes = doc.SelectNodes("//RealResult");
            if (nodes != null)
                foreach (XmlNode n in nodes) sb.Append(n.InnerText).Append('\u0001');
            return sb.ToString();
        }

        private static string Truncate(string value, int width)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Length <= width ? value : value.Substring(0, width) + "…";
        }
    }
}
