using Robust.Shared.Serialization;

namespace Content.Shared._Horizon.ShiftStartEffect;

/// <summary>
/// Отправляется игроку при появлении, запускает на клиенте полноэкранное интро.
/// </summary>
[Serializable, NetSerializable]
public sealed class ShiftStartIntroEvent : EntityEventArgs
{
    public string Title;
    public string Company;
    public string[] Lines;
    public Color ThreatColor;
    public ShiftStartIntroColumn[] Columns;

    public ShiftStartIntroEvent(string title, string company, string[] lines, Color threatColor, ShiftStartIntroColumn[] columns)
    {
        Title = title;
        Company = company;
        Lines = lines;
        ThreatColor = threatColor;
        Columns = columns;
    }
}

[Serializable, NetSerializable]
public sealed class ShiftStartIntroColumn
{
    public string Header;
    public int Total;
    public ShiftStartIntroEntry[] Entries;

    public ShiftStartIntroColumn(string header, int total, ShiftStartIntroEntry[] entries)
    {
        Header = header;
        Total = total;
        Entries = entries;
    }
}

[Serializable, NetSerializable]
public sealed class ShiftStartIntroEntry
{
    public string Name;
    public string Job;

    public ShiftStartIntroEntry(string name, string job)
    {
        Name = name;
        Job = job;
    }
}
