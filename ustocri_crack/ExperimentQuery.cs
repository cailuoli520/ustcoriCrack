using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using USTCORi.WebLabClient.DataModel;

namespace ustcori_crack
{
    public static class ExperimentQuery
    {
        public static Uri Endpoint(LaunchArguments launch)
        {
            return launch.ServiceAddress(launch.IsJson ? "ServiceAPI/FindByLABID" : "BizService.svc");
        }

        public static LABINFO ReadJson(LaunchArguments launch)
        {
            if (!launch.IsJson || launch.SysId != "1")
                throw new InvalidOperationException("当前链接属于其他系统流程，实验信息查询入口使用 SysID=1。");
            Uri endpoint = Endpoint(launch);
            var request = (HttpWebRequest)WebRequest.Create(endpoint);
            request.Method = "POST";
            request.ContentType = "application/x-www-form-urlencoded; charset=utf-8";
            request.Accept = "text/json,*/*;q=0.5";
            request.AllowAutoRedirect = false;
            request.Timeout = 20000;
            request.ReadWriteTimeout = 20000;
            request.ServicePoint.Expect100Continue = false;
            byte[] data = Encoding.UTF8.GetBytes("LabID=" + Uri.EscapeDataString(launch.LabId));
            request.ContentLength = data.Length;
            try
            {
                using (Stream stream = request.GetRequestStream()) stream.Write(data, 0, data.Length);
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    if (response.StatusCode != HttpStatusCode.OK)
                        throw new InvalidOperationException("实验信息接口返回 HTTP " + (int)response.StatusCode + "，重定向地址：" + (response.Headers["Location"] ?? "(none)"));
                    using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8, true))
                    {
                        var text = new StringBuilder();
                        var buffer = new char[4096];
                        int count;
                        while ((count = reader.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            if (text.Length + count > 524288) throw new InvalidOperationException("实验信息响应超过读取上限。");
                            text.Append(buffer, 0, count);
                        }
                        string json = text.ToString();
                        if (json.TrimStart().StartsWith("<", StringComparison.Ordinal))
                            throw new InvalidOperationException("实验信息接口返回 HTML 页面，而不是 JSON。Content-Type=" + response.ContentType);
                        return ParseJson(json, launch.LabId);
                    }
                }
            }
            catch (WebException ex)
            {
                using (var response = ex.Response as HttpWebResponse)
                {
                    if (response != null)
                        throw new InvalidOperationException("实验信息接口返回 HTTP " + (int)response.StatusCode + "。", ex);
                }
                throw new InvalidOperationException("实验信息接口连接失败：" + ex.Message, ex);
            }
        }

        public static LABINFO ParseJson(string json, string requestedLabId)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Trim() == "null")
                throw new InvalidOperationException("未找到相应的实验项目。");
            LABINFO lab;
            try
            {
                // Newtonsoft 3.5 matches member names strictly. Adapt the current
                // wire object to the existing model without upgrading client DLLs.
                JObject wire = JObject.Parse(json);
                var normalized = new JObject();
                foreach (var property in typeof(LABINFO).GetProperties())
                {
                    if (property.GetSetMethod() == null || property.GetIndexParameters().Length != 0) continue;
                    JToken value = FindField(wire, property.Name, property.Name == "UpTime" ? "UpdateTime" : property.Name);
                    if (value != null) normalized.Add(new JProperty(property.Name, value));
                }
                lab = JavaScriptConvert.DeserializeObject<LABINFO>(normalized.ToString());
            }
            catch (Exception ex) { throw new InvalidOperationException("实验信息接口的 JSON 数据格式错误。", ex); }
            if (lab == null || lab.LabID <= 0 || string.IsNullOrWhiteSpace(lab.LABNAME) || string.IsNullOrWhiteSpace(lab.LabFileUrl))
                throw new InvalidOperationException("实验信息接口缺少有效的 LabID、LabName 或 LabFileUrl。");
            int expected;
            if (int.TryParse(requestedLabId, NumberStyles.Integer, CultureInfo.InvariantCulture, out expected) && lab.LabID != expected)
                throw new InvalidOperationException("实验信息接口返回的 LabID 与当前启动链接不一致。");
            return lab;
        }

        private static JToken FindField(JObject obj, string name, string alias)
        {
            JToken result = null;
            foreach (JProperty property in obj.Properties())
            {
                if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(property.Name, alias, StringComparison.OrdinalIgnoreCase)) continue;
                if (result != null && !SameFieldValue(result, property.Value))
                    throw new FormatException("实验信息字段值冲突：" + name);
                result = property.Value;
            }
            return result;
        }

        private static bool SameFieldValue(JToken left, JToken right)
        {
            if (left.GetType() != right.GetType()) return false;
            JValue scalarLeft = left as JValue;
            JValue scalarRight = right as JValue;
            if (scalarLeft != null) return object.Equals(scalarLeft.Value, scalarRight.Value);
            return string.Equals(left.ToString(), right.ToString(), StringComparison.Ordinal);
        }
    }
}
