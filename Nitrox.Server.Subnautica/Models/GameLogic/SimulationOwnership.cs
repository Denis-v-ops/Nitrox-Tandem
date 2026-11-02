using System.Collections.Generic;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;

namespace Nitrox.Server.Subnautica.Models.GameLogic
{
    internal sealed class SimulationOwnershipData
    {
        public struct PlayerLock
        {
            public Player Player { get; }
            public SimulationLockType LockType { get; set; }

            public PlayerLock(Player player, SimulationLockType lockType)
            {
                Player = player;
                LockType = lockType;
            }
        }

        Dictionary<NitroxId, PlayerLock> playerLocksById = new Dictionary<NitroxId, PlayerLock>();

        /// <summary>
        /// MULTINAUGHTICA_SPEC M5 (ported from MultiNaughtica): who lost the lock of an id last, and when
        /// (<see cref="TickCount" />). Guarded by <see cref="playerLocksById" />.
        /// </summary>
        private readonly Dictionary<NitroxId, (SessionId Session, long Tick)> previousOwnersById = [];

        private const int MAX_REMEMBERED_PREVIOUS_OWNERS = 256;
        private const long FORGET_PREVIOUS_OWNER_AFTER_MS = 5000;

        /// <summary>
        /// Milliseconds clock for <see cref="WasOwnerWithin" />; tests replace it.
        /// </summary>
        internal Func<long> TickCount { get; init; } = () => Environment.TickCount64;

        public bool TryToAcquire(NitroxId id, Player player, SimulationLockType requestedLock)
        {
            lock (playerLocksById)
            {
                // If no one is simulating then acquire a lock for this player
                if (!playerLocksById.TryGetValue(id, out PlayerLock playerLock))
                {
                    playerLocksById[id] = new PlayerLock(player, requestedLock);
                    return true;
                }

                // If this player owns the lock then they are already simulating
                if (playerLock.Player == player)
                {
                    // update the lock type in case they are attempting to downgrade
                    playerLocksById[id] = new PlayerLock(player, requestedLock);
                    return true;
                }

                // If the current lock owner has a transient lock then only override if we are requesting exclusive access
                if (playerLock.LockType == SimulationLockType.TRANSIENT && requestedLock == SimulationLockType.EXCLUSIVE)
                {
                    RememberPreviousOwner(id, playerLock.Player);
                    playerLocksById[id] = new PlayerLock(player, requestedLock);
                    return true;
                }

                // We must be requesting a transient lock and the owner already has a lock (either transient or exclusive).
                // there is no way to break it so we will return false.
                return false;
            }
        }

        public bool RevokeIfOwner(NitroxId id, Player player)
        {
            lock (playerLocksById)
            {
                if (playerLocksById.TryGetValue(id, out PlayerLock playerLock) && playerLock.Player == player)
                {
                    playerLocksById.Remove(id);
                    RememberPreviousOwner(id, playerLock.Player);
                    return true;
                }

                return false;
            }
        }

        public List<NitroxId> RevokeAllForOwner(SessionId sessionId)
        {
            lock (playerLocksById)
            {
                List<NitroxId> revokedIds = [];

                foreach (KeyValuePair<NitroxId, PlayerLock> idWithPlayerLock in playerLocksById)
                {
                    if (idWithPlayerLock.Value.Player.SessionId == sessionId)
                    {
                        revokedIds.Add(idWithPlayerLock.Key);
                    }
                }

                foreach (NitroxId id in revokedIds)
                {
                    playerLocksById.Remove(id);
                }

                return revokedIds;
            }
        }

        public bool RevokeOwnerOfId(NitroxId id)
        {
            lock (playerLocksById)
            {
                if (playerLocksById.TryGetValue(id, out PlayerLock playerLock))
                {
                    RememberPreviousOwner(id, playerLock.Player);
                }
                return playerLocksById.Remove(id);
            }
        }

        /// <summary>
        /// True if the session held the lock of the id until it was revoked or taken over at most <paramref name="withinMs" /> ms ago.
        /// </summary>
        public bool WasOwnerWithin(NitroxId id, SessionId sessionId, long withinMs)
        {
            lock (playerLocksById)
            {
                return previousOwnersById.TryGetValue(id, out (SessionId Session, long Tick) previous) &&
                       previous.Session == sessionId &&
                       TickCount() - previous.Tick <= withinMs;
            }
        }

        /// <summary>
        /// Called when the current owner proved to be up to date: what the previous owner still has in flight is older from then on.
        /// </summary>
        public void ForgetPreviousOwner(NitroxId id)
        {
            lock (playerLocksById)
            {
                previousOwnersById.Remove(id);
            }
        }

        private void RememberPreviousOwner(NitroxId id, Player previousOwner)
        {
            long now = TickCount();
            if (previousOwnersById.Count >= MAX_REMEMBERED_PREVIOUS_OWNERS)
            {
                List<NitroxId> expired = [];
                foreach (KeyValuePair<NitroxId, (SessionId Session, long Tick)> entry in previousOwnersById)
                {
                    if (now - entry.Value.Tick > FORGET_PREVIOUS_OWNER_AFTER_MS)
                    {
                        expired.Add(entry.Key);
                    }
                }
                foreach (NitroxId expiredId in expired)
                {
                    previousOwnersById.Remove(expiredId);
                }
            }
            previousOwnersById[id] = (previousOwner.SessionId, now);
        }

        public Player? GetPlayerForLock(NitroxId id)
        {
            lock (playerLocksById)
            {
                if (playerLocksById.TryGetValue(id, out PlayerLock playerLock))
                {
                    return playerLock.Player;
                }
            }
            return null;
        }

        public bool TryGetLock(NitroxId id, out PlayerLock playerLock)
        {
            lock (playerLocksById)
            {
                return playerLocksById.TryGetValue(id, out playerLock);
            }
        }
    }
}
