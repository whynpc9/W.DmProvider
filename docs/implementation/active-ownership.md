# 当前状态：R2 review 定点修复；原 R2 已验收、旧输入只读

本轮用户指令：实现 R2。基线 `6b3d1796dfdc207ec5c1949f1998ca0df4e9697c` 工作树干净，R1 已验收。本轮新归属见文末，历史窗口均已结束。

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

## 2026-10-01：R2（T13 → T14）新窗口

用户「实现R2」激活 T13，T14 仅在 T13 独立验收后推进；T15 及后续任务不进入本轮。历史基线 archive 和 54 项 evidence SHA 已保存于 `.local/verification/r2/historical-baseline/manifest.json`，旧证据只读。

| agent | 角色与唯一写入范围 |
| --- | --- |
| root | 设计、review、docs/implementation、兼容性与根入口文档 |
| r2_transport（GPT-6.1 Sol High） | Internal/Transport、Internal/Sessions、DmFrameReader；TransportTests async/兼容 fake、SecurityTests 新 AsyncTlsValidationTests |
| r2_protocol（GPT-6.1 Sol High） | Internal/Legacy/A、DmConnInstance、EP/EPGroup、B 网络 LOB/cache async 核心、DriverUtil 必要 helper |
| r2_command（GPT-6.1 Sol High） | DmCommand、DmDataReader、DmGetValue、Internal/Execution、新 AsyncTests project 与 Command/Reader tests、项目 friend assembly |
| r2_connection（GPT-6.1 Sol High） | DmConnection、DmTransaction、新 Connection/Transaction async tests |
| r2_lob（GPT-6.1 Sol High） | AbstractLob、DmBlob、DmClob 与新 Lob async tests |
| r2_probe（GPT-6.1 Sol High） | tools/R2AsyncProbe、eng/t13.sh、eng/T13.md 独立包 consumer |
| r2_data（Luna Max） | tests/fixtures/async-r2 逻辑调度场景 |
| r2_verify（GPT-6.1 Sol Low） | 唯一 build/test/DB 窗口，仅 ignored 新验证脚本和制品 |

编码期间无 dotnet/DB 执行，所有 owner 完成并经 root review 后由 Low 验证；失败归还 High 修复并重新冻结。异步调用链不得 Task.Run、同步网络 fallback 或猜原生取消 opcode。保留 TEST/TLS 显式策略及 R1 事务/安全边界。源码/API/结果未验证前不得声明 R2 通过。Orca 协调请求返回 no_active_sender_terminal，本轮通过 T3 会话协作 agent 协调；不冒用其他终端身份。

T13 第一轮 515 项输入已整体冻结：`.local/verification/r2/t13/frozen-source-v1/manifest.json`，SHA `89664f09f77cb7421765affb25adb7742c011baa63b677b9ef76654d3d20c1d5`。High 均停止写入，Low 独占编译/测试/实库窗口。每个失败需先报告并停止后续步骤，再经 root 定点解冻、High 修复和新快照后继续。当前尚无通过结论。

T13 v1 编译退出 1 后已释放窗口，经 High 定点修复 typed CreateCommand 后重新冻结 v2：515 项、manifest SHA `ab4bff7fa58a313edfd5a9506ec717c87056b1e75dcf1597fed3b4e271e203ed`。Low 重新独占窗口；v1 失败保留。

## 续接同一 R2 任务

用户「继续」保持 T13 → T14 范围。前轮窗口已结束，480 项离线通过；consumer-v4 的 offline 26 项 override 审计通过，shared 在 configuration 阶段拒绝非默认 BufPrefetch，未进入 DB。新 owner r2_probe_resume（Sol High）仅修工具，r2_verify_resume（Sol Low）等工具新冻结后消费同一产品包。产品及测试保持冻结，旧运行证据只读。

T13 已独立验收并经 root review：480 离线、同包双 profile 业务、安全拒绝通过；最终516项 accepted-source/binaries及公共evidence已保存，历史54哈希不变。原窗口释放；当前用户R2授权推进T14，旧T13输入只读。新的T14归属将在本节追加，尚未冻结前不执行build/DB。

## T14 新文件归属（T13 输入/证据只读）

