using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ustcori_crack
{
    /// <summary>单个评分点的求解与复算结果。</summary>
    public sealed class ScoreItem
    {
        public string Question;
        public string TestTarget;
        public string ParaName;
        public string VarType;
        public double Achievable;      // 本点可得分（按官方判据复算，不是估计）
        public double TotalScore;      // 该 Para 的 TotalScore
        public string RealResult;
        public string Note;
        public bool Judgeable = true;  // 是否存在可用判据
    }

    /// <summary>整份试卷的满分求解报告。</summary>
    public sealed class FullScoreReport
    {
        public FullScoreSolver.Mode Mode = FullScoreSolver.Mode.Solve;
        public double PaperTotal;
        public double Achievable;
        public readonly List<ScoreItem> Items = new List<ScoreItem>();
        public readonly List<string> Warnings = new List<string>();
        public int Unjudgeable;

        public double Gap { get { return Math.Round(PaperTotal - Achievable, 6); } }
        public bool IsFullScore { get { return Gap <= 1e-6 && Unjudgeable == 0; } }

        /// <summary>统计无判据评分点数量（不能用 Finalize 这个名字，会与 object.Finalize 冲突）。</summary>
        public void Summarize()
        {
            Unjudgeable = 0;
            foreach (ScoreItem item in Items)
                if (!item.Judgeable) Unjudgeable++;
        }

        public string Format()
        {
            var sb = new StringBuilder();
            sb.AppendLine("卷面总分：" + Num(PaperTotal));
            sb.AppendLine("求解后可得：" + Num(Achievable));
            sb.AppendLine("差距：" + Num(Gap));

            string question = null;
            foreach (ScoreItem item in Items)
            {
                if (!string.Equals(question, item.Question, StringComparison.Ordinal))
                {
                    question = item.Question;
                    sb.AppendLine();
                    sb.AppendLine("题：" + question);
                }
                sb.AppendFormat(CultureInfo.InvariantCulture, "  {0,-26} {1,-8} {2,5} / {3,-5}  →  {4}",
                    Trim(item.ParaName, 26), item.VarType, Num(item.Achievable), Num(item.TotalScore), Trim(item.RealResult, 64));
                if (!string.IsNullOrEmpty(item.Note)) sb.Append("   [" + item.Note + "]");
                sb.AppendLine();
            }

            if (Warnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("说明：");
                foreach (string w in Warnings) sb.AppendLine("  - " + w);
            }
            return sb.ToString();
        }

        private static string Num(double v)
        {
            return Math.Round(v, 6).ToString("0.######", CultureInfo.InvariantCulture);
        }

        private static string Trim(string v, int width)
        {
            if (string.IsNullOrEmpty(v)) return string.Empty;
            return v.Length <= width ? v : v.Substring(0, width - 1) + "…";
        }
    }
}
