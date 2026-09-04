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

using System.Threading.Tasks;
using Content.Shared.ModularComputer.Programmer;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Shared.Utility;

namespace Content.Client.ModularComputer.Programmer;

public sealed class ProgrammerSystem : SharedProgrammerSystem
{
    [Dependency] private readonly IFileDialogManager _fileDialog = default!;
    
    [PublicAPI]
    public async Task TryLoadBootromFile(EntityUid uid, ProgrammerComponent? component)
    {
        if (!Resolve(uid, ref component))
            return;
        
        var fileStream = await _fileDialog.OpenFile();
        
        if (fileStream is null)
            return;

        if (fileStream.Length > MaxBootromSize)
            return;

        var buffer = new byte[fileStream.Length];
        fileStream.ReadToEnd(buffer);

        RaiseNetworkEvent(new UploadBootromEvent(GetNetEntity(uid), buffer));
    }
}
