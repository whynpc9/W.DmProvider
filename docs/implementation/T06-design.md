# T06 — 加固传输与帧边界

状态：T06 已实施并通过独立验收，见 [报告](reports/T06.md)。以 S04 中 T06 的范围为准；TLS/native 为 T07，异步专用传输验收 NET-05 为 T13。

## 基线与分工

T05 已验收的 268 个实现文件和 8 个附件已按 evidence 哈希保存到 `.local/t05/accepted-source/`。原始规范、调查快照和历史 evidence 保持原样；所有后续工作在当前分支延续，逐项记录真实进度。

主 agent 负责设计与 code review。Sol High 分别负责受控 transport 和 D 适配、frame/B/b 边界、deadline 和会话接入、测试工具；Luna Max 提供合成向量；Sol Low 在冻结后独立验收。共享文件必须明确交接，构建和真实库测试安排独占窗口，冻结后不再自行重建或修改生产代码。

## 设计决定

- `DmTransport` 拥有实际字节通道与 socket；构造不建连，显式 Open 才执行 DNS/TCP。取消或 Close 只销毁捕获的目标实例。多地址可在剩余期限内顺序尝试，一旦连接成功，不再关闭后重复 Connect。
- `DmDeadline` 使用单调时钟和绝对到期点，0 表示无限；剩余毫秒向上取整并受 int 边界限制。握手根预算从 DNS 前开始，覆盖 TCP、STARTUP、后续认证和 schema 初始化；借用子 invocation 不重建预算。
- 每个公开 Command/Reader 调用有相应 invocation 的共同预算，完整消息、短读写循环和 heartbeat 继承该预算。Reader 的调用间用户停顿不消耗后续 Read 新调用的预算。本次不宣称已完成 T14 的取消竞态和错误分类。
- `DmFrameReader/Writer` 以已经核实的 opcode/版本字段为依据：先精确读取 64 字节头，验证长度及 checked 总长，再申请有界 body；两帧粘连不能吞掉下一帧，截断或零进展明确失败。
- 默认总帧上限为 64 MiB 的产品策略。header/body、CRC 开销、count×width、偏移和字符串等均检查合法范围，失败不能继续复用会话。旧 buffer 的集中读写边界优先加固，保留协议 codec，不凭报告概述重写格式。
- heartbeat 仅按已识别的 opcode 消费完整帧并校验，随后继续等待业务响应；不重置总预算。未实现的压缩模式明确拒绝，避免无界解压。
- Socket.Send 必须循环处理短写，0 返回表示失败；ReadExactly 循环处理短读，EOF 不伪装成结果结束。T05 的 session/lease/invocation/wire 身份门及明确的参数上传、解码 continuation 仍生效。
- RequireTls 保持拒绝直到 T07 完成所需实现与验收；本项目现有真实库验证仍显式使用 PlaintextAllowed。

## 验收

NET-01/02：从源码核实字段后的合成帧与 fake I/O，覆盖每字节头、随机 body 分片、两帧连续输入、短写、零进展、EOF、负长、溢出、超限、错误 CRC。拒绝必须发生在大分配和响应解码前。

NET-03：可注入时钟证明连续 heartbeat 消费完整内容但不能延长 deadline；真实协议探针不得猜造未核实报文。

NET-04 的 T06 部分：DNS、多地址 TCP、LOGIN、初始化故障均释放已建立资源；握手中 Close 不留存活 socket，构造没有网络副作用，一次成功 Open 只有一个最终认证 TCP 会话。TLS/native 的额外失败边界归 T07。

真实库只使用 TEST wrapper，核实身份、唯一对象及清理后的独立读回；保留 T05 会话/LOB/事务和必要配置回归。失败记录与最终通过分开，完成后记录源码、程序集哈希及边界限制。后续 T07–T25 按依赖和实证门槛继续，不将未知能力标记已完成。
