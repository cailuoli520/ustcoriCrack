using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Xml;
using LitJson;
using PackageLibrary;
using USTCORi.WebLabClient;
using USTCORi.WebLabClient.DataModel;

namespace ustcori_crack
{
    /// <summary>
    /// 2023 版站点（JSON 启动链接）的纯 HTTP 流程：排队、开始计时、取实验包、启动、提交。
    /// 端点取自安装目录下 USTCORi.ExamSystem.Client.exe 的静态 IL。
    /// </summary>
    public sealed class NewSiteFlow
    {
        private const string FormContentType = "application/x-www-form-urlencoded; charset=utf-8";

        private readonly LaunchArguments launch;
        private readonly LABINFO lab;
        private readonly string baseDirectory;
        private readonly bool headless;
        private readonly int waitSeconds;
        private int ucId;
        private int runningId;

        public NewSiteFlow(LaunchArguments launch, LABINFO lab, string baseDirectory)
            : this(launch, lab, baseDirectory, false, 0)
        {
        }

        /// <param name="headless">true 时不启动仿真程序、不等待按键，直接求解后提交。</param>
        /// <param name="waitSeconds">提交前等待秒数（服务端会检查作答时长）。</param>
        public NewSiteFlow(LaunchArguments launch, LABINFO lab, string baseDirectory, bool headless, int waitSeconds)
        {
            this.launch = launch;
            this.lab = lab;
            this.baseDirectory = baseDirectory;
            this.headless = headless;
            this.waitSeconds = waitSeconds;
        }

        private string LabMode
        {
            get { return string.IsNullOrEmpty(launch.Mode) ? "0" : launch.Mode; }
        }

        public int Run()
        {
            Console.WriteLine("排队中……");
            string error;
            int slot = EnterQueue(out error);
            if (slot < 0)
            {
                Console.WriteLine("您前面还有" + (-slot).ToString(CultureInfo.InvariantCulture) + "人在排队，请耐心等候！");
                return 0;
            }
            if (slot == 0)
            {
                Console.WriteLine("您当前排在第一位，请耐心等候！");
                return 0;
            }
            ucId = slot;
            try
            {
                return RunExperiment();
            }
            finally
            {
                LeaveQueue();
            }
        }

