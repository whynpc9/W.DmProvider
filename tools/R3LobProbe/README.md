# T17 public streaming 独立包 consumer

仅精确 PackageReference 消费 root 冻结的新候选；校验包/version/DLL hash/MVID/deps，旧 ACK/profile工具保持只读。High 写源码后停写，root整体review/freeze，Low独占 build/test/真实库窗口。

预固定输入与预算写入每轮 manifest：Blob **67109888 bytes（64MiB+1024）**，byte_cycle_v1 seed17/stride131，unknown/nonseek/async-only generator，Length/Position/CanSeek/Seek及同步Read若被产品访问即抛。独立reference增量SHA256只一块32KiB缓冲，不存完整Blob。Clob与声明NCLOB各 **1158260 UTF16 chars**：Luna独立large Unicode值十个周期加不同cycle marker，包含emoji/BMP/NUL/组合字符，unknownlength async-only TextReader产生正短读并切开代理对；产品不得Dispose或rewind。固定 command总300秒、connect15秒、过程1800秒，失败后不提高预算或降低值大小。64MiB完整API限额拒绝与同字段public流完整读取都必须通过，不能据此推断服务器最大LOB。

只调用公开 GetStream/GetTextReader 和 Value=Stream/Blob、TextReader/Clob。构造无GET_LOB_LEN/DATA；firstRead记录实际opcode32请求差额1–2与实际reply body累计小于1MiB，分别返回最多1031bytes/17chars，不能背后先扫全值再仅返回小buffer而通过。small buffers逐段独立内容/hash/UTF16/rune对照，不物化大字符值。64MiB cap必须精确匹配NotSupportedException的固定安全limit消息（仅内部比较，JSON不打印原异常文本），且GetValue无GETDATA、reader仍Open、同reader同字段GetStream可读正确前缀，随后独立完整大值hash门。测GetBytes/GetChars forward和向后拒绝、null-buffer长度独立扫描不消耗main flow、合法zeroLen无IO、第二activeflow拒绝、row/result/reader-close/reopened physical lease下旧flow在I/O前失效。同步公共GetValue/GetBytes/GetChars明确隔离网络计数，不冒充async；其余实际async入口sync0。

Empty/exact-negotiated-chunk/final-short的Blob与ASCII Clob共六个真实小输入，正caller短读不能当EOF；用实际AfterChunk数字及stored public stream内容验证[0]/[chunk,0]/[chunk,17]。chunk取本profile实际negotiated值与32768的最小值，未假设所有profile为32000。PUT_DATA2 ACK验证已独立观察的261/status0/body21，只数字header，不读token/body。

真实故障为输入在已上传块后抛错、opcode26发后取消、opcode32读发后取消：caller不被Dispose、typed原token/Unknown/reusablefalse（取消）、Broken/Closed、无重放/额外socket，fresh TEST查本轮业务行0，最终精确DROP唯一T17S_<20hex>表，再另一fresh确认absence。旧40KiB array/string单列回归，不替代大值。缺功能/错误/cleanup失败非零，无隐藏skip。Malformed response/truncation及>1GiB逻辑输入由独立 offline LobTests负责，工具不伪造真实DB大值或畸形包结果。

仅数字观察：现有frame/header hooks计opcode32/29/26、实际reply body峰值；input AfterChunk给cursor-owned buffers真实数字；每次publicRead后只读反射cursor.BufferedBytes给sampled retained output buffer峰值。它们不包含CLR对象/协议额外缓冲的全量，不是瞬时全进程峰值或RSS/OS资源soak。保持all-discard/reuse0，owner最终六计数归零；公开标准stream路径没有反射raw GET_LOB_DATA。

仅TEST/TLS wrappers、每新连接USER/Schema与实际mode。限定TLS8.1.4.6/GB18030/msg11、shared8.1.5.60/UTF8/msg21已字符协议观察的范围；声明NCLOB沿该profile同codec与Clob typedparameter，不声称national charset全覆盖。安全JSON/checkpoint不含SQL/值/locator/凭据/异常文本；timeout后按本轮名字独立精确恢复，原失败不改写。
