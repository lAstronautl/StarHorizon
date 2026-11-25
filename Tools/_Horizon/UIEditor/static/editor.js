/* SS14 UI Editor — визуальный редактор XAML для Robust UI. */
"use strict";

// ===================================================================
//  Модель документа
// ===================================================================
let nextId = 1;
const byId = new Map();

function mkNode(kind, tag, attrs = [], children = [], value = "") {
    const n = { id: nextId++, kind, tag, attrs, children, value, parent: null };
    byId.set(n.id, n);
    for (const c of children) c.parent = n;
    return n;
}

const doc = { root: null, path: null };
let selectedId = null;
let serverMode = false;
let locale = {};
const tabView = new Map(); // id TabContainer -> индекс открытой вкладки (только в редакторе)

function getAttr(n, k) {
    const a = n.attrs.find(a => a[0] === k);
    return a ? a[1] : undefined;
}
function setAttr(n, k, v) {
    const i = n.attrs.findIndex(a => a[0] === k);
    if (v === undefined || v === null || v === "") {
        if (i >= 0) n.attrs.splice(i, 1);
    } else if (i >= 0) n.attrs[i][1] = v;
    else if (k === "Name" || k === "Access") {
        // Name/Access — сразу после xmlns, как принято в репозитории
        let at = 0;
        n.attrs.forEach((a, j) => { if (a[0].startsWith("xmlns") || a[0] === "Name") at = j + 1; });
        n.attrs.splice(at, 0, [k, v]);
    } else n.attrs.push([k, v]);
}
const isPropElem = n => n.kind === "el" && n.tag.includes(".");
const localName = tag => tag.split(":").pop();
const schemaOf = n => CONTROL_MAP[n.tag] || CONTROL_MAP[localName(n.tag)] || null;
const elChildren = n => n.children.filter(c => c.kind === "el" && !isPropElem(c));

function childMode(n) {
    if (isPropElem(n)) return "none";
    const s = schemaOf(n);
    return s ? s.children : "multi"; // неизвестные (пользовательские) контролы считаем контейнерами
}
function canHoldChild(n) {
    const m = childMode(n);
    if (m === "multi") return true;
    if (m === "single") return elChildren(n).length === 0;
    return false;
}
function isAncestor(a, b) { // a — предок b?
    for (let p = b; p; p = p.parent) if (p === a) return true;
    return false;
}
function cloneNode(n) {
    return mkNode(n.kind, n.tag, n.attrs.map(a => [a[0], a[1]]), n.children.map(cloneNode), n.value);
}
function removeNode(n) {
    if (!n.parent) return;
    const arr = n.parent.children;
    arr.splice(arr.indexOf(n), 1);
    n.parent = null;
}
function insertNode(parent, n, index) {
    // index — среди "контрольных" детей; переводим в индекс массива children
    const ctrls = elChildren(parent);
    let at;
    if (index === undefined || index >= ctrls.length) at = parent.children.length;
    else at = parent.children.indexOf(ctrls[Math.max(0, index)]);
    parent.children.splice(at, 0, n);
    n.parent = parent;
}

// ===================================================================
//  Парсинг / сериализация XAML
// ===================================================================
function parseXaml(text) {
    text = text.replace(/^﻿/, "");
    const dom = new DOMParser().parseFromString(text, "application/xml");
    const err = dom.getElementsByTagName("parsererror")[0];
    if (err) throw new Error((err.querySelector("div") || err).textContent.trim().split("\n")[0]);
    return convertDom(dom.documentElement);
}
function convertDom(el) {
    const attrs = [];
    for (const a of el.attributes) attrs.push([a.name, a.value]);
    const children = [];
    for (const c of el.childNodes) {
        if (c.nodeType === 1) children.push(convertDom(c));
        else if (c.nodeType === 8) children.push(mkNode("comment", "", [], [], c.nodeValue));
        else if (c.nodeType === 3 || c.nodeType === 4) {
            if (c.nodeValue.trim()) children.push(mkNode("text", "", [], [], c.nodeValue.trim()));
        }
    }
    return mkNode("el", el.tagName, attrs, children);
}

const escAttr = s => s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/"/g, "&quot;");
const escText = s => s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");

function serialize(n, depth = 0, isRoot = true) {
    const ind = "    ".repeat(depth);
    if (n.kind === "comment") return `${ind}<!--${n.value}-->\n`;
    if (n.kind === "text") return `${ind}${escText(n.value)}\n`;
    const parts = n.attrs.map(([k, v]) => `${k}="${escAttr(v)}"`);
    let open;
    const oneLine = `${ind}<${n.tag}${parts.length ? " " + parts.join(" ") : ""}`;
    const hasNs = n.attrs.some(a => a[0].startsWith("xmlns"));
    if (parts.length <= 1 || (!(isRoot && hasNs) && oneLine.length <= 110)) open = oneLine;
    else {
        const pad = " ".repeat(ind.length + n.tag.length + 2);
        open = `${ind}<${n.tag} ` + parts.join("\n" + pad);
    }
    const kids = n.children;
    if (!kids.length) return open + "/>\n";
    if (kids.length === 1 && kids[0].kind === "text")
        return `${open}>${escText(kids[0].value)}</${n.tag}>\n`;
    let s = open + ">\n";
    for (const c of kids) s += serialize(c, depth + 1, false);
    return s + `${ind}</${n.tag}>\n`;
}
const serializeDoc = () => (doc.root ? serialize(doc.root) : "");

function ensureNamespace(tag) {
    const p = tag.includes(":") ? tag.split(":")[0] : null;
    if (!p || !doc.root) return;
    if (getAttr(doc.root, "xmlns:" + p) !== undefined) return;
    if (NAMESPACES[p]) {
        // вставляем после последнего xmlns
        let i = 0;
        doc.root.attrs.forEach((a, j) => { if (a[0].startsWith("xmlns")) i = j + 1; });
        doc.root.attrs.splice(i, 0, ["xmlns:" + p, NAMESPACES[p]]);
    }
}
function ensureNamespacesDeep(n) {
    if (n.kind !== "el") return;
    ensureNamespace(n.tag);
    n.children.forEach(ensureNamespacesDeep);
}

function newControl(tag) {
    const s = CONTROL_MAP[tag];
    const attrs = s && s.defaults ? Object.entries(s.defaults).map(([k, v]) => [k, v]) : [];
    return mkNode("el", tag, attrs, []);
}

// ===================================================================
//  История (undo/redo)
// ===================================================================
const history = { stack: [], pos: -1 };
function pathOf(n) {
    const p = [];
    for (; n && n.parent; n = n.parent) p.unshift(n.parent.children.indexOf(n));
    return p;
}
function nodeAtPath(p) {
    let n = doc.root;
    for (const i of p) { if (!n || !n.children[i]) return null; n = n.children[i]; }
    return n;
}
function commit(keepSelection = true) {
    const snap = { xaml: serializeDoc(), sel: selectedId && byId.get(selectedId) ? pathOf(byId.get(selectedId)) : null };
    if (history.stack[history.pos] && history.stack[history.pos].xaml === snap.xaml) { refresh(); return; }
    history.stack.length = history.pos + 1;
    history.stack.push(snap);
    if (history.stack.length > 200) history.stack.shift();
    history.pos = history.stack.length - 1;
    saveDraft();
    refresh();
}
function restore(snap) {
    byId.clear();
    doc.root = parseXaml(snap.xaml);
    const n = snap.sel ? nodeAtPath(snap.sel) : null;
    selectedId = n ? n.id : null;
    saveDraft();
    refresh();
}
function undo() { if (history.pos > 0) restore(history.stack[--history.pos]); }
function redo() { if (history.pos < history.stack.length - 1) restore(history.stack[++history.pos]); }

function loadDocument(text, path) {
    byId.clear();
    doc.root = parseXaml(text);
    doc.path = path || null;
    selectedId = doc.root.id;
    history.stack = []; history.pos = -1;
    tabView.clear();
    rootSize = { w: 0, h: 0 };
    commit();
    $("#fileName").textContent = doc.path || "без имени";
    updateCsMeta();
}

// ===================================================================
//  Утилиты значений
// ===================================================================
const $ = s => document.querySelector(s);
const h = (tag, cls, text) => {
    const e = document.createElement(tag);
    if (cls) e.className = cls;
    if (text !== undefined) e.textContent = text;
    return e;
};
const num = v => (v === undefined || v === "" || isNaN(parseFloat(v)) ? null : parseFloat(v));
const isTrue = v => typeof v === "string" && v.toLowerCase() === "true";
const isFalse = v => typeof v === "string" && v.toLowerCase() === "false";

