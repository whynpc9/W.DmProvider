# R2 review 跟进

状态：**代码修复已完成，offline_verified / TLS verified / shared integration_pending**。root 已完成定点源码与独立证据审阅；不将新候选宣称为双profile完整验收。原R2候选验收保持历史身份，未提交/推送/发行，T15不启动。

[本轮安全汇总](../evidence/R2-review/validation-summary.json)、[离线计数](../evidence/R2-review/offline-summary.json)、[旧版复现](../evidence/R2-review/red-summary.json)、[新包来源](../evidence/R2-review/source-package-manifest.json) 和[源码清单](../evidence/R2-review/accepted-source-manifest.json) 可复核，接受制品位于 `.local/verification/r2-review/final-validation-v1/`。

## Review 处理

| 意见 | 修复/边界 |
| --- | --- |
| 异步send预算构造间隙到期漏记首因 | 原始Timeout也先用捕获invocation在session锁下记录TotalDeadline，再保留pre-send标记；Query/Prepare/Fetch未发时wire Dispose不拆会话；首User/Command不覆盖，部分发送仍Broken |
| Reader IsClosed先true而plan仍持有 | 保留生产getter表示物理失效/不可读的语义；补文档和4case：反复IsClosed不释放，命令mutation继续拒绝；无条件Close/Dispose后lease/plan才释放；陈旧CloseConnection reader不伤替换session |
| BEGIN后续阶段用旧configuration翻译 | sync/async按当前configuration/cleanup/activation child捕获；工厂前raw异常按原Timeout/OCE token分类，已有typed首因/Inner不替换；关闭前冻结目标txn最终Outcome，防实例清理后变null |
| CommitUnknown丢ServerErrorNumber | immutable转换复制ServerErrorNumber与CancelSource，固定Commit/Unknown/code/不可复用；缺号仍null，不新增server恢复白名单 |
| 设计状态过期 | T14-design更新为原R2已验收、本轮另见review状态，补Reader生命周期说明 |

OperationOutcome描述失败child发送边界；activation未发送可为NotSent，但BEGIN之前已发送时TransactionOutcome仍Unknown、ConnectionReusable=false，不是重试证明。服务器号测试为合成单位metadata，不称线上协议特征。

## 独立验证

| 门 | 实际结果 |
| --- | --- |
| 旧版 red（新隔离副本） | Transport6：2pass/4fail（3类未发wire误Broken、partial-send漏登记）；FailureInfo10：5pass/5fail（supplied号丢失，missing号正确） |
| 新版 green | SDK10.0.203，九套603/603，零非通过项；新增Transport6、FailureInfo10、Reader4、BEGIN8全部通过 |
| pack / source / exact PackageReference / API26 | 全exit0，源286项，包内/实际加载DLL/hash/MVID一致 |
| 独立TLS8.1.4.6 | T13业务、未知CA拒绝、T14 8功能case三门exit0，TEST身份/Schema，业务对象fresh不存在，安全无对象创建 |
| shared8.1.5.60 | 新候选两轮Fetch超时，旧accepted同工具/同2048×1024负载同预算也超时；不归因新修改，不标通过；security-shared/T14shared未执行 |
| 三轮独立维护 | 每次TEST身份核验、仅exact本轮随机表DROP、freshCount0；恢复exit0，不回写原exit1/finalunverified |

所有构建为可写CLI_HOME/single-node/noReuse/noShared/disable-build-servers，High不dotnet/DB，Low独占窗口。任何失败先stop/释放后High定点修改/newfreeze。没有读取SA/DEV或更改模式、权限、驱动/进程预算，没有干预其他dotnet进程。

新候选 `0.1.0-r2.review.20261002092837`；nupkg SHA `462ad2d275ce4745184809a14c70264b2f96d371a8da02cdd775e8deb00f1abb`，DLL SHA `c88913339fb373d586c9502b426df448b22d3031995a254348fa9d68c19a895a`，MVID `834905ee-d786-42f9-a69b-2e0089b0ac55`。产品286项，source SHA `b17bea440ccac4103a24ee774aef2d4009cdd42df3d972081a343f4d45d3591f`；working_tree身份。

549项最终输入和618项接受制品由root逐项核对，75项历史evidence未变。focused生产diff保存在 `.local/verification/r2-review/scoped-production.diff`；原R2全部未提交改动保留。

## 保留的失败与当前限制

第一次green603/595/8失败：BEGIN关闭会话清空instance.Transaction，已知Unknown在翻译时丢为null。第二版仅DmConnection保存关闭前target/finalOutcome，8个原断言不变；source-v1/red/首次green log/TRX不回写。

shared新候选第一次3个Fetch后Receive超时、第二次1个Fetch后超时；cleanup EndOfStream/finalunverified保持。旧accepted同负载对照也在跨页读取超时（0Fetch、已返回初始rowset）。三次随机对象分别单列 `recovery-v1/v2/v3`，原失败不是通过。当前没有本轮待清理对象，所有窗口释放。

需在共享实例恢复可满足原调用预算后完成新候选shared门；本轮没有通过提高deadline、减少行数或重试次数来代替验收。TLS和离线证据已完成，但不取代shared/PER实证。
