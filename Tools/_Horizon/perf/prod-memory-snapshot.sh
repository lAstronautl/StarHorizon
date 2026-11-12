#!/usr/bin/env bash
# Снимок памяти на боевом сервере: кто сколько занимает и сколько работает.
# Запускать на машине сервера; вывод прислать целиком. Ничего не меняет.
echo "== $(date -u +%FT%TZ) uptime: $(uptime -p)"
echo "== процессы по памяти (RSS МБ, время работы сек, команда)"
ps -eo rss,etimes,cmd --sort=-rss | head -15 | awk 'NR==1{print;next}{printf "%8d МБ %9d с  ", $1/1024, $2; for(i=3;i<=NF&&i<12;i++) printf "%s ", $i; print ""}'
echo "== память системы"
free -m
# Если на сервере включены метрики (metrics.enabled=true, порт по умолчанию 44880):
if curl -fsS --max-time 3 http://localhost:44880/metrics -o /tmp/ss14-metrics.txt 2>/dev/null; then
    echo "== метрики игры"
    grep -E '^(robust_entities_count|robust_player_count|dotnet_gc_heap_size_bytes|dotnet_gc_committed|process_working_set_bytes|dotnet_total_memory_bytes)' /tmp/ss14-metrics.txt
else
    echo "== метрики игры недоступны (metrics.enabled=false или другой порт)"
fi
