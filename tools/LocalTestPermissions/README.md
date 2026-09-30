# 限定 TEST 目录查询角色维护工具

当前状态：用户已批准一次固定授权，Low 已确认 GRANT ACK、角色存在及管理员连接关闭；原工具只认 NO，因本数据库实际返回字符串 N 而拒绝后置条件。随后普通 TEST 只读 inspect 确认 USER/schema、恰好一个 SOI 角色记录、ADMIN_OPTION=N 和关闭连接；root 已接受固定 GRANT 无 ADMIN OPTION 子句及此读回证据。原失败后置证据保留，**不得重复管理员连接、GRANT 或 REVOKE**。工具现仅按已证表示识别 N 和原有 NO，YES/Y 及未知值仍拒绝。`--approval-granted` 是原维护执行选择器，不能代替新授权；原 execute 模式本次不可重跑。

唯一写操作固定为：

```sql
GRANT SOI TO "WDM_PROVIDER_TEST"
```

没有 ADMIN OPTION，不接受 SQL、账号、角色、连接串或 secret 路径参数，不授予 DBA/ANY，不改默认角色、DEV、其他用户或实例配置。任何失败直接返回安全结果，不扩大权限、不自动重连/重放；本工具没有 REVOKE 模式。

## 构建（不读取 secret）

```text
python3 tools/LocalTestPermissions/run.py build
```

项目使用仓库 W.DmProvider 的 ProjectReference。构建输入必须处于 Low 控制的冻结窗口；构建 .NET 10 使用临时可写 DOTNET_CLI_HOME、关闭首次体验/遥测、单节点及禁用 build servers。输出在 ignored `.local/maintenance/LocalTestPermissions/`；manifest 保存四个工具源文件与工具/驱动 DLL、deps/runtimeconfig 的 SHA256。没有构建即不能执行，输入/制品变化后必须重新构建。

build 不读取或检查 `.local/secrets/dameng-admin.env`，不将任何 DAMENG 环境变量传给 build 子进程。工具尚未由本轮编码 agent 构建或执行。

## 批准后的限定执行

下面是原批准维护模式的记录，本次不得重跑：

```text
python3 tools/LocalTestPermissions/run.py execute --approval-granted
```

仅 execute 能打开固定 ignored `.local/secrets/dameng-admin.env`。拒绝文件及本项目路径中的 symlink；文件必须是当前用户拥有的 regular file、0600，直接父目录必须当前用户拥有且0700，并且 `git check-ignore` 确认未跟踪且被忽略。用 no-follow directory/file descriptor 检查后读取，限定64KiB；不自动修改权限。

文件按 shlex 数据解析，只接受单个 `DAMENG_ADMIN_CONNECTION_STRING=...` 赋值，可带 `export` 和注释。值中的引号与转义按 shlex 处理；没有 source/eval、shell、变量替换、命令替换或反引号求值。不读取/复制 TEST、DEV 或 bootstrap secret。连接串只传给 child 的 `WDM_PROVIDER_MAINTENANCE_CONNECTION_STRING` 私有环境，C# 读取后清除；不写命令行、构建 manifest 或日志。

C# 用 typed builder 确认配置用户 SA，再用实际 `SELECT USER FROM DUAL` 确认 SA；ConnectTimeout/ReadIdleTimeout/CommandTimeout 均有限20秒，CleanupTimeout5秒。共享开发实例显式使用 PlaintextAllowed；不关闭 TLS 证书校验，不写共享 secret 新键。不使用事务、不改变会话默认角色或隔离配置。

连接后先只读 `DBA_ROLE_PRIVS` 的固定 GRANTEE=`WDM_PROVIDER_TEST`、GRANTED_ROLE=`SOI`。仅角色不存在时执行固定 GRANT，再读回要求恰好一个角色记录且 ADMIN_OPTION 为已证字符串 `N` 或兼容的 `NO`。已有无 ADMIN OPTION 的 SOI 是幂等只读成功；YES/Y、未知值或多行直接拒绝，不试图覆盖/撤销已有权限。管理员连接随后关闭。此实现收口不授权再次执行原管理员路径。

输出仅包含固定角色、结果 code、异常 type/数值 provider code 和 proof 布尔值；不输出 Message、Data、连接串、用户 SQL、原始认证、环境值或子进程原始 stderr/stdout。执行有120秒总进程上限；超时/断链不能证明 GRANT 未发生，失败结果仍须 root 安排后续独立状态核验，不扩大本次范围。

成功维护后，普通验收须回到新 TEST 连接，重新核身份、历史目录查询、迁移锁及完整相关验收。SOI 的实际权限范围和本次批准依据见维护文档；工具输出的 grant proof 不代替 TEST 业务验收。

## 普通 TEST 只读 inspect

先由 Low 在统一构建窗口重建工具及 manifest，再只执行：

```text
scripts/with-dameng-test.sh python3 tools/LocalTestPermissions/run.py inspect
```

inspect 的 Python 分支只取 wrapper 的唯一 `DAMENG_TEST_CONNECTION_STRING`，不打开、读取或 stat 管理员 secret，也不调用管理员解析函数。它将连接值传给 child 私有 `WDM_PROVIDER_TEST_INSPECTION_CONNECTION_STRING`；C# 选择独立的 `inspect-soi-as-test` 方法，不读维护环境变量。typed builder、实际 USER 与 CURRENT_SCHEMA 都必须是 WDM_PROVIDER_TEST，连接/命令20秒、清理5秒。

除两条身份 SELECT 外，唯一查询固定为 `SELECT ADMIN_OPTION FROM USER_ROLE_PRIVS WHERE GRANTED_ROLE = 'SOI'`。不接收 SQL、账号或查询参数，不授予/撤销任何权限。最多记录16行；超出时记录有界 observed count 并标记 count 不完整，失败关闭 TEST 连接。

输出 declared field CLR type、各 flag 的实际 CLR type、行数及完整计数 proof。直接输出仅 bool、32位有界整数（byte/short 可无损拓宽）或长度不超过8且严格属于 NO/YES/N/Y/0/1/是/否的字符串；未知值只输出类型与 SHA256，没有原值或 Message。不把任何 flag 推断成无 ADMIN OPTION，未知类型没有许可放宽；后续规范化仅在 root 审阅实际证据后定点修改。inspection_completed 仅表示只读观察及关闭成功，不表示权限后置条件已验收。
