# T12 CLI 安全及候选消费补丁

状态：静态准备完成，未运行 dotnet、CLI、数据库。基于 clean EF commit `113014cc74dd1f751ef97226a78d2ec855b32c8c`，仅修改 `DamengDotNetEfCliFunctionalTests.cs`。哈希见 `cli-safety.json`。先在 clean archive 应用 `cli-safety.patch`，再统一替换 `using Dm;`（包含生成 Program.cs 的字符串中的 using）为 W 命名空间。

Runner 必须设置四个接口：

| 环境变量 | 内容 |
| --- | --- |
| `DAMENG_T12_DOTNET_HOST` | SDK 10.0.401 的绝对 dotnet host 路径 |
| `DAMENG_T12_CANDIDATE_VERSION` | 本次唯一 W.DmProvider prerelease 版本 |
| `DAMENG_T12_NUGET_CONFIG` | 无 secret、含本地候选 feed 的绝对 NuGet.Config 路径 |
| `DAMENG_T12_CLI_ROOT` | 已创建的隔离 CLI 工程和工具目录 |

继承隔离且已创建的 `DOTNET_CLI_HOME`、`NUGET_PACKAGES`、`NUGET_HTTP_CACHE_PATH`。测试连接只来自统一 `DamengTestEnvironment.GetRequiredConnectionString()` 的 TEST wrapper。工具安装、版本检查、restore/build 不接收连接；EF 和模型验证只接收环境连接。`DOTNET_ROOT` 从固定 host 设置。generated project 用自身 `global.json` 禁止 SDK roll-forward，显式 build/restore single-node/no-reuse/no-shared-compiler/disable-build-servers，并关闭上层 Directory.Build.props/targets 导入。新子工程首次恢复产生锁，后续恢复使用 locked-mode。

固定 EF 与 dotnet-ef 10.0.12，动态 csproj 仅引用准确 W.DmProvider 候选版本及已构建 EF 提供程序。核对 assets 无官方驱动、EF 版本及候选版本正确，记录 assets/lock 哈希。子工程 scaffold 完成后再编译并运行模型核对，比较父测试宿主和子进程实际加载的驱动 DLL 哈希。为标准 `CreateHostBuilder` 提供 `Microsoft.AspNetCore.App` FrameworkReference；子进程仅传既有 `DAMENG_TEST_CONNECTION_STRING`，HostBuilder 读取后通过 `AddInMemoryCollection` 映射到配置键 `ConnectionStrings:T12Cli`，支持 scaffold 的 `Name=ConnectionStrings:T12Cli`。不新增连接环境变量，配置映射只在内存中，无连接源码、配置文件或 argv。

每次生成唯一 table/sequence/history，建前验证 TEST 登录和当前 schema、一一确认对象不存在后取得本次清理权。迁移配置、历史查询和清理全用同一唯一 history，保留迁移脚本、表注释、列注释、identity、sequence、降序索引和实体断言。scaffold 加 `--no-onconfiguring`，核对 options 构造函数、无 OnConfiguring、无连接/口令文本，再通过运行时配置编译与模型核对。失败 finally 对每个本次对象独立尝试清理，新连接验证全部不存在；finally 中仅收集清理异常类型，退出后与测试主体异常类型一起报告，避免 CA2219 与掩盖任一失败。不操作固定历史表，不创建用户/schema/tablespace。

所有 subprocess 在 launch 前建立同一 300 秒期限，stdout/stderr 并发读取与进程等待共享 token，超时仅终止自身进程树并最多 5 秒收割。输出仅静态 stage、exit code、stdout/stderr 哈希、包/锁/DLL 哈希；异常只保留静态阶段与异常类型，不附 raw args、message、inner exception 或环境值。源码与运行结果仍需 Low 独立构建、实库及安全扫描验证，静态 dry-run 不代表 CLI 已通过。

## 本轮诊断增量

本轮仅在 `.local/t12/cli-debug-work` 的单文件副本接续 codec 的 63a52f 补丁；原工作副本、clean commit 和历史 snapshots 不修改。业务 SQL、facet/model/rowcount/assertions、唯一 history、TEST identity、清理权和 300 秒 shared subprocess deadline 保留。

