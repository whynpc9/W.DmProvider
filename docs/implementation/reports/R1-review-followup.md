# R1 main review 跟进

状态：integration_verified；本轮限定 R1 修复已完成独立验收。基线 `95a71bcfd7c31c292f5aaaf255d20d24c5056cbf`；本轮工作树修复尚未提交。PR #1 [已合并](https://github.com/whynpc9/W.DmProvider/pull/1)，合并时间 2026-09-30T15:44:49Z。本轮处理 R1 既有边界，T13 不启动，NuGet 未发行。

原 [T12](T12.md) 候选 `0.1.0-r1.20260930064454` 与 [PR review](R1-review.md) 候选 `0.1.0-r1.20260930151754` 继续保留历史身份、结果及证据。

## 当前修复候选

| 身份 | 值 |
| --- | --- |
| 版本 | `0.1.0-r1.20261001064256`，net10.0 |
| nupkg SHA-256 | `abfcf66245b3740657c009b95e5db1a9b78c467cc2a42a8035c0adda94de0002` |
| DLL SHA-256 | `9a0f9273ceac91dcb593f07467c71514e495eb9fb38c2d7f5829e0ac0f5fdb29` |
| MVID | `fb09ad56-9958-4a36-a9bc-f605bdb03497` |
| 产品来源 | 279 项，SHA `b35a64b86a34fac7d6fc5b6dd7534e5084a911a453049004c52e825435b9025c` |
| 公开 API SHA | `f914653da6908a767a3760e6b7470cefeffdbfc62b539e48c7689a2b30cb4bbd`，签名未变 |

包在未提交工作树构建，SourceLink=false；逐文件 source hash 追溯，不声称包包含新的交付 commit。产品冻结后只修订包 probe 的诊断与契约，沿用同一实际 DLL，未重建或重包。

## Review 处理

| 问题 | 最终契约 |
| --- | --- |
| LOB 上限未接读取 | CLOB/BLOB 全量读取、长度探测、inline/fetchAll 和 hex 转换接入固定 64 MiB payload 上限，超限拒绝，不截断 |
| raw SQL 可触及内部保存点 | 拒绝用户触及 WSP_ 服务器命名空间；允许单条 EF/用户保存点，确认执行后维护 API/raw 有序栈 |
| typed NULL 异常不一致 | 沿用 DmException 6081，包含 Guid、string、generic 和 DmDecimal；DBNull 对象读取保留，SequentialAccess 单次取值 |
| 合并前状态过时 | release-gates 记录 R0/R1 限定验收及后续门未启动；兼容矩阵、规范入口和进度记录 PR #1 已合并及本轮状态 |
| TLS 原因丢失 | 外层安全固定消息保留，原 AuthenticationException/IOException 作为 InnerException；TLS 策略和失败关闭不变 |

### LOB 和 NULL

64 MiB 为 67,108,864 字节，包含边界值。BLOB 按 byte[] 长度，CLOB 按 UTF-16 payload（2×CLR char），BLOB hex string 按 4×原始字节计；超限抛 NotSupportedException。frame、StringBuilder、临时副本及对象头不计入这个值大小限额，不能据此宣称进程总内存只有 64 MiB。

inline CLOB 先校验编码长度不超过可用 payload，用不分配 string 的 GetCharCount 精确检查，再解码。定位符 CLOB 的已知长度保守早检；按原 Data.len 推进，每块在解码/append 前累计检查 UTF-16 大小，每次请求同时遵守固定 chunk 与服务器 MaxLobDataLenPerMsg。普通 GetChars 小片段可以读取大字段，null-buffer 长度探测仍需整段物化并受限。这不是 R3 流式 API。

GetString 的 SQL NULL 也改为 6081，与 GetFieldValue<string> 一致；空字符串另测。GetValue/GetFieldValue<object>/GetFieldValue<DBNull> 仍返回 DBNull。generic getter 不预消费列位置；仅 DmDecimal 分支在单次 provider-specific 取值后处理 DBNull。