        private int RunExperiment()
        {
            string error;
            runningId = StartRecord(out error);
            if (runningId <= 0)
            {
                Console.WriteLine("记录实验开始时间失败：" + (error ?? ("返回 " + runningId.ToString(CultureInfo.InvariantCulture))));
                return 2;
            }

            string package = EnsurePackage();
            if (package == null) return 2;
            string work = Unpack(package);
            if (work == null) return 2;

            if (!headless)
            {
                string executable = ResolveExecutable(work);
                if (executable == null)
                {
                    Console.WriteLine("仿真实验部署错误，实验无法运行！");
                    return 2;
                }

                Console.WriteLine("准备完毕，接下来将启动实验程序，");
                Console.WriteLine("请在所有实验内容的实验数据表格中点击“完成操作”或“保存状态”等类似按钮(如果有)，之后点击“结束操作”。");
                Console.WriteLine("输入 y 继续。");
                if (Console.ReadLine() != "y") return 0;

                Process process = new Process();
                process.StartInfo.FileName = executable;
                process.StartInfo.WorkingDirectory = Path.GetDirectoryName(executable) + Path.DirectorySeparatorChar;
                Console.WriteLine("启动实验程序：" + executable);
                process.Start();
                process.WaitForExit();
                Console.WriteLine("实验程序已退出，开始整理实验数据……");
            }
            else
            {
                Console.WriteLine("无头模式：跳过仿真启动，直接读取实验配置并求解。");
            }

            XmlDocument document = Utiles.LoadXml();
            if (document == null || document.DocumentElement == null)
            {
                Console.WriteLine("读取实验数据失败，未提交。");
                return 2;
            }
            XmlDocument modified = Utiles.ModifyExperiment(document);

            // 导出求解结果，便于与服务端实际收到的 Content 逐字节比对
            try
            {
                modified.Save(Path.Combine(baseDirectory, "solved-exam.xml"));
            }
            catch (Exception ex)
            {
                Console.WriteLine("导出求解结果失败：" + ex.Message);
            }

            // 提交前用官方判据等价实现复算，防止把低分数据送出去
            XmlDocument replayDoc = (XmlDocument)modified.CloneNode(true);
            double replay = new PaperReviewer().ReviewOpXml(replayDoc);
            FullScoreReport report = FullScoreSolver.Apply((XmlDocument)document.CloneNode(true), FullScoreSolver.Mode.Audit);
            Console.WriteLine("提交前复算：" + replay.ToString("0.##", CultureInfo.InvariantCulture)
                + " / 卷面 " + report.PaperTotal.ToString("0.##", CultureInfo.InvariantCulture) + " 分");

            if (!headless)
            {
                Console.WriteLine("实验数据修改完成，按 y 显示答案，其他键跳过：");
                if (Console.ReadKey(true).KeyChar == 'y') new PaperReviewer().ReviewOpXml(modified);
            }

            int waitTime;
            if (headless)
            {
                waitTime = waitSeconds;
            }
            else
            {
                Console.WriteLine("请输入在提交前需要等待时间（秒），输入 -1 阻止提交：");
                if (!int.TryParse(Console.ReadLine(), out waitTime)) waitTime = -1;
            }
            if (waitTime < 0)
            {
                Console.WriteLine("已阻止提交，本次操作不写入记录。");
                return 0;
            }
            if (waitTime > 0)
            {
                Console.WriteLine("提交前等待 " + waitTime.ToString(CultureInfo.InvariantCulture) + " 秒……");
                Thread.Sleep(waitTime * 1000);
            }

            string result = Submit(modified, out error);
            if (result == null)
            {
                Console.WriteLine("提交失败：" + error);
                return 2;
            }
            Console.WriteLine("提交完成：" + result.Trim());
            Console.WriteLine("可查看分数。");
            if (!headless) Console.ReadKey(true);
            return 0;
        }


