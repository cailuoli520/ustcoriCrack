using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Xml;

namespace ustcori_crack
{
    /// <summary>
    /// 满分改写：把实验数据里的 RealResult 全部求解成“落在最高分档”的取值。
    ///
    /// 与官方评分器（PaperReviewer）的关系：
    ///   · 求解依据是 ExamConfig 的 JudgeRule，不是凭经验填数；
    ///   · 求解后立即用 ScoreJudge（官方 GainScore 的等价实现）本地复算，
    ///     两者一致才算通过，否则把该点标记为不可得满分并给出原因。
    /// </summary>
    public static class FullScoreSolver
    {
        private const double Eps = 1e-7;

        public enum Mode
        {
            /// <summary>把 RealResult 改写成满分取值（默认，用于真实验提交）。</summary>
            Solve,
            /// <summary>完全不改动 RealResult，只用标准答案复算，输出“不改能得多少分”。</summary>
            Audit
        }

        public static FullScoreReport Apply(XmlDocument document, Mode mode)
        {
            var report = new FullScoreReport { Mode = mode };
            if (document == null || document.DocumentElement == null)
            {
                report.Warnings.Add("实验数据为空，未做任何求解。");
                return report;
            }

            XmlNode content = document.SelectSingleNode("ExamPaper/Content");
            if (content == null)
            {
                report.Warnings.Add("实验数据缺少 ExamPaper/Content 节点。");
                return report;
            }

            foreach (XmlNode question in content.SelectNodes("Question"))
            {
                string questionName = ScoreJudge.Text(question, "Name");
                double questionTotal = ScoreJudge.Parse(ScoreJudge.Text(question.SelectSingleNode("Score"), "Total"), 0);
                report.PaperTotal += questionTotal;

                double questionGain = 0;
                XmlNode checkPoint = question.SelectSingleNode("CheckPoint");
                if (checkPoint == null)
                {
                    report.Warnings.Add("题目《" + questionName + "》缺少 CheckPoint。");
                    continue;
                }

                foreach (XmlNode testTarget in checkPoint.SelectNodes("TestTarget"))
                {
                    string targetName = ScoreJudge.Text(testTarget, "Script");
                    XmlNode group = testTarget.SelectSingleNode("Group");
                    if (group == null) continue;

                    double targetGain = 0;
                    foreach (XmlNode para in group.SelectNodes("Para"))
                    {
                        targetGain += SolvePara(questionName, targetName, para, report);
                    }
                    questionGain += targetGain;

                    XmlNode targetReal = testTarget.SelectSingleNode("RealScore");
                    string text = Round(targetGain);
                    if (targetReal != null) targetReal.InnerText = text;
                    else testTarget.AppendChild(CreateNode(testTarget, "RealScore", text));
                }

                if (questionGain > questionTotal + Eps) questionGain = questionTotal;
                if (questionGain < 0) questionGain = 0;
                report.Achievable += questionGain;

                XmlNode scoreNode = question.SelectSingleNode("Score");
                XmlNode realNode = scoreNode == null ? null : scoreNode.SelectSingleNode("RealScore");
                if (realNode != null) realNode.InnerText = Round(questionGain);
                else if (scoreNode != null) scoreNode.AppendChild(CreateNode(scoreNode, "RealScore", Round(questionGain)));

                if (questionGain + Eps < questionTotal)
                {
                    report.Warnings.Add(string.Format(CultureInfo.InvariantCulture,
                        "题目《{0}》卷面 {1} 分，实际可拿 {2} 分，差 {3} 分：这些评分点在卷面里没有满分解（无 JudgeRule、区间全为 0 分，或标准答案缺失）。",
                        questionName, Round(questionTotal), Round(questionGain), Round(questionTotal - questionGain)));
                }
            }

            report.Summarize();
            return report;
        }

        // ------------------------------------------------------------------ Para

