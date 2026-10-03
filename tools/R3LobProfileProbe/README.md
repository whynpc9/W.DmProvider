# T17 真实 LOB profile 协议语义观察

这是 `capability_characterization` 子门，不代表 T17 流式 API 或大值验收完成。工具只消费 T16 已接受的精确包 `0.1.0-r3.t16.20261002143244`，硬锁既有包/DLL hash、MVID 与实际加载 PackageReference；不修改产品、不使用官方驱动，不猜网络 opcode。

输入为独立 Luna fixture `tests/fixtures/r3-lob/vectors.json` 的 `out_row_profile_large`，174495 UTF8 bytes、115813 UTF16 code units、107621 scalar values，含16个分布的独立编号 marker、emoji/BMP/组合字符/NUL。输入大小不能代替 out-row 证明，必须实际观察 locator storageType/local。工具读取独立长度/hash/marker offsets 作全文 ordinal 对照，不输出值或 locator 内容。

真实 profile 先确认 TEST/同名 Schema，再只创建本次唯一 `T17C_<20 hex>` 与可选 `T17N_<20 hex>` 表。以现有 typed Clob/string 参数上传，明确记录旧输入路径。NCLOB 仅声明 SQL 列并观察本 profile 的实际 codec/type 元数据；没有 national enum/charset 标记时不推断所有 CLOB 是 NCLOB。仅 NCLOB 声明阶段、带已验证 ServerReported 与数字编号的拒绝才记 `unsupported_by_profile`；上传/传输/字符破坏不改称不支持而通过。

`DmDataReader.GetClob` 绑定 reader lease；每次在调用流同步反射 `BeginPublicOperation` 获取子 invocation，然后 await 内部 `ReadLobAsync(AbstractLob,long,int,token)` 单帧 Task<Data> 或 GetLobLengthAsync，完成/释放 invocation。没有调用高层 ReadLob helper 或 GetString 完整读取。包内实际 network hook 记录 connect/tls/send/receive：本工具 async 门要求同步计数0。反射是固定已接受包的观察工具，不是产品公开 API。

每帧只输出请求 offset/length、回复 byte count、Data.len、readOver 与严格解码 UTF16/scalar counts。Data.len 非负时按该值 checked 累加，并保留响应更新的同一 locator 状态；下一请求 offset 必须等于此前累和。每个非 EOF 块必须非空且 Data.len 正向推进；最后严格 Unicode/rune/hash/EOF 全文对照、GET_LOB_LEN 等于完整进度累和即可证明该 profile 的 `opaque_server_units` 顺序读取。`progress_requires_updated_locator=true,absolute_seek_proven=false`：不把 opaque 单位解释为 CLR chars、Unicode scalar 或 bytes，不宣称 fresh locator 的绝对 seek。

缺少非 EOF Data.len 时，仍须至少三个独立有差异的 marker、唯一匹配的 scalar/UTF16/encoded-byte 候选，加上全文精确对照才可继续观察；没有证明就 pending，不按 decoded.Length 猜测。该缺失分支不提升本初始 gate 的 opaque-unit 证明：通过仍要求所有帧都有非负 Data.len、完整 wire 累和等于 GET_LOB_LEN。对超过 GET_LOB_LEN 的候选 offset 不发请求且不当负证据，其他 marker 的失败也保留原事实。最后 readOver=true 且 len=-1 不需下一 offset，单列 terminal_wire_length_missing，final_wire_offset 留空，不假补长度；完整 wire 进度 ledger 未证明时该子门不通过。

v1 的错误来自工具 oracle：实现者曾要求 GET_LOB_LEN 必须等于独立 scalar/UTF16/bytes 之一。TLS 观察中，27帧 Data.len 累和与 GET_LOB_LEN 均为107618，完整内容 UTF16=115813、runes=107621、独立 hash 全部正确；三个 marker 单位候选均失败。该失败不表示旧上传损坏，差3也没有证明是 NUL 或某种 Unicode 规则。新 oracle 保留这些比较字段为 false，以同一 updated locator 的完整 opaque 进度 ledger 为证据；输入、4093请求、30秒命令与900秒过程预算不变。旧失败证据保留。后续 GetChars 的 CLR length 必须由独立从0有界扫描的严格解码结果取得，不能把 GET_LOB_LEN 返回为 CLR UTF16 长度。

所有路径在 finally 精确清理本轮表，再由另一 fresh TEST 连接确认 absence。输出不含原始 SQL/异常文本/凭据/环境/认证报文/私钥；checkpoint、实际命令与退出码、包前后 hash、source/fixture 输入 hash 均保存。runtime 900秒护栏，超时后需 Low 根据 checkpoint 本轮名字进行独立精确恢复。High 仅准备源码，root review/freeze 后 Low 独占构建与 DB 窗口。
