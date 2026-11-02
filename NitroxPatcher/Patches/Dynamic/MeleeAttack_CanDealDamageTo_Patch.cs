using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Nitrox.Model.DataStructures;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.PlayerLogic;
using NitroxClient.MonoBehaviours.Cyclops;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Prefix (FIX_SPEC S14): for creatures whose bites on players the simulating game decides (<see cref="AI.AttacksPlayersFromSimulator" />),
/// - the simulating game may bite other players' bodies, as it bites its own player (vanilla only accepts a target with a
///   <see cref="Player" /> component, which a body hasn't). The bite reaches the bitten player through LiveMixin_TakeDamage_Patch;
/// - the other games don't bite their own player: that would hurt the player a second time.
/// Transpiler: for some reason, when a creature attacks a Cyclops, its main rigidbody is sometimes not properly detected by the raycasts.
/// Thus we change the target check (when it's a Cyclops) to also check if the collider is part of the target cyclops.
/// </summary>
public sealed partial class MeleeAttack_CanDealDamageTo_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((MeleeAttack t) => t.CanDealDamageTo(default));

    public static bool Prefix(MeleeAttack __instance, GameObject target, ref bool __result)
    {
        Creature creature = __instance.creature;
        if (!target || !Resolve<AI>().AttacksPlayersFromSimulator(creature) || !creature.TryGetNitroxId(out NitroxId creatureId))
        {
            return true;
        }
        bool simulated = Resolve<SimulationOwnership>().HasAnyLockType(creatureId);

        if (target.TryGetComponent(out RemotePlayerIdentifier remotePlayerIdentifier))
        {
            __result = simulated && __instance.canBitePlayer && remotePlayerIdentifier.RemotePlayer.CanBeAttacked() && IsPathClear(__instance, target);
            return false;
        }
        if (!simulated && Player.main && target == Player.main.gameObject)
        {
            __result = false;
            return false;
        }
        return true;
    }

    /// <summary>
    /// The end of <see cref="MeleeAttack.CanDealDamageTo" /> (game 83031): nothing but the target, the creature itself or
    /// other creatures between the creature and the target.
    /// </summary>
    private static bool IsPathClear(MeleeAttack meleeAttack, GameObject target)
    {
        Vector3 origin = meleeAttack.transform.position;
        Vector3 direction = target.transform.position - origin;
        int hitCount = UWE.Utils.RaycastIntoSharedBuffer(origin, direction, direction.magnitude, -5, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hitCount; i++)
        {
            Collider collider = UWE.Utils.sharedHitBuffer[i].collider;
            GameObject hitObject = collider.attachedRigidbody ? collider.attachedRigidbody.gameObject : collider.gameObject;
            if (hitObject != target && hitObject != meleeAttack.gameObject && !hitObject.GetComponent<Creature>())
            {
                return false;
            }
        }
        return true;
    }

    /*
     * REPLACE:
     * if (!(gameObject == target) && !(gameObject == base.gameObject) && !(gameObject.GetComponent<Creature>() != null))
     * 
     * BY:
     * if (!(gameObject == target) && !(gameObject == base.gameObject) && !(gameObject.GetComponent<Creature>() != null) && !MeleeAttack_CanDealDamageTo_Patch.IsValidTarget(target, gameObject))
     */
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        // Match the latest part of the if
        CodeMatcher matcher = new CodeMatcher(instructions).MatchEndForward([
            new CodeMatch(OpCodes.Ldloc_S),
            new CodeMatch(OpCodes.Callvirt, Reflect.Method((GameObject t) => t.GetComponent<Creature>())),
            new CodeMatch(OpCodes.Ldnull),
            new CodeMatch(OpCodes.Call),
            new CodeMatch(OpCodes.Brtrue),
        ]);

        // We copy the "brtrue" so we can easily insert another clause in the "if"
        CodeInstruction brTrueInstruction = matcher.Instruction;

        // Advance 1 to insert after the matched pattern
        matcher.Advance(1)
               .Insert([
                   new CodeInstruction(OpCodes.Ldarg_1), // target
                   new CodeInstruction(OpCodes.Ldloc_S, (byte)7), // gameObject
                   new CodeInstruction(OpCodes.Call, Reflect.Method(() => IsValidTarget(default, default))),
                   brTrueInstruction
                ]);
        
        return matcher.InstructionEnumeration();
    }

    public static bool IsValidTarget(GameObject target, GameObject gameObject)
    {
        return target.GetComponent<NitroxCyclops>() && gameObject.GetComponentInParent<NitroxCyclops>(true);
    }
}
