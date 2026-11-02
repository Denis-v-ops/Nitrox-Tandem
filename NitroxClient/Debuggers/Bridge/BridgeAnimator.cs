#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NitroxClient.Debuggers.Bridge;

/// <summary>
///     <c>animator &lt;target&gt; [all]</c>: every parameter of the target's <see cref="Animator" /> (on the object or its
///     first child that has one; <c>all</c>: every animator below it) with its current value, and per layer the playing
///     clips. Parameters aren't fields, so <c>get</c> can't read them. Useful for the partner's body: its id is the
///     partner's player id (<c>state</c> on the partner's game).
/// </summary>
internal static class BridgeAnimator
{
    public static object Command(List<string> args)
    {
        if (args.Count == 0)
        {
            throw new ArgumentException("animator <target> [all]");
        }
        GameObject gameObject = BridgeReflection.ResolveTarget(args[0]);
        bool all = args.Count > 1 && args[1].Equals("all", StringComparison.OrdinalIgnoreCase);
        List<Animator> animators = all
            ? gameObject.GetComponentsInChildren<Animator>(true).ToList()
            : [gameObject.GetComponent<Animator>() ? gameObject.GetComponent<Animator>() : gameObject.GetComponentInChildren<Animator>(true)];
        if (animators.Count == 0 || !animators[0])
        {
            throw new InvalidOperationException($"No Animator on {gameObject.name}");
        }
        return animators.Select(Describe).ToList();
    }

    private static Dictionary<string, object?> Describe(Animator animator)
    {
        Dictionary<string, object?> description = new()
        {
            ["object"] = animator.gameObject.name,
            ["enabled"] = animator.enabled && animator.gameObject.activeInHierarchy,
            ["cullingMode"] = animator.cullingMode.ToString(),
            ["speed"] = animator.speed,
            ["controller"] = animator.runtimeAnimatorController ? animator.runtimeAnimatorController.name : null
        };
        if (!animator.isInitialized || !animator.runtimeAnimatorController)
        {
            description["initialized"] = false;
            return description;
        }
        Dictionary<string, object?> parameters = new();
        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            parameters[parameter.name] = parameter.type switch
            {
                AnimatorControllerParameterType.Float => Math.Round(animator.GetFloat(parameter.nameHash), 3),
                AnimatorControllerParameterType.Int => animator.GetInteger(parameter.nameHash),
                AnimatorControllerParameterType.Bool => animator.GetBool(parameter.nameHash),
                AnimatorControllerParameterType.Trigger => animator.GetBool(parameter.nameHash) ? "trigger set" : "trigger",
                _ => null
            };
        }
        description["parameters"] = parameters;
        List<Dictionary<string, object?>> layers = [];
        for (int layer = 0; layer < animator.layerCount; layer++)
        {
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(layer);
            layers.Add(new Dictionary<string, object?>
            {
                ["layer"] = animator.GetLayerName(layer),
                ["weight"] = Math.Round(animator.GetLayerWeight(layer), 3),
                ["stateHash"] = state.shortNameHash,
                ["normalizedTime"] = Math.Round(state.normalizedTime, 3),
                ["inTransition"] = animator.IsInTransition(layer),
                ["clips"] = animator.GetCurrentAnimatorClipInfo(layer).Where(clip => clip.clip).Select(clip => $"{clip.clip.name} ({Math.Round(clip.weight, 2)})").ToList()
            });
        }
        description["layers"] = layers;
        return description;
    }
}
#endif
