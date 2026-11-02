using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.MonoBehaviours.Cyclops;
using UnityEngine;
using static NitroxClient.GameLogic.LockerCraftingRules;

namespace NitroxClient.GameLogic;

/// <summary>
///     Crafting from base and Cyclops lockers (MULTINAUGHTICA_SPEC M8). The crafting stations of a base (fabricator,
///     modification station, the moonpool's vehicle upgrade console: all <see cref="GhostCrafter" />s) and the Cyclops
///     fabricator also use the items in the lockers of the base or Cyclops they belong to:
///     <list type="bullet">
///         <item>
///             While such a station's menu is open, the counts the menu works with include the lockers:
///             <c>CrafterLogic.IsCraftRecipeFulfilled</c> (which recipes light up) and <c>TooltipFactory.WriteIngredients</c>
///             (the tooltip counts) ask <see cref="GetCount" /> instead of <c>Inventory.GetPickupCount</c> (transpilers).
///         </item>
///         <item>
///             A craft spends the inventory first and the lockers for the rest (<see cref="CreatePlan{TKey}" />). When
///             the lockers are needed the craft waits: the exact items are claimed at the server
///             (<see cref="CraftingClaimRequest" />), which removes them for everyone, and only a granted answer starts the
///             craft (<see cref="OnResponse" />). A refused claim is tried once more with other items, else cancelled with a
///             message.
///         </item>
///         <item>
///             If a granted craft can't start anymore (the station got busy or lost power, the inventory lost its share,
///             the answer came after the timeout), the claim is given back (<see cref="CraftingClaimReturn" />): the server
///             puts the same items back into their lockers.
///         </item>
///         <item>
///             Whenever the vanilla craft runs without a claim (the inventory pays, or the station has no power), the
///             lockers don't count (<see cref="inventoryOnly" />): vanilla would spend nothing for the locker part.
///         </item>
///     </list>
///     Off when the server has <c>CraftFromLockers</c> off, when the game needs no ingredients (creative, the nocost
///     cheat; survival, freedom and hardcore need them), and for stations outside a base or Cyclops (the escape pod).
///     Ported from MultiNaughtica (Alexander1170/MultiNaughtica @ 99428c8), adapted.
/// </summary>
public sealed class LockerCrafting
{
    private readonly IPacketSender packetSender;
    private readonly LocalPlayer localPlayer;

    /// <summary>Granted claims given up after <see cref="ANSWER_TIMEOUT" />, kept so that a late grant is given back.</summary>
    private readonly HashSet<int> abandonedClaimIds = [];

    private readonly List<ItemsContainer> subscribedContainers = [];

    /// <summary>The craft whose claim awaits its answer. One at a time: the player crafts one thing at a time.</summary>
    private Claim pending;

    /// <summary>The claim whose granted craft is starting right now: <see cref="OnConsumeResources" /> spends only its inventory share.</summary>
    private Claim granting;

    /// <summary>
    ///     Set while the vanilla <c>GhostCrafter.Craft</c> of a station in a base or Cyclops runs without a claim, so that
    ///     <c>CrafterLogic.ConsumeResources</c> doesn't decide on counts it can't pay for. Reset by <see cref="OnCraftFinished" />.
    /// </summary>
    private bool inventoryOnly;

    /// <summary>The server answered <see cref="CraftingClaimOutcome.Disabled" />: it is the truth, whatever the join said.</summary>
    private bool refusedByServer;

    private int nextClaimId = 1;
    private int scopeFrame = -1;
    private Crafter scopeCrafter;
    private LockerScope scopeCache;
    private uGUI_CraftingMenu subscribedMenu;

    /// <summary>The recipe node clicked in the menu, and the frame: <c>uGUI_CraftingMenu.Action</c> calls <c>PerformAction</c> in the same frame.</summary>
    private uGUI_CraftingMenu.Node actionNode;

    private int actionFrame = -1;

    public LockerCrafting(IPacketSender packetSender, LocalPlayer localPlayer)
    {
        this.packetSender = packetSender;
        this.localPlayer = localPlayer;
    }

