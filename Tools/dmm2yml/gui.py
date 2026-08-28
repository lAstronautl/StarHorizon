"""A window for dmm2yml, so the substitution table can be filled without a spreadsheet.

The command line is fine for scanning and converting, but the part a person
actually spends time on -- deciding what each SS13 path becomes -- is a poor fit
for a CSV in Excel: nothing there knows which prototype ids exist, so a typo
looks the same as a correct answer until the conversion refuses it.

Here the same table is editable in place, every id is completed and checked
against Resources/Prototypes as it is typed, and the map is built from the same
code the CLI uses (dmm2yml.build_map and friends) rather than a second copy of it.

Run it with `python3 dmm2yml.py gui`, or straight from this file.
"""

from __future__ import annotations

import csv
import os
import queue
import sys
import threading
import tkinter as tk
from dataclasses import dataclass
from tkinter import filedialog, messagebox, ttk

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import dmm2yml
import dmmparser
import mappings as mapping_rules
import protoindex

# column id, heading, width, alignment
COLUMNS = (
    ("path", "путь SS13", 400, "w"),
    ("kind", "тип", 70, "w"),
    ("count", "шт.", 60, "e"),
    ("example", "пример", 80, "e"),
    ("suggestion", "подсказка", 180, "w"),
    ("value", "замена в SS14", 230, "w"),
)
EDIT_COLUMN = "value"

VALID_BG = "#e4f3e4"
INVALID_BG = "#fbe0e0"
SKIP_FG = "#8a8a8a"
VALID_FG = "#146c14"
INVALID_FG = "#a11212"


@dataclass
class Row:
    """One unresolved SS13 path, and what the person decided about it."""

    path: str
    kind: str
    count: int
    example: str
    suggestion: str
    value: str = ""
    color: str = ""

    def state(self, index: protoindex.ProtoIndex | None) -> str:
        if not self.value:
            return "empty"
        if self.value.lower() == mapping_rules.SKIP:
            return "skip"
        if index is None:
            return "empty"
        wanted = {"turf": protoindex.TILE, "decal": protoindex.DECAL}.get(self.kind, protoindex.ENTITY)
        parts = [part.strip() for part in self.value.split(dmm2yml.MULTI_SEPARATOR) if part.strip()]
        # A turf may name a tile, an entity, or one of each.
        if self.kind == "turf":
            ok = all(
                index.has(protoindex.TILE, part) or index.has(protoindex.ENTITY, part)
                for part in parts
            )
        else:
            ok = all(index.has(wanted, part) or index.has(protoindex.ENTITY, part) for part in parts)
        return "ok" if ok and parts else "bad"


