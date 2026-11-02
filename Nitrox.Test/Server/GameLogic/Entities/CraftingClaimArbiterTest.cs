using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Bases;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;

namespace Nitrox.Test.Server.GameLogic.Entities;

/// <summary>
///     MULTINAUGHTICA_SPEC M8, the server's decision about a claim for crafting from lockers. Two players crafting at the same
///     moment can pick the same last items of a locker (each game only sees its own copy); a claim is all or nothing and an
///     item goes to one of them. Ported from MultiNaughtica (Alexander1170/MultiNaughtica @ 99428c8), adapted; the return
///     tests are ours.
/// </summary>
[TestClass]
public class CraftingClaimArbiterTest
{
    private static readonly SessionId alice = (ushort)1;
    private static readonly SessionId bob = (ushort)2;

    private EntityRegistry registry = null!;
    private CraftingClaimArbiter arbiter = null!;
    private BuildEntity baseEntity = null!;
    private ModuleEntity locker = null!;
    private ModuleEntity secondLocker = null!;
    private int nextClaimId = 1;

    [TestInitialize]
    public void Init()
    {
        registry = new EntityRegistry(NullLogger<EntityRegistry>.Instance);
        arbiter = new CraftingClaimArbiter(registry);
        baseEntity = BuildEntity.MakeEmpty();
        baseEntity.Id = new NitroxId();
        registry.AddOrUpdate(baseEntity);
        locker = AddModule(baseEntity);
        secondLocker = AddModule(baseEntity);
    }

    private Entity? Remove(NitroxId id)
    {
        Optional<Entity> removed = registry.RemoveEntity(id);
        return removed.HasValue ? removed.Value : null;
    }

    private CraftingClaimDecision Claim(NitroxId? scope, params NitroxId[] items) => arbiter.Claim(alice, nextClaimId++, scope, items, Remove);

    private ModuleEntity AddModule(Entity parent)
    {
        ModuleEntity module = ModuleEntity.MakeEmpty();
        module.Id = new NitroxId();
        module.ParentId = parent.Id;
        module.TechType = new NitroxTechType("Locker");
        registry.AddOrUpdate(module);
        return module;
    }

    private InventoryItemEntity AddItem(Entity container, string techType = "Titanium")
    {
        InventoryItemEntity item = new(new NitroxId(), "classId", new NitroxTechType(techType), null, container.Id, []);
        registry.AddOrUpdate(item);
        return item;
    }

    private static PlayerEntity NewPlayer(Entity? parent)
    {
        return new PlayerEntity(new NitroxTransform(NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxVector3.One), 0, "player", true, new NitroxId(), new NitroxTechType("Player"), null, parent?.Id, []);
    }

    private static VehicleEntity NewVehicle(string techType, Entity? parent = null)
    {
        return new VehicleEntity(new NitroxId(), 0f, new NitroxTransform(NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxVector3.One), 0, techType.ToLowerInvariant(), true, new NitroxId(),
                                 new NitroxTechType(techType), null, parent?.Id, []);
    }

    // ---- granted

    [TestMethod]
    public void ItemsInTheLockersOfTheBaseAreGrantedAndRemoved()
    {
        InventoryItemEntity a = AddItem(locker);
        InventoryItemEntity b = AddItem(secondLocker);

        CraftingClaimDecision decision = Claim(baseEntity.Id, a.Id, b.Id);

        decision.Outcome.Should().Be(CraftingClaimOutcome.Granted);
        decision.Removed.Should().BeEquivalentTo(new[] { a, b });
        registry.TryGetEntityById(a.Id, out Entity? _).Should().BeFalse();
        registry.TryGetEntityById(b.Id, out Entity? _).Should().BeFalse();
        locker.ChildEntities.Should().BeEmpty("the removal leaves the locker's list too");
    }

    [TestMethod]
    public void TheRemovalOrderIsTheOrderOfTheClaim()
    {
        InventoryItemEntity a = AddItem(locker);
        InventoryItemEntity b = AddItem(locker);
        InventoryItemEntity c = AddItem(locker);
        List<NitroxId> removalOrder = [];

        arbiter.Claim(alice, 1, baseEntity.Id, [c.Id, a.Id, b.Id], id =>
        {
            removalOrder.Add(id);
            return Remove(id);
        });

        removalOrder.Should().Equal(c.Id, a.Id, b.Id);
    }

