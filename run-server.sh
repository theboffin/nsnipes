#!/bin/bash

# NSnipes gRPC Server Run Script
# Runs the gRPC server

set -e  # Exit on error

echo "NSnipes gRPC Server - Run"
echo "========================="
echo ""

cd "$(dirname "$0")"

if [ -z "$PORT" ]; then
    echo "Using port from appsettings.json (default: 5000)"
    echo "To use a different port, set PORT environment variable: PORT=5001 ./run-server.sh"
    echo ""
fi

echo "Starting NSnipes gRPC Server..."
echo ""

dotnet run --project NSnipes.GrpcServer/NSnipes.GrpcServer.csproj --no-build
