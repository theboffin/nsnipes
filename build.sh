#!/bin/bash

# NSnipes Build Script
# Builds the complete solution (NSnipes.sln)

set -e  # Exit on error

echo "Building NSnipes solution..."
cd "$(dirname "$0")"

dotnet build NSnipes.sln --configuration Debug

if [ $? -ne 0 ]; then
    echo "Build failed!"
    exit 1
fi

echo "Build succeeded."
