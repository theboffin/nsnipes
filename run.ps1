# NSnipes Build and Run Script (PowerShell)
# This script runs the NSnipes game
# Use -build flag to rebuild before running

param(
    [switch]$build
)

# Exit on error
$ErrorActionPreference = "Stop"

# Check if -build flag is passed
$BUILD_FLAG = $build

Write-Host "NSnipes - Run"
Write-Host "============="
Write-Host ""

# Change to the script's directory
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptDir

# Build the project only if -build flag is passed
if ($BUILD_FLAG) {
    Write-Host "Building NSnipes client..."
    dotnet build NSnipes/NSnipes.csproj --configuration Debug --no-dependencies

    if ($LASTEXITCODE -ne 0) {
        Write-Host "Build failed!"
        exit 1
    }

    Write-Host ""
    Write-Host "Build successful!"
    Write-Host ""
} else {
    Write-Host "Skipping build (use -build flag to rebuild)"
    Write-Host ""
}

Write-Host "Starting NSnipes..."
Write-Host ""

# Run the project (without building if -build flag not passed)
if ($BUILD_FLAG) {
    dotnet run --project NSnipes/NSnipes.csproj
} else {
    dotnet run --project NSnipes/NSnipes.csproj --no-build
}
