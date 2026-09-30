#!/bin/sh
set -eu
cd "$(dirname "$0")/.."
release_rid=${1:-osx-arm64}
case "$release_rid" in
  osx-arm64|linux-arm64) ;;
  *) echo 'Supported publish targets: osx-arm64, linux-arm64' >&2; exit 2 ;;
esac
proto run dotnet -- publish src/DesignLinter/DesignLinter.fsproj \
  -c Release -r "$release_rid" --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:PublishTrimmed=false -p:RestoreLockedMode=true -o "artifacts/$release_rid"
