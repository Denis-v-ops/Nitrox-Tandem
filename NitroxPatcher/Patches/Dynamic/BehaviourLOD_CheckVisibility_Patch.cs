#if DEBUG
using System.Reflection;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     Windowless test games (Unity batch mode, tools/dual-client.ps1 -Windowless) render no camera, so
///     <see cref="Renderer.isVisible" /> is always false and every <see cref="BehaviourLOD" /> farther away than its
///     very-close distance stays Minimal. The Cyclops engine (SubControl), fires (SubFire), SubRoot and base flooding
///     (BaseFloodSim) depend on that state. In batch mode, a renderer counts as visible when its bounds are inside the
///     main camera's view: close to what a windowed game reports.
/// </summary>
public sealed partial class BehaviourLOD_CheckVisibility_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Method((BehaviourLOD t) => t.CheckVisibility());

    private static readonly Plane[] frustumPlanes = new Plane[6];
    private static int frustumFrame = -1;

    public static bool Prefix(BehaviourLOD __instance, ref bool __result)
    {
        if (!Application.isBatchMode || __instance.useBoundingBoxForVisibility || __instance.renderers == null || !MainCamera.camera)
        {
            return true;
        }
        if (frustumFrame != Time.frameCount)
        {
            GeometryUtility.CalculateFrustumPlanes(MainCamera.camera, frustumPlanes);
            frustumFrame = Time.frameCount;
        }
        foreach (Renderer renderer in __instance.renderers)
        {
            if (renderer && renderer.enabled && renderer.gameObject.activeInHierarchy && GeometryUtility.TestPlanesAABB(frustumPlanes, renderer.bounds))
            {
                __result = true;
                return false;
            }
        }
        __result = false;
        return false;
    }
}
#endif
