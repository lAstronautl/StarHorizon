#!/usr/bin/env python3
"""Сравнение двух наборов прогонов horizon-load: compare.py <каталог> <метка A> <метка B> [число систем]."""
import glob
import json
import statistics as st
import sys
from collections import defaultdict


def load(d, label):
    runs = [json.load(open(f)) for f in sorted(glob.glob(f"{d}/{label}-[0-9]*.json"))]
    if not runs:
        sys.exit(f"нет прогонов {label}")
    sysm = defaultdict(float)
    for r in runs:
        for s in r["systems"]:
            sysm[s["name"]] += s["msPerTick"] / len(runs)
    agg = {k: st.mean(r["tickMs"][k] for r in runs) for k in ("mean", "p50", "p95", "p99")}
    agg["alloc"] = st.mean(r["allocBytesPerTick"] for r in runs) / 1024
    agg["cpu"] = st.mean(r["cpuMsPerTick"] for r in runs)
    agg["sd"] = st.pstdev([r["tickMs"]["mean"] for r in runs])
    return len(runs), agg, sysm


d, a, b = sys.argv[1:4]
top = int(sys.argv[4]) if len(sys.argv) > 4 else 20
na, A, SA = load(d, a)
nb, B, SB = load(d, b)
pct = lambda x, y: f"{100 * (y - x) / x:+.1f}%" if x else "n/a"
print(f"{a} ({na} прог.) -> {b} ({nb} прог.)")
for k, name in (("mean", "средний тик, мс"), ("p50", "p50, мс"), ("p95", "p95, мс"), ("p99", "p99, мс"),
                ("cpu", "CPU на тик (все потоки), мс"), ("alloc", "аллокации, КБ/тик")):
    print(f"  {name:30} {A[k]:9.3f} -> {B[k]:9.3f}  {pct(A[k], B[k])}")
print(f"  разброс среднего между прогонами: {A['sd']:.2f} / {B['sd']:.2f} мс")
print("  системы (мс/тик):")
for n in sorted(set(SA) | set(SB), key=lambda n: -abs(SB.get(n, 0) - SA.get(n, 0)))[:top]:
    print(f"    {n:42} {SA.get(n, 0):8.4f} -> {SB.get(n, 0):8.4f}  {pct(SA.get(n, 0), SB.get(n, 0))}")
