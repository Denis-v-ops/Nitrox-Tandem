using Nitrox.Model.Subnautica.Packets;
using static NitroxClient.GameLogic.LockerCraftingRules;

namespace NitroxClient.GameLogic;

/// <summary>
///     MULTINAUGHTICA_SPEC M8, crafting from base and Cyclops lockers: what a craft counts, where it spends from, in which
///     order, and what the server's answer means for it. Ported from MultiNaughtica (Alexander1170/MultiNaughtica @ 99428c8).
/// </summary>
[TestClass]
public class LockerCraftingRulesTest
{
    private static IngredientLine<string> Line(string key, int amount) => new(key, amount);

    private static LockerItemRef Item(string container, string item) => new(container, item);

    /// <summary>An inventory and lockers as lookups: what the game would answer.</summary>
    private static CraftPlan<string> Plan(IEnumerable<IngredientLine<string>> recipe, Dictionary<string, int>? inventory = null, Dictionary<string, LockerItemRef[]>? lockers = null, params string[] excluded)
    {
        inventory ??= [];
        lockers ??= [];
        return CreatePlan(recipe, key => inventory.GetValueOrDefault(key), key => lockers.GetValueOrDefault(key), excluded);
    }

    // ---- counting

    [TestMethod]
    public void TheCountIsTheInventoryAndTheLockersTogether()
    {
        CombinedCount(2, 3).Should().Be(5);
        CombinedCount(0, 4).Should().Be(4);
        CombinedCount(4, 0).Should().Be(4);
        CombinedCount(0, 0).Should().Be(0);
    }

    [TestMethod]
    public void ACountBelowZeroIsNothing()
    {
        CombinedCount(-3, 2).Should().Be(2);
        CombinedCount(2, -3).Should().Be(2);
        CombinedCount(-1, -1).Should().Be(0);
    }

    [TestMethod]
    public void TheCountDoesNotOverflow()
    {
        CombinedCount(int.MaxValue, 1).Should().Be(int.MaxValue);
        CombinedCount(int.MaxValue, int.MaxValue).Should().Be(int.MaxValue);
    }

    [TestMethod]
    public void AnIngredientListedTwiceIsOneLine()
    {
        List<IngredientLine<string>> merged = MergeLines([Line("Titanium", 2), Line("Copper", 1), Line("Titanium", 3)]);

        merged.Select(line => (line.Key, line.Amount)).Should().Equal(("Titanium", 5), ("Copper", 1));
    }

    [TestMethod]
    public void LinesWithoutAnAmountAreDropped()
    {
        MergeLines([Line("Titanium", 0), Line("Copper", -2), Line("Lead", 1)]).Select(line => line.Key).Should().Equal("Lead");
    }

    [TestMethod]
    public void MergedAmountsDoNotOverflow()
    {
        MergeLines([Line("Titanium", int.MaxValue), Line("Titanium", 5)]).Single().Amount.Should().Be(int.MaxValue);
    }

    // ---- where the ingredients come from

    [TestMethod]
    public void WhenTheInventoryIsEnoughTheLockersAreNotTouched()
    {
        CraftPlan<string> plan = Plan([Line("Titanium", 2)], new() { ["Titanium"] = 4 }, new() { ["Titanium"] = [Item("c1", "i1"), Item("c1", "i2")] });

        plan.IsComplete.Should().BeTrue();
        plan.NeedsLockers.Should().BeFalse();
        plan.FromLockers.Should().BeEmpty();
        plan.InventoryShare("Titanium").Should().Be(2, "no more than the recipe needs");
    }

    [TestMethod]
    public void TheInventoryIsSpentFirstAndTheLockersGiveTheRest()
    {
        CraftPlan<string> plan = Plan([Line("Titanium", 5)], new() { ["Titanium"] = 2 }, new() { ["Titanium"] = [Item("c1", "i1"), Item("c1", "i2"), Item("c1", "i3"), Item("c1", "i4")] });

        plan.IsComplete.Should().BeTrue();
        plan.NeedsLockers.Should().BeTrue();
        plan.InventoryShare("Titanium").Should().Be(2);
        plan.FromLockers.Select(picked => picked.Item.ItemId).Should().Equal("i1", "i2", "i3");
    }

