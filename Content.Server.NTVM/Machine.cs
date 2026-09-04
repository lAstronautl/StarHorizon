//-----------------------------------------------------------------------------
// Copyright 2024 Igor Spichkin
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using JetBrains.Annotations;
using NativeMethods;

namespace Content.Server.NTVM;

[PublicAPI]
public record struct MachineConfig
{
    public const int DefaultMemBase = 0x20000000;
    
    public ulong HartCount;

    /// <summary>
    ///     Max instructions per quant (10 ms).
    ///     Thus, 1 IPQ = 10 IPS.
    ///     Also, this does not work with JIT
    ///     (and this is the reason why it's always off).
    /// </summary>
    public ulong IPQ;

    public ulong MemBase;
    public ulong MemSize;

    public bool RV64;

    public static MachineConfig Default()
    {
        return new MachineConfig
        {
            MemSize = 33554432,
            MemBase = DefaultMemBase,
            HartCount = 1,
            RV64 = true,
            IPQ = 10
        };
    }
}

public sealed class Machine : IDisposable
{
    /// <summary>
    ///     Stores <see cref="Machine" /> by <see cref="rvvm_machine_t" /> pointer.
    /// </summary>
    private static readonly Dictionary<nint, Machine> Machines = new();

    private readonly unsafe rvvm_machine_t* _ptr;
    public MMIOAccessHandler? MMIOAccess;

    /// <summary>
    ///     32 MB by default
    /// </summary>
    [PublicAPI]
    public Machine(MachineConfig cfg)
    {
        unsafe
        {
            _ptr = RVVM.rvvm_create_machine(cfg.MemBase, (nuint)cfg.MemSize, (nuint)cfg.HartCount, cfg.RV64);

            if (_ptr == null)
                throw new InvalidOperationException("Failed to create a machine");

            SetIPQ(cfg.IPQ);
            Machines.Add((nint)_ptr, this);

            RVVM.rvvm_set_mmio_acces_handler(_ptr, &OnMMIOAccessNative);
        }
    }

    public void Dispose()
    {
        MMIOAccess = null;
        ReleaseUnmanagedResources();

        GC.SuppressFinalize(this);
    }

    [PublicAPI]
    public void SetIPQ(ulong ipq)
    {
        unsafe
        {
            RVVM.rvvm_set_opt(_ptr, (uint)MachineOption.IPQ, ipq);
        }
    }

    [PublicAPI]
    public ulong GetRAMBase()
    {
        unsafe
        {
            return RVVM.rvvm_get_opt(_ptr, 0x80000001U);
        }
    }

    [PublicAPI]
    public ulong GetRAMSize()
    {
        unsafe
        {
            return RVVM.rvvm_get_opt(_ptr, 0x80000002U);
        }
    }

    [PublicAPI]
    public ulong GetHartsCount()
    {
        unsafe
        {
            return RVVM.rvvm_get_opt(_ptr, 0x80000003U);
        }
    }

    [PublicAPI]
    public Hart? GetHart(ulong index)
    {
        unsafe
        {
            var ptr = RVVM.rvvm_get_hart(_ptr, (nuint)index);

            return ptr == null ? null : new Hart(ptr);
        }
    }

    [PublicAPI]
    public static void RunEventLoop()
    {
        RVVM.rvvm_run_eventloop();
    }

    [PublicAPI]
    public static ulong MachinesCount()
    {
        return RVVM.rvvm_machines_count();
    }

    [PublicAPI]
    public void LoadBootrom(string? bootromPath)
    {
        unsafe
        {
            if (bootromPath is null)
            {
                RVVM.rvvm_load_bootrom(_ptr, null);
                return;
            }

            var path = bootromPath + char.MinValue;
            var bytes = Encoding.ASCII.GetBytes(path);

            fixed (byte* ptr = bytes)
            {
                if (!RVVM.rvvm_load_bootrom(_ptr, ptr))
                    throw new InvalidOperationException("Can't load a bootrom!");
            }
        }
    }

    [PublicAPI]
    public void Run()
    {
        if (IsRunning())
            return;

        unsafe
        {
            RVVM.rvvm_enable_builtin_eventloop(false);

            if (!RVVM.rvvm_start_machine(_ptr))
                throw new InvalidOperationException("Can't start a machine!");
        }
    }