阶段细化为 migrations_add / migrations_script / database_update / scaffold / verify_model，保留 restore/build/tool/version 独立阶段。非零退出记录 exit code、stdout/stderr SHA，并在内存由 .NET DbConnectionStringBuilder 提取完整连接串及密码/用户/服务器/host/address/port/schema 等敏感值，生成 XML/HTML/JSON/URI 编码及解码变体后逐个移除。再检查残余 sensitive value、连接键模式和 URI userinfo。无法确认安全时只能保留 hash 和静态 unknown。

当前不输出任何脱敏原文：只从已过滤临时字符串提取 CS/NU/MSB/NETSDK 数字代码、固定 allowlist 的 exception typename 与固定 EF 分类（context/service/constructor/provider/assembly/project/model 等）。输出字段再做敏感变体复查；不把未知 token、路径、quoted literal 或 raw exception Message 作为诊断返回。因此 unknown 不会降级成 raw output。原始 stdout/stderr 不写日志/文件。

安全 structured summary 随专用 CliProcessFailure 传到测试 body 捕获，再在 finally 后与 cleanup_exception_kind 一并抛出；进程树收割失败作为独立 TerminationExceptionKind，不能抹去原非零退出/超时原因。子项目仍在 finally 删除，诊断不依赖残留目录。新增信息仅帮助定位，不能替代业务断言通过；实际编译/CLI/数据库及敏感扫描由 Low fresh snapshot 验证，当前 tests_run=false。

### 必要的二级诊断

首轮结构化层已实际编译成功，但 migrations_add/exit1 仍无已知 code/type/category。经 root 明确批准，现增加 `SafeExcerpt`：只有完整敏感变体替换和 residual connection/URI userinfo/token/private-key 扫描全过才允许。先移除 ANSI CSI/OSC，剩余 C0/DEL 转显式 Unicode escape；再次完整检查，然后按 Unicode rune 截取 UTF-8 最多 8192 字节，截取后再次完整复查。未知/不确定/超界时返回 null excerpt，只保留 hash 与静态 unknown。当前允许的是经过这些检查的脱敏文本；原始 streams 仍只在内存、不写文件、不原样包装 InnerException。其它业务/期限/双方失败保留规则不变；该增量尚未由 Low 实际运行。

### 已证实的 nested host 修正

Low 二级诊断已证：explicit 10.0.401 host 的 restore/build 通过，但 dotnet-ef 内部以 PATH 启动 bare dotnet/msbuild，命中系统 10.0.203 host，无法满足子工程禁止 roll-forward 的 10.0.401 pin。现在 RunDotNetAsync 只对子进程 PATH prepend 已验证 DAMENG_T12_DOTNET_HOST 的目录，与原 DOTNET_ROOT/HOST_PATH 同一目录；不改父进程 PATH、不安装 SDK、不放宽 pin。业务、metadata 与断言全部保留。该定点修正尚待 Low fresh snapshot 实际验证，不能将诊断成功当 CLI 功能通过。

### generated checker 的 EF 10.0.12 metadata 契约

只读核实官方 v10.0.12 的 RelationalEntityTypeExtensions.GetComment 对 RuntimeEntityType 直接抛 RuntimeModelMissingData，RelationalPropertyExtensions.GetComment 对 RuntimeProperty 同样拒绝。这是 checker 误用 runtime model 的明确契约；真实 v5 exit2 原 catch 未保异常类型，因此不能仅凭旧空输出断言确切 throw 点。当前 generated checker 改用 scoped `configuredContext.GetService<IDesignTimeModel>().Model`，保留原表名、表 comment、Code property comment 比较及同包 DLL SHA 断言，不修改提供程序或业务 schema。

checker 若异常，仅输出固定阶段（context_lookup/options_build/context_create/design_model/table_and_comments/candidate_dll_hash）及异常类型，仍 exit2；无 Message、inner exception、连接或实际 facet 值。成功仍只有 DLL SHA。v5 其它完整 gates 已实际通过；此定点变更需 Low 新 CLI-only snapshot/run，旧 immutable artifact 不改。

来源：[EF 10.0.12 entity GetComment](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore.Relational/Extensions/RelationalEntityTypeExtensions.cs#L872)、[property GetComment](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore.Relational/Extensions/RelationalPropertyExtensions.cs#L1183)、[IDesignTimeModel](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/Metadata/IDesignTimeModel.cs)。
