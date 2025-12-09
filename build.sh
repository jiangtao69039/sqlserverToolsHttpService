#!/bin/bash

# 构建脚本 - 编译SqlServerTableDDLService和SqlServerTableDDLHttpService

echo "========================================"
echo "开始构建 SQL Server Table DDL 服务"
echo "========================================"

# 设置变量 - 使用绝对路径
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
OUTPUT_DIR="$SCRIPT_DIR/output"
CLI_PROJECT="$SCRIPT_DIR/SqlServerTableDDLService"
HTTP_PROJECT="$SCRIPT_DIR/SqlServerTableDDLHttpService"

# 清理输出目录
echo "清理输出目录..."
rm -rf "$OUTPUT_DIR"
mkdir -p "$OUTPUT_DIR"

# 构建CLI工具
echo ""
echo "========================================"
echo "1. 构建 SqlServerTableDDLService (CLI工具)"
echo "========================================"
cd "$CLI_PROJECT" || exit 1
dotnet publish -c Release -o "$OUTPUT_DIR" -r linux-x64 --self-contained true  /p:PublishSingleFile=true  /p:IncludeNativeLibrariesForSelfExtract=true

if [ $? -ne 0 ]; then
    echo "❌ CLI工具构建失败"
    exit 1
fi

echo "✅ CLI工具构建成功"

# 构建HTTP服务
echo ""
echo "========================================"
echo "2. 构建 SqlServerTableDDLHttpService (HTTP服务)"
echo "========================================"
cd "$HTTP_PROJECT" || exit 1
dotnet publish -c Release -o "$OUTPUT_DIR" -r linux-x64 --self-contained true  /p:PublishSingleFile=true  /p:IncludeNativeLibrariesForSelfExtract=true

if [ $? -ne 0 ]; then
    echo "❌ HTTP服务构建失败"
    exit 1
fi

echo "✅ HTTP服务构建成功"

# 显示输出目录内容
echo ""
echo "========================================"
echo "构建完成！输出目录:"
echo "========================================"
ls -lh "$OUTPUT_DIR" | grep -E "SqlServerTableDDL|总计"

echo ""
echo "========================================"
echo "使用说明"
echo "========================================"
echo "1. CLI工具: ./output/SqlServerTableDDLService --help"
echo "2. HTTP服务: ./output/SqlServerTableDDLHttpService"
echo "3. 两个可执行文件必须在同一目录下运行"
echo "========================================"
