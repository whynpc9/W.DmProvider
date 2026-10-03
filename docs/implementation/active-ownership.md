# 当前状态：R3 设计与分阶段实施；历史输入和证据只读

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

## 2026-10-02：R3 用户授权新窗口

用户明确要求完整实现 R3，并指定 Astra High 设计、Sol High 编码、Sol Low 独立验收。基线 main `ed0c8a19d2a3d275eb469960ae56778e6280b5b0` 工作树干净；94 项历史 evidence 哈希与基线 archive 保存在 `.local/verification/r3/historical-baseline/`，保持只读。旧章节的“不启动 T15”与旧源码/提交状态是历史窗口约束，本轮按新的 R3 授权推进。

| owner | 角色与唯一写入范围 |
| --- | --- |
| r3_design（Astra High） | 新 `docs/implementation/R3-design.md`；产品、测试和历史 evidence 只读 |
| r3_code（Sol High，设计后派发） | 当前 dependency-satisfied 阶段明确列出的产品、测试、探测工具与 eng 文件 |
| r3_verify（Sol Low） | `.local/verification/r3/` 新验收输入和输出；唯一 build/test/DB 窗口 |
| root | 范围控制、code review、新公共 evidence、任务报告、进度和入口文档 |

按 T15 → T16 → T17 → T18 → T25 → T19 逐阶段冻结、独立验收与审阅，具体编码范围在派发时明确。High 不运行 dotnet/DB；Low 在整体冻结且 root 授权窗口后执行，修复先释放窗口、回交 High，再冻结新输入。所有失败轮次保留。

R2 历史门已满足；当前 review 候选离线603/TLS已验收，shared跨页Fetch同新旧包超时仍为 integration_pending。R3分别报告TLS和shared证据，不提高预算或降低负载代替原门。正常测试只使用 TEST/TLS wrappers，先核实身份，唯一对象精确清理和 fresh 最终检查；不读SA、不变更实例或授权。R3候选材料和本地验收属于授权范围，远程发行不在本轮范围。

T15 v1 已经 root review 并冻结 559 项输入，manifest SHA `4ad6374fe03898a66bf5bd3fab54ae46ad7f4fd6a4100f1a473558e9199ce0e5`，路径 `.local/verification/r3/t15/frozen-source-v1/`。所有 High 停写；r3_verify（Sol Low）持唯一构建/实库窗口，按 offline → TLS → shared 运行，任一必需失败停止后续并释放。尚无通过结论。

T15 v1 双 profile 独立验收完成并经 root 审阅：offline/TLS/shared 三门 exit0，每个真实 profile 16 创建=16关闭、精确对象 fresh absence。559 输入与94历史哈希未变。完整reset仍未证明，实际reuse0，保守discard成立。窗口释放；按DAG进入T16，新归属另行明确，T15旧输入/包/证据只读。

### T16 新编码归属（T15 已接受输入只读）

- r3_pool_core（Sol High）：`Internal/Pooling/**`，新 PoolTests 的 csproj 和 `DmPoolSchedulerTests` / `DmPoolIdentityTests` / `DmPoolRegistryTests`。
- r3_code（Sol High）：Connection/Builder/Settings/Factory、DataSource/command wrapper、PendingOpen、FailureInfo 的 PoolWait、产品 friend assembly；PoolTests 的 ConnectionPool/DataSource/PoolConfiguration 文件；新 R3PoolProbe、eng/t16.sh 与 eng/T16.md。
- r3_verify（Sol Low）：下一整体冻结前仅准备，无构建/DB。root 负责审阅/进度/新证据。

双方使用约定 owner/lease 接口：许可覆盖 creating/leased/closing，物理 transport 关闭后一次释放；Clear 保留原 capacity domain。生产默认 all-discard、不启用旧池或statement cache。显式证书文件的直接 `DmConnection Pooling=true` 首版明确拒绝，使用独立 `DmDataSource` 容量域；禁止偷偷退回每连接独立且无界的池。默认系统 trust TLS 可使用有界 registry。此限制与零复用率必须公开记录。