    /// <summary>The server option <c>CraftFromLockers</c>, as told at join.</summary>
    public bool Enabled => localPlayer.CoopSettings is { CraftFromLockers: true } && !refusedByServer;

    /// <summary>
    ///     The lockers of the base or Cyclops a station belongs to, or null when crafting from lockers doesn't apply to it.
    /// </summary>
    public LockerScope GetScope(Crafter crafter)
    {
        if (!Enabled || !crafter || crafter is not GhostCrafter || !GameModeUtils.RequiresIngredients())
        {
            return null;
        }

        // The menu asks for every recipe in a frame: the lockers are looked up once per frame
        if (scopeFrame == Time.frameCount && scopeCrafter == crafter)
        {
            return scopeCache;
        }

        LockerScope scope = null;
        SubRoot subRoot = crafter.GetComponentInParent<SubRoot>();
        if (subRoot && !subRoot.GetComponentInParent<VirtualCyclops>())
        {
            scope = BuildScope(subRoot);
        }

        scopeFrame = Time.frameCount;
        scopeCrafter = crafter;
        scopeCache = scope;
        return scope;
    }

    /// <remarks>
    ///     Not lockers: the containers of planters (also inside a water park), aquariums, trash cans, filtration machines and
    ///     scanner rooms (each names its <see cref="StorageContainer" /> in a field, and it may sit next to it rather than
    ///     below it), a docked vehicle's storage (the Prawn Suit's is a <see cref="StorageContainer" />), and a container
    ///     inside an item (a carry-all in a locker). Bioreactors, reactors and the Cyclops decoy tube have no
    ///     <see cref="StorageContainer" />. Checked in game 83031.
    /// </remarks>
    private static LockerScope BuildScope(SubRoot subRoot)
    {
        if (!subRoot.TryGetNitroxId(out NitroxId scopeId) && !(subRoot is BaseRoot baseRoot && baseRoot.baseComp && baseRoot.baseComp.TryGetNitroxId(out scopeId)))
        {
            return null;
        }

        HashSet<StorageContainer> notLockers = [];
        foreach (Planter planter in subRoot.GetComponentsInChildren<Planter>(true))
        {
            notLockers.Add(planter.storageContainer);
        }
        foreach (Aquarium aquarium in subRoot.GetComponentsInChildren<Aquarium>(true))
        {
            notLockers.Add(aquarium.storageContainer);
        }
        foreach (Trashcan trashcan in subRoot.GetComponentsInChildren<Trashcan>(true))
        {
            notLockers.Add(trashcan.storageContainer);
        }
        foreach (FiltrationMachine machine in subRoot.GetComponentsInChildren<FiltrationMachine>(true))
        {
            notLockers.Add(machine.storageContainer);
        }
        foreach (MapRoomFunctionality mapRoom in subRoot.GetComponentsInChildren<MapRoomFunctionality>(true))
        {
            notLockers.Add(mapRoom.storageContainer);
        }

        LockerScope scope = new(scopeId);
        foreach (StorageContainer storage in subRoot.GetComponentsInChildren<StorageContainer>(true))
        {
            if (storage && storage.container != null && !notLockers.Contains(storage) && !storage.GetComponentInParent<Planter>(true) && !storage.GetComponentInParent<Vehicle>(true) &&
                !storage.GetComponentInParent<Pickupable>(true))
            {
                scope.Add(storage.TryGetNitroxId(out NitroxId storageId) ? storageId.ToString() : string.Empty, storage.container);
            }
        }
        return scope;
    }

    /// <summary>The station whose menu is open, if crafting from lockers applies to it.</summary>
    private LockerScope GetOpenMenuScope()
    {
        uGUI_CraftingMenu menu = uGUI.main ? uGUI.main.craftingMenu : null;
        GhostCrafter crafter = menu ? menu.client as GhostCrafter : null;
        return crafter ? GetScope(crafter) : null;
    }

