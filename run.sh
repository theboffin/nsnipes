#!/bin/bash

# NSnipes Run Script
# Runs the NSnipes game client

set -e  # Exit on error

echo "NSnipes - Run"
echo "============="
echo ""

cd "$(dirname "$0")"

echo "Starting NSnipes..."
echo ""

dotnet run --project NSnipes/NSnipes.csproj --no-build
