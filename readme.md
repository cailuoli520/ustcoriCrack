# USTC OriCrack

虚拟实验客户端的评分逻辑复现与研究工具。项目复刻 `PaperReviewer.GainScore` 判据，并提供独立复算和回归用例。

## 下载：使用完整运行包

**[下载可直接解压启动的完整运行包](https://github.com/cailuoli520/ustcoriCrack/releases/download/v1.3.0-runtime-fix/ustcori-crack-v1.3.0-runtime-fixed.zip)** · [发布页与校验信息](https://github.com/cailuoli520/ustcoriCrack/releases/tag/v1.3.0-runtime-fix)

运行环境：Windows + .NET Framework 4.8 / 4.8.1，无需 SDK。把整包解压到新目录，保留主程序、两份配置与 **9 件配套程序集**。Code → Download ZIP 的根目录也带相同运行文件；发布附件是不带源码的小型运行包。

在解压目录打开 CMD：

```bat
check-runtime.cmd -StrictPackage
ustcori_crack.exe --help
ustcori_crack.exe --cases
```

预期：`RUNTIME_CHECK_PASS assemblies=10`，帮助正常显示，13 个回归用例全部通过。

此前发布漏带了 Model 等依赖，且两份配置中的主机占位符破坏 XML，分别导致程序集缺失和 Windows 启动错误。本次补齐同一套原配、修复配置并增加逐文件检查。主 EXE 仍是 1.3.0.0，字节未变。

详细下载、启动和依赖说明见 [完整运行包说明](<docs/runtime.md>)。运行文件应放在同一目录，单独复制主 EXE 会再次缺依赖。

## --submit / --wait 用法

```bat
ustcori_crack.exe --check-launch "[LABORI_LINK]"
ustcori_crack.exe --submit --wait 1200 "[LABORI_LINK]"
```

用本次完整的 `labori://...` 链接替换 `[LABORI_LINK]`。

- `--check-launch`：只解析链接，零请求。
- `--submit`：跳过仿真窗口，使用无头流程；支持 JSON 格式启动链接。
- `--wait N`：**求解并复算之后、发送之前等待 N 秒**，不是排队等待。1200 为 20 分钟，期间没有逐秒进度；0 表示这一步等待为零。1.3.0 内置帮助对这个选项的描述仍是旧文字，以本节实际语义为准。
- 未取得运行名额时，程序可能只打印排队信息后退出。
- 负数等待会跳过最终 `UpdateRecord`，但前段仍可能创建排队和开始计时记录；它不是零请求模式。
- 本地复算、进程退出码和服务端接受结果分开判断。服务端非成功返回值需单独排查，记录可能在等待期间失效；第三方对 -3/-4 的解释不作为本项目的服务端协议保证。

`--metadata-only` 是联网只读查询。`--cases`、`--probe-payload` 是离线测试。`--self-test` 需合法链接和本地实验包；运行包不附实验 `.lab` 数据。

## 验收与开发

完整包的发布验收覆盖：最终 ZIP 新目录解压、10 个程序集的身份/大小/SHA-256、两份配置的 XML/URI、帮助、13 个回归、编码探针、30 个链接解析用例和 3 个 loopback CLI 用例。该验收不等价于所有仿真 GUI、每个实验或真实服务器结果已验证。

[协议回归脚本](<build/protocol-tests/run-tests.py>) 使用本地 fixture。先把整包运行文件放到脚本旁的 `runtime` 子目录，再运行：

```bat
python build\protocol-tests\run-tests.py
```

测试输入使用人工学号与 loopback；只读服务失败测试仅调用 `FindByLABID`，不排队、不开始实验、不提交记录。

如需完整实验包自检或提交链路 fixture，另准备人工脱敏的实验包。fixture 启动需 `--keep-open`；校验命令需显式捕获路径：

```bat
python build\fixture\server.py --keep-open
ustcori_crack.exe --verify-fixture build\fixture\captured.json solved-exam.xml
```

捕获验证器核对列出的评分项与答案序列，不是全份 XML 的逐字节比较。实验包、个人链接、捕获、求解 XML、错误日志及部署路径均不作为本次公开附件输入；公共依赖来源 URL 有意保留。

### 编译与打包

编译依赖：Windows、.NET Framework 4.8 引用程序集、.NET SDK Roslyn。将发布包原配 DLL 和配套客户端复制到 [源码依赖目录](<ustocri_crack/lib/>)；来源与固定哈希见 [dependency-provenance.json](<ustocri_crack/lib/dependency-provenance.json>)。

```powershell
powershell -ExecutionPolicy Bypass -File build\local-build.ps1
python build\package-runtime.py --runtime . --dependencies . --stage build\runtime-stage --output ustcori-crack-v1.3.0-runtime-fixed.zip
```

[完整包构建器](<build/package-runtime.py>) 固定匹配 1.3.0 的主 EXE 与 9 件依赖，并附检查脚本及清单；新编译主程序发布时需同步更新其固定指纹和验收结果。[旧增量部署脚本](<build/deploy-fullscore.py>) 只生成三文件 `patch.zip`，不再以完整包命名。

### 评分机制

- 百分比型：`(x/s-k)/k ∈ [Min,Max]`；绝对值型：`x-k*s ∈ [Min,Max]`。
- 保留官方同分最高规则重叠导致 `Single()` 异常的边界行为。
- 仅处理有判据且 `TotalScore > 0` 的评分点；其他读数和标准答案 `StdResult` 保留。

## Change Log

- **1.3.0 runtime fix**：补齐 9 个匹配程序集；合法 loopback 默认配置；完整包与增量包分开；增加运行清单/启动检查；修正用法与人工测试输入。
- 1.3.0：评分求解与复算、PE 完整性校验、帮助、自检及无头流程。
- 1.2.0：实验数据查看和时间提示。
- 1.1.x：放弃提交、兼容性与新系统修复。
