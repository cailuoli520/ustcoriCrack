using System;
using System.IO;
using System.Text;

namespace ustcori_crack
{
    /// <summary>
    /// 探测 LitJson 的真实编码格式。
    ///
    /// 提交链路里 Content 和 Key 都由 USTCORi.SimExp.Instruments.Component 内的
    /// LitJson.JsonWriter / LitJson.Sec 生成，文档里没有格式说明。要验证提交是否
    /// 正确，必须先把这两个函数的确切输出形状测出来。
    /// </summary>
    internal static class PayloadProbe
    {
        public static int Run()
        {
            byte[] sample = Encoding.UTF8.GetBytes("<ExamPaper><Content/></ExamPaper>");
            Console.WriteLine("明文：" + Encoding.UTF8.GetString(sample) + "  (" + sample.Length + " 字节)");

            byte[] encoded = LitJson.JsonWriter.Write(sample, "haohaoxuexi,tiantianxiangshang.");
            Console.WriteLine();
            Console.WriteLine("Content 长度：" + encoded.Length);
            string asText = Encoding.UTF8.GetString(encoded);
            Console.WriteLine("Content 前 200 字符：");
            Console.WriteLine(asText.Length > 200 ? asText.Substring(0, 200) : asText);
            Console.WriteLine("是否可打印 ASCII：" + IsPrintable(encoded));

            // 往返验证：能解回原文才算编码正确
            try
            {
                byte[] back = LitJson.JsonReader.Read(encoded, "haohaoxuexi,tiantianxiangshang.");
                Console.WriteLine("往返一致：" + (Encoding.UTF8.GetString(back) == Encoding.UTF8.GetString(sample)));
            }
            catch (Exception ex)
            {
                Console.WriteLine("往返失败：" + ex.Message);
            }

            // 错误密钥必须解不出来，否则说明根本没加密
            try
            {
                byte[] wrong = LitJson.JsonReader.Read(encoded, "wrong-key-value-here");
                Console.WriteLine("错误密钥仍能解开：" + (Encoding.UTF8.GetString(wrong) == Encoding.UTF8.GetString(sample)) + "（应为 False）");
            }
            catch (Exception ex)
            {
                Console.WriteLine("错误密钥被拒绝：" + ex.GetType().Name);
            }

            Console.WriteLine();
            string sampleKeyInput = "90000001,2,90000001_20260101000000.xml";
            string key = LitJson.Sec.GenerateKey(sampleKeyInput);
            Console.WriteLine("Key 长度：" + key.Length);
            Console.WriteLine("Key 内容：" + key);
            Console.WriteLine("Key 是否十六进制：" + IsHex(key));

            string key2 = LitJson.Sec.GenerateKey(sampleKeyInput);
            Console.WriteLine("Key 是否确定性（同输入同输出）：" + (key == key2));
            return 0;
        }

        private static bool IsPrintable(byte[] data)
        {
            foreach (byte b in data)
                if (b < 0x20 || b > 0x7e) return false;
            return true;
        }

        private static bool IsHex(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s)
                if (!Uri.IsHexDigit(c)) return false;
            return true;
        }
    }
}
