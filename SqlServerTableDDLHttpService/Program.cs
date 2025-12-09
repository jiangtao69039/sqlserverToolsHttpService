using System.Diagnostics;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// 配置Kestrel监听所有网络接口
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.ListenAnyIP(16904);
});

var app = builder.Build();

// 健康检查endpoint
app.MapGet("/api/health", () =>
{
    return Results.Ok(new { status = "healthy", service = "SqlServerTableDDLHttpService" });
});

// 获取表DDL的POST接口
app.MapPost("/api/tableDDL", async (TableDDLRequest request) =>
{
    try
    {
        // 参数验证
        if (string.IsNullOrWhiteSpace(request.Ip))
            return Results.Ok(new ApiResponse { Code = 9999, Data = "IP地址不能为空" });

        if (request.Port <= 0)
            return Results.Ok(new ApiResponse { Code = 9999, Data = "端口号无效" });

        if (string.IsNullOrWhiteSpace(request.DatabaseName))
            return Results.Ok(new ApiResponse { Code = 9999, Data = "数据库名不能为空" });

        if (string.IsNullOrWhiteSpace(request.UserName))
            return Results.Ok(new ApiResponse { Code = 9999, Data = "用户名不能为空" });

        if (string.IsNullOrWhiteSpace(request.SchemaName))
            return Results.Ok(new ApiResponse { Code = 9999, Data = "Schema名不能为空" });

        if (string.IsNullOrWhiteSpace(request.TableName))
            return Results.Ok(new ApiResponse { Code = 9999, Data = "表名不能为空" });

        Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 启动独立进程处理请求 - 表: [{request.SchemaName}].[{request.TableName}]");

        // 调用独立进程获取DDL
        var result = await InvokeCliToolAsync(request);

        Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 独立进程已完成,内存已释放");

        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 错误: {ex.Message}");
        return Results.Ok(new ApiResponse { Code = 9999, Data = $"错误: {ex.Message}" });
    }
    finally
    {
        // 请求结束后强制垃圾回收
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 垃圾回收已完成");
    }
});

Console.WriteLine("========================================");
Console.WriteLine("SQL Server Table DDL HTTP 服务已启动");
Console.WriteLine("========================================");
Console.WriteLine($"监听端口: 16904");
Console.WriteLine($"健康检查: http://localhost:16904/api/health");
Console.WriteLine($"接口地址: POST http://localhost:16904/api/tableDDL");
Console.WriteLine($"运行模式: 独立进程模型 (调用命令行工具)");
Console.WriteLine("========================================");

app.Run();

// ============================================
// 调用CLI工具获取DDL
// ============================================
static async Task<ApiResponse> InvokeCliToolAsync(TableDDLRequest request)
{
    try
    {
        // 获取CLI工具的路径 (与HTTP服务在同一目录下)
        var currentDir = AppDomain.CurrentDomain.BaseDirectory;
        var cliToolPath = Path.Combine(currentDir, "SqlServerTableDDLService");

        // 如果是Windows系统,添加.exe扩展名
        if (OperatingSystem.IsWindows())
        {
            cliToolPath += ".exe";
        }

        // 检查CLI工具是否存在
        if (!File.Exists(cliToolPath))
        {
            return new ApiResponse
            {
                Code = 9999,
                Data = $"CLI工具不存在: {cliToolPath}. 请确保SqlServerTableDDLService与HttpService在同一目录"
            };
        }

        // 构建命令行参数
        var arguments = $"--ip \"{request.Ip}\" " +
                       $"--port {request.Port} " +
                       $"--databaseName \"{request.DatabaseName}\" " +
                       $"--userName \"{request.UserName}\" " +
                       $"--password \"{request.Password}\" " +
                       $"--schemaName \"{request.SchemaName}\" " +
                       $"--tableName \"{request.TableName}\"";

        // 配置进程启动信息
        var startInfo = new ProcessStartInfo
        {
            FileName = cliToolPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        // 异步读取输出
        process.OutputDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                outputBuilder.AppendLine(e.Data);
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                errorBuilder.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // 等待进程完成,超时60秒
        var completed = await Task.Run(() => process.WaitForExit(60000));

        if (!completed || !process.HasExited)
        {
            try
            {
                process.Kill();
            }
            catch { }
            return new ApiResponse { Code = 9999, Data = "CLI工具执行超时" };
        }

        if (process.ExitCode != 0)
        {
            var error = errorBuilder.ToString();
            Console.WriteLine($"CLI工具错误: {error}");

            // 尝试解析输出中的错误JSON
            var output = outputBuilder.ToString().Trim();
            if (!string.IsNullOrEmpty(output))
            {
                try
                {
                    var errorResult = JsonSerializer.Deserialize<ApiResponse>(output);
                    if (errorResult != null)
                        return errorResult;
                }
                catch { }
            }

            return new ApiResponse { Code = 9999, Data = $"CLI工具执行失败: {error}" };
        }

        // 解析输出
        var resultOutput = outputBuilder.ToString().Trim();
        try
        {
            var result = JsonSerializer.Deserialize<ApiResponse>(resultOutput);
            return result ?? new ApiResponse { Code = 9999, Data = "CLI工具返回了无效的JSON" };
        }
        catch (Exception ex)
        {
            return new ApiResponse
            {
                Code = 9999,
                Data = $"解析CLI工具输出失败: {ex.Message}. 输出: {resultOutput.Substring(0, Math.Min(200, resultOutput.Length))}"
            };
        }
    }
    catch (Exception ex)
    {
        return new ApiResponse { Code = 9999, Data = $"调用CLI工具异常: {ex.Message}" };
    }
}

// ============================================
// 数据模型
// ============================================

// 请求模型
public record TableDDLRequest(
    string Ip,
    int Port,
    string DatabaseName,
    string UserName,
    string Password,
    string SchemaName,
    string TableName
);

// 响应模型
public record ApiResponse
{
    public int Code { get; set; }
    public string Data { get; set; } = string.Empty;
}
