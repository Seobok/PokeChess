using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;
namespace PokeChess.Client.Match
{
    // Local UI uses the same trusted Core executor as the online Host.
    public sealed class LocalMatchCommandGateway
    {
        private readonly MatchCommandExecutor executor;
        public LocalMatchCommandGateway(MatchState match,PokemonCatalog catalog,string playerId){executor=new MatchCommandExecutor(match,catalog,playerId);}
        public ShopRules ShopRules=>executor.ShopRules;
        public LevelRules LevelRules=>executor.LevelRules;
        public bool CanBuy=>executor.CanBuy;
        public bool CanTrade=>executor.CanTrade;
        public bool CanSell(string id)=>executor.CanSell(id);
        public MatchCommandResult PreviewBuy(int slot,long revision)=>executor.PreviewBuy(slot,revision);
        public MatchCommandResult Buy(int slot,long revision)=>executor.Buy(slot,revision);
        public MatchCommandResult Reroll(long revision)=>executor.Reroll(revision);
        public MatchCommandResult Lock(bool locked,long revision)=>executor.Lock(locked,revision);
        public MatchCommandResult BuyXP()=>executor.BuyXP();
        public int SalePrice(string id)=>executor.SalePrice(id);
        public MatchCommandResult Sell(string id,long revision)=>executor.Sell(id,revision);
        public MatchCommandResult ChooseItem(int slot,long revision)=>executor.ChooseItem(slot,revision);
        public MatchCommandResult EquipItem(string unitId,string itemId,long revision)=>executor.EquipItem(unitId,itemId,revision);
        public MatchCommandResult UnequipItem(string unitId,long revision)=>executor.UnequipItem(unitId,revision);
        public MatchCommandResult PreviewEvolution(string id,long evolutionRevision,long placementRevision)=>executor.PreviewEvolution(id,evolutionRevision,placementRevision);
        public MatchCommandResult Evolve(string id,long evolutionRevision,long placementRevision)=>executor.Evolve(id,evolutionRevision,placementRevision);
    }
}