进入 T16 前历史基线（含新 T15 证据）已保存 `.local/verification/r3/t16/historical-baseline/manifest.json`。所有旧源码/包/失败/证据只读，编码期间无 dotnet/DB。

T16 配置契约扩展时，r3_code 另获授权更新 ConfigurationTests.cs 中旧 Pooling=true 拒绝断言，以及 configuration/cases.json 的该单一负面场景归属与 README 对应说明；其他 unsupported 配置及断言保留。旧 T04/T15 接受快照不回写。

T16 pool core 7文件已完成静态审阅并冻结，46项核心场景待Low执行。main尚未整体冻结；用户授权同一T16阶段进一步分离文件：新增 R3PoolProbe/eng t16/T16.md 转交 r3_pool_core，r3_code 仅继续Connection/DataSource/config/tests；双方已确认无交叉写入。root另授权 r3_code 在 DmDeadline.cs 添加同Clock绝对期限 EarlierOf，仅防止queue→handshake续预算，不改变原期限断言。

T16 v1 root review 后整体冻结 580项，manifest SHA `7d226df0f957e040bee60db36bddf8e0e1c968b5d7b47b285c67a6294483cf2e`，`.local/verification/r3/t16/frozen-source-v1/`。两位High全部停写，r3_verify（Sol Low）持唯一build/test/DB窗口；先旧九套+新PoolTests，全部通过才pack/offline/TLS/shared。102项历史evidence不变，失败保留并stop/释放后定点修复。

T16 v1 离线 685/685 与 offline 包 probe 通过，TLS shortcut 重复主键错误分类断言失败，shared 未执行；保留 `evidence/T16/failed-v1/`。该轮7创建=7关闭、所有owner零、精确对象fresh absence；Low窗口释放。仅Probe Program定点加安全diagnostic字段/认证真实计数/主次失败区分，580项诊断冻结 SHA `17e1dfadebb7ba7fa69ecab63851bd2aa5ce613034e4a69f2207fbcf4e4979df`，同v1包一次TLS诊断仍exit1，Number=-6602被分类Transport/Unknown；7认证=create=dispose，cleanup确认，窗口释放。旧结果不回写。

Astra High/root定案：严格完整服务器错误的响应证据与会话可恢复白名单必须分开。r3_code（Sol High）现定点修复 DmException/Command 的verified消费点、Session/Invocation/必要WireExchange、同步/异步execute/prepare codec共享严格错误body与原子receipt入口；仅新增ServerErrorClassificationTests。其它T16产品/core/probe继续冻结。原8.1.5.60/-2106/ActiveTX恢复白名单不扩大，generic严格servererror仍Broken/discard/Reusablefalse；取消与期限首因获胜时不得提升ServerReported。修复后新全源冻结、新唯一包和独立全回归/实库才能关闭T16。

strict receipt 窄修复经 Astra/root只读审阅通过，T16 v2冻结581项，SHA `9495659ab52608a488e73c85dfb15dcc019fa44d71487e87fd40880bde9f3fa3`。High停写，Low再次独占9旧套+PoolTests/新唯一包/offline→TLS→shared窗口；原v1/诊断failed均只读。未执行前仍无T16接受结论。

T16 v2 旧九套603通过，Pool134中133通过/1失败，总736/737；失败仅新sync shortcut服务器错误场景实际NullReference，async及其他严格receipt场景通过。Low已停止释放，无pack/DB。r3_code定点排查新增ServerErrorClassificationTests初始化与DmCommand同步失败cleanup，仅该新测试及必要同步cleanup解冻，原strict断言和其它files保留。失败v2/evidence和102历史哈希不回写，修复后v3再独立验收。