        private static double SolvePara(string question, string target, XmlNode para, FullScoreReport report)
        {
            string name = ScoreJudge.Text(para, "Name");
            string varType = (ScoreJudge.Text(para, "VarType") ?? string.Empty).Trim();
            string totalText = ScoreJudge.Text(para, "TotalScore");

            // ANSWER 型不参与判分
            if (varType.Equals("Answer", StringComparison.OrdinalIgnoreCase)) return 0;

            if (varType.Equals("TList", StringComparison.OrdinalIgnoreCase))
                return SolveTList(question, target, para, report);

            XmlNode judge = para.SelectSingleNode("JudgeRule");
            XmlNode paraAdd = para.SelectSingleNode("ParaAdd");
            string stdText = ScoreJudge.Text(para, "StdResult");

            string answer;
            string note;
            if (report.Mode == Mode.Solve) answer = SolveValue(para, varType, judge, out note);
            else { answer = ScoreJudge.Text(para, "RealResult"); note = "审计：保留原始作答"; }

            double gain = 0;
            if (judge != null)
            {
                JudgeOutcome outcome = ScoreJudge.Evaluate(varType, answer, stdText, judge, totalText);
                gain = outcome.IsLoss ? 0 : ScoreJudge.ApplyParaAdd(totalText, paraAdd, outcome.Score);
                if (outcome.IsLoss) note = (note == null ? string.Empty : note + "；") + "触发 " + outcome.Text + "（该题按 0 分计）";
            }
            else if (answer != null)
            {
                // 无 JudgeRule：官方评审器直接 record("答案：…") 并给 0 分，这里如实反映
                note = (note == null ? string.Empty : note + "；") + "无 JudgeRule，该点官方判 0 分";
            }

            WriteResult(para, answer, gain);
            report.Items.Add(new ScoreItem
            {
                Question = question,
                TestTarget = target,
                ParaName = name,
                VarType = varType,
                Achievable = gain,
                TotalScore = ScoreJudge.Parse(totalText, 0),
                RealResult = answer,
                Note = note,
                Judgeable = judge != null
            });
            return gain;
        }

