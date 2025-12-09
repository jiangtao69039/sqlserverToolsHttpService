using Microsoft.SqlServer.Management.Smo;
using Microsoft.SqlServer.Management.Common;
using Microsoft.Data.SqlClient;
using System.Text;
using System.Text.Json;
using System.CommandLine;

// 创建根命令
var rootCommand = new RootCommand("SQL Server Table DDL Generator - 获取表的DDL脚本");

// 定义命令行选项
var ipOption = new Option<string>(
    name: "--ip",
    description: "SQL Server IP地址")
{ IsRequired = true };

var portOption = new Option<int>(
    name: "--port",
    description: "SQL Server端口号")
{ IsRequired = true };

var databaseOption = new Option<string>(
    name: "--databaseName",
    description: "数据库名")
{ IsRequired = true };

var userNameOption = new Option<string>(
    name: "--userName",
    description: "登录用户名")
{ IsRequired = true };

var passwordOption = new Option<string>(
    name: "--password",
    description: "登录密码")
{ IsRequired = true };

var schemaOption = new Option<string>(
    name: "--schemaName",
    description: "Schema名称")
{ IsRequired = true };

var tableOption = new Option<string>(
    name: "--tableName",
    description: "表名")
{ IsRequired = true };

// 添加选项到根命令
rootCommand.AddOption(ipOption);
rootCommand.AddOption(portOption);
rootCommand.AddOption(databaseOption);
rootCommand.AddOption(userNameOption);
rootCommand.AddOption(passwordOption);
rootCommand.AddOption(schemaOption);
rootCommand.AddOption(tableOption);

// 设置处理程序
rootCommand.SetHandler((ip, port, databaseName, userName, password, schemaName, tableName) =>
{
    var result = GetTableDDL(ip, port, databaseName, userName, password, schemaName, tableName);

    // 输出JSON到stdout
    var json = JsonSerializer.Serialize(result);
    Console.WriteLine(json);

    // 根据结果设置退出码
    Environment.Exit(result.Code == 10000 ? 0 : 1);
},
ipOption, portOption, databaseOption, userNameOption, passwordOption, schemaOption, tableOption);

// 执行命令
return await rootCommand.InvokeAsync(args);

// ============================================
// 获取表DDL的核心方法
// ============================================
static ApiResponse GetTableDDL(string ip, int port, string databaseName, string userName, string password, string schemaName, string tableName)
{
    SqlConnection? connection = null;
    ServerConnection? serverConnection = null;

    try
    {
        // 构建连接字符串
        string serverAddress = $"{ip},{port}";
        SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder
        {
            DataSource = serverAddress,
            InitialCatalog = databaseName,
            UserID = userName,
            Password = password,
            TrustServerCertificate = true,
            Encrypt = false,
            ConnectTimeout = 30,
            Pooling = false  // 禁用连接池
        };

        // 创建连接
        connection = new SqlConnection(builder.ConnectionString);
        serverConnection = new ServerConnection(connection);

        // 创建Server对象
        Server server = new Server(serverConnection);
        Database database = server.Databases[databaseName];

        if (database == null)
        {
            return new ApiResponse { Code = 9999, Data = $"数据库 [{databaseName}] 不存在" };
        }

        // 获取表对象
        Table table = database.Tables[tableName, schemaName];

        if (table == null)
        {
            return new ApiResponse { Code = 9999, Data = $"表 [{schemaName}].[{tableName}] 不存在" };
        }

        // 配置脚本选项
        ScriptingOptions options = new ScriptingOptions
        {
            DriAll = true,
            Indexes = true,
            Triggers = true,
            Default = true,
            ScriptDrops = false,
            IncludeIfNotExists = false,
            IncludeHeaders = true,
            ToFileOnly = false,
            AppendToFile = false,
            WithDependencies = false,
            AllowSystemObjects = false,
            Permissions = false,
            TargetServerVersion = SqlServerVersion.Version160
        };
        options.TargetServerVersion = server.Version.Major switch
        {
            8  => SqlServerVersion.Version80,
            9  => SqlServerVersion.Version90,
            10 => SqlServerVersion.Version100,
            11 => SqlServerVersion.Version110,
            12 => SqlServerVersion.Version120,
            13 => SqlServerVersion.Version130,
            14 => SqlServerVersion.Version140,
            15 => SqlServerVersion.Version150,
            16 => SqlServerVersion.Version160,
            _  => SqlServerVersion.Version160
        };

        // 生成DDL脚本
        System.Collections.Specialized.StringCollection scripts = table.Script(options);

        // 合并所有脚本段
        StringBuilder ddlBuilder = new StringBuilder();
        foreach (string? script in scripts)
        {
            if (script is null)
                continue;
            ddlBuilder.AppendLine(script);
            ddlBuilder.AppendLine("GO");
            ddlBuilder.AppendLine();
        }

        string ddl = ddlBuilder.ToString().TrimEnd();

        return new ApiResponse { Code = 10000, Data = ddl };
    }
    catch (SqlException ex)
    {
        return new ApiResponse { Code = 9999, Data = $"SQL Server错误: {ex.Message}" };
    }
    catch (Exception ex)
    {
        return new ApiResponse { Code = 9999, Data = $"错误: {ex.Message}" };
    }
    finally
    {
        // 清理资源
        try
        {
            if (serverConnection != null && serverConnection.IsOpen)
            {
                serverConnection.Disconnect();
            }

            if (connection != null)
            {
                if (connection.State == System.Data.ConnectionState.Open)
                {
                    connection.Close();
                }
                connection.Dispose();
            }
        }
        catch
        {
            // 忽略清理错误
        }
    }
}

// ============================================
// 响应模型
// ============================================
public record ApiResponse
{
    public int Code { get; set; }
    public string Data { get; set; } = string.Empty;
}
