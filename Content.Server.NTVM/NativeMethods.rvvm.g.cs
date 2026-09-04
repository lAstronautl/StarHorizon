//-----------------------------------------------------------------------------
// Hand-written placeholder for the P/Invoke bindings that igorsaux/SS14.ModularComputers
// generates from a privately patched fork of https://github.com/LekKit/RVVM
// (see Content.Server.NTVM/Content.Server.NTVM.csproj, which originally compiled a
// generated ..\RVVM\artifacts\NativeMethods.rvvm.g.cs). That fork and its generated
// bindings aren't published, and current upstream RVVM's public API/ABI (rvvm_hart_t
// is now opaque, rvvm_create_machine's signature changed, the interrupt-mask API was
// replaced by an IRQ-device model) is no longer a drop-in match for the signatures the
// ported game code below expects.
//
// This file only reproduces the signatures Hart.cs/Machine.cs call, so the project
// compiles. Without a native "rvvm" library built from a fork that actually exports
// this exact ABI, every call here throws DllNotFoundException/EntryPointNotFoundException
// at runtime - see Content.Server.NTVM/README.md.
//-----------------------------------------------------------------------------

using System.Runtime.InteropServices;

namespace NativeMethods;

public unsafe struct rvvm_hart_t
{
    public csr_t csr;
    public rvtimer_t timer;
}

public unsafe struct rvvm_machine_t
{
    public rvtimer_t timer;
}

public struct csr_t
{
    public uint ip;
}

public struct rvtimer_t
{
    public ulong timecmp;
}

public static unsafe class RVVM
{
    private const string LibName = "rvvm";

    [DllImport(LibName)]
    public static extern ulong rvvm_read_cpu_reg(rvvm_hart_t* hart, nuint regId);

    [DllImport(LibName)]
    public static extern void rvvm_write_cpu_reg(rvvm_hart_t* hart, nuint regId, ulong value);

    [DllImport(LibName)]
    public static extern void riscv_interrupt(rvvm_hart_t* hart, byte cause);

    [DllImport(LibName)]
    public static extern void riscv_interrupt_clear(rvvm_hart_t* hart, byte cause);

    [DllImport(LibName)]
    public static extern ulong rvtimer_get(rvtimer_t* timer);

    [DllImport(LibName)]
    public static extern void rvtimer_rebase(rvtimer_t* timer, ulong time);

    [DllImport(LibName)]
    public static extern rvvm_machine_t* rvvm_create_machine(ulong memBase, nuint memSize, nuint hartCount, [MarshalAs(UnmanagedType.U1)] bool rv64);

    [DllImport(LibName)]
    public static extern void rvvm_set_mmio_acces_handler(rvvm_machine_t* machine, delegate* unmanaged[Cdecl]<rvvm_machine_t*, uint, void*, byte, byte, byte> handler);

    [DllImport(LibName)]
    public static extern void rvvm_set_opt(rvvm_machine_t* machine, uint opt, ulong value);

    [DllImport(LibName)]
    public static extern ulong rvvm_get_opt(rvvm_machine_t* machine, uint opt);

    [DllImport(LibName)]
    public static extern rvvm_hart_t* rvvm_get_hart(rvvm_machine_t* machine, nuint index);

    [DllImport(LibName)]
    public static extern void rvvm_run_eventloop();

    [DllImport(LibName)]
    public static extern ulong rvvm_machines_count();

    [DllImport(LibName)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool rvvm_load_bootrom(rvvm_machine_t* machine, byte* path);

    [DllImport(LibName)]
    public static extern void rvvm_enable_builtin_eventloop([MarshalAs(UnmanagedType.U1)] bool enabled);

    [DllImport(LibName)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool rvvm_start_machine(rvvm_machine_t* machine);

    [DllImport(LibName)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool rvvm_machine_is_running(rvvm_machine_t* machine);

    [DllImport(LibName)]
    public static extern byte rvvm_machine_power_state(rvvm_machine_t* machine);

    [DllImport(LibName)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool rvvm_machine_powered(rvvm_machine_t* machine);

    [DllImport(LibName)]
    public static extern void rvvm_reset_machine(rvvm_machine_t* machine, [MarshalAs(UnmanagedType.U1)] bool reset);

    [DllImport(LibName)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool rvvm_read_ram(rvvm_machine_t* machine, void* dest, ulong address, nuint size);

    [DllImport(LibName)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool rvvm_write_ram(rvvm_machine_t* machine, ulong address, void* src, nuint size);

    [DllImport(LibName)]
    public static extern void rvvm_free_machine(rvvm_machine_t* machine);
}
