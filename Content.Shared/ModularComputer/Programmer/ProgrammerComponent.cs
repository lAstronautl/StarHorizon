namespace Content.Shared.ModularComputer.Programmer;

[RegisterComponent]
public sealed partial class ProgrammerComponent : Component
{
    public ProgrammerState State = ProgrammerState.Empty;
}
