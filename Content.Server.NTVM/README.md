# Content.Server.NTVM

C# wrapper around [RVVM](https://github.com/LekKit/RVVM) (a RISC-V emulator), ported from
[igorsaux/SS14.ModularComputers](https://github.com/igorsaux/SS14.ModularComputers).

## Native library

The original mod links against a **privately patched fork** of RVVM and a bindings file
(`NativeMethods.rvvm.g.cs`) auto-generated from it. Neither is published anywhere, and current
public upstream RVVM has since changed its ABI enough (opaque `rvvm_hart_t`, a different
`rvvm_create_machine` signature, interrupt masks replaced by an IRQ-device model) that it can't
be swapped in as-is.

`NativeMethods.rvvm.g.cs` in this project is a **hand-written placeholder** that only restates
the signatures `Hart.cs`/`Machine.cs` call, so the project compiles. Until a native library
that actually exports this exact ABI is supplied, every call into it throws
`DllNotFoundException` (no `rvvm` library present) or `EntryPointNotFoundException` at runtime -
CPU emulation is inert, though everything else (devices, UI, prototypes) is unaffected.

To make it functional, either:
- Obtain/rebuild the original patched RVVM fork and its generated bindings, or
- Adapt `NativeMethods.rvvm.g.cs` and the direct `rvvm_hart_t`/`rvvm_machine_t` field access in
  `Hart.cs`/`Machine.cs` to current upstream RVVM's `librvvm` API (this also requires rewriting
  the interrupt delivery path to use `rvvm_irq_dev_t`/`rvvm_irq_set` instead of
  `riscv_interrupt`/`riscv_interrupt_clear`).

Once you have a native binary, drop it at `RVVM/artifacts/release/rvvm.dll` /
`RVVM/artifacts/debug/rvvm.dll` (Windows) or `RVVM/artifacts/librvvm.so` (Linux/macOS) at the
solution root - the `.csproj` copies it to the output directory automatically if present.
