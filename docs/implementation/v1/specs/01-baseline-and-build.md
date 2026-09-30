# S01 — 来源基线与可编译恢复

状态：实施规范。优先级：P0。依赖：无。关联任务：T01–T03。

## 1. 目标

将调查仓库恢复成可审阅、可重现构建的产品工程，同时保留官方证据。R0 不修复官方行为；W 才是产品。现有 `decompiled/net9.0` 与 `docs/reverse/net9-verification.md` 是起点，不重新做一轮全面逆向。[R01]

## 2. 目录与构建

```text
upstream/DM.DmProvider/8.3.1.47463/
  manifest.json
  decompiled/net9.0/              # 只读来源快照
  public-api.txt
  source-map.json
src/W.DmProvider/
  W.DmProvider.csproj
  PublicApi/
  Internal/{Configuration,Sessions,Transport,Protocol,Execution,TypeHandling,Pooling,Transactions,Diagnostics,Legacy}/
  Native/
  Resources/
tests/{W.DmProvider.UnitTests,W.DmProvider.ProtocolTests,W.DmProvider.IntegrationTests}/
tools/{DmProbe,UpstreamSnapshot}/
eng/{build,test,verify}/
```

最初允许 W 内大量代码保留在 Legacy；不要为了目录整齐进行无意义大规模重写。R 基线可以使用固定 Git tag/worktree 构建，不要求长期复制第二套可修改源码到 main。三个被测实现必须独立进程运行。

`AssemblyName=PackageId=W.DmProvider`，新公开 namespace `W.Dm`，生产目标 `net10.0`。使用锁定稳定 SDK，禁止继承反编译 csproj 的任意 LangVersion 或继续伪装官方程序集身份。资源的 LogicalName 是明确映射，不以全局字符串替换误改资源查找名。

## 3. 必须产生的清单

manifest 记录包 id/version、原 nupkg SHA-256、net9.0 DLL SHA-256/MVID/程序集版本、卫星资源列表、实际 TFM、反编译工具版本/参数、源码 commit。缺失字段为 null+原因，禁止写猜测值。所有哈希由工具计算，不把 Git blob SHA 当 SHA-256。

source-map 每个恢复过的类/方法记录原 assembly/type/signature、原路径、恢复路径、改名及原因。机械恢复导致的语义可疑点必须回读 IL 并标记需要差分测试。

public-api 清单包含可见类型与成员；W 只承诺 S02 的 surface，额外上游 public 成员逐一登记为 preserved/tested、obsolete、internalized 或 unsupported。对已存在但未覆盖的危险入口（HA、FLDR、自动重连、XA）在 W 中加入明确守卫，不能因为 fork 复制了它们就被默认启用。

许可证与原生依赖按精确资产保留来源、归属及修改声明；包声明的 Apache-2.0 不能替代单独原生库的分发审核。[R09] 本 spec 不作未取得资产的分发授权结论。

## 4. O/R/W 差分运行器

`DmProbe` 以场景 ID 驱动官方 O、恢复 R 和工作 W。它输出 JSON，不输出连接字符串。

```text
scenarioId, implementation, driverAssetHash, sourceCommit,
runtime, serverIdentity, configurationFingerprint,
observedResult, exceptionKind, finalDatabaseState,
evidenceLevel, passed, skippedReason
```

实现适配器只将同一场景编译到各独立宿主，不在同一进程加载三套驱动。配置指纹使用不暴露凭据的不可逆、进程/运行隔离标识；不得提交含密码连接串的散列供离线猜测。

场景最少：Open/Close、参数 SELECT、Unicode CRUD、BEGIN/Commit/Rollback、DML+影响行数、DML+生成键、失败后下一条 SELECT、LOB、已知官方缺陷。对正确场景 O≈R；对于 O 错误行为，R 可保留作为研究基线，W 按 approved-differences.json 改善。

不能宣称反编译 C# 相同即运行行为绝对相同；R 恢复是新编译资产，仍需差分。

## 5. 验收

BAS-01：干净 checkout 在锁定 SDK 下 restore/build/test/pack 可执行。
BAS-02：官方包与产品依赖树隔离，最终 W nupkg 不依赖/不嵌入 DM.DmProvider.dll。
BAS-03：资源 en/zh-CN 等已保留范围能正常读取，不混入未授权原生库。
BAS-04：O/R 差异清单无未解释的功能变化；没有 DM 环境时只标 offline_verified。
BAS-05：根目录/产物不含测试连接串、数据库口令、证书私钥或原始认证报文。

## 6. 不做

不逐项修所有上游缺陷；不全库启用 warnings-as-errors 迫使噪音性重写；允许 Legacy 的有理由告警豁免，但新文件 nullable 与分析规则严格。不要同时扩展 netstandard/net48、多版本 EF 或 NativeAOT。