    [TestMethod]
    public void ItemsInABuiltInLockerOfACyclopsAreGranted()
    {
        // The Cyclops' own lockers are PathBasedChildEntity children of its vehicle entity (VehicleChildEntityHelper)
        VehicleEntity cyclops = NewVehicle("Cyclops");
        registry.AddOrUpdate(cyclops);
        PathBasedChildEntity cyclopsLocker = new("submarine_locker_01", new NitroxId(), null, null, cyclops.Id, []);
        registry.AddOrUpdate(cyclopsLocker);
        InventoryItemEntity item = AddItem(cyclopsLocker, "Quartz");

        Claim(cyclops.Id, item.Id).Outcome.Should().Be(CraftingClaimOutcome.Granted);
    }

    [TestMethod]
    public void ItemsInALockerBuiltInACyclopsAreGranted()
    {
        // A locker built in a Cyclops is a module whose parent is the Cyclops (Builder_TryPlace_Patch.GhostCreated)
        VehicleEntity cyclops = NewVehicle("Cyclops");
        registry.AddOrUpdate(cyclops);
        InventoryItemEntity item = AddItem(AddModule(cyclops));

        Claim(cyclops.Id, item.Id).Outcome.Should().Be(CraftingClaimOutcome.Granted);
    }

    // ---- two players, the same items

    [TestMethod]
    public void TheSecondClaimForTheSameItemFindsItGone()
    {
        InventoryItemEntity last = AddItem(locker);

        CraftingClaimDecision first = Claim(baseEntity.Id, last.Id);
        CraftingClaimDecision second = Claim(baseEntity.Id, last.Id);

        first.Outcome.Should().Be(CraftingClaimOutcome.Granted);
        second.Outcome.Should().Be(CraftingClaimOutcome.Taken);
        second.Unavailable.Should().Equal(last.Id);
        second.Removed.Should().BeEmpty();
    }

    [TestMethod]
    public void AClaimThatOverlapsAnEarlierOneTakesNothingAtAll()
    {
        InventoryItemEntity a = AddItem(locker);
        InventoryItemEntity b = AddItem(locker);
        Claim(baseEntity.Id, b.Id).Outcome.Should().Be(CraftingClaimOutcome.Granted);

        CraftingClaimDecision second = Claim(baseEntity.Id, a.Id, b.Id);

        second.Outcome.Should().Be(CraftingClaimOutcome.Taken);
        second.Unavailable.Should().Equal(b.Id);
        registry.TryGetEntityById(a.Id, out InventoryItemEntity? stillThere).Should().BeTrue("all or nothing: the free item isn't taken");
        stillThere!.ParentId.Should().Be(locker.Id);
    }

    [TestMethod]
    public void TwoClaimsForTheSameLastItemsRunInParallelAndExactlyOneWins()
    {
        for (int round = 0; round < 50; round++)
        {
            Init();
            NitroxId[] items = Enumerable.Range(0, 5).Select(_ => AddItem(locker).Id).ToArray();
            NitroxId[] reversed = Enumerable.Reverse(items).ToArray();

            // Both start at the same moment, name the items in opposite orders and yield between two removals: without the
            // gate each would take some of the items and fail on the rest
            using Barrier start = new(2);
            Entity? Slow(NitroxId id)
            {
                Thread.Yield();
                return Remove(id);
            }
            Task<CraftingClaimDecision> first = Task.Run(() =>
            {
                start.SignalAndWait();
                return arbiter.Claim(alice, 1, baseEntity.Id, items, Slow);
            });
            Task<CraftingClaimDecision> second = Task.Run(() =>
            {
                start.SignalAndWait();
                return arbiter.Claim(bob, 1, baseEntity.Id, reversed, Slow);
            });
            Task.WaitAll(first, second);

            CraftingClaimOutcome[] outcomes = [first.Result.Outcome, second.Result.Outcome];
            outcomes.Count(outcome => outcome == CraftingClaimOutcome.Granted).Should().Be(1, "exactly one of two claims for the same items is granted");
            outcomes.Count(outcome => outcome == CraftingClaimOutcome.Taken).Should().Be(1);
            locker.ChildEntities.Should().BeEmpty();
        }
    }

