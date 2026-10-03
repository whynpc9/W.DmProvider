# T17 单帧协议能力观察

本门是 **capability_characterization**，不是新流式API/未知长度上传/大值内存验收。root已独立核输入与逐帧递推、内容和EOF，原T16接受包未改。

| 实际 profile | charset / msg | 类型观察 | GET_LOB_LEN / ΣData.len | CLR UTF16 / Unicode scalar |
| --- | --- | --- | ---: | --- |
| TLS 8.1.4.6 | GB18030 cp54936 /11 | CLOB: CLOB; 声明NCLOB: TEXT；均cType19/scale0/isLob、storage2、new+long | 107618 | 115813 /107621 |
| shared 8.1.5.60 | UTF8 cp65001 /21 | 同样声明NCLOB返回TEXT/cType19、使用该profile同一charset | 107623 | 115813 /107621 |

独立输入UTF8为174495字节，含emoji/BMP/NUL/combining与16独立marker；严格解码全文逐段相等、UTF16/scalar计数和独立UTF8 SHA `158962106a0c3edf27c33da5b0065d7f57e11421f4643f3ada95dbacedd89792`均通过。每profile CLOB与声明NCLOB各完整扫描，Data.len全非负、非EOF正推进，requestoffset等此前checked累和、保留响应更新locator，最终readOver且ΣData.len==GETLEN。两profile各104物理创建=关闭，实际syncIO0，TEST身份与两唯一对象fresh精确absence通过。

使用 **opaque_server_units**，不能把Data.len/GETLEN解释为Scalar/UTF16/bytes，三个marker候选失败保留；不声称freshlocator绝对seek或缺len fallback。NCLOB仅这两个具体profile中声明列+Clob参数传输同charset的真实观察，原始typeName折叠为TEXT，不泛化独立national codec。流式实现的GetChars(null)应从原始locator副本有界扫描累计CLR UTF16长度，随机非SequentialAccess偏移同样零起点解码扫描，不能直接把CLR offset传入wire。

[汇总与逐帧事实](../evidence/T17/profile-accepted-v2/validation-summary.json)、[输入清单](../evidence/T17/profile-accepted-v2/accepted-source-manifest.json)可复核；固定消费T16版本 `0.1.0-r3.t16.20261002143244`，包/DLL/MVID身份和593输入/127历史hash before/after一致。Offline/TLS/shared门均exit0；没有重建产品或重复739测试。

v1的最后工具oracle额外要求GETLEN等Scalar/UTF16/bytes之一，造成TLS失败；完整内容当时已准确。Astra/root核对后仅修正该假设，输入、请求4093、30秒命令/900秒总预算、内容/hash/EOF严格检查均不变。旧failed-v1保持Characterization，四说明/tool差异另冻结，新v2才通过。

后续仍须完成新GetStream/GetTextReader/GetBytes/GetChars、unknownlength nonseek输入、编码边界/lifetime/取消、1GiB逻辑受控buffer、至少64MiB实际Blob及多字节NCLOB、最终nupkg EF回归。T17尚未关闭。

## 参数上传 ACK 形状补充

固定T16接受包的新small byte[]/string观察门，不是未知长度stream输入验收。两profile req26各11次，发送/数字ACK trace/BeforeDecode一一对应；实际响应opcode261、status0、bodylength21。仅记录数字header，不读取token/body/locator。两profile各3创建=关闭，actualsync0、TEST身份/fresh唯一表absence、620观察输入与143历史哈希不变。新partial产品当时未编译/打包，不能把此source snapshot称新产品接受。

[ACK汇总](../evidence/T17/upload-profile-accepted-v1/validation-summary.json)指导新输入校验261/0/21、同wire及最新opaque token；其它responseop不猜。初始token及链语义由源码+后续empty/exactfull/newstream实库证明，headerbodylength本身不证明token内部。