    [TestMethod]
    public void WithAnEmptyInventoryEverythingComesFromTheLockers()
    {
        CraftPlan<string> plan = Plan([Line("Titanium", 2), Line("Copper", 1)], null, new() { ["Titanium"] = [Item("c1", "t1"), Item("c2", "t2")], ["Copper"] = [Item("c1", "k1")] });

        plan.IsComplete.Should().BeTrue();
        plan.FromInventory.Should().BeEmpty();
        plan.FromLockers.Select(picked => (picked.Key, picked.Item.ItemId)).Should().Equal(("Titanium", "t1"), ("Titanium", "t2"), ("Copper", "k1"));
    }

    [TestMethod]
    public void EveryIngredientIsPlannedOnItsOwn()
    {
        // titanium from the inventory alone, copper from both, lead from the lockers alone
        CraftPlan<string> plan = Plan([Line("Titanium", 1), Line("Copper", 2), Line("Lead", 1)], new() { ["Titanium"] = 3, ["Copper"] = 1 },
                                      new() { ["Titanium"] = [Item("c1", "t1")], ["Copper"] = [Item("c1", "k1"), Item("c1", "k2")], ["Lead"] = [Item("c2", "l1")] });

        plan.InventoryShare("Titanium").Should().Be(1);
        plan.InventoryShare("Copper").Should().Be(1);
        plan.InventoryShare("Lead").Should().Be(0);
        plan.FromLockers.Select(picked => picked.Item.ItemId).Should().Equal("k1", "l1");
    }

    [TestMethod]
    public void WhatIsMissingEvenWithTheLockersIsListed()
    {
        CraftPlan<string> plan = Plan([Line("Titanium", 5), Line("Copper", 1)], new() { ["Titanium"] = 1 }, new() { ["Titanium"] = [Item("c1", "t1"), Item("c1", "t2")], ["Copper"] = [Item("c1", "k1")] });

        plan.IsComplete.Should().BeFalse();
        plan.Missing.Select(pair => (pair.Key, pair.Value)).Should().Equal(("Titanium", 2));
    }

    [TestMethod]
    public void AnIngredientThatIsNowhereIsMissingWhole()
    {
        CraftPlan<string> plan = Plan([Line("Titanium", 2)]);

        plan.IsComplete.Should().BeFalse();
        plan.Missing.Select(pair => (pair.Key, pair.Value)).Should().Equal(("Titanium", 2));
        plan.NeedsLockers.Should().BeFalse();
    }

    [TestMethod]
    public void ARecipeWithoutIngredientsIsComplete()
    {
        CraftPlan<string> plan = Plan([]);

        plan.IsComplete.Should().BeTrue();
        plan.NeedsLockers.Should().BeFalse();
    }

    [TestMethod]
    public void ALookupThatAnswersNullIsAnEmptyLocker()
    {
        CraftPlan<string> plan = CreatePlan([Line("Titanium", 1)], _ => 0, _ => null!);

        plan.IsComplete.Should().BeFalse();
    }

    [TestMethod]
    public void ANegativeInventoryCountIsNothing()
    {
        CraftPlan<string> plan = CreatePlan([Line("Titanium", 1)], _ => -5, _ => [Item("c1", "t1")]);

        plan.IsComplete.Should().BeTrue();
        plan.InventoryShare("Titanium").Should().Be(0);
        plan.FromLockers.Should().ContainSingle();
    }

    // ---- which items

    [TestMethod]
    public void TheItemsAreTakenByContainerAndThenByItemId()
    {
        LockerItemRef[] inAnyOrder = [Item("locker-b", "i2"), Item("locker-a", "i9"), Item("locker-b", "i1"), Item("locker-a", "i3")];

        CraftPlan<string> plan = Plan([Line("Titanium", 3)], null, new() { ["Titanium"] = inAnyOrder });

        plan.FromLockers.Select(picked => (picked.Item.ContainerId, picked.Item.ItemId)).Should().Equal(("locker-a", "i3"), ("locker-a", "i9"), ("locker-b", "i1"));
    }

