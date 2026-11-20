# JSON 启动兼容修复包

## 使用
将整个压缩包解压到同一目录，运行 ustcori_crack.exe，再粘贴实验按钮提供的 labori:// 启动链接。
目录中的 DLL、WebLabClient.exe 和两份 .exe.config 都要保留。

## 本次修改
- 识别 Base64 内的 JSON，兼容 SiteAdress / SiteAddress 及大小写，并保留旧位置参数。
- 保留站点协议、原端口和路径前缀；Port=false / undefined 不另加端口。
- 用解析后的站点显式配置实际 WCF 客户端，同时更新主程序与 WebLabClient 的端点及 HTTP/HTTPS 绑定模式。
- 修正旧版文件传输客户端重复拼接 http:// 的问题。
- 参数错误返回1；服务查询或初始化失败返回2，不继续使用空实验对象。

## 诊断命令
ustcori_crack.exe --check-launch "[实验启动链接]"
只解析，不连接服务、不创建实验对象、不写配置。

ustcori_crack.exe --metadata-only "[实验启动链接]"
只调用 FindByLABID 查询实验名称，初始化后直接退出；会更新本目录的两份服务配置，但不排队、不启动实验、不提交记录。

## 已验证及当前状态
.NET Framework 4.8 / Windows AnyCPU；可使用本机已有4.8.1运行时。
真实编译：11份源文件，0个编译错误；21个原有未使用成员/变量警告。
30项实际程序集解析回归、3项程序入口/失败退出回归、2项 HTTP/HTTPS 双配置往返验证通过。
构建产物与本机源码按字节核对，所有依赖保留真实客户端程序集；未使用桩替代依赖。

当前学校链接解析结果为实验ID 2、站点 http://<SERVER_HOST>/、Port=false。
最终程序只读请求 http://<SERVER_HOST>/BizService.svc，实际收到 text/html Error 页面，而非旧客户端预期的 text/xml WCF 响应。
所以 JSON 解析、误连旧默认地址和后续空引用已修复；当前站点的旧 WCF 实验查询兼容性仍未通过，完整实验启动/提交未测试。
现有公开资料及站点已知首页/脚本未给出可验证的其他实验查询地址。本包没有猜测或自动扫描其他接口。

主EXE SHA256：01461a8a8556cae136c454d97972ddab0680124c9b1f7d2324add6ad2f9400e6
主EXE：73216 bytes，程序集版本1.2.0.0。

## 来源与回滚
业务源码基于 whmmhw/ustcoriCrack 的原始提交，新增 LaunchArguments.cs 并修复启动路径。
官方安装包提供多数程序集；USTCORi.SimExp.Instruments.Component.dll来自固定提交的第三方客户端再分发，来源及哈希保留于 provenance 文件，未宣称官方签名已验证。
原源码已备份于 build/backups/protocol-before；运行目录更新前会备份原EXE及原配置。
