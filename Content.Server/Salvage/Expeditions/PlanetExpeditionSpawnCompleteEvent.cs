namespace Content.Server.Salvage.Expeditions;

/// <summary>
/// Raised on an expedition planet when its expedition map has finished generating (or failed).
/// </summary>
public sealed class PlanetExpeditionSpawnCompleteEvent : EntityEventArgs
{
    public readonly EntityUid Map;
    public readonly bool Success;

    public PlanetExpeditionSpawnCompleteEvent(EntityUid map, bool success)
    {
        Map = map;
        Success = success;
    }
}
