# NSnipes gRPC Server Run Script (PowerShell)
# Runs the gRPC server

$ErrorActionPreference = "Stop"

Write-Host "NSnipes gRPC Server - Run"
Write-Host "========================="
Write-Host ""

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptDir

Write-Host "Starting NSnipes gRPC Server..."
Write-Host ""

dotnet run --project NSnipes.GrpcServer/NSnipes.GrpcServer.csproj --no-build