| agent | 唯一写入范围 |
| --- | --- |
| root | 设计/review、docs/implementation、兼容性/入口文档 |
| r2_cancel_core（Sol High） | Sessions、Transport、必要FrameReader、DmException与新FailureInfo/Timeout/Canceled API类、新Session/Transport取消/期限测试 |
| r2_cancel_api（Sol High） | DmCommand、DmDataReader、Execution、DmGetValue必要helper、Command/Reader取消/期限测试 |
| r2_cancel_connection（Sol High） | DmConnection、DmTransaction、新Connection/Transaction取消/期限测试 |
| r2_cancel_protocol（Sol High） | B/D/RequestCodec/ResponseCodec Async、DmWireTestHooks数字AfterFrameSent、新ProtocolCancellation tests、必要DmConnInstance receipt helper |
| r2_probe_resume（Sol High） | 新 tools/R2CancellationProbe、eng/t14.sh、eng/T14.md；T13工具不修改 |
| r2_verify_resume（Sol Low） | ignored验证输入/制品，唯一build/test/DB窗口，等整体freeze |

T14 前已保存 62 项历史证据 SHA（原54+T13新8）与接受源码manifest哈希于 `.local/verification/r2/t14/historical-baseline/manifest.json`。T13 accepted-source/binaries继续只读，后续不得重写旧输入。T14编码期间无dotnet/DB；Luna已生成的async-r2逻辑调度fixture可用于新测试，不另生成伪实测结果。

T14整体v1已冻结536项，manifest SHA `388fbf3e89b0b05a247fe5a8914fef8eda22a5e5fed7d32037f22eb1635c0334`，2026-10-02续接时逐项核对无变化，62历史evidence无变化。旧agent窗口已结束；r2_verify_final（Sol Low）现持唯一build/test/DB窗口，High全部停写，失败交root定点修复再新freeze。

T14 v1编译、v2旧超时断言、v3三Async失败均已保留并先释放窗口再修复。最新v4整体冻结536项，SHA `23ec9139b11b645931b3215e446476cbfbd209f1fba857fe71c395e15a2b4098`，r2_verify_final独占验证窗口。

## R2 最终收口

T14与同finalcandidate T13回归全部通过，root已逐项review545接受输入、575计数、6实际包layer及62历史SHA。最终source/binaries只读保存在 `.local/verification/r2/t14/final-validation-v1/`，公共evidence/T14可复核。High停写、Low窗口释放；T15–T25不启动，未commit/push/发行。随后仅root交付文档收口，不重建产品或改写接受输入。

最终交付文档overlay仅更正tools/R2CancellationProbe README的1200整过程护栏说明，差异记录于`.local/verification/r2/t14/delivery-docs-overlay/manifest.json`；运行接受545输入保持原样，产品286项source/DLL/包未改，不重建或重包。

## 2026-10-02：R2 用户 review 跟进

用户授权处理本轮4项源码/契约意见及设计状态。原545接受输入、候选、全部evidence只读，历史哈希存 `.local/verification/r2-review/historical-baseline/manifest.json`。不进入T15，不发行/推送/提交。

| owner | 唯一写入范围 |
| --- | --- |
| root | 设计/review、docs/implementation、compatibility和包README文档 |
| review_transport（Sol High） | DmTransport、DmFailureInfo、TransportTests新R2ReviewSendDeadlineTests、AsyncTests新R2ReviewFailureInfoTests |
| review_begin（Sol High） | DmConnection、新AsyncTests/R2ReviewBeginTests |
| review_reader（Sol High） | 新AsyncTests/R2ReviewReaderLifecycleTests，必要共享fixture仅先与root协调；不改生产Reader getter |
| review_verify（Sol Low） | 唯一build/test/DB窗口，ignored新证据，源码冻结前只准备 |

High不dotnet/DB，rootreview并整体freeze后交Low；失败停止后续并释放，再定点修复/newfreeze。原R2门指历史候选，新工作树review未完成不能冒充已验收。

R2 review代码修复已完成；source-v2 549项、603离线/TLS三门通过。新candidate shared两轮Fetch超时，旧accepted baseline同负载同预算也超时；三轮随机对象独立精确恢复/fresh0，全部窗口释放，不再盲重试。本轮当前status offline_verified/TLS verified/shared integration_pending；原R2门仅原acceptedcandidate，不提升新candidate为完整实库通过。High停写，Low仅准备最终制品；未commit/push/发行。

本轮review主审阅完成：603离线/TLS三门、549输入/618制品/75历史hash已核对。原始shared两新候选及一旧baseline失败保持，三独立恢复fresh0，无待清对象。最终statusoffline_verified/shared integration_pending，代码修复已完成；全部窗口释放，未来shared门需环境满足原预算后新run，不发布/提交。