    [TestMethod]
    public void TheOrderIsOrdinalNotCultureSensitive()
    {
        LockerItemRef[] items = [Item("c", "b"), Item("c", "B"), Item("c", "a"), Item("c", "A")];

        CraftPlan<string> plan = Plan([Line("Titanium", 4)], null, new() { ["Titanium"] = items });

        plan.FromLockers.Select(picked => picked.Item.ItemId).Should().Equal("A", "B", "a", "b");
    }

    [TestMethod]
    public void TwoClientsThatSeeTheSameLockersPickTheSameItems()
    {
        LockerItemRef[] aliceSees = [Item("c1", "i3"), Item("c1", "i1"), Item("c2", "i2"), Item("c1", "i4")];
        LockerItemRef[] bobSees = Enumerable.Reverse(aliceSees).ToArray();

        CraftPlan<string> alice = Plan([Line("Titanium", 2)], null, new() { ["Titanium"] = aliceSees });
        CraftPlan<string> bob = Plan([Line("Titanium", 2)], null, new() { ["Titanium"] = bobSees });

        bob.FromLockers.Select(picked => picked.Item.ItemId).Should().Equal(alice.FromLockers.Select(picked => picked.Item.ItemId), "that is what makes the claims compete for the same items");
    }

    [TestMethod]
    public void AnItemTheServerSaidIsGoneIsSkipped()
    {
        LockerItemRef[] items = [Item("c1", "i1"), Item("c1", "i2"), Item("c1", "i3")];

        CraftPlan<string> plan = Plan([Line("Titanium", 2)], null, new() { ["Titanium"] = items }, "i1");

        plan.IsComplete.Should().BeTrue();
        plan.FromLockers.Select(picked => picked.Item.ItemId).Should().Equal("i2", "i3");
    }

    [TestMethod]
    public void WhenTheOnlyEnoughItemsAreGoneThePlanIsIncomplete()
    {
        LockerItemRef[] items = [Item("c1", "i1"), Item("c1", "i2")];

        CraftPlan<string> plan = Plan([Line("Titanium", 2)], null, new() { ["Titanium"] = items }, "i2");

        plan.IsComplete.Should().BeFalse();
        plan.Missing.Single().Value.Should().Be(1);
    }

    [TestMethod]
    public void TheSameItemIsNeverPickedTwice()
    {
        // two ingredients whose lookups both list the same item (a lookup that is not exact about the tech type)
        LockerItemRef shared = Item("c1", "i1");

        CraftPlan<string> plan = Plan([Line("Titanium", 1), Line("Copper", 1)], null, new() { ["Titanium"] = [shared], ["Copper"] = [shared, Item("c1", "i2")] });

        plan.FromLockers.Select(picked => picked.Item.ItemId).Should().Equal("i1", "i2");
    }

    [TestMethod]
    public void AnItemThatALookupListsTwiceIsOneItem()
    {
        // two picks of the same item would be a claim with the same item twice, which the server refuses as invalid
        LockerItemRef twice = Item("c1", "i1");

        CraftPlan<string> plan = Plan([Line("Titanium", 2)], null, new() { ["Titanium"] = [twice, twice] });

        plan.FromLockers.Select(picked => picked.Item.ItemId).Should().Equal("i1");
        plan.IsComplete.Should().BeFalse();
        plan.Missing.Single().Value.Should().Be(1);
    }

    [TestMethod]
    public void AnItemWithoutAnIdCannotBeClaimedAndIsSkipped()
    {
        CraftPlan<string> plan = Plan([Line("Titanium", 1)], null, new() { ["Titanium"] = [Item("c1", ""), new LockerItemRef(null!, null!)] });

        plan.IsComplete.Should().BeFalse();
    }

