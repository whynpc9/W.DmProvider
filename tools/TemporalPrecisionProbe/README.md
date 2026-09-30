# T12 七位时间精度探针

状态：源码准备，未运行 dotnet/数据库。仅验证冻结输入，不能据 `diagnostic_complete` 宣称 W 公共七位写入可用。

## 输入和入口

- `Official.csproj`：exact `DM.DmProvider 8.3.1.47463`，运行 net10 探针但强校验加载官方 `lib/net9.0` 资产。
- `Candidate.csproj`：exact R1 包 `W.DmProvider 0.1.0-r1.20260930051717`，没有项目引用或源码绕过。
- `Program.cs profile <package.nupkg> <safe-report.json>`：包、资产、已加载 DLL SHA/MVID 均必须匹配 `expected-identities.json` 与源码常量，才可进入 TEST。连接仅来自 wrapper 的 `DAMENG_TEST_CONNECTION_STRING`；O 保留官方配置，W typed builder 显式 PlaintextAllowed/PersistSecurityInfo=false。同一 TEST 登录及当前 schema 均检查。
- `Program.cs codec <package.nupkg> <safe-report.json>`：只调用已有 public static 扩展 encoder，记录9/11-byte合成时间字节和哈希；不加载连接。profile 也尝试此独立 helper 比较，但 helper 缺失单独报告，不阻止关键公开 O 写入/服务器 oracle。
- `python3 validate.py <O-report.json> <W-report.json> --output <diagnostic.json>`：独立校验安全报告。O public typed write 与 server FF7/offset 必须正确；O 的 raw/typed/generic getter差异仅作表征。W literal typed/generic DateTime、DTO、GetString+Invariant Parse必须保持一tick、Unspecified与原偏移；W public typed write失败必须保留 characterization，不计公共七位成功。

包路径、哈希、MVID、asset 路径固定在 `expected-identities.json`。候选 feed 默认本仓库 `.local/t12/packages/20260930T051717Z`，官方 feed 默认本仓库 `packages`。两个项目的 bin/obj独立，锁路径明确为 `Official.packages.lock.json` / `Candidate.packages.lock.json`；round1产生的旧共享 `packages.lock.json` 不再作为这两个项目的锁输入，保留原证据。SDK pin 为工具目录自己的 `global.json`，**10.0.203、rollForward=disable**；从该目录启动固定可用 host。`.401` 是 EF repo pin，不用于悄悄覆盖本工具 pin。

## 样本和验收语义

仅用固定 EF113014 的 `2026-07-23 14:15:16` + `AddTicks(1)` 与本次指定的 UTC、+08:00、-05:00、+05:30 四个偏移，不扩展数据集。

每 lane 在 TEST 当前 schema 创建唯一 `TP7_<random>` table，列为 TIMESTAMP(7)/DATETIME(7) WITH TIME ZONE，建前确认同名对象不存在。O 使用公开 DbType.DateTime2/DateTimeOffset 参数（Precision=7、Scale未显式设置）执行真实插入；W 使用完全相同的合成日期经 CAST 字面量插入、独立新连接读取。两 lane 记录 prepare 后实际 CType/typeFlag/precision/scale/mask 与 requested DbType，不记录参数值或认证报文。W 当前公开参数写入被拒绝后再用新连接确认对应 ID 未增加。

服务器 `TO_CHAR(...,'YYYY-MM-DD HH24:MI:SS.FF7')` 与 DTO 的 `FF7 TZH:TZM` 文本必须精确匹配合成输入。报告仅保存 expected ticks/offset、getter类型与ticks/offset、匹配布尔值和文本哈希。DTO O raw GetValue=DateTime 等已知差异分别记录。W 第七位公开 gate不会被反射移除，也不会在插入参数时调用内部 SetValue绕过。

每次连接在任何 DDL 前严格核对目标 `ServerVersion == 8.1.5.60`，安全报告记录版本，validator要求 O/W同时匹配该固定profile。建表前写入 `<safe-report.json>.ownership.json`，仅保留本次唯一表名、已验证身份/profile与包哈希，供宿主超时后精确定位本次对象。finally只清理该唯一表，清理连接再次验证身份/profile，随后另一新连接核实对象不存在。DDL按隐式提交处理。任何创建、写入、读取或清理失败报告安全 stage、异常类型及 Number，不输出 message、SQL、连接、环境内容或 inner异常文本。没有EnsureDeleted/admin/cross-schema DDL。

## Low 独占执行模板

以下是准备模板，不是已执行证据。Low先冻结工具源码、两包、manifest哈希，设置独立且可写 DOTNET_CLI_HOME/NUGET_PACKAGES/NUGET_HTTP_CACHE_PATH、DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1、DOTNET_CLI_TELEMETRY_OPTOUT=1。从 `tools/TemporalPrecisionProbe` 工作目录，以能解析10.0.203的固定host顺序构建两项目，并记录 `--version`、实际资产/锁/DLL身份。

```sh
dotnet restore Official.csproj --disable-parallel -m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers
dotnet build Official.csproj --no-restore -m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers
dotnet restore Candidate.csproj --disable-parallel -m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers
dotnet build Candidate.csproj --no-restore -m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers
```

