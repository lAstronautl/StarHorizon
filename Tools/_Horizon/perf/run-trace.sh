#!/usr/bin/env bash
# CPU-профиль сервера под нагрузочным сценарием: подключается dotnet-trace на время фазы замера.
# Использование: Tools/_Horizon/perf/run-trace.sh <метка> <каталог результатов> [секунд трассы] [доп. аргументы сценария...]
# Требуется: dotnet tool install -g dotnet-trace
set -euo pipefail

label="$1"; out="$2"; seconds="${3:-40}"; shift $(( $# < 3 ? $# : 3 ))
root="$(cd "$(dirname "$0")/../../.." && pwd)"
mkdir -p "$out"
out="$(cd "$out" && pwd)"
export DOTNET_gcServer="${DOTNET_gcServer:-1}"
export PATH="$PATH:$HOME/.dotnet/tools"
export DOTNET_ROOT="${DOTNET_ROOT:-$(dirname "$(readlink -f "$(command -v dotnet)")")}"

cd "$root/bin/Content.Benchmarks"
dotnet Content.Benchmarks.dll horizon-load --label "$label" --out "$out" --ticks 6000 "$@" > "$out/$label.log" 2>&1 &
pid=$!

# Ждём конца прогрева, чтобы в трассу попала только установившаяся нагрузка.
until grep -q 'warmup .* ticks in' "$out/$label.log" 2>/dev/null; do
    kill -0 "$pid" 2>/dev/null || { echo "scenario exited early, see $out/$label.log"; exit 1; }
    sleep 2
done

dur=$(printf '00:00:%02d:%02d' $((seconds / 60)) $((seconds % 60)))
dotnet-trace collect -p "$pid" --duration "$dur" --profile dotnet-sampled-thread-time --format Speedscope -o "$out/$label.nettrace" > "$out/$label.trace.log" 2>&1 || true
kill "$pid" 2>/dev/null || true
wait "$pid" 2>/dev/null || true

python3 "$root/Tools/_Horizon/perf/analyze-trace.py" "$out/$label.speedscope.json" > "$out/$label.profile.txt"
echo "profile: $out/$label.profile.txt"
