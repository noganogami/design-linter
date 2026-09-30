#!/bin/sh
set -eu
cd "$(dirname "$0")/.."
proto run dotnet -- restore DesignLinter.slnx --locked-mode
proto run dotnet -- build DesignLinter.slnx --no-restore
proto run dotnet -- run --project tests/DesignLinter.Tests --no-build --no-restore
