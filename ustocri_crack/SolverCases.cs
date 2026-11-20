using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Xml;

namespace ustcori_crack
{
    /// <summary>
    /// 求解器自检用例。
    ///
    /// 每个用例构造一段最小的 ExamConfig 片段（结构、字段名、判定语义都与真实试卷一致），
    /// 然后走完整流程：求解 → 用官方 GainScore 的等价实现复算 → 断言拿到预期分数。
    /// 目的是证明求解器在 StdValue≠1、负标准答案、绝对值区间、宽窄双档、熔断档、
    /// 缺 JudgeRule、ParaAdd 折算、零分点等边界上都不会算错，而不是只会解光电效应那一份。
    /// </summary>
    public static class SolverCases
    {
        private sealed class Case
        {
            public string Name;
            public string Xml;
            public double Expect;       // 期望得分
            public double ExpectTotal;  // 该点卷面分
            public string ExpectHint;   // 期望取值（可空，只做提示）
        }

        public static int Run()
        {
            var cases = new List<Case>
            {
                new Case
                {
                    Name = "百分比双档，k=1，正标准答案（真实试卷里的遏止电压场景）",
                    Expect = 5, ExpectTotal = 5, ExpectHint = "-0.2",
                    Xml = Para("double", "-0.2", "5",
                        Rule("1", "百分比", "-0.1", "0.1", "5") + Rule("1", "百分比", "-0.2", "0.2", "2.5"))
                },
                new Case
                {
                    Name = "StdValue=2，必须把答案写到 2·s 附近才满分",
                    Expect = 10, ExpectTotal = 10, ExpectHint = "13.26",
                    Xml = Para("double", "6.63", "10",
                        Rule("2", "百分比", "-0.1", "0.1", "10") + Rule("2", "百分比", "-0.2", "0.2", "5"))
                },
                new Case
                {
                    Name = "绝对值区间 min=max=0，只接受与 k·s 完全相同的值",
                    Expect = 1, ExpectTotal = 1, ExpectHint = "577.0",
                    Xml = Para("double", "577.0", "1", Rule("1", "绝对值", "0", "0", "1"))
                },
                new Case
                {
                    Name = "负标准答案 + 绝对值区间 [-0.05,0.05]",
                    Expect = 7, ExpectTotal = 7, ExpectHint = "-1.18",
                    Xml = Para("double", "-1.18", "7", Rule("1", "绝对值", "-0.05", "0.05", "7"))
                },
                new Case
                {
                    // 重叠区会触发官方 Single() 异常判 0，求解器要挑宽档的“独占区”绕过去
                    Name = "宽窄双档同分：必须避开重叠区，取宽档独占区仍拿满分",
                    Expect = 4, ExpectTotal = 4, ExpectHint = "0.75",
                    Xml = Para("double", "1", "4",
                        Rule("1", "百分比", "-0.5", "0.5", "4") + Rule("1", "百分比", "-0.01", "0.01", "4"))
                },
                new Case
                {
                    Name = "两条不同分规则重叠时，取高分档而不触发 Single()",
                    Expect = 5, ExpectTotal = 5, ExpectHint = "1",
                    Xml = Para("double", "1", "5",
                        Rule("1", "百分比", "-0.1", "0.1", "5") + Rule("1", "百分比", "-0.2", "0.2", "2.5"))
                },
                new Case
                {
                    Name = "含熔断档，必须避开",
                    Expect = 3, ExpectTotal = 3, ExpectHint = "1",
                    Xml = Para("double", "1", "3",
                        Rule("1", "百分比", "-0.3", "0.3", "3") + Rule("1", "百分比", "-0.95", "0.95", "LostWholeQuestion"))
                },
                new Case
                {
                    Name = "bool：取标准答案即满分",
                    Expect = 3, ExpectTotal = 3, ExpectHint = "true",
                    Xml = BoolPara("true", "3", "0")
                },
                new Case
                {
                    Name = "缺 JudgeRule：官方判 0，求解器不得虚报",
                    Expect = 0, ExpectTotal = 2, ExpectHint = "42",
                    Xml = Para("double", "42", "2", string.Empty)
                },
                new Case
                {
                    Name = "TotalScore=0 的评分点：官方只给 1e-5",
                    Expect = Math.Pow(10.0, -5.0), ExpectTotal = 0, ExpectHint = "1",
                    Xml = Para("double", "1", "0", Rule("1", "百分比", "-0.1", "0.1", "5"))
                },
                new Case
                {
                    Name = "百分比区间全为 0 分档：无满分解",
                    Expect = 0, ExpectTotal = 5, ExpectHint = "1",
                    Xml = Para("double", "1", "5", Rule("1", "百分比", "-0.1", "0.1", "0"))
                },
                new Case
                {
                    Name = "标准答案为空，但 JudgeRule 可反解（百分比型围绕 0 退化）",
                    Expect = 0, ExpectTotal = 5, ExpectHint = null,
                    Xml = Para("double", "", "5", Rule("1", "百分比", "-0.1", "0.1", "5"))
                },
                new Case
                {
                    Name = "int 型评分点",
                    Expect = 6, ExpectTotal = 6, ExpectHint = "10",
                    Xml = Para("int", "10", "6", Rule("1", "绝对值", "-0.001", "0.001", "6"))
                }
            };

            int failed = 0;
            Console.WriteLine("=== 求解器回归用例 ===");
            foreach (Case c in cases)
            {
                XmlDocument doc = BuildDocument(c.Xml);
                FullScoreReport report = FullScoreSolver.Apply(doc, FullScoreSolver.Mode.Solve);

                // 用官方判据等价实现独立复算，不看求解器自己报的分
                double replay = new PaperReviewer().ReviewOpXml((XmlDocument)doc.CloneNode(true));

                bool ok = Math.Abs(replay - c.Expect) < 1e-4 && Math.Abs(report.Achievable - c.Expect) < 1e-4;
                if (!ok) failed++;

                string got = string.Empty;
                foreach (ScoreItem item in report.Items) got = item.RealResult;

                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  [{0}] {1}  期望 {2} / 复算 {3} / 求解 {4}{5}",
                    ok ? "通过" : "失败", c.Name,
                    Round(c.Expect), Round(replay), Round(report.Achievable),
                    c.ExpectHint == null ? string.Empty : "  取值=" + got));
            }