    /// <summary>
    ///     Replaces <c>Inventory.GetPickupCount</c> in <c>CrafterLogic.IsCraftRecipeFulfilled</c> and
    ///     <c>TooltipFactory.WriteIngredients</c>: the inventory's count, plus the lockers' while the menu of a station in a
    ///     base or Cyclops is open.
    /// </summary>
    public int GetCount(Inventory inventory, TechType techType)
    {
        int inventoryCount = inventory.GetPickupCount(techType);
        if (inventoryOnly)
        {
            return inventoryCount;
        }

        LockerScope scope = GetOpenMenuScope();
        return scope == null ? inventoryCount : CombinedCount(inventoryCount, scope.CountOf(techType));
    }

    /// <summary><c>uGUI_CraftingMenu.Action</c> (prefix): a recipe was clicked. <c>Crafter.PerformAction</c> follows in the same frame.</summary>
    public void OnMenuAction(uGUI_CraftingMenu.Node sender)
    {
        actionNode = sender;
        actionFrame = Time.frameCount;
    }

    /// <summary>
    ///     <c>GhostCrafter.Craft</c> was called. True when the vanilla method is to run; false when the craft waits for the
    ///     answer to a claim or can't be done. Vanilla also runs for the craft a granted answer starts (<see cref="granting" />).
    /// </summary>
    public bool OnCraft(GhostCrafter crafter, TechType techType, float duration)
    {
        if (granting != null)
        {
            return true;
        }

        LockerScope scope = GetScope(crafter);
        if (scope == null)
        {
            return true;
        }

        // From here vanilla runs or not at all, and its ConsumeResources must not count the lockers
        inventoryOnly = true;

        if (pending != null)
        {
            if (pending.Crafter && !HasTimedOut(Time.time - pending.RequestTime))
            {
                // A second click while the answer is on its way
                return false;
            }
            Abandon(pending);
        }

        ReadOnlyCollection<Ingredient> ingredients = TechData.GetIngredients(techType);
        if (ingredients == null || ingredients.Count == 0)
        {
            return true;
        }

        // The menu counted the lockers, so vanilla must not run unless the inventory is enough: it would spend nothing for the locker part
        CraftPlan<TechType> plan = PlanFor(scope, ingredients, null);
        if (!plan.IsComplete)
        {
            ErrorMessage.AddWarning(Language.main.Get("DontHaveNeededIngredients"));
            return false;
        }
        if (!plan.NeedsLockers)
        {
            return true;
        }

        // What Crafter.PerformAction checks before and GhostCrafter.Craft after the ingredients, so a craft that can't start claims nothing
        if (!crafter.logic || crafter.logic.inProgress || crafter.HasCraftedItem())
        {
            return false;
        }
        if (!crafter.HasEnoughPower())
        {
            // Vanilla takes the power first and stops there
            return true;
        }

        Claim claim = new()
        {
            Attempt = 1,
            Crafter = crafter,
            TechType = techType,
            Duration = duration,
            Node = actionFrame == Time.frameCount ? actionNode : null
        };
        SendClaim(claim, scope, plan);
        return false;
    }

    /// <summary><c>GhostCrafter.Craft</c> ended (finalizer): the lockers count again for the menu.</summary>
    public void OnCraftFinished()
    {
        inventoryOnly = false;
    }

    private static CraftPlan<TechType> PlanFor(LockerScope scope, IEnumerable<Ingredient> ingredients, ICollection<string> excluded)
    {
        List<IngredientLine<TechType>> lines = [];
        foreach (Ingredient ingredient in ingredients)
        {
            lines.Add(new IngredientLine<TechType>(ingredient.techType, ingredient.amount));
        }
        Inventory inventory = Inventory.main;
        return CreatePlan(lines, techType => inventory.GetPickupCount(techType), scope.ItemsOf, excluded);
    }

