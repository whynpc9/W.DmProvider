# T11 隔离 EF 消费者

只从 EF clean commit `113014cc74dd1f751ef97226a78d2ec855b32c8c` 导出 O/W 两份；原 EF 仓库只读。本入口不读取其秘密文件、不运行 EF launcher、all/admin、dotnet ef 或管理员迁移测试。当前只覆盖 T11 SaveChanges 矩阵，完整 EF suite 和 R1 nupkg 交接仍属 T12。

`prepare.py snapshot` / `bind` 不执行 dotnet/DB。所有 build/run 由 verify_61 在唯一窗口执行，owner 源码 ready 后冻结。每次新候选使用新 output 目录；不覆盖同版本包或旧验收记录。

```sh
python3 tools/DownstreamAcceptance/prepare.py snapshot --output .local/t11/ef/<run>
python3 tools/DownstreamAcceptance/prepare.py bind --output .local/t11/ef/<run> \
  --candidate <candidate.nupkg> --candidate-sha256 <SHA256> \
  --version 0.1.0-t11.<UTCtimestamp> --driver-dll-sha256 <SHA256>
```

PackageId 固定 `W.DmProvider`、TFM `net10.0`；bind 校验 nuspec identity、nupkg hash 与包内 DLL hash，拷贝候选到 isolated local feed，记录确定性补丁。生产 EF src 的 `using Dm` / XML cref / PackageReference 和 central package 切到 W；EF 固定 `10.0.12`。SQL generator、SingleModificationCommandBatch、类型行为保持原输入；日期时间/TimeSpan GetString 路径保留。上游 tests/launcher 未切换，不能把原 tests 当 W suite 执行。

Low 的受控命令：

```sh
python3 tools/DownstreamAcceptance/run.py build --output .local/t11/ef/<run> --lane O
python3 tools/DownstreamAcceptance/run.py build --output .local/t11/ef/<run> --lane W
python3 tools/DownstreamAcceptance/run.py run --output .local/t11/ef/<run> --lane O --entry public
python3 tools/DownstreamAcceptance/run.py run --output .local/t11/ef/<run> --lane W --entry profile
# 能力门通过并生成新的公开入口候选后，用其新 snapshot 正常公开 Begin 复验：
python3 tools/DownstreamAcceptance/run.py run --output .local/t11/ef/<final-run> --lane W --entry public
```

SDK 用 `/Users/wanghongyi/.dotnet/dotnet` 且实际要求 `10.0.401`；每 lane 隔离 cli-home、NuGet cache、HTTP cache、bin/obj/feed/lockfiles。首次有界更新 lockfile 后 locked restore，再 no-restore build，全部单节点/关闭复用/关闭 build servers。构建不接收 DAMENG_TEST_CONNECTION_STRING；运行只能由 runner 调用 W `scripts/with-dameng-test.sh`，主机每次新连接查询 CURRENT_USER 并要求 WDM_PROVIDER_TEST，W typed builder 明确 PlaintextAllowed（当前共享测试实例）。不复制 secret 或给 O 共用字符串加 W 键。

每 lane 24 个案例：RC/RU/Serializable × 自动保存点开/关 × tracked UPDATE/INSERT × commit/rollback。开启与关闭都记录，不允许以关闭替代开启成功。每案例唯一表，EF 显式 UseTransaction；profile 模式仅反射 internal 单连接 `BeginProfileProbeTransaction(IsolationLevel)`，结果标实验；public 模式正常 BeginTransaction。调用 SaveChangesAsync 后检查影响行、生成键，关闭事务后独立连接确认行，再准确 DROP 本次对象并确认不存在。失败时显式 rollback，清理错误独立记录，不自动重试。

O 分类 OfficialCharacterization：RC 要成功，RU/Serializable 可以观察成功或旧 CommandText 特征失败，但都须 rollback/最终状态/清理确认；结果表示观察完整，不等于 W 正确性或隔离语义。W CandidateCorrectness 全部要成功且最终状态/清理准确。JSON 仅记录 SQL hash/长度、参数数量、statement owner/事务归属、安全类型/方法栈、合成对象名及状态布尔值；不记录 SQL正文、参数值或原始异常消息。数据库服务器 litmus 与 ADO causal matrix 由独立 IsolationProbe 负责，这个 EF 子集不能替代。

当前异常重试策略未为 W 扩大：消费者不启用 EnableRetryOnFailure。完整 T12 接入时须单独审查 Unknown outcome 的公开分类与 additionalErrorNumbers 优先拒绝；不能把现有官方通信错误码表无条件用于 W。