        /// <summary>
        /// 按 JudgeRule 反解一个标量评分点的满分取值。
        ///
        /// 不能只按解析式取区间中点：官方 GetMaxScore 用
        ///   outRule = source3.Where(score == maxScore).Single()
        /// 选规则，两条同分规则同时命中会抛异常，整点直接判 0。
        /// 所以这里枚举候选点（各档区间内的多个位置），用官方判据的等价实现实测打分，
        /// 取实测分最高、且离边界最远的那个点。
        /// </summary>
        private static string SolveValue(XmlNode para, string varType, XmlNode judge, out string note)
        {
            note = null;
            string stdText = ScoreJudge.Text(para, "StdResult");
            string totalText = ScoreJudge.Text(para, "TotalScore");

            if (judge == null) return stdText;

            string type = varType.Trim().ToLowerInvariant();
            if (type == "bool" || type == "string")
            {
                note = "布尔/字符串：取标准答案";
                return stdText;
            }
            if (type != "int" && type != "double")
            {
                note = "未识别的 VarType：" + varType;
                return stdText;
            }

            double std;
            if (!ScoreJudge.TryParse(stdText, out std))
            {
                note = "标准答案不是数值，无法反解";
                return stdText;
            }

            XmlNodeList rules = judge.SelectNodes("Rule");
            if (rules == null || rules.Count == 0)
            {
                note = "JudgeRule 内无 Rule";
                return stdText;
            }

            // 1) 逐条规则算出它的“满分区间”，并取若干候选点
            var candidates = new List<double>();
            double runningStd = std;
            foreach (XmlNode rule in rules)
            {
                string scoreText = ScoreJudge.Text(rule, "Score");
                if (scoreText == "LostWholeQuestion" || scoreText == "LostWholeCheckPoint") continue;

                double k = ScoreJudge.Parse(ScoreJudge.Text(rule, "StdValue"), 1);
                if (Math.Abs(k) < 1e-12) k = 1e-11;

                XmlNode range = rule.SelectSingleNode("Range");
                double min = ScoreJudge.Parse(ScoreJudge.Text(range, "Min"), 0);
                double max = ScoreJudge.Parse(ScoreJudge.Text(range, "Max"), 0);
                string rangeType = (ScoreJudge.Text(range, "Type") ?? string.Empty).Trim();

                double center = k * runningStd;
                double low, high;
                if (rangeType == "绝对值")
                {
                    low = center + min;
                    high = center + max;
                }
                else
                {
                    low = center * (1.0 + min);
                    high = center * (1.0 + max);
                }
                if (low > high) { double t = low; low = high; high = t; }

                // 中点最容易撞上“同分规则重叠”，所以同时取区间内偏侧的点
                foreach (double frac in new[] { 0.5, 0.25, 0.75, 0.1, 0.9 })
                    candidates.Add(low + (high - low) * frac);

                // 绝对值区间常常退化成一个点（min=max=0），中心必须单独加进来
                candidates.Add(center);
                candidates.Add(std);
            }
            if (candidates.Count == 0)
            {
                note = "所有 Rule 都是熔断档，无满分解";
                return stdText;
            }

            // 2) 用官方判据实测打分，挑实测最优
            double bestScore = double.NegativeInfinity;
            double bestValue = std;
            bool found = false;
            foreach (double candidate in candidates)
            {
                string text = Round(candidate);
                JudgeOutcome outcome = ScoreJudge.Evaluate(varType, text, stdText, judge, totalText);
                if (outcome.IsLoss) continue;

                double measured = outcome.Score;
                // 同分时优先取更贴近标准答案的点，读数看起来最自然
                bool better = !found
                              || measured > bestScore + 1e-9
                              || (Math.Abs(measured - bestScore) <= 1e-9 && Math.Abs(candidate - std) < Math.Abs(bestValue - std));
                if (better)
                {
                    found = true;
                    bestScore = measured;
                    bestValue = candidate;
                }
            }

            if (!found)
            {
                note = "所有候选点都触发熔断，无满分解";
                return stdText;
            }

            note = string.Format(CultureInfo.InvariantCulture, "实测 {0} 分", Round(bestScore));
            return Round(bestValue);
        }

        // ------------------------------------------------------------------ TList

