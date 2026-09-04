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

using Content.Server.ModularComputer.Cpu;
using Content.Server.ModularComputer.Devices.Mmio;
using Content.Server.NTVM;
using JetBrains.Annotations;

namespace Content.Server.ModularComputer.Devices;

public abstract class DeviceSystem<TComponent, TState> : EntitySystem
    where TComponent : DeviceComponent<TState>
    where TState : DeviceState, new()
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TComponent, MmioDeviceDetachedEvent>(OnMmioDeviceDetached);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var componentQuery = EntityQueryEnumerator<TComponent>();

        while (componentQuery.MoveNext(out var uid, out var component))
        {
            OnUpdate(frameTime, uid, component);

            var channel = component.State.EventsChannel;

            if (channel.Reader.Count == 0)
                continue;

            while (channel.Reader.TryRead(out var ev))
            {
                OnDeviceEvent(uid, component, ev);
            }
        }
    }

    [PublicAPI]
    protected virtual void OnUpdate(float frameTime, EntityUid uid, TComponent component)
    {
    }

    protected void AttachCallbacks(EntityUid uid, TComponent component, MmioDevice newDevice)
    {
        newDevice.MmioWrite += (machine, device, data, offset) =>
        {
            var state = component.State;

            lock (state)
            {
                return OnMmioWrite(uid, machine, device, state, data, offset);
            }
        };

        newDevice.MmioRead += (machine, device, data, offset) =>
        {
            var state = component.State;

            lock (state)
            {
                return OnMmioRead(uid, machine, device, state, data, offset);
            }
        };
    }

    [PublicAPI]
    protected virtual void OnMmioDeviceDetached(EntityUid uid, TComponent component, ref MmioDeviceDetachedEvent args)
    {
        component.Motherboard = null;
    }

    [PublicAPI]
    protected virtual void OnDeviceEvent(EntityUid uid, TComponent component, DeviceEvent ev)
    {
    }

    [PublicAPI]
    protected virtual bool OnMmioWrite(EntityUid uid, Machine machine, MmioDevice device, TState state, BinaryRw data,
        int offset)
    {
        return true;
    }

    [PublicAPI]
    protected virtual bool OnMmioRead(EntityUid uid, Machine machine, MmioDevice device, TState state, BinaryRw data,
        int offset)
    {
        return true;
    }

    [PublicAPI]
    protected void UpdateState(EntityUid uid, TComponent component, Action<TState> func)
    {
        lock (component.State)
        {
            func(component.State);
        }
    }

    [PublicAPI]
    protected TReturn UpdateState<TReturn>(EntityUid uid, TComponent component, Func<TState, TReturn> func)
    {
        lock (component.State)
        {
            return func(component.State);
        }
    }
}