单次sync诊断定位旧C2p优化调用失败后catch重放closed B，造成NRE覆盖原错。r3_code最小移除 T02_02000064 中reader和当前internalNonQuery可达的两处network fallback，本地parser fallback与成功优化路径保留；新增unknown IO no-replay计数场景，移除临时firstChance。v3 root审阅冻结581项 SHA `d417d733d0b87842124de6fb53608adcbeb6d4da9fa484e4d5ebe0d76ca7f605`，High停写；Low独占先targeted class，green才全10套/新包/真实门，重复计数分开。所有旧失败与诊断保持。

用户补充授权：完整R3实现/验收后由root commit/push/创建PR，持续处理当前head review bots直到无新待处理意见且必需检查通过，再merge。worker无Git/PR写权限；不因此扩展为NuGet或GitHub Release发行。当前本地分支 `codex/r3-datasource-streaming`，HEAD仍基线，源码待最终接受。

T16 v3完整739/739、targeted54单列、新包offline/TLS/shared均通过，root逐581输入/102历史哈希核验并归档accepted-v3；13/15认证与物理创建关闭配平、syncIO0、owner0/fresh精确清理成立。窗口释放，T16旧源/包/失败只读，按DAG进入T17。T17历史127项保存本地hash。

T17第一子门仅能力观察：r3_pool_core写新增R3LobProfileProbe及eng/t17-profile入口，consume精确T16接受包，单帧GET/metadata事实不改产品。r3_code只准备ignored输入设计；r3_lob_vectors（Luna Max）只写新r3-lob合成fixture，完整大控制174495字节和独立marker/hash已root检查，1GiB仍仅逻辑生成descriptor。新profile工具冻结后Low唯一窗口，未知NCLOB/缺len推进不能猜，不把能力门当streamAPI验收；完成前High不开始产品实现。

用户另明确：R3完整实现、验收、PR review收口及merge结束后，才创建独立新会话，以同样Astra High设计/Sol High编码/Sol Low验收及PR/bots/merge流程处理R4。本轮仍只推进R3，R4不提前激活。

T17 profile v1 TLS完整Unicode/hash/EOF正确，但工具额外要求GETLEN属于Rune/U16/bytes造成false gate；Astra/root核27帧Data.len累和107618=GETLEN，保留差3与三个marker失败事实。四个工具/说明文件定点修正oracle，不改产品/输入/负载/预算。profile-v2冻结593项 SHA `c2dc094277e25a24b47c14e20566bec6c1cda0ae63fe86ee06e4a38bac0ce6dc`，High停写，Low独占sameT16pkg offline→TLS→shared。opaque进度与CLR长度明确分离，缺len/绝对seek/其它profiles不猜；旧failed-v1只读，不将协议门当stream实现验收。

T17 profile-v2能力观察独立通过并root核验：TLSGB18030/msg11 wire107618，sharedUTF8/msg21 wire107623，两者UTF16=115813/scalar=107621且全文hash准确；CLOB与声明NCLOB(cType19/TEXT折叠)各按Data.len opaque进度准确读取，每profile104创建关闭配平、sync0、精确fresh清理。593输入/127历史未变，窗口释放。此门不算T17stream完成；下面新分离Input/Output实现归属必须按真实codec/opaque单位。旧T16/profile输入包证据继续只读。

### T17 全流式实现归属

- r3_lob_output（Sol High）：Reader；AbstractLob/Blob/Clob/GetValue/Data；GET_LOB_DATA/GETLEN严格入口；新ReadCursor/Stream/TextReader/LocatorSnapshot；DmTextCodec独立strictEncoding；新LobTests输出/decoder/lifetime/offset类。必要metadata codec改动先root确认。
- r3_code（Sol High）：Parameter/Command输入freeze和bind；ParamValue/ParameterInternal/SetValue/Binding；RequestCodec.Async/T02_02000093/B.Async/T02_02000091的现代参数上传/ACK；新LobInput；LobTests csproj与输入类/product friend。只该owner写B文件，output直接调用现单帧AbstractLob网络方法。
- r3_pool_core（Sol High）：新R3LobProbe及eng/t17.sh/T17.md独立包流式consumer。
- r3_verify（Sol Low）：准备/冻结后唯一构建/DB，不改源码。Luna fixture已完成只读；root公共docs/evidence/review。