        private static double SolveTList(string question, string target, XmlNode para, FullScoreReport report)
        {
            string name = ScoreJudge.Text(para, "Name");
            XmlNode stdNode = para.SelectSingleNode("StdResult");
            XmlNode realNode = para.SelectSingleNode("RealResult");
            string stdText = stdNode == null ? string.Empty : stdNode.InnerText;
            string realText = realNode == null ? string.Empty : realNode.InnerText;
            string totalText = ScoreJudge.Text(para, "TotalScore");

            List<List<string>> stdRows = SplitTable(stdText);
            List<List<string>> realRows = SplitTable(realText);
            XmlNodeList columns = para.SelectNodes("Para");

            // 原程序会随机化 StdResult 去“蒙”任意值表格，那是错的：标准答案一旦被改写，
            // 服务端拿什么当判据就不可控了。这里只清掉它留下的标记，不动 StdResult。
            XmlNode mark = para.SelectSingleNode("Randomized");
            if (mark != null) para.RemoveChild(mark);

            double gain = 0;
            int solved = 0;
            var rebuilt = new List<string>();

            for (int r = 0; r < stdRows.Count; r++)
            {
                var cells = new List<string>(stdRows[r]);
                List<string> existing = r < realRows.Count ? realRows[r] : null;
                XmlNode column = columns != null && r < columns.Count ? columns[r] : null;

                if (column != null)
                {
                    string columnType = (ScoreJudge.Text(column, "VarType") ?? string.Empty).Trim();
                    XmlNode judge = column.SelectSingleNode("JudgeRule");
                    bool isIndex = (ScoreJudge.Text(column, "Name") ?? string.Empty)
                        .IndexOf("INDEX", StringComparison.OrdinalIgnoreCase) >= 0;
                    double columnTotal = ScoreJudge.Parse(ScoreJudge.Text(column, "TotalScore"), 0);

                    for (int c = 0; c < cells.Count; c++)
                    {
                        // 不计分的列（INDEX、以及 TotalScore=0 且无判据的列）保留学生原始读数，
                        // 既不影响得分，也避免提交一张被清空的表格。
                        bool scorable = !isIndex && judge != null && columnTotal > 0;
                        string original = existing != null && c < existing.Count ? existing[c] : null;

                        if (!scorable)
                        {
                            if (!string.IsNullOrEmpty(original)) cells[c] = original;
                            continue;
                        }

                        string cellStd = cells[c];
                        string answer;
                        if (report.Mode == Mode.Solve)
                        {
                            XmlNode synthetic = column.CloneNode(true);
                            XmlNode stdCell = synthetic.SelectSingleNode("StdResult");
                            if (stdCell == null) stdCell = synthetic.AppendChild(synthetic.OwnerDocument.CreateElement("StdResult"));
                            stdCell.InnerText = cellStd;

                            string unused;
                            answer = SolveValue(synthetic, columnType, judge, out unused);
                        }
                        else
                        {
                            answer = original ?? cellStd;
                        }
                        cells[c] = answer;

                        JudgeOutcome outcome = ScoreJudge.Evaluate(columnType, answer, cellStd, judge, ScoreJudge.Text(column, "TotalScore"));
                        if (!outcome.IsLoss) gain += outcome.Score;
                        solved++;
                    }
                }
                rebuilt.Add("(" + string.Join(",", cells.ToArray()) + ")");
            }

            string output = solved > 0 ? string.Join(";", rebuilt.ToArray()) : realText;
            WriteResult(para, output, gain);

            report.Items.Add(new ScoreItem
            {
                Question = question,
                TestTarget = target,
                ParaName = name,
                VarType = "TList",
                Achievable = gain,
                TotalScore = ScoreJudge.Parse(totalText, 0),
                RealResult = output,
                Note = stdRows.Count == 0
                    ? "标准答案表格无法解析，原样保留"
                    : (solved == 0
                        ? "该表所有列 TotalScore=0 且无判据，本身不计分，仅保留原始读数"
                        : "逐格求解 " + solved + " 格，累计 " + Round(gain) + " 分"),
                Judgeable = true
            });
            return gain;
        }

        private static List<List<string>> SplitTable(string text)
        {
            var rows = new List<List<string>>();
            if (string.IsNullOrWhiteSpace(text)) return rows;
            foreach (string chunk in text.Split(';'))
            {
                int open = chunk.IndexOf('(');
                int close = chunk.LastIndexOf(')');
                if (open < 0 || close <= open) continue;
                rows.Add(new List<string>(chunk.Substring(open + 1, close - open - 1).Split(',')));
            }
            return rows;
        }

        // ------------------------------------------------------------------ 写回

        private static void WriteResult(XmlNode para, string realResult, double gain)
        {
            XmlNode real = para.SelectSingleNode("RealResult");
            if (real != null) real.InnerText = realResult ?? string.Empty;
            else para.AppendChild(CreateNode(para, "RealResult", realResult ?? string.Empty));

            XmlNode score = para.SelectSingleNode("RealScore");
            string text = Round(gain);
            if (score != null) score.InnerText = text;
            else para.AppendChild(CreateNode(para, "RealScore", text));
        }

        private static XmlNode CreateNode(XmlNode parent, string name, string text)
        {
            XmlNode node = parent.OwnerDocument.CreateElement(name);
            node.InnerText = text;
            return node;
        }

        internal static string Round(double value)
        {
            return Math.Round(value, 6).ToString("0.######", CultureInfo.InvariantCulture);
        }
    }
}