    [TestMethod]
    public void ADuplicatedLineAsksForTheSumOnce()
    {
        CraftPlan<string> plan = Plan([Line("Titanium", 1), Line("Titanium", 2)], new() { ["Titanium"] = 1 }, new() { ["Titanium"] = [Item("c1", "i1"), Item("c1", "i2"), Item("c1", "i3")] });

        plan.InventoryShare("Titanium").Should().Be(1);
        plan.FromLockers.Should().HaveCount(2);
    }

    [TestMethod]
    public void ARecipeThatNeedsMoreThanAClaimCanHoldIsIncomplete()
    {
        LockerItemRef[] plenty = Enumerable.Range(0, CraftingClaimRequest.MAX_ITEMS + 1).Select(i => Item("c1", $"i{i:D3}")).ToArray();

        CraftPlan<string> atTheLimit = Plan([Line("Titanium", CraftingClaimRequest.MAX_ITEMS)], null, new() { ["Titanium"] = plenty });
        CraftPlan<string> overTheLimit = Plan([Line("Titanium", CraftingClaimRequest.MAX_ITEMS + 1)], null, new() { ["Titanium"] = plenty });

        atTheLimit.IsComplete.Should().BeTrue();
        overTheLimit.IsComplete.Should().BeFalse("the server would refuse the claim as invalid");
    }

    // ---- what the answer means

    [TestMethod]
    public void AGrantedAnswerStartsTheCraft()
    {
        NextStep(CraftingClaimOutcome.Granted, 1, false).Should().Be(ClaimStep.Craft);
        NextStep(CraftingClaimOutcome.Granted, MAX_ATTEMPTS, false).Should().Be(ClaimStep.Craft);
    }

    [TestMethod]
    public void ARefusalIsRetriedWithOtherItemsWhenThereAreEnough()
    {
        NextStep(CraftingClaimOutcome.Taken, 1, true).Should().Be(ClaimStep.Retry);
    }

    [TestMethod]
    public void ARefusalWithoutEnoughOtherItemsCancels()
    {
        // the other player took the last items
        NextStep(CraftingClaimOutcome.Taken, 1, false).Should().Be(ClaimStep.Cancel);
    }

    [TestMethod]
    public void TheRetryIsLimited()
    {
        NextStep(CraftingClaimOutcome.Taken, MAX_ATTEMPTS, true).Should().Be(ClaimStep.Cancel);
        NextStep(CraftingClaimOutcome.Taken, MAX_ATTEMPTS + 1, true).Should().Be(ClaimStep.Cancel);
    }

    [TestMethod]
    public void AnOptionThatIsOffOrANonsenseRequestIsNotRetried()
    {
        NextStep(CraftingClaimOutcome.Disabled, 1, true).Should().Be(ClaimStep.Cancel);
        NextStep(CraftingClaimOutcome.Invalid, 1, true).Should().Be(ClaimStep.Cancel);
        NextStep((CraftingClaimOutcome)99, 1, true).Should().Be(ClaimStep.Cancel);
        NextStep(CraftingClaimOutcome.Unknown, 1, true).Should().Be(ClaimStep.Cancel);
    }

    [TestMethod]
    public void TheClaimedItemsGoBackWhenTheCraftCannotHappen()
    {
        ShouldReturnClaimedItems(true, true).Should().BeFalse("the craft started: the items are spent");
        ShouldReturnClaimedItems(false, true).Should().BeTrue();
        ShouldReturnClaimedItems(true, false).Should().BeTrue();
        ShouldReturnClaimedItems(false, false).Should().BeTrue();
    }

    [TestMethod]
    public void AClaimTimesOutAfterTheLimit()
    {
        HasTimedOut(0f).Should().BeFalse();
        HasTimedOut(ANSWER_TIMEOUT - 0.01f).Should().BeFalse();
        HasTimedOut(ANSWER_TIMEOUT).Should().BeTrue();
        HasTimedOut(ANSWER_TIMEOUT * 10f).Should().BeTrue();
    }
}
