using Content.Server.Materials;
using Content.Server.Stack;
using Content.Shared._Horizon.Materials;
using Content.Shared.Maps;
using Content.Shared.Materials;

namespace Content.Server._Horizon.Materials;

/// <summary>
/// Server side of <see cref="TileReclaimerSystem"/>: puts the tile materials into the reclaimer's storage and ejects them as stacks.
/// </summary>
public sealed class ServerTileReclaimerSystem : TileReclaimerSystem
{
    [Dependency] private readonly MaterialStorageSystem _materialStorage = default!;
    [Dependency] private readonly StackSystem _stack = default!;

    protected override void SpawnMaterialsFromComposition(Entity<MaterialStorageComponent?, TransformComponent?> ent,
        ContentTileDefinition tileDefinition,
        float efficiency)
    {
        var (uid, storage, xform) = ent;
        if (!Resolve(uid, ref storage, ref xform, false))
            return;

        foreach (var (material, amount) in tileDefinition.MaterialComposition)
        {
            var outputAmount = (int) (amount * efficiency);
            _materialStorage.TryChangeMaterialAmount(uid, material, outputAmount, storage);
        }

        foreach (var (storedMaterial, storedAmount) in storage.Storage)
        {
            var stacks = _materialStorage.SpawnMultipleFromMaterial(storedAmount,
                storedMaterial,
                xform.Coordinates,
                out var materialOverflow);
            var amountConsumed = storedAmount - materialOverflow;
            _materialStorage.TryChangeMaterialAmount(uid, storedMaterial, -amountConsumed, storage);
            foreach (var stack in stacks)
            {
                _stack.TryMergeToContacts(stack);
            }
        }
    }
}
