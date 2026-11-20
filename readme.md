# USTC OriCrack（逆向研究 / 演示版本）

> 本项目是对一款虚拟实验客户端的**逆向学习与评分逻辑复现**演示，所有目标地址、学号、课程号均已脱敏。仅供本地离线研究，请勿用于真实教学平台。

本项目复刻了官方评分器 `PaperReviewer.GainScore` 的判据，并提供一个“满分求解器”：按官方判据反解每组读数，再用独立复算器核对，确保答案在判据允许的区间内拿到最高分。附带回归用例与本地 fixture，可完整离线验收。

## 编译

环境：Windows + .NET Framework 4.8 引用程序集 + .NET SDK 的 Roslyn `csc.dll`。

```powershell
powershell -ExecutionPolicy Bypass -File build\local-build.ps1
```

产物：`ustocri_crack\bin\Release-local\ustcori_crack.exe`。

## 验证（离线，无网络请求）

```powershell
# 1) 回归用例：13 个判据边界用例
ustcori_crack.exe --cases

# 2) 离线全程验收：审计原答案 → 求解 → 独立复算
ustcori_crack.exe --self-test

# 3) 解析启动链接但不发请求
ustcori_crack.exe --check-launch "<labori 启动链接>"

# 4) 只查实验元数据（只读）
ustcori_crack.exe --metadata-only "<labori 启动链接>"

# 5) Payload 格式自检 / 端到端 fixture 验证
ustcori_crack.exe --probe-payload
python build\fixture\server.py   # 另开一个终端
ustcori_crack.exe --verify-fixture
```

退出码：`0` 成功，`1` 参数错误，`2` 初始化或网络失败，`3` 验收未通过。

## 启动链接格式

`build\make-link.py` 可生成脱敏的 labori 启动链接：

```powershell
python build\make-link.py --site http://<SERVER_HOST> --lab <实验ID> --student <学号> --course <课程ID> --mode <模式>
```

程序解析时会自动为 `SiteAdress` 补 `http://`；`Port` 为 `false` / 空时表示不覆盖端口。

## 目录说明

- `ustocri_crack/`：C# 源码（`ScoreJudge.cs` 判据复刻、`FullScoreSolver.cs` 求解器、`ScoreReport.cs` 报告、`NewSiteFlow.cs` 流程、`FixtureVerifier.cs` 验收等）。
- `build/`：构建脚本、回归脚本、脱敏后的 fixture 服务端、启动链接生成器。
- `ustocri_crack/lib/`：第三方 DLL 通过 `dependency-provenance.json` 记录来源，**不入库二进制**（见 `.gitignore`），请按来源自行获取。

## 设计要点

- 判据复刻：百分比型 `(x/s-k)/k ∈ [Min,Max]`，绝对值型 `x-k·s ∈ [Min,Max]`；保留官方 `Single()` 多解冲突抛异常被吞的边界行为。
- 求解器只在有判据且 `TotalScore>0` 的评分点上改动答案，其余读数原样保留，标准答案列 `StdResult` 一字不动。
- 打包与提交：加密与签名格式经 fixture 端到端验证，服务端收到的密文可还原为求解后的 XML，且本地复算与服务端复算一致。

## Change Log

- 1.3.0：满分求解器 + 官方判据复算 + PE 完整性校验（自动修复解包损坏）+ `--help` / 多种自检模式。
- 1.2.0：实验数据查看、提交时间过短提示。
- 1.1.2：修复放弃提交无效。
- 1.1.1：修复部分系统兼容问题。
- 1.1.0：支持新系统。
- 1.0.0：首次发布。