### 保存点

仅精确 8.1.5.60 开放严格单条 `SAVEPOINT name`、`ROLLBACK TO [SAVEPOINT] name`、`RELEASE SAVEPOINT name`，支持双引号、转义、非嵌套注释和可选末尾分号；SQL 原文不改写。解码名称以 WSP_ 开头（OrdinalIgnoreCase）则发送前拒绝。完整 COMMIT/ROLLBACK、额外 token、复合保存点控制、参数化标识符及 bare RELEASE 继续拒绝。

API/raw 共用有序栈，API 查找只匹配 API 点。成功 EXEC 返回后、结果导航前确认 raw 操作；Prepare、执行失败、缓存和内部 borrowed 控制不登记。raw 回滚保留目标并删除后继，raw 释放删除目标及后继，其中包括 API 点；API 回滚/释放同样删除后继 raw 点。大小写/引号差异、多重匹配或成功目标未知时保守清空，不猜服务器 collation；仍存在的服务器点也可能被客户端失效。精确 decoded-name+quote 匹配保持精确跟踪。总栈及 API 生命周期序号分别有 128 上限，发送前容量检查只读。

普通服务器错误继续沿用 T10 的 fail-closed：当前完整恢复白名单仅验证 `8.1.5.60 / -2106`。真实缺失保存点 `-2121` 为非瞬态，关闭连接、OutcomeUnknown，旧连接/事务操作客户端拒绝；独立新连接确认未提交行已回滚。未扩错误恢复范围，也未把不确定结果标为可重试。

## 独立验证

[安全验证汇总](../evidence/R1-review-followup/validation-summary.json) 包含实际命令、完整计数、加载 DLL、来源及最终状态；[实际包 probe](../evidence/R1-review-followup/fixed-probe.json) 记录 19 项行为。所有 lane 全部执行完成，零 skip/abort/timeout；原失败轮单独保留。

| 门 | 退出码 / 结果 |
| --- | --- |
| 八套 Release 离线 | 0；421/421，Type 142、Transaction 88，零非通过项 |
| 实际包、API、source/SBOM 审计 | 0；精确 PackageReference consumer 与来源匹配 |
| 无 TEST 变量 release-environment | 66（预期拒绝）；不冒充实库成功 |
| 最终实际 PackageReference probe | 0；19/19，TEST 身份、精确 DLL、最终表数量 0 |
| 固定 EF unit | 0；307/307 |
| 固定 EF ordinary functional | 0；76/76，原五项保存点/事务用例全部通过，独立 inventory 0→0 |
| 当前 schema scripts / specification 切片 / CLI | 0；4/4、4/4、1/1，各 lane 独立 inventory 0→0 |
| 修改过的 Transaction/Isolation runner | 0；quoted-savepoint 通过、public matrix 36 项及 strict validator 通过，实际同 DLL、对象精确清理通过 |

EF 固定 commit `113014cc74dd1f751ef97226a78d2ec855b32c8c`、EF Core 10.0.12、SDK 10.0.401；新隔离 archive 保留原 SQL 和断言，原 EF 工作区只读。driver SDK 10.0.203，主 profile 8.1.5.60、gb18030、WDM_PROVIDER_TEST、本机 macOS arm64。四项 specification 是现有切片，不是完整 EF conformance；Async 同步 fallback、无池、无完整流式 LOB及其他版本/OS/RID边界不变。

## 历史失败与有限维护

全部失败轮保留在 `.local/verification/r1-review-followup/`，不覆盖旧候选及原 T12/R1-review evidence。

