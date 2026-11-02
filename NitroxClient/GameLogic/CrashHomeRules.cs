namespace NitroxClient.GameLogic;

/// <summary>
///     What the player who holds a sulfur plant's (<c>CrashHome</c>) lock does with its crash fish (MULTINAUGHTICA_SPEC M4,
///     #2673). Free of Unity types so it can be tested.
/// </summary>
/// <remarks>
///     A home keeps one number, <c>spawnTime</c>: -1 while a fish lives in it, the game time of the next fish after the
///     fish died (<c>CrashHome.Update</c> starts a 20 minute timer when the home has no fish and the number is negative),
///     and 0 for a home nobody has met yet (the server's <c>CrashHomeBootstrapper</c>). <c>CrashHome.Update</c> calls
///     <c>Spawn</c> once the number isn't negative and the game time reached it, whatever the home holds (game 83031). The
///     fish is a server entity, a child of its home, and comes back with it when a cell is loaded, so a home can hold a fish
///     and a timer at once: the lock holder started the timer before the server's fish reached the home (a home is spawned
///     before its children), or a save kept both. Ported from MultiNaughtica, adapted.
///     <para>
///         <c>CrashHome.OnDestroy</c> destroys the home's fish object when the home's cell unloads. That isn't a death and
///         isn't sent: until M4 a patch sent <c>EntityDestroyed</c> for it whenever the home's lock holder walked away, so
///         every home lost its fish for both players and came back empty with a 20 minute timer (#2673). A fish that dies
///         is sent by <c>LiveMixin_Kill_Patch</c> (crash fish are on <c>LiveMixinManager</c>'s death list).
///     </para>
/// </remarks>
public static class CrashHomeRules
{
    /// <summary>Value of <c>CrashHome.spawnTime</c> while a fish lives in the home.</summary>
    public const float FISH_ALIVE = -1f;

    /// <summary>
    ///     How long (seconds) the holder of a home waits for the home's saved state before it acts on the number the prefab
    ///     came with.
    /// </summary>
    public const float METADATA_GRACE_SECONDS = 20f;

    public enum SpawnCall
    {
        /// <summary>This player doesn't hold the home's lock: the holder makes the fish and the server sends it here.</summary>
        NotTheHolder = 0,

        /// <summary>The home has its fish: no second one, the number is set back to <see cref="FISH_ALIVE" /> and sent.</summary>
        FishAlready = 1,

        /// <summary>The home is empty and it is time: make the fish.</summary>
        MakeFish = 2
    }

    /// <param name="holdsLock">This player holds a simulation lock of the home.</param>
    /// <param name="homeHasLivingFish">The home's <c>crash</c> exists (Unity's null check: a destroyed fish doesn't count).</param>
    public static SpawnCall DecideSpawn(bool holdsLock, bool homeHasLivingFish)
    {
        if (!holdsLock)
        {
            return SpawnCall.NotTheHolder;
        }
        return homeHasLivingFish ? SpawnCall.FishAlready : SpawnCall.MakeFish;
    }

    /// <summary>
    ///     Whether the holder of a home may act on its number (start the 20 minute timer of an empty home and send it, or make
    ///     a fish). A home is spawned from its prefab with the prefab's number and gets its saved state
    ///     (<c>CrashHomeMetadata</c>) a little later; acting in between overwrites the saved timer of an emptied home with a
    ///     new one, or makes a fish for a home whose saved timer hasn't run out. The wait ends when the saved state arrived,
    ///     or after <see cref="METADATA_GRACE_SECONDS" /> (a home the server has no state for).
    /// </summary>
    /// <param name="metadataApplied">The home's saved state was applied to it.</param>
    /// <param name="secondsSinceFirstAsked">Real seconds since the holder was first asked to act on this home.</param>
    public static bool MayActOnItsNumber(bool metadataApplied, float secondsSinceFirstAsked)
    {
        // A broken clock (NaN) keeps waiting
        return metadataApplied || secondsSinceFirstAsked >= METADATA_GRACE_SECONDS;
    }

    public enum IncomingFishCall
    {
        /// <summary>The home has no living fish, or it is the incoming one: the incoming fish becomes the home's fish.</summary>
        Adopt = 0,

        /// <summary>The home has another living fish object: it is destroyed and the incoming fish takes its place.</summary>
        ReplaceTheOldFish = 1
    }

    /// <summary>
    ///     What <c>CrashEntitySpawner</c> does with the home's <c>crash</c> when the server's fish for that home arrives. A
    ///     spawned fish isn't a child of its home's transform, so only <c>CrashHome.OnDestroy</c> removes it, and that only
    ///     knows the object <c>crash</c> points to: a fish no longer pointed at stays in the world until the scene ends. The
    ///     incoming fish stays: a fish spawned again while its old object still waits to be destroyed has the same id.
    /// </summary>
    /// <param name="homeHasLivingFish">The home's <c>crash</c> exists (Unity's null check).</param>
    /// <param name="livingFishIsTheIncomingOne">That object is the very object that arrives.</param>
    public static IncomingFishCall DecideIncomingFish(bool homeHasLivingFish, bool livingFishIsTheIncomingOne)
    {
        return homeHasLivingFish && !livingFishIsTheIncomingOne ? IncomingFishCall.ReplaceTheOldFish : IncomingFishCall.Adopt;
    }
}
