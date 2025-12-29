#!/bin/bash

# NSnipes Build and Run Script
# This script builds and runs the NSnipes game

set -e  # Exit on error

echo "🎮 NSnipes - Build and Run"
echo "=========================="
echo ""

# Change to the script's directory
cd "$(dirname "$0")"

# Build the project
echo "📦 Building project..."
dotnet build NSnipes.sln --configuration Debug

if [ $? -ne 0 ]; then
    echo "❌ Build failed!"
    exit 1
fi

echo ""
echo "✅ Build successful!"
echo ""
echo "🚀 Starting NSnipes..."
echo ""

# Run the project
dotnet run --project NSnipes/NSnipes.csproj
