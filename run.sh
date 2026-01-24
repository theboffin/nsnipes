#!/bin/bash

# NSnipes Build and Run Script
# This script runs the NSnipes game
# Use -build flag to rebuild before running

set -e  # Exit on error

# Check if -build flag is passed
BUILD_FLAG=false
if [ "$1" == "-build" ]; then
    BUILD_FLAG=true
fi

echo "NSnipes - Run"
echo "============="
echo ""

# Change to the script's directory
cd "$(dirname "$0")"

# Build the project only if -build flag is passed
if [ "$BUILD_FLAG" == true ]; then
    echo "Building NSnipes client..."
    dotnet build NSnipes/NSnipes.csproj --configuration Debug --no-dependencies

    if [ $? -ne 0 ]; then
        echo "Build failed!"
        exit 1
    fi

    echo ""
    echo "Build successful!"
    echo ""
else
    echo "Skipping build (use -build flag to rebuild)"
    echo ""
fi

echo "Starting NSnipes..."
echo ""

# Run the project (without building if -build flag not passed)
if [ "$BUILD_FLAG" == true ]; then
    dotnet run --project NSnipes/NSnipes.csproj
else
    dotnet run --project NSnipes/NSnipes.csproj --no-build
fi
