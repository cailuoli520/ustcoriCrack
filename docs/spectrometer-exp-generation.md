# 分光计 EXP 动态标准：最终研究结论

## 结论与完成状态

**两个动态标准具有原始生成函数，探针无需显式创建仿真窗口或发送鼠标、键盘输入即可调用它们。**在此前读取的试卷中，两个 `StdSource=EXP`、`StdResult` 为空的评分项共计 22 分，与观察到的 78/100 缺口相符。无头路径跳过仪器初始化及动态标准保存，是已识别的候选路径原因；本次未做同一状态下的端到端评分对照，不将其表述为完整因果链已验证。

本次完成的是独立缓存样本中的原函数调用和公式一致性验证。**发布的主 EXE 和评分、提交流程未修改；自动生成没有接入主程序，也未验证服务器接受或最终成绩 100。**此处归档研究结论，不作为新的运行包发布。

## 评分缺口

| 参数 | 含义 | 分值 | 已观察到的初始标准 |
| --- | --- | ---: | --- |
| `SLJZSL_DOUBLE` | 折射率 | 6 | `StdSource=EXP`，`StdResult` 为空 |
| `SLJSXPXJ_DOUBLE` | 最小偏向角 | 16 | `StdSource=EXP`，`StdResult` 为空 |

两项都有判据，不是无生成公式。评分器对空标准返回零分；等待时长不会补出动态标准，当前流程在等待期间也不重读试卷。

## 原始生成与保存调用链

在已记录指纹的分光计样本中，静态 IL 与实际运行核对如下：

1. `InstrumentWork.InitEnvData()` 读取仪器枚举参数 `1`，写入 `EnvData` 枚举参数 `8`。不要把仪器参数 `1` 与另一个名为 `NGreen` 的参数混同。
2. 原模型 `CreateDefaultParam()` 为仪器参数 `1` 生成折射率：`n = 1.6519 * (rng.Next(9186, 10100) / 10000.0)`，其中 `rng` 为新建的 `Random` 实例。先除再乘的浮点运算顺序与原 IL 一致；`Next` 的上界不包含在结果中。
3. 原 `DataWindow.GetStd(object)` 对 `SLJZSL_DOUBLE` 返回 `n.ToString("F2")`。
4. 同一函数对 `SLJSXPXJ_DOUBLE` 使用同一个未舍入 `n` 和顶角 `A = PI / 3`（60°）计算最小偏向角，再保留两位小数：

```csharp
double A = Math.PI / 3.0;
double delta = (2.0 * Math.Asin(n * Math.Sin(A / 2.0)) - A) * 180.0 / Math.PI;
string refractiveIndexStd = n.ToString("F2");
string minimumDeviationStd = delta.ToString("F2");
```

5. 原 `DataWindow.SaveData(bool)` 取生成结果，对 `StdSource.ToUpper() == "EXP"` 且结果不是 `"*"` 的字段写入 `StdResult`，随后调用实验配置保存函数。**保存路径本次仅作静态分析，探针没有调用它。**

保存按钮是正常 GUI 路径中的保存触发点，而不是标准值的数学来源。数据生成本身不以人工测量或点击为依赖。

## 无界面实际验证

[原始验证结果 JSON](<evidence/spectrometer-generator-verification.json>) 记录三次独立运行，全部退出 0 并输出 `GENERATOR_VERIFIED`：

| 试验 | 原模型生成 n | 原 GetStd 折射率 | 原 GetStd 最小偏向角（°） |
| --- | --- | --- | --- |
| 1 | 1.56220183 | 1.56 | 42.72 |
| 2 | 1.6621417799999998 | 1.66 | 52.42 |
| 3 | 1.55691575 | 1.56 | 42.24 |

- 调用原模型的生成函数，再调用原 `GetStd`，与重建公式逐项比较；不是只运行自写公式。
- 未调用实验主入口、WPF 数据窗口或参数控件的构造函数；对已审阅、不读取 `this` 字段的 `GetStd` 使用未初始化调用载体。
- 模型及其基类、EnvData 的构造函数和原方法会执行；绕过的是 DataWindow 与 SinglePara 的构造函数。这不是“程序集完全不执行”的静态工具，也不是整个仿真实验的自动操作测试。
- 窗口、输入、文件修改三个零标记为探针的固定说明输出，不是观测计数，不覆盖全部间接副作用。文件保持一致由下一项单独的 SHA-256 校验支持；未额外做 OS 级窗口或网络观测。
- 另行在重复验证前后比较两个样本程序集和两份试卷配置的 SHA-256，均保持一致。探针没有改写试卷，也没有调用客户端提交路径。
- 发布证据仅含人工复现说明、样本指纹、数值结果及源码；不附学生启动链接、账号、站点、试卷、实验包或个人目录。

每一轮仪器状态可能随机化，以上数值不是其他运行的固定答案。偏向角必须使用未提前舍入的同一个 `n`；恢复已有实验时应读取已有模型状态，而不是另起一轮随机值。

## 独立复现

源码为 [ProbeSpectrometerGenerator.cs](<../build/analysis/ProbeSpectrometerGenerator.cs>)，独立 CLR 配置为 [ProbeSpectrometerGenerator.exe.config](<../build/analysis/ProbeSpectrometerGenerator.exe.config>)。

环境：Windows、.NET Framework 4.8/4.8.1。准备与 JSON 指纹匹配的本地分光计样本，将下列 `SAMPLE_DIR` 替换为包含原实验程序集及配套依赖的目录。不要使用或覆盖正在进行的实验目录。仓库不附该实验包。

在仓库根目录运行 PowerShell：

```powershell
$out = 'build\analysis\out'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
& $compiler /nologo /target:exe /platform:x86 /optimize+ /out:build\analysis\out\ProbeSpectrometerGenerator.exe build\analysis\ProbeSpectrometerGenerator.cs
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
Copy-Item build\analysis\ProbeSpectrometerGenerator.exe.config build\analysis\out\ProbeSpectrometerGenerator.exe.config
& build\analysis\out\ProbeSpectrometerGenerator.exe 'SAMPLE_DIR'
if ($LASTEXITCODE -ne 0) { throw 'Generator verification failed' }
```

该样本实验主模块要求 32 位，探针必须编译为 **x86**。独立配置中的 `loadFromRemoteSources` 仅用于此分析探针加载已有下载来源标记的样本，不修改正式客户端配置。

预期输出包含 `GENERATOR_VERIFIED` 与 `formula_matches_original=True`。随机生成的具体数值不要求重复上表。

## 自动化实现落点（本次未接入）

[NewSiteFlow.cs:122–128](<../ustocri_crack/NewSiteFlow.cs#L122-L128>) 在 `Utiles.LoadXml()` 后直接调用 `Utiles.ModifyExperiment()`。研究确定的逻辑落点是这两步之间：恢复或初始化本轮仪器状态，读取同一轮 `n`，生成两个 EXP 标准，再交给后续校验处理。

这里仅记录落点和一致性条件，不声称已有主程序实现、完整实验动作复现或服务端结果。当前研究任务到此归档结束。
