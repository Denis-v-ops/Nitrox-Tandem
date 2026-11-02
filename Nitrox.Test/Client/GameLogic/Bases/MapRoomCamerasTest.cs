using Nitrox.Model.DataStructures;

namespace NitroxClient.GameLogic.Bases;

[TestClass]
public class MapRoomCamerasTest
{
    [TestMethod]
    public void DefaultCameraIdsAreDerivedFromTheScannerRoomId()
    {
        // Both players and every load must give a scanner room's drones the same ids (FIX_SPEC S2 step 4, #2576)
        NitroxId mapRoomId = new();

        List<NitroxId> ids = MapRoomCameras.GetDefaultCameraIds(mapRoomId, 2);

        ids.Should().Equal(mapRoomId.Increment(), mapRoomId.Increment().Increment());
        ids.Should().Equal(MapRoomCameras.GetDefaultCameraIds(new NitroxId(mapRoomId.ToString()), 2));
        ids.Should().NotContain(mapRoomId);
    }
}