        /// <summary>
        /// 离线验收：解包 → 审计原始作答 → 满分求解 → 用官方评分器的等价实现独立复算。
        /// 全程不联网、不提交，只回答一个问题：这份实验数据最多能拿多少分，我求出来的解是否真的落在最高分档。
        /// </summary>
        public static int SelfTest(string link)
        {
            LaunchArguments launch = LaunchArguments.Parse(link);
            Console.WriteLine("启动格式：" + (launch.IsJson ? "JSON" : "旧版位置参数") + "；流程：" + (launch.UseNewHall ? "新版" : "旧版"));
            string package = null;
            string directory = AppDomain.CurrentDomain.BaseDirectory;
            string candidate = Path.Combine(directory, "lab.lab");
            if (File.Exists(candidate)) package = candidate;
            if (package == null)
            {
                // 已经解包过的情况：直接读 TEMP\lab
                string unpacked = Path.Combine(Path.GetTempPath(), "lab");
                if (Directory.Exists(unpacked)) package = unpacked;
            }
            Console.WriteLine("自检实验包：" + (package ?? "(未找到 lab.lab)"));
            if (package == null) return 2;

            string work = package;
            if (File.Exists(package))
            {
                work = Unpack(package);
                if (work == null) return 2;
                string executable = ResolveExecutable(work);
                Console.WriteLine("解包目录：" + work);
                Console.WriteLine("可执行文件：" + (executable ?? "(未找到 Main.exe)"));
                Console.WriteLine("解包文件数：" + Directory.GetFiles(work, "*", SearchOption.AllDirectories).Length);
            }

            XmlDocument raw = Utiles.LoadXml();
            if (raw == null || raw.DocumentElement == null)
            {
                Console.WriteLine("自检读取实验数据失败。");
                return 2;
            }

            // 1) 审计：完全不动 RealResult，只看“原样提交”能拿多少
            XmlDocument auditDoc = (XmlDocument)raw.CloneNode(true);
            FullScoreReport audit = FullScoreSolver.Apply(auditDoc, FullScoreSolver.Mode.Audit);
            Console.WriteLine();
            Console.WriteLine("=== 审计：原样提交 ===");
            Console.WriteLine("可得 " + audit.Achievable.ToString("0.##", CultureInfo.InvariantCulture)
                + " / " + audit.PaperTotal.ToString("0.##", CultureInfo.InvariantCulture) + " 分");

            // 2) 求解：按 JudgeRule 反解满分取值
            XmlDocument solved = (XmlDocument)raw.CloneNode(true);
            FullScoreReport report = FullScoreSolver.Apply(solved, FullScoreSolver.Mode.Solve);
            Console.WriteLine();
            Console.WriteLine("=== 求解：满分改写 ===");
            Console.WriteLine("可得 " + report.Achievable.ToString("0.##", CultureInfo.InvariantCulture)
                + " / " + report.PaperTotal.ToString("0.##", CultureInfo.InvariantCulture) + " 分，差距 "
                + report.Gap.ToString("0.##", CultureInfo.InvariantCulture));

            // 3) 独立复算：用官方 GainScore 的等价实现把改写后的数据重新判一遍
            XmlDocument replayDoc = (XmlDocument)solved.CloneNode(true);
            double replay = new PaperReviewer().ReviewOpXml(replayDoc);
            Console.WriteLine();
            Console.WriteLine("=== 复算：官方判据等价实现 ===");
            Console.WriteLine("复算得分 " + replay.ToString("0.##", CultureInfo.InvariantCulture) + " 分");

            bool pass = Math.Abs(replay - report.Achievable) < 0.01
                        && Math.Abs(replay - report.PaperTotal) < 0.01;
            Console.WriteLine();
            Console.WriteLine(pass
                ? "验收通过：求解结果与官方判据一致，且已达到卷面满分。"
                : (Math.Abs(replay - report.Achievable) < 0.01
                    ? "求解自洽，但卷面分拿不满（见下方说明）。"
                    : "验收失败：求解结果与官方判据不一致，请勿提交。"));

            // 把求解后的实验数据落盘，便于逐字段核对
            string dump = Path.Combine(directory, "solved-exam.xml");
            try
            {
                solved.Save(dump);
                Console.WriteLine("求解结果已导出：" + dump);
            }
            catch (Exception ex)
            {
                Console.WriteLine("导出求解结果失败：" + ex.Message);
            }

            if (!pass || report.Warnings.Count > 0 || audit.Gap > 0)
            {
                Console.WriteLine();
                Console.WriteLine(report.Format());
            }
            if (audit.Achievable + 0.01 < report.Achievable)
            {
                Console.WriteLine("原样提交会少拿 " + (report.Achievable - audit.Achievable).ToString("0.##", CultureInfo.InvariantCulture)
                    + " 分（标准答案不在最高分档，或答案本身缺失）。");
            }
            Console.WriteLine("自检未联网、未提交。");
            return pass ? 0 : 3;
        }

        /// <summary>只审计、不改写：把当前实验数据的最优解与差距打出来。</summary>
        public static int Audit()
        {
            XmlDocument raw = Utiles.LoadXml();
            if (raw == null || raw.DocumentElement == null)
            {
                Console.WriteLine("读取实验数据失败，未找到已解包的实验。");
                return 2;
            }
            FullScoreReport audit = FullScoreSolver.Apply(raw, FullScoreSolver.Mode.Audit);
            FullScoreReport best = FullScoreSolver.Apply((XmlDocument)raw.CloneNode(true), FullScoreSolver.Mode.Solve);
            Console.WriteLine("=== 审计（不改动数据）===");
            Console.WriteLine(audit.Format());
            Console.WriteLine("=== 满分解（未写回）===");
            Console.WriteLine(best.Format());
            return 0;
        }

