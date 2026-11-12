#!/usr/bin/env python3
"""Разбор трассы dotnet-trace (формат speedscope): где игровой поток сервера проводит время.

Использование: analyze-trace.py <файл.speedscope.json> [число строк]
Печатает для игрового потока (тот, где больше всего времени под BaseServer.Update)
включающее (inclusive) и собственное (exclusive) время по методам в процентах от времени тика.
"""
import json
import sys
from collections import defaultdict

ROOT_MARK = "Robust.Server.BaseServer.Update"


def load(path):
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def analyze(profile, frames):
    """Возвращает (время под ROOT_MARK, inclusive, exclusive) для одного потока."""
    inclusive = defaultdict(float)
    exclusive = defaultdict(float)
    stack = []  # (frame, open_at, child_time)
    root_depth = None
    root_total = 0.0
    for ev in profile["events"]:
        name = frames[ev["frame"]]["name"]
        at = ev["at"]
        if ev["type"] == "O":
            stack.append([name, at, 0.0])
            if root_depth is None and ROOT_MARK in name:
                root_depth = len(stack)
        else:
            frame, opened, child = stack.pop()
            dur = at - opened
            if stack:
                stack[-1][2] += dur
            inside = root_depth is not None and len(stack) + 1 >= root_depth
            if inside:
                # Рекурсию считаем один раз: только если этого метода нет выше по стеку.
                if not any(s[0] == frame for s in stack[root_depth - 1:]):
                    inclusive[frame] += dur
                exclusive[frame] += dur - child
            if root_depth is not None and len(stack) + 1 == root_depth:
                root_total += dur
                root_depth = None
    return root_total, inclusive, exclusive


def main():
    path = sys.argv[1]
    top = int(sys.argv[2]) if len(sys.argv) > 2 else 70
    data = load(path)
    frames = data["shared"]["frames"]
    best = None
    for profile in data["profiles"]:
        if profile.get("type") != "evented":
            continue
        total, inc, exc = analyze(profile, frames)
        if best is None or total > best[0]:
            best = (total, inc, exc, profile.get("name", "?"))

    if best is None or best[0] <= 0:
        print("игровой поток не найден")
        return

    total, inc, exc, name = best
    print(f"Игровой поток: {name}; время под {ROOT_MARK}: {total:.1f} мс (100%)\n")
    print(f"=== Включающее время (inclusive), топ {top} ===")
    for frame, t in sorted(inc.items(), key=lambda kv: -kv[1])[:top]:
        print(f"{100 * t / total:6.2f}%  {frame}")
    print(f"\n=== Собственное время (exclusive), топ {top} ===")
    for frame, t in sorted(exc.items(), key=lambda kv: -kv[1])[:top]:
        print(f"{100 * t / total:6.2f}%  {frame}")


if __name__ == "__main__":
    main()
