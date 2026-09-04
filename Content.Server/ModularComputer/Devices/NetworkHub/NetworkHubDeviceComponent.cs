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

using Content.Server.ModularComputer.Devices.Pci;

namespace Content.Server.ModularComputer.Devices.NetworkHub;

[RegisterComponent]
[Access(typeof(NetworkHubDeviceSystem))]
public sealed partial class NetworkHubDeviceComponent : PciDeviceComponent<NetworkHubDeviceState>
{
    [ViewVariables] public const int ArgumentsOffset = 0x100;

    [ViewVariables] public const int Arguments = 10;

    [ViewVariables] public const string BaseInputName = "NetworkInput";

    [ViewVariables] public const string BaseOutputName = "NetworkOutput";

    [ViewVariables] public const int MaxPorts = 32;

    [ViewVariables] public const string EthernetMessageCommand = "ethernet_message";

    [ViewVariables] public const string MessagePayload = "ethernet_message_payload";

    [ViewVariables] public const int MaxPortQuery = 32;

    /// <summary>
    ///     In bytes
    /// </summary>
    [ViewVariables] public const int MaxMessageSize = 1500;

    public override PciDevice Device { get; } =
        new("network_hub", 0x1000, VendorId.CommSolutions, DeviceId.NetworkHub);
}

[Access(typeof(NetworkHubDeviceSystem))]
public sealed class NetworkHubDeviceState : DeviceState
{
    [ViewVariables] public readonly double[] Arguments = new double[NetworkHubDeviceComponent.Arguments];

    [ViewVariables] public Dictionary<string, InputHubPort> InputPorts = new();

    [ViewVariables] public double OpResult = (double)NetworkHubError.Ok;

    [ViewVariables] public Dictionary<string, OutputHubPort> OutputPorts = new();
}

public sealed class OutputHubPort
{
    [ViewVariables] public string Id;

    [ViewVariables] public bool IsConnected;

    [ViewVariables] public HubPortMode Mode = HubPortMode.Simple;

    public OutputHubPort(string id)
    {
        Id = id;
    }
}

public sealed class InputHubPort
{
    [ViewVariables] public readonly Queue<byte[]> MessagesQueue = new(NetworkHubDeviceComponent.MaxPortQuery);

    [ViewVariables] public string Id;

    [ViewVariables] public bool IsConnected;

    [ViewVariables] public bool IsEnabled;

    [ViewVariables] public bool IsPending;

    [ViewVariables] public HubPortMode Mode = HubPortMode.Simple;

    public InputHubPort(string id)
    {
        Id = id;
    }
}

public enum HubPortMode
{
    Simple = 0,
    Complex = 1
}

public enum NetworkHubError
{
    Ok = 0,
    InvalidPort = -1,
    InvalidPortMode = -2,
    InvalidSize = -3,
    Unknown = 0xFFFFFF
}
