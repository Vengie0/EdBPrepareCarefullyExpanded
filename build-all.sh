#!/usr/bin/env bash

set -e

# Get the directory containing this script
SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"

echo "Building RimWorld 1.6..."
dotnet build "$SCRIPT_DIR/build16.csproj" -c Release

echo "Building RimWorld 1.7..."
dotnet build "$SCRIPT_DIR/build17.csproj" -c Release

echo "Copying Resources into dist..."

mkdir -p "$SCRIPT_DIR/dist"

cp -a "$SCRIPT_DIR/Resources/." "$SCRIPT_DIR/dist/"

echo "Both builds and resource copying completed successfully!"
