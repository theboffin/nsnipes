# NSnipes gRPC Server Build and Run Script (PowerShell)
# This script runs the gRPC server
# Use -build flag to rebuild before running

param(
    [switch]$build
)

# Exit on error
$ErrorActionPreference = "Stop"

# Check if -build flag is passed
$BUILD_FLAG = $build

Write-Host "🎮 NSnipes gRPC Server - Run"
Write-Host "============================="
Write-Host ""

# Change to the script's directory
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptDir

# Build the project only if -build flag is passed
if ($BUILD_FLAG) {
    Write-Host "📦 Building server project..."
    dotnet build NSnipes.GrpcServer/NSnipes.GrpcServer.csproj --configuration Debug

    if ($LASTEXITCODE -ne 0) {
        Write-Host "❌ Build failed!"
        exit 1
    }

    Write-Host ""
    Write-Host "✅ Build successful!"
    Write-Host ""
} else {
    Write-Host "⏭️  Skipping build (use -build flag to rebuild)"
    Write-Host ""
}

Write-Host "🚀 Starting NSnipes gRPC Server..."
Write-Host ""

# Run the server (without building if -build flag not passed)
if ($BUILD_FLAG) {
    dotnet run --project NSnipes.GrpcServer/NSnipes.GrpcServer.csproj
} else {
    dotnet run --project NSnipes.GrpcServer/NSnipes.GrpcServer.csproj --no-build
}
