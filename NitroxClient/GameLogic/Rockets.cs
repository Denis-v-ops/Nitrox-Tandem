using NitroxClient.Communication.Abstract;
using NitroxClient.MonoBehaviours;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.Packets;
using UnityEngine;

namespace NitroxClient.GameLogic
{
    public class Rockets
    {
        private readonly IPacketSender packetSender;
        private readonly Vehicles vehicles;
        private readonly PlayerManager playerManager;

        public Rockets(IPacketSender packetSender, Vehicles vehicles, PlayerManager playerManager)
        {
            this.packetSender = packetSender;
            this.vehicles = vehicles;
            this.playerManager = playerManager;
        }

        public void RequestRocketLaunch(Rocket rocket)
        {
            if (rocket.TryGetNitroxEntity(out NitroxEntity entity))
            {
                packetSender.Send(new RocketLaunch(entity.Id));
            }
            else
            {
                Log.Error($"{nameof(Rockets.RequestRocketLaunch)}: Can't find a NitroxEntity attached to the Rocket: {rocket.name}");
            }
        }

        public void RocketLaunch(NitroxId rocketId)
        {
            // Avoid useless calculations
            if (LaunchRocket.launchStarted)
            {
                return;
            }

            RocketLaunch(NitroxEntity.RequireObjectFrom(rocketId));
        }

        /// <summary>
        ///     Whether the local player is in the rocket's cockpit: within 5.55 m (pre-calculated radius) of a point in the
        ///     middle of the rocket's last stage, not underwater and not in a sub.
        /// </summary>
        public static bool IsAboard(GameObject rocketObject)
        {
            GameObject sphereCenter = rocketObject.FindChild("AtmosphereVolume");
            return sphereCenter &&
                   !Player.main.IsUnderwater() &&
                   !Player.main.currentSub &&
                   NitroxVector3.Distance(Player.main.transform.position.ToDto(), sphereCenter.transform.position.ToDto()) <= 5.55f;
        }

        public void RocketLaunch(GameObject rocketObject)
        {
            LaunchRocket launchRocket = rocketObject.RequireComponentInChildren<LaunchRocket>(true);

            // Only launch if you're in the rocket
            if (!IsAboard(rocketObject))
            {
                return;
            }

            // When the server sends this to the client, he should execute the rocket launch
            // Code extracted from LaunchRocket::OnHandClick
            LaunchRocket.SetLaunchStarted();
            PlayerTimeCapsule.main.Submit(null);
            launchRocket.StartCoroutine(launchRocket.StartEndCinematic());
            HandReticle.main.RequestCrosshairHide();

            // We also need to hide the other players
            foreach (RemotePlayer player in playerManager.GetAll())
            {
                player.PlayerModel.SetActive(false);
            }

            Log.InGame(Language.main.Get("Nitrox_ThankForPlaying"));
        }
    }
}
