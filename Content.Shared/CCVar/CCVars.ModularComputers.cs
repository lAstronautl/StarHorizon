using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    ///     Whether modular computers (the RISC-V emulated motherboards) are enabled at all.
    /// </summary>
    public static readonly CVarDef<bool> ModularComputersEnabled =
        CVarDef.Create("modular_computers.enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     Hard cap on the number of simultaneously running modular computer machines.
    /// </summary>
    public static readonly CVarDef<int> ModularComputersMaxMachinesHard =
        CVarDef.Create("modular_computers.max_machines_hard", 32, CVar.SERVERONLY);

    /// <summary>
    ///     Total RAM (in bytes) all modular computer machines combined may allocate.
    /// </summary>
    public static readonly CVarDef<int> ModularComputersMaxMemory =
        CVarDef.Create("modular_computers.max_memory", 256 * 1024 * 1024, CVar.SERVERONLY);
}
