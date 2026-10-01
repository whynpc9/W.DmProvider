# 当前状态：R1 main review 已独立验收（T13 不启动）

本轮用户指令：处理 main `95a71bc` 上的 R1 review；具体文件归属及验证窗口见文末 2026-10-01 跟进。原 R1 收口与 PR #1 合并已完成；T13 不启动。

T01–T12 已验收；T11 最终 443 项输入与二进制已保存 `.local/t11/accepted-source/` 和 `accepted-binaries/`，旧证据不回写。用户指定 GPT-6.1 Sol High 编码、Sol Low 实际测试；root 负责设计和 review，Luna Max 仍只在明确需要合成向量时派发。

| agent | 当前角色 | 唯一写入范围 |
| --- | --- | --- |
| root | 设计、review、证据/进度 | docs/implementation、兼容性和根入口文档 |
| downstream_prep_61（High） | T12 主接入/runner | tools/DownstreamAcceptance/t12*、自身 bridge patch、T12 README；新的隔离 EF archive |
| codec_61 / harness_61（High） | CLI 补丁已冻结 | codec 初版安全接入；harness 最终固定 host 与设计时模型检查 |
| transaction_61（High） | 当前 Schema 脚本 fixture | 自己的 .local/t12/script-work 下新增测试/helper；T12/patches/current-schema-scripts.patch 与说明 |
| binding_61（High） | 产品保留 owner | 暂无新写入；具体失败由 root 定点派发 |
| harness_61（High） | R1 离线 CI / release guard | .github/workflows/r1-offline.yml、eng/t12-offline.sh、eng/T12.md、必要 tools/R1ReleaseAudit/；T11 全部冻结 |
| verify_61（Low） | 唯一 build/test/DB 执行 | ignored 验证输出/快照；不修改实现或弱化测试断言 |

## 规则

- 方案见 [T12-design.md](T12-design.md)。原 EF 工作区和所有历史 archive/evidence 只读；在 clean pinned archive 先应用各自 patch，再统一 W bridge。具体路径/接口先与 main downstream agent 对齐，禁止交叉修改。
- High 不执行 dotnet/DB；阶段初段 ready 可单独新快照进入 Low 窗口，后续 patch 在另一新副本整合，不能修改冻结输入。新功能缺陷修复必须先释放实际验证窗口，再重新冻结新源/包；旧失败不算通过。
- Low 控制单一构建/数据库窗口，全部 dotnet 设置可写 cli-home 与单节点/noReuse/noSharedCompilation/disable-build-servers。无显式 TEST 时 release gate 失败，不把skip算pass。
- 仅TEST wrapper/唯一对象/精确清理，新连接验证身份和最终状态。无 SA、无原EF all/admin、无跨Schema DDL/DML、无全Schema清理。秘密不进入patch/源码/配置/日志/制品。权限不足如实记录，不静默提权。
- 每次 candidate 版本唯一，manifest/package内/实际加载DLL hash一致。原官方特征与W正确契约分别分类。发行不在范围；用户已授权R1通过后由root提交/推送和更新现有PR，工作agent不得自行执行。
- 已结束的agent通过 followup_task 启动新任务，send_message 不会唤醒已结束任务。T13尚未激活，其 .local 准备文档不代表实现或通过。

## R1 收口（历史）

最终 CLI 已独立通过，所有运行输入冻结；root 仅完成证据/文档和 Git/PR 收口。没有新的编码任务，T13 不启动。后续实施必须由用户再次指示。

## 2026-09-30：PR #1 review 修复窗口

用户要求处理 review；范围限定 R1 既有行为，T13 仍未启动。旧 T12 源码/包/证据已按哈希保存在 `.local/verification/r1-review/historical-baseline/`，不会回写。

| agent | 唯一写入范围 |
| --- | --- |
| review_lexer（Sol High） | DmParameterBinding.cs、RollbackToSqlTests.cs、新 SqlCommentReviewTests.cs |
| review_reader（Sol High） | DmDataReader.cs、新 ReaderReviewTests.cs、tools/R1ReviewProbe/ |
| review_detach（Sol High） | DmCommand.cs、新 ConnectionDetachReviewTests.cs |
| review_data（Luna Max） | tests/fixtures/r1-review/ 合成逻辑输入 |
| review_verify（Sol Low） | ignored 验证输出；唯一 build/test/DB 窗口 |
| root | 设计、review、公共证据/文档、产品 README 和 Git/PR 收口 |

各编码者冻结后才由 Low 验证；失败回交对应 owner。只有当前 TEST 身份、唯一对象和精确清理；无 SA 或实例设置变更。新候选使用唯一版本，旧候选的缺陷复现与新候选的正确性结果分开。

## 2026-10-01：main `95a71bc` review 跟进

PR #1 已于 2026-09-30 合并；本轮用户授权修复现有 R1 review 五项意见，不进入 T13、不发行。root 保存 `95a71bc` archive 和 48 项历史 evidence SHA 于 `.local/verification/r1-review-followup/historical-baseline/`；旧 T12/R1-review 候选及证据保持只读。

| agent | 唯一写入范围 |
| --- | --- |
| lob_null（GPT-6.1 Sol High） | DmDataReader、DmGetValue、DmBlob/DmClob/AbstractLob、新 LOB guard、TypeTests review tests、R1ReviewProbe/Program.cs |
| savepoints（GPT-6.1 Sol High） | DmTransaction、TransactionTests、IsolationTests 的保存点行为测试 |
| tls（GPT-6.1 Sol High） | DmTransport、SecurityTests 的 TLS tests/harness |
| verify（GPT-6.1 Sol Low） | 唯一 build/test/DB 窗口；新 ignored 验证脚本/制品，不改源码 |
| root | 设计、review、docs/implementation、兼容矩阵、根入口 README |

所有 High 冻结并经 root review 后才进入 Low 窗口；任何修复先通知 Low，停止旧窗口，再重新冻结。无需新的合成 fixture，故本轮不派发 Luna。进度与新证据见 [本轮报告](reports/R1-review-followup.md)。

本轮最终独立验收完成，唯一构建/实库窗口已释放；源码、测试和二进制冻结，后续 agent 不再修改或重建。当前工作树待用户审阅；T13 不启动，未发行。详细结果及失败保留见 [R1 main review 报告](reports/R1-review-followup.md)。
