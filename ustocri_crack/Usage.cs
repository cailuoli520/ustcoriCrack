using System;
using System.Reflection;

namespace ustcori_crack
{
    /// <summary>
    /// 命令行用法说明。
    ///
    /// 程序有三种完全不同的运行方式，互相之间不能混用参数，所以把边界讲清楚：
    ///   1. 带链接的正常运行（会连服务器）
    ///   2. 带链接的只读/验收模式（不提交任何数据）
    ///   3. 不带链接的离线自检（不需要服务器）
    /// </summary>
    internal static class Usage
    {
        public static int Show()
        {
            string version = Assembly.GetExecutingAssembly().GetName().Version.ToString();

            Console.WriteLine("ustcori_crack " + version + "  —  实验评分分析与满分求解");
            Console.WriteLine();
            Console.WriteLine("用法：ustcori_crack.exe [模式] [选项] [启动链接]");
            Console.WriteLine();
            Console.WriteLine("链接来源");
            Console.WriteLine("  浏览器打开实验时地址栏里的 labori://... 整串复制过来即可；");
            Console.WriteLine("  不带链接运行时会提示你粘贴。也可以自己生成：");
            Console.WriteLine("    python build/make-link.py --site http://HOST:PORT --student 学号");
            Console.WriteLine();
            Console.WriteLine("模式（默认：正常运行，会连接服务器）");
            Console.WriteLine("  （不给模式）        正常流程：排队 → 下载实验 → 启动仿真 → 交卷");
            Console.WriteLine("  --check-launch      只解析链接，打印目标地址，不发出任何请求");
            Console.WriteLine("  --metadata-only     只查实验名称，不创建排队记录、不下载、不提交");
            Console.WriteLine("  --self-test         离线验收：解包 → 审计 → 求解 → 独立复算（不联网）");
            Console.WriteLine("  --audit             离线审计：读当前已解包的实验，报告能拿多少分（不联网）");
            Console.WriteLine("  --cases             求解器回归用例（13 个边界情形，不联网）");
            Console.WriteLine("  --probe-payload     打印 LitJson 编码格式与 Key 生成规律（不联网）");
            Console.WriteLine("  --verify-fixture    校验仿真服务端捕获的提交数据");
            Console.WriteLine();
            Console.WriteLine("选项");
            Console.WriteLine("  --wait N            排队等待秒数，0 表示不等待直接进");
            Console.WriteLine();
            Console.WriteLine("示例");
            Console.WriteLine("  查目标地址（最安全，零请求）");
            Console.WriteLine("    ustcori_crack.exe --check-launch \"labori://...\"");
            Console.WriteLine();
            Console.WriteLine("  确认实验能查到（只读查询，不留记录）");
            Console.WriteLine("    ustcori_crack.exe --metadata-only \"labori://...\"");
            Console.WriteLine();
            Console.WriteLine("  离线验证评分逻辑能拿满分（不联网、不提交）");
            Console.WriteLine("    ustcori_crack.exe --self-test \"labori://...\"");
            Console.WriteLine();
            Console.WriteLine("  自查当前作答能拿多少分");
            Console.WriteLine("    ustcori_crack.exe --audit");
            Console.WriteLine();
            Console.WriteLine("  跑回归用例");
            Console.WriteLine("    ustcori_crack.exe --cases");
            Console.WriteLine();
            Console.WriteLine("  校验提交链路（配合 build/fixture/server.py）");
            Console.WriteLine("    ustcori_crack.exe --verify-fixture build\\fixture\\captured.json solved-exam.xml");
            Console.WriteLine();
            Console.WriteLine("退出码");
            Console.WriteLine("  0  成功          1  链接/参数错误          2  初始化或网络失败");
            Console.WriteLine("  3  验收未通过（求解结果与官方判据不一致，或卷面分拿不满）");
            Console.WriteLine();
            Console.WriteLine("文件位置");
            Console.WriteLine("  求解结果    solved-exam.xml（与 exe 同目录）");
            Console.WriteLine("  错误日志    startup-error.log（与 exe 同目录，只追加）");
            Console.WriteLine("  解包目录    %TEMP%\\lab，不可写时自动退到 exe\\labwork 或 %LOCALAPPDATA%\\ustcori-lab");
            Console.WriteLine();
            Console.WriteLine("常用组合：先 --check-launch 确认地址，再 --metadata-only 确认实验，最后正常跑。");
            return 0;
        }
    }
}
