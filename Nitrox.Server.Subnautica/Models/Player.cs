using System.Collections.Generic;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.MultiplayerSession;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models
{
    internal sealed class Player
    {
        private const ushort OFFLINE_SESSION_ID = 0;

        private readonly ThreadSafeSet<AbsoluteEntityCell> visibleCells;

        private ushort sessionId;

        public ThreadSafeList<NitroxTechType> UsedItems { get; }
        public Optional<NitroxId>[] QuickSlotsBindingIds { get; set; }

        public PlayerContext? PlayerContext { get; set; }
        /// <inheritdoc cref="Nitrox.Model.Core.PeerId" />
        public PeerId PeerId { get; init; }

        /// <inheritdoc cref="Nitrox.Model.Core.SessionId" />
        public SessionId SessionId
        {
            get => Interlocked.CompareExchange(ref sessionId, OFFLINE_SESSION_ID, OFFLINE_SESSION_ID);
            set => Interlocked.Exchange(ref sessionId, value);
        }

        public bool IsOnline
        {
            get => SessionId != OFFLINE_SESSION_ID;
            set
            {
                if (value)
                {
                    throw new InvalidOperationException($"{nameof(Player)} cannot be put in an online state without a {nameof(SessionId)}!");
                }
                SessionId = OFFLINE_SESSION_ID;
            }
        }

        public string Name { get; }
        public bool IsPermaDeath { get; set; }
        public NitroxVector3 Position { get; set; }
        public NitroxQuaternion Rotation { get; set; }
        public NitroxId GameObjectId { get; set; }
        public Optional<NitroxId> SubRootId { get; set; }
        public Perms Permissions { get; set; }
        public PlayerStatsData Stats { get; set; }
        public SubnauticaGameMode GameMode { get; set; }
        public NitroxVector3? LastStoredPosition { get; set; }
        public Optional<NitroxId> LastStoredSubRootID { get; set; }
        public bool LastStoredInPrecursor { get; set; }
        public ThreadSafeDictionary<string, float> PersonalCompletedGoalsWithTimestamp { get; }
        public ThreadSafeDictionary<string, PingInstancePreference> PingInstancePreferences { get; set; }
        public ThreadSafeList<int> PinnedRecipePreferences { get; set; }
        public ThreadSafeDictionary<string, NitroxId> EquippedItems { get; set; }
        public ThreadSafeSet<NitroxId> OutOfCellVisibleEntities { get; set; } = [];
        public bool InPrecursor { get; set; }
        public bool DisplaySurfaceWater { get; set; }

        /// <summary>
        ///     When the player's rescue window ends (COOP_FEATURES_SPEC T1), in seconds of <c>TimeService.ActiveTime</c>.
        ///     Null when the player isn't down. Saved.
        /// </summary>
        public double? DownedUntil { get; set; }

        /// <summary>
        ///     How often the player went down since they last rested (COOP_FEATURES_SPEC T1). Saved.
        /// </summary>
        public int DownsSinceRest { get; set; }

        /// <summary>
        ///     When the player last went down (COOP_FEATURES_SPEC T1), in seconds of <c>TimeService.ActiveTime</c>. Null if
        ///     never. Saved.
        /// </summary>
        public double? LastDownedAt { get; set; }

        /// <summary>
        ///     Keys of the biome and location goals whose PDA line this player heard themselves (COOP_FEATURES_SPEC D5b).
        ///     Saved.
        /// </summary>
        public ThreadSafeSet<string> HeardGoals { get; init; } = [];

        /// <summary>
        ///     Whether the player's signal is cut off, from their latest <see cref="PlayerStats" /> (COOP_FEATURES_SPEC D2).
        ///     Not saved.
        /// </summary>
        public bool Interfered { get; set; }

        public PlayerEntity Entity { get; set; }

        public Player(PeerId peerId, SessionId sessionId, string name, bool isPermaDeath, PlayerContext? playerContext,
                      NitroxVector3 position, NitroxQuaternion rotation, NitroxId playerId, Optional<NitroxId> subRootId, Perms perms, PlayerStatsData stats, SubnauticaGameMode gameMode,
                      IEnumerable<NitroxTechType> usedItems, Optional<NitroxId>[] quickSlotsBindingIds,
                      IDictionary<string, NitroxId> equippedItems, IDictionary<string, float> personalCompletedGoalsWithTimestamp, IDictionary<string, PingInstancePreference> pingInstancePreferences, IList<int> pinnedRecipePreferences, bool inPrecursor,
                      bool displaySurfaceWater)
        {
            PeerId = peerId;
            SessionId = sessionId;
            Name = name;
            IsPermaDeath = isPermaDeath;
            PlayerContext = playerContext;
            Position = position;
            Rotation = rotation;
            SubRootId = subRootId;
            GameObjectId = playerId;
            Permissions = perms;
            Stats = stats;
            GameMode = gameMode;
            LastStoredPosition = null;
            LastStoredSubRootID = Optional.Empty;
            UsedItems = new ThreadSafeList<NitroxTechType>(usedItems);
            QuickSlotsBindingIds = quickSlotsBindingIds;
            EquippedItems = new ThreadSafeDictionary<string, NitroxId>(equippedItems);
            visibleCells = new ThreadSafeSet<AbsoluteEntityCell>();
            PersonalCompletedGoalsWithTimestamp = new ThreadSafeDictionary<string, float>(personalCompletedGoalsWithTimestamp);
            PingInstancePreferences = new(pingInstancePreferences);
            PinnedRecipePreferences = new(pinnedRecipePreferences);
            InPrecursor = inPrecursor;
            DisplaySurfaceWater = displaySurfaceWater;
        }

        public static bool operator ==(Player? left, Player? right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(Player? left, Player? right)
        {
            return !Equals(left, right);
        }

        public override bool Equals(object? obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }
            if (ReferenceEquals(this, obj))
            {
                return true;
            }
            if (obj.GetType() != GetType())
            {
                return false;
            }
            return Equals((Player)obj);
        }

        public override int GetHashCode()
        {
            return PeerId.GetHashCode();
        }

        public void AddCells(IEnumerable<AbsoluteEntityCell> cells)
        {
            foreach (AbsoluteEntityCell cell in cells)
            {
                visibleCells.Add(cell);
            }
        }

        public void RemoveCells(IEnumerable<AbsoluteEntityCell> cells)
        {
            foreach (AbsoluteEntityCell cell in cells)
            {
                visibleCells.Remove(cell);
            }
        }

        public bool HasCellLoaded(AbsoluteEntityCell cell)
        {
            return visibleCells.Contains(cell);
        }

        public void ClearVisibleCells()
        {
            visibleCells.Clear();
        }

        public bool CanSee(Entity entity)
        {
            if (entity is WorldEntity worldEntity)
            {
                return worldEntity is GlobalRootEntity || HasCellLoaded(worldEntity.AbsoluteEntityCell) ||
                       OutOfCellVisibleEntities.Contains(entity.Id);
            }

            return true;
        }

        /// <param name="inPrecursor">Whether the destination is a dry precursor room (see <see cref="InPrecursor" />).</param>
        public void Teleport(NitroxVector3 destination, Optional<NitroxId> subRootID, bool inPrecursor, IPacketSender packetSender)
        {
            PlayerTeleported playerTeleported = new(Name, Position, destination, subRootID, inPrecursor);

            LastStoredPosition = Position;
            LastStoredSubRootID = SubRootId;
            LastStoredInPrecursor = InPrecursor;
            Position = destination;
            InPrecursor = inPrecursor;
            packetSender.SendPacketAsync(playerTeleported, SessionId);
        }

        public override string ToString()
        {
            return $"[{nameof(Player)} - {nameof(PeerId)}: {PeerId}, {nameof(Name)}: {Name}, Perms: {Permissions}, {nameof(Position)}: {Position}]";
        }

        /// <summary>
        ///     Returns a <b>new</b> list from the original set. To use the original set, use <see cref="AddCells" />,
        ///     <see cref="RemoveCells" /> and <see cref="HasCellLoaded" />.
        /// </summary>
        internal List<AbsoluteEntityCell> GetVisibleCells()
        {
            return [.. visibleCells];
        }

        private bool Equals(Player other)
        {
            return PeerId == other.PeerId;
        }
    }
}
