#!/usr/bin/env bash
# Нагрузочный замер сервера StarHorizon.
# Использование: Tools/_Horizon/perf/run-load.sh <метка> <каталог результатов> [число прогонов] [доп. аргументы сценария...]
# Переменные окружения .NET (DOTNET_gcServer, DOTNET_GCgen0size и т.п.) наследуются процессом,
# поэтому варианты настроек рантайма сравниваются простым их выставлением перед запуском.
# Перед запуском: dotnet build Content.Benchmarks -c Release
set -euo pipefail

label="$1"; out="$2"; runs="${3:-3}"; shift $(( $# < 3 ? $# : 3 ))
root="$(cd "$(dirname "$0")/../../.." && pwd)"
mkdir -p "$out"
out="$(cd "$out" && pwd)"

# Боевой сервер собирается с Server GC (Content.Server.csproj), повторяем это для стенда.
export DOTNET_gcServer="${DOTNET_gcServer:-1}"

cd "$root/bin/Content.Benchmarks"
for i in $(seq 1 "$runs"); do
    dotnet Content.Benchmarks.dll horizon-load --label "${label}-${i}" --out "$out" "$@" > "$out/${label}-${i}.log" 2>&1 \
        || echo "run ${label}-${i} failed, see $out/${label}-${i}.log"
    grep -E 'RESULT' "$out/${label}-${i}.log" || true
done
