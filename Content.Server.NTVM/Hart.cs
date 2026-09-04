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

using JetBrains.Annotations;
using NativeMethods;

namespace Content.Server.NTVM;

public sealed class Hart
{
    private readonly unsafe rvvm_hart_t* _ptr;

    internal unsafe Hart(rvvm_hart_t* ptr)
    {
        _ptr = ptr;
    }

    [PublicAPI]
    public ulong ReadRegister(Register register)
    {
        unsafe
        {
            return RVVM.rvvm_read_cpu_reg(_ptr, (nuint)register);
        }
    }

    [PublicAPI]
    public void WriteRegister(Register register, ulong value)
    {
        unsafe
        {
            RVVM.rvvm_write_cpu_reg(_ptr, (nuint)register, value);
        }
    }

    [PublicAPI]
    public void Interrupt(InterruptMask mask)
    {
        unsafe
        {
            RVVM.riscv_interrupt(_ptr, (byte)mask);
        }
    }

    [PublicAPI]
    public void ClearInterrupt(InterruptMask mask)
    {
        unsafe
        {
            RVVM.riscv_interrupt_clear(_ptr, (byte)mask);
        }
    }

    [PublicAPI]
    public uint GetCsrIp()
    {
        unsafe
        {
            return _ptr->csr.ip;
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
    public void SetTimeCmp(ulong timeCmp)
    {
        unsafe
        {
            _ptr->timer.timecmp = timeCmp;
        }
    }

    [PublicAPI]
    internal void SetTimer(rvtimer_t timer)
    {
        unsafe
        {
            _ptr->timer = timer;
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
}

[PublicAPI]
public enum InterruptMask : byte
{
    UserSoftware = 0x0,
    SupervisorSoftware = 0x1,
    MachineSoftware = 0x3,
    UserTimer = 0x4,
    SupervisorTimer = 0x5,
    MachineTimer = 0x7,
    UserExternal = 0x8,
    SupervisorExternal = 0x9,
    MachineExternal = 0xB
}
