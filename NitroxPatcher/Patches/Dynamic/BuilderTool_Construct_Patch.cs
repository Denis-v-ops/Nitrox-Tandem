using System.Reflection;
using NitroxClient.GameLogic.Bases;
using Nitrox.Model.DataStructures;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     Building or deconstructing with the builder tool (every frame the button is held): not on a base another player just
///     changed, and only with the object's exclusive lock (<see cref="ConstructionLocks" />, COOP_FEATURES_SPEC B2).
/// </summary>
public sealed partial class BuilderTool_Construct_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Method((BuilderTool t) => t.Construct(default, default, default));

    public static bool Prefix(Constructable c)
    {
        // The original does nothing then (the first deconstruction step of a finished object is Constructable.SetState)
        if (!c || c.constructed)
        {
            return true;
        }
        if (!BuildingHandler.Main || !c.tr.parent || !c.tr.parent.TryGetNitroxId(out NitroxId parentId))
        {
            return Resolve<ConstructionLocks>().MayConstruct(c);
        }

        bool isAllowed = true;
        string message = string.Empty;

        BuildUtils.DeconstructionAllowed(parentId, ref isAllowed, ref message);
        if (!isAllowed)
        {
            Log.InGame(message);
            return false;
        }

        return Resolve<ConstructionLocks>().MayConstruct(c);
    }
}
