# 本仓库 agent 规则

## .NET 构建

- 运行 `dotnet` 前设置可写的任务目录 `DOTNET_CLI_HOME`，并设置 `DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1`、`DOTNET_CLI_TELEMETRY_OPTOUT=1`。
- 构建和测试使用单节点 `-m:1 /nodeReuse:false /p:UseSharedCompilation=false`；SDK 支持时添加 `--disable-build-servers`（.NET 6 不支持）。
- 如本地 IPC 报 `SocketException (13): Permission denied`，先关闭 build servers，再用上述参数重试。不要例行清空全局 NuGet 缓存。

## 本地达梦开发实例

- 本项目在现有达梦实例中使用两个专用账号及同名 Schema：`WDM_PROVIDER_TEST` 用于自动化集成测试，`WDM_PROVIDER_DEV` 用于手工开发探测。默认只使用 `WDM_PROVIDER_TEST`。
- 2026-09-30 经用户单独批准，TEST 另有 `SOI` 系统目录只读角色供 EF 迁移使用，`ADMIN_OPTION=N` 已由 TEST 新连接核实；DEV 未变。这不授权普通测试使用 SA，也不授予 DBA/ANY/其他业务 Schema 权限。限定维护见 [T12 目录访问](docs/implementation/maintenance/T12-test-catalog-access.md)。
- 本机连接信息只在被 Git 忽略的 `.local/secrets/dameng-test.env`、`dameng-dev.env`、`dameng-admin.env` 中。不要将连接串、口令、原始认证报文、含凭据的异常或环境变量输出到终端、日志、测试制品、Git 或对话。不要把本地 secrets 复制到 CI。
- 测试命令通过 `scripts/with-dameng-test.sh <command> ...` 加载唯一测试变量 `DAMENG_TEST_CONNECTION_STRING`。手工探测才显式加载 dev secret。正常开发、测试和 agent 任务不得加载 SA 管理凭据；新增用户或变更授权另行明确限定操作。
- 只对当前专用 Schema 内本次测试创建的对象进行清理。不得用 SA 运行测试，不得执行跨 Schema 的 DML/DDL、`DROP USER`、`DROP SCHEMA`、`DROP TABLESPACE` 或实例级设置变更。自动化测试使用唯一对象名并在失败时清理自己的对象；不要并行运行会互相删除固定对象名的测试。
- 每次真实库测试先确认连接身份为 `WDM_PROVIDER_TEST`，并记录测试命令、退出码、服务器侧最终状态。无显式测试连接时只跑离线测试，并报告 `integration_pending`。官方驱动、可编译恢复版和新驱动的测试结果分开记录。
- 新驱动的 TLS/明文模式必须显式配置并验证；不能通过关闭证书校验让测试通过。当前连接凭据的使用方式见 [本地达梦环境](docs/implementation/local-dameng.md)。
- T07 已接通严格 TLS；默认 `RequireTls` 对不满足策略的 STARTUP 模式在 LOGIN 前拒绝。原共享明文 TEST 的回归仍通过 typed W Builder 显式选择 `PlaintextAllowed`，不要把 W 专用键写进 O/R 共用 secret。
- T07 新增隔离本机实例 `wdm-provider-tls-t07`（仅 `127.0.0.1:15236`）；普通 TLS 测试使用 `scripts/with-dameng-tls-test.sh` 和 `.local/secrets/dameng-tls-test.env`，仍先验证 `WDM_PROVIDER_TEST` 身份。无 CRL 的本机 CA 测试显式 NoCheck；产品默认 Online 不变。详情见 [本机 TLS 环境](docs/implementation/local-dameng-tls.md)。
- 新增 `dameng-tls-bootstrap.env` 只供本机实例初始化工具使用，不供普通测试加载；不得替换或读取原 SA secret。模式矩阵只操作 manifest 精确锁定的本机容器 ID、镜像和 bind，结束必须恢复 mode 1 并实际登录确认；禁止据此变更共享远程实例。
- 证书/私钥和新 secret 只存 ignored 本地目录，私钥/secret 为 0600，父目录为 0700；日志不得输出凭据或密钥。macOS PFX 使用 .NET 临时磁盘 keychain，不宣称完全不落盘；不写入系统信任库。

## 实施分工与任务门槛

- 主 agent 负责设计、范围控制和 code review；后续使用 **GPT-6.1 Sol High** 负责产品及测试代码，**GPT-6.1 Sol Low**（对应用户的 light 档）执行构建和测试验证；Luna Max 负责生成合成测试数据。当前文件归属和窗口协调见 [active-ownership.md](docs/implementation/active-ownership.md)。
- 每次只派发有明确文件范围和验收条件的任务。共享工作区时禁止交叉修改对方负责的文件，保留其他未提交工作。修复由编码 agent 完成，再由验证 agent 复核，主 agent 审阅后才关闭任务。
- 构建和实库验收窗口由测试负责人统一安排，避免并行重建共享 bin/obj。宣布源码冻结后不得自行追加修改或重建；具体缺陷修复须通知独立验证者并更新相应证据。进入后续任务前按历史 evidence 哈希保存本地验收快照，不回写旧证据。
- [v1 规范](docs/implementation/v1/README.md)及其任务 DAG 是原始设计；实际进度以 [progress.json](docs/implementation/progress.json) 和对应任务报告为准。v1 中的初始 `not_started` 不表示已验收任务需要重新执行。
- 默认选择依赖已完成且 activation gate 已满足的最早未完成任务，不把来源固化、恢复编译和行为改进混作一次交付。
