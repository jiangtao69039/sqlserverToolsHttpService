# SQL Server Table DDL 服务

独立进程模型的SQL Server表结构DDL获取服务，彻底解决内存累积问题。

## 架构设计

本项目采用**独立进程模型**，将服务拆分为两个独立的可执行文件：

### 1. SqlServerTableDDLService (CLI工具)
- 纯命令行工具
- 接收命令行参数，查询SQL Server表的DDL
- 输出JSON格式的结果
- 每次调用完成后进程退出，内存完全释放

### 2. SqlServerTableDDLHttpService (HTTP服务)
- HTTP API服务器，监听16904端口
- 接收HTTP请求
- 启动独立的CLI工具进程处理请求
- 将CLI工具的输出返回给HTTP客户端
- 完全隔离内存，避免内存累积

## 目录结构

```
sqlserverToolsHttpService/
├── SqlServerTableDDLService/          # CLI工具项目
│   ├── Program.cs                     # CLI工具主程序
│   └── SqlServerTableDDLService.csproj
├── SqlServerTableDDLHttpService/      # HTTP服务项目
│   ├── Program.cs                     # HTTP服务主程序
│   └── SqlServerTableDDLHttpService.csproj
├── build.sh                           # 构建脚本
└── README.md                          # 本文件
```

## 构建项目

运行构建脚本：

```bash
./build.sh
```

构建完成后，所有可执行文件会输出到 `output/` 目录。

## 使用方法

### 方法一：使用CLI工具（命令行）

```bash
cd output
./SqlServerTableDDLService \
  --ip "192.168.1.100" \
  --port 1433 \
  --databaseName "MyDatabase" \
  --userName "sa" \
  --password "YourPassword" \
  --schemaName "dbo" \
  --tableName "Users"
```

输出示例：
```json
{"Code":10000,"Data":"CREATE TABLE [dbo].[Users](...)\nGO\n..."}
```

### 方法二：使用HTTP服务

1. 启动HTTP服务：
```bash
cd output
./SqlServerTableDDLHttpService
```

2. 调用HTTP API：
```bash
curl -X POST http://localhost:16904/api/tableDDL \
  -H "Content-Type: application/json" \
  -d '{
    "Ip": "192.168.1.100",
    "Port": 1433,
    "DatabaseName": "MyDatabase",
    "UserName": "sa",
    "Password": "YourPassword",
    "SchemaName": "dbo",
    "TableName": "Users"
  }'
```

3. 健康检查：
```bash
curl http://localhost:16904/api/health
```

## API接口说明

### POST /api/tableDDL

获取表的DDL脚本

**请求体：**
```json
{
  "Ip": "string",           // SQL Server IP地址
  "Port": 1433,             // 端口号
  "DatabaseName": "string", // 数据库名
  "UserName": "string",     // 登录用户名
  "Password": "string",     // 登录密码
  "SchemaName": "string",   // Schema名称
  "TableName": "string"     // 表名
}
```

**响应：**
```json
{
  "Code": 10000,            // 10000=成功, 9999=失败
  "Data": "string"          // 成功时返回DDL脚本，失败时返回错误信息
}
```

### GET /api/health

健康检查接口

**响应：**
```json
{
  "status": "healthy",
  "service": "SqlServerTableDDLHttpService"
}
```

## 内存管理优势

### 独立进程模型的优势：

1. **完全内存隔离**：每个请求的worker进程退出后，内存真正还给操作系统
2. **彻底解决泄漏**：即使存在内存泄漏，进程退出后也会被清理
3. **稳定可靠**：不依赖GC机制，内存管理由操作系统保证

### 性能考虑：

- 进程启动开销：每次请求需要几百毫秒启动新进程
- 适用场景：请求频率不高（每分钟几次到几十次）
- 不适用：高频调用场景（每秒多次）

## 部署说明

1. 确保 `SqlServerTableDDLService` 和 `SqlServerTableDDLHttpService` 在同一目录
2. 启动HTTP服务即可，它会自动调用CLI工具
3. 建议使用systemd或supervisor管理HTTP服务进程

## 技术栈

- .NET 8.0
- ASP.NET Core (Minimal API)
- SQL Server Management Objects (SMO)
- System.CommandLine

## 故障排查

### CLI工具不存在错误

如果HTTP服务报错"CLI工具不存在"，请确保：
1. `SqlServerTableDDLService` 和 `SqlServerTableDDLHttpService` 在同一目录
2. CLI工具有执行权限：`chmod +x SqlServerTableDDLService`

### 连接SQL Server失败

检查：
1. SQL Server地址和端口是否正确
2. 用户名和密码是否正确
3. SQL Server是否允许远程连接
4. 防火墙是否允许访问

### 进程超时

如果表结构非常复杂，可能需要调整超时时间（默认60秒）。
修改 `SqlServerTableDDLHttpService/Program.cs` 中的超时参数。

## 许可证

本项目仅供内部使用。
