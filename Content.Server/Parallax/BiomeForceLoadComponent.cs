namespace Content.Server.Parallax;

/// <summary>
/// StarHorizon: пока сущность на карте с биомом, земля вокруг неё остаётся подгруженной, даже если рядом нет игроков
/// (иначе чанки биома выгружаются и на месте остаётся пустота).
/// </summary>
[RegisterComponent]
public sealed partial class BiomeForceLoadComponent : Component;
