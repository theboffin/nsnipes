#!/bin/bash

# NSnipes gRPC Server Build and Run Script
# This script runs the gRPC server
# Use -build flag to rebuild before running

set -e  # Exit on error

# Check if -build flag is passed
BUILD_FLAG=false
if [ "$1" == "-build" ]; then
    BUILD_FLAG=true
fi

echo "🎮 NSnipes gRPC Server - Run"
echo "============================="
echo ""

# Change to the script's directory
cd "$(dirname "$0")"

# Build the project only if -build flag is passed
if [ "$BUILD_FLAG" == true ]; then
    echo "📦 Building server project..."
    dotnet build NSnipes.GrpcServer/NSnipes.GrpcServer.csproj --configuration Debug

    if [ $? -ne 0 ]; then
        echo "❌ Build failed!"
        exit 1
    fi

    echo ""
    echo "✅ Build successful!"
    echo ""
else
    echo "⏭️  Skipping build (use -build flag to rebuild)"
    echo ""
fi

echo "🚀 Starting NSnipes gRPC Server..."
echo ""

# Check if PORT environment variable is set, otherwise use default from appsettings.json
if [ -z "$PORT" ]; then
    echo "ℹ️  Using port from appsettings.json (default: 5000)"
    echo "   To use a different port, set PORT environment variable: PORT=5001 ./run-server.sh"
    echo ""
fi

# Run the server (without building if -build flag not passed)
if [ "$BUILD_FLAG" == true ]; then
    dotnet run --project NSnipes.GrpcServer/NSnipes.GrpcServer.csproj
else
    dotnet run --project NSnipes.GrpcServer/NSnipes.GrpcServer.csproj --no-build
fi