    private void SendClaim(Claim claim, LockerScope scope, CraftPlan<TechType> plan)
    {
        List<NitroxId> itemIds = [];
        foreach (PickedItem<TechType> picked in plan.FromLockers)
        {
            itemIds.Add(scope.IdOf(picked.Item.ItemId));
        }

        claim.Id = nextClaimId++;
        claim.Plan = plan;
        claim.RequestTime = Time.time;
        pending = claim;
        Log.Info($"[CraftFromLockers] Claiming {itemIds.Count} items for {claim.TechType} (try {claim.Attempt}, claim {claim.Id}); {plan.FromInventory.Count} ingredients come from the inventory");
        if (!packetSender.Send(new CraftingClaimRequest(claim.Id, scope.ScopeId, itemIds)))
        {
            pending = null;
            Log.InGame(Language.main.Get("Nitrox_CraftFromLockersFailed"));
        }
    }

    /// <summary>The server's answer to a claim. It comes after the <see cref="EntityDestroyed" /> packets of a granted claim.</summary>
    public void OnResponse(CraftingClaimResponse response)
    {
        Claim claim = pending;
        if (claim == null || claim.Id != response.ClaimId)
        {
            // Given up because the answer was late: a granted claim took the items, so they go back
            if (abandonedClaimIds.Remove(response.ClaimId) && response.Outcome == CraftingClaimOutcome.Granted)
            {
                GiveBack(response.ClaimId);
            }
            return;
        }
        pending = null;
        if (response.Outcome == CraftingClaimOutcome.Disabled)
        {
            refusedByServer = true;
        }

        bool replanComplete = false;
        LockerScope scope = null;
        ReadOnlyCollection<Ingredient> ingredients = null;
        if (response.Outcome == CraftingClaimOutcome.Taken)
        {
            foreach (NitroxId unavailable in response.UnavailableIds)
            {
                claim.Unavailable.Add(unavailable.ToString());
            }
            scope = claim.Crafter ? GetScope(claim.Crafter) : null;
            ingredients = TechData.GetIngredients(claim.TechType);
            replanComplete = scope != null && ingredients != null && PlanFor(scope, ingredients, claim.Unavailable).IsComplete;
        }

        ClaimStep step = NextStep(response.Outcome, claim.Attempt, replanComplete);
        Log.Info($"[CraftFromLockers] Claim {claim.Id} for {claim.TechType}: {response.Outcome}, {step}");
        switch (step)
        {
            case ClaimStep.Craft:
                claim.Granted = true;
                Craft(claim);
                break;
            case ClaimStep.Retry:
                claim.Attempt++;
                CraftPlan<TechType> retryPlan = PlanFor(scope, ingredients, claim.Unavailable);
                if (retryPlan.NeedsLockers)
                {
                    SendClaim(claim, scope, retryPlan);
                }
                else
                {
                    // The inventory pays for it all by now (the player picked up what was missing meanwhile): nothing to claim
                    claim.Plan = retryPlan;
                    Craft(claim);
                }
                break;
            default:
                Log.InGame(Language.main.Get(response.Outcome switch
                {
                    CraftingClaimOutcome.Taken => "Nitrox_CraftFromLockersTaken",
                    CraftingClaimOutcome.Disabled => "Nitrox_CraftFromLockersDisabled",
                    _ => "Nitrox_CraftFromLockersFailed"
                }));
                break;
        }
    }