    [TestMethod]
    public async Task TheDeliveryOfOneClaimIsOutBeforeTheNextClaimIsDecided()
    {
        InventoryItemEntity last = AddItem(locker);
        List<string> events = [];
        int inDelivery = 0;
        int overlaps = 0;

        async Task Deliver(string who, CraftingClaimDecision decision)
        {
            if (Interlocked.Increment(ref inDelivery) > 1)
            {
                Interlocked.Increment(ref overlaps);
            }
            lock (events)
            {
                events.Add($"{who}:{decision.Outcome}:start");
            }
            await Task.Delay(30);
            lock (events)
            {
                events.Add($"{who}:{decision.Outcome}:end");
            }
            Interlocked.Decrement(ref inDelivery);
        }

        Task<CraftingClaimDecision> first = arbiter.ClaimAsync(alice, 1, baseEntity.Id, [last.Id], Remove, decision => Deliver("alice", decision));
        Task<CraftingClaimDecision> second = arbiter.ClaimAsync(bob, 1, baseEntity.Id, [last.Id], Remove, decision => Deliver("bob", decision));
        await Task.WhenAll(first, second);

        overlaps.Should().Be(0, "the second claim isn't decided while the first one is still being delivered");
        events.Should().HaveCount(4);
        events[0].Should().EndWith(":Granted:start");
        events[1].Should().EndWith(":Granted:end");
        events[2].Should().EndWith(":Taken:start");
        events[3].Should().EndWith(":Taken:end");
    }

    [TestMethod]
    public void AClaimForOtherItemsAfterTheRefusalGoesThrough()
    {
        InventoryItemEntity a = AddItem(locker);
        InventoryItemEntity b = AddItem(locker);
        Claim(baseEntity.Id, a.Id).Outcome.Should().Be(CraftingClaimOutcome.Granted);
        Claim(baseEntity.Id, a.Id).Outcome.Should().Be(CraftingClaimOutcome.Taken);

        Claim(baseEntity.Id, b.Id).Outcome.Should().Be(CraftingClaimOutcome.Granted);
    }

    // ---- not claimable

    [TestMethod]
    public void AnItemThatIsNotInTheRegistryIsUnavailable()
    {
        InventoryItemEntity a = AddItem(locker);
        NitroxId unknown = new();

        CraftingClaimDecision decision = Claim(baseEntity.Id, a.Id, unknown);

        decision.Outcome.Should().Be(CraftingClaimOutcome.Taken);
        decision.Unavailable.Should().Equal(unknown);
    }

    [TestMethod]
    public void AnItemInAnotherBaseIsUnavailable()
    {
        BuildEntity otherBase = BuildEntity.MakeEmpty();
        otherBase.Id = new NitroxId();
        registry.AddOrUpdate(otherBase);
        InventoryItemEntity item = AddItem(AddModule(otherBase));

        Claim(baseEntity.Id, item.Id).Outcome.Should().Be(CraftingClaimOutcome.Taken);
        registry.TryGetEntityById(item.Id, out Entity? _).Should().BeTrue();
    }

    [TestMethod]
    public void AnItemThePartnerMovedIntoTheirInventoryIsUnavailable()
    {
        // In the locker when the claiming game looked; the partner picked it up (EntityReparented to their player entity)
        InventoryItemEntity item = AddItem(locker);
        PlayerEntity partner = NewPlayer(baseEntity);
        registry.AddOrUpdate(partner);
        registry.ReparentEntity(item.Id, partner.Id);

        CraftingClaimDecision decision = Claim(baseEntity.Id, item.Id);

        decision.Outcome.Should().Be(CraftingClaimOutcome.Taken);
        registry.TryGetEntityById(item.Id, out Entity? _).Should().BeTrue("a refused claim removes nothing");
    }

    [TestMethod]
    public void AnItemInAPlayersInventoryUnderTheBaseIsNeverClaimable()
    {
        // A player is parented to the base or Cyclops he is in: the walk up from his items reaches the scope through him
        PlayerEntity partner = NewPlayer(baseEntity);
        registry.AddOrUpdate(partner);
        InventoryItemEntity item = AddItem(partner);

        Claim(baseEntity.Id, item.Id).Outcome.Should().Be(CraftingClaimOutcome.Taken);
    }