        private int EnterQueue(out string error)
        {
            error = null;
            var token = new Dictionary<string, object>
            {
                { "UserID", launch.StudentId },
                { "ServiceID", "开始实验" },
                { "OperType", 0 },
                { "LabID", lab.LabID }
            };
            string body = "token=" + Newtonsoft.Json.JavaScriptConvert.SerializeObject(token);
            try
            {
                string response = Post("ServiceAPI/ucControlMethod", body);
                int value;
                if (!int.TryParse(response.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                {
                    error = "排队接口返回了非数字内容。";
                    return 0;
                }
                return value;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Console.WriteLine("排队失败：" + ex.Message);
                return 0;
            }
        }

        private void LeaveQueue()
        {
            if (ucId <= 0) return;
            try
            {
                var token = new Dictionary<string, object>
                {
                    { "ID", ucId },
                    { "OperType", 1 }
                };
                Post("ServiceAPI/ucControlMethod", "token=" + Newtonsoft.Json.JavaScriptConvert.SerializeObject(token));
            }
            catch (Exception ex)
            {
                Console.WriteLine("释放排队失败：" + ex.Message);
            }
            ucId = 0;
        }

        private int StartRecord(out string error)
        {
            error = null;
            string body = "USERID=" + Uri.EscapeDataString(launch.StudentId)
                + "&LABID=" + lab.LabID.ToString(CultureInfo.InvariantCulture)
                + "&ClientIP=" + Uri.EscapeDataString(launch.ClientIp ?? "")
                + "&ClientArea=" + Uri.EscapeDataString(launch.ClientArea ?? "")
                + "&CourseID=" + Uri.EscapeDataString(launch.CourseId ?? "");
            try
            {
                string response = Post("ServiceAPI/SetLabTimeRecordStart", body).Trim();
                int value;
                if (!int.TryParse(response, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return -1;
                if (value < 0) error = "服务器返回 " + value.ToString(CultureInfo.InvariantCulture) + "。";
                return value;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return -1;
            }
        }

        private string Submit(XmlDocument document, out string error)
        {
            error = null;
            string fileName = launch.StudentId + "_" + DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + ".xml";
            byte[] key = null;
            try
            {
                byte[] plain;
                using (MemoryStream stream = new MemoryStream())
                {
                    document.Save(stream);
                    plain = stream.ToArray();
                }
                key = LitJson.JsonWriter.Write(plain, "haohaoxuexi,tiantianxiangshang.");
                string signature = LitJson.Sec.GenerateKey(launch.StudentId + "," + lab.LabID.ToString(CultureInfo.InvariantCulture) + fileName);
                string body = "UserID=" + Uri.EscapeDataString(launch.StudentId)
                    + "&LabID=" + lab.LabID.ToString(CultureInfo.InvariantCulture)
                    + "&LabName=" + Uri.EscapeDataString(lab.LABNAME + "." + (launch.CourseId ?? "") + "." + LabMode)
                    + "&RecordID=" + runningId.ToString(CultureInfo.InvariantCulture)
                    + "&FileName=" + Uri.EscapeDataString(fileName)
                    + "&Content=" + Uri.EscapeDataString(Convert.ToBase64String(key))
                    + "&Key=" + Uri.EscapeDataString(signature);
                string response = Post("ServiceAPI/UpdateRecord", body);
                return response;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        private string EnsurePackage()
        {
            string folder = Path.Combine(baseDirectory, "DownLoad", lab.LABTYPENAME ?? "");
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, lab.LABNAME + ".lab");
            if (File.Exists(file) && new FileInfo(file).Length > 0) return file;
            if (string.IsNullOrEmpty(lab.LabFileUrl))
            {
                Console.WriteLine("实验信息缺少实验包地址。");
                return null;
            }
            Console.WriteLine("开始下载……");
            try
            {
                using (WebClient client = new WebClient())
                {
                    client.DownloadFile(Address("Upload/lab/" + lab.LabFileUrl), file);
                }
                if (!File.Exists(file) || new FileInfo(file).Length == 0)
                {
                    Console.WriteLine("下载失败：文件为空。");
                    return null;
                }
                Console.WriteLine("下载完成。");
                return file;
            }
            catch (Exception ex)
            {
                Console.WriteLine("下载失败：" + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// 解包目录候选。
        ///
        /// 官方客户端写死 %TEMP%\lab，但 %TEMP% 在受限账户、组策略重定向、
        /// 或者被安全软件锁定的机器上经常不可写（实测托管进程会在
        /// Directory.CreateDirectory 处抛 UnauthorizedAccessException，而同一个
        /// 路径用 cmd 反而能写）。所以这里按优先级探测，第一个真正写得进去的胜出：
        /// 官方布局优先（仿真程序本身也认 %TEMP%\lab），其次退到程序目录旁。
        /// </summary>
        private static readonly string[] UnpackRoots =
        {
            Path.Combine(Path.GetTempPath(), "lab"),
            Path.Combine(Path.GetTempPath(), "ustcori-lab"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "labwork"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ustcori-lab")
        };

        /// <summary>探测一个目录是否真的可用：能建、能写、能删，缺一不可。</summary>
        private static bool IsUsable(string directory)
        {
            try
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
                Directory.CreateDirectory(directory);
                string probe = Path.Combine(directory, ".write-probe");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string Unpack(string package)
        {
            Exception last = null;
            foreach (string candidate in UnpackRoots)
            {
                if (!IsUsable(candidate)) continue;
                for (int attempt = 1; attempt <= 2; attempt++)
                {
                    try
                    {
                        Package pack = new Package(new FileInfo(package));
                        pack.SetDirectoryToUnPack(candidate);
                        pack.Run();

                        // 解包产物必须通过 PE 校验。实测有安全软件会在解包后改写
                        // Main.exe 的正文（前 260 字节保留，0x104..0x27FF 被覆写），
                        // Windows 加载器随即报 ERROR_BAD_EXE_FORMAT。这种损坏无法
                        // 从字节上修复，只能清掉重解，所以这里做一次自动重试。
                        string problem = ValidateUnpack(candidate);
                        if (problem == null)
                        {
                            Utiles.LabRoot = candidate;      // 让 LoadXml 找到同一个目录
                            return candidate;
                        }

                        Console.WriteLine("解包产物校验失败（第 " + attempt + " 次）：" + problem);
                        if (attempt == 1)
                        {
                            Console.WriteLine("正在清空并重新解包……");
                            try { Directory.Delete(candidate, true); }
                            catch (Exception ex) { Console.WriteLine("清空失败：" + ex.Message); break; }
                            try { Directory.CreateDirectory(candidate); }
                            catch (Exception ex) { Console.WriteLine("重建失败：" + ex.Message); break; }
                        }
                        else
                        {
                            Console.WriteLine("重解后仍然损坏，可能是安全软件持续拦截该目录。");
                        }
                    }
                    catch (Exception ex)
                    {
                        last = ex;
                        Console.WriteLine("解包到 " + candidate + " 失败：" + ex.Message);
                        break;
                    }
                }
            }
            Console.WriteLine("解包失败：" + (last == null
                ? "所有候选目录都不可用（" + string.Join("、", UnpackRoots) + "）"
                : last.Message));
            return null;
        }

        /// <summary>
        /// 检查解包目录里的可执行文件是不是完整的 PE。
        ///
        /// 只查三件事，够用且不误报：
        ///   1. e_lfanew 指向的偏移在文件内，且那里是 "PE\0\0"；
        ///   2. 节表第一项的名字是可打印 ASCII（.text / .rsrc / .reloc …）；
        ///   3. 每个节的原始数据指针 + 长度不越界。
        /// 损坏文件会同时违反 1 和 2，正常文件不会。
        /// </summary>
        private static string ValidateUnpack(string work)
        {
            string executable = ResolveExecutable(work);
            if (executable == null) return "解包后找不到 Main.exe";

            string[] binaries;
            try
            {
                binaries = Directory.GetFiles(Path.GetDirectoryName(executable), "*.exe");
            }
            catch (Exception ex)
            {
                return "无法枚举可执行文件：" + ex.Message;
            }

            foreach (string binary in binaries)
            {
                string problem = CheckPe(binary);
                if (problem != null) return Path.GetFileName(binary) + " " + problem;
            }
            return null;
        }

        private static string CheckPe(string path)
        {
            byte[] data;
            try { data = File.ReadAllBytes(path); }
            catch (Exception ex) { return "读取失败：" + ex.Message; }

            if (data.Length < 0x100) return "文件过小（" + data.Length + " 字节）";
            if (data[0] != 0x4D || data[1] != 0x5A) return "缺少 MZ 头";

            int peOffset = BitConverter.ToInt32(data, 0x3C);
            if (peOffset <= 0 || peOffset + 24 > data.Length) return "e_lfanew 越界（0x" + peOffset.ToString("X") + "）";
            if (data[peOffset] != 0x50 || data[peOffset + 1] != 0x45) return "缺少 PE 签名";

            int sections = BitConverter.ToInt16(data, peOffset + 6);
            int optionalSize = BitConverter.ToInt16(data, peOffset + 20);
            if (sections <= 0 || sections > 96) return "节数异常（" + sections + "）";

            int tableOffset = peOffset + 24 + optionalSize;
            if (tableOffset + sections * 40 > data.Length) return "节表越界";

            // 第一节名必须全是可打印字符，损坏文件的这里是一堆随机字节
            for (int i = 0; i < 8; i++)
            {
                byte b = data[tableOffset + i];
                if (b == 0) break;
                if (b < 0x20 || b > 0x7E) return "节表损坏（第一节名不可打印）";
            }

            for (int i = 0; i < sections; i++)
            {
                int entry = tableOffset + i * 40;
                int rawSize = BitConverter.ToInt32(data, entry + 16);
                int rawPointer = BitConverter.ToInt32(data, entry + 20);
                if (rawSize == 0) continue;
                if (rawPointer < 0 || rawSize < 0 || (long)rawPointer + rawSize > data.Length)
                    return "第 " + (i + 1) + " 节数据越界";
            }
            return null;
        }

        private static string ResolveExecutable(string work)
        {
            string[] candidates = { Path.Combine(work, "Main.exe"), Path.Combine(work, "4", "Main.exe"), Path.Combine(work, "3", "Main.exe") };
            foreach (string candidate in candidates)
                if (File.Exists(candidate)) return candidate;
            return null;
        }

        private Uri Address(string relative)
        {
            return new Uri(launch.SiteBaseUri, relative);
        }

        private string Post(string relative, string body)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(Address(relative));
            request.Method = "POST";
            request.ContentType = FormContentType;
            request.Timeout = 20000;
            request.ReadWriteTimeout = 20000;
            request.AllowAutoRedirect = false;
            request.ServicePoint.Expect100Continue = false;
            byte[] data = Encoding.UTF8.GetBytes(body);
            request.ContentLength = data.Length;
            using (Stream stream = request.GetRequestStream())
                stream.Write(data, 0, data.Length);
            using (WebResponse response = request.GetResponse())
            using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
            {
                string text = reader.ReadToEnd();
                // 站点这些接口（ucControlMethod/SetLabTimeRecordStart/UpdateRecord）一律把
                // Content-Type 写成 text/html，即使是纯数字或纯文本。所以只按正文判定：
                // 真正返回 HTML 页面时正文一定以 "<" 开头，此时才是错误。
                if (text.TrimStart().StartsWith("<", StringComparison.Ordinal))
                    throw new InvalidOperationException("服务器返回了 HTML 页面而不是接口数据：" + relative);
                return text;
            }
        }
    }
}
