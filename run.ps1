# NSnipes Run Script (PowerShell)
# Runs the NSnipes game client

$ErrorActionPreference = "Stop"

Write-Host "NSnipes - Run"
Write-Host "============="
Write-Host ""

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptDir

Write-Host "Starting NSnipes..."
Write-Host ""

dotnet run --project NSnipes/NSnipes.csproj --no-build