    [TestMethod]
    public void AnItemInAPlanterIsNotClaimable()
    {
        PlanterEntity planter = new(new NitroxId(), baseEntity.Id);
        registry.AddOrUpdate(planter);
        InventoryItemEntity seed = AddItem(planter, "CreepvineSeed");

        Claim(baseEntity.Id, seed.Id).Outcome.Should().Be(CraftingClaimOutcome.Taken);
    }

    [TestMethod]
    public void AnItemInAVehicleDockedInTheBaseOrTheCyclopsIsNotClaimable()
    {
        VehicleEntity seamoth = NewVehicle("SeaMoth", baseEntity);
        registry.AddOrUpdate(seamoth);
        InventoryItemEntity item = AddItem(seamoth);
        VehicleEntity cyclops = NewVehicle("Cyclops");
        registry.AddOrUpdate(cyclops);
        VehicleEntity exosuit = NewVehicle("Exosuit", cyclops);
        registry.AddOrUpdate(exosuit);
        PathBasedChildEntity exosuitStorage = new("StorageRoot", new NitroxId(), null, null, exosuit.Id, []);
        registry.AddOrUpdate(exosuitStorage);
        InventoryItemEntity inExosuit = AddItem(exosuitStorage);

        Claim(baseEntity.Id, item.Id).Outcome.Should().Be(CraftingClaimOutcome.Taken);
        Claim(cyclops.Id, inExosuit.Id).Outcome.Should().Be(CraftingClaimOutcome.Taken);
    }

    [TestMethod]
    public void TheContentsOfABaseMachineAreNotClaimable()
    {
        // Bioreactors, reactors, water parks and filtration machines are interior pieces (IBaseModule), the scanner room a map room entity
        InteriorPieceEntity bioreactor = InteriorPieceEntity.MakeEmpty();
        bioreactor.Id = new NitroxId();
        bioreactor.ParentId = baseEntity.Id;
        registry.AddOrUpdate(bioreactor);
        InventoryItemEntity fuel = AddItem(bioreactor, "CreepvineSeedCluster");
        MapRoomEntity scannerRoom = new(new NitroxId(), baseEntity.Id, new NitroxInt3(0, 0, 0));
        registry.AddOrUpdate(scannerRoom);
        InventoryItemEntity chip = AddItem(scannerRoom, "MapRoomUpgradeScanRange");
        InventoryItemEntity inLocker = AddItem(locker);

        Claim(baseEntity.Id, fuel.Id).Outcome.Should().Be(CraftingClaimOutcome.Taken);
        Claim(baseEntity.Id, chip.Id).Outcome.Should().Be(CraftingClaimOutcome.Taken);
        Claim(baseEntity.Id, inLocker.Id).Outcome.Should().Be(CraftingClaimOutcome.Granted);
        registry.TryGetEntityById(fuel.Id, out Entity? _).Should().BeTrue();
        registry.TryGetEntityById(chip.Id, out Entity? _).Should().BeTrue();
    }

    [TestMethod]
    [DataRow("Aquarium")]
    [DataRow("Trashcans")]
    [DataRow("LabTrashcan")]
    public void TheContentsOfAModuleThatIsNoLockerAreNotClaimable(string techType)
    {
        ModuleEntity notALocker = AddModule(baseEntity);
        notALocker.TechType = new NitroxTechType(techType);
        InventoryItemEntity item = AddItem(notALocker, "Bladderfish");

        Claim(baseEntity.Id, item.Id).Outcome.Should().Be(CraftingClaimOutcome.Taken);
    }

    [TestMethod]
    public void AnItemInsideAnotherItemIsNotClaimable()
    {
        // A carry-all stored in a locker: vanilla crafting doesn't reach into bags either (ours)
        InventoryItemEntity bag = AddItem(locker, "LuggageBag");
        InventoryItemEntity inBag = AddItem(bag);

        Claim(baseEntity.Id, inBag.Id).Outcome.Should().Be(CraftingClaimOutcome.Taken);
        Claim(baseEntity.Id, bag.Id).Outcome.Should().Be(CraftingClaimOutcome.Granted, "the bag itself lies in the locker");
    }

