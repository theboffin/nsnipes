# NSnipes Build Script (PowerShell)
# Builds the complete solution (NSnipes.sln)

$ErrorActionPreference = "Stop"

Write-Host "Building NSnipes solution..."
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptDir

dotnet build NSnipes.sln --configuration Debug

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!"
    exit 1
}

Write-Host "Build succeeded."
