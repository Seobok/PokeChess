using System;
using System.Collections.Generic;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Match
{
    public enum MatchCommandFailure { None, NotEnoughGold, Changed, FullBench, PoolEmpty, WrongPhase, Eliminated, Unavailable, InvalidRequest, NotEnoughEvolutionPoints }
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
    public sealed class MatchCommandExecutor
    {
        private readonly MatchState match;
        private readonly string playerId;
        private readonly ShopTransactionSystem trades;
        private readonly EvolutionSystem evolutions;
        private readonly ShopSystem shops=new ShopSystem();
        private readonly LevelSystem levels=new LevelSystem();
        public ShopRules ShopRules => shops.Rules;
        public LevelRules LevelRules => levels.Rules;
        private PlayerState Player => match.GetPlayer(playerId);
        public bool CanBuy => (match.Phase==MatchPhase.Preparation || match.Phase==MatchPhase.Combat) && Player.HP>0 && !Player.IsEliminated;
        public bool CanTrade => match.Phase==MatchPhase.Preparation && Player.HP>0 && !Player.IsEliminated;
        public bool CanSell(string id) => CanBuy && System.Linq.Enumerable.Any(Player.Units,u=>u.InstanceId==id && (match.Phase==MatchPhase.Preparation || u.Placement.Kind==PlacementKind.Bench));
        public MatchCommandExecutor(MatchState match,PokemonCatalog catalog,string playerId)
        {
            this.match=match ?? throw new ArgumentNullException(nameof(match));this.playerId=playerId;match.GetPlayer(playerId);
            trades=new ShopTransactionSystem(catalog);
            evolutions=new EvolutionSystem(catalog);
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
            if(reason.IndexOf("Not enough evolution points",StringComparison.OrdinalIgnoreCase)>=0) return new MatchCommandResult("진화포인트가 부족합니다.",failure:MatchCommandFailure.NotEnoughEvolutionPoints);
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
        public MatchCommandResult PreviewEvolution(string id,long evolutionRevision,long placementRevision)
            => Run(()=> { evolutions.Preview(match,playerId,id,evolutionRevision,placementRevision);return new MatchCommandResult("진화 가능"); });
        public MatchCommandResult Evolve(string id,long evolutionRevision,long placementRevision)
            => Run(()=> { var unit=evolutions.Evolve(match,playerId,id,evolutionRevision,placementRevision);
                return new MatchCommandResult(unit.DefinitionId+" 진화 완료. 다음 상점 갱신부터 진화체가 등장합니다."); });
        public MatchCommandResult ChooseItem(int slot,long revision)=>Run(()=>{
            var id=new ItemSystem().Choose(match,playerId,slot,revision);
            return new MatchCommandResult(ItemCatalog.DisplayName(id)+" 획득");
        });
        public MatchCommandResult EquipItem(string unitId,string itemId,long revision)=>Run(()=>{
            new ItemSystem().Equip(match,playerId,unitId,itemId,revision);
            return new MatchCommandResult(ItemCatalog.DisplayName(itemId)+" 장착");
        });
        public MatchCommandResult UnequipItem(string unitId,long revision)=>Run(()=>{
            new ItemSystem().Unequip(match,playerId,unitId,revision);
            return new MatchCommandResult("도구를 보관함으로 회수했습니다.");
        });
        public MatchCommandResult Sell(string id,long revision) => Run(()=>
        {
            if(!CanSell(id)) throw new InvalidOperationException(Player.HP==0 || Player.IsEliminated ? "Player is eliminated." : "Requires Preparation.");
            if(Player.PlacementRevision!=revision) throw new InvalidOperationException("Stale placement revision.");
            int items=Player.GetUnit(id).ItemInstanceIds.Count;int gold=trades.Sell(match,playerId,id);
            return new MatchCommandResult("Sold for +"+gold+"G; "+items+" item(s) returned.",saleGold:gold,returnedItems:items);
        });
    }
}


