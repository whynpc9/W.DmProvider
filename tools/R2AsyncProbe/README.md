# T13 实际包异步 consumer

此项目只有精确 `PackageReference`，无产品 `ProjectReference`、官方驱动引用或 DLL HintPath。必须传 `R2PackageVersion` 和本地 `R2PackageSource`。运行先匹配 nupkg/loaded DLL SHA、assembly version/MVID 和 deps 的 package 类型，再检查公开异步 API 的 provider override，避免继承 ADO.NET 默认同步 fallback。

由唯一 Low 验证负责人执行 [eng/t13.sh](../../eng/t13.sh)。输入为冻结的实际 nupkg，输出落在新的 ignored run 目录，consumer 的 bin/obj/cache 均独立。该 runner 不打包产品、不运行其他 tests。完整 T13 门槛还需 owner 的 async-only fake transport barrier 测试和 root 调用链审计，实际数据库完成结果不单独证明所有等待均不占同步 worker。

`offline` 不加载连接变量。`shared` 只通过 TEST wrapper，显式 `PlaintextAllowed` 并验证 STARTUP mode 0。`tls` 只通过独立本机 TLS wrapper，锁定 127.0.0.1:15236、`RequireTls`，固定现有本机 CA/client cert、显式本机无 CRL 的 `NoCheck` 并验证 mode 1。不关闭证书校验，不读取 admin/DEV/bootstrap secret，不改容器或服务器设置。

真实路径全部 await：Open、Prepare、ExecuteNonQuery/Scalar/Reader、Read、GetFieldValue/IsDBNull、NextResult、Commit/Rollback/Save/Release、Close 和 Dispose。2048 行各 1024 字符使用默认 BufPrefetch（初始 0，由 LOGIN 协商服务器值），在 ReadAsync 阶段必须实际观察 Fetch opcode 7；仅缓存首批行会失败。若服务器默认缓存覆盖全部数据，应扩大逻辑数据规模后重新冻结，不修改产品配置策略或弱化 Fetch 断言。双 SELECT 复用 T08 已验证语句形状，并必须观察 MORE_RESULT opcode 44。256 KiB BLOB 与 163840 UTF-16 units CLOB 使用异步参数上传、字段/Scalar物化，必须观察 GET_LOB_DATA opcode 32。此处不宣称 R3 流式 LOB 支持。

每轮只创建 `T13_<随机24位>` 同一 Schema 表。提交/回滚/保存点/Dispose 回滚都由新 TEST 连接核验行结果；总流程 finally 在新 TEST 连接按单一名称检查并精确 DROP，再由另一新连接确认 absent。异常只报告 CLR 类型、固定安全分类和 Number，绝不输出 message/stack、SQL、连接串、认证数据或环境变量。

保存点验收按实际 `ServerVersion` 分支：`8.1.5.60` 必须支持 Save/Rollback-to/Release 并满足新连接保留 1 行、回滚 0 行的成功断言；独立 TLS 的 `8.1.4.6` 必须报告 `unsupported_for_server_profile`，分别验证 SaveAsync、RollbackAsync(name)、ReleaseAsync 精确抛出 `System.NotSupportedException`、每次新增 frame 为 0、事务仍 Active 且连接 Open，随后整事务 Rollback/Dispose 并由新连接确认候选行与回滚行均 0。其他 profile 拒绝验收。两条路径均保留全部 13 项 case，继续完成 Dispose rollback、LOB 与最终清理；`package_async_verified` 表示该 profile 的契约验收，保存点是否支持以单独 `savepoint_status` 为准，明确拒绝不计为保存点成功或 skip。

运行形式：`R2AsyncProbe.dll offline|shared|tls <repo-root> <package-manifest.json> <safe-report.json>`。不通过 runner 时调用者仍须遵守仓库构建环境/唯一 DB 窗口，并确保 manifest 来自同一实际包。

独立安全模式 `security-shared|security-tls` 沿用同一参数和精确包，分别验证 RequireTls 遇实际 mode 0 在 LOGIN 前精确拒绝，以及实际 TLS mode 1 遇无关 CA 认证失败并关闭。每次负向前后用正确策略和原本有效证书建立新 TEST 身份连接；`AfterHandshakeExchangeEntered` 由 `HandshakeFrameEncoded` 调用，正控制必须记录 STARTUP 200 与 LOGIN 1，负向必须仅有 200、LOGIN 编码次数 0。还要求单 socket 创建/释放、TLS 成功升级 0，TLS 失败计数 1；不创建数据库对象、不改变容器/实例模式、不重跑正向 13 项 case。安全异常只记录精确 CLR 类型与固定分类，不输出 message/inner/stack。无关 CA 的 RSA 私钥只在内存，公开证书文件为 0600、父目录 0700，finally 删除；原本客户端私钥按既有显式路径由产品读取，不复制、不导出、不写系统信任库。