| 原轮次 | 真实结果与处理 |
| --- | --- |
| `0.1.0-r1.20261001054517` | Type 134 中 130 pass、4 fail，另七套 238 pass；整体退出 1，未打包/实库。合成 long-row 长度错写偏移 47，解析器及原 net8/net9 实际读取 39；仅修 fixture并补实际解码断言，保留超限拒绝测试 |
| `.20261001055007` 首包 probe | 15/16；旧 TransactionGuard setup raw SAVEPOINT 被新门拒绝，改为 API setup，产品未变 |
| 同包 EF functional | 71/76，5 项均在 EF CreateSavepointAsync 被全部拒绝策略挡住；自然退出 1、无超时。改为保留内部命名空间并同步栈，另修补审确认的 generic NULL 顺序预消费回归；新产品版本重验 |
| `.20261001064256` v4/v5 probe | 16/18、17/18；未知保存点错误的“仍可 Active”强预期不符合既有恢复门。独立诊断确认 -2121/Closed/Unknown/旧租约拒绝/fresh rows 0；最终 v6 精确验证 fail-closed，19/19，无产品策略放宽 |
| v6 整轮 900 秒 | 124、无 TRX，不能算通过；最终 inventory 0→1。指定 DateBucket 表经身份/哈希/列结构复核，只删 `EF10_DB_5892FFF511AD` 一次，独立最终三目录为 0 |
| 错误父进程预算 wrapper | 未覆盖 test worker，按 owned PID/load record/starttime 证明后，仅停止自己的 PGID 40018；无 TRX，不计产品失败或通过。指定 Aggregate 表复核后只删 `EF10_AGF_EF3B20249307` 一次，独立最终为 0 |

两次清理只针对当前 TEST、本次随机对象，未使用 SA、跨 Schema、CASCADE 或实例变更；原超时/失败审计不改为通过，清理另记维护证据。

原已验收功能运行为 202.489275 秒，早期完整拒绝轮为 892.941776 秒；三个 Aggregate 用例从 104.504646 秒变为 507.144989 秒，每个 fixture 至少有 400 次顺序 INSERT。同环境旧/新实际 DLL 的只读对照各 25 次新物理连接，总耗时 17.815/17.734 秒，SELECT 均值 226.51/228.27 ms，此范围无明显退化，不等于完整性能或 EF 验收。验证器 ordinary functional 的整轮预算校准为 1800 秒；仅 ignored wrapper 路由指定 test worker 并覆盖 exact command/filter 的 900 秒，单 SQL 20 秒、原 before/after 90 秒、源码、断言及其他 lane 预算不变。实际覆盖必须在 dotnet dispatch 前记录 original/wrapper SHA、具体 argv 和 1800 秒预算。

## 分工和冻结

Sol High 负责 LOB/NULL、保存点、TLS，Sol Low 独占 build/test/DB，root 设计及 review。历史 main archive SHA `5de6076c9c0632f9e18382e3df1eb3f8cf7e34f7dbe921affdb13182c6761719`、48 项历史 evidence SHA 保存于 `historical-baseline/`。最终产品、测试与工具 493 项冻结于 `frozen-source-v4/manifest.json`（SHA `9736fcc12bf14b290fe3dc58be76023943b86e048c500a50ec46beb87e69a971`）；只有 probe Program 通过后续单独冻结覆盖，最终 SHA `f287c53e6fe516af740fd5326301d909f604f63e712543aca9f3c60aef4a32bc`，产品输入不变。

完整 ordinary functional 在校准后约 907 秒自然结束，76/76、零非通过项，全部此前五个事务/保存点用例 Passed；TEST 身份、current Schema、独立最后 inventory 0→0 均通过。此前 900 秒退出 124 与路由错误中止仍保留，不改写。该结果证明完整门通过及旧整轮预算不足，不作为普遍性能或生产吞吐量声明。

最终独立 TEST 新连接再次确认三目录为空、inventory SHA `4f53cda18c2baa0c0354bb5f9a3ecbe5ed12ab4d8e11ba873c2f11161202b945`，本次终验只读、DDL 数量 0。78 项可报告制品扫描通过，493 项冻结输入及唯一 probe overlay 对应，48 项历史 evidence SHA 未变；独立接受源码/二进制已保存 `.local/verification/r1-review-followup/accepted-source/` 与 `accepted-binaries/`。验证窗口已释放，T13 仍未启动，Git 提交/推送及包发行不属于本轮交付。
