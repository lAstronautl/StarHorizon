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

using Content.Shared.Examine;

namespace Content.Server.ModularComputer.SerialNumber;

public sealed class SerialNumberSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<SerialNumberComponent, ExaminedEvent>(OnSerialNumberExamined);
    }

    private void OnSerialNumberExamined(EntityUid uid, SerialNumberComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString("serial-number-examine", ("uuid", component.Uuid)));
    }
}