function parseThickness(v) {
    if (!v) return null;
    const p = v.trim().split(/[\s,]+/).map(Number);
    if (p.some(isNaN)) return null;
    if (p.length === 1) return { l: p[0], t: p[0], r: p[0], b: p[0] };
    if (p.length === 2) return { l: p[0], t: p[1], r: p[0], b: p[1] };
    if (p.length === 4) return { l: p[0], t: p[1], r: p[2], b: p[3] };
    return null;
}
function parseVec2(v) {
    if (!v) return null;
    const p = v.trim().split(/[\s,]+/).map(Number);
    if (p.length === 1 && !isNaN(p[0])) return [p[0], p[0]];
    if (p.length === 2 && !p.some(isNaN)) return p;
    return null;
}
function cssColor(v) {
    if (!v) return null;
    v = v.trim();
    const m = v.match(/^#([0-9a-f]{3,8})$/i);
    if (m) {
        let x = m[1];
        if (x.length === 3 || x.length === 4) x = x.split("").map(c => c + c).join("");
        if (x.length === 6) return "#" + x;
        if (x.length === 8) return "#" + x; // RRGGBBAA — CSS понимает
    }
    return v; // именованные (Red, White…) — CSS тоже понимает большинство
}
function resolveText(v) {
    if (v === undefined) return "";
    const m = v.match(/^\{\s*Loc\s+'?([^'}]+?)'?\s*\}$/);
    if (m) {
        const key = m[1].trim();
        if (!$("#lang").value) return key;
        return locale[key] !== undefined ? stripFluent(locale[key]) : key;
    }
    return v;
}
function stripFluent(s) {
    return s.replace(/\{\s*\$([A-Za-z_]+)\s*\}/g, "<$1>").replace(/\{[^}]*\}/g, "…");
}
function richToHtml(text) {
    let s = escText(text);
    s = s.replace(/\[bold\]/gi, "<b>").replace(/\[\/bold\]/gi, "</b>")
        .replace(/\[italic\]/gi, "<i>").replace(/\[\/italic\]/gi, "</i>")
        .replace(/\[color=([#\w]+)\]/gi, (_, c) => `<span style="color:${cssColor(c)}">`).replace(/\[\/color\]/gi, "</span>")
        .replace(/\[font size=(\d+)\]/gi, (_, n) => `<span style="font-size:${n}px">`).replace(/\[\/font\]/gi, "</span>")
        .replace(/\[head=(\d)\]/gi, (_, n) => `<span style="font-weight:bold;font-size:${22 - n * 3}px">`).replace(/\[\/head\]/gi, "</span>")
        .replace(/\[bullet\/?\]/gi, "• ")
        .replace(/\[\/?[a-z]+(=[^\]]*)?\/?\]/gi, "");
    return s;
}
function hasClass(n, c) {
    const v = getAttr(n, "StyleClasses");
    return !!v && v.split(/[\s,]+/).includes(c);
}

// ===================================================================
//  Предпросмотр (рендер в HTML с приближением раскладки Robust)
// ===================================================================
let renderMap = new Map(); // id -> element

function renderCanvas() {
    const host = $("#canvasHost");
    host.innerHTML = "";
    renderMap = new Map();
    if (!doc.root) return;
    host.style.zoom = $("#zoom").value;
    host.classList.toggle("bounds", $("#showBounds").checked);
    host.appendChild(renderRoot(doc.root));
    enableCanvasDrag();
    markSelection();
}

function renderRoot(n) {
    const s = schemaOf(n);
    const frame = h("div", "root-frame");
    const ms = parseVec2(getAttr(n, "MinSize")) || [num(getAttr(n, "MinWidth")) || 0, num(getAttr(n, "MinHeight")) || 0];
    const ss = parseVec2(getAttr(n, "SetSize")) || [num(getAttr(n, "SetWidth")), num(getAttr(n, "SetHeight"))];
    const isWindow = !!(s && s.root);
    const w = rootSize.w || ss[0] || Math.max(ms[0], isWindow ? 400 : 360);
    // окно — фиксированного размера; обычный контрол — по высоте содержимого, пока его не растянули
    const hgt = rootSize.h || ss[1] || (isWindow ? Math.max(ms[1], 300) : null);
    frame.style.width = w + "px";
    if (hgt) frame.style.height = hgt + "px";
    frame.style.minWidth = (ms[0] || 50) + "px";
    frame.style.minHeight = (ms[1] || 30) + "px";
    if (rootObserver) rootObserver.disconnect();
    rootObserver = new ResizeObserver(() => {
        if (!frame.isConnected) return;
        // запоминаем размер, только если пользователь его поменял (тянул за угол)
        if (frame.style.height || isWindow) rootSize = { w: frame.offsetWidth, h: frame.offsetHeight };
        else if (frame.offsetWidth !== w) rootSize = { w: frame.offsetWidth, h: 0 };
        const st = document.getElementById("statusSize");
        if (st) st.textContent = `${frame.offsetWidth}×${frame.offsetHeight}`;
    });
    rootObserver.observe(frame);

    if (s && s.root) {
        const win = h("div", "ctl window " + (n.tag.endsWith("FancyWindow") ? "fancy" : "default"));
        win.dataset.id = n.id;
        renderMap.set(n.id, win);
        const head = h("div", "win-head");
        head.appendChild(h("span", "win-title", resolveText(getAttr(n, "Title")) || ""));
        head.appendChild(h("span", "win-close", "✕"));
        win.appendChild(head);
        if (n.tag.endsWith("FancyWindow")) win.appendChild(h("div", "win-divider"));
        const body = h("div", "win-body lay-stack");
        renderChildren(n, body, "stack");
        win.appendChild(body);
        frame.appendChild(win);
    } else {
        frame.classList.add("control-root");
        const el = renderNode(n, "stack");
        frame.appendChild(el);
    }
    return frame;
}
let rootSize = { w: 0, h: 0 };
let rootObserver = null;

function renderChildren(n, container, layout) {
    const kids = elChildren(n);
    kids.forEach((c, i) => {
        const el = renderNode(c, layout, i);
        if (layout === "tabs") {
            const cur = tabView.has(n.id) ? tabView.get(n.id) : (num(getAttr(n, "CurrentTab")) || 0);
            if (i !== cur) el.classList.add("tab-hidden");
        }
        container.appendChild(el);
    });
    if (!kids.length && childMode(n) !== "none" && !isPropElem(n)) container.classList.add("empty");
}

function containerLayout(n) {
    const ln = localName(n.tag);
    if (ln === "BoxContainer" || ln === "Collapsible" || ln === "CollapsibleBody")
        return getAttr(n, "Orientation") === "Horizontal" ? "box-h" : "box-v";
    if (ln === "GridContainer") return "grid";
    if (ln === "SplitContainer") return getAttr(n, "Orientation") === "Vertical" ? "box-v" : "box-h";
    if (ln === "ScrollContainer") return "scroll";
    if (ln === "TabContainer") return "tabs";
    if (!schemaOf(n)) return "unknown";
    return "stack";
}

function applyLayoutProps(n, el, parentLayout) {
    const m = parseThickness(getAttr(n, "Margin"));
    if (m) el.style.margin = `${m.t}px ${m.r}px ${m.b}px ${m.l}px`;

    const minS = parseVec2(getAttr(n, "MinSize")) || [null, null];
    const setS = parseVec2(getAttr(n, "SetSize")) || [null, null];
    const maxS = parseVec2(getAttr(n, "MaxSize")) || [null, null];
    const minW = num(getAttr(n, "MinWidth")) ?? minS[0], minH = num(getAttr(n, "MinHeight")) ?? minS[1];
    const setW = num(getAttr(n, "SetWidth")) ?? setS[0], setH = num(getAttr(n, "SetHeight")) ?? setS[1];
    const maxW = num(getAttr(n, "MaxWidth")) ?? maxS[0], maxH = num(getAttr(n, "MaxHeight")) ?? maxS[1];
    if (minW) el.style.minWidth = minW + "px";
    if (minH) el.style.minHeight = minH + "px";
    if (maxW) el.style.maxWidth = maxW + "px";
    if (maxH) el.style.maxHeight = maxH + "px";

    const hExp = isTrue(getAttr(n, "HorizontalExpand"));
    const vExp = isTrue(getAttr(n, "VerticalExpand"));
    const ratio = num(getAttr(n, "SizeFlagsStretchRatio")) || 1;
    const hA = getAttr(n, "HorizontalAlignment") || "Stretch";
    const vA = getAttr(n, "VerticalAlignment") || "Stretch";
    const selfA = { Stretch: "stretch", Left: "start", Top: "start", Center: "center", Right: "end", Bottom: "end" };
    const flexA = { Stretch: "stretch", Left: "flex-start", Top: "flex-start", Center: "center", Right: "flex-end", Bottom: "flex-end" };

    if (parentLayout === "box-h" || parentLayout === "box-v") {
        const horiz = parentLayout === "box-h";
        const exp = horiz ? hExp : vExp;
        const setMain = horiz ? setW : setH;
        el.style.flex = exp ? `${ratio} 1 0` : `0 0 ${setMain ? setMain + "px" : "auto"}`;
        el.style.alignSelf = flexA[horiz ? vA : hA] || "stretch";
        const mainA = horiz ? hA : vA;
        if (exp && mainA !== "Stretch") el.classList.add("exp-align-" + (selfA[mainA] || "start"));
        if (setW && !horiz) el.style.width = setW + "px";
        if (setH && horiz) el.style.height = setH + "px";
        if (setW && horiz && exp) el.style.minWidth = Math.max(setW, minW || 0) + "px";
        if (setH && !horiz && exp) el.style.minHeight = Math.max(setH, minH || 0) + "px";
    } else {
        // stack / grid / scroll: выравнивание внутри ячейки
        el.style.justifySelf = selfA[hA] || "stretch";
        el.style.alignSelf = selfA[vA] || "stretch";
        if (setW) el.style.width = setW + "px";
        if (setH) el.style.height = setH + "px";
        if (parentLayout === "unknown") el.style.flex = "0 0 auto";
    }
    if (isFalse(getAttr(n, "Visible"))) el.classList.add("invisible-ctl");
    if (isTrue(getAttr(n, "RectClipContent"))) el.style.overflow = "hidden";
}

function renderNode(n, parentLayout, index) {
    const ln = localName(n.tag);
    const s = schemaOf(n);
    let el;
    const text = () => resolveText(getAttr(n, "Text"));

    if (s && s.children === "none") {
        el = renderLeaf(n, ln, text);
    } else {
        const lay = containerLayout(n);
        el = h("div", "ctl container lay-" + lay);
        if (lay === "box-h" || lay === "box-v") {
            const sep = num(getAttr(n, "SeparationOverride"));
            if (sep) el.style.gap = sep + "px";
            const al = getAttr(n, "Align");
            if (al) el.style.justifyContent = { Begin: "flex-start", Center: "center", End: "flex-end" }[al] || "";
            if (ln === "SplitContainer") el.classList.add("split");
        }
        if (lay === "grid") setupGrid(n, el);
        if (ln === "PanelContainer") stylePanel(n, el);
        if (ln === "StripeBack") el.classList.add("stripeback");
        if (ln === "ContainerButton") el.classList.add("containerbutton");
        if (lay === "unknown") {
            el.appendChild(h("div", "unknown-tag", n.tag));
        }
        if (lay === "tabs") {
            const bar = h("div", "tab-bar");
            const cur = tabView.has(n.id) ? tabView.get(n.id) : (num(getAttr(n, "CurrentTab")) || 0);
            elChildren(n).forEach((c, i) => {
                const title = getAttr(c, "TabContainer.TabTitle");
                const t = h("span", "tab" + (i === cur ? " cur" : ""), (title && resolveText(title)) || getAttr(c, "Name") || `Tab ${i + 1}`);
                t.addEventListener("click", ev => { ev.stopPropagation(); tabView.set(n.id, i); renderCanvas(); });
                bar.appendChild(t);
            });
            el.appendChild(bar);
            const body = h("div", "tab-body lay-stack");
            renderChildren(n, body, "tabs");
            el.appendChild(body);
        } else if (lay === "scroll") {
            if (!isFalse(getAttr(n, "HScrollEnabled"))) el.classList.add("hscroll");
            if (isFalse(getAttr(n, "VScrollEnabled"))) el.classList.add("novscroll");
            renderChildren(n, el, "scroll");
        } else if (ln === "CollapsibleHeading") {
            el.appendChild(h("div", "coll-head", "▼ " + resolveText(getAttr(n, "Title"))));
        } else {
            renderChildren(n, el, lay === "unknown" ? "box-v" : lay);
        }
    }
    el.classList.add("ctl");
    el.dataset.id = n.id;
    el.title = n.tag + (getAttr(n, "Name") ? ` "${getAttr(n, "Name")}"` : "");
    applyLayoutProps(n, el, parentLayout);
    renderMap.set(n.id, el);
    return el;
}

function setupGrid(n, el) {
    const cols = num(getAttr(n, "Columns"));
    const rows = num(getAttr(n, "Rows"));
    const kids = elChildren(n);
    const hs = num(getAttr(n, "HSeparationOverride")), vs = num(getAttr(n, "VSeparationOverride"));
    el.style.columnGap = (hs ?? 0) + "px";
    el.style.rowGap = (vs ?? 0) + "px";
    if (rows && !cols) {
        el.style.gridAutoFlow = "column";
        const rexp = Array.from({ length: rows }, (_, r) => kids.some((k, i) => i % rows === r && isTrue(getAttr(k, "VerticalExpand"))));
        el.style.gridTemplateRows = rexp.map(e => (e ? "1fr" : "auto")).join(" ");
    } else {
        const c = cols || 1;
        const cexp = Array.from({ length: c }, (_, col) => kids.some((k, i) => i % c === col && isTrue(getAttr(k, "HorizontalExpand"))));
        el.style.gridTemplateColumns = cexp.map(e => (e ? "minmax(0,1fr)" : "auto")).join(" ");
        const nRows = Math.ceil(kids.length / c);
        const rexp = Array.from({ length: nRows }, (_, r) => kids.some((k, i) => Math.floor(i / c) === r && isTrue(getAttr(k, "VerticalExpand"))));
        el.style.gridTemplateRows = rexp.map(e => (e ? "1fr" : "auto")).join(" ");
    }
}

function stylePanel(n, el) {
    const po = n.children.find(c => c.kind === "el" && c.tag.endsWith("PanelContainer.PanelOverride"));
    const sb = po && po.children.find(c => c.kind === "el");
    if (sb) {
        const bg = cssColor(getAttr(sb, "BackgroundColor"));
        if (bg) el.style.background = bg;
        const bc = cssColor(getAttr(sb, "BorderColor"));
        const bt = parseThickness(getAttr(sb, "BorderThickness"));
        if (bt) {
            el.style.borderStyle = "solid";
            el.style.borderColor = bc || "transparent";
            el.style.borderWidth = `${bt.t}px ${bt.r}px ${bt.b}px ${bt.l}px`;
        }
        const pad = ["Top", "Right", "Bottom", "Left"].map(s => num(getAttr(sb, `ContentMargin${s}Override`)) || 0);
        if (pad.some(Boolean)) el.style.padding = pad.map(p => p + "px").join(" ");
    }
    for (const c of ["LowDivider", "HighDivider", "AngleRect", "BackgroundDark", "Inset", "PanelBackgroundLight",
        "PanelBackgroundBaseDark", "WindowHeadingBackground"]) if (hasClass(n, c)) el.classList.add("sc-" + c);
}

function renderLeaf(n, ln, text) {
    let el;
    switch (ln) {
        case "Label": {
            el = h("div", "leaf label");
            el.appendChild(h("span", "", text()));
            const al = getAttr(n, "Align"), va = getAttr(n, "VAlign");
            el.style.justifyContent = { Center: "center", Right: "flex-end" }[al] || "flex-start";
            el.style.alignItems = { Top: "flex-start", Bottom: "flex-end" }[va] || "center";
            const c = cssColor(getAttr(n, "FontColorOverride"));
            if (c) el.style.color = c;
            if (isTrue(getAttr(n, "ClipText"))) el.classList.add("clip");
            break;
        }
        case "RichTextLabel":
            el = h("div", "leaf richtext");
            el.innerHTML = richToHtml(text());
            break;
        case "Button":
        case "ConfirmButton":
            el = h("div", "leaf button");
            el.appendChild(h("span", "", text()));
            el.style.justifyContent = { Left: "flex-start", Right: "flex-end" }[getAttr(n, "TextAlign")] || "center";
            if (isTrue(getAttr(n, "Pressed")) && isTrue(getAttr(n, "ToggleMode"))) el.classList.add("pressed");
            if (isTrue(getAttr(n, "Disabled"))) el.classList.add("disabled");
            if (isTrue(getAttr(n, "ClipText"))) el.classList.add("clip");
            if (ln === "ConfirmButton") el.classList.add("confirm");
            break;
        case "CheckBox":
            el = h("div", "leaf checkbox");
            el.appendChild(h("span", "box" + (isTrue(getAttr(n, "Pressed")) ? " on" : ""), isTrue(getAttr(n, "Pressed")) ? "✓" : ""));
            el.appendChild(h("span", "", text()));
            break;
        case "OptionButton":
            el = h("div", "leaf button option");
            el.appendChild(h("span", "", "Вариант"));
            el.appendChild(h("span", "arrow", "▼"));
            break;
        case "LineEdit": {
            el = h("div", "leaf lineedit");
            const t = text();
            el.appendChild(h("span", t ? "" : "ph", t || resolveText(getAttr(n, "PlaceHolder"))));
            break;
        }
        case "TextEdit":
            el = h("div", "leaf lineedit textedit");
            el.appendChild(h("span", "ph", resolveText(getAttr(n, "Placeholder"))));
            break;
        case "SpinBox":
        case "FloatSpinBox":
            el = h("div", "leaf spinbox");
            el.append(h("span", "sb-btn", "-"), h("span", "sb-val", getAttr(n, "Value") || "0"), h("span", "sb-btn", "+"));
            break;
        case "Slider": {
            el = h("div", "leaf slider");
            const f = frac(n);
            const tr = h("div", "track"), fill = h("div", "fill"), g = h("div", "grab");
            fill.style.width = f * 100 + "%"; g.style.left = `calc(${f * 100}% - 4px)`;
            tr.append(fill, g); el.appendChild(tr);
            break;
        }
        case "ProgressBar": {
            el = h("div", "leaf progress");
            const fill = h("div", "fill");
            fill.style.width = frac(n) * 100 + "%";
            el.appendChild(fill);
            break;
        }
        case "ItemList":
            el = h("div", "leaf itemlist");
            for (let i = 1; i <= 3; i++) el.appendChild(h("div", "item", `Элемент ${i}`));
            break;
        case "TextureRect":
        case "TextureButton": {
            el = h("div", "leaf texture" + (ln === "TextureButton" ? " texbtn" : ""));
            const p = getAttr(n, "TexturePath");
            if (p && serverMode) {
                const img = document.createElement("img");
                img.src = "/tex" + (p.startsWith("/") ? p : "/" + p);
                img.draggable = false;
                const st = getAttr(n, "Stretch") || "KeepAspectCentered";
                img.style.objectFit = { Scale: "fill", Tile: "none", Keep: "none", KeepCentered: "none", KeepAspect: "contain", KeepAspectCentered: "contain", KeepAspectCovered: "cover" }[st] || "contain";
                if (st === "Keep" || st === "KeepAspect") img.style.objectPosition = "left top";
                img.onerror = () => { img.replaceWith(h("span", "tex-missing", "нет текстуры")); };
                el.appendChild(img);
            } else el.appendChild(h("span", "tex-missing", p ? p.split("/").pop() : "Texture"));
            break;
        }
        case "SpriteView":
        case "EntityPrototypeView":
            el = h("div", "leaf sprite");
            el.appendChild(h("span", "", "Sprite"));
            break;
        case "HLine": {
            el = h("div", "leaf hline");
            el.style.background = cssColor(getAttr(n, "Color")) || THEME.accentGold;
            el.style.minHeight = (num(getAttr(n, "Thickness")) || 2) + "px";
            break;
        }
        case "HSeparator": el = h("div", "leaf hsep"); break;
        case "VSeparator": el = h("div", "leaf vsep"); break;
        case "CollapsibleHeading":
            el = h("div", "leaf label coll-head");
            el.appendChild(h("span", "", "▼ " + resolveText(getAttr(n, "Title"))));
            break;
        default:
            el = h("div", "leaf unknown");
            el.appendChild(h("span", "", n.tag));
    }
    for (const c of (getAttr(n, "StyleClasses") || "").split(/[\s,]+/)) if (c) el.classList.add("sc-" + c);
    return el;
}
function frac(n) {
    const mn = num(getAttr(n, "MinValue")) ?? 0, mx = num(getAttr(n, "MaxValue")) ?? 100, v = num(getAttr(n, "Value")) ?? mn;
    return mx > mn ? Math.min(1, Math.max(0, (v - mn) / (mx - mn))) : 0;
}

function markSelection() {
    document.querySelectorAll(".ctl.sel").forEach(e => e.classList.remove("sel"));
    const el = renderMap.get(selectedId);
    if (el) el.classList.add("sel");
    updateStatus();
}

function updateStatus() {
    const bar = $("#statusBar");
    bar.innerHTML = "";
    const n = byId.get(selectedId);
    const crumbs = h("span", "crumbs");
    if (n) {
        const chain = [];
        for (let p = n; p; p = p.parent) chain.unshift(p);
        chain.forEach((p, i) => {
            if (i) crumbs.appendChild(h("span", "sep", "›"));
            const c = h("a", "", localName(p.tag) + (getAttr(p, "Name") ? `#${getAttr(p, "Name")}` : ""));
            c.addEventListener("click", () => select(p.id));
            crumbs.appendChild(c);
        });
    }
    bar.appendChild(crumbs);
    const size = h("span", "size");
    size.id = "statusSize";
    size.textContent = rootSize.w ? `${rootSize.w}×${rootSize.h}` : "";
    bar.appendChild(size);
}

// ===================================================================
//  Дерево иерархии
// ===================================================================
const collapsed = new Set();

function renderTree() {
    const tree = $("#tree");
    tree.innerHTML = "";
    if (doc.root) tree.appendChild(treeItem(doc.root, 0));
}
function treeItem(n, depth) {
    const wrap = h("div", "titem");
    const row = h("div", "trow" + (n.id === selectedId ? " sel" : "") + (isPropElem(n) ? " propel" : ""));
    row.style.paddingLeft = 6 + depth * 14 + "px";
    row.dataset.id = n.id;
    const kids = n.children.filter(c => c.kind === "el");
    const tog = h("span", "tog", kids.length ? (collapsed.has(n.id) ? "▸" : "▾") : "");
    tog.addEventListener("click", ev => {
        ev.stopPropagation();
        collapsed.has(n.id) ? collapsed.delete(n.id) : collapsed.add(n.id);
        renderTree();
    });
    row.appendChild(tog);
    row.appendChild(h("span", "tname", isPropElem(n) ? "." + n.tag.split(".").pop() : localName(n.tag)));
    const name = getAttr(n, "Name");
    if (name) row.appendChild(h("span", "tid", name));
    const t = getAttr(n, "Text") || getAttr(n, "Title");
    if (t) row.appendChild(h("span", "ttext", resolveText(t)));
    if (isFalse(getAttr(n, "Visible"))) row.appendChild(h("span", "thidden", "скрыт"));
    row.addEventListener("click", () => select(n.id));
    row.addEventListener("contextmenu", ev => { ev.preventDefault(); select(n.id); showContextMenu(ev.clientX, ev.clientY); });
    if (n !== doc.root && !isPropElem(n)) {
        row.draggable = true;
        row.addEventListener("dragstart", ev => {
            ev.dataTransfer.setData("text/x-ui-move", String(n.id));
            ev.dataTransfer.effectAllowed = "move";
            drag = { move: n.id };
        });
    }
    row.addEventListener("dragover", ev => treeDragOver(ev, n, row));
    row.addEventListener("dragleave", () => row.classList.remove("drop-before", "drop-after", "drop-inside"));
    row.addEventListener("drop", ev => treeDrop(ev, n, row));
    wrap.appendChild(row);
    if (kids.length && !collapsed.has(n.id)) for (const c of kids) wrap.appendChild(treeItem(c, depth + 1));
    return wrap;
}

function treeDropPos(ev, n, row) {
    const r = row.getBoundingClientRect();
    const y = (ev.clientY - r.top) / r.height;
    if (isPropElem(n)) return null;
    if (n === doc.root) return canAccept(n) ? "inside" : null;
    if (canAccept(n) && y > 0.25 && y < 0.75) return "inside";
    return y < 0.5 ? "before" : "after";
}
function canAccept(target) {
    const movingId = drag && drag.move;
    const moving = movingId ? byId.get(movingId) : null;
    if (moving && (moving === target || isAncestor(moving, target))) return false;
    if (moving && moving.parent === target && childMode(target) === "single") return true;
    return canHoldChild(target);
}
function treeDragOver(ev, n, row) {
    if (!drag) return;
    const pos = treeDropPos(ev, n, row);
    row.classList.remove("drop-before", "drop-after", "drop-inside");
    if (!pos) return;
    const moving = drag.move ? byId.get(drag.move) : null;
    if (moving && (pos !== "inside") && (moving === n || isAncestor(moving, n))) return;
    if (pos !== "inside" && !canHoldChildOrSame(n.parent, moving)) return;
    ev.preventDefault();
    row.classList.add("drop-" + pos);
}
function canHoldChildOrSame(parent, moving) {
    if (!parent) return false;
    if (moving && moving.parent === parent) return true;
    return canHoldChild(parent);
}
function treeDrop(ev, n, row) {
    ev.preventDefault();
    row.classList.remove("drop-before", "drop-after", "drop-inside");
    const pos = treeDropPos(ev, n, row);
    if (!pos || !drag) return;
    if (pos === "inside") performDrop(n, elChildren(n).length);
    else {
        const idx = elChildren(n.parent).indexOf(n);
        performDrop(n.parent, pos === "before" ? idx : idx + 1);
    }
}

// ===================================================================
//  Перетаскивание (палитра и холст)
// ===================================================================
let drag = null; // { add: tag } | { move: id }

function performDrop(parent, index) {
    if (!drag || !parent) return;
    let node;
    if (drag.add) {
        node = newControl(drag.add);
        ensureNamespace(drag.add);
    } else {
        node = byId.get(drag.move);
        if (!node || node === parent || isAncestor(node, parent)) return;
        if (node.parent === parent) {
            const cur = elChildren(parent).indexOf(node);
            if (cur < index) index--;
        }
        removeNode(node);
    }
    insertNode(parent, node, index);
    drag = null;
    select(node.id, false);
    commit();
}

const dropInd = () => $("#dropIndicator");

function canvasDropTarget(ev) {
    let el = ev.target.closest && ev.target.closest(".ctl");
    if (!el) return null;
    let n = byId.get(+el.dataset.id);
    if (!n) return null;
    const moving = drag && drag.move ? byId.get(drag.move) : null;
    // нельзя бросить в себя или потомка — поднимаемся вверх
    while (n && moving && (n === moving || isAncestor(moving, n))) {
        n = moving.parent;
        el = renderMap.get(n.id);
    }
    if (!n) return null;
    const r = el.getBoundingClientRect();
    const edge = 6 * (+$("#zoom").value);
    const nearEdge = ev.clientX - r.left < edge || r.right - ev.clientX < edge || ev.clientY - r.top < edge || r.bottom - ev.clientY < edge;
    // пустой Control обычно служит распоркой — бросаем рядом с ним (Shift — внутрь)
    const spacer = localName(n.tag) === "Control" && !elChildren(n).length && !ev.shiftKey && n.parent;
    const accept = !spacer && (canAccept(n) || (moving && moving.parent === n && childMode(n) === "single"));
    if (accept && (!nearEdge || !n.parent)) {
        return { parent: n, index: insertionIndex(n, ev), el };
    }
    // вставка рядом (до/после) в родителя
    if (!n.parent || !(canHoldChild(n.parent) || (moving && moving.parent === n.parent))) return null;
    const lay = containerLayout(n.parent);
    const horiz = lay === "box-h" || lay === "grid";
    const after = horiz ? ev.clientX > r.left + r.width / 2 : ev.clientY > r.top + r.height / 2;
    const idx = elChildren(n.parent).indexOf(n) + (after ? 1 : 0);
    return { parent: n.parent, index: idx, el: renderMap.get(n.parent.id) };
}

function insertionIndex(parent, ev) {
    const kids = elChildren(parent);
    const lay = containerLayout(parent);
    let i = 0;
    for (; i < kids.length; i++) {
        const el = renderMap.get(kids[i].id);
        if (!el || el.classList.contains("tab-hidden")) continue;
        const r = el.getBoundingClientRect();
        if (lay === "box-h") { if (ev.clientX < r.left + r.width / 2) break; }
        else if (lay === "grid") {
            if (ev.clientY < r.top) break;
            if (ev.clientY <= r.bottom && ev.clientX < r.left + r.width / 2) break;
        } else if (ev.clientY < r.top + r.height / 2) break;
    }
    return i;
}

function showDropIndicator(t) {
    const ind = dropInd();
    if (!t) { ind.hidden = true; document.querySelectorAll(".drop-target").forEach(e => e.classList.remove("drop-target")); return; }
    document.querySelectorAll(".drop-target").forEach(e => e.classList.remove("drop-target"));
    if (t.el) t.el.classList.add("drop-target");
    const kids = elChildren(t.parent);
    const lay = containerLayout(t.parent);
    const host = $("#canvasScroll").getBoundingClientRect();
    const sc = $("#canvasScroll");
    const pr = t.el ? t.el.getBoundingClientRect() : null;
    let x, y, w, hh;
    const horiz = lay === "box-h" || lay === "grid";
    const ref = kids[t.index] ? renderMap.get(kids[t.index].id) : null;
    const prev = kids[t.index - 1] ? renderMap.get(kids[t.index - 1].id) : null;
    if (ref || prev) {
        const r = (ref || prev).getBoundingClientRect();
        if (horiz) { x = ref ? r.left - 2 : r.right + 1; y = r.top; w = 3; hh = r.height; }
        else { x = r.left; y = ref ? r.top - 2 : r.bottom + 1; w = r.width; hh = 3; }
    } else if (pr) { x = pr.left + 2; y = pr.top + 2; w = pr.width - 4; hh = 3; }
    else { ind.hidden = true; return; }
    ind.hidden = false;
    ind.style.left = x - host.left + sc.scrollLeft + "px";
    ind.style.top = y - host.top + sc.scrollTop + "px";
    ind.style.width = w + "px";
    ind.style.height = hh + "px";
}

function setupCanvasEvents() {
    const sc = $("#canvasScroll");
    const ind = h("div", "drop-indicator"); ind.id = "dropIndicator"; ind.hidden = true;
    sc.appendChild(ind);

    sc.addEventListener("click", ev => {
        const el = ev.target.closest(".ctl");
        if (el) select(+el.dataset.id);
    });
    sc.addEventListener("dblclick", ev => {
        const el = ev.target.closest(".ctl");
        if (!el) return;
        select(+el.dataset.id);
        const inp = document.querySelector('#props [data-prop="Text"], #props [data-prop="Title"]');
        if (inp) { inp.focus(); inp.select && inp.select(); }
    });
    sc.addEventListener("contextmenu", ev => {
        const el = ev.target.closest(".ctl");
        if (!el) return;
        ev.preventDefault();
        select(+el.dataset.id);
        showContextMenu(ev.clientX, ev.clientY);
    });
    sc.addEventListener("mouseover", ev => {
        document.querySelectorAll(".ctl.hover").forEach(e => e.classList.remove("hover"));
        const el = ev.target.closest(".ctl");
        if (el) el.classList.add("hover");
    });
    sc.addEventListener("mouseleave", () => document.querySelectorAll(".ctl.hover").forEach(e => e.classList.remove("hover")));

    sc.addEventListener("dragstart", ev => {
        const el = ev.target.closest && ev.target.closest(".ctl");
        if (!el) return;
        const n = byId.get(+el.dataset.id);
        if (!n || n === doc.root) { ev.preventDefault(); return; }
        drag = { move: n.id };
        ev.dataTransfer.setData("text/x-ui-move", String(n.id));
        ev.dataTransfer.effectAllowed = "move";
    });
    sc.addEventListener("dragover", ev => {
        if (!drag) return;
        const t = canvasDropTarget(ev);
        showDropIndicator(t);
        if (t) ev.preventDefault();
    });
    sc.addEventListener("dragleave", ev => { if (!sc.contains(ev.relatedTarget)) showDropIndicator(null); });
    sc.addEventListener("drop", ev => {
        ev.preventDefault();
        const t = canvasDropTarget(ev);
        showDropIndicator(null);
        if (t) performDrop(t.parent, t.index);
    });
    document.addEventListener("dragend", () => { drag = null; showDropIndicator(null); });
}

// Делаем элементы холста перетаскиваемыми (кроме корня)
function enableCanvasDrag() {
    renderMap.forEach((el, id) => { if (id !== doc.root.id) el.draggable = true; });
}

// ===================================================================
//  Палитра
// ===================================================================
function renderPalette() {
    const pal = $("#palette");
    pal.innerHTML = "";
    const f = $("#paletteFilter").value.trim().toLowerCase();
    const cats = {};
    for (const c of CONTROLS) {
        if (f && !c.tag.toLowerCase().includes(f)) continue;
        (cats[c.cat] = cats[c.cat] || []).push(c);
    }
    for (const [cat, list] of Object.entries(cats)) {
        pal.appendChild(h("div", "pcat", cat));
        const grid = h("div", "pgrid");
        for (const c of list) {
            const item = h("div", "pitem", localName(c.tag));
            item.title = c.tag + " — перетащите на холст или дважды щёлкните, чтобы добавить в выделенный контейнер";
            item.draggable = true;
            item.addEventListener("dragstart", ev => {
                drag = { add: c.tag };
                ev.dataTransfer.setData("text/x-ui-add", c.tag);
                ev.dataTransfer.effectAllowed = "copy";
            });
            item.addEventListener("dblclick", () => addToSelection(c.tag));
            grid.appendChild(item);
        }
        pal.appendChild(grid);
    }
}
function addToSelection(tag) {
    let n = byId.get(selectedId) || doc.root;
    let parent, index;
    if (canHoldChild(n)) { parent = n; index = elChildren(n).length; }
    else if (n.parent && canHoldChild(n.parent)) { parent = n.parent; index = elChildren(n.parent).indexOf(n) + 1; }
    else { toast("Некуда добавить: выделите контейнер"); return; }
    drag = { add: tag };
    performDrop(parent, index);
}

// ===================================================================
//  Панель свойств
// ===================================================================
function renderProps() {
    const box = $("#props");
    box.innerHTML = "";
    const n = byId.get(selectedId);
    $("#propType").textContent = n ? n.tag : "";
    if (!n) { box.appendChild(h("div", "dim pad", "Ничего не выделено")); return; }
    if (n.kind !== "el") return;

    // Тип элемента
    const typeRow = h("div", "prow");
    typeRow.appendChild(h("label", "", "Тип"));
    const typeInp = h("input");
    typeInp.value = n.tag;
    typeInp.setAttribute("list", "typeList");
    typeInp.addEventListener("change", () => {
        const v = typeInp.value.trim();
        if (v && v !== n.tag) { n.tag = v; ensureNamespace(v); commit(); }
    });
    typeRow.appendChild(typeInp);
    box.appendChild(typeRow);
    if (!document.getElementById("typeList")) {
        const dl = h("datalist"); dl.id = "typeList";
        for (const c of CONTROLS) { const o = h("option"); o.value = c.tag; dl.appendChild(o); }
        document.body.appendChild(dl);
    }

    if (isPropElem(n)) {
        box.appendChild(h("div", "dim pad", "Элемент-свойство. Редактируйте вложенные элементы или XAML."));
        renderOtherAttrs(box, n, new Set());
        return;
    }

    const s = schemaOf(n);
    const shown = new Set();
    const groups = {};
    const specific = (s && s.props) || [];
    for (const p of specific) (groups[localName(n.tag)] = groups[localName(n.tag)] || []).push(p);
    if (n.parent && localName(n.parent.tag) === "TabContainer")
        groups["Вкладка"] = [{ name: "TabContainer.TabTitle", type: "loc", hint: "заголовок вкладки" }, { name: "TabContainer.TabVisible", type: "bool" }];
    for (const p of COMMON_PROPS) (groups[p.group] = groups[p.group] || []).push(p);

    for (const [g, props] of Object.entries(groups)) {
        const det = h("details", "pgroup");
        det.open = openGroups.has(g) || !closedGroups.has(g);
        det.addEventListener("toggle", () => { det.open ? (closedGroups.delete(g), openGroups.add(g)) : (closedGroups.add(g), openGroups.delete(g)); });
        det.appendChild(h("summary", "", g));
        for (const p of props) {
            if (shown.has(p.name)) continue;
            shown.add(p.name);
            det.appendChild(propRow(n, p));
        }
        box.appendChild(det);
    }

    if (s && s.panelOverride) box.appendChild(panelOverrideEditor(n));
    renderOtherAttrs(box, n, shown);

    if (n === doc.root) {
        const det = h("details", "pgroup");
        det.appendChild(h("summary", "", "Пространства имён (xmlns)"));
        for (const [k, v] of n.attrs.filter(a => a[0].startsWith("xmlns"))) det.appendChild(h("div", "nsrow", `${k} = ${v}`));
        box.appendChild(det);
    }
}
const openGroups = new Set();
const closedGroups = new Set(["Размер"]);

function propRow(n, p, target) {
    target = target || n;
    const row = h("div", "prow");
    const cur = getAttr(target, p.name);
    const lab = h("label", cur !== undefined ? "set" : "", p.name);
    if (p.hint) lab.title = p.hint;
    row.appendChild(lab);
    const set = v => { setAttr(target, p.name, v); commit(); };
    let ed;

    switch (p.type) {
        case "bool":
        case "enum": {
            ed = h("select");
            const values = p.type === "bool" ? ENUMS.Bool : p.values;
            for (const v of values) { const o = h("option", "", v || "—"); o.value = v; ed.appendChild(o); }
            if (cur !== undefined && !values.some(v => v.toLowerCase() === cur.toLowerCase())) {
                const o = h("option", "", cur); o.value = cur; ed.appendChild(o);
            }
            ed.value = cur === undefined ? "" : (values.find(v => v.toLowerCase() === cur.toLowerCase()) ?? cur);
            ed.addEventListener("change", () => set(ed.value));
            break;
        }
        case "color": {
            ed = h("div", "pcombo");
            const t = h("input"); t.value = cur || ""; t.placeholder = "#RRGGBB(AA)";
            const c = h("input"); c.type = "color";
            const cc = cssColor(cur);
            c.value = cc && /^#[0-9a-f]{6}/i.test(cc) ? cc.slice(0, 7) : "#ffffff";
            t.addEventListener("change", () => set(t.value.trim()));
            c.addEventListener("change", () => {
                const alpha = cur && /^#[0-9a-f]{8}$/i.test(cur) ? cur.slice(7) : "";
                set(c.value.toUpperCase() + alpha);
            });
            t.dataset.prop = p.name;
            ed.append(c, t);
            break;
        }
        case "loc": {
            ed = h("div", "pcombo col");
            const isLoc = cur !== undefined && /^\{\s*Loc\b/.test(cur);
            const keyOf = v => (v.match(/^\{\s*Loc\s+'?([^'}]+?)'?\s*\}$/) || [])[1] || "";
            const line = h("div", "pcombo");
            const t = h("input");
            t.value = isLoc ? keyOf(cur) : (cur || "");
            t.placeholder = isLoc ? "ключ-локализации" : "текст";
            t.dataset.prop = p.name;
            const chk = h("label", "loc-toggle");
            const cb = h("input"); cb.type = "checkbox"; cb.checked = isLoc;
            chk.append(cb, document.createTextNode("Loc"));
            chk.title = "Использовать {Loc 'ключ'} из Resources/Locale";
            const build = () => {
                const v = t.value;
                if (!v) return "";
                return cb.checked ? `{Loc '${v.trim()}'}` : v;
            };
            t.addEventListener("change", () => set(build()));
            cb.addEventListener("change", () => {
                if (cb.checked && t.value && !/^[a-z0-9\-_.]+$/i.test(t.value))
                    t.value = t.value.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "") || "my-key";
                set(build());
            });
            line.append(t, chk);
            ed.appendChild(line);
            if (isLoc) {
                const key = keyOf(cur);
                const val = locale[key];
                ed.appendChild(h("div", "loc-preview" + (val === undefined ? " missing" : ""), val === undefined ? `нет ключа в ${$("#lang").value || "локали"}` : `→ ${val}`));
            }
            break;
        }
        case "styleclasses": {
            ed = h("div", "pcombo col");
            const list = (cur || "").split(/[\s,]+/).filter(Boolean);
            const chips = h("div", "chips");
            for (const c of list) {
                const chip = h("span", "chip", c);
                const x = h("b", "", "×");
                x.addEventListener("click", () => set(list.filter(z => z !== c).join(" ")));
                chip.appendChild(x);
                chips.appendChild(chip);
            }
            const add = h("input"); add.placeholder = "+ класс"; add.setAttribute("list", "styleList");
            add.addEventListener("change", () => { if (add.value.trim()) set([...list, add.value.trim()].join(" ")); });
            if (!document.getElementById("styleList")) {
                const dl = h("datalist"); dl.id = "styleList";
                for (const c of STYLE_CLASSES) { const o = h("option"); o.value = c; dl.appendChild(o); }
                document.body.appendChild(dl);
            }
            ed.append(chips, add);
            break;
        }
        case "vec2": {
            ed = h("div", "pcombo");
            const v = parseVec2(cur) || ["", ""];
            const a = h("input"), b = h("input");
            a.type = b.type = "number";
            a.value = cur ? v[0] : ""; b.value = cur ? v[1] : "";
            a.placeholder = "X"; b.placeholder = "Y";
            const upd = () => set(a.value === "" && b.value === "" ? "" : `${a.value || 0} ${b.value || 0}`);
            a.addEventListener("change", upd); b.addEventListener("change", upd);
            ed.append(a, b);
            break;
        }
        case "texture": {
            ed = h("div", "pcombo col");
            const t = h("input"); t.value = cur || ""; t.placeholder = p.hint || "";
            t.addEventListener("change", () => set(t.value.trim()));
            ed.appendChild(t);
            break;
        }
        case "int":
        case "float": {
            ed = h("input"); ed.type = "number"; ed.value = cur ?? "";
            if (p.type === "float") ed.step = "any";
            ed.addEventListener("change", () => set(ed.value));
            break;
        }
        default: {
            ed = h("input"); ed.value = cur ?? "";
            if (p.hint) ed.placeholder = p.hint;
            ed.addEventListener("change", () => {
                const v = ed.value.trim();
                if (p.name === "Name" && v && !/^[A-Za-z_][A-Za-z0-9_]*$/.test(v)) { toast("Name должно быть идентификатором C#"); ed.value = cur ?? ""; return; }
                if (p.name === "Name" && v && v !== cur && [...byId.values()].some(o => o !== target && o.kind === "el" && isInDoc(o) && getAttr(o, "Name") === v)) {
                    toast("Такое имя уже есть"); ed.value = cur ?? ""; return;
                }
                set(v);
            });
        }
    }
    if (ed.tagName === "INPUT" || ed.tagName === "SELECT") ed.dataset.prop = p.name;
    row.appendChild(ed);
    const rst = h("button", "rst", "×");
    rst.title = "Сбросить (удалить атрибут)";
    rst.disabled = cur === undefined;
    rst.addEventListener("click", () => set(undefined));
    row.appendChild(rst);
    return row;
}
function isInDoc(n) { for (; n; n = n.parent) if (n === doc.root) return true; return false; }

function panelOverrideEditor(n) {
    const det = h("details", "pgroup");
    det.open = true;
    det.appendChild(h("summary", "", "PanelOverride (StyleBoxFlat)"));
    const po = n.children.find(c => c.kind === "el" && c.tag.endsWith("PanelContainer.PanelOverride"));
    const sb = po && po.children.find(c => c.kind === "el");
    if (!sb) {
        const b = h("button", "wide", "Добавить фон / рамку");
        b.addEventListener("click", () => {
            ensureNamespace("gfx:StyleBoxFlat");
            const prefix = n.tag.includes(":") ? n.tag.split(":")[0] + ":" : "";
            const sbn = mkNode("el", "gfx:StyleBoxFlat", [["BackgroundColor", "#1B1B1E"]]);
            const pon = mkNode("el", `${prefix}${localName(n.tag)}.PanelOverride`, [], [sbn]);
            n.children.unshift(pon); pon.parent = n;
            commit();
        });
        det.appendChild(b);
    } else {
        for (const p of STYLEBOX_PROPS) det.appendChild(propRow(n, p, sb));
        const b = h("button", "wide", "Удалить PanelOverride");
        b.addEventListener("click", () => { removeNode(po); commit(); });
        det.appendChild(b);
    }
    return det;
}

function renderOtherAttrs(box, n, shown) {
    const det = h("details", "pgroup");
    const other = n.attrs.filter(a => !shown.has(a[0]) && !a[0].startsWith("xmlns"));
    det.open = other.length > 0;
    det.appendChild(h("summary", "", "Прочие атрибуты"));
    for (const [k, v] of other) {
        const row = h("div", "prow");
        row.appendChild(h("label", "set", k));
        const inp = h("input"); inp.value = v;
        inp.addEventListener("change", () => { setAttr(n, k, inp.value); commit(); });
        row.appendChild(inp);
        const rst = h("button", "rst", "×");
        rst.addEventListener("click", () => { setAttr(n, k, undefined); commit(); });
        row.appendChild(rst);
        det.appendChild(row);
    }
    const add = h("div", "prow");
    const kk = h("input"); kk.placeholder = "Атрибут";
    const vv = h("input"); vv.placeholder = "Значение";
    const ok = h("button", "rst", "+");
    ok.title = "Добавить атрибут";
    const doAdd = () => {
        const k = kk.value.trim();
        if (!/^[A-Za-z_][\w.:]*$/.test(k)) { toast("Некорректное имя атрибута"); return; }
        setAttr(n, k, vv.value); commit();
    };
    ok.addEventListener("click", doAdd);
    vv.addEventListener("keydown", e => { if (e.key === "Enter") doAdd(); });
    add.append(kk, vv, ok);
    det.appendChild(add);
    box.appendChild(det);
}

// ===================================================================
//  Генерация C# code-behind
// ===================================================================
function updateCsMeta() {
    const p = doc.path || "";
    const parts = p.replace(/\.xaml$/, "").split("/");
    const cls = parts.length > 1 ? parts.pop() : "MyWindow";
    const ns = parts.length ? parts.join(".").replace(/[^A-Za-z0-9_.]/g, "") : "Content.Client._Horizon.UI";
    $("#csNamespace").value = ns;
    $("#csClass").value = cls;
}
const EVENTS = {
    Button: ["OnPressed"], ConfirmButton: ["OnPressed"], TextureButton: ["OnPressed"], ContainerButton: ["OnPressed"],
    CheckBox: ["OnToggled"], LineEdit: ["OnTextEntered"], OptionButton: ["OnItemSelected"],
    Slider: ["OnValueChanged"], SpinBox: ["ValueChanged"], FloatSpinBox: ["OnValueChanged"], ItemList: ["OnItemSelected"],
    TextEdit: ["OnTextChanged"],
};
function generateCs() {
    if (!doc.root) return "";
    const ns = $("#csNamespace").value.trim() || "Content.Client";
    const cls = $("#csClass").value.trim() || "MyWindow";
    const s = schemaOf(doc.root);
    const base = s && s.base ? s.base : localName(doc.root.tag);
    const usings = new Set(["Robust.Client.AutoGenerated", "Robust.Client.UserInterface.XAML", "Robust.Client.UserInterface.Controls"]);
    if (s && s.using) usings.add(s.using);
    const named = [];
    const walk = n => {
        if (n.kind !== "el") return;
        const nm = getAttr(n, "Name");
        if (nm && n !== doc.root) named.push({ name: nm, type: localName(n.tag) });
        n.children.forEach(walk);
    };
    walk(doc.root);
    const evs = [];
    for (const x of named) for (const e of EVENTS[x.type] || []) evs.push({ ...x, ev: e });
    if (named.some(x => x.type === "ConfirmButton" || x.type === "StripeBack")) usings.add("Content.Client.UserInterface.Controls");
    if (evs.length) usings.add("System");

    let o = "";
    for (const u of [...usings].sort()) o += `using ${u};\n`;
    o += `\nnamespace ${ns};\n\n`;
    o += `[GenerateTypedNameReferences]\npublic sealed partial class ${cls} : ${base}\n{\n`;
    if (evs.length) {
        o += `    // События интерфейса — логику подключим позже (например, в BoundUserInterface).\n`;
        for (const e of evs) o += `    public event Action? ${e.name}${e.ev.replace(/^On/, "")};\n`;
        o += "\n";
    }
    o += `    public ${cls}()\n    {\n        RobustXamlLoader.Load(this);\n`;
    if (evs.length) {
        o += "\n";
        for (const e of evs) o += `        ${e.name}.${e.ev} += _ => ${e.name}${e.ev.replace(/^On/, "")}?.Invoke();\n`;
    }
    o += `    }\n`;
    if (named.length) {
        o += `\n    // Именованные элементы (доступны как поля благодаря GenerateTypedNameReferences):\n`;
        for (const x of named) o += `    //   ${x.type} ${x.name}\n`;
    }
    o += `}\n`;
    return o;
}

// ===================================================================
//  Команды
// ===================================================================
let clipboard = null;

function select(id, re = true) {
    selectedId = id;
    if (!re) return;
    renderTree();
    markSelection();
    renderProps();
    const row = document.querySelector(`.trow[data-id="${id}"]`);
    if (row) row.scrollIntoView({ block: "nearest" });
}
function refresh() {
    renderCanvas();
    renderTree();
    renderProps();
    const v = currentView();
    if (v === "xaml") $("#xamlSrc").value = serializeDoc();
    if (v === "cs") $("#csSrc").value = generateCs();
    $("#btnUndo").disabled = history.pos <= 0;
    $("#btnRedo").disabled = history.pos >= history.stack.length - 1;
}
const sel = () => byId.get(selectedId);

function cmdDelete() {
    const n = sel();
    if (!n || n === doc.root) return;
    const p = n.parent;
    const sibs = p.children.filter(c => c.kind === "el");
    const i = sibs.indexOf(n);
    removeNode(n);
    const rest = p.children.filter(c => c.kind === "el");
    selectedId = (rest[i] || rest[i - 1] || p).id;
    commit();
}
function cmdDuplicate() {
    const n = sel();
    if (!n || n === doc.root) return;
    const c = cloneNode(n);
    stripNames(c);
    const p = n.parent;
    p.children.splice(p.children.indexOf(n) + 1, 0, c);
    c.parent = p;
    selectedId = c.id;
    commit();
}
function stripNames(n) {
    // дубликаты имён ломают генерацию — делаем уникальными
    const nm = getAttr(n, "Name");
    if (nm) {
        let k = 2;
        const exists = v => [...byId.values()].some(o => o.kind === "el" && isInDoc(o) && getAttr(o, "Name") === v);
        while (exists(nm + k)) k++;
        setAttr(n, "Name", nm + k);
    }
    n.children.forEach(stripNames);
}
function cmdCopy() { const n = sel(); if (n && n !== doc.root) { clipboard = serialize(n); toast("Скопировано"); } }
function cmdCut() { cmdCopy(); cmdDelete(); }
function cmdPaste() {
    if (!clipboard) return;
    const ns = doc.root.attrs.filter(a => a[0].startsWith("xmlns")).map(([k, v]) => `${k}="${escAttr(v)}"`);
    for (const [p, uri] of Object.entries(NAMESPACES)) if (p && !ns.some(x => x.startsWith(`xmlns:${p}=`))) ns.push(`xmlns:${p}="${uri}"`);
    let frag;
    try { frag = parseXaml(`<Wrap ${ns.join(" ")}>${clipboard}</Wrap>`).children.find(c => c.kind === "el"); }
    catch (e) { toast("Ошибка вставки: " + e.message); return; }
    frag.parent = null;
    stripNames(frag);
    ensureNamespacesDeep(frag);
    const n = sel() || doc.root;
    if (canHoldChild(n)) insertNode(n, frag);
    else if (n.parent && canHoldChild(n.parent)) insertNode(n.parent, frag, elChildren(n.parent).indexOf(n) + 1);
    else { toast("Некуда вставить"); return; }
    selectedId = frag.id;
    commit();
}
function cmdMove(dir) {
    const n = sel();
    if (!n || n === doc.root) return;
    const arr = n.parent.children;
    const sibs = arr.filter(c => c.kind === "el");
    const i = sibs.indexOf(n);
    const j = i + dir;
    if (j < 0 || j >= sibs.length) return;
    const other = sibs[j];
    arr.splice(arr.indexOf(n), 1);
    const oi = arr.indexOf(other);
    arr.splice(dir > 0 ? oi + 1 : oi, 0, n);
    commit();
}
function cmdWrap(tag, attrs = []) {
    const n = sel();
    if (!n || n === doc.root || isPropElem(n)) return;
    ensureNamespace(tag);
    const w = mkNode("el", tag, attrs.map(a => [...a]));
    const p = n.parent;
    p.children.splice(p.children.indexOf(n), 1, w);
    w.parent = p;
    w.children.push(n); n.parent = w;
    selectedId = w.id;
    commit();
}
function cmdUnwrap() {
    const n = sel();
    if (!n || n === doc.root) return;
    const kids = n.children.filter(c => !isPropElem(c));
    const p = n.parent;
    if (childMode(p) === "single" && elChildren(n).length > 1) { toast("Родитель принимает только один элемент"); return; }
    const i = p.children.indexOf(n);
    p.children.splice(i, 1, ...kids);
    kids.forEach(k => (k.parent = p));
    n.parent = null;
    selectedId = (kids.find(k => k.kind === "el") || p).id;
    commit();
}
function cmdSelectParent() { const n = sel(); if (n && n.parent) select(n.parent.id); }

function showContextMenu(x, y) {
    const m = $("#ctxMenu");
    m.innerHTML = "";
    const n = sel();
    const notRoot = n && n !== doc.root;
    const items = [
        ["Выделить родителя (Esc)", cmdSelectParent, notRoot],
        null,
        ["Копировать (Ctrl+C)", cmdCopy, notRoot],
        ["Вырезать (Ctrl+X)", cmdCut, notRoot],
        ["Вставить (Ctrl+V)", cmdPaste, !!clipboard],
        ["Дублировать (Ctrl+D)", cmdDuplicate, notRoot],
        ["Удалить (Del)", cmdDelete, notRoot],
        null,
        ["Выше (Alt+↑)", () => cmdMove(-1), notRoot],
        ["Ниже (Alt+↓)", () => cmdMove(1), notRoot],
        null,
        ["Обернуть в BoxContainer (вертикальный)", () => cmdWrap("BoxContainer", [["Orientation", "Vertical"]]), notRoot],
        ["Обернуть в BoxContainer (горизонтальный)", () => cmdWrap("BoxContainer", [["Orientation", "Horizontal"]]), notRoot],
        ["Обернуть в PanelContainer", () => cmdWrap("PanelContainer"), notRoot],
        ["Обернуть в ScrollContainer", () => cmdWrap("ScrollContainer", [["HScrollEnabled", "False"], ["VerticalExpand", "True"]]), notRoot],
        ["Распаковать (убрать контейнер)", cmdUnwrap, notRoot && childMode(n) !== "none"],
    ];
    for (const it of items) {
        if (!it) { m.appendChild(h("hr")); continue; }
        const b = h("button", "", it[0]);
        b.disabled = !it[2];
        b.addEventListener("click", () => { m.hidden = true; it[1](); });
        m.appendChild(b);
    }
    m.hidden = false;
    const r = m.getBoundingClientRect();
    m.style.left = Math.min(x, innerWidth - r.width - 4) + "px";
    m.style.top = Math.min(y, innerHeight - r.height - 4) + "px";
}

// ===================================================================
//  Файлы: открыть / сохранить
// ===================================================================
async function api(path, opts) {
    const r = await fetch(path, opts);
    const j = await r.json();
    if (!r.ok || j.error) throw new Error(j.error || r.statusText);
    return j;
}
let fileListCache = null;
async function openRepoDialog() {
    if (!serverMode) { $("#fileInput").click(); return; }
    const dlg = $("#openDlg");
    const list = $("#openList");
    list.innerHTML = "<li class='dim'>загрузка…</li>";
    dlg.showModal();
    try {
        fileListCache = fileListCache || (await api("/api/files")).files;
    } catch (e) { list.innerHTML = `<li class='err'>${escText(e.message)}</li>`; return; }
    const draw = () => {
        const f = $("#openFilter").value.toLowerCase();
        list.innerHTML = "";
        const items = fileListCache.filter(p => p.toLowerCase().includes(f)).slice(0, 400);
        for (const p of items) {
            const li = h("li", "", p);
            li.addEventListener("click", async () => {
                dlg.close();
                try {
                    const j = await api("/api/file?path=" + encodeURIComponent(p));
                    loadDocument(j.content, p);
                    toast("Открыт " + p);
                } catch (e) { toast("Ошибка: " + e.message); }
            });
            list.appendChild(li);
        }
        if (!items.length) list.appendChild(h("li", "dim", "ничего не найдено"));
    };
    $("#openFilter").oninput = draw;
    draw();
    $("#openFilter").focus();
}

async function save(asNew) {
    if (!doc.root) return;
    if (!serverMode) { download(); return; }
    if (!doc.path || asNew) {
        $("#savePath").value = doc.path || `Content.Client/_Horizon/${$("#csClass").value || "MyWindow"}/UI/${$("#csClass").value || "MyWindow"}.xaml`;
        $("#saveCs").checked = true;
        const dlg = $("#saveDlg");
        dlg.showModal();
        dlg.onclose = async () => {
            if (dlg.returnValue !== "ok") return;
            const p = $("#savePath").value.trim().replace(/\\/g, "/");
            if (!p.endsWith(".xaml")) { toast("Путь должен оканчиваться на .xaml"); return; }
            doc.path = p;
            updateCsMeta();
            await doSave($("#saveCs").checked);
        };
        return;
    }
    await doSave(false);
}
async function doSave(withCs) {
    try {
        await api("/api/save", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ path: doc.path, content: serializeDoc() }) });
        let msg = "Сохранено: " + doc.path;
        if (withCs) {
            const r = await api("/api/save", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ path: doc.path + ".cs", content: generateCs(), overwrite: false }) });
            msg += r.skipped ? " (.xaml.cs уже существует — не трогал)" : " + .xaml.cs";
        }
        $("#fileName").textContent = doc.path;
        fileListCache = null;
        toast(msg);
    } catch (e) { toast("Ошибка сохранения: " + e.message); }
}
function download() {
    const name = (doc.path ? doc.path.split("/").pop() : ($("#csClass").value || "MyWindow") + ".xaml");
    const dl = (content, fname) => {
        const a = h("a");
        a.href = URL.createObjectURL(new Blob([content], { type: "text/plain" }));
        a.download = fname;
        document.body.appendChild(a); a.click(); a.remove();
        setTimeout(() => URL.revokeObjectURL(a.href), 1000);
    };
    dl(serializeDoc(), name);
    setTimeout(() => dl(generateCs(), name + ".cs"), 300);
}

