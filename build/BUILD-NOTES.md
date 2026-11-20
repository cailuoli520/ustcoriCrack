# ustcori_crack 1.2.0.0 — 构建记录

## 结果
- 构建日期：2026-11-20。
- 目标：Windows AnyCPU / Release / .NET Framework 4.8。
- 输出：ustcori_crack.exe，64,512 字节。
- EXE SHA-256：89065f6dd366ad32f276f586c09393500d4747e3558894b53cbbc6c70ff30b6a。
- 编译结果：0 错误、21 警告，警告为未使用的变量/字段。
- 原项目 10 个 C# 文件保持原样；只调整目标框架和引用路径，并增加构建辅助脚本。
- 已静态核验程序集版本 1.2.0.0、入口 ustcori_crack.Program.Main、目标框架与 PE/CLR AnyCPU 标记。
- 已核验随包非 Framework 程序集引用闭包，没有缺失引用或版本不一致。
- 只进行了编译和静态核验；未启动实验客户端，未连接实验平台，未验证在线功能。

## 文件组织
完整解压此 ZIP，保持 EXE、DLL 和配置在同一目录。配置与依赖应一同保留。
构建日志、输入哈希、依赖来源和静态验收数据均随包附带。程序仍依赖其原有实验客户端环境与启动参数，ZIP 不是实验环境安装器。
已检查当前本机 Framework 4.8.1 运行时；本机无需为这个 EXE 另外安装 4.8 运行时。

## 依赖来源
- 除 Component 外，客户端依赖由[奥锐公开安装包](http://aryun.ustcori.com:5014/Upload/LabRoom/%E8%99%9A%E6%8B%9F%E8%BF%90%E8%A1%8C%E7%8E%AF%E5%A2%83.exe)静态提取，选用 net4.0/Download/Updata 文件。安装包 SHA-256：db50a2972c80fb81241caae4e4f0571a33cd53eaccb88ce238f7a9e8c178b94b。
- USTCORi.SimExp.Instruments.Component.dll 来自[固定 GitHub 历史附件](https://raw.githubusercontent.com/NaBr406/weblab-toolkit/678de300feb244c8eecafa0b1237afb551d52145/tools/USTCORi.SimExp.Instruments.Component.dll)，属于第三方再分发；其 README 声明取自实验客户端，官方签名未验证。
- Component 内容长度 2,798,080 字节，Git blob SHA-1：39b21d072bd4283cefc5ce13ade79606c4e15154；SHA-256：0d89e35b6383223999ced9047ae1837f2eb560a7de015f8f5a9d970e4259c7ca。
- Newtonsoft 使用安装包附带的 3.5.0.0 版本；原项目 SpecificVersion=False，本轮真实编译已通过，随包引用版本核验一致。
- 所有输入均为实际程序集，没有代替客户端 API 的桩代码。安装器、第三方脚本和客户端均未执行。

## 复现环境
- 编译机：已配置 build 主机，Linux aarch64，使用任务隔离目录。
- 工具链：Microsoft .NET SDK 10.0.404 (linux-arm64)，Roslyn 5.9.0。
- 引用包：Microsoft.NETFramework.ReferenceAssemblies.net48 1.0.3。
- C# 7.3，TRACE，/platform:anycpu，/optimize+，/deterministic+，/debug-，/filealign:512。
- 原仓库基线：[whmmhw/ustcoriCrack](https://github.com/whmmhw/ustcoriCrack/tree/c5ff96e8507aaa606992058133e61edd83620373)。原目标 4.5.2；本轮按前述方案使用 4.8。
- 远端项目位置与构建命令：

~~~sh
python3 /opt/dsh-build/ustcoriCrack/source/build/remote-build.py
~~~

完整输入文件哈希、编译器路径、编译日志、PE 验证信息和依赖版本在随包清单中。
