#!/usr/bin/env bash
# Release-сборка стенда; после сборки гасит фоновые процессы компилятора, чтобы они не мешали замерам.
set -euo pipefail
root="$(cd "$(dirname "$0")/../../.." && pwd)"
cd "$root"
dotnet build Content.Benchmarks/Content.Benchmarks.csproj -c Release -v q -nologo -p:WarningLevel=0 -clp:ErrorsOnly "$@"
dotnet build-server shutdown > /dev/null 2>&1 || true
pkill -f MSBuild.dll 2>/dev/null || true
pkill -f VBCSCompiler 2>/dev/null || true
