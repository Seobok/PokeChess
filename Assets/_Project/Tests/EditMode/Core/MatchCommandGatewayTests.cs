using System.Linq;
using NUnit.Framework;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;
using PokeChess.Client.Match;

namespace PokeChess.Core.Tests
{
    public sealed class MatchCommandGatewayTests
    {
        private PokemonCatalog catalog;
        private MatchState match;
        private PlayerState player;
        private LocalMatchCommandGateway commands;
        [SetUp] public void Setup()
        {
            catalog=new PokemonCatalog(new[]{new PokemonDefinition("test","Test",1,new PokemonStats(100,10,0,1,0,0,1,2,0,0,.25f),"role","skill")});
            match=MatchStateFactory.CreateWithPool("ui",new[]{"p1","p2"},catalog,new[]{"test"});player=match.GetPlayer("p1");
            match.TransitionTo(MatchPhase.Starting);match.TransitionTo(MatchPhase.Preparation);new ShopSystem().RefreshForRound(match,"p1");commands=new LocalMatchCommandGateway(match,catalog,"p1");
        }
        private string Snapshot() => player.Gold+"|"+player.Level+"|"+player.XP+"|"+player.Shop.Revision+"|"+player.Shop.IsLocked+"|"+player.Shop.RandomState+"|"+player.Shop.RandomDrawCount+"|"+player.PlacementRevision+
            "|"+string.Join(",",player.Shop.Slots.Select(s=>s.DefinitionId))+"|"+string.Join(",",player.Units.Select(u=>u.InstanceId))+"|"+match.Pool.GetStock("test").Available;
        [Test] public void PreviewHasNoSideEffectsAndBuyUpdatesOwnedState()
        {
            var before=Snapshot();Assert.That(commands.PreviewBuy(0,player.Shop.Revision).Accepted,Is.True);Assert.That(Snapshot(),Is.EqualTo(before));
            var result=commands.Buy(0,player.Shop.Revision);Assert.That(result.Accepted,Is.True);Assert.That(result.Purchase.Unit,Is.SameAs(player.Units.Single()));Assert.That(player.Gold,Is.EqualTo(4));
        }
        [Test] public void StaleShopActionsReturnChangedAndDoNotMutate()
        {
            long revision=player.Shop.Revision;commands.Reroll(revision);var before=Snapshot();
            Assert.That(commands.Buy(0,revision).Failure,Is.EqualTo(MatchCommandFailure.Changed));
            Assert.That(commands.Reroll(revision).Failure,Is.EqualTo(MatchCommandFailure.Changed));Assert.That(commands.Lock(true,revision).Failure,Is.EqualTo(MatchCommandFailure.Changed));Assert.That(Snapshot(),Is.EqualTo(before));
        }
        [Test] public void LockAllowsPurchaseAndManualRerollAndPreservesNextRoundOffers()
        {
            player.SetProgress(20,0,1);commands.Lock(true,player.Shop.Revision);Assert.That(commands.Buy(0,player.Shop.Revision).Accepted,Is.True);
            Assert.That(commands.Reroll(player.Shop.Revision).Accepted,Is.True);Assert.That(player.Shop.IsLocked,Is.True);var rng=player.Shop.RandomState;var slots=player.Shop.Slots.Select(s=>s.DefinitionId).ToArray();
            match.TransitionTo(MatchPhase.Combat);match.TransitionTo(MatchPhase.Result);match.TransitionTo(MatchPhase.Preparation);Assert.That(new ShopSystem().RefreshForRound(match,"p1"),Is.False);
            Assert.That(player.Shop.RandomState,Is.EqualTo(rng));Assert.That(player.Shop.Slots.Select(s=>s.DefinitionId),Is.EqualTo(slots));
        }
        [Test] public void XPUsesCoreCostAndLevelRulesAndMaximumIsRejected()
        {
            Assert.That(commands.BuyXP().Accepted,Is.True);Assert.That(player.Gold,Is.EqualTo(1));Assert.That(player.Level,Is.EqualTo(3));Assert.That(player.XP,Is.Zero);
            player.SetProgress(20,0,10);var before=Snapshot();Assert.That(commands.BuyXP().Accepted,Is.False);Assert.That(Snapshot(),Is.EqualTo(before));
        }
        [Test] public void InsufficientGoldReturnsReasonAndPreservesState()
        {
            player.SetProgress(0,0,1);var before=Snapshot();Assert.That(commands.PreviewBuy(0,player.Shop.Revision).Failure,Is.EqualTo(MatchCommandFailure.NotEnoughGold));
            Assert.That(commands.Buy(0,player.Shop.Revision).Failure,Is.EqualTo(MatchCommandFailure.NotEnoughGold));Assert.That(commands.Reroll(player.Shop.Revision).Failure,Is.EqualTo(MatchCommandFailure.NotEnoughGold));
            Assert.That(commands.BuyXP().Failure,Is.EqualTo(MatchCommandFailure.NotEnoughGold));Assert.That(Snapshot(),Is.EqualTo(before));
        }
        [Test] public void SellReturnsItemsAndStaleSelectionRejects()
        {
            var unit=commands.Buy(0,player.Shop.Revision).Purchase.Unit;unit.AddItem("item");long revision=player.PlacementRevision;
            player.SetPlacement(unit.InstanceId,UnitPlacement.OnBoard(new BoardPosition(0,0)));var before=Snapshot();
            Assert.That(commands.Sell(unit.InstanceId,revision).Failure,Is.EqualTo(MatchCommandFailure.Changed));Assert.That(Snapshot(),Is.EqualTo(before));
            var sale=commands.Sell(unit.InstanceId,player.PlacementRevision);Assert.That(sale.Accepted,Is.True);Assert.That(sale.SaleGold,Is.EqualTo(1));Assert.That(sale.ReturnedItems,Is.EqualTo(1));Assert.That(player.ItemInventory,Is.EqualTo(new[]{"item"}));
        }
        [Test] public void CombatAndEliminationBlockAllOwnerEconomyCommands()
        {
            var unit=commands.Buy(0,player.Shop.Revision).Purchase.Unit;match.TransitionTo(MatchPhase.Combat);var before=Snapshot();
            Assert.That(commands.Buy(1,player.Shop.Revision).Failure,Is.EqualTo(MatchCommandFailure.WrongPhase));Assert.That(commands.BuyXP().Failure,Is.EqualTo(MatchCommandFailure.WrongPhase));
            Assert.That(commands.Sell(unit.InstanceId,player.PlacementRevision).Failure,Is.EqualTo(MatchCommandFailure.WrongPhase));Assert.That(commands.Lock(true,player.Shop.Revision).Failure,Is.EqualTo(MatchCommandFailure.WrongPhase));
            Assert.That(Snapshot(),Is.EqualTo(before));player.SetHP(0);Assert.That(commands.BuyXP().Failure,Is.EqualTo(MatchCommandFailure.Eliminated));Assert.That(commands.Reroll(player.Shop.Revision).Failure,Is.EqualTo(MatchCommandFailure.Eliminated));
        }
        [Test] public void OtherPlayersUnitsCannotBeSoldOrChangeEconomy()
        {
            match.GetPlayer("p2").AddUnit(catalog.CreateUnit("foreign","test","p2",UnitRank.One,0,UnitPlacement.OnBench(0)));var before=Snapshot();
            Assert.That(commands.Sell("foreign",player.PlacementRevision).Accepted,Is.False);Assert.That(Snapshot(),Is.EqualTo(before));Assert.That(match.GetPlayer("p2").Units.Count,Is.EqualTo(1));
        }
    }
}
