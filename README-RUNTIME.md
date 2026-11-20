# 完整运行包（1.3.0 runtime fix）

## 下载和解压

下载 [完整运行包](https://github.com/cailuoli520/ustcoriCrack/releases/download/v1.3.0-runtime-fix/ustcori-crack-v1.3.0-runtime-fixed.zip)，把整个 ZIP 解压到一个新目录。Windows 需安装 .NET Framework 4.8 或 4.8.1；运行时无需安装 SDK。

主程序、两份配置与 **9 个配套程序集必须在同一个目录**。请保留整包，单独复制 EXE 会再次缺依赖。GitHub 的 Code ZIP 也带有同一套根目录运行文件；小型运行包更便于直接使用。

## 先验证启动

在解压目录打开 CMD：

```bat
check-runtime.cmd -StrictPackage
ustcori_crack.exe --help
ustcori_crack.exe --cases
```

预期：`RUNTIME_CHECK_PASS assemblies=10`、帮助正常显示、`全部用例通过。`。检查脚本只读取程序集元数据、哈希和配置，不发送请求。第一次启动前使用 `-StrictPackage` 检查原始配置；正常启动会按启动链接改写配置，之后可执行不带此开关的检查。

## 启动参数

```bat
ustcori_crack.exe --check-launch "[LABORI_LINK]"
ustcori_crack.exe --submit --wait 1200 "[LABORI_LINK]"
```

- 用你本次的完整 `labori://...` 链接替换 `[LABORI_LINK]`。`--check-launch` 只解析，零请求。
- `--submit` 使用无头流程，跳过仿真窗口；支持 JSON 格式启动链接。
- `--wait 1200` 是**求解和复算之后、发送之前等待 20 分钟**，不是排队等待。等待期间没有每秒进度。`--wait 0` 表示这一步等待为零。
- 此包保留 1.3.0 原 EXE；其内置帮助仍把 `--wait` 标为“排队等待”，以上按实际代码说明。
- 排队未取得运行名额时，本次运行可能只输出排队信息然后退出。
- `--wait -1` 跳过最终 `UpdateRecord`，但流程前段可能已创建排队和开始计时记录。零请求检查使用 `--check-launch`，而不是负数等待。
- 本地复算分数、进程退出码与服务端是否接受是不同结果。服务端非成功返回值需另行检查；等待期间记录也可能失效。

## 依赖为何必须匹配

完整包包含 Model、Component、Security、PackageLibrary、Newtonsoft.Json、SharpZipLib、Unity3DWPFConnection、WpfAnimatedGif 以及配套 WebLabClient。

其中 `WebLabClient.exe` 的程序集身份应为 `WebLabClient, Version=1.1.7507.17612`；把其他版本或不同程序集改成这个文件名并不等价。即使程序集版本相同，字节也可能不同；整包清单同时记录大小、SHA-256 和完整身份。WpfAnimatedGif 是 WPF BAML 的依赖，单看 CLR 引用列表会漏掉。

默认服务地址使用有效的 `http://127.0.0.1/`，启动时再按链接更新。XML 属性里的主机占位符若包含裸尖括号，会使配置语法损坏并导致 Windows 启动错误。

## 本次修复与验证口径

本次是包装修复：补齐原配依赖、修正配置、增加启动前检查；主 EXE SHA-256 保持：

```text
b5be0ad1661433eeb8647c5d7476835f4769dca9479f485936cc65a43028debf
```

包内不带实验 `.lab` 数据、求解 XML、真实链接、提交捕获、个人日志或本地路径。依赖来源的公共 URL 保留在仓库来源记录中，厂商原件不修改字节。

发布验收针对最终 ZIP 在新目录执行：依赖与配置检查、帮助、13 个回归用例、编码探针、30 个链接解析用例，以及仅连接 loopback fixture 的 3 个 CLI 用例。这些结果验证启动和已列出的代码路径，不代表每个实验包、仿真 GUI 或真实服务器结果已验证。
