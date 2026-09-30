# S10 — 流式 BLOB/CLOB/NCLOB

状态：实施规范。优先级：P1/P2；R3。依赖：S05、S06、S08。任务：T17。

## 1. 目标与现有基础

原代码的底层 LOB 消息已有按定位符分块读写，但 Reader 的 GetBytes/GetValue 等上层可能先完整物化；因此目标是接通端到端分块，不重写所有 LOB wire。[R04]

起点：AbstractLob、DmBlob/DmClob、DmBLobStream、GET/SET_LOB_DATA、GET_LOB_LEN、DmDataReader.GetBytes/GetChars、A/B.cs 的 LOB 分片。wire offset 单位、基数和 locator 更新遵循已验证源实现，不假设全是字节或全是0基。

## 2. 输出流

`GetStream(ordinal)` 提供只读 forward-only Stream，`CanSeek=false`；`Length/Position setter/Seek/SetLength/Write` 不支持时明确抛出。不要为了好看暴露一个设置无效的 Position。原 explicit DmBlob 可定位读写属于单独兼容surface，保留时需要其自己的 seek测试，不影响基础流模型。

`GetTextReader(ordinal)` 对 CLOB/NCLOB 增量解码。GetStream/GetTextReader 创建时不整字段读取；实际 Read/ReadAsync 才取下一块。每个 reader v1同一时刻最多一个活跃字段流；下一行/下一结果/reader关闭会关闭旧流并使其后续操作失效。读取另一列时按 SequentialAccess 顺序检查，明确拒绝向后访问。

流关联 session+generation+execution+rowVersion；不能把 locator 从一个连接交到另一个连接使用，也不能在父reader关闭后继续网络访问。多线程并读同一流拒绝；独立连接的流可并行。

## 3. GetBytes/GetChars

非空 buffer：只读取请求的区间并复制到 buffer；GetBytes 的 dataOffset 是字节，GetChars 是 CLR char 单元，CLOB协议单位由适配层换算。SequentialAccess 下 dataOffset 小于已消费位置明确失败，向前跳过可分块消费，不建全量中间数组。

buffer=null 的长度查询不能偷偷物化。BLOB 使用已验证长度元数据/GET_LOB_LEN；CLOB 如无法从服务端准确获得 CLR char 长度，允许用独立定位符有界扫描计算、不改变调用方逻辑游标。不能做到时必须明确声明该长度查询未支持，不能返回字节长度冒充字符长度。

合法的0长度读取不发无用消息；offset/length溢出、目标buffer越界在网络前拒绝。EOF返回0，非预期截断抛协议错误，不把缺少尾部当EOF。

## 4. 字符集

使用带状态 Encoder/Decoder，跨网络片段保留最多必要的尾部字节/代理项；UTF-8、GB18030等已声明字符集的边界测试必需。空字符串、NUL字符、中文、emoji、组合字符、代理对分割都覆盖；非法序列按S06拒绝，不替换丢字。

CLOB/NCLOB依照真实字符集/类型元数据处理，不能以“都是string”统一发送同一个错误codec。最后一块必须flush并检测不完整字符。

## 5. 输入流参数

支持参数 Value=Stream（BLOB/二进制大参数）或TextReader（CLOB/NCLOB），显式DmSqlType；长度未知、CanSeek=false也能流式上传。不得访问Stream.Length/Position作为前提，不做CopyTo(MemoryStream)+ToArray。

调用方拥有输入流，驱动默认不Dispose它；成功/失败后位置已经前移，重复执行不能自动回放或rewind，必须由调用方重建/定位并文档说明。

分块大小取协商限制/配置/当前编码完整边界；正确设置首尾标记并处理响应更新后的locator。用户取消、读输入流失败或服务端错误时停止上传，按S08处理会话，未确认的副作用不自动重试。

## 6. 全量API与内存指标

`GetValue/GetString/GetFieldValue<byte[]>` 仍可按其返回类型物化，但遵守MaxMaterializedLobSize。已知超限尽早拒绝；未知长度累计到上限拒绝，不能无限增长。告知使用GetStream/GetTextReader，而不是静默截断。值大小限额与.NET分配开销区别记录。

流式读的额外活跃内存应为 O(受控frame缓冲+chunk缓冲+解码状态)，不随LOB总长度增长。行内LOB可能已经随完整结果帧抵达，不能忽略frame大小把“零分配”作为虚假指标。

离线测试以生成器模拟1GiB+非seek流，不先分配1GiB数组；记录受控缓冲峰值。真实数据库测试建议至少64MiB BLOB与足够大的多字节NCLOB，具体大小可按测试实例资源显式配置，不把数字当服务器上限。

## 7. 验收

LOB-01：大字段GetStream第一块返回前未读取完整字段，峰值缓冲不随总长度线性增长。
LOB-02：不可seek且长度未知Stream/TextReader输入逐块发送，准确往返。
LOB-03：UTF-8/GB18030分片的每个可能边界、代理对、非法尾部。
LOB-04：reader前进/关闭、事务终结、连接重新租用后旧流不能访问新会话。
LOB-05：半途取消、输入流异常、GET/SET报文错误不回池、不重试、资源无泄漏。
LOB-06：GetBytes/GetChars偏移/长度/空buffer/0长度符合契约；没有先全量物化。
LOB-07：大值全量API超限明确抛错，不悄悄截断；流式API仍可读取。
LOB-08：使用新驱动nupkg的EF 40KiB LOB既有回归不退化；EF整实体string仍物化属于消费层，不算驱动流式失败。