    [PublicAPI]
    public bool IsRunning()
    {
        unsafe
        {
            return RVVM.rvvm_machine_is_running(_ptr);
        }
    }

    [PublicAPI]
    public PowerState GetPowerState()
    {
        unsafe
        {
            return (PowerState)RVVM.rvvm_machine_power_state(_ptr);
        }
    }

    [PublicAPI]
    public bool IsPowered()
    {
        unsafe
        {
            return RVVM.rvvm_machine_powered(_ptr);
        }
    }

    [PublicAPI]
    public void Reset()
    {
        unsafe
        {
            RVVM.rvvm_reset_machine(_ptr, true);
        }
    }

    [PublicAPI]
    public void Shutdown()
    {
        if (!IsPowered())
            return;

        unsafe
        {
            RVVM.rvvm_reset_machine(_ptr, false);
        }
    }

    [PublicAPI]
    public ulong GetTimer()
    {
        unsafe
        {
            return RVVM.rvtimer_get(&_ptr->timer);
        }
    }

    [PublicAPI]
    public ulong GetTimeCmp()
    {
        unsafe
        {
            return _ptr->timer.timecmp;
        }
    }

    [PublicAPI]
    public void SetTimeCmp(ulong value)
    {
        unsafe
        {
            _ptr->timer.timecmp = value;
        }

        var harts = GetHartsCount();

        for (var i = 0ul; i < harts; i++)
        {
            var hart = GetHart(i);

            unsafe
            {
                hart?.SetTimer(_ptr->timer);
            }
        }
    }

    [PublicAPI]
    public void Interrupt(InterruptMask interrupt)
    {
        var harts = GetHartsCount();

        for (var i = 0ul; i < harts; i++)
        {
            var hart = GetHart(i);

            hart?.Interrupt(interrupt);
        }
    }

    [PublicAPI]
    public void ClearInterrupt(InterruptMask interrupt)
    {
        var harts = GetHartsCount();

        for (var i = 0ul; i < harts; i++)
        {
            var hart = GetHart(i);

            hart?.ClearInterrupt(interrupt);
        }
    }

    [Obsolete("Causes memory corruption, don't call!!!")]
    private void RebaseTimer(ulong time)
    {
        unsafe
        {
            // WTF MEMORY CORRUPTION
            RVVM.rvtimer_rebase(&_ptr->timer, time);
        }

        var harts = GetHartsCount();

        for (var i = 0ul; i < harts; i++)
        {
            var hart = GetHart(i);

            unsafe
            {
                hart?.SetTimer(_ptr->timer);
            }
        }
    }

    [PublicAPI]
    public byte[] ReadRam(ulong address, int size)
    {
        unsafe
        {
            var arr = new byte[size];

            fixed (void* ptr = arr)
            {
                RVVM.rvvm_read_ram(_ptr, ptr, address, (nuint)size);
            }

            return arr;
        }
    }

    [PublicAPI]
    public void WriteRam(byte[] data, ulong address)
    {
        unsafe
        {
            fixed (void* ptr = data)
            {
                RVVM.rvvm_write_ram(_ptr, address, ptr, (UIntPtr)data.Length);
            }
        }
    }

    private void ReleaseUnmanagedResources()
    {
        unsafe
        {
            RVVM.rvvm_free_machine(_ptr);
            Machines.Remove((nint)_ptr);
        }
    }

    ~Machine()
    {
        ReleaseUnmanagedResources();
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static unsafe byte OnMMIOAccessNative(rvvm_machine_t* ptr, uint address, void* dest, byte size, byte access)
    {
        if (!Machines.TryGetValue((nint)ptr, out var machine))
            return 0;

        if (machine.MMIOAccess is not { } func)
            return 0;

        var dataArray = new byte[size];
        Marshal.Copy((nint)dest, dataArray, 0, size);

        var memAccess = (MemoryAccess)access;
        var ret = func.Invoke(machine, address, dataArray, memAccess);

        if (ret && memAccess == MemoryAccess.Read)
            Marshal.Copy(dataArray, 0, (nint)dest, size);

        return (byte)(ret ? 1 : 0);
    }
}

[PublicAPI]
public delegate bool MMIOAccessHandler(Machine machine, uint address, byte[] data, MemoryAccess access);

public enum MachineOption : byte
{
    IPQ = 6
}

public enum MemoryAccess : byte
{
    Read = 0x2,
    Write = 0x4,
    Exec = 0x8
}
