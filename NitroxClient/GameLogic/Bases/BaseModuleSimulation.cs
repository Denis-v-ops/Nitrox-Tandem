using Nitrox.Model.DataStructures;
using UnityEngine;

namespace NitroxClient.GameLogic.Bases;

/// <summary>
///     Decides which game runs the base machines that create or burn items by themselves (filtration machine,
///     bioreactor). Every game that has the base loaded runs their timers, so without this each game adds its own items
///     (#2495, filtration overflow) or burns its own copies (#2494, bioreactor).
/// </summary>
/// <remarks>
///     The producer is the game holding the simulation lock on the machine's base, like for trash cans
///     (<c>Trashcan_Update_Patch</c>). Bases are global root entities, so the server already hands that lock out: to the
///     first player who joins (<c>EntitySimulation.AssignGlobalRootEntitiesAndGetData</c>) or who builds the base
///     (<c>BuildingProcessor.TryClaimBuildPieceAsync</c>), and to another connected player when the holder leaves
///     (<c>EntitySimulation.CalculateSimulationChangesFromPlayerDisconnect</c>; every player can "see" a global root).
/// </remarks>
public class BaseModuleSimulation
{
    private readonly SimulationOwnership simulationOwnership;

    public BaseModuleSimulation(SimulationOwnership simulationOwnership)
    {
        this.simulationOwnership = simulationOwnership;
    }

    public bool IsLocalProducer(Component module)
    {
        SubRoot subRoot = module.GetComponentInParent<SubRoot>();
        if (!subRoot || !subRoot.TryGetNitroxId(out NitroxId baseId))
        {
            // Not in a synced base: nobody else can produce for it, so behave like the unmodded game.
            Log.WarnOnce($"[{nameof(BaseModuleSimulation)}] {module.GetType().Name} {module.GetFullHierarchyPath()} has no base with an id, it produces locally");
            return true;
        }
        return simulationOwnership.HasAnyLockType(baseId);
    }
}
