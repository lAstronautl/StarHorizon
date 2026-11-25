/* Блочный редактор логики (в стиле Scratch) для SS14 UI Editor.
 *
 * Блоки описываются модулями (static/blocks/modules/*.js), каждый вызывает UIBlocks.registerModule({...}).
 * Программа хранится внутри сгенерированного файла <Класс>.logic.cs (в комментарии в конце),
 * а сам C#-код — partial class с методом InitializeLogic(), который вызывается из конструктора .xaml.cs.
 */
"use strict";

const UIBlocks = (() => {
    const modules = [];
    const defs = {};
    let root = emptyProgram();     // программа окна (в ней же root.item — предмет и его серверная программа)
    let program = root;            // текущая редактируемая программа (окно или сервер)
    let scope = "client";
    let host = null; // доступ к редактору интерфейса (getControls, isWindow, getMeta, onChange)
    let activeModule = null;
    let uidCounter = 1;
    const undoStack = [], redoStack = [];

    function emptyProgram() { return { vars: [], procs: [], scripts: [] }; }
    const uid = () => "b" + (uidCounter++);

    // ---------------------------------------------------------------
    //  Регистрация модулей
    // ---------------------------------------------------------------
    function registerModule(m) {
        if (modules.some(x => x.id === m.id)) return;
        m.blocks = m.blocks || [];
        for (const b of m.blocks) {
            b.module = m;
            b.args = b.args || {};
            if (defs[b.id]) console.warn("Блок уже существует:", b.id);
            defs[b.id] = b;
        }
        modules.push(m);
        if (!activeModule) activeModule = m.id;
        if (host) renderPalette();
    }

    function makeBlock(op) {
        const d = defs[op];
        const b = { id: uid(), op, args: {} };
        for (const [k, s] of Object.entries(d.args)) {
            if (s.def !== undefined) b.args[k] = String(s.def);
            else if (s.type === "control") b.args[k] = firstControl(s) || "";
            else if (s.type === "var") b.args[k] = (filteredVars(s)[0] || {}).name || "";
            else if (s.type === "proc") b.args[k] = program.procs[0] || "";
            else if (s.type === "choice") { const o = typeof s.options === "function" ? s.options(api) : s.options; b.args[k] = o.length ? o[0][1] : ""; }
            else if (s.type === "color") b.args[k] = "#FFFFFF";
            else if (s.type === "bool") { /* пустой слот */ }
            else b.args[k] = "";
        }
        if (d.shape === "c" || d.shape === "c2") b.sub = [];
        if (d.shape === "c2") b.sub2 = [];
        return b;
    }
    function controlsOf(spec) {
        const all = host ? host.getControls() : [];
        return all.filter(c => !spec.of || spec.of.includes(c.type));
    }
    function firstControl(spec) { const c = controlsOf(spec)[0]; return c ? c.name : ""; }
    function filteredVars(spec) { return program.vars.filter(v => !spec.vtypes || spec.vtypes.includes(v.type)); }
    const isBlock = v => v && typeof v === "object";
    const shapeOf = b => defs[b.op] ? defs[b.op].shape : "cmd";
    const isReporter = b => ["reporter", "bool"].includes(shapeOf(b));

    function outType(b, prog = program) {
        const d = defs[b.op];
        if (!d) return "text";
        if (typeof d.out === "function") return d.out(b, api, prog);
        return d.out || (d.shape === "bool" ? "bool" : "text");
    }

    // ---------------------------------------------------------------
    //  Undo / изменения
    // ---------------------------------------------------------------
    function snapshot() {
        undoStack.push(JSON.stringify(program));
        if (undoStack.length > 100) undoStack.shift();
        redoStack.length = 0;
    }
    function assignCurrent(p) {
        if (scope === "server") { root.item.server = p; program = p; }
        else { root = p; program = p; }
    }
    function undo() {
        if (!undoStack.length) return;
        redoStack.push(JSON.stringify(program));
        assignCurrent(JSON.parse(undoStack.pop()));
        changed();
    }
    function redo() {
        if (!redoStack.length) return;
        undoStack.push(JSON.stringify(program));
        assignCurrent(JSON.parse(redoStack.pop()));
        changed();
    }
    // модули и блоки текущей области (окно/сервер)
    const blockInScope = (d, sc) => !d.scopes || d.scopes.includes(sc);
    const moduleInScope = (m, sc) => (m.scope || "client") === sc || m.scope === "both";
    const visibleModules = () => modules.filter(m => moduleInScope(m, scope));
    let genTimer = null;
    function changed(rerender = true) {
        if (rerender) {
            renderWorkspace();
            if (api.afterRender) api.afterRender();
        }
        clearTimeout(genTimer);
        genTimer = setTimeout(updateCodePreview, 120);
        if (host && host.onChange) host.onChange();
    }

    // ---------------------------------------------------------------
    //  Рендер блоков
    // ---------------------------------------------------------------
    const el = (tag, cls, text) => {
        const e = document.createElement(tag);
        if (cls) e.className = cls;
        if (text !== undefined) e.textContent = text;
        return e;
    };

    function renderBlock(b, palette = false) {
        const d = defs[b.op];
        const e = el("div", "blk");
        e._block = b;
        e.dataset.bid = b.id;
        e.dataset.op = b.op;
        if (!d) {
            e.classList.add("shape-cmd", "blk-missing");
            e.appendChild(el("div", "blk-row", `неизвестный блок: ${b.op}`));
            return e;
        }
        e.classList.add("shape-" + d.shape);
        e.style.setProperty("--c", d.color || d.module.color);
        if (d.shape === "reporter" || d.shape === "bool") e.classList.add("rep");
        e.appendChild(renderRow(b, d.text, d, palette));
        if (d.shape === "c" || d.shape === "c2") {
            const sub = el("div", "blk-sub");
            renderStack(b.sub, sub, palette);
            sub._subList = b.sub;
            e.appendChild(sub);
            if (d.shape === "c2") {
                e.appendChild(renderRow(b, d.text2 || "иначе", d, palette));
                const sub2 = el("div", "blk-sub");
                renderStack(b.sub2, sub2, palette);
                sub2._subList = b.sub2;
                e.appendChild(sub2);
            }
            e.appendChild(el("div", "blk-foot"));
        }
        const warn = !palette && validateBlock(b);
        if (warn) { e.classList.add("blk-warn"); e.title = warn; }
        return e;
    }

    function renderRow(b, text, d, palette) {
        const row = el("div", "blk-row");
        const parts = text.split(/\{(\w+)\}/);
        parts.forEach((p, i) => {
            if (i % 2 === 0) { if (p.trim()) row.appendChild(el("span", "blk-txt", p.trim())); return; }
            const spec = d.args[p];
            if (!spec) return;
            row.appendChild(renderSlot(b, p, spec, palette));
        });
        return row;
    }

    function renderSlot(b, name, spec, palette) {
        const v = b.args[name];
        const slot = el("span", "slot slot-" + spec.type);
        slot._slot = { block: b, name, spec };
        if (isBlock(v)) {
            const inner = renderBlock(v, palette);
            inner._inSlot = { block: b, name };
            slot.classList.add("filled");
            slot.appendChild(inner);
            return slot;
        }
        const stop = e => e.stopPropagation();
        const set = val => { snapshot(); b.args[name] = val; changed(false); };
        let inp;
        switch (spec.type) {
            case "num": case "text": case "any": {
                inp = el("input");
                inp.value = v ?? "";
                inp.size = Math.max(spec.type === "num" ? 2 : 3, String(inp.value).length + 1);
                if (spec.type === "num") inp.inputMode = "decimal";
                inp.addEventListener("input", () => { inp.size = Math.max(2, inp.value.length + 1); });
                inp.addEventListener("change", () => set(inp.value));
                break;
            }
            case "color":
                inp = el("input"); inp.type = "color"; inp.value = /^#[0-9a-f]{6}$/i.test(v) ? v : "#ffffff";
                inp.addEventListener("change", () => set(inp.value.toUpperCase()));
                break;
            case "bool":
                return slot; // пустой шестиугольник — сюда вставляется условие
            case "control": case "var": case "proc": case "choice": {
                inp = el("select");
                let opts;
                if (spec.type === "control") opts = controlsOf(spec).map(c => [c.name, c.name]);
                else if (spec.type === "var") opts = filteredVars(spec).map(x => [x.name, x.name]);
                else if (spec.type === "proc") opts = program.procs.map(x => [x, x]);
                else opts = typeof spec.options === "function" ? spec.options(api) : spec.options;
                if (v && !opts.some(o => o[1] === v)) opts = [[v + " (?)", v], ...opts];
                if (!opts.length) opts = [["—", ""]];
                for (const [label, val] of opts) { const o = el("option", "", label); o.value = val; inp.appendChild(o); }
                inp.value = v ?? "";
                inp.addEventListener("change", () => { set(inp.value); renderWorkspace(); });
                break;
            }
        }
        if (inp) {
            if (palette) inp.tabIndex = -1;
            inp.addEventListener("pointerdown", stop);
            inp.addEventListener("mousedown", stop);
            inp.addEventListener("keydown", stop);
            slot.appendChild(inp);
        }
        return slot;
    }

    function renderStack(list, container, palette) {
        list.forEach((b, i) => {
            const e = renderBlock(b, palette);
            e._loc = { list, index: i };
            container.appendChild(e);
        });
    }

    function renderWorkspace() {
        const ws = document.getElementById("blkWorkspace");
        if (!ws) return;
        ws.innerHTML = "";
        const ind = el("div", "blk-indicator");
        ind.id = "blkIndicator";
        ind.hidden = true;
        ws.appendChild(ind);
        let maxX = 0, maxY = 0;
        program.scripts.forEach(s => {
            const se = el("div", "bscript");
            se.style.left = s.x + "px";
            se.style.top = s.y + "px";
            se._script = s;
            renderStack(s.blocks, se, false);
            ws.appendChild(se);
            maxX = Math.max(maxX, s.x + 600); maxY = Math.max(maxY, s.y + 400);
        });
        ws.style.minWidth = Math.max(maxX, 1600) + "px";
        ws.style.minHeight = Math.max(maxY, 1200) + "px";
        if (!program.scripts.length) {
            const hint = el("div", "blk-empty-hint");
            hint.innerHTML = "Перетащите сюда блок-событие (например, <b>когда нажата кнопка</b>),<br>а под него — действия.<br><br>Блоки ссылаются на элементы с заданным <b>Name</b>.";
            ws.appendChild(hint);
        }
    }

    function renderPalette() {
        const cats = document.getElementById("blkCats");
        const pal = document.getElementById("blkPalette");
        if (!cats || !pal) return;
        cats.innerHTML = "";
        const vis = visibleModules();
        if (!vis.some(m => m.id === activeModule) && vis[0]) activeModule = vis[0].id;
        for (const m of vis) {
            const c = el("button", "blk-cat" + (m.id === activeModule ? " active" : ""));
            const dot = el("span", "dot"); dot.style.background = m.color;
            c.append(dot, el("span", "", m.name));
            c.addEventListener("click", () => {
                activeModule = m.id;
                renderPalette();
                const target = document.getElementById("pal-" + m.id);
                if (target) target.scrollIntoView({ block: "start" });
            });
            cats.appendChild(c);
        }
        pal.innerHTML = "";
        if (api.paletteOverride) { api.paletteOverride(pal); return; }
        const isWin = host && host.isWindow();
        for (const m of vis) {
            const head = el("div", "blk-pal-head", m.name);
            head.id = "pal-" + m.id;
            head.style.color = m.color;
            pal.appendChild(head);
            if (m.paletteTop) m.paletteTop(pal, api);
            for (const d of m.blocks) {
                if (d.hidden || !blockInScope(d, scope)) continue;
                if (d.windowOnly && !isWin) continue;
                if (d.paletteIf && !d.paletteIf(api)) continue;
                const proto = makeBlock(d.id);
                if (d.paletteArgs) Object.assign(proto.args, d.paletteArgs(api));
                const e = renderBlock(proto, true);
                e._palette = proto;
                e.title = d.help || "";
                const wrap = el("div", "blk-pal-item");
                wrap.appendChild(e);
                pal.appendChild(wrap);
            }
        }
    }

    // ---------------------------------------------------------------
    //  Перетаскивание
    // ---------------------------------------------------------------
    let drag = null;

    function onPointerDown(ev) {
        if (ev.button !== 0) return;
        const be = ev.target.closest(".blk");
        if (!be || ev.target.closest("input, select")) return;
        ev.preventDefault();
        const rect = be.getBoundingClientRect();
        let blocks;
        // из палитры — копия
        const palEl = ev.target.closest(".blk-pal-item > .blk");
        if (palEl && document.getElementById("blkPalette").contains(palEl)) {
            const proto = palEl._palette;
            const b = JSON.parse(JSON.stringify(proto));
            reId(b);
            blocks = [b];
            snapshot();
        } else if (be._inSlot) {
            snapshot();
            const { block, name } = be._inSlot;
            const d = defs[block.op];
            const spec = d.args[name];
            blocks = [block.args[name]];
            block.args[name] = spec.def !== undefined ? String(spec.def) : (spec.type === "bool" ? undefined : "");
            if (block.args[name] === undefined) delete block.args[name];
        } else if (be._loc) {
            snapshot();
            const { list, index } = be._loc;
            blocks = list.splice(index);
            // пустой скрипт удаляем
            program.scripts = program.scripts.filter(s => s.blocks.length);
        } else return;

        const float = el("div", "bscript drag-float");
        renderStack(blocks, float, true);
        document.body.appendChild(float);
        drag = { blocks, float, dx: ev.clientX - rect.left, dy: ev.clientY - rect.top, target: null };
        moveFloat(ev);
        renderWorkspace();
        document.addEventListener("pointermove", onPointerMove);
        document.addEventListener("pointerup", onPointerUp, { once: true });
    }
    function reId(b) {
        if (!isBlock(b)) return;
        b.id = uid();
        Object.values(b.args).forEach(reId);
        (b.sub || []).forEach(reId);
        (b.sub2 || []).forEach(reId);
    }
    function moveFloat(ev) {
        drag.float.style.left = ev.clientX - drag.dx + "px";
        drag.float.style.top = ev.clientY - drag.dy + "px";
    }
    function onPointerMove(ev) {
        if (!drag) return;
        moveFloat(ev);
        drag.target = findTarget();
        showIndicator(drag.target);
    }

    function findTarget() {
        const fr = drag.float.getBoundingClientRect();
        const first = drag.blocks[0];
        const ws = document.getElementById("blkWorkspace");
        const R = 28;
        let best = null, bestD = R;
        const consider = (x, y, t) => {
            const d = Math.hypot(x, y);
            if (d < bestD) { bestD = d; best = t; }
        };
        if (isReporter(first)) {
            const fromType = outType(first);
            ws.querySelectorAll(".slot").forEach(s => {
                const info = s._slot;
                if (!info || s.closest(".drag-float")) return;
                if (info.spec.type === "bool" && fromType !== "bool") return;
                if (!["num", "text", "any", "bool"].includes(info.spec.type)) return;
                if (isBlock(info.block.args[info.name])) return;
                const r = s.getBoundingClientRect();
                consider(fr.left - r.left, fr.top - r.top, { kind: "slot", info, el: s });
            });
            return best;
        }
        const firstIsHat = shapeOf(first) === "hat";
        const lastShape = shapeOf(drag.blocks[drag.blocks.length - 1]);
        if (!firstIsHat) {
            ws.querySelectorAll(".blk").forEach(b => {
                if (!b._loc) return;
                if (shapeOf(b._loc.list[b._loc.index]) === "cap") return;
                const rows = b.getBoundingClientRect();
                consider(fr.left - rows.left, fr.top - rows.bottom, { kind: "after", list: b._loc.list, index: b._loc.index + 1, el: b, y: rows.bottom, x: rows.left });
            });
            ws.querySelectorAll(".blk-sub").forEach(s => {
                const r = s.getBoundingClientRect();
                consider(fr.left - r.left, fr.top - r.top, { kind: "after", list: s._subList, index: 0, el: s, y: r.top, x: r.left });
            });
        }
        // сверху скрипта (если у скрипта нет шапки-события)
        if (lastShape !== "cap") {
            ws.querySelectorAll(".bscript").forEach(se => {
                const s = se._script;
                if (!s || !s.blocks.length || shapeOf(s.blocks[0]) === "hat") return;
                const r = se.getBoundingClientRect();
                consider(fr.left - r.left, fr.bottom - r.top, { kind: "before", script: s, el: se, y: r.top, x: r.left });
            });
        }
        return best;
    }

    function showIndicator(t) {
        document.querySelectorAll(".slot.drop-ok").forEach(e => e.classList.remove("drop-ok"));
        const ind = document.getElementById("blkIndicator");
        if (!t) { ind.hidden = true; return; }
        if (t.kind === "slot") { ind.hidden = true; t.el.classList.add("drop-ok"); return; }
        const ws = document.getElementById("blkWorkspace").getBoundingClientRect();
        ind.hidden = false;
        ind.style.left = t.x - ws.left + "px";
        ind.style.top = t.y - ws.top - 2 + "px";
    }

    function onPointerUp(ev) {
        document.removeEventListener("pointermove", onPointerMove);
        if (!drag) return;
        const t = drag.target;
        const fr = drag.float.getBoundingClientRect();
        drag.float.remove();
        showIndicator(null);
        const pal = document.getElementById("blkPaletteWrap").getBoundingClientRect();
        const overPalette = ev.clientX >= pal.left && ev.clientX <= pal.right && ev.clientY >= pal.top && ev.clientY <= pal.bottom;
        const wsEl = document.getElementById("blkWorkspace");
        const scr = document.getElementById("blkScroll").getBoundingClientRect();
        const overWs = ev.clientX >= scr.left && ev.clientX <= scr.right && ev.clientY >= scr.top && ev.clientY <= scr.bottom;
        if (overPalette || (!t && !overWs)) {
            // удалено
        } else if (t && t.kind === "slot") {
            t.info.block.args[t.info.name] = drag.blocks[0];
        } else if (t && t.kind === "after") {
            t.list.splice(t.index, 0, ...drag.blocks);
        } else if (t && t.kind === "before") {
            t.script.blocks.unshift(...drag.blocks);
            const h = fr.height;
            t.script.y = Math.max(0, t.script.y - h);
        } else {
            const ws = wsEl.getBoundingClientRect();
            program.scripts.push({ x: Math.max(0, Math.round(fr.left - ws.left)), y: Math.max(0, Math.round(fr.top - ws.top)), blocks: drag.blocks });
        }
        drag = null;
        changed();
    }

    function onContextMenu(ev) {
        const be = ev.target.closest("#blkWorkspace .blk");
        if (!be) return;
        ev.preventDefault();
        const menu = document.getElementById("ctxMenu");
        menu.innerHTML = "";
        const items = [];
        if (be._loc) {
            const { list, index } = be._loc;
            items.push(["Дублировать (с блоками ниже)", () => {
                const copy = JSON.parse(JSON.stringify(list.slice(index)));
                copy.forEach(reId);
                const se = be.closest(".bscript")._script;
                program.scripts.push({ x: se.x + 30, y: se.y + 30, blocks: copy });
            }]);
            items.push(["Удалить блок", () => { list.splice(index, 1); program.scripts = program.scripts.filter(s => s.blocks.length); }]);
        } else if (be._inSlot) {
            items.push(["Удалить", () => {
                const { block, name } = be._inSlot;
                const spec = defs[block.op].args[name];
                if (spec.def !== undefined) block.args[name] = String(spec.def); else delete block.args[name];
            }]);
        }
        for (const [label, fn] of items) {
            const b = el("button", "", label);
            b.addEventListener("click", () => { menu.hidden = true; snapshot(); fn(); changed(); });
            menu.appendChild(b);
        }
        menu.hidden = false;
        menu.style.left = ev.clientX + "px";
        menu.style.top = ev.clientY + "px";
    }

    function tidy() {
        snapshot();
        let y = 20;
        const ws = document.getElementById("blkWorkspace");
        const els = [...ws.querySelectorAll(":scope > .bscript")];
        program.scripts.forEach((s, i) => {
            s.x = 20; s.y = y;
            y += (els[i] ? els[i].getBoundingClientRect().height : 80) + 24;
        });
        changed();
    }

    // ---------------------------------------------------------------
    //  Переменные и «Мои блоки»
    // ---------------------------------------------------------------
    function addVariable(name, type) {
        name = (name || "").trim();
        if (!name) return false;
        if (program.vars.some(v => v.name === name)) { alertMsg("Такая переменная уже есть"); return false; }
        snapshot();
        program.vars.push({ name, type });
        renderPalette();
        changed();
        return true;
    }
    function removeVariable(name) {
        snapshot();
        program.vars = program.vars.filter(v => v.name !== name);
        renderPalette();
        changed();
    }
    function addProc(name) {
        name = (name || "").trim();
        if (!name || program.procs.includes(name)) return false;
        snapshot();
        program.procs.push(name);
        const def = makeBlock("proc_def");
        def.args.P = name;
        program.scripts.push({ x: 40, y: 40 + program.scripts.length * 20, blocks: [def] });
        renderPalette();
        changed();
        return true;
    }
    function alertMsg(m) { if (host && host.toast) host.toast(m); }

    // ---------------------------------------------------------------
    //  Генерация C#
    // ---------------------------------------------------------------
    class Skip extends Error { }

    const csString = s => '"' + String(s).replace(/\\/g, "\\\\").replace(/"/g, '\\"').replace(/\n/g, "\\n").replace(/\r/g, "") + '"';
    function csNumber(s) {
        const v = parseFloat(String(s).replace(",", "."));
        if (!isFinite(v)) return "0d";
        return (Number.isInteger(v) ? String(v) : String(v)) + "d";
    }
    const ident = s => String(s).replace(/[^\p{L}\p{N}_]/gu, "_").replace(/^(\p{N})/u, "_$1");
    const indent = (code, n = 1) => code.split("\n").map(l => (l ? "    ".repeat(n) + l : l)).join("\n");

    function convert(code, from, to) {
        if (from === to || to === "any") return code;
        if (to === "num") {
            if (from === "text") return `UiNum(${code})`;
            if (from === "bool") return `((${code}) ? 1d : 0d)`;
        }
        if (to === "text") {
            if (from === "num" || from === "bool") return `UiText(${code})`;
        }
        if (to === "bool") {
            if (from === "num") return `((${code}) != 0)`;
            if (from === "text") return `UiBool(${code})`;
        }
        return code;
    }

    const scopeOfProg = prog => (prog === root ? "client" : "server");

    function validateBlock(b, prog = program) {
        const d = defs[b.op];
        if (!d) return "Неизвестный блок";
        const sc = scopeOfProg(prog);
        if (!blockInScope(d, sc)) return sc === "server" ? "Этот блок работает только в окне (клиент)" : "Этот блок работает только на сервере";
        if (d.windowOnly && host && !host.isWindow()) return "Блок работает только в окнах";
        for (const [k, spec] of Object.entries(d.args)) {
            const v = b.args[k];
            if (spec.type === "control") {
                const c = host ? host.getControls().find(x => x.name === v) : null;
                if (!v) return "Не выбран элемент (задайте Name элементу в дизайнере)";
                if (!c) return `Элемент «${v}» не найден`;
                if (spec.of && !spec.of.includes(c.type)) return `Элемент «${v}» (${c.type}) не подходит`;
            }
            if (spec.type === "var" && !prog.vars.some(x => x.name === v)) return v ? `Нет переменной «${v}»` : "Не выбрана переменная";
            if (spec.type === "proc" && !prog.procs.includes(v)) return "Не выбран блок";
            if (spec.type === "choice" && spec.required && !v) return "Не выбрано значение";
        }
        return null;
    }

    // Транслитерация для серверных идентификаторов (поля компонента, ключи YAML)
    const TR = { а: "a", б: "b", в: "v", г: "g", д: "d", е: "e", ё: "e", ж: "zh", з: "z", и: "i", й: "y", к: "k", л: "l", м: "m", н: "n", о: "o", п: "p", р: "r", с: "s", т: "t", у: "u", ф: "f", х: "h", ц: "ts", ч: "ch", ш: "sh", щ: "sch", ъ: "", ы: "y", ь: "", э: "e", ю: "yu", я: "ya" };
    const translit = s => String(s).toLowerCase().split("").map(c => (TR[c] !== undefined ? TR[c] : c)).join("").replace(/[^a-z0-9_]/g, "_").replace(/^(\d)/, "_$1");
    const svField = name => "V_" + translit(name);

    // Общий компилятор блоков. mode: "client" (окно) или "server" (система предмета).
    function compile(prog, mode, meta) {
        const uses = new Set();
        const warnings = [];
        let loopN = 0;
        const controls = new Map((host ? host.getControls() : []).map(c => [c.name, c.type]));
        const procAsync = {};
        const procBodies = {};
        for (const s of prog.scripts) {
            const top = s.blocks[0];
            if (top && top.op === "proc_def" && top.args.P) procBodies[top.args.P] = s.blocks.slice(1);
        }
        const containsAwait = list => list.some(b => {
            const d = defs[b.op];
            if (!d) return false;
            if (d.async) return true;
            if (b.op === "proc_call" && procAsync[b.args.P]) return true;
            return containsAwait(b.sub || []) || containsAwait(b.sub2 || []);
        });
        for (let i = 0; i < 10; i++) {
            let ch = false;
            for (const [p, body] of Object.entries(procBodies)) {
                if (containsAwait(body) && !procAsync[p]) { procAsync[p] = true; ch = true; }
            }
            if (!ch) break;
        }
        const server = mode === "server";
        const ctx = {
            mode,
            isWindow: meta.isWindow,
            use: f => uses.add(f),
            typeOf: name => controls.get(name),
            varField: name => (server ? "ent.Comp." + svField(name) : "_v_" + ident(name)),
            varType: name => (prog.vars.find(v => v.name === name) || {}).type || "text",
            procMethod: name => "Blk_" + (server ? translit(name) : ident(name)),
            procAsync: name => !server && !!procAsync[name],
            procCall: name => {
                const m = "Blk_" + (server ? translit(name) : ident(name));
                if (server) return `${m}(ent, user, value)`;
                return procAsync[name] ? `await ${m}()` : `${m}()`;
            },
            loopVar: () => "i" + (++loopN),
            stopCheck: server ? "" : "if (_uiStopped) return;",
            str: csString,
            num: csNumber,
            uiKey: root.item ? `${root.item.id}UiKey.Key` : "default!",
            serverVarType: name => {
                const it = root.item;
                const v = it && it.server.vars.find(x => x.name === name);
                return v ? v.type : "text";
            },
        };
        function arg(b, name) {
            const spec = defs[b.op].args[name];
            const v = b.args[name];
            if (isBlock(v)) {
                const r = expr(v);
                return spec.type === "any" ? r : { code: convert(r.code, r.type, spec.type), type: spec.type };
            }
            switch (spec.type) {
                case "num": return { code: csNumber(v ?? 0), type: "num" };
                case "text": return { code: csString(v ?? ""), type: "text" };
                case "any": return /^\s*-?\d+([.,]\d+)?\s*$/.test(v ?? "") ? { code: csNumber(v), type: "num" } : { code: csString(v ?? ""), type: "text" };
                case "bool": return { code: "false", type: "bool" };
                case "color": {
                    const c = /^#[0-9a-f]{6,8}$/i.test(v) ? v : "#FFFFFF";
                    return { code: csString(c), type: "color", raw: c };
                }
                default: return { code: v, raw: v, type: spec.type };
            }
        }
        function args(b) {
            const w = validateBlock(b, prog);
            if (w) throw new Skip(w);
            const a = {};
            for (const k of Object.keys(defs[b.op].args)) a[k] = arg(b, k);
            return a;
        }
        function expr(b) {
            const d = defs[b.op];
            if (!d) throw new Skip("неизвестный блок " + b.op);
            const code = d.gen(ctx, args(b), b);
            return { code: `(${code})`, type: outType(b, prog) };
        }
        const label = d => (d ? d.text.replace(/\{\w+\}/g, "…") : "?");
        function stmts(list) {
            const out = [];
            for (const b of list) {
                const d = defs[b.op];
                if (!d || d.shape === "reporter" || d.shape === "bool" || d.shape === "hat") continue;
                try {
                    const a = args(b);
                    if (d.shape === "c" || d.shape === "c2") {
                        a.SUB = indent(stmts(b.sub || []));
                        a.SUB2 = indent(stmts(b.sub2 || []));
                    }
                    out.push(d.gen(ctx, a, b));
                    if (d.async && ctx.stopCheck) out.push(ctx.stopCheck);
                } catch (e) {
                    if (!(e instanceof Skip)) throw e;
                    warnings.push(`${label(d)}: ${e.message}`);
                    out.push(`// ⚠ блок пропущен: ${e.message}`);
                }
            }
            return out.join("\n");
        }
        function hatArgs(top) {
            try { return args(top); }
            catch (e) {
                if (!(e instanceof Skip)) throw e;
                warnings.push(`${label(defs[top.op])}: ${e.message}`);
                return null;
            }
        }
        return { ctx, stmts, hatArgs, containsAwait, procAsync, warnings, uses };
    }

    const COMMON_HELPERS = [
`private static double UiNum(string? s) =>
    double.TryParse((s ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;`,
`private static string UiText(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);`,
`private static string UiText(bool v) => v ? "true" : "false";`,
`private static bool UiBool(string s) => s.Trim().ToLowerInvariant() is "true" or "1" or "да";`,
`private static bool UiEq(string a, string b)
{
    if (double.TryParse(a.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
        double.TryParse(b.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
        return Math.Abs(x - y) < 1e-9;
    return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}`];
    const RANDOM_HELPER =
`private double UiRandom(double a, double b)
{
    if (a > b)
        (a, b) = (b, a);
    if (a == Math.Floor(a) && b == Math.Floor(b))
        return _uiRandom.Next((int) a, (int) b + 1);
    return a + _uiRandom.NextDouble() * (b - a);
}`;
    const GEN_MARK = "Сгенерировано SS14 UI Editor";

    function generate(meta) {
        const c = compile(root, "client", meta);
        const { ctx, stmts, containsAwait, procAsync, warnings, uses } = c;
        const init = [];
        const methods = [];
        for (const s of root.scripts) {
            const top = s.blocks[0];
            const d = top && defs[top.op];
            if (!d || d.shape !== "hat") continue;
            const body = s.blocks.slice(1);
            if (top.op === "proc_def") {
                const name = top.args.P;
                if (!name) continue;
                methods.push(`private ${procAsync[name] ? "async Task" : "void"} ${ctx.procMethod(name)}()\n{\n${indent(stmts(body))}\n}`);
                continue;
            }
            const a = c.hatArgs(top);
            if (!a) { init.push(`// ⚠ скрипт «${d.text.replace(/\{\w+\}/g, "…")}» пропущен`); continue; }
            const inner = indent(stmts(body));
            const run = containsAwait(body) ? `UiRunAsync(async () =>\n{\n${inner}\n})` : `UiRun(() =>\n{\n${inner}\n})`;
            init.push(d.gen(ctx, a, run, top));
        }
        if (meta.isWindow) init.push("OnClose += () =>\n{\n    _uiStopped = true;\n    _uiCts.Cancel();\n};");

        const usings = ["System", "System.Collections.Generic", "System.Globalization", "System.Threading", "System.Threading.Tasks",
            "Robust.Client.UserInterface.Controls", "Robust.Shared.Maths"];
        if (uses.has("linq")) usings.push("System.Linq");
        if (uses.has("rope")) usings.push("Robust.Shared.Utility");
        usings.sort();

        const fields = [];
        for (const v of root.vars) {
            const f = ctx.varField(v.name);
            if (v.type === "num") fields.push(`private double ${f};`);
            else if (v.type === "bool") fields.push(`private bool ${f};`);
            else fields.push(`private string ${f} = "";`);
        }
        fields.push("private bool _uiStopped;");
        fields.push("private readonly CancellationTokenSource _uiCts = new();");
        if (uses.has("random")) fields.push("private readonly Random _uiRandom = new();");

        // связь с сервером (используется BoundUserInterface предмета)
        const link = [
`/// <summary>Отправка действия на сервер. Назначается BoundUserInterface предмета.</summary>
public Action<string, string>? ServerSend;`,
`private readonly Dictionary<string, string> _uiServerVars = new();`,
`private event Action? UiServerStateChanged;`,
`/// <summary>Новое состояние от сервера (переменные предмета).</summary>
public void ApplyServerState(Dictionary<string, string> vars)
{
    _uiServerVars.Clear();
    foreach (var (k, v) in vars)
        _uiServerVars[k] = v;
    UiServerStateChanged?.Invoke();
}`,
`private string UiServerVar(string name) => _uiServerVars.TryGetValue(name, out var v) ? v : "";`];

        const helpers = [
`private void UiRun(Action action)
{
    try { action(); }
    catch (Exception e) { UiLogError(e); }
}`,
`private async void UiRunAsync(Func<Task> action)
{
    try { await action(); }
    catch (Exception e) { UiLogError(e); }
}`,
`private static Robust.Shared.Log.ISawmill UiSawmill =>
    Robust.Shared.IoC.IoCManager.Resolve<Robust.Shared.Log.ILogManager>().GetSawmill("ui.blocks");`,
`private static void UiLogError(Exception e) => UiSawmill.Error(e.ToString());`,
`private static void UiLog(string text) => UiSawmill.Info(text);`,
            ...COMMON_HELPERS];
        if (uses.has("random")) helpers.push(RANDOM_HELPER);

        let o = `// ${GEN_MARK} из блоков (вкладка «Логика»).\n`;
        o += "// Не редактируйте вручную: файл перезаписывается при сохранении. Свой код пишите в .xaml.cs.\n";
        o += "#pragma warning disable CS0414, CS0067 // служебные поля/события могут не использоваться\n";
        o += usings.map(u => `using ${u};`).join("\n") + "\n\n";
        o += `namespace ${meta.ns};\n\n`;
        o += `partial class ${meta.cls}\n{\n`;
        o += indent(fields.join("\n")) + "\n\n";
        o += indent(`private void InitializeLogic()\n{\n${indent(init.join("\n\n"))}\n}`) + "\n";
        for (const m of methods) o += "\n" + indent(m) + "\n";
        o += "\n    // --- связь с сервером ---\n\n" + indent(link.join("\n\n")) + "\n";
        o += "\n    // --- вспомогательные методы ---\n\n" + indent(helpers.join("\n\n")) + "\n";
        o += "}\n";
        o += "\n/* ui-blocks\n" + JSON.stringify(root, null, 1).replace(/\*\//g, "*\\/") + "\n*/\n";
        return { code: o.replace(/[ \t]+\n/g, "\n"), warnings };
    }

    // ---------------------------------------------------------------
    //  Предмет: прототип + общий код + серверная система + BoundUserInterface
    // ---------------------------------------------------------------
    const yamlStr = s => '"' + String(s ?? "").replace(/\\/g, "\\\\").replace(/"/g, '\\"') + '"';

    function generateItem(meta) {
        const it = root.item;
        if (!it) return null;
        const N = it.id;
        const comp = `${N}ItemComponent`;
        const sp = it.server;
        const c = compile(sp, "server", meta);
        const { stmts, warnings, uses } = c;

        // --- Shared: ключ, сообщение, состояние, компонент
        let sh = `// ${GEN_MARK} (предмет «${it.name}»). Не редактируйте вручную.\n`;
        sh += "using System;\nusing System.Collections.Generic;\nusing Robust.Shared.GameObjects;\nusing Robust.Shared.Serialization;\nusing Robust.Shared.Serialization.Manager.Attributes;\n\n";
        sh += "namespace Content.Shared._Horizon.UIItems;\n\n";
        sh += `[Serializable, NetSerializable]\npublic enum ${N}UiKey : byte\n{\n    Key,\n}\n\n`;
        sh += `/// <summary>Действие из окна: имя действия и значение.</summary>\n[Serializable, NetSerializable]\npublic sealed class ${N}UiMessage : BoundUserInterfaceMessage\n{\n    public readonly string Action;\n    public readonly string Value;\n\n    public ${N}UiMessage(string action, string value)\n    {\n        Action = action;\n        Value = value;\n    }\n}\n\n`;
        sh += `/// <summary>Переменные предмета, которые сервер отправляет окну.</summary>\n[Serializable, NetSerializable]\npublic sealed class ${N}UiState : BoundUserInterfaceState\n{\n    public readonly Dictionary<string, string> Values;\n\n    public ${N}UiState(Dictionary<string, string> values)\n    {\n        Values = values;\n    }\n}\n\n`;
        sh += `/// <summary>Переменные предмета «${it.name}» (хранятся на сервере, у каждого экземпляра свои).</summary>\n[RegisterComponent]\npublic sealed partial class ${comp} : Component\n{\n`;
        for (const v of sp.vars) {
            const f = svField(v.name), key = translit(v.name).replace(/^_+/, "") || "var";
            const t = v.type === "num" ? "double" : v.type === "bool" ? "bool" : "string";
            const init = v.type === "text" ? ` = "";` : ";";
            sh += `    /// <summary>${v.name}</summary>\n    [DataField("${key}")]\n    public ${t} ${f}${init}\n\n`;
        }
        sh = sh.replace(/\n\n$/, "\n") + "}\n";

        // --- Server: система
        const handlers = { message: [], opened: [], init: [] };
        const methods = [];
        let k = 0;
        const sig = `Entity<${comp}> ent, EntityUid user, string value`;
        for (const s of sp.scripts) {
            const top = s.blocks[0];
            const d = top && defs[top.op];
            if (!d || d.shape !== "hat") continue;
            const body = s.blocks.slice(1);
            if (top.op === "proc_def") {
                if (!top.args.P) continue;
                methods.push(`private void ${c.ctx.procMethod(top.args.P)}(${sig})\n{\n${indent(stmts(body))}\n}`);
                continue;
            }
            if (!d.serverEvent) continue;
            const a = c.hatArgs(top);
            if (!a) continue;
            const m = `Script${++k}`;
            methods.push(`// ${d.text.replace(/\{(\w+)\}/g, (_, x) => (a[x] ? a[x].code : x))}\nprivate void ${m}(${sig})\n{\n${indent(stmts(body))}\n}`);
            handlers[d.serverEvent].push(d.serverCall ? d.serverCall(c.ctx, a, m) : `${m}(ent, user, value);`);
        }
        const dict = sp.vars.map(v => `["${v.name.replace(/"/g, '\\"')}"] = ${v.type === "text" ? "ent.Comp." + svField(v.name) : "UiText(ent.Comp." + svField(v.name) + ")"},`);
        let sv = `// ${GEN_MARK} (предмет «${it.name}»). Не редактируйте вручную: файл перезаписывается при сохранении.\n`;
        sv += "#pragma warning disable CS0414\n";
        sv += ["System", "System.Collections.Generic", "System.Globalization", "Content.Shared._Horizon.UIItems",
            "Content.Shared.Hands.EntitySystems", "Content.Shared.Popups", "Robust.Server.GameObjects",
            "Robust.Shared.Audio", "Robust.Shared.Audio.Systems", "Robust.Shared.GameObjects", "Robust.Shared.IoC"].map(u => `using ${u};`).join("\n") + "\n\n";
        sv += "namespace Content.Server._Horizon.UIItems;\n\n";
        sv += `/// <summary>Серверная логика предмета «${it.name}», собранная из блоков.</summary>\npublic sealed class ${N}ItemSystem : EntitySystem\n{\n`;
        sv += indent(["[Dependency] private readonly UserInterfaceSystem _ui = default!;",
            "[Dependency] private readonly SharedPopupSystem _popup = default!;",
            "[Dependency] private readonly SharedAudioSystem _audio = default!;",
            "[Dependency] private readonly SharedHandsSystem _hands = default!;",
            ...(uses.has("random") ? ["private readonly Random _uiRandom = new();"] : [])].join("\n")) + "\n\n";
        sv += indent(`public override void Initialize()
{
    base.Initialize();
    SubscribeLocalEvent<${comp}, MapInitEvent>(OnMapInit);
    Subs.BuiEvents<${comp}>(${N}UiKey.Key, subs =>
    {
        subs.Event<BoundUIOpenedEvent>(OnOpened);
        subs.Event<${N}UiMessage>(OnMessage);
    });
}

private void OnMapInit(Entity<${comp}> ent, ref MapInitEvent args)
{
    var user = EntityUid.Invalid;
    var value = "";
${indent(handlers.init.join("\n"))}
}

private void OnOpened(Entity<${comp}> ent, ref BoundUIOpenedEvent args)
{
    var user = args.Actor;
    var value = "";
${indent(handlers.opened.join("\n"))}
    UpdateUi(ent);
}

private void OnMessage(Entity<${comp}> ent, ref ${N}UiMessage msg)
{
    var user = msg.Actor;
    var value = msg.Value;
${indent(handlers.message.join("\n"))}
    UpdateUi(ent);
}

/// <summary>Отправляет окну текущие переменные предмета.</summary>
public void UpdateUi(Entity<${comp}> ent)
{
    var vars = new Dictionary<string, string>
    {
${indent(dict.join("\n"), 2)}
    };
    _ui.SetUiState(ent.Owner, ${N}UiKey.Key, new ${N}UiState(vars));
}`) + "\n";
        for (const m of methods) sv += "\n" + indent(m) + "\n";
        const svHelpers = [...COMMON_HELPERS, `private void UiLog(string text) => Log.Info(text);`];
        if (uses.has("random")) svHelpers.push(RANDOM_HELPER);
        sv += "\n    // --- вспомогательные методы ---\n\n" + indent(svHelpers.join("\n\n")) + "\n}\n";

        // --- Client: BoundUserInterface
        let bui = `// ${GEN_MARK} (предмет «${it.name}»). Не редактируйте вручную.\n`;
        bui += "using System;\nusing Content.Shared._Horizon.UIItems;\nusing Robust.Client.GameObjects;\nusing Robust.Client.UserInterface;\nusing Robust.Shared.GameObjects;\n\n";
        bui += `namespace ${meta.ns};\n\n`;
        bui += `/// <summary>Связывает предмет ${N} с окном ${meta.cls}.</summary>\npublic sealed class ${N}BoundUserInterface : BoundUserInterface\n{\n    private ${meta.cls}? _window;\n\n`;
        bui += `    public ${N}BoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)\n    {\n    }\n\n`;
        bui += `    protected override void Open()\n    {\n        base.Open();\n        _window = this.CreateWindow<${meta.cls}>();\n        _window.ServerSend = (action, value) => SendMessage(new ${N}UiMessage(action, value));\n    }\n\n`;
        bui += `    protected override void UpdateState(BoundUserInterfaceState state)\n    {\n        base.UpdateState(state);\n        if (state is ${N}UiState s)\n            _window?.ApplyServerState(s.Values);\n    }\n}\n`;

        // --- Прототип
        let y = `# ${GEN_MARK} (предмет «${it.name}»). Не редактируйте вручную: файл перезаписывается при сохранении.\n`;
        y += `- type: entity\n  parent: ${it.parent || "BaseItem"}\n  id: ${N}\n  name: ${yamlStr(it.name)}\n  description: ${yamlStr(it.desc)}\n  components:\n`;
        y += `  - type: Sprite\n    sprite: ${it.sprite}\n    state: ${it.state || "icon"}\n`;
        if ((it.parent || "BaseItem") === "BaseItem") y += `  - type: Item\n    sprite: ${it.sprite}\n`;
        y += `  - type: ActivatableUI\n    key: enum.${N}UiKey.Key\n`;
        y += `  - type: UserInterface\n    interfaces:\n      enum.${N}UiKey.Key:\n        type: ${N}BoundUserInterface\n`;
        y += `  - type: ${N}Item\n`;

        const clean = s => s.replace(/[ \t]+\n/g, "\n");
        return {
            warnings,
            files: [
                { path: `Content.Shared/_Horizon/UIItems/${N}.cs`, content: clean(sh), kind: "shared" },
                { path: `Content.Server/_Horizon/UIItems/${N}ItemSystem.cs`, content: clean(sv), kind: "server" },
                { path: `${meta.dir}/${N}BoundUserInterface.cs`, content: clean(bui), kind: "bui" },
                { path: `Resources/Prototypes/_Horizon/UIItems/${N}.yml`, content: y, kind: "proto" },
            ],
        };
    }

    function updateCodePreview() {
        const pre = document.getElementById("blkCode");
        if (!pre || !host) return;
        const meta = host.getMeta();
        let r;
        if (scope === "server" && root.item) {
            const g = generateItem(meta);
            r = { code: g.files.find(f => f.kind === "server").content, warnings: g.warnings };
        } else r = generate(meta);
        pre.value = r.code;
        const head = document.getElementById("blkCodeTitle");
        if (head) head.textContent = scope === "server" ? `C# → ${root.item.id}ItemSystem.cs (сервер)` : "C# → <Класс>.logic.cs (окно)";
        const w = document.getElementById("blkWarnings");
        w.textContent = r.warnings.length ? `⚠ ${r.warnings.length}: ` + r.warnings.join("; ") : "";
    }

    // ---------------------------------------------------------------
    //  Загрузка / сохранение / область (окно или сервер)
    // ---------------------------------------------------------------
    function parseLogicFile(text) {
        const m = text && text.match(/\/\* ui-blocks\n([\s\S]*?)\n\*\//);
        if (!m) return null;
        try { return JSON.parse(m[1]); } catch (e) { return null; }
    }
    function normProg(p) {
        p = p && p.scripts ? p : emptyProgram();
        p.vars = p.vars || [];
        p.procs = p.procs || [];
        return p;
    }
    function setProgram(p) {
        root = normProg(p);
        if (root.item) root.item.server = normProg(root.item.server);
        scope = "client";
        program = root;
        undoStack.length = 0; redoStack.length = 0;
        const walk = b => { if (!isBlock(b)) return; uidCounter++; Object.values(b.args || {}).forEach(walk); (b.sub || []).forEach(walk); (b.sub2 || []).forEach(walk); };
        root.scripts.forEach(s => s.blocks.forEach(walk));
        if (root.item) root.item.server.scripts.forEach(s => s.blocks.forEach(walk));
        renderPalette();
        changed();
        if (api.onScopeChange) api.onScopeChange(scope);
    }
    function setScope(s) {
        if (s === "server" && !root.item) return false;
        scope = s;
        program = s === "server" ? root.item.server : root;
        undoStack.length = 0; redoStack.length = 0;
        activeModule = null;
        renderPalette();
        changed();
        if (api.onScopeChange) api.onScopeChange(scope);
        return true;
    }
    function setItem(item) {
        snapshot();
        if (item) {
            const prev = root.item;
            root.item = Object.assign({}, prev || {}, item);
            root.item.server = normProg(prev ? prev.server : null);
        } else {
            delete root.item;
            if (scope === "server") { scope = "client"; program = root; }
        }
        undoStack.length = 0; redoStack.length = 0;
        renderPalette();
        changed();
        if (api.onScopeChange) api.onScopeChange(scope);
    }
    const isEmpty = () => !root.scripts.length && !root.vars.length && !root.item;

    function init(h) {
        host = h;
        const ws = document.getElementById("blkWorkspace");
        const lv = document.getElementById("logicView");
        lv.addEventListener("pointerdown", onPointerDown);
        // не даём браузеру выделять текст при перетаскивании блоков
        lv.addEventListener("mousedown", e => { if (e.target.closest(".blk") && !e.target.closest("input, select")) e.preventDefault(); });
        ws.addEventListener("contextmenu", onContextMenu);
        document.getElementById("blkTidy").onclick = tidy;
        document.getElementById("blkClear").onclick = () => {
            if (!program.scripts.length || confirm("Удалить все блоки?")) { snapshot(); program.scripts = []; changed(); }
        };
        document.getElementById("blkCopy").onclick = () => navigator.clipboard.writeText(document.getElementById("blkCode").value).then(() => alertMsg("Скопировано"));
        renderPalette();
        renderWorkspace();
    }

    const api = {
        registerModule, init, defs, modules,
        get program() { return program; },
        get root() { return root; },
        get scope() { return scope; },
        setProgram, parseLogicFile, generate, generateItem, isEmpty, setScope, setItem, visibleModules,
        blockInScope: d => blockInScope(d, scope), GEN_MARK, onScopeChange: null,
        refresh() { renderPalette(); renderWorkspace(); updateCodePreview(); },
        undo, redo, addVariable, removeVariable, addProc,
        controlsOf, host: () => host,
        // для других видов (узлы)
        makeBlock, snapshot, changed, reId, outType, shapeOf, isReporter, isBlock, validateBlock,
        renderPalette, el,
        afterRender: null, paletteOverride: null,
    };
    return api;
})();
