using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Xml;

namespace ustcori_crack
{
    /// <summary>一次判定的结果，含 LostWhole* 两种熔断。</summary>
    public sealed class JudgeOutcome
    {
        public double Score;
        public bool LostWholeQuestion;
        public bool LostWholeCheckPoint;

        public bool IsLoss { get { return LostWholeQuestion || LostWholeCheckPoint; } }
        public string Text
        {
            get
            {
                if (LostWholeQuestion) return "LostWholeQuestion";
                if (LostWholeCheckPoint) return "LostWholeCheckPoint";
                return Math.Round(Score, 6).ToString("0.######", CultureInfo.InvariantCulture);
            }
        }
    }

    /// <summary>
    /// 官方判分器的等价实现。
    ///
    /// 逻辑逐行对应 PaperReviewer.GainScore（由 WebLabClient.exe 反编译得到），
    /// 用于在不联网的前提下验证“我算出的答案是否真的落在最高分档”。求解器只依赖它，
    /// 所以只要等价实现正确，得到的分数就是服务端会给出的分数。
    /// </summary>
    public static class ScoreJudge
    {
        public const double Eps = 1e-7;
        private const double ZeroStd = 1e-11;

        public static JudgeOutcome Evaluate(string varType, string student, string std, XmlNode judge, string paraTotalScore)
        {
            var result = new JudgeOutcome();
            if (judge == null) return result;      // 无 JudgeRule：判 0，且不改写任何东西

            string type = (varType ?? string.Empty).Trim().ToLowerInvariant();

            if (type == "bool" || type == "string")
            {
                XmlNode right = judge.SelectSingleNode("RightRule");
                XmlNode wrong = judge.SelectSingleNode("WrongRule");
                bool equal = string.Equals((student ?? string.Empty).Trim().ToLowerInvariant(),
                                           (std ?? string.Empty).Trim().ToLowerInvariant(),
                                           StringComparison.Ordinal);
                XmlNode hit = equal ? right : wrong;
                if (hit != null) result.Score = Parse(Text(hit, "Score"), 0);
                if (!equal && hit != null && Text(hit, "Score") == "LostWholeQuestion") result.LostWholeQuestion = true;
                if (!equal && hit != null && Text(hit, "Score") == "LostWholeCheckPoint") result.LostWholeCheckPoint = true;
                return result;
            }

            if (type != "int" && type != "double") return result;

            if (string.IsNullOrEmpty(std)) return result;

            double x;
            if (string.IsNullOrEmpty(student)) x = 1145141919810.0;     // 与官方一致的“空答案”哨兵值
            else if (!TryParse(student, out x)) return result;

            double s = Parse(std, double.NaN);
            if (double.IsNaN(s)) return result;

            XmlNodeList rules = judge.SelectNodes("Rule");
            if (rules == null) return result;

            double stdEff = s;                 // 官方实现会在循环里就地改写 num2，这里保持一致
            var matched = new List<KeyValuePair<XmlNode, double>>();

            foreach (XmlNode rule in rules)
            {
                double k = Parse(Text(rule, "StdValue"), 1);
                if (k == 0.0) k = ZeroStd;

                XmlNode range = rule.SelectSingleNode("Range");
                double min = Parse(Text(range, "Min"), 0);
                double max = Parse(Text(range, "Max"), 0);
                string rangeType = (Text(range, "Type") ?? string.Empty).Trim();
                bool flag = false;

                if (rangeType == "绝对值")
                {
                    double delta = x - k * stdEff;
                    if (delta >= min - Eps && delta <= max + Eps) matched.Add(new KeyValuePair<XmlNode, double>(rule, k));
                }
                else
                {
                    if (Math.Abs(stdEff) <= Eps) { stdEff = ZeroStd; flag = true; }
                    double ratio = (x / stdEff - k) / k;
                    if (ratio >= min - Eps && ratio <= max + Eps) matched.Add(new KeyValuePair<XmlNode, double>(rule, k));
                    else if (flag && Math.Abs(x) <= Eps) matched.Add(new KeyValuePair<XmlNode, double>(rule, k));
                }
            }

            // 与 GetMaxScore 一致：数值分优先，其次 LostWholeCheckPoint，最后 LostWholeQuestion
            double bestNumeric = double.NegativeInfinity;
            int bestCount = 0;
            foreach (var pair in matched)
            {
                string scoreText = Text(pair.Key, "Score");
                if (scoreText == "LostWholeQuestion" || scoreText == "LostWholeCheckPoint") continue;
                double score = Parse(scoreText, 0);
                if (score > bestNumeric + Eps) { bestNumeric = score; bestCount = 1; }
                else if (Math.Abs(score - bestNumeric) <= Eps) bestCount++;
            }
            if (bestCount > 0)
            {
                // 官方实现是 source3.Where(score == maxScore).Single()：
                // 两条同分规则同时命中会抛异常，被 GainScore 外层 catch 吞掉并返回 "0"。
                // 这不是推测——是 PaperReviewer.GainScore 的实际控制流，必须照样复刻。
                if (bestCount > 1) return result;
                result.Score = bestNumeric;
                if (paraTotalScore == "0") result.Score = Math.Pow(10.0, -5.0);
                return result;
            }

            foreach (var pair in matched)
            {
                if (Text(pair.Key, "Score") == "LostWholeCheckPoint") { result.LostWholeCheckPoint = true; return result; }
            }
            foreach (var pair in matched)
            {
                if (Text(pair.Key, "Score") == "LostWholeQuestion") { result.LostWholeQuestion = true; return result; }
            }
            return result;
        }

        /// <summary>按 ParaAdd 折算（对应 GetPerPara 尾部逻辑），返回最终计入的分。</summary>
        public static double ApplyParaAdd(string totalScore, XmlNode paraAdd, double gain)
        {
            if (paraAdd == null) return gain;
            XmlNode change = paraAdd.SelectSingleNode("ParaChangeInnerText");
            string type = (Text(paraAdd, "VarType") ?? string.Empty).Trim().ToUpperInvariant();
            if (change == null || type != "DOUBLE") return gain;

            string factorText = change.InnerText;
            if (string.IsNullOrEmpty(factorText)) factorText = "0";
            double factor = Parse(factorText, 0);
            double total = Parse(totalScore, 0);
            double cap = total * factor;
            if (gain < 1E-08) return gain;
            if (gain > cap) gain = cap;
            return Math.Round(gain, 1);
        }

        internal static string Text(XmlNode node, string child)
        {
            if (node == null) return null;
            XmlNode found = node.SelectSingleNode(child);
            return found == null ? null : found.InnerText;
        }

        internal static double Parse(string text, double fallback)
        {
            double value;
            if (string.IsNullOrWhiteSpace(text)) return fallback;
            if (double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return value;
            if (double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out value)) return value;
            return fallback;
        }

        internal static bool TryParse(string text, out double value)
        {
            return double.TryParse((text ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                || double.TryParse((text ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }
    }
}