function saveDraft() {
    try { localStorage.setItem("ss14-ui-editor-draft", JSON.stringify({ xaml: serializeDoc(), path: doc.path })); } catch (e) { /* нет хранилища */ }
}
function loadDraft() {
    try {
        const d = JSON.parse(localStorage.getItem("ss14-ui-editor-draft") || "null");
        if (d && d.xaml) { loadDocument(d.xaml, d.path); return true; }
    } catch (e) { /* ignore */ }
    return false;
}

async function loadLocale() {
    const lang = $("#lang").value;
    locale = {};
    if (lang && serverMode) {
        try { locale = await api("/api/locale?lang=" + encodeURIComponent(lang)); } catch (e) { /* ignore */ }
    }
    refresh();
}

// ===================================================================
//  Вкладки, тосты, горячие клавиши
// ===================================================================
function currentView() { return document.querySelector("#viewTabs .active").dataset.view; }
function setView(v) {
    document.querySelectorAll("#viewTabs button").forEach(b => b.classList.toggle("active", b.dataset.view === v));
    document.querySelectorAll(".view").forEach(e => e.classList.remove("active"));
    $("#" + v + "View").classList.add("active");
    $("#xamlError").textContent = "";
    if (v === "xaml") $("#xamlSrc").value = serializeDoc();
    if (v === "cs") $("#csSrc").value = generateCs();
}
function applyXaml() {
    let newRoot;
    try { newRoot = parseXaml($("#xamlSrc").value); }
    catch (e) { $("#xamlError").textContent = "Ошибка XML: " + e.message; return; }
    const selPath = sel() ? pathOf(sel()) : null;
    doc.root = newRoot;
    reindex();
    const n = selPath ? nodeAtPath(selPath) : null;
    selectedId = n ? n.id : doc.root.id;
    $("#xamlError").textContent = "";
    commit();
    toast("XAML применён");
}
function reindex() {
    byId.clear();
    const walk = n => { byId.set(n.id, n); n.children.forEach(walk); };
    walk(doc.root);
}

