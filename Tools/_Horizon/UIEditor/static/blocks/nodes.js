/* Узловой вид логики (как ноды в Blender / канвас в Obsidian).
 *
 * Это другой взгляд на ту же программу, что и в блоках: каждый блок — узел,
 * белые стрелки — порядок выполнения («далее», «тело», «иначе»),
 * цветные провода — значения (вывод круглого/шестиугольного блока → поле другого узла).
 * Позиции узлов хранятся в самих блоках (nx, ny), поэтому переключение «Блоки ↔ Узлы» ничего не теряет.
 */
"use strict";

const UINodes = (() => {
    const B = UIBlocks;
    let active = false;
    const shown = () => active && document.getElementById("logicView").classList.contains("active");
    let zoom = 1;
    let selected = null;
    let wire = null;   // протягиваемый провод
    let move = null;   // перетаскиваемый узел
    let pan = null;    // прокрутка полотна мышью
    const NODE_W = 220;

    const TYPE_LABEL = { control: "элемент", var: "переменная", proc: "блок", choice: "вид", num: "число", text: "текст", any: "значение", bool: "условие", color: "цвет" };
    const PORT_COLOR = { num: "#4caf50", text: "#b48cff", any: "#9aa0a6", bool: "#2ec4b6", exec: "#ffffff" };

    const el = (t, c, x) => B.el(t, c, x);
    const defOf = b => B.defs[b.op];
    const isStmt = b => { const d = defOf(b); return d && !["reporter", "bool"].includes(d.shape); };
    const isValueArg = spec => ["num", "text", "any", "bool"].includes(spec.type);

    // ---------------------------------------------------------------
    //  Обход модели
    // ---------------------------------------------------------------
    function forEachBlock(fn) {
        const visit = (b, parent) => {
            if (!B.isBlock(b)) return;
            fn(b, parent);
            for (const [k, v] of Object.entries(b.args || {})) if (B.isBlock(v)) visit(v, { kind: "slot", block: b, name: k });
            (b.sub || []).forEach((c, i) => visit(c, { kind: "list", list: b.sub, index: i, owner: b }));
            (b.sub2 || []).forEach((c, i) => visit(c, { kind: "list", list: b.sub2, index: i, owner: b }));
        };
        B.program.scripts.forEach(s => s.blocks.forEach((b, i) => visit(b, { kind: "list", list: s.blocks, index: i, script: s })));
    }
    function locate(target) {
        let found = null;
        forEachBlock((b, parent) => { if (b === target) found = parent; });
        return found;
    }
    function byId(id) {
        let found = null;
        forEachBlock(b => { if (b.id === id) found = b; });
        return found;
    }
    function contains(root, target) { // target внутри root (включая цепочку после root не считаем)
        if (root === target) return true;
        const kids = [...Object.values(root.args || {}).filter(B.isBlock), ...(root.sub || []), ...(root.sub2 || [])];
        return kids.some(k => contains(k, target));
    }
    function chainContains(chain, target) { return chain.some(b => contains(b, target)); }
    function cleanupScripts() { B.program.scripts = B.program.scripts.filter(s => s.blocks.length); }
    function addFree(blocks) {
        if (!blocks.length) return;
        const f = blocks[0];
        B.program.scripts.push({ x: Math.round((f.nx || 40) / 2), y: Math.round((f.ny || 40) / 2), blocks });
    }
    function resetArg(owner, name) {
        const spec = defOf(owner).args[name];
        if (spec.def !== undefined) owner.args[name] = String(spec.def); else delete owner.args[name];
    }

    // ---------------------------------------------------------------
    //  Операции над связями (меняют дерево программы)
    // ---------------------------------------------------------------
    function detachChain(b) { // b и всё, что выполняется после него
        const loc = locate(b);
        if (!loc || loc.kind !== "list") return [b];
        const chain = loc.list.splice(loc.index);
        cleanupScripts();
        return chain;
    }
    function connectExec(a, port, b) {
        if (a === b) return false;
        const db = defOf(b);
        if (!db || db.shape === "hat" || !isStmt(b)) return false;
        // запрет циклов: a не должен оказаться внутри переносимой цепочки
        const locB = locate(b);
        const chainPreview = locB && locB.kind === "list" ? locB.list.slice(locB.index) : [b];
        if (chainContains(chainPreview, a)) return false;
        B.snapshot();
        const chain = detachChain(b);
        if (port === "next") {
            const la = locate(a);
            if (!la || la.kind !== "list") return false;
            addFree(la.list.splice(la.index + 1));
            la.list.push(...chain);
        } else {
            const list = port === "body" ? a.sub : a.sub2;
            addFree(list.splice(0));
            list.push(...chain);
        }
        cleanupScripts();
        return true;
    }
    function connectValue(r, owner, name) {
        const spec = defOf(owner).args[name];
        if (!spec || !isValueArg(spec)) return false;
        if (contains(r, owner)) return false;
        B.snapshot();
        const old = owner.args[name];
        if (B.isBlock(old)) { resetArg(owner, name); addFree([old]); }
        const loc = locate(r);
        let src = r;
        if (loc && loc.kind === "slot") {
            // один выход → несколько входов: подключаем копию узла
            src = JSON.parse(JSON.stringify(r));
            B.reId(src);
            shiftPositions(src, 30, 30);
        } else if (loc && loc.kind === "list") {
            loc.list.splice(loc.index, 1);
            cleanupScripts();
        }
        owner.args[name] = src;
        return true;
    }
    function shiftPositions(b, dx, dy) {
        if (!B.isBlock(b)) return;
        if (b.nx !== undefined) { b.nx += dx; b.ny += dy; }
        Object.values(b.args || {}).forEach(v => shiftPositions(v, dx, dy));
    }
    function disconnectInput(owner, name) {
        const v = owner.args[name];
        if (!B.isBlock(v)) return null;
        B.snapshot();
        resetArg(owner, name);
        addFree([v]);
        return v;
    }
    function disconnectExecIn(b) {
        const loc = locate(b);
        if (!loc || loc.kind !== "list" || (loc.script && loc.index === 0)) return false;
        B.snapshot();
        addFree(detachChain(b));
        return true;
    }
    function deleteNode(b) {
        const loc = locate(b);
        if (!loc) return;
        B.snapshot();
        for (const [k, v] of Object.entries(b.args || {})) if (B.isBlock(v)) addFree([v]);
        if (b.sub && b.sub.length) addFree(b.sub.splice(0));
        if (b.sub2 && b.sub2.length) addFree(b.sub2.splice(0));
        if (loc.kind === "list") loc.list.splice(loc.index, 1);
        else resetArg(loc.block, loc.name);
        cleanupScripts();
    }

    // ---------------------------------------------------------------
    //  Авторасстановка
    // ---------------------------------------------------------------
    function estHeight(b) {
        const d = defOf(b);
        if (!d) return 40;
        let rows = Object.keys(d.args).length;
        if (d.shape === "c") rows += 1;
        if (d.shape === "c2") rows += 2;
        return 36 + rows * 30;
    }
    const repArgs = r => Object.values(r.args || {}).filter(B.isBlock);
    // ширина дерева значений: сам узел + его входы левее
    function repWidth(r) {
        const kids = repArgs(r);
        return NODE_W + (kids.length ? 50 + Math.max(...kids.map(repWidth)) : 0);
    }
    // значение ставим так, чтобы его правый край был на x + width; входы — левее, столбиком
    function placeRep(r, x, y) {
        const w = repWidth(r);
        r.nx = x + w - NODE_W; r.ny = y;
        let yy = y, bottom = y + estHeight(r);
        for (const k of repArgs(r)) {
            const res = placeRep(k, x + w - NODE_W - 50 - repWidth(k), yy);
            yy = res.bottom + 16; bottom = Math.max(bottom, res.bottom);
        }
        return { bottom };
    }
    function placeChain(list, x, y) {
        let curX = x, bottom = y, right = x;
        for (const b of list) {
            const kids = repArgs(b);
            const colW = kids.length ? Math.max(...kids.map(repWidth)) + 50 : 0;
            // столбик значений слева от узла
            let yy = y + 30;
            for (const k of kids) {
                const res = placeRep(k, curX, yy);
                yy = res.bottom + 16;
            }
            b.nx = curX + colW; b.ny = y;
            let below = Math.max(yy, y + estHeight(b) + 30);
            let r = b.nx + NODE_W;
            for (const sub of [b.sub, b.sub2]) {
                if (!sub || !sub.length) continue;
                const res = placeChain(sub, b.nx + 40, below);
                below = res.bottom + 30; r = Math.max(r, res.right);
            }
            bottom = Math.max(bottom, below);
            right = Math.max(right, r);
            curX = r + 70;
        }
        return { right, bottom };
    }
    function layout(all) {
        let y = 40;
        if (!all) forEachBlock(b => { if (b.nx !== undefined) y = Math.max(y, b.ny + estHeight(b) + 60); });
        for (const s of B.program.scripts) {
            let missing = false;
            const check = b => { if (!B.isBlock(b)) return; if (b.nx === undefined) missing = true; Object.values(b.args || {}).forEach(check); (b.sub || []).forEach(check); (b.sub2 || []).forEach(check); };
            s.blocks.forEach(check);
            if (!all && !missing) continue;
            const first = s.blocks[0];
            const keep = !all && first && first.nx !== undefined;
            const res = placeChain(s.blocks, keep ? first.nx : 40, keep ? first.ny : y);
            if (!keep) y = Math.max(y, res.bottom + 60);
        }
    }

    // ---------------------------------------------------------------
    //  Рендер
    // ---------------------------------------------------------------
    const inner = () => document.getElementById("nodeInner");

    function render() {
        if (!active) return;
        layout(false);
        const root = inner();
        root.innerHTML = "";
        const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
        svg.id = "nodeWires";
        root.appendChild(svg);
        let maxX = 0, maxY = 0;
        forEachBlock((b, parent) => {
            const n = renderNode(b, parent);
            root.appendChild(n);
            maxX = Math.max(maxX, b.nx + 400); maxY = Math.max(maxY, b.ny + 400);
        });
        root.style.width = Math.max(maxX, 2400) + "px";
        root.style.height = Math.max(maxY, 1600) + "px";
        root.style.transform = `scale(${zoom})`;
        if (!B.program.scripts.length) {
            const hint = el("div", "blk-empty-hint");
            hint.innerHTML = "Перетащите узел из палитры (например, <b>когда нажата</b>),<br>затем протяните стрелку от его выхода <b>▶</b> к входу следующего узла.<br>Значения (переменные, текст) подключаются цветными проводами к полям.";
            root.appendChild(hint);
        }
        drawWires();
    }

    function title(d) {
        return d.text.replace(/\{\w+\}/g, "…").replace(/\s+/g, " ").trim();
    }
    // заголовок конкретного узла: у блоков без своего текста (переменная) — понятное имя
    function nodeTitle(b) {
        const d = defOf(b);
        if (!d) return b.op;
        if (b.op === "var_get") return `переменная «${b.args.V || "?"}»`;
        const t = title(d);
        return /^[…\s]*$/.test(t) ? d.module.name : t;
    }

    function renderNode(b, parent) {
        const d = defOf(b);
        const n = el("div", "node");
        n.dataset.nid = b.id;
        n.style.left = b.nx + "px";
        n.style.top = b.ny + "px";
        if (b === selected) n.classList.add("sel");
        if (!d) { n.appendChild(el("div", "node-head", "неизвестный: " + b.op)); return n; }
        n.style.setProperty("--c", d.color || d.module.color);
        const warn = B.validateBlock(b);
        if (warn) { n.classList.add("warn"); n.title = warn; }
        if (!parent || (parent.kind === "list" && parent.script && parent.index === 0 && d.shape !== "hat")) {
            n.classList.add("free");
            if (!n.title) n.title = "Не подключено: узел не выполняется, пока его не соединят с событием";
        }

        const head = el("div", "node-head");
        const stmt = isStmt(b);
        if (stmt && d.shape !== "hat") head.appendChild(port(b, "in", "exec", "exec"));
        head.appendChild(el("span", "node-title", nodeTitle(b)));
        if (stmt && d.shape !== "cap") head.appendChild(port(b, "out", "next", "exec"));
        if (!stmt) head.appendChild(port(b, "out", "value", B.outType(b)));
        n.appendChild(head);

        for (const [name, spec] of Object.entries(d.args)) {
            const row = el("div", "node-row");
            if (isValueArg(spec)) row.appendChild(port(b, "in", name, spec.type));
            row.appendChild(el("span", "node-lbl", TYPE_LABEL[spec.type] || name));
            const v = b.args[name];
            if (B.isBlock(v)) row.appendChild(el("span", "node-linked", "← " + nodeTitle(v)));
            else if (spec.type !== "bool") row.appendChild(field(b, name, spec));
            else row.appendChild(el("span", "node-linked dim", "нет (ложь)"));
            n.appendChild(row);
        }
        if (d.shape === "c" || d.shape === "c2") {
            const r1 = el("div", "node-row out");
            r1.appendChild(el("span", "node-lbl", d.shape === "c2" ? "то" : "тело"));
            r1.appendChild(port(b, "out", "body", "exec"));
            n.appendChild(r1);
            if (d.shape === "c2") {
                const r2 = el("div", "node-row out");
                r2.appendChild(el("span", "node-lbl", d.text2 || "иначе"));
                r2.appendChild(port(b, "out", "else", "exec"));
                n.appendChild(r2);
            }
        }
        return n;
    }

    function port(b, dir, name, type) {
        const p = el("span", `nport ${dir} t-${type}`);
        p.dataset.nid = b.id;
        p.dataset.dir = dir;
        p.dataset.port = name;
        p.dataset.type = type;
        p.title = type === "exec" ? (dir === "in" ? "вход: откуда приходит выполнение" : { next: "далее", body: "тело", else: "иначе" }[name]) : `${dir === "in" ? "вход" : "выход"}: ${TYPE_LABEL[type] || type}`;
        return p;
    }

    function field(b, name, spec) {
        let inp;
        const set = v => { B.snapshot(); b.args[name] = v; B.changed(false); };
        if (["num", "text", "any"].includes(spec.type)) {
            inp = el("input");
            inp.value = b.args[name] ?? "";
            inp.addEventListener("change", () => set(inp.value));
        } else if (spec.type === "color") {
            inp = el("input"); inp.type = "color";
            inp.value = /^#[0-9a-f]{6}$/i.test(b.args[name]) ? b.args[name] : "#ffffff";
            inp.addEventListener("change", () => set(inp.value.toUpperCase()));
        } else {
            inp = el("select");
            let opts;
            if (spec.type === "control") opts = B.controlsOf(spec).map(c => [c.name, c.name]);
            else if (spec.type === "var") opts = B.program.vars.filter(v => !spec.vtypes || spec.vtypes.includes(v.type)).map(v => [v.name, v.name]);
            else if (spec.type === "proc") opts = B.program.procs.map(x => [x, x]);
            else opts = (typeof spec.options === "function" ? spec.options(B) : spec.options) || [];
            const v = b.args[name];
            if (v && !opts.some(o => o[1] === v)) opts = [[v + " (?)", v], ...opts];
            if (!opts.length) opts = [["—", ""]];
            for (const [l, val] of opts) { const o = el("option", "", l); o.value = val; inp.appendChild(o); }
            inp.value = v ?? "";
            inp.addEventListener("change", () => { set(inp.value); render(); });
        }
        inp.className = "node-field";
        return inp;
    }

    // ---------------------------------------------------------------
    //  Провода
    // ---------------------------------------------------------------
    function portPos(nid, dir, name) {
        const p = inner().querySelector(`.nport[data-nid="${nid}"][data-dir="${dir}"][data-port="${name}"]`);
        if (!p) return null;
        const r = p.getBoundingClientRect(), ir = inner().getBoundingClientRect();
        return { x: (r.left + r.width / 2 - ir.left) / zoom, y: (r.top + r.height / 2 - ir.top) / zoom, type: p.dataset.type };
    }
    function edges() {
        const out = [];
        const chain = (list, from) => {
            list.forEach((b, i) => {
                if (i === 0 && from) out.push({ a: from.id, ap: from.port, b: b.id, kind: "exec" });
                if (i > 0) out.push({ a: list[i - 1].id, ap: "next", b: b.id, kind: "exec" });
            });
        };
        forEachBlock(b => {
            for (const [k, v] of Object.entries(b.args || {})) if (B.isBlock(v)) out.push({ a: v.id, ap: "value", b: b.id, bp: k, kind: "value" });
            if (b.sub && b.sub.length) chain(b.sub, { id: b.id, port: "body" });
            if (b.sub2 && b.sub2.length) chain(b.sub2, { id: b.id, port: "else" });
        });
        B.program.scripts.forEach(s => chain(s.blocks, null));
        return out;
    }
    function curve(p1, p2) {
        const dx = Math.max(50, Math.abs(p2.x - p1.x) / 2);
        return `M ${p1.x} ${p1.y} C ${p1.x + dx} ${p1.y}, ${p2.x - dx} ${p2.y}, ${p2.x} ${p2.y}`;
    }
    function drawWires() {
        const svg = document.getElementById("nodeWires");
        if (!svg) return;
        svg.innerHTML = "";
        const ns = "http://www.w3.org/2000/svg";
        for (const e of edges()) {
            const p1 = portPos(e.a, "out", e.ap);
            const p2 = e.kind === "exec" ? portPos(e.b, "in", "exec") : portPos(e.b, "in", e.bp);
            if (!p1 || !p2) continue;
            const path = document.createElementNS(ns, "path");
            path.setAttribute("d", curve(p1, p2));
            path.setAttribute("class", "wire " + (e.kind === "exec" ? "exec" : "val"));
            path.setAttribute("stroke", e.kind === "exec" ? PORT_COLOR.exec : (PORT_COLOR[p1.type] || PORT_COLOR.any));
            svg.appendChild(path);
        }
        if (wire) {
            const path = document.createElementNS(ns, "path");
            const a = wire.fromDir === "out" ? wire.p : wire.cur, b2 = wire.fromDir === "out" ? wire.cur : wire.p;
            path.setAttribute("d", curve(a, b2));
            path.setAttribute("class", "wire temp");
            path.setAttribute("stroke", PORT_COLOR[wire.type] || "#fff");
            svg.appendChild(path);
        }
    }

    // ---------------------------------------------------------------
    //  Мышь
    // ---------------------------------------------------------------
    function toInner(ev) {
        const ir = inner().getBoundingClientRect();
        return { x: (ev.clientX - ir.left) / zoom, y: (ev.clientY - ir.top) / zoom };
    }

    function onDown(ev) {
        if (!shown() || ev.button !== 0) return;
        const t = ev.target;
        if (t.closest("input, select")) return;
        const p = t.closest(".nport");
        if (p) {
            ev.preventDefault();
            startWire(p, ev);
            return;
        }
        const n = t.closest(".node");
        if (n) {
            ev.preventDefault();
            const b = byId(n.dataset.nid);
            selected = b;
            inner().querySelectorAll(".node.sel").forEach(x => x.classList.remove("sel"));
            n.classList.add("sel");
            const pos = toInner(ev);
            move = { b, el: n, dx: pos.x - b.nx, dy: pos.y - b.ny, moved: false, withChildren: ev.shiftKey };
            return;
        }
        if (t.closest("#nodeScroll")) {
            selected = null;
            inner().querySelectorAll(".node.sel").forEach(x => x.classList.remove("sel"));
            const sc = document.getElementById("nodeScroll");
            pan = { x: ev.clientX, y: ev.clientY, sl: sc.scrollLeft, st: sc.scrollTop };
        }
    }

    function startWire(p, ev) {
        const b = byId(p.dataset.nid);
        const dir = p.dataset.dir, name = p.dataset.port;
        // тянем от занятого входа — снимаем провод и держим его за начало
        if (dir === "in") {
            if (name === "exec") {
                const loc = locate(b);
                if (loc && loc.kind === "list" && !(loc.script && loc.index === 0)) {
                    const src = loc.index > 0 ? { b: loc.list[loc.index - 1], port: "next" } : { b: loc.owner, port: loc.list === (loc.owner && loc.owner.sub) ? "body" : "else" };
                    disconnectExecIn(b);
                    B.changed();
                    const sp = portPos(src.b.id, "out", src.port);
                    if (sp) { wire = { from: src.b, fromDir: "out", port: src.port, type: "exec", p: sp, cur: toInner(ev) }; drawWires(); return; }
                }
            } else if (B.isBlock(b.args[name])) {
                const r = disconnectInput(b, name);
                B.changed();
                const sp = portPos(r.id, "out", "value");
                if (sp) { wire = { from: r, fromDir: "out", port: "value", type: B.outType(r), p: sp, cur: toInner(ev) }; drawWires(); return; }
            }
        }
        const pos = portPos(b.id, dir, name);
        wire = { from: b, fromDir: dir, port: name, type: p.dataset.type, p: pos, cur: toInner(ev) };
        drawWires();
    }

    function onMove(ev) {
        if (!shown()) return;
        if (wire) {
            wire.cur = toInner(ev);
            drawWires();
            inner().querySelectorAll(".nport.hot").forEach(x => x.classList.remove("hot"));
            const t = document.elementFromPoint(ev.clientX, ev.clientY);
            const p = t && t.closest && t.closest(".nport");
            if (p && compatible(p)) p.classList.add("hot");
        } else if (move) {
            const pos = toInner(ev);
            const nx = Math.max(0, Math.round(pos.x - move.dx)), ny = Math.max(0, Math.round(pos.y - move.dy));
            const dx = nx - move.b.nx, dy = ny - move.b.ny;
            if (!dx && !dy) return;
            if (!move.moved) { B.snapshot(); move.moved = true; }
            const shift = b => {
                b.nx += dx; b.ny += dy;
                const e = inner().querySelector(`.node[data-nid="${b.id}"]`);
                if (e) { e.style.left = b.nx + "px"; e.style.top = b.ny + "px"; }
            };
            if (move.withChildren) { // Shift — переместить узел вместе с подключёнными значениями и телом
                const walk = b => { shift(b); Object.values(b.args || {}).forEach(v => B.isBlock(v) && walk(v)); [...(b.sub || []), ...(b.sub2 || [])].forEach(walk); };
                walk(move.b);
            } else shift(move.b);
            drawWires();
        } else if (pan) {
            const sc = document.getElementById("nodeScroll");
            sc.scrollLeft = pan.sl - (ev.clientX - pan.x);
            sc.scrollTop = pan.st - (ev.clientY - pan.y);
        }
    }

    function compatible(p) {
        if (!wire) return false;
        const b = byId(p.dataset.nid);
        if (!b || b === wire.from) return false;
        if (p.dataset.dir === wire.fromDir) return false;
        const isExec = p.dataset.type === "exec";
        if (isExec !== (wire.type === "exec")) return false;
        if (isExec) return true;
        // любое значение подходит к любому полю: типы приводятся автоматически
        return true;
    }

    function onUp(ev) {
        if (!shown()) return;
        if (wire) {
            const t = document.elementFromPoint(ev.clientX, ev.clientY);
            const p = t && t.closest && t.closest(".nport");
            let ok = false;
            if (p && compatible(p)) {
                const other = byId(p.dataset.nid);
                const [outB, outPort, inB, inPort] = wire.fromDir === "out"
                    ? [wire.from, wire.port, other, p.dataset.port]
                    : [other, p.dataset.port, wire.from, wire.port];
                ok = wire.type === "exec" || p.dataset.type === "exec"
                    ? connectExec(outB, outPort, inB)
                    : connectValue(outB, inB, inPort);
                if (!ok) toast("Так соединить нельзя (получился бы цикл или неподходящий тип)");
            }
            wire = null;
            B.changed();
        } else if (move) {
            if (move.moved) B.changed(false);
            move = null;
        }
        pan = null;
    }

    function onKey(ev) {
        if (!shown() || !selected || ev.target.closest("input, select, textarea")) return;
        if (ev.key === "Delete" || ev.key === "Backspace") {
            ev.preventDefault();
            deleteNode(selected);
            selected = null;
            B.changed();
        } else if ((ev.ctrlKey || ev.metaKey) && ev.key.toLowerCase() === "d") {
            ev.preventDefault();
            const copy = JSON.parse(JSON.stringify(selected));
            delete copy.sub; delete copy.sub2;
            if (defOf(copy).shape === "c" || defOf(copy).shape === "c2") { copy.sub = []; if (defOf(copy).shape === "c2") copy.sub2 = []; }
            for (const [k, v] of Object.entries(copy.args)) if (B.isBlock(v)) resetArg(copy, k);
            B.reId(copy);
            copy.nx += 40; copy.ny += 40;
            B.snapshot();
            B.program.scripts.push({ x: 20, y: 20, blocks: [copy] });
            selected = copy;
            B.changed();
        }
    }

    function onWheel(ev) {
        if (!shown() || !(ev.ctrlKey || ev.metaKey)) return;
        ev.preventDefault();
        setZoom(zoom * (ev.deltaY < 0 ? 1.1 : 1 / 1.1));
    }
    function setZoom(z) {
        zoom = Math.min(2, Math.max(0.3, z));
        inner().style.transform = `scale(${zoom})`;
        document.getElementById("nodeZoom").textContent = Math.round(zoom * 100) + "%";
        drawWires();
    }

    // ---------------------------------------------------------------
    //  Палитра узлов
    // ---------------------------------------------------------------
    function palette(pal) {
        const isWin = B.host() && B.host().isWindow();
        for (const m of B.visibleModules()) {
            const head = el("div", "blk-pal-head", m.name);
            head.id = "pal-" + m.id;
            head.style.color = m.color;
            pal.appendChild(head);
            if (m.paletteTop) m.paletteTop(pal, B);
            for (const d of m.blocks) {
                if (d.hidden || !B.blockInScope(d) || (d.windowOnly && !isWin) || (d.paletteIf && !d.paletteIf(B))) continue;
                const it = el("div", "node-pal-item", /^[…\s]*$/.test(title(d)) ? (d.id === "var_get" ? "переменная" : m.name) : title(d));
                it.style.setProperty("--c", d.color || m.color);
                it.dataset.op = d.id;
                if (d.help) it.title = d.help;
                it.addEventListener("pointerdown", ev => startPaletteDrag(ev, d));
                pal.appendChild(it);
            }
        }
    }
    function startPaletteDrag(ev, d) {
        ev.preventDefault();
        const ghost = el("div", "node-pal-item ghost", title(d));
        ghost.style.setProperty("--c", d.color || d.module.color);
        document.body.appendChild(ghost);
        const mv = e => { ghost.style.left = e.clientX - 10 + "px"; ghost.style.top = e.clientY - 10 + "px"; };
        mv(ev);
        document.addEventListener("pointermove", mv);
        document.addEventListener("pointerup", e => {
            document.removeEventListener("pointermove", mv);
            ghost.remove();
            const sc = document.getElementById("nodeScroll").getBoundingClientRect();
            if (e.clientX < sc.left || e.clientX > sc.right || e.clientY < sc.top || e.clientY > sc.bottom) return;
            const pos = toInner(e);
            const b = B.makeBlock(d.id);
            b.nx = Math.max(0, Math.round(pos.x - 10)); b.ny = Math.max(0, Math.round(pos.y - 10));
            B.snapshot();
            B.program.scripts.push({ x: 20, y: 20, blocks: [b] });
            selected = b;
            B.changed();
        }, { once: true });
    }

    // ---------------------------------------------------------------
    //  Переключение вида
    // ---------------------------------------------------------------
    let toast = () => { };
    function setMode(nodes) {
        active = nodes;
        document.getElementById("logicView").classList.toggle("nodes-mode", nodes);
        document.getElementById("blkModeBlocks").classList.toggle("active", !nodes);
        document.getElementById("blkModeNodes").classList.toggle("active", nodes);
        B.paletteOverride = nodes ? palette : null;
        try { localStorage.setItem("ss14-ui-editor-logic-mode", nodes ? "nodes" : "blocks"); } catch (e) { /* ignore */ }
        B.renderPalette();
        if (nodes) render(); else B.refresh();
    }

    function init(opts = {}) {
        toast = opts.toast || toast;
        B.afterRender = () => render();
        const sc = document.getElementById("nodeScroll");
        sc.addEventListener("pointerdown", onDown);
        document.addEventListener("pointermove", onMove);
        document.addEventListener("pointerup", onUp);
        document.addEventListener("keydown", onKey);
        sc.addEventListener("wheel", onWheel, { passive: false });
        document.getElementById("blkModeBlocks").onclick = () => setMode(false);
        document.getElementById("blkModeNodes").onclick = () => setMode(true);
        document.getElementById("nodeLayout").onclick = () => { B.snapshot(); layout(true); B.changed(); };
        document.getElementById("nodeZoomIn").onclick = () => setZoom(zoom * 1.2);
        document.getElementById("nodeZoomOut").onclick = () => setZoom(zoom / 1.2);
        let saved = "blocks";
        try { saved = localStorage.getItem("ss14-ui-editor-logic-mode") || "blocks"; } catch (e) { /* ignore */ }
        setMode(saved === "nodes");
    }

    return {
        init, setMode, render, layout,
        get active() { return active; },
        // для тестов
        _connectExec: connectExec, _connectValue: connectValue, _byId: byId,
    };
})();