# T12 R1 下游验收

`t12_prepare.py` 在新的 `.local/t12/ef/<run>` 导出固定113014 commit/9dead archive，验证实际nupkg四identity，先应用CLI/currentSchema owner的有限patch，再应用W bridge，保留每份patch与hash。原EF仓库/T11历史只读。源码与candidate不可覆盖；修复/新候选要新snapshot。

```sh
python3 -B tools/DownstreamAcceptance/t12_prepare.py --output .local/t12/ef/<run> \
  --ef-repo <existing-EF-checkout> --candidate <actual.nupkg> \
  --candidate-sha256 <SHA256> --version <unique-t11-or-t12-version> --driver-dll-sha256 <SHA256>
# 可选 --patch <cli.patch> --patch <current-schema.patch>，必须先apply到clean官方源码再bridge。
```

EF路径也可用非秘密环境`DAMENG_T12_EF_REPO`；.NET host用`DAMENG_T12_DOTNET_HOST`覆盖本机默认。仍固定SDK10.0.401、EF10.0.12、准确commit/archivehash和实际包hash，不跟任意HEAD。driver包只能实际nupkg，EF没有driver ProjectReference。

R1 W重试guard只信`DmException.IsTransient==true`且非CommitUnknown；当前普通错误与CommitUnknown均false，不推荐重放。非空additionalErrorNumbers在builder和strategy构造明确NotSupportedException，null/empty保留；O旧number策略在原archive，未伪称兼容。

Low唯一实际窗口。首段可先build小audit与unit，立即反馈真实catalog/lock权限和离线正确性，再build完整普通functional/spec4slice：

```sh
python3 -B tools/DownstreamAcceptance/t12_run.py build --output .local/t12/ef/<run> --lane audit
python3 -B tools/DownstreamAcceptance/t12_run.py run --output .local/t12/ef/<run> --lane audit
python3 -B tools/DownstreamAcceptance/t12_run.py build --output .local/t12/ef/<run> --lane unit
python3 -B tools/DownstreamAcceptance/t12_run.py run --output .local/t12/ef/<run> --lane unit
python3 -B tools/DownstreamAcceptance/t12_run.py build --output .local/t12/ef/<run> --lane functional
python3 -B tools/DownstreamAcceptance/t12_run.py run --output .local/t12/ef/<run> --lane functional
python3 -B tools/DownstreamAcceptance/t12_run.py build --output .local/t12/ef/<run> --lane specification
python3 -B tools/DownstreamAcceptance/t12_run.py run --output .local/t12/ef/<run> --lane specification
```

每lane严格检查宿主exit、TRX total/executed/passed一致且failed/notExecuted为0、实际testhost加载DLLhash、candidate包/依赖锁和冻结source。DB只由runner调用TEST wrapper；typed builder在认证前拒绝其他user，服务器USER/currentSchema再核实。DB audit前后独立连接读取本TEST schema tables/views/sequences哈希集合，必须相等；差异只报告，不自动DROP未知对象。测试仍各自唯一对象/finally精确清理。

TRX全部16个counter必须存在；error/timeout/aborted/inconclusive/notRunnable等非通过字段全部为0。每次build/run结束重新核源、工具、实际依赖锁与运行二进制。`queries`是明确排除migration/reverse/CLI/script/probe后的先行lane，权限前置未满足时可独立验证；它通过仍不能称完整functional/R1 gate完成。

进程stdout/stderr只在内存接收，脱敏后才写log。TRX由VSTest写文件后扫描；实际敏感命中使该run失败并保留安全摘要，不能把后续脱敏说成秘密从未落盘。无命中只表示本次实际制品检查结果；CLI生成源另须no-onconfiguring和owned目录检查。

权限audit使用原EF history Exists读取SQL、唯一不存在history名，以及原MigrationLock固定20260723 Acquire/Release API；20秒command timeout+90秒整个host期限。失败记录准确code/type与阶段，不改SQL/skip/授SA权；原session取得的lock由其自身dispose释放。缺数据库时release gate失败。

首段排除admin用户/表空间fixture、SchemaCreationGuard、CLI/currentSchema后续lane和原DateTimeOffset CLR表示characterization；W改进isolation成功路径保持普通suite，autoSavepoints/SQL/batch不改。CLI/script ownerpatch合入新snapshot后才开放独立lane。fullsuite结果不证明R2真异步、取消、pool、streaming LOB；R1结束不进入T13。
