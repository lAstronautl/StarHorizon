// Модуль «Мои блоки»: свои подпрограммы. «определить» задаёт тело, «выполнить» вызывает его.
"use strict";
UIBlocks.registerModule({
    id: "myblocks", name: "Мои блоки", color: "#e0457b", scope: "both",
    paletteTop(pal, api) {
        const btn = document.createElement("button");
        btn.className = "blk-pal-btn";
        btn.textContent = "Создать блок";
        btn.onclick = () => {
            const name = prompt("Название блока (например: обновить экран):");
            if (name) api.addProc(name);
        };
        pal.appendChild(btn);
    },
    blocks: [
        { id: "proc_def", shape: "hat", text: "определить {P}",
          args: { P: { type: "proc" } },
          paletteIf: api => api.program.procs.length > 0,
          gen: () => "" },
        { id: "proc_call", shape: "cmd", text: "выполнить {P}",
          args: { P: { type: "proc" } },
          paletteIf: api => api.program.procs.length > 0,
          gen: (ctx, a) => `${ctx.procCall(a.P.raw)};` },
    ],
});