    [TestMethod]
    public void OnlyItemEntitiesCanBeClaimed()
    {
        Claim(baseEntity.Id, locker.Id).Outcome.Should().Be(CraftingClaimOutcome.Taken);
        registry.TryGetEntityById(locker.Id, out Entity? _).Should().BeTrue();
    }

    [TestMethod]
    public void AParentChainThatNeverReachesTheScopeIsGivenUp()
    {
        // A loop of parents must not hang the server
        InventoryItemEntity a = AddItem(locker);
        InventoryEntity first = new(0, new NitroxId(), new NitroxTechType("None"), null!, null!, []);
        InventoryEntity second = new(0, new NitroxId(), new NitroxTechType("None"), null!, first.Id, []);
        first.ParentId = second.Id;
        registry.AddEntity(first);
        registry.AddEntity(second);
        registry.ReparentEntity(a.Id, first.Id);

        Claim(baseEntity.Id, a.Id).Outcome.Should().Be(CraftingClaimOutcome.Taken);
    }

    // ---- scopes that are not a base or a Cyclops

    [TestMethod]
    public void APlayerAsTheScopeIsInvalidAndHisItemsStay()
    {
        // The walk stops at the scope before it looks at what it passes: a player as the scope must not open his inventory
        PlayerEntity partner = NewPlayer(baseEntity);
        registry.AddOrUpdate(partner);
        InventoryItemEntity item = AddItem(partner);

        Claim(partner.Id, item.Id).Outcome.Should().Be(CraftingClaimOutcome.Invalid);
        registry.TryGetEntityById(item.Id, out Entity? _).Should().BeTrue();
        partner.ChildEntities.Should().Contain(item);
    }

    [TestMethod]
    public void APlanterAVehicleThatIsNoCyclopsOrAModuleAsTheScopeIsInvalid()
    {
        PlanterEntity planter = new(new NitroxId(), baseEntity.Id);
        registry.AddOrUpdate(planter);
        InventoryItemEntity seed = AddItem(planter, "CreepvineSeed");
        VehicleEntity seamoth = NewVehicle("SeaMoth");
        registry.AddOrUpdate(seamoth);
        InventoryItemEntity inSeamoth = AddItem(seamoth);
        InventoryItemEntity inLocker = AddItem(locker);

        Claim(planter.Id, seed.Id).Outcome.Should().Be(CraftingClaimOutcome.Invalid);
        Claim(seamoth.Id, inSeamoth.Id).Outcome.Should().Be(CraftingClaimOutcome.Invalid);
        Claim(locker.Id, inLocker.Id).Outcome.Should().Be(CraftingClaimOutcome.Invalid);
        registry.TryGetEntityById(seed.Id, out Entity? _).Should().BeTrue();
        registry.TryGetEntityById(inSeamoth.Id, out Entity? _).Should().BeTrue();
        registry.TryGetEntityById(inLocker.Id, out Entity? _).Should().BeTrue();
    }

    // ---- requests that make no sense

    [TestMethod]
    public void AnUnknownScopeIsInvalid()
    {
        InventoryItemEntity item = AddItem(locker);

        Claim(new NitroxId(), item.Id).Outcome.Should().Be(CraftingClaimOutcome.Invalid);
        Claim(null, item.Id).Outcome.Should().Be(CraftingClaimOutcome.Invalid);
        registry.TryGetEntityById(item.Id, out Entity? _).Should().BeTrue();
    }

    [TestMethod]
    public void NoItemsTheSameItemTwiceANullIdOrTooManyIsInvalid()
    {
        InventoryItemEntity item = AddItem(locker);
        NitroxId[] tooMany = Enumerable.Range(0, CraftingClaimRequest.MAX_ITEMS + 1).Select(_ => AddItem(secondLocker).Id).ToArray();

        arbiter.Claim(alice, 1, baseEntity.Id, [], Remove).Outcome.Should().Be(CraftingClaimOutcome.Invalid);
        arbiter.Claim(alice, 2, baseEntity.Id, null, Remove).Outcome.Should().Be(CraftingClaimOutcome.Invalid);
        Claim(baseEntity.Id, item.Id, item.Id).Outcome.Should().Be(CraftingClaimOutcome.Invalid);
        arbiter.Claim(alice, 3, baseEntity.Id, [item.Id, null!], Remove).Outcome.Should().Be(CraftingClaimOutcome.Invalid);
        Claim(baseEntity.Id, tooMany).Outcome.Should().Be(CraftingClaimOutcome.Invalid);
        registry.TryGetEntityById(item.Id, out Entity? _).Should().BeTrue();

        Claim(baseEntity.Id, tooMany.Take(CraftingClaimRequest.MAX_ITEMS).ToArray()).Outcome.Should().Be(CraftingClaimOutcome.Granted);
    }