    /// <summary>
    ///     Starts the craft the way the menu's click would have. The vanilla <c>GhostCrafter.Craft</c> takes the power,
    ///     <see cref="OnConsumeResources" /> spends the inventory's share (the lockers' share is gone already), and the station
    ///     starts; <c>GhostCrafter_OnCraftingBegin_Patch</c> then sends the crafter state and takes the crafter's lock as for
    ///     any craft (COOP_FEATURES_SPEC B2).
    /// </summary>
    private void Craft(Claim claim)
    {
        bool inventoryHoldsItsShare = true;
        foreach (KeyValuePair<TechType, int> share in claim.Plan.FromInventory)
        {
            inventoryHoldsItsShare &= Inventory.main && Inventory.main.GetPickupCount(share.Key) >= share.Value;
        }

        // The answer can come after the partner started a craft at the same station (its CrafterMetadata came first): starting one would overwrite it
        bool started = false;
        CrafterLogic logic = claim.Crafter ? claim.Crafter.logic : null;
        if (inventoryHoldsItsShare && logic && !logic.inProgress && !claim.Crafter.HasCraftedItem())
        {
            granting = claim;
            try
            {
                claim.Crafter.Craft(claim.TechType, claim.Duration);
            }
            catch (Exception exception)
            {
                // The items are gone from the lockers: whatever went wrong, the check below decides about giving them back
                Log.Error(exception, $"[CraftFromLockers] The craft of {claim.TechType} threw after its claim was granted");
            }
            finally
            {
                granting = null;
            }
            started = logic && logic.inProgress && logic.craftingTechType == claim.TechType;
        }

        if (ShouldReturnClaimedItems(inventoryHoldsItsShare, started))
        {
            Log.Warn($"[CraftFromLockers] The craft of {claim.TechType} didn't start after claim {claim.Id} (inventory share held: {inventoryHoldsItsShare})");
            // A retry the inventory paid for alone claimed nothing
            if (claim.Granted)
            {
                GiveBack(claim.Id);
            }
            return;
        }

        RestoreMenuLock(claim);
    }

    private void GiveBack(int claimId)
    {
        Log.Info($"[CraftFromLockers] Giving claim {claimId} back");
        packetSender.Send(new CraftingClaimReturn(claimId));
        Log.InGame(Language.main.Get("Nitrox_CraftFromLockersReturned"));
    }

    /// <summary>
    ///     The menu's click sets the menu up for a craft that started (<c>uGUI_CraftingMenu.Action</c>: it locks the menu and
    ///     remembers the node, which shows the progress and unlocks it when the craft is done). This craft started after the
    ///     click, so that is done here. <c>Crafter.OnCraftingBegin</c> has already locked or closed the menu.
    /// </summary>
    private static void RestoreMenuLock(Claim claim)
    {
        uGUI_CraftingMenu menu = uGUI.main ? uGUI.main.craftingMenu : null;
        if (!menu || claim.Node == null || !ReferenceEquals(menu.client, claim.Crafter))
        {
            return;
        }

        if (menu.tree != null)
        {
            menu.SetLocked(true);
        }
        menu.craftedNode = claim.Node;
    }

    /// <summary>
    ///     <c>CrafterLogic.ConsumeResources</c> (prefix). For the craft a granted claim starts, only the inventory's share is
    ///     spent (the server removed the lockers' items). Returns false (vanilla must not run) for that craft.
    /// </summary>
    public bool OnConsumeResources(TechType techType, ref bool result)
    {
        Claim claim = granting;
        if (claim == null || claim.TechType != techType)
        {
            return true;
        }

        List<IngredientLine<TechType>> lines = [];
        foreach (Ingredient ingredient in TechData.GetIngredients(techType))
        {
            lines.Add(new IngredientLine<TechType>(ingredient.techType, ingredient.amount));
        }

        // Like Inventory.ConsumeResourcesForRecipe: ItemsContainer_DestroyItem_Patch tells the server about each removed item
        foreach (IngredientLine<TechType> line in MergeLines(lines))
        {
            int fromInventory = claim.Plan.InventoryShare(line.Key);
            for (int i = 0; i < line.Amount; i++)
            {
                if (i < fromInventory && !Inventory.main.DestroyItem(line.Key, true))
                {
                    Log.Error($"[CraftFromLockers] Unable to remove one {line.Key} from the inventory to craft {techType}");
                }
                uGUI_IconNotifier.main.Play(line.Key, uGUI_IconNotifier.AnimationType.To, null);
            }
        }

        result = true;
        return false;
    }

