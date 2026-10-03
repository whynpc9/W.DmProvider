# T17 upload ACK profile runner

```sh
eng/t17-upload-profile.sh offline 0.1.0-r3.t16.20261002143244 <absolute-accepted-feed>
eng/t17-upload-profile.sh tls 0.1.0-r3.t16.20261002143244 <absolute-accepted-feed>
eng/t17-upload-profile.sh shared 0.1.0-r3.t16.20261002143244 <absolute-accepted-feed>
```

固定已接受 feed `.local/verification/r3/t16/validation-v3/feed`，新消费者不重建产品。独立 CLI home/cache、pinned SDK10.0.203、单节点/noReuse/noSharedCompilation/disable-build-servers。runtime仅TEST/TLS wrapper注入secret，90秒护栏与安全checkpoint，失败stop/release并精确恢复本次对象。

`.local/verification/r3/t17/upload-profile/runs/` 保存命令/退出码、源/输入hash、immutable包前后hash、actualDLL/MVID、数字ACK header、BeforeDecode对应次数和独立validator。只观察旧byte[]/string≥128KiB上传的request26 ACK，未知response opcode/body length不猜，不读opaque token，不冒充新streamAPI支持；详见[R3UploadProfileProbe](../tools/R3UploadProfileProbe/README.md)。rootreview/freeze后Low独占短观察窗口，之后才将真实数字交给输入编码者。
