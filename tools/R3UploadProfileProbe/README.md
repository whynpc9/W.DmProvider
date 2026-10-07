# T17 PUT_DATA2 ACK 小门

只消费固定 T16 接受包 `0.1.0-r3.t16.20261002143244`（包/DLL SHA 与 MVID 硬锁），使用已有 byte[]/string 参数；不是新流式输入或 T17 全量验收。Blob 输入131089bytes，独立 byte_cycle_v1（seed17/stride131）；Clob 输入为 Luna out_row_profile_large，UTF8 174495bytes，均小于完整值物化限额。

`AfterFrameSent` 只计 request26；现有只数字 `DmResultProtocolTrace.AfterFrame` 只过滤 request26，记录 request opcode、实际 response opcode、SQL status、body length。`BeforeDecode` 用 CurrentRequestOpcode==26 交叉核对覆盖次数。不读响应 body、token、locator 或认证帧。通用 Csi buffer 不代表所有局部 ACK buffer，故直接使用已存在的 header 数字 trace。成功要求已发送26帧、ACK header 和 BeforeDecode 三计数相等、正状态及成功 INSERT/UPDATE/行存在；具体 response opcode/body length 只报告实测集合，不猜26/0/21。

TEST新连接核身份/同名Schema，显式TLS/明文，唯一 T17U_<20hex>表精确清理再 fresh absence。全部 actual network async、创建/关闭与模式计数配平。runtime护栏90秒；失败或无ACK覆盖即非零/not_proven，保留checkpoint后由Low精确恢复本次对象，无自行重试或提权。root冻结后Low执行，High无dotnet/DB。