首次 restore 产生锁，后续使用 `--locked-mode`。执行时使用本仓库 TEST wrapper，每个 probe独立输出目录、有限宿主超时、退出码和最终对象状态。入口 DLL通常为 `bin/Official/net10.0/TemporalPrecisionProbe.Official.dll` 和 `bin/Candidate/net10.0/TemporalPrecisionProbe.Candidate.dll`；Low以实际 build output确认，不能混读旧输出。命令参数只有 mode、包路径与安全输出路径；不传连接到 argv。不能并行重建共享工具、同时运行依赖同一 Schema清理的probe。

validator成功只表示 O 公共七位写入与 frozen W literal decode通过，以及可用时 W/O合成 helper一致。下一步产品 family guard 与七位activation gate修改必须等 root审阅这些证据后另行派发，产生新候选并验证公共W写读；当前冻结R1包和旧失败证据保持原样。

## Round 2：O seed → W cross-read → O cleanup

Round 1 的 W CAST 字面量读回实际存库 oracle为零小数，不能据此推断 W decoder丢一tick。O/W pure helper的 `...C8000000` 在 byte5 中编码100ns（`0xC8 >> 1`），并非末字节为零就丢fraction。原 profile与validator的真实七位断言保留，旧失败证据不回写。

新受控模式接受四个非secret参数：`<seed|read|cleanup> <package.nupkg> <ownership.json> <phase-report.json>`。

- **O seed** 创建唯一 `TP7H_<nonce>` 表，先保存 CreateNew ownership manifest，绑定32位 nonce与精确表名、创建前absent、TEST身份、目标8.1.5.60及官方包/DLL/MVID。O公开 typed参数写ID1–4，并独立读取 server FF7/offset核实。ID101–104执行与 W原profile同一 CAST表达式，差异单列 literal characterization，不替代typed oracle、不算 Wdecoder通过。记录C2p/EscapeProcess安全数值、请求SQL和statement SQL哈希；不输出SQL正文。seed任一核心失败自动清理唯一表并新连接确认，成功状态仅为 `seed_ready`，明确`cleanup_required=true`。
- **W read** 只接受状态`ready_for_W_read`、nonce/name/身份/profile与官方固定包身份匹配的manifest，只读O typed ID1–4，核 typed/generic DateTime、DTO原tick/offset与GetString+EF Parse，不执行参数写入或去除gate。读取期间row count保持一致。
- **O cleanup** 使用同一 manifest的精确表名；仅在TEST和8.1.5.60上存在时DROP该表，另开新连接确认absence，再保存`cleanup_verified`状态。接受 seed_started以收尾宿主中断，以及已清理状态以重核；禁止任意表名/通配DROP。

Low必须在外层 finally 串行执行cleanup，即使seed/read命令失败或超时也执行；manifest未产生则不推测对象。成功seed不是长期fixture，必须在同一窗口结束前清理。seed/read记录相同ready manifest哈希，Low保存该哈希后交W，并验证源和包冻结没有漂移；cleanup后的manifest是本次生命周期收尾记录，旧round1所有制品保持原样。

```sh
# Low以固定10.0.203 host、同一TEST wrapper执行；路径为本次全新的safe artifact路径。
dotnet bin/Official/net10.0/TemporalPrecisionProbe.Official.dll seed <O-package> <ownership.json> <O-seed.json>
dotnet bin/Candidate/net10.0/TemporalPrecisionProbe.Candidate.dll read <W-package> <ownership.json> <W-read.json>
# 上述两步均须包在Low外层try/finally，finally必跑这一步。
dotnet bin/Official/net10.0/TemporalPrecisionProbe.Official.dll cleanup <O-package> <ownership.json> <O-cleanup.json>
python3 validate-handoff.py <O-seed.json> <W-read.json> <O-cleanup.json> <ownership.json> --output <handoff-validation.json>
```

新validator的decoder gate只使用 O typed原数据→W cross-read；O CAST literal精度差异单列表征。成功仍 `public_W_seven_digit_acceptance=false`，不升级为 W公共写入验收。独立helper比较同时解析位字段，明确期望100ns；纯helper一致只能支持codec布局，不能证明公共binding/gate已开放。

## Round 2 实际证据与限定候选修复

Low已运行完整 O seed → W cross-read → O cleanup，目标8.1.5.60、TEST身份均通过；[安全聚合结果](../../../.local/verification/t12/temporal-round2/validation.json)为 `cross_decode_verified`、issues=[]。原始 [O typed写入与CAST表征](../../../.local/verification/t12/temporal-round2/seed.json)、[frozen W读取](../../../.local/verification/t12/temporal-round2/read.json)、[独立连接清理](../../../.local/verification/t12/temporal-round2/cleanup.json)保留原样。

四个偏移的O公开参数七位写入、server FF7/offset、W typed/generic getter与EF GetString Parse均保存一tick及原偏移；O/W 9/11-byte helper输出相同且nanoseconds=100。相同CAST字面量在O也被服务器转换成零小数，此行为保留为literal characterization，不将它误归因于 W decoder，也不通过放宽原profile断言改写round1失败。

据此root仅批准新的产品候选：fixed绑定承认16/26、23/27同语义家族，且仅在可靠服务器版本精确为8.1.5.60时允许扩展26/27的scale7。null/未知/其他服务器版本仍拒绝七位；16/23与TIME的六位wire仍拒绝第七位，8/9位输入仍拒绝。原已证 encoder/reader不重写。

这些证据证明旧冻结W decoder和编码布局，**尚未证明新候选公共W七位参数写入通过**。修复后须Low冻结新源、完整驱动测试、唯一新包，再由新的EF suite验证公开路径。当前round2的`public_W_seven_digit_acceptance=false`与旧包/旧失败证据不回写。
