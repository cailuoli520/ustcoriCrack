using System;
using System.Globalization;
using System.Text;
using Newtonsoft.Json.Linq;

namespace ustcori_crack
{
    public sealed class LaunchArguments
    {
        public string Protocol { get; private set; }
        public bool IsJson { get; private set; }
        public bool UseNewHall { get; private set; }
        public string LabId { get; private set; }
        public Uri SiteBaseUri { get; private set; }
        public string StudentId { get; private set; }
        public string UserType { get; private set; }
        public string ClientIp { get; private set; }
        public string ClientArea { get; private set; }
        public string Language { get; private set; }
        public string CourseId { get; private set; }
        public string SysId { get; private set; }
        public string Mode { get; private set; }
        public string WindowHints { get; private set; }

        public Uri ServiceAddress(string serviceName)
        {
            return new Uri(SiteBaseUri, serviceName);
        }

        public static LaunchArguments Parse(string link)
        {
            if (string.IsNullOrWhiteSpace(link))
                throw new FormatException("启动链接为空，请复制具体实验的开始实验链接。");
            string input = link.Trim();
            int marker = input.IndexOf("://", StringComparison.Ordinal);
            if (marker < 1)
                throw new FormatException("启动链接缺少协议头。");
            string protocol = input.Substring(0, marker);
            if (protocol.Equals("http", StringComparison.OrdinalIgnoreCase) || protocol.Equals("https", StringComparison.OrdinalIgnoreCase))
                throw new FormatException("这里需要实验启动链接，而不是网站首页地址。");
            string encoded = input.Substring(marker + 3);
            string payload;
            try
            {
                encoded = Uri.UnescapeDataString(encoded);
                if (encoded.EndsWith("/", StringComparison.Ordinal) && encoded.Length % 4 == 1)
                    encoded = encoded.Substring(0, encoded.Length - 1);
                payload = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(encoded));
            }
            catch (Exception ex) when (ex is FormatException || ex is DecoderFallbackException || ex is UriFormatException)
            {
                throw new FormatException("启动参数的 Base64/UTF-8 格式错误。", ex);
            }
            var result = new LaunchArguments { Protocol = protocol, UserType = "3", ClientIp = "", ClientArea = "", Language = "ZH-CN", CourseId = "-1", SysId = "1", Mode = "", WindowHints = "" };
            string site;
            string port;
            if (payload.TrimStart().StartsWith("{", StringComparison.Ordinal))
            {
                JObject obj;
                try { obj = JObject.Parse(payload); }
                catch (Exception ex) { throw new FormatException("启动参数的 JSON 格式错误。", ex); }
                result.IsJson = true;
                result.LabId = Read(obj, null, "ExamOrLabID", "LabID");
                result.StudentId = Read(obj, null, "StudentID", "UserID");
                site = Read(obj, null, "SiteAdress", "SiteAddress");
                port = Read(obj, "false", "Port");
                result.UserType = Read(obj, "3", "UserType");
                result.ClientIp = Read(obj, "", "Clientip");
                result.ClientArea = Read(obj, "", "Clientarea");
                result.Language = Read(obj, "ZH-CN", "Language");
                result.CourseId = Read(obj, "-1", "CourseID");
                result.SysId = Read(obj, "1", "SysID");
                result.Mode = Read(obj, "", "Mode");
                result.WindowHints = Read(obj, "", "Whtn");
                // Keep format detection separate from an undocumented Mode value.
                result.UseNewHall = Has(obj, "Language") || Has(obj, "CourseID");
            }
            else
            {
                string[] fields = payload.Split('/');
                if (fields.Length < 5)
                    throw new FormatException("旧版启动参数至少需要五项。");
                result.LabId = fields[1];
                site = fields[2];
                port = fields[3];
                result.StudentId = fields[4];
                if (fields.Length > 7) result.UserType = fields[7];
                if (fields.Length > 8) result.ClientIp = fields[8];
                if (fields.Length > 9) result.ClientArea = fields[9];
                if (fields.Length > 10) result.Language = fields[10];
                if (fields.Length > 11) result.CourseId = fields[11];
                result.UseNewHall = fields.Length > 10;
            }
            if (string.IsNullOrWhiteSpace(result.LabId)) throw new FormatException("启动参数缺少实验 ID。");
            if (string.IsNullOrWhiteSpace(result.StudentId)) throw new FormatException("启动参数缺少用户 ID。");
            result.SiteBaseUri = NormalizeSite(site, port);
            return result;
        }

        private static bool Has(JObject obj, string name)
        {
            foreach (JProperty property in obj.Properties())
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string Read(JObject obj, string fallback, params string[] names)
        {
            string value = null;
            bool found = false;
            foreach (JProperty property in obj.Properties())
            {
                foreach (string name in names)
                {
                    if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                    JValue scalar = property.Value as JValue;
                    if (property.Value == null || (scalar != null && scalar.Value == null)) break;
                    if (scalar == null) throw new FormatException("启动参数字段应为标量：" + names[0]);
                    string current = Convert.ToString(scalar.Value, CultureInfo.InvariantCulture);
                    if (found && !string.Equals(value, current, StringComparison.Ordinal))
                        throw new FormatException("启动参数字段值冲突：" + names[0]);
                    found = true;
                    value = current;
                    break;
                }
            }
            return found ? value : fallback;
        }

        private static Uri NormalizeSite(string site, string port)
        {
            if (string.IsNullOrWhiteSpace(site)) throw new FormatException("启动参数缺少站点地址。");
            string value = site.Trim();
            if (value.IndexOf("://", StringComparison.Ordinal) < 0) value = "http://" + value;
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) || (uri.Scheme != "http" && uri.Scheme != "https") || string.IsNullOrEmpty(uri.Host))
                throw new FormatException("启动参数的站点地址格式错误。");
            if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                throw new FormatException("站点基址应仅包含协议、主机、端口与路径前缀。");
            var builder = new UriBuilder(uri);
            string suppliedPort = (port ?? "").Trim();
            if (suppliedPort.Length > 0 && !suppliedPort.Equals("false", StringComparison.OrdinalIgnoreCase) && !suppliedPort.Equals("undefined", StringComparison.OrdinalIgnoreCase) && !suppliedPort.Equals("null", StringComparison.OrdinalIgnoreCase))
            {
                int number;
                if (!int.TryParse(suppliedPort, NumberStyles.None, CultureInfo.InvariantCulture, out number) || number < 1 || number > 65535)
                    throw new FormatException("启动参数的端口格式错误。");
                builder.Port = number;
            }
            builder.Path = uri.AbsolutePath.TrimEnd('/') + "/";
            return builder.Uri;
        }
    }
}