共享唯一codec接口由output提供CreateStrictEncoding，返回独立clone，input使用Encoder、output使用Decoder，state不共享。真实profile已证明GB18030/msg11及UTF8/msg21/CLOB与NCLOB(TEXT19映射)同charset传输、Data.len opaque进度；不按Rune/UTF16/bytes猜wireoffset。GetChars CLR长度/随机offset用独立zero-scan，不直传offset。

大值probe预先固定新的输入/cmd300秒、Connect15秒、Process1800秒；Blob64MiB+1024和至少1MiB多字节Clob/声明NClob，这是新负载的预设设计，不放宽旧R2跨页预算。完整T17同时要求1GiB逻辑生成器且不储wholepayload/受控buffer、fullAPI cap拒绝和stream成功。当前仅编码，不运行dotnet/DB，完成rootreview整体freeze后才Low。历史含profile新证据hash保存 `.local/verification/r3/t17/full-historical-baseline/manifest.json`。

T17 ACK-only短窗口新consumer固定T16包：两profile各11req26=ACKheader=BeforeDecode，response261/status0/body21；3创建关闭配平、sync0/fresh清理。620partial源与143历史hash不变，未编译新产品。窗口已释放，Input恢复同任务并加精确261 guard/negative严格error+非法opcode测试，Core恢复fullprobe；Output正补SequentialAccess stream消耗/后列访问/Dispose-reopen与typed fullgetter不得后退的合同守卫。所有能力证据只读，本轮full产品仍未验收。

T17 full-v4 targeted95通过，但旧Type33项fixture初始化失败；v5仅将已移除skipCol初始化更换activeLobOrdinal，定点Type120/142，保留22项实际契约冲突。Low均stop/released，无pack/DB，失败归档full-failed-v4/v5。Astra/root将旧CLOB编码字节/NULL/顺序/bounds视为兼容回归修复；仅BLOB范围/长度与CLOBUTF-16扫描旧物化cap视为有意T17改进并迁移独立有界oracle。r3_lob_output独占Reader、旧ReaderReview/LobMaterializationReview及新ReaderOffset/ReadStream兼容测试范围，cursor/protocol/input/probe继续冻结，不dotnet/DB。新的统一freeze前不启动T18。

### T18 独立范围归属（2026-10-03）

T17实施/843离线/TLS16case/同包T16offlineTLS已root接受；T17完整completed=false，shared大值和本轮表恢复pending。按R3-design§1分阶段解释，仅激活T18implementation/offline/TLS，upstream_pending显式保留；最终R3完整门不关闭。179项历史evidence本地hash存t18/historical-baseline，只读。

- r3_code（Sol High）独占新Internal/Diagnostics、PublicApi/DmDiagnostics及csproj friend、PoolOwner/Registry、Transport、Session/Invocation、必要Connection/Command/Reader/Transaction观测接点、LobReadCursor/ParameterUpload.Streaming的数值chunk接点；仅诊断接线，不重写行为。
- r3_pool_core（Sol High）独占新DiagnosticsTests、R3ResourceProbe、eng/t18、t18 offline manifest与新.github/workflows/r3-offline.yml；不改产品或旧tests。
- r3_lob_output（Sol High）只准备ignored精确维护review工具，当前不读adminsecret、不登录/关闭服务器会话；执行另需用户明确限定授权。
- root独占报告/进度/证据，Astra设计与rootreview；Low唯一freeze后的dotnet/DB窗口。High不build/DB。

资源门预算已写R3-design，不能失败后放宽或将smoke当long-run。无任何shared大payload重试/SA/授权变更已被此阶段派发授权。