    private void Abandon(Claim claim)
    {
        Log.Warn($"[CraftFromLockers] No answer to claim {claim.Id} for {claim.TechType} after {Time.time - claim.RequestTime:F0} s, giving it up");
        if (abandonedClaimIds.Count > 8)
        {
            abandonedClaimIds.Clear();
        }
        abandonedClaimIds.Add(claim.Id);
        pending = null;
    }

    /// <summary>
    ///     Lets the open menu notice when a locker's items change (the partner takes or stores items): the menu only listens
    ///     to the inventory.
    /// </summary>
    public void OnMenuOpened(uGUI_CraftingMenu menu)
    {
        OnMenuClosed();
        LockerScope scope = GetOpenMenuScope();
        if (scope == null)
        {
            return;
        }

        subscribedMenu = menu;
        foreach (LockerSlot locker in scope.Lockers)
        {
            locker.Container.onAddItem += menu.InventoryChanged;
            locker.Container.onRemoveItem += menu.InventoryChanged;
            subscribedContainers.Add(locker.Container);
        }
    }

    /// <summary>
    ///     The menu was deselected, the end of every use of it (<c>uGUI_CraftingMenu.Close</c> is called by any crafter and
    ///     does nothing unless it is the menu's own).
    /// </summary>
    public void OnMenuClosed()
    {
        if (subscribedMenu)
        {
            foreach (ItemsContainer container in subscribedContainers)
            {
                container.onAddItem -= subscribedMenu.InventoryChanged;
                container.onRemoveItem -= subscribedMenu.InventoryChanged;
            }
        }
        subscribedContainers.Clear();
        subscribedMenu = null;
    }

    public sealed class LockerScope
    {
        private readonly List<LockerSlot> lockers = [];
        private readonly Dictionary<string, NitroxId> itemIdsByKey = [];

        public LockerScope(NitroxId scopeId)
        {
            ScopeId = scopeId;
        }

        /// <summary>The base (its <c>Base</c>'s id) or the Cyclops (its vehicle id): what the server looks the items up under.</summary>
        public NitroxId ScopeId { get; }

        public IReadOnlyList<LockerSlot> Lockers => lockers;

        public void Add(string containerId, ItemsContainer container)
        {
            lockers.Add(new LockerSlot(containerId, container));
        }

        /// <summary>The items of a tech type in the lockers that can be claimed (one without an id can't).</summary>
        public IReadOnlyList<LockerItemRef> ItemsOf(TechType techType)
        {
            List<LockerItemRef> items = [];
            foreach (LockerSlot locker in lockers)
            {
                if (locker.Container.GetCount(techType) <= 0)
                {
                    continue;
                }
                IList<InventoryItem> inContainer = locker.Container.GetItems(techType);
                if (inContainer == null)
                {
                    continue;
                }
                foreach (InventoryItem item in inContainer)
                {
                    Pickupable pickupable = item != null ? item.item : null;
                    if (pickupable && !pickupable.isDestroyed && pickupable.TryGetNitroxId(out NitroxId itemId))
                    {
                        string key = itemId.ToString();
                        itemIdsByKey[key] = itemId;
                        items.Add(new LockerItemRef(locker.Id, key));
                    }
                }
            }
            return items;
        }

        public int CountOf(TechType techType) => ItemsOf(techType).Count;

        public NitroxId IdOf(string itemKey) => itemIdsByKey.TryGetValue(itemKey, out NitroxId id) ? id : null;
    }

    public readonly struct LockerSlot
    {
        public LockerSlot(string id, ItemsContainer container)
        {
            Id = id ?? string.Empty;
            Container = container;
        }

        public string Id { get; }
        public ItemsContainer Container { get; }
    }

    private sealed class Claim
    {
        public int Id;
        public int Attempt;
        public bool Granted;
        public GhostCrafter Crafter;
        public TechType TechType;
        public float Duration;
        public CraftPlan<TechType> Plan;
        public float RequestTime;

        /// <summary>The clicked recipe node of the open menu; null when the craft didn't come from the menu.</summary>
        public uGUI_CraftingMenu.Node Node;

        public readonly HashSet<string> Unavailable = [];
    }
}
