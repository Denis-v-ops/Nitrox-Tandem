#if DEBUG
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace NitroxClient.Debuggers.Bridge;

/// <summary>
///     Holds the game's movement input (<c>input move &lt;x&gt; &lt;y&gt; &lt;z&gt;</c> | <c>input off</c>) as if a stick and the
///     up/down keys were held: x = strafe/turn, y = up/down, z = forward. Everything that reads
///     <see cref="GameInput.GetMoveDirection" /> sees it: walking (<c>PlayerMotor</c>, the Cyclops motor) and piloting
///     (<c>SubControl.Update</c>, vehicles), with the game's own power use and Nitrox's driven-vehicle packets.
/// </summary>
/// <remarks>
///     <c>GameInput.OnUpdate</c> (queue <c>UpdateInput</c>) recomputes the direction every frame; this runs right after it
///     in the same <c>ManagedUpdate</c> call, so every other reader of the frame sees the held value.
/// </remarks>
internal static class BridgeInput
{
    private static Vector3? heldMove;
    private static bool subscribed;

    public static object Command(List<string> args)
    {
        const string USAGE = "input move <x> <y> <z> | input off";
        if (args.Count == 0)
        {
            throw new ArgumentException(USAGE);
        }
        switch (args[0].ToLowerInvariant())
        {
            case "move":
                if (args.Count < 4)
                {
                    throw new ArgumentException(USAGE);
                }
                heldMove = new Vector3(Parse(args[1]), Parse(args[2]), Parse(args[3]));
                if (!subscribed)
                {
                    ManagedUpdate.Subscribe(ManagedUpdate.Queue.UpdateAfterInput, Apply);
                    subscribed = true;
                }
                return new Dictionary<string, object?> { ["move"] = heldMove };
            case "off":
                heldMove = null;
                if (subscribed)
                {
                    ManagedUpdate.Unsubscribe(ManagedUpdate.Queue.UpdateAfterInput, Apply);
                    subscribed = false;
                }
                return new Dictionary<string, object?> { ["move"] = null };
            default:
                throw new ArgumentException(USAGE);
        }
    }

    private static float Parse(string value) => float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static void Apply()
    {
        if (heldMove.HasValue)
        {
            GameInput.moveDirection = heldMove.Value;
        }
    }
}
#endif
