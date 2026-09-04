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

using Content.Server.Chat.Systems;
using Content.Server.ModularComputer.Devices.Mmio;
using Content.Server.ModularComputer.Devices.Pci;
using Content.Server.NTVM;
using Content.Shared.Speech;
using JetBrains.Annotations;
using Robust.Shared.Timing;

namespace Content.Server.ModularComputer.Devices.Tts;

public sealed class TtsDeviceSystem : PciDeviceSystem<TtsDeviceComponent, TtsDeviceState>
{
    private const int MaxStringLength = 80;

    [Dependency] private readonly ChatSystem _chat = default!;

    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<TtsDeviceComponent>();

        while (query.MoveNext(out var uid, out var component))
        {
            if (component.NextSpeech == TimeSpan.Zero || _timing.CurTime < component.NextSpeech)
                continue;

            component.NextSpeech = TimeSpan.Zero;
            UpdateState(uid, component, state => state.IsReady = true);
        }
    }

    protected override void OnDeviceEvent(EntityUid uid, TtsDeviceComponent component, DeviceEvent ev)
    {
        if (ev is not SpeechTtsEvent speechEvent)
            return;

        if (_timing.CurTime < component.NextSpeech)
            return;

        component.NextSpeech = _timing.CurTime + CalculateSpeechTime(speechEvent.StringToSpeech.Length);

        UpdateState(uid, component, state => state.IsReady = false);
        _chat.TrySendInGameICMessage(uid, speechEvent.StringToSpeech, InGameICChatType.Speak, false);
    }

    [PublicAPI]
    public static TimeSpan CalculateSpeechTime(int textLength)
    {
        return TimeSpan.FromMilliseconds(textLength * 300);
    }

    protected override bool OnMmioWrite(EntityUid uid, Machine machine, MmioDevice device, TtsDeviceState state,
        BinaryRw data, int offset)
    {
        var register = (DeviceWriteRegister)offset;

        switch (register)
        {
            case DeviceWriteRegister.String:
                if (state.String.Length >= MaxStringLength)
                    break;

                state.String.Append((char)data.ReadByte());

                break;
            case DeviceWriteRegister.Speech:
                state.TryEnqueueEvent(new SpeechTtsEvent(state.String.ToString()));

                break;
            case DeviceWriteRegister.Flush:
                state.String.Clear();

                break;
        }

        return true;
    }

    protected override bool OnMmioRead(EntityUid uid, Machine machine, MmioDevice device, TtsDeviceState state,
        BinaryRw data, int offset)
    {
        var register = (DeviceReadRegister)offset;

        switch (register)
        {
            case DeviceReadRegister.StringLength:
                data.Write(state.String.Length);

                break;
            case DeviceReadRegister.SpeechTime:
                var time = CalculateSpeechTime(state.String.Length);

                data.Write(time.Milliseconds);

                break;
            case DeviceReadRegister.IsReady:
                break;
            default:
                data.Write(state.IsReady ? 0b1 : 0b0);

                break;
        }

        return true;
    }

    protected override void OnComponentStartup(EntityUid uid, TtsDeviceComponent component, ComponentStartup args)
    {
        EnsureComp<SpeechComponent>(uid);
    }

    private enum DeviceWriteRegister : byte
    {
        String = 0x0,
        Speech = 0x4,
        Flush = 0x8
    }

    private enum DeviceReadRegister : byte
    {
        StringLength = 0x0,
        SpeechTime = 0x4,
        IsReady = 0x8
    }
}

public sealed class SpeechTtsEvent : DeviceEvent
{
    public SpeechTtsEvent(string stringToSpeech)
    {
        StringToSpeech = stringToSpeech;
    }

    public string StringToSpeech { get; }
}