let toastTimer;
function toast(msg) {
    const t = $("#toast");
    t.textContent = msg;
    t.classList.add("show");
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => t.classList.remove("show"), 2600);
}

function setupUi() {
    $("#btnNew").onclick = () => {
        const list = $("#templateList");
        list.innerHTML = "";
        for (const t of TEMPLATES) {
            const b = h("button", "tpl", t.name);
            b.addEventListener("click", ev => {
                ev.preventDefault();
                $("#newDlg").close();
                loadDocument(t.xaml, null);
                updateCsMeta();
            });
            list.appendChild(b);
        }
        $("#newDlg").showModal();
    };
    $("#btnOpen").onclick = openRepoDialog;
    $("#btnOpenLocal").onclick = () => $("#fileInput").click();
    $("#fileInput").onchange = async e => {
        const f = e.target.files[0];
        if (!f) return;
        try { loadDocument(await f.text(), null); $("#fileName").textContent = f.name; $("#csClass").value = f.name.replace(/\.xaml$/, ""); }
        catch (err) { toast("Ошибка: " + err.message); }
        e.target.value = "";
    };
    $("#btnSave").onclick = () => save(false);
    $("#btnSaveAs").onclick = () => save(true);
    $("#btnDownload").onclick = download;
    $("#btnUndo").onclick = undo;
    $("#btnRedo").onclick = redo;
    $("#zoom").onchange = renderCanvas;
    $("#showBounds").onchange = renderCanvas;
    $("#lang").onchange = loadLocale;
    $("#paletteFilter").oninput = renderPalette;
    $("#btnApplyXaml").onclick = applyXaml;
    $("#csNamespace").oninput = $("#csClass").oninput = () => ($("#csSrc").value = generateCs());
    $("#btnCopyCs").onclick = () => { navigator.clipboard.writeText($("#csSrc").value).then(() => toast("Скопировано")); };
    document.querySelectorAll("#viewTabs button").forEach(b => (b.onclick = () => setView(b.dataset.view)));
    document.addEventListener("click", e => { if (!e.target.closest("#ctxMenu")) $("#ctxMenu").hidden = true; });

    // Перетаскивание .xaml файла на окно
    document.addEventListener("dragover", e => { if (e.dataTransfer.types.includes("Files")) e.preventDefault(); });
    document.addEventListener("drop", async e => {
        const f = e.dataTransfer.files && e.dataTransfer.files[0];
        if (!f) return;
        e.preventDefault();
        try { loadDocument(await f.text(), null); $("#fileName").textContent = f.name; } catch (err) { toast("Ошибка: " + err.message); }
    });

    $("#xamlSrc").addEventListener("keydown", e => {
        if (e.key === "Enter" && (e.ctrlKey || e.metaKey)) { e.preventDefault(); applyXaml(); }
        if (e.key === "Tab") {
            e.preventDefault();
            const t = e.target, s = t.selectionStart;
            t.setRangeText("    ", s, t.selectionEnd, "end");
        }
    });

    document.addEventListener("keydown", e => {
        const ctrl = e.ctrlKey || e.metaKey;
        const typing = e.target.closest("input, textarea, select");
        if (ctrl && e.key.toLowerCase() === "s") { e.preventDefault(); save(false); return; }
        if (ctrl && e.key.toLowerCase() === "o") { e.preventDefault(); openRepoDialog(); return; }
        if (typing || document.querySelector("dialog[open]")) return;
        if (currentView() !== "design") return;
        const k = e.key.toLowerCase();
        if (ctrl && k === "z" && !e.shiftKey) { e.preventDefault(); undo(); }
        else if (ctrl && (k === "y" || (k === "z" && e.shiftKey))) { e.preventDefault(); redo(); }
        else if (ctrl && k === "c") cmdCopy();
        else if (ctrl && k === "x") cmdCut();
        else if (ctrl && k === "v") cmdPaste();
        else if (ctrl && k === "d") { e.preventDefault(); cmdDuplicate(); }
        else if (e.key === "Delete" || e.key === "Backspace") { e.preventDefault(); cmdDelete(); }
        else if (e.key === "Escape") cmdSelectParent();
        else if (e.altKey && e.key === "ArrowUp") { e.preventDefault(); cmdMove(-1); }
        else if (e.altKey && e.key === "ArrowDown") { e.preventDefault(); cmdMove(1); }
        else if (e.key === "ArrowUp" || e.key === "ArrowDown") {
            e.preventDefault();
            const rows = [...document.querySelectorAll(".trow")];
            const i = rows.findIndex(r => +r.dataset.id === selectedId);
            const nr = rows[i + (e.key === "ArrowUp" ? -1 : 1)];
            if (nr) select(+nr.dataset.id);
        }
    });
}

// ===================================================================
//  Старт
// ===================================================================
(async function init() {
    setupUi();
    setupCanvasEvents();
    renderPalette();
    try {
        const info = await fetch("/api/info").then(r => r.json());
        serverMode = !!info.server;
    } catch (e) { serverMode = false; }
    if (!serverMode) {
        $("#btnSave").title = "Без сервера — скачивание файла";
        $("#btnOpen").textContent = "Открыть файл";
        $("#btnOpenLocal").hidden = true;
        toast("Автономный режим: запустите ui_editor.py, чтобы открывать/сохранять файлы репозитория");
    }
    if (!loadDraft()) loadDocument(TEMPLATES[1].xaml, null);
    if (serverMode) await loadLocale();
})();