T18准备期间root/Astra发现T17类型分流错误：28为ROWID binary，不能ServerEncoding解码；0/54为已有实际text被遗漏。Reader仍r3_code独占，最小修复text集合0/1/2/19/54，ROWID28只GetChars本地rendered兼容，新GetTextReader28拒绝；GetBytes0/1/2本来InvalidCast，不扩展。r3_lob_output只新ReaderTypeCompatibilityTests及旧R3LobProbe安全numericerror/失败阶段观测，不改Reader/旧test；r3_lob_vectors仅新r3-reader-types独立合成fixtures；r3_code另只LobTests.csproj新fixturecopy项。此发现/修复单列，T17旧accepted source/evidence不回写。

共享管理维护prep经High编写/rootreview/Low离线build、dryrun和falseapproval拒绝通过；没有adminsecret读取或DB操作。root已向用户提出一次限定维护或外部管理员处理的异步选择，当前尚无答复，模板两个approval=false；elapsed/default选项不构成授权。独立T18编码继续，任何admin执行必须得到明确用户答复并限定同sharedendpoint、TEST/本轮表/INSERT、唯一且二次身份核验会话。

### T25 对应独立范围（T18 scoped accepted）

T18 865完整同DLL/7first-init进程/TLS600s资源门已root审阅接受；648source/179历史hash不变，完整shared/R3仍pending。T25历史271 evidence保存本地，不回写T18。root只读审查public wrapper委托、input cursor调用者所有权、物理Close→permit、标准诊断；Astra独立第二视角未报新的已证实阻断，提出三个公开链组合case。

- r3_code：只新LobTests/T25LateInputCancellationTests.cs，真实publicExecute，已发ACK后source忽略cancel有限晚返回，旧输入不能续发/伤新Open，原token/callerownership/permits。
- r3_pool_core：只新PoolTests/T25ShortcutLobOwnershipTests.cs，真正Source shortcut reader流/缓存，物理close屏障前许可不得返还，旧flow不能碰newsession。
- r3_lob_output：只新DiagnosticsTests/T25ReentrantDiagnosticsTests.cs，wdm.commit stopped回调Clear/DisposeSource，Committed/单Commit/结束waiter不杀在借业务/无死锁。

各自helper只能同新file，不改旧fixture/product/docs，no dotnet/DB/Git；失败必须root定点再授产品修复。Low整体freeze后独占验收。用户有限维护问题尚无答复，模板仍false，不读取SA或执行admin操作。

T25 v1/v2/v3/v4 失败证据保持只读。v2 确认 shortcut cleanup 与并发 Dispose 竞态，r3_code 定点 Release 修复并新增 4 个确定性/负控制；v3/v4 隔离到成功 Commit 诊断被错标 TransportError。r3_code 只 Session/Invocation 复制协调员最终 Commit/Rollback outcome 并令 Unknown 优先、已确认成功为 Success，不改变 ACK/Completed/cancel 行为。r3_lob_output 保留 Commit 强断言及安全旗标，另新增真实 public Rollback ACK 成功 span 断言。所有 High 已停写，v5 整体冻结后 Low 独占 targeted 10 → 完整 12 套 → 新精确包/offline → 同包 T16 offline/TLS；本轮不执行 shared/admin，也不重复旧资源 soak。完整 R3 shared 门仍待维护授权和验证。

### T19 当前实施归属

T25 v5 652源/271历史 hash root逐项一致；target10、完整875、精确包/7first-init/T16offlineTLS通过，旧失败只读。T19历史377项 evidence 保存新 baseline；T17 shared/cleanup仍pending，R3未结束。Astra High 新设计已审阅，Low窗口释放后派发：r3_code 仅新R3CandidateGate、eng/t19及新CI与csproj默认Version/Description；r3_pool_core 仅新DownstreamAcceptance/T19 prepare/run/validate/handoff（排除以下两文件）；r3_lob_output 仅T19/r3_contract_tests.cs及candidate_guard.cs。root包README/兼容矩阵/reports/evidence/progress，High不dotnet/DB/Git，新的源码冻结后Low独占。EF固定archive/SDK10.0.401与driverSDK10.0.203分开，最终包资源门重新运行，不沿用T18旧包结果。shared/admin未授权动作不得派发。

