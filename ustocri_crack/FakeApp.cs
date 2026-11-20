using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;
using USTCORi.WebLabClient;
using USTCORi.WebLabClient.BizServiceReference;
using USTCORi.WebLabClient.DataModel;
using USTCORi.WebLabClient.RuningInfo;
using USTCORi.WebLabClient.WLServiceReference;

namespace ustcori_crack
{
    public class FakeApp
    {
        public static RunningInfo runningInfo;
        public string Verifykey = "USTCORikeyLab";
        public Task findLabTask;
        public FakeHall fakeHall;
        public FakeHall2 fakeHall2;
        public bool isNewVersion = false;

        public LaunchArguments Launch { get; private set; }
        public bool IsInitialized { get; private set; }
        public string InitializationError { get; private set; }

        public FakeApp(string labAddress, bool metadataOnly = false)
        {
            Launch = LaunchArguments.Parse(labAddress);
            isNewVersion = Launch.UseNewHall;
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            InitInfo(baseDirectory);
            App.runningInfo.Const.SetUserName(Launch.StudentId);
            ConfigureEndpoints(baseDirectory, Launch);
            initServiceClient(Launch);
            try
            {
                Console.WriteLine("获取实验项目……");
                LABINFO lab;
                if (Launch.IsJson)
                {
                    lab = ExperimentQuery.ReadJson(Launch);
                }
                else
                {
                    findLabTask = new Task();
                    findLabTask.BizCode = "UstcOri.BLL.BLLLabClent";
                    findLabTask.MethodName = "FindByLABID";
                    findLabTask.ReturnType = typeof(IList<LABINFO>);
                    findLabTask.SetParameter("LabID", (object)Launch.LabId);
                    SvcResponse response = findLabTask.Run();
                    if (response == null) { FailInitialization("实验服务返回了空响应。"); return; }
                    if (!string.IsNullOrEmpty(response.Message)) { FailInitialization(response.Message); return; }
                    IList<LABINFO> labs = response.Data as IList<LABINFO>;
                    if (labs == null) { FailInitialization("实验服务返回的数据类型不匹配。"); return; }
                    if (labs.Count == 0 || labs[0] == null) { FailInitialization("未找到相应的实验项目。"); return; }
                    lab = labs[0];
                }
                Console.WriteLine("实验名称：" + lab.LABNAME);
                if (metadataOnly) { IsInitialized = true; return; }
                if (isNewVersion)
                    fakeHall2 = new FakeHall2(lab, baseDirectory, Launch.StudentId, Launch.UserType, Launch.ClientIp, Launch.ClientArea, Launch.CourseId);
                else
                    fakeHall = new FakeHall(lab, baseDirectory, Launch.StudentId, Launch.UserType);
                IsInitialized = isNewVersion ? fakeHall2 != null : fakeHall != null;
            }
            catch (Exception ex)
            {
                FailInitialization("获取实验项目失败：" + ex.Message);
            }
        }

        private void FailInitialization(string message)
        {
            InitializationError = message;
            IsInitialized = false;
            Console.WriteLine(message);
        }

        private static void ConfigureEndpoints(string baseDirectory, LaunchArguments launch)
        {
            string mainConfig = System.IO.Path.Combine(baseDirectory, AppDomain.CurrentDomain.FriendlyName + ".config");
            XElement config = XElement.Load(mainConfig);
            XElement serviceModel = config.Element("system.serviceModel");
            XElement client = serviceModel == null ? null : serviceModel.Element("client");
            if (client == null) throw new InvalidOperationException("主程序配置缺少服务客户端设置。");
            foreach (XElement endpoint in client.Elements("endpoint"))
            {
                string contract = (string)endpoint.Attribute("contract") ?? "";
                string service = null;
                if (contract.EndsWith(".IBizService", StringComparison.Ordinal)) service = "BizService.svc";
                else if (contract.EndsWith(".IWLService", StringComparison.Ordinal)) service = "WLService.svc";
                else if (contract.EndsWith(".IFileTransfer", StringComparison.Ordinal)) service = "FileTransfer.svc";
                if (service != null) endpoint.SetAttributeValue("address", launch.ServiceAddress(service).AbsoluteUri);
            }
            XElement bindings = serviceModel.Element("bindings");
            XElement basicBindings = bindings == null ? null : bindings.Element("basicHttpBinding");
            if (basicBindings != null)
            {
                foreach (XElement binding in basicBindings.Elements("binding"))
                {
                    string name = (string)binding.Attribute("name") ?? "";
                    bool referenced = false;
                    foreach (XElement endpoint in client.Elements("endpoint"))
                    {
                        string contract = (string)endpoint.Attribute("contract") ?? "";
                        if ((contract.EndsWith(".IBizService", StringComparison.Ordinal) || contract.EndsWith(".IWLService", StringComparison.Ordinal) || contract.EndsWith(".IFileTransfer", StringComparison.Ordinal)) && (string)endpoint.Attribute("bindingConfiguration") == name) referenced = true;
                    }
                    if (!referenced) continue;
                    XElement security = binding.Element("security");
                    if (security == null) { security = new XElement("security"); binding.Add(security); }
                    security.SetAttributeValue("mode", launch.SiteBaseUri.Scheme == "https" ? "Transport" : "None");
                }
            }
            config.Save(mainConfig);
            config.Save(System.IO.Path.Combine(baseDirectory, "WebLabClient.exe.config"));
        }

        public void ExitApp()
        {
            Console.WriteLine("Exiting...");
        }

        private void InitInfo(string path)
        {
            App.runningInfo = new RunningInfo();
            App.runningInfo.Const.SetShoolUser("No School");
            App.runningInfo.Const.SetVersion("V2.01.0914");
            App.runningInfo.Const.SetDownloadExperimentPath(path + "Download\\");
            App.runningInfo.Const.SetDownloadExperimentXmlPath(path + "Download\\download.xml");
            App.runningInfo.Const.SetDownloadUpdataPath(path + "Download\\Updata\\");
            App.runningInfo.Global.NeedConfirmShutdown = true;
        }

        private void initServiceClient(LaunchArguments launch)
        {
            BizServiceClient c = new BizServiceClient();
            c.Endpoint.Address = new System.ServiceModel.EndpointAddress(launch.ServiceAddress("BizService.svc"));
            var binding = c.Endpoint.Binding as System.ServiceModel.BasicHttpBinding;
            if (binding != null)
            {
                binding.Security.Mode = launch.SiteBaseUri.Scheme == "https" ? System.ServiceModel.BasicHttpSecurityMode.Transport : System.ServiceModel.BasicHttpSecurityMode.None;
                binding.OpenTimeout = TimeSpan.FromSeconds(8);
                binding.SendTimeout = TimeSpan.FromSeconds(20);
            }
            App.runningInfo.Const.SetClient(c);
        }

    }
}