            Console.WriteLine(failed == 0
                ? "全部用例通过。"
                : "有 " + failed + " 个用例未通过。");
            return failed == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------ 构造

        private static XmlDocument BuildDocument(string paraXml)
        {
            string xml =
                "<ExamPaper><Content><Question Type=\"OP\">" +
                "<Name>用例题</Name><Score><Total>100</Total><RealScore>0</RealScore></Score>" +
                "<CheckPoint><TestTarget><Script>用例目标</Script><Group>" + paraXml + "</Group></TestTarget></CheckPoint>" +
                "</Question></Content></ExamPaper>";
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            return doc;
        }

        private static string Para(string varType, string std, string total, string judgeRules)
        {
            return "<Para><Name>P1</Name><Script>用例评分点</Script><VarType>" + varType + "</VarType>" +
                   "<RealResult></RealResult><StdResult>" + std + "</StdResult>" +
                   "<TotalScore>" + total + "</TotalScore><RealScore>0</RealScore>" +
                   (judgeRules.Length == 0 ? string.Empty : "<JudgeRule>" + judgeRules + "</JudgeRule>") +
                   "</Para>";
        }

        private static string BoolPara(string std, string right, string wrong)
        {
            return "<Para><Name>P1</Name><Script>用例评分点</Script><VarType>bool</VarType>" +
                   "<RealResult></RealResult><StdResult>" + std + "</StdResult>" +
                   "<TotalScore>" + right + "</TotalScore><RealScore>0</RealScore>" +
                   "<JudgeRule><RightRule><Score>" + right + "</Score><RuleShowInfo>对</RuleShowInfo></RightRule>" +
                   "<WrongRule><Score>" + wrong + "</Score><RuleShowInfo>错</RuleShowInfo></WrongRule></JudgeRule>" +
                   "</Para>";
        }

        private static string Rule(string k, string type, string min, string max, string score)
        {
            return "<Rule><StdValue>" + k + "</StdValue>" +
                   "<Range><Type>" + type + "</Type><Min>" + min + "</Min><Max>" + max + "</Max></Range>" +
                   "<Score>" + score + "</Score><RuleShowInfo>样例</RuleShowInfo></Rule>";
        }

        private static string Round(double v)
        {
            return Math.Round(v, 6).ToString("0.######", CultureInfo.InvariantCulture);
        }
    }
}
