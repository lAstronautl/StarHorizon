/* Привязка окна к предмету: переключатель «Окно | Сервер (предмет)» и диалог настроек предмета. */
"use strict";

const UIItem = (() => {
    const B = UIBlocks;
    let host = null;
    const $ = id => document.getElementById(id);

    function updateButtons(scope) {
        const it = B.root.item;
        $("blkScopeClient").classList.toggle("active", scope === "client");
        $("blkScopeServer").classList.toggle("active", scope === "server");
        $("blkScopeServer").textContent = it ? `Сервер (${it.id})` : "Сервер (предмет)";
        $("itemSettings").textContent = it ? "Предмет…" : "Сделать предметом…";
        $("logicView").classList.toggle("server-scope", scope === "server");
    }

    async function loadRsiList() {
        if ($("rsiList").childElementCount || !host.serverMode()) return;
        try {
            const j = await fetch("/api/rsi").then(r => r.json());
            for (const p of j.rsi) { const o = document.createElement("option"); o.value = p; $("rsiList").appendChild(o); }
        } catch (e) { /* без списка */ }
    }
    async function loadStates(keep) {
        const sel = $("itemState");
        sel.innerHTML = "";
        const p = $("itemSprite").value.trim();
        let states = [];
        if (p.endsWith(".rsi") && host.serverMode()) {
            try { states = (await fetch("/api/rsi?path=" + encodeURIComponent(p)).then(r => r.json())).states || []; } catch (e) { /* ignore */ }
        }
        if (!states.length) states = [keep || "icon"];
        for (const s of states) { const o = document.createElement("option"); o.value = o.textContent = s; sel.appendChild(o); }
        sel.value = states.includes(keep) ? keep : (states.includes("icon") ? "icon" : states[0]);
        preview();
    }
    function preview() {
        const p = $("itemSprite").value.trim(), st = $("itemState").value;
        $("itemPreview").src = p && st ? `/tex/Textures/${p}/${st}.png` : "";
    }

    function openDialog() {
        if (!host.isWindow()) { host.toast("Предметом можно сделать только окно (FancyWindow или DefaultWindow)"); return; }
        if (!host.getPath()) { host.toast("Сначала сохраните окно в репозиторий"); return; }
        const it = B.root.item || {};
        const cls = host.getMeta().cls.replace(/Window$/, "");
        $("itemId").value = it.id || ("Horizon" + cls.replace(/^[a-z]/, c => c.toUpperCase()));
        $("itemName").value = it.name || cls;
        $("itemDesc").value = it.desc || "";
        $("itemParent").value = it.parent || "BaseItem";
        $("itemSprite").value = it.sprite || "Objects/Devices/gps.rsi";
        $("itemUnlink").hidden = !B.root.item;
        $("itemErr").textContent = "";
        loadRsiList();
        loadStates(it.state || "icon");
        const dlg = $("itemDlg");
        dlg.showModal();
        dlg.onclose = () => {
            if (dlg.returnValue === "unlink") {
                if (confirm("Отвязать предмет? Серверные блоки будут удалены (уже созданные файлы останутся в репозитории).")) {
                    B.setItem(null);
                    host.toast("Предмет отвязан");
                }
                return;
            }
            if (dlg.returnValue !== "ok") return;
            const id = $("itemId").value.trim();
            if (!/^[A-Z][A-Za-z0-9]*$/.test(id)) { host.toast("ID: латиница и цифры, с заглавной буквы"); return; }
            const created = !B.root.item;
            B.setItem({
                id,
                name: $("itemName").value.trim() || id,
                desc: $("itemDesc").value.trim(),
                parent: $("itemParent").value.trim() || "BaseItem",
                sprite: $("itemSprite").value.trim() || "Objects/Devices/gps.rsi",
                state: $("itemState").value || "icon",
            });
            if (created) { B.setScope("server"); host.toast("Предмет создан. Соберите серверную логику и нажмите «Сохранить»"); }
        };
    }

    function init(h) {
        host = h;
        B.onScopeChange = updateButtons;
        $("blkScopeClient").onclick = () => B.setScope("client");
        $("blkScopeServer").onclick = () => { if (!B.setScope("server")) openDialog(); };
        $("itemSettings").onclick = openDialog;
        $("itemSprite").addEventListener("change", () => loadStates($("itemState").value));
        $("itemState").addEventListener("change", preview);
        updateButtons(B.scope);
    }
    return { init, openDialog };
})();