    [TestMethod]
    public void AnOutcomeThatWasNeverFilledInIsNotAGrant()
    {
        default(CraftingClaimOutcome).Should().Be(CraftingClaimOutcome.Unknown);
        CraftingClaimOutcome.Granted.Should().NotBe(default(CraftingClaimOutcome));
    }

    // ---- a removal that fails half way

    [TestMethod]
    public void AnItemThatVanishesBetweenTheCheckAndTheRemovalPutsTheOthersBack()
    {
        InventoryItemEntity a = AddItem(locker);
        InventoryItemEntity b = AddItem(locker);
        InventoryItemEntity c = AddItem(secondLocker);

        CraftingClaimDecision decision = arbiter.Claim(alice, 1, baseEntity.Id, [a.Id, b.Id, c.Id], id => id.Equals(b.Id) ? null : Remove(id));

        decision.Outcome.Should().Be(CraftingClaimOutcome.Taken);
        decision.Unavailable.Should().Equal(b.Id);
        decision.Removed.Should().BeEmpty();
        registry.TryGetEntityById(a.Id, out InventoryItemEntity? aBack).Should().BeTrue("the item removed before is put back");
        aBack!.ParentId.Should().Be(locker.Id);
        locker.ChildEntities.Should().Contain(a, "and back in its locker's list");
        arbiter.Return(alice, 1, new NitroxId()).Should().BeEmpty("a refused claim can't be returned");
    }

    [TestMethod]
    public void ARemovalThatThrowsPutsTheEarlierOnesBackAndTheClaimIsInvalid()
    {
        InventoryItemEntity a = AddItem(locker);
        InventoryItemEntity b = AddItem(locker);

        CraftingClaimDecision decision = arbiter.Claim(alice, 1, baseEntity.Id, [a.Id, b.Id], id => id.Equals(b.Id) ? throw new InvalidOperationException("boom") : Remove(id));

        decision.Outcome.Should().Be(CraftingClaimOutcome.Invalid);
        registry.TryGetEntityById(a.Id, out InventoryItemEntity? aBack).Should().BeTrue();
        aBack!.ParentId.Should().Be(locker.Id);
        locker.ChildEntities.Should().BeEquivalentTo(new[] { a, b });
    }

    [TestMethod]
    public void AnItemThatMovedIntoAnInventoryAfterTheCheckIsNotRemoved()
    {
        InventoryItemEntity a = AddItem(locker);
        InventoryItemEntity b = AddItem(locker);
        PlayerEntity partner = NewPlayer(baseEntity);
        registry.AddOrUpdate(partner);

        CraftingClaimDecision decision = arbiter.Claim(alice, 1, baseEntity.Id, [a.Id, b.Id], id =>
        {
            if (id.Equals(a.Id))
            {
                Entity? removed = Remove(id);
                registry.ReparentEntity(b.Id, partner.Id);
                return removed;
            }
            return Remove(id);
        });

        decision.Outcome.Should().Be(CraftingClaimOutcome.Taken);
        decision.Unavailable.Should().Equal(b.Id);
        registry.TryGetEntityById(b.Id, out InventoryItemEntity? bStill).Should().BeTrue("the item the partner holds now isn't touched");
        bStill!.ParentId.Should().Be(partner.Id);
        registry.TryGetEntityById(a.Id, out InventoryItemEntity? aBack).Should().BeTrue("and the one taken before is put back");
        aBack!.ParentId.Should().Be(locker.Id);
    }