T19补充精确归属：r3_lob_output另独占新r3_retry_unit_tests.cs，仅Unit三Fact的SyntheticDownstreamUnit；root已补根LICENSE标准Apache文本与notices来源，不改变原许可表达。r3_code获csproj唯一新增LICENSE Pack条目，保留所有原accepted输入与失败。Astra澄清原资源门只TLS600s，shared长时标not_verified，shared大值/原跨页/完整EF/exactcleanup仍必过。

T19首轮准备源码全部Ready/High停写。root与Astra审阅识别的overlay删除遗漏、CREATE前持久owned ledger、R2完整门/T17外层预算与精确EFpin均已修正，未改产品业务。新包metadata只改默认R3/Description/LICENSE pack，新增标准许可证保持原Apache表达。整体v1冻结后Low先final offline/sourcearchive/no-build精确包及身份，再新隔离EFprepare/Unit+TLS支持subset和精确包TLS完整矩阵（T16/T17/T13/T14/security/T18资源）。377历史evidence保持。没有shared/admin执行授权；任一失败stop/release、由High定点修、保留失败并新freeze。整个最终candidate窗口内root不回写included docs/evidence/progress，输出只ignored候选目录；窗口末再统一归档。

T19 v1 final候选离线875/精确包/7first-init/完整源archive重建/API/依赖/SBOM通过，EFprepare在新shared-unit archive拒绝anchor_cli_unique_proof。Lowstop/释放；没有EFbuild/DB/最终TLS/shared/admin。实际acceptedcombined后的CLI采用Process?局部变量与try内Process.Start，并无工具假定的using-var启动行。只prepare.py的CLI proof插入点由pool High定点修，旧Process生命周期/预算保持，不改failed输出。旧候选/源archive ignored只读，public安全JSON/TRX归档failed-v1（不把二进制sourcearchive递归加入后续源树）。修复Ready后整体v2、新最终Version重跑；不覆盖原包/source/proof。

T19 v2 offline新Version875/pack/资料通过；EFprepare已通过CLI anchor但在overlay重建失败，Lowstop/releases，无EF/TLS/DB。root只读定位unified_diff对无末尾newline的原/新文件没有输出标准EOF marker，造成后续---header拼在上一行。只prepare.py由pool High保留原字节/增加标准patch EOF处理并继续严格重建；不normalize源码或跳过门。v1/v2所有失败及包/source archive保持，新candidate仍需独立全门。

T19 v3准备定点修复：overlay标准EOF无newline marker保留原始字节；本机Apple patch默认保留删除后的0-byte文件，新temp重建overlay专用-E（dry/apply一致）删除明确deleted文件，acceptedcombined原命令不改。High新隔离pure Python/patch诊断159源SHA及EOF边界匹配，accepted_final=false，不算Low最终接受。prepare/README/handoff三文件停写，v1/v2候选及失败只读；整体v3新Version由Low独立执行全门。

T19 v3 offline875/prepare严格byte重建成功，同最终候选SDK401 shared-unit310/tls-unit310均通过；R3 TLS Functional编译阻断为新合同四项analyzer：两CA1305 invariant转换、CA2219 finally抛异常、CA1001 Store disposable ownership。Lowstop/releases无实库。只output High new r3_contract_tests.cs定点修，不禁分析器/弱化断言/改budget，失败v3与包/isolatedarchive只读。下一v4新Version独立全部门，shared/admin仍无授权。

T19 v4 compile/Unit310x2/prepare/875通过后真实R3 TLS 2case1通过Savepoint拒绝，CRUD在crud_insert_identity失败；SafeFailure仅genericHResult/无DMnumber不足定位，两个exact本轮表已独立确认absent，schema inventory一致，无遗留TLS对象。Lowstop/releases不执行TLS最终matrix。只output High增受控known exception chain/可信程序集staticframe/命令数字快照，禁Message/SQL/value。先新冻结诊断consumer针对immutable v4包复现，诊断不是最终验收；定位后再精确修/最终newVersion全门。Astra并行只读分析，不改变AutoSavepoints/原負载/assert。shared维护授权仍无答复。

