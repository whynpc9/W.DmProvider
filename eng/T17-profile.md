# T17 profile 子门 runner

只消费已接受的固定 T16 nupkg，不能以新产品替换包来证明既有协议能力。

```sh
eng/t17-profile.sh offline 0.1.0-r3.t16.20261002143244 <absolute-accepted-feed>
eng/t17-profile.sh tls 0.1.0-r3.t16.20261002143244 <absolute-accepted-feed>
eng/t17-profile.sh shared 0.1.0-r3.t16.20261002143244 <absolute-accepted-feed>
```

当前接受 feed 为 `.local/verification/r3/t16/validation-v3/feed`。runner 使用独立 CLI home、NuGet cache、单节点/noReuse/noSharedCompilation/disable-build-servers；可通过 DOTNET_COMMAND 选择仓库 pinned 10.0.203，不更改 SDK 或清空全局 cache。

每轮 `.local/verification/r3/t17/profile/runs/` 保存安全命令/退出码、包前后 manifest、consumer/fixture 输入 hash、consumer build、checkpoint 和独立 validator。消费者仅精确 PackageReference；硬锁包/DLL hash/MVID，source mapping 指向 T16 accepted-v3 source manifest。TEST/TLS wrapper 仅向 runtime 注入凭据，无 SA，无权限或实例修改。

这是 capability_characterization；offline 仍 integration_pending，真实 profile 只记录该服务器/codec/单位事实。GET_LOB_LEN、单帧 Data.len/readOver、Unicode scalar/UTF16/byte offset、实际 NCLOB 元数据必须通过独立编号输入与全文对照，不将 GetClob 全量 helper、错误上传或 decode.Length 猜测当流式支持。详细观察边界见 [R3LobProfileProbe](../tools/R3LobProfileProbe/README.md)。任一 mandatory CLOB、上传或清理失败即非零，不推进后续；900秒超时按 checkpoint 精确恢复本次对象。High 完成后等待 root review/freeze，尚无执行或 T17 完成结论。

v1 工具错误地要求 server length 属于 Unicode scalar/UTF16/bytes，现 oracle 修正为完整 `opaque_server_units` 进度证据：非 EOF 正 Data.len、下一 offset 为 checked 累和、保留响应更新的 locator、严格全文 Unicode/rune/hash/EOF、GET_LOB_LEN 等于该累和。原 marker 候选失败及 Unicode 长度比较 false 保留，不解释差异、不声称绝对 seek。最后缺 len 不合成长短，ledger 未证实则 pending；GetChars 长度以后独立从0扫描得到 CLR UTF16 数，不拿 server length 冒充。固定接受包、独立输入、请求大小与预算均未变。