    [TestMethod]
    public void AnItemWithChildrenComesBackWithThem()
    {
        // A tool with a battery: the battery is a child of the item and leaves the registry with it
        InventoryItemEntity tool = AddItem(locker, "Welder");
        InstalledBatteryEntity battery = new(0, new NitroxId(), new NitroxTechType("Battery"), null, tool.Id, []);
        registry.AddOrUpdate(battery);
        InventoryItemEntity other = AddItem(locker);

        arbiter.Claim(alice, 1, baseEntity.Id, [tool.Id, other.Id], id => id.Equals(other.Id) ? null : Remove(id)).Outcome.Should().Be(CraftingClaimOutcome.Taken);

        registry.TryGetEntityById(tool.Id, out InventoryItemEntity? toolBack).Should().BeTrue();
        toolBack!.ParentId.Should().Be(locker.Id);
        locker.ChildEntities.Should().Contain(tool);
        registry.TryGetEntityById(battery.Id, out Entity? batteryBack).Should().BeTrue();
        batteryBack!.ParentId.Should().Be(tool.Id);
    }

    // ---- giving a granted claim back (ours)

    [TestMethod]
    public void AReturnedClaimPutsTheSameItemsBackIntoTheirLockersOnce()
    {
        InventoryItemEntity a = AddItem(locker);
        InventoryItemEntity b = AddItem(secondLocker, "Copper");
        arbiter.Claim(alice, 7, baseEntity.Id, [a.Id, b.Id], Remove).Outcome.Should().Be(CraftingClaimOutcome.Granted);
        NitroxId aliceInventory = new();

        List<Entity> returned = arbiter.Return(alice, 7, aliceInventory);

        returned.Select(entity => entity.Id).Should().Equal(a.Id, b.Id);
        registry.TryGetEntityById(a.Id, out InventoryItemEntity? aBack).Should().BeTrue();
        aBack!.ParentId.Should().Be(locker.Id);
        registry.TryGetEntityById(b.Id, out InventoryItemEntity? bBack).Should().BeTrue();
        bBack!.ParentId.Should().Be(secondLocker.Id);
        locker.ChildEntities.Should().ContainSingle().Which.Should().BeSameAs(a);
        arbiter.Return(alice, 7, aliceInventory).Should().BeEmpty("a claim goes back once");
        locker.ChildEntities.Should().ContainSingle();
    }

    [TestMethod]
    public void OnlyTheClaimantCanReturnAClaim()
    {
        InventoryItemEntity a = AddItem(locker);
        arbiter.Claim(alice, 3, baseEntity.Id, [a.Id], Remove).Outcome.Should().Be(CraftingClaimOutcome.Granted);

        arbiter.Return(bob, 3, new NitroxId()).Should().BeEmpty();
        registry.TryGetEntityById(a.Id, out Entity? _).Should().BeFalse();
        arbiter.Return(alice, 3, new NitroxId()).Should().ContainSingle();
    }

    [TestMethod]
    public void AnItemWhoseLockerIsGoneGoesToTheClaimantsInventory()
    {
        InventoryItemEntity a = AddItem(locker);
        PlayerEntity claimant = NewPlayer(baseEntity);
        registry.AddOrUpdate(claimant);
        arbiter.Claim(alice, 4, baseEntity.Id, [a.Id], Remove).Outcome.Should().Be(CraftingClaimOutcome.Granted);
        registry.RemoveEntity(locker.Id);

        arbiter.Return(alice, 4, claimant.Id).Should().ContainSingle();

        registry.TryGetEntityById(a.Id, out InventoryItemEntity? back).Should().BeTrue();
        back!.ParentId.Should().Be(claimant.Id);
        claimant.ChildEntities.Should().Contain(a);
    }

    [TestMethod]
    public void OnlyTheLatestGrantsAreKeptForAReturn()
    {
        InventoryItemEntity first = AddItem(locker);
        arbiter.Claim(alice, 1, baseEntity.Id, [first.Id], Remove).Outcome.Should().Be(CraftingClaimOutcome.Granted);
        for (int claimId = 2; claimId <= CraftingClaimArbiter.MAX_KEPT_GRANTS + 1; claimId++)
        {
            arbiter.Claim(alice, claimId, baseEntity.Id, [AddItem(locker).Id], Remove).Outcome.Should().Be(CraftingClaimOutcome.Granted);
        }

        arbiter.Return(alice, 1, new NitroxId()).Should().BeEmpty("the oldest grant is forgotten");
        arbiter.Return(alice, CraftingClaimArbiter.MAX_KEPT_GRANTS + 1, new NitroxId()).Should().ContainSingle();
    }
}
