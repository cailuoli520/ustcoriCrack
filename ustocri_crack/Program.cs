using System;
using System.Threading;

namespace ustcori_crack
{
    internal class Program
    {
        public static bool shouldAlert = true;
        static int Main(string[] args)
        {
            Console.Title = "Ustcori Crack V1.3.0 - 满分求解 + 官方判据复算";
            bool checkOnly = false;
            bool metadataOnly = false;
            bool selfTest = false;
            bool auditOnly = false;
            bool runCases = false;
            bool submit = false;
            bool probe = false;
            int waitSeconds = 0;
            string suppliedLink = null;
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg == "--check-launch") checkOnly = true;
                else if (arg == "--help" || arg == "-h" || arg == "/?") return Usage.Show();
                else if (arg == "--metadata-only") metadataOnly = true;
                else if (arg == "--self-test") selfTest = true;
                else if (arg == "--audit") auditOnly = true;
                else if (arg == "--cases") runCases = true;
                else if (arg == "--submit") submit = true;
                else if (arg == "--probe-payload") probe = true;
                else if (arg == "--verify-fixture")
                {
                    if (i + 1 >= args.Length)
                    {
                        Console.WriteLine("--verify-fixture 需要捕获文件路径。");
                        return 1;
                    }
                    string archive = args[++i];
                    string expected = i + 1 < args.Length ? args[++i] : null;
                    return FixtureVerifier.Run(archive, expected);
                }
                else if (arg == "--wait")
                {
                    int parsed;
                    if (i + 1 >= args.Length || !int.TryParse(args[i + 1], out parsed))
                    {
                        Console.WriteLine("--wait 需要一个秒数。");
                        return 1;
                    }
                    waitSeconds = parsed;
                    i++;
                }
                else if (suppliedLink == null) suppliedLink = arg;
            }
            if (runCases) return SolverCases.Run();
            if (probe) return PayloadProbe.Run();
            if (selfTest) return NewSiteFlow.SelfTest(suppliedLink);
            if (auditOnly) return NewSiteFlow.Audit();
            bool holdOnError = !checkOnly && !metadataOnly && !auditOnly && !runCases && !probe && !submit && !Console.IsInputRedirected;
            string endpoint = null;
            try
            {
                Console.WriteLine("输入链接：");
                string labLink = suppliedLink ?? Console.ReadLine();
                LaunchArguments launch = LaunchArguments.Parse(labLink);
                Console.WriteLine("启动格式：" + (launch.IsJson ? "JSON" : "旧版位置参数") + "；流程：" + (launch.UseNewHall ? "新版" : "旧版"));
                endpoint = ExperimentQuery.Endpoint(launch).AbsoluteUri;
                Console.WriteLine("目标服务：" + endpoint);
                if (checkOnly)
                {
                    Console.WriteLine("启动参数解析通过；未连接服务。");
                    return 0;
                }
                FakeApp fakeApp = new FakeApp(labLink, metadataOnly);
                if (!fakeApp.IsInitialized)
                {
                    Console.WriteLine("实验初始化未完成，已停止后续操作。");
                    return Finish(2, holdOnError, fakeApp.InitializationError, endpoint);
                }
                if (metadataOnly) return 0;
                if (submit)
                {
                    if (!fakeApp.Launch.IsJson)
                    {
                        Console.WriteLine("--submit 只支持 JSON 启动链接（2023 版站点）。");
                        return 1;
                    }
                    if (fakeApp.fakeHall2 == null) throw new InvalidOperationException("新版实验对象未初始化。");
                    NewSiteFlow flow = new NewSiteFlow(fakeApp.Launch, fakeApp.fakeHall2.lab,
                        AppDomain.CurrentDomain.BaseDirectory, true, waitSeconds);
                    return flow.Run();
                }
                if (fakeApp.isNewVersion)
                {
                    if (fakeApp.fakeHall2 == null) throw new InvalidOperationException("新版实验对象未初始化。");
                    if (fakeApp.Launch.IsJson)
                    {
                        NewSiteFlow flow = new NewSiteFlow(fakeApp.Launch, fakeApp.fakeHall2.lab, AppDomain.CurrentDomain.BaseDirectory);
                        return flow.Run();
                    }
                    fakeApp.fakeHall2.UserControlModel();
                }
                else
                {
                    if (fakeApp.fakeHall == null) throw new InvalidOperationException("旧版实验对象未初始化。");
                    fakeApp.fakeHall.UserControlModel();
                }
                while (!shouldAlert) Thread.Sleep(500);
                if (!Console.IsInputRedirected)
                {
                    Console.WriteLine("按任意键退出……");
                    Console.ReadKey();
                }
                return 0;            }
            catch (FormatException ex)
            {
                Console.WriteLine("启动参数错误：" + ex.Message);
                return Finish(1, holdOnError, ex.Message, endpoint);
            }
            catch (Exception ex)
            {
                Console.WriteLine("启动失败：" + ex.Message);
                return Finish(2, holdOnError, ex.Message, endpoint);
            }
        }

        private static int Finish(int code, bool waitForKey, string detail, string endpoint)
        {
            string logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup-error.log");
            try
            {
                string record = "[" + DateTime.Now.ToString("s") + "] ExitCode=" + code + Environment.NewLine
                    + "Endpoint=" + (endpoint ?? "(not parsed)") + Environment.NewLine
                    + (detail ?? "Initialization stopped.") + Environment.NewLine + Environment.NewLine;
                System.IO.File.AppendAllText(logPath, record, new System.Text.UTF8Encoding(false));
                Console.WriteLine("错误详情已保存：" + logPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine("日志写入失败：" + ex.Message);
            }
            if (waitForKey)
            {
                Console.WriteLine("程序已停止后续操作，按任意键关闭窗口……");
                try { Console.ReadKey(true); }
                catch (InvalidOperationException) { }
            }
            return code;
        }

    }
}
