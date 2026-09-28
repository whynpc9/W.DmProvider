# DM.DMPROVIDER

DM .NET DATA PROVIDER



## 更新记录

### 8.3.1.47463

* 连接析构函数优化
* 服务器JSON类型转换优化
* 大字段数据转换优化

### 8.3.1.38775

* 修复DmBulkCopy2精度溢出问题
* 修复TimeOnly插入数据有误问题
* 优化数据类型转换逻辑

### 8.3.1.35543

* 支持.Net9 SDK
* 修复部分参数绑定相关问题
* 快装接口支持Commit方法
* 修复部分连接异常问题
* 修复部分TransactionScope功能相关问题
* 新增ShowExtraInfo参数

### 8.3.1.33449

* 修复了DmConnection.Database属性设置问题
* RS\_CACHE\_SIZE参数默认值同步JDBC改为20

### 8.3.1.32947

* 修复绑定类型为out的参数时相关sql执行失败的问题
* 修复TransactionScope功能相关问题
* 修复结果集读取超长列出错相关问题
* 新增DBAPassword参数
* 补充了DmConnectionStringBuilder连接参数

### 8.3.1.30495

* 支持多租户环境的连接方式
* 修复开启连接池时事务相关问题
* 支持DmFldr接口

### 8.3.1.28188

* 修复AutoCommit相关问题
* 修复连接池复用事务等问题
* 新增schemaSensitive参数
* 新增DmBulkCopy2类支持快速装载
* 新增NET7.0和NET8.0框架支持

### 8.3.1.25526

* 支持refCursor类型参数绑定
* 支持索引表和嵌套表的相关功能
* 修复时间类型精度丢失等问题
* 修复连接池相关问题

### 8.3.1.24059

* 修复enlist与connpooling相关功能
* 支持SavePoint功能
* 修复时间类型相关问题

### 8.3.1.22640

* 修复enlist相关功能

### 8.3.1.22486

* 修复存储过程参数绑定功能
* 新增心跳连接超时
* 支持空间数据类型

### 8.3.1.21595

* 修复DmDataReader.GetChars方法；
* 修复DmClob、DmBlob部分功能

### 8.3.1.21072

* 初次发版

