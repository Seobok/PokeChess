using System;
using System.Collections.Generic;
using System.Linq;
using PokeChess.Core.Battle;
using PokeChess.Core.Board;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Match
{
    public static class WildRoundSystem
    {
        public static bool IsWild(MatchState match)=>match.Rules.EnableWildRounds&&IsWildRound(match.RoundNumber);
        public static bool IsWildRound(int round)=>round>=1&&round<=21&&(round-1)%5==0;
        public static string Pattern(int round)=>round==1?"Tutorial":((round-1)/5-1)%3==0?"Swarm":((round-1)/5-1)%3==1?"Guard":"Rush";
        public static string Preview(int round)
        {
            switch(Pattern(round)){
                case "Tutorial":return "약한 야생 포켓몬 1마리 · 도구 장착 연습";
                case "Swarm":return "무리형 · 적 3마리 · 광역 공격 추천";
                case "Guard":return "수호형 · 앞줄 2마리 + 원거리 뒷줄 · 집중 공격 추천";
                default:return "돌진형 · 빠른 적 3마리 · 후방 보호 추천";
            }
        }
        internal static BattleState CreateBattle(MatchState match,PokemonCatalog catalog,PlayerState player,
            IEnumerable<BattleUnitSetup> allies,int tickRate)
        {
            int round=match.RoundNumber;string pattern=Pattern(round);int count=round==1?1:3;
            string[] preferred=pattern=="Swarm"?new[]{"weedle","caterpie","weedle"}:
                pattern=="Guard"?new[]{"geodude","slowpoke","magnemite"}:
                pattern=="Rush"?new[]{"mankey","mankey","gastly"}:new[]{"caterpie"};
            var first=player.Units.FirstOrDefault();
            var enemies=new List<BattleUnitSetup>();
            for(int i=0;i<count;i++){
                string id=preferred[i];
                if(!catalog.TryGet(id,out _)){
                    id=first?.DefinitionId;
                    if(id==null||!catalog.TryGet(id,out _))
                        id=new[]{"bulbasaur","charmander","squirtle","pikachu"}.FirstOrDefault(candidate=>catalog.TryGet(candidate,out _));
                }
                if(id==null)throw new InvalidOperationException("No wild encounter definition available.");
                var position=HexCoordinates.MirrorCombat(new BoardPosition(2+i*2,pattern=="Guard"&&i==2?0:3));
                var unit=catalog.CreateUnit("wild-"+round+"-"+player.PlayerId+"-"+i,id,"wild:"+player.PlayerId,
                    round>=16?UnitRank.Two:UnitRank.One,0,UnitPlacement.OnBoard(position));
                enemies.Add(new BattleUnitSetup(unit,2,position));
            }
            return BattleStateFactory.Create(match.MatchId+"-wild-"+round+"-"+player.PlayerId,round,
                unchecked(match.MatchSeed^(ulong)round*0x9E3779B97F4A7C15UL),tickRate,catalog,allies.Concat(enemies),
                (unit,definition)=>{
                    var s=RankRules.Default.Apply(definition.BaseStats,unit.Rank);
                    if(unit.OwnerPlayerId==player.PlayerId)return s;
                    float scale=round==1?.35f: .65f+(round-6)*.025f;
                    return new PokemonStats(s.MaxHP*scale,s.Attack*scale,s.SpellPower,s.AttackSpeed,
                        s.Armor,s.MagicResistance,s.AttackRange,s.MoveSpeed*(pattern=="Rush"?1.5f:1),
                        s.MaxEnergy,s.StartingEnergy,s.AttackHitTime,s.AttackDelivery,s.ProjectileSpeed,s.CritChance,s.CritMultiplier,s.EnergyLockSeconds);
                });
        }
    }
}
