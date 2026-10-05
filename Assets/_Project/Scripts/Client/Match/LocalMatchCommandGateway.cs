using System;
using System.Collections.Generic;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;

namespace PokeChess.Client.Match
{
    public enum MatchCommandFailure { None, NotEnoughGold, Changed, FullBench, PoolEmpty, WrongPhase, Eliminated, Unavailable, InvalidRequest }
    public sealed class MatchCommandResult
    {
        public bool Accepted => Failure==MatchCommandFailure.None;
        public MatchCommandFailure Failure { get; }
        public string Message { get; }
        public PurchaseResult Purchase { get; }
        public PurchasePreview Preview { get; }
        public int SaleGold { get; }
        public int ReturnedItems { get; }
        internal MatchCommandResult(string message,PurchaseResult purchase=null,PurchasePreview preview=null,int saleGold=0,int returnedItems=0,MatchCommandFailure failure=MatchCommandFailure.None)
        { Message=message;Purchase=purchase;Preview=preview;SaleGold=saleGold;ReturnedItems=returnedItems;Failure=failure; }
    }
    // Local host executor. UI does not mutate Gold, Rank, shop or ownership directly.
    public sealed class LocalMatchCommandGateway
    {
        private readonly MatchState match;
        private readonly string playerId;
        private readonly ShopTransactionSystem trades;
        private readonly ShopSystem shops=new ShopSystem();
        private readonly LevelSystem levels=new LevelSystem();
        public ShopRules ShopRules => shops.Rules;
        public LevelRules LevelRules => levels.Rules;
        private PlayerState Player => match.GetPlayer(playerId);
        public bool CanBuy => (match.Phase==MatchPhase.Preparation || match.Phase==MatchPhase.Combat) && Player.HP>0 && !Player.IsEliminated;
        public bool CanTrade => match.Phase==MatchPhase.Preparation && Player.HP>0 && !Player.IsEliminated;
        public LocalMatchCommandGateway(MatchState match,PokemonCatalog catalog,string playerId)
        {
            this.match=match ?? throw new ArgumentNullException(nameof(match));this.playerId=playerId;match.GetPlayer(playerId);
            trades=new ShopTransactionSystem(catalog);
        }
        private MatchCommandResult Run(Func<MatchCommandResult> action)
        {
            try { return action(); }
            catch(InvalidOperationException e) { return Reject(e.Message); }
            catch(ArgumentException) { return new MatchCommandResult("Invalid request.",failure:MatchCommandFailure.InvalidRequest); }
            catch(KeyNotFoundException) { return new MatchCommandResult("Unit or offer is no longer available.",failure:MatchCommandFailure.Unavailable); }
            catch(OverflowException) { return new MatchCommandResult("Action cannot be completed.",failure:MatchCommandFailure.InvalidRequest); }
        }
        private static MatchCommandResult Reject(string reason)
        {
            if(reason.IndexOf("gold",StringComparison.OrdinalIgnoreCase)>=0) return new MatchCommandResult("Not enough gold.",failure:MatchCommandFailure.NotEnoughGold);
            if(reason.IndexOf("stale",StringComparison.OrdinalIgnoreCase)>=0) return new MatchCommandResult("State changed. Please retry.",failure:MatchCommandFailure.Changed);
            if(reason.IndexOf("bench is full",StringComparison.OrdinalIgnoreCase)>=0) return new MatchCommandResult("Bench full: no room after merging.",failure:MatchCommandFailure.FullBench);
            if(reason.IndexOf("PoolUnavailable",StringComparison.OrdinalIgnoreCase)>=0) return new MatchCommandResult("No copies left in the shared pool.",failure:MatchCommandFailure.PoolEmpty);
            if(reason.IndexOf("eliminated",StringComparison.OrdinalIgnoreCase)>=0) return new MatchCommandResult("Eliminated players cannot act.",failure:MatchCommandFailure.Eliminated);
            if(reason.IndexOf("Preparation",StringComparison.OrdinalIgnoreCase)>=0) return new MatchCommandResult("This action is available during preparation only.",failure:MatchCommandFailure.WrongPhase);
            return new MatchCommandResult("Action is unavailable.",failure:MatchCommandFailure.Unavailable);
        }
        private void CheckShopRevision(long expected)
        {
            if(!CanTrade) throw new InvalidOperationException(Player.HP==0 || Player.IsEliminated ? "Player is eliminated." : "Requires Preparation.");
            if(Player.Shop.Revision!=expected) throw new InvalidOperationException("Stale shop revision.");
        }
        public MatchCommandResult PreviewBuy(int slot,long revision) => Run(()=>new MatchCommandResult("Available.",preview:trades.PreviewBuy(match,playerId,slot,revision)));
        public MatchCommandResult Buy(int slot,long revision) => Run(()=>new MatchCommandResult("Purchase accepted.",purchase:trades.BuyWithRankUp(match,playerId,slot,revision)));
        public MatchCommandResult Reroll(long revision) => Run(()=> { CheckShopRevision(revision);shops.Reroll(match,playerId);return new MatchCommandResult("Shop refreshed."); });
        public MatchCommandResult Lock(bool locked,long revision) => Run(()=> { CheckShopRevision(revision);shops.SetLocked(match,playerId,locked);return new MatchCommandResult(locked ? "Shop locked for the next round." : "Shop unlocked."); });
        public MatchCommandResult BuyXP() => Run(()=> { if(Player.HP==0 || Player.IsEliminated) throw new InvalidOperationException("Player is eliminated.");levels.BuyXP(match,playerId);return new MatchCommandResult("XP purchased."); });
        public int SalePrice(string id) => trades.SalePrice(Player.GetUnit(id));
        public MatchCommandResult Sell(string id,long revision) => Run(()=>
        {
            if(!CanTrade) throw new InvalidOperationException(Player.HP==0 || Player.IsEliminated ? "Player is eliminated." : "Requires Preparation.");
            if(Player.PlacementRevision!=revision) throw new InvalidOperationException("Stale placement revision.");
            int items=Player.GetUnit(id).ItemInstanceIds.Count;int gold=trades.Sell(match,playerId,id);
            return new MatchCommandResult("Sold for +"+gold+"G; "+items+" item(s) returned.",saleGold:gold,returnedItems:items);
        });
    }
}
