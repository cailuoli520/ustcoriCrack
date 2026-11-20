# HTTP 实验信息查询修复

## 已修复
- 当前站点的普通实验 JSON 启动流程使用 POST /ServiceAPI/FindByLABID，表单只有 LabID；旧 BizService.svc 请求会重定向到 /Login/Error。
- 已从本机协议注册所引用的 2023 客户端静态恢复实际接口，并成功读取 LabID=2 的实验元数据。
- 保留旧位置参数链接的 WCF 查询；规范化 LabName → LABNAME、UpdateTime → UpTime，并保留旧模型支持的文件属性。
- 保留当前官方客户端 DLL；没有升级旧 Newtonsoft.Json，也没有改动实验评分或提交代码。
- 原来的错误按键停留及日志记录继续保留。

## 实际只读输出
```text
目标服务：http://<SERVER_HOST>/ServiceAPI/FindByLABID
获取实验项目……
实验名称：<LAB_NAME>
```

## 验证
- 64 项本机回归通过：30 启动解析、2 配置、3 CLI、2 原生交互窗口、1 重定向输入、17 新旧查询、9 模型映射。
- 已验证实际旧 DLL 的 Microsoft JSON 日期转换、文件属性保留、名称大小写和不同 token 类型的别名冲突。
- 真实站点只执行 --metadata-only 元数据读取；没有启动实验或提交记录。完整的下载、排队和实验运行尚未进行端到端验证。
- 目标为 .NET Framework 4.8 / Windows AnyCPU；本机 4.8.1 运行时满足该编译目标。

## 运行
已更新 [当前程序](<<USER_HOME>/Downloads/Compressed/Updata/ustcori_crack.exe>)。重新打开并粘贴原实验启动链接。
窗口标题应包含 HTTP JSON query + error pause；目标服务应显示 ServiceAPI/FindByLABID。
压缩包包含全部运行依赖；不要只拷贝主程序到空目录。
原程序和配置在运行目录的新备份子目录保留，当前 DLL 和配置不被交付脚本替换。

## 版本校验
```text
EXE bytes: 76800
EXE SHA256: b3438e14b26d8b6cb05812960dacaa8fb85111c42e0ca1a10f1a0a5df7a903fb
```