class App(ttk.Frame):
    def __init__(self, master: tk.Tk, mapping_dir: str, prototypes: str, z: int | None, variants: str):
        super().__init__(master, padding=8)
        self.master = master
        self.mapping_dir = mapping_dir
        self.prototypes = prototypes
        self.z_level = z
        self.variants = variants

        self.index: protoindex.ProtoIndex | None = None
        self.rows: dict[str, Row] = {}
        self.dmm_cache: tuple[str, dmmparser.DmmMap] | None = None
        self.events: queue.Queue = queue.Queue()
        self.busy = False

        self.dmm_path = tk.StringVar()
        self.output_path = tk.StringVar()
        self.z_choice = tk.StringVar()
        self.filter_text = tk.StringVar()
        self.only_unfilled = tk.BooleanVar(value=False)
        self.status = tk.StringVar(value="индекс прототипов загружается...")

        self._editor: ttk.Entry | None = None
        self._editing: str | None = None
        self._popup: tk.Toplevel | None = None
        self._popup_list: tk.Listbox | None = None
        self._edit_bbox: tuple[int, int, int, int] = (0, 0, 0, 0)

        self._build()
        self.pack(fill="both", expand=True)
        self.filter_text.trace_add("write", lambda *_: self._refill())
        self.only_unfilled.trace_add("write", lambda *_: self._refill())

        self._run_async("индекс", lambda: protoindex.build(self.prototypes))
        self.after(80, self._drain)

    # ---------------------------------------------------------------- layout

    def _build(self) -> None:
        files = ttk.LabelFrame(self, text="Файлы", padding=6)
        files.pack(fill="x")
        files.columnconfigure(1, weight=1)

        ttk.Label(files, text="Карта .dmm").grid(row=0, column=0, sticky="w", padx=(0, 6))
        ttk.Entry(files, textvariable=self.dmm_path).grid(row=0, column=1, sticky="ew")
        ttk.Button(files, text="Обзор...", command=self._pick_dmm).grid(row=0, column=2, padx=(6, 0))
        ttk.Label(files, text="Уровень (z)").grid(row=0, column=3, sticky="w", padx=(12, 6))
        self.z_combo = ttk.Combobox(files, textvariable=self.z_choice, width=5, state="readonly")
        self.z_combo.grid(row=0, column=4)

        ttk.Label(files, text="Результат .yml").grid(row=1, column=0, sticky="w", pady=(4, 0), padx=(0, 6))
        ttk.Entry(files, textvariable=self.output_path).grid(row=1, column=1, sticky="ew", pady=(4, 0))
        ttk.Button(files, text="Обзор...", command=self._pick_output).grid(row=1, column=2, pady=(4, 0), padx=(6, 0))

        actions = ttk.Frame(self, padding=(0, 6))
        actions.pack(fill="x")
        self.buttons: dict[str, ttk.Button] = {}
        for key, text, command in (
            ("scan", "Разобрать карту", self._do_scan),
            ("convert", "Собрать карту", self._do_convert),
            ("merge", "Сохранить в словари", self._do_merge),
            ("load", "Открыть таблицу...", self._load_table),
            ("save", "Сохранить таблицу...", self._save_table),
            ("test", "Проверка", self._do_selftest),
        ):
            button = ttk.Button(actions, text=text, command=command)
            button.pack(side="left", padx=(0, 6))
            self.buttons[key] = button

        table = ttk.LabelFrame(self, text="Не сопоставлено", padding=6)
        table.pack(fill="both", expand=True)

        controls = ttk.Frame(table)
        controls.pack(fill="x", pady=(0, 6))
        ttk.Label(controls, text="Фильтр").pack(side="left")
        ttk.Entry(controls, textvariable=self.filter_text, width=28).pack(side="left", padx=(4, 10))
        ttk.Checkbutton(controls, text="только незаполненные", variable=self.only_unfilled).pack(side="left")
        ttk.Label(controls, textvariable=self.status).pack(side="right")

        holder = ttk.Frame(table)
        holder.pack(fill="both", expand=True)
        self.tree = ttk.Treeview(holder, columns=[c[0] for c in COLUMNS], show="headings", selectmode="extended")
        for name, heading, width, anchor in COLUMNS:
            self.tree.heading(name, text=heading, command=lambda n=name: self._sort_by(n))
            self.tree.column(name, width=width, anchor=anchor, stretch=(name == "path"))
        scroll = ttk.Scrollbar(holder, orient="vertical", command=self.tree.yview)
        self.tree.configure(yscrollcommand=scroll.set)
        self.tree.pack(side="left", fill="both", expand=True)
        scroll.pack(side="right", fill="y")

        self.tree.tag_configure("ok", background=VALID_BG)
        self.tree.tag_configure("bad", background=INVALID_BG)
        self.tree.tag_configure("skip", foreground=SKIP_FG)

        self.tree.bind("<Double-1>", self._begin_edit)
        self.tree.bind("<Return>", self._begin_edit)
        self.tree.bind("<Button-1>", self._close_editor_if_open, add="+")

        bulk = ttk.Frame(table)
        bulk.pack(fill="x", pady=(6, 0))
        ttk.Button(bulk, text="Пропустить выбранные", command=self._bulk_skip).pack(side="left")
        ttk.Button(bulk, text="Взять подсказку", command=self._bulk_suggestion).pack(side="left", padx=6)
        ttk.Button(bulk, text="Очистить выбранные", command=self._bulk_clear).pack(side="left")
        ttk.Label(bulk, text="двойной клик по строке — ввод замены").pack(side="right")

        log_frame = ttk.LabelFrame(self, text="Журнал", padding=6)
        log_frame.pack(fill="both")
        self.progress = ttk.Progressbar(log_frame, mode="indeterminate")
        self.progress.pack(fill="x", pady=(0, 4))
        self.log_text = tk.Text(log_frame, height=9, wrap="none")
        log_scroll = ttk.Scrollbar(log_frame, orient="vertical", command=self.log_text.yview)
        self.log_text.configure(yscrollcommand=log_scroll.set, state="disabled")
        self.log_text.pack(side="left", fill="both", expand=True)
        log_scroll.pack(side="right", fill="y")

    # ---------------------------------------------------------------- basics

    def log(self, text: str = "") -> None:
        self.log_text.configure(state="normal")
        self.log_text.insert("end", text + "\n")
        self.log_text.see("end")
        self.log_text.configure(state="disabled")

    def _set_busy(self, busy: bool) -> None:
        self.busy = busy
        for button in self.buttons.values():
            button.state(["disabled"] if busy else ["!disabled"])
        if busy:
            self.progress.start(12)
        else:
            self.progress.stop()

    def _run_async(self, name: str, work) -> None:
        """Run slow work off the UI thread; results come back through the queue."""
        if self.busy:
            return
        self._set_busy(True)

        def worker():
            try:
                self.events.put(("done", name, work()))
            except Exception as error:  # reported in the log, never as a stack trace
                self.events.put(("error", name, error))

        threading.Thread(target=worker, daemon=True).start()

    def _drain(self) -> None:
        while True:
            try:
                kind, name, payload = self.events.get_nowait()
            except queue.Empty:
                break
            if kind == "log":
                self.log(payload)
                continue
            self._set_busy(False)
            if kind == "error":
                self.log(f"[{name}] ошибка: {type(payload).__name__}: {payload}")
                messagebox.showerror("Ошибка", f"{name}: {payload}")
            else:
                self._finished(name, payload)
        self.after(80, self._drain)

    def _thread_log(self, text: str) -> None:
        self.events.put(("log", "", text))

    # ---------------------------------------------------------------- files

    def _pick_dmm(self) -> None:
        path = filedialog.askopenfilename(
            title="Карта SS13", filetypes=[("Карты BYOND", "*.dmm"), ("Все файлы", "*.*")]
        )
        if path:
            self.dmm_path.set(path)
            if not self.output_path.get():
                base = os.path.splitext(os.path.basename(path))[0].lower()
                self.output_path.set(os.path.join(os.path.dirname(path), base + ".yml"))
            self.dmm_cache = None
            self.z_choice.set("")
            self.z_combo["values"] = ()
            self._probe_z_levels()

    def _probe_z_levels(self) -> None:
        """SS14 has no stacked floors, so a multi-z .dmm needs one picked by hand
        before scan/convert can run -- see _ready(). Parsing the whole file is
        the only way to know how many z-levels it has, so this runs off the UI
        thread and _finished() fills the dropdown once it is back."""

        def work():
            return self._parsed_map().z_levels

        self._run_async("z-levels", work)

    def _pick_output(self) -> None:
        path = filedialog.asksaveasfilename(
            title="Куда сохранить карту SS14", defaultextension=".yml",
            filetypes=[("Карты SS14", "*.yml"), ("Все файлы", "*.*")],
        )
        if path:
            self.output_path.set(path)

    def _parsed_map(self) -> dmmparser.DmmMap:
        path = self.dmm_path.get()
        if self.dmm_cache and self.dmm_cache[0] == path:
            return self.dmm_cache[1]
        parsed = dmmparser.parse(path)
        self.dmm_cache = (path, parsed)
        return parsed

    # ---------------------------------------------------------------- actions

    def _do_scan(self) -> None:
        if not self._ready():
            return

        z = int(self.z_choice.get())

        def work():
            dmm = self._parsed_map()
            mapping_set = mapping_rules.load(self.mapping_dir)
            self._thread_log(f"{os.path.basename(self.dmm_path.get())}: {dmm.width}x{dmm.height}, z={z}")
            survey = dmm2yml.walk(dmm, mapping_set, self.index, z, None, self.variants)
            return survey

        self._run_async("scan", work)

    def _do_convert(self) -> None:
        if not self._ready():
            return
        if not self.output_path.get():
            messagebox.showwarning("Некуда сохранять", "Укажите файл результата .yml")
            return

        table = {
            row.path: {"dmm_path": row.path, "kind": row.kind, "ss14_id": row.value, "color": row.color}
            for row in self.rows.values()
        }
        output = self.output_path.get()
        z = int(self.z_choice.get())

        def work():
            dmm = self._parsed_map()
            mapping_set = mapping_rules.load(self.mapping_dir)

            problems = dmm2yml.apply_table(table, mapping_set, self.index)
            problems += dmm2yml.collect_problems(
                dmm2yml.walk(dmm, mapping_set, self.index, z, None, self.variants)
            )
            lines = dmm2yml.format_problems(problems)
            if lines:
                return ("refused", lines)

            builder = dmm2yml.build_map(dmm, mapping_set, self.index, z, self.variants, self.mapping_dir)
            with open(output, "w", encoding="utf-8", newline="\n") as handle:
                handle.write(builder.render())
            self._thread_log(f"записано: {output}")
            dmm2yml.describe_map(builder, self._thread_log)
            dmm2yml.verify(builder, self._thread_log)
            return ("written", output)

        self._run_async("convert", work)

    def _do_merge(self) -> None:
        if self.index is None or not self.rows:
            messagebox.showinfo("Нечего сохранять", "Сначала разберите карту и заполните замены.")
            return
        decided = {path: row for path, row in self.rows.items() if row.value}
        if not decided:
            messagebox.showinfo("Нечего сохранять", "Ни одной замены не заполнено.")
            return
        if not messagebox.askyesno(
            "Сохранить в словари",
            f"Дописать {len(decided)} правил(о) в {self.mapping_dir}?\n"
            "Они начнут применяться ко всем картам.",
        ):
            return

        table = {
            row.path: {"dmm_path": row.path, "kind": row.kind, "ss14_id": row.value, "color": row.color}
            for row in decided.values()
        }

        def work():
            written = dmm2yml.merge_table(table, self.mapping_dir, self.prototypes, self._thread_log)
            return ("merged", written)

        self._run_async("merge", work)

    def _do_selftest(self) -> None:
        def work():
            import selftest

            return ("selftest", selftest.run(dmm2yml.REPO_ROOT, self.mapping_dir, self.prototypes))

        self._run_async("selftest", work)

    def _finished(self, name: str, payload) -> None:
        if name == "z-levels":
            levels = payload
            self.z_combo["values"] = [str(z) for z in levels]
            if len(levels) == 1:
                self.z_choice.set(str(levels[0]))
            elif self.z_level is not None and self.z_level in levels:
                self.z_choice.set(str(self.z_level))
            else:
                self.z_choice.set("")
                self.log(f"уровни карты: {levels} -- выберите нужный в списке «Уровень (z)»")
            return

        if name == "индекс":
            self.index = payload
            self.log(
                f"индекс прототипов: {len(payload.entities)} сущностей, "
                f"{len(payload.tiles)} тайлов, {len(payload.decals)} декалей"
            )
            self._update_status()
            # --dmm at launch sets the path before this (the very first async job)
            # finishes, so _run_async's busy guard would have swallowed a probe
            # fired then. Fire it now instead, once the index job has cleared.
            if self.dmm_path.get() and not self.z_combo["values"]:
                self._probe_z_levels()
            return

        if name == "scan":
            self.rows = {
                report.path: Row(
                    path=report.path,
                    kind=report.kind,
                    count=report.count,
                    example=report.example,
                    suggestion=dmm2yml.suggest_for(self.index, report.path, report.kind),
                )
                for report in payload.unresolved.values()
            }
            self.log(
                f"разобрано: {payload.resolved_count} атомов по словарям, "
                f"{payload.skipped_count} пропущено, "
                f"{sum(r.count for r in self.rows.values())} без правила "
                f"({len(self.rows)} путей)"
            )
            self._refill()
            return

        if name == "convert":
            outcome, data = payload
            if outcome == "refused":
                self.log(f"конвертация отменена: {len(data)} путь(ей) без решения")
                for line in data[:15]:
                    self.log(f"  {line}")
                if len(data) > 15:
                    self.log(f"  ... и ещё {len(data) - 15}")
                messagebox.showwarning(
                    "Не хватает решений",
                    f"{len(data)} путь(ей) без замены. Заполните колонку «замена в SS14» "
                    f"или отметьте их как «{mapping_rules.SKIP}».",
                )
            else:
                messagebox.showinfo("Готово", f"Карта записана:\n{data}")
            return

        if name == "merge":
            _, written = payload
            self.log(f"дописано правил: {written}")
            messagebox.showinfo("Готово", f"В словари дописано правил: {written}")
            return

        if name == "selftest":
            _, results = payload
            width = max(len(result.name) for result in results)
            for result in results:
                self.log(f"  [{'ok  ' if result.passed else 'FAIL'}] {result.name.ljust(width)}  {result.detail}")
            failed = [r for r in results if not r.passed]
            self.log(f"провалено {len(failed)} из {len(results)}" if failed else f"все {len(results)} проверок пройдены")

    def _ready(self) -> bool:
        if self.index is None:
            messagebox.showinfo("Ещё не готово", "Индекс прототипов загружается, подождите секунду.")
            return False
        if not self.dmm_path.get() or not os.path.exists(self.dmm_path.get()):
            messagebox.showwarning("Нет карты", "Выберите файл .dmm")
            return False
        if not self.z_combo["values"]:
            messagebox.showinfo("Ещё не готово", "Идёт разбор уровней карты, подождите секунду.")
            return False
        if not self.z_choice.get():
            messagebox.showwarning(
                "Выберите уровень",
                "У карты несколько уровней (z), а в SS14 нет многоэтажных зданий -- "
                "каждый уровень становится отдельной картой. Выберите один в списке «Уровень (z)».",
            )
            return False
        return True

    # ---------------------------------------------------------------- table

    def _visible_rows(self) -> list[Row]:
        needle = self.filter_text.get().strip().lower()
        rows = [
            row for row in self.rows.values()
            if (not needle or needle in row.path.lower() or needle in row.value.lower())
            and (not self.only_unfilled.get() or not row.value)
        ]
        rows.sort(key=lambda row: (-row.count, row.path))
        return rows

    def _refill(self) -> None:
        self._close_editor()
        self.tree.delete(*self.tree.get_children())
        for row in self._visible_rows():
            self.tree.insert("", "end", iid=row.path, values=self._row_values(row), tags=(row.state(self.index),))
        self._update_status()

    @staticmethod
    def _row_values(row: Row) -> tuple:
        return (row.path, row.kind, row.count, row.example, row.suggestion, row.value)

    def _refresh_row(self, path: str) -> None:
        row = self.rows[path]
        if self.tree.exists(path):
            self.tree.item(path, values=self._row_values(row), tags=(row.state(self.index),))
        self._update_status()

    def _update_status(self) -> None:
        if not self.rows:
            self.status.set("индекс готов" if self.index else "индекс прототипов загружается...")
            return
        filled = sum(1 for row in self.rows.values() if row.value)
        bad = sum(1 for row in self.rows.values() if row.state(self.index) == "bad")
        text = f"заполнено {filled} из {len(self.rows)}"
        if bad:
            text += f", неизвестных id: {bad}"
        self.status.set(text)

    def _sort_by(self, column: str) -> None:
        rows = self._visible_rows()
        key = {
            "path": lambda r: r.path,
            "kind": lambda r: (r.kind, r.path),
            "count": lambda r: -r.count,
            "example": lambda r: r.example,
            "suggestion": lambda r: (r.suggestion == "", r.suggestion),
            "value": lambda r: (r.value == "", r.value),
        }[column]
        self._close_editor()
        self.tree.delete(*self.tree.get_children())
        for row in sorted(rows, key=key):
            self.tree.insert("", "end", iid=row.path, values=self._row_values(row), tags=(row.state(self.index),))

    def _selected(self) -> list[str]:
        return [iid for iid in self.tree.selection() if iid in self.rows]

    def _bulk_set(self, value_for) -> None:
        changed = 0
        for iid in self._selected():
            value = value_for(self.rows[iid])
            if value is not None and value != self.rows[iid].value:
                self.rows[iid].value = value
                self._refresh_row(iid)
                changed += 1
        if changed:
            self.log(f"изменено строк: {changed}")
            if self.only_unfilled.get():
                self._refill()

    def _bulk_skip(self) -> None:
        self._bulk_set(lambda row: mapping_rules.SKIP)

    def _bulk_suggestion(self) -> None:
        self._bulk_set(lambda row: row.suggestion or None)

    def _bulk_clear(self) -> None:
        self._bulk_set(lambda row: "")

    # ---------------------------------------------------------------- editing

    def _begin_edit(self, event=None) -> str | None:
        self._close_editor()
        iid = self.tree.identify_row(event.y) if event and event.type == "4" else self.tree.focus()
        if not iid or iid not in self.rows:
            return None
        self.tree.selection_set(iid)
        self.tree.focus(iid)
        bbox = self.tree.bbox(iid, EDIT_COLUMN)
        if not bbox:
            return None

        x, y, width, height = bbox
        # Remember where the cell is. Asking the editor for its own screen
        # position right after place() returns the value it had before being
        # mapped, which put the completion list in the window's top-left corner.
        self._edit_bbox = bbox
        self._editing = iid
        self._editor = ttk.Entry(self.tree)
        self._editor.place(x=x, y=y, width=width, height=height)
        self._editor.insert(0, self.rows[iid].value)
        self._editor.select_range(0, "end")
        self._editor.focus_set()
        self._editor.bind("<KeyRelease>", self._on_typing)
        self._editor.bind("<Return>", lambda e: self._commit_edit())
        self._editor.bind("<Escape>", lambda e: self._close_editor())
        self._editor.bind("<Down>", lambda e: self._move_popup(1))
        self._editor.bind("<Up>", lambda e: self._move_popup(-1))
        self._on_typing()
        return "break"

    def _on_typing(self, event=None) -> None:
        if self._editor is None or self._editing is None:
            return
        if event is not None and event.keysym in ("Up", "Down", "Return", "Escape"):
            return

        text = self._editor.get().strip()
        row = self.rows[self._editing]
        probe = Row(row.path, row.kind, row.count, row.example, row.suggestion, text)
        state = probe.state(self.index)
        self._editor.configure(
            foreground={"ok": VALID_FG, "bad": INVALID_FG, "skip": SKIP_FG}.get(state, "black")
        )

        last = text.split(dmm2yml.MULTI_SEPARATOR)[-1].strip()
        kind = {"turf": protoindex.TILE, "decal": protoindex.DECAL}.get(row.kind, protoindex.ENTITY)
        matches = self.index.search(kind, last, limit=12) if self.index and last else []
        if row.kind == "turf" and len(matches) < 12 and last:
            matches += [m for m in self.index.search(protoindex.ENTITY, last, limit=6) if m not in matches]
        self._show_popup(matches)

    def _show_popup(self, matches: list[str]) -> None:
        if not matches:
            self._close_popup()
            return
        if self._popup is None:
            self._popup = tk.Toplevel(self)
            self._popup.overrideredirect(True)
            self._popup_list = tk.Listbox(self._popup, height=min(12, len(matches)), activestyle="dotbox")
            self._popup_list.pack(fill="both", expand=True)
            self._popup_list.bind("<Button-1>", lambda e: self.after(1, self._accept_popup))
        assert self._popup_list is not None
        self._popup_list.delete(0, "end")
        for match in matches:
            self._popup_list.insert("end", match)
        self._popup_list.configure(height=min(12, len(matches)))

        cell_x, cell_y, cell_width, cell_height = self._edit_bbox
        x = self.tree.winfo_rootx() + cell_x
        y = self.tree.winfo_rooty() + cell_y + cell_height
        self._popup.geometry(f"{max(240, cell_width)}x{min(12, len(matches)) * 18 + 4}+{x}+{y}")
        self._popup.deiconify()

    def _move_popup(self, delta: int) -> str:
        if self._popup_list is None or self._popup_list.size() == 0:
            return "break"
        current = self._popup_list.curselection()
        position = (current[0] + delta) if current else (0 if delta > 0 else self._popup_list.size() - 1)
        position = max(0, min(self._popup_list.size() - 1, position))
        self._popup_list.selection_clear(0, "end")
        self._popup_list.selection_set(position)
        self._popup_list.activate(position)
        self._popup_list.see(position)
        return "break"

    def _accept_popup(self) -> None:
        if self._popup_list is None or self._editor is None:
            return
        selection = self._popup_list.curselection()
        if not selection:
            return
        chosen = self._popup_list.get(selection[0])
        parts = self._editor.get().split(dmm2yml.MULTI_SEPARATOR)
        parts[-1] = chosen
        self._editor.delete(0, "end")
        self._editor.insert(0, dmm2yml.MULTI_SEPARATOR.join(part.strip() for part in parts))
        self._close_popup()
        self._on_typing()

    def _commit_edit(self) -> str:
        if self._popup_list is not None and self._popup_list.curselection():
            self._accept_popup()
            return "break"
        if self._editor is not None and self._editing is not None:
            self.rows[self._editing].value = self._editor.get().strip()
            path = self._editing
            self._close_editor()
            self._refresh_row(path)
            if self.only_unfilled.get():
                self._refill()
        return "break"

    def _close_popup(self) -> None:
        if self._popup is not None:
            self._popup.destroy()
            self._popup = None
            self._popup_list = None

    def _close_editor(self, event=None) -> None:
        self._close_popup()
        if self._editor is not None:
            self._editor.destroy()
            self._editor = None
        self._editing = None

    def _close_editor_if_open(self, event=None) -> None:
        if self._editor is not None:
            self._commit_edit()

    # ---------------------------------------------------------------- csv

    def _save_table(self) -> None:
        if not self.rows:
            messagebox.showinfo("Пусто", "Сначала разберите карту.")
            return
        path = filedialog.asksaveasfilename(
            title="Сохранить таблицу", defaultextension=".csv",
            filetypes=[("Таблица CSV", "*.csv"), ("Все файлы", "*.*")],
        )
        if not path:
            return
        with open(path, "w", encoding="utf-8-sig", newline="") as handle:
            writer = csv.DictWriter(handle, fieldnames=dmm2yml.CSV_COLUMNS)
            writer.writeheader()
            for row in sorted(self.rows.values(), key=lambda r: (-r.count, r.path)):
                writer.writerow({
                    "dmm_path": row.path, "kind": row.kind, "count": row.count,
                    "example": row.example, "suggestion": row.suggestion,
                    "ss14_id": row.value, "color": row.color, "notes": "",
                })
        self.log(f"таблица сохранена: {path}")

    def _load_table(self) -> None:
        path = filedialog.askopenfilename(
            title="Открыть таблицу", filetypes=[("Таблица CSV", "*.csv"), ("Все файлы", "*.*")]
        )
        if not path:
            return
        loaded = dmm2yml.read_table(path)
        applied = added = 0
        for dmm_path, values in loaded.items():
            if dmm_path in self.rows:
                self.rows[dmm_path].value = values.get("ss14_id", "")
                self.rows[dmm_path].color = values.get("color", "")
                applied += 1
            elif not self.rows:
                self.rows[dmm_path] = Row(
                    path=dmm_path, kind=values.get("kind", "entity"),
                    count=int(values.get("count") or 0), example=values.get("example", ""),
                    suggestion=values.get("suggestion", ""), value=values.get("ss14_id", ""),
                    color=values.get("color", ""),
                )
                added += 1
        self.log(f"из таблицы: применено {applied}, добавлено {added}")
        self._refill()


def main(argv: list[str] | None = None) -> int:
    import argparse

    parser = argparse.ArgumentParser(description="Окно для dmm2yml")
    parser.add_argument("--mapping-dir", default=dmm2yml.DEFAULT_MAPPING_DIR)
    parser.add_argument("--prototypes", default=dmm2yml.DEFAULT_PROTOTYPES)
    parser.add_argument("--z", type=int, default=None)
    parser.add_argument("--variants", choices=("deterministic", "zero"), default="deterministic")
    parser.add_argument("--dmm", default=None, help="открыть эту карту сразу")
    args = parser.parse_args(argv)

    root = tk.Tk()
    root.title("dmm2yml — конвертер карт SS13 в SS14")
    root.geometry("1180x780")
    app = App(root, args.mapping_dir, args.prototypes, args.z, args.variants)
    if args.dmm:
        app.dmm_path.set(args.dmm)
        app._probe_z_levels()
    root.mainloop()
    return 0


if __name__ == "__main__":
    sys.exit(main())