T19 diag-v4 exact旧v4包/新isolated安全观测实际定位DbUpdate→InvalidOperation于DmParameter.ValidateTypeConfiguration/PlanCapture，stage before_save，不是servererror。两个exact新TLS表独立absent，diagnostic accepted_final=false，不计最终验收。Astra确认fixedEF显式CLOB生成DbType.AnsiString+Dm.Clob/Size=-1；当前driver只允许String+Clob/Text。r3_code获唯一DmParameter兼容分支扩大到String或AnsiString＋Clob/Text、新独立TypeTests正反例及Clone/PlanFreeze，其他类型冲突、FixedLength拒绝/encoding/绑定precedence/servermetadata不变。consumer CLOB/40KiB/AutoSavepoint不改；Output观测代码冻结保留。修后v5新Version完整门独立复核。

T19参数兼容窄修High ready：仅DmParameter String/AnsiString＋Clob/Text允许分支；新增独立TypeTests17case覆盖两setter order、Clone/PlanMetadata/ExplicitDm precedence、Size=-1及原Unicode、FixedLength/错类型拒绝和BinaryBlob原正例。无其它产品改动、不跑build/DB。安全diagnostic保留。root静态审阅PlanCapture无真实I/O/不削contract；v5目标17单列→完整预期892→新最终包/来源→fixedEF310x2/TLS2→同包完整TLS矩阵，shared/admin继续pending。

### T19 v5 历史归档与宿主预算修订

v5同包892离线、fixedEF Unit310×2/TLS2及完整TLS矩阵/600秒资源通过；shared原T13/T14/security/T16/T17大值通过。旧T17表先由普通TEST清理；用户随后批准该旧表一次限定管理员维护，因已清理未使用管理员凭据，该授权不扩展到后续EF对象。两次shared functional旧900秒超时和各唯一遗留表普通TEST精确清理、同步/异步与两组ABBA诊断保持历史证据。所有诊断结束，fresh库存为空，Low窗口释放，公共安全归档candidate-v5-interim；v5源/包与失败记录只读。

Astra High/root批准shared-functional外层宿主护栏1500秒的明确设计修订，公式及原始证据hash见maintenance/T19-shared-host-budget.md。仅pool High负责T19 common/prepare/run/validate/README及纯离线预算合同测试；不改产品/EF fixture/SQL/原76项/seed/call20/AutoSavepoints。root负责设计说明、兼容矩阵/历史归档/progress。原v5整体冻结明确退役，下一唯一版本重新冻结来源并由Low独占完整同包验收；其它High停止写入，R3尚未结束，R4不启动。

修订六文件High Ready并停写，root审阅固定路由/启动前拒绝篡改/真实host结果及validator对账通过。产品298和测试136个既有输入与v5逐文件一致。v6历史baseline903项含v5安全归档和诊断源码字节，原791项未变；下一轮Low独占先7项纯离线预算合同，再新唯一Version完整12套/精确no-build包/来源/API/依赖、固定EF310×2/TLS2与同包TLS及shared全矩阵，最后shared完整76/CLI/scripts/spec/R3合同。整个候选窗口不修改 included 源码、工具或公共docs/evidence/progress，输出只ignored目录；任一失败停止并释放窗口，不自动放大1500秒预算。

## R3 最终窗口结束与交付

2026-10-03：Astra High八工具来源链只读审查通过；Sol Low独占执行35项Python合同、独立9项guard合同、新CLI原实库1项及shared/TLS原证据聚合均通过。Root复核13运行时门、两profile九个下游lane及903历史证据后释放窗口；当前所有编码agent停写。Root负责验证后公共证据、报告、最小CI合同步骤、提交/PR及bot闭环。PR merge前不开始R4，发行仍未授权。
