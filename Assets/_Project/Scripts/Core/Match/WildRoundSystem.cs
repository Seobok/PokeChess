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
        public static bool IsWildRound(int round)=>round>=1&&(round<=3||round>=9&&(round-9)%5==0);
        public static bool HasItemReward(int round)=>IsWildRound(round)&&round>=3;
        public static int EnemyCount(int round)=>round==1?2:round<=3?3:1;
        public static string Pattern(int round)=>round<=3?"Tutorial":"Single";
        public static string Preview(int round)
        {
            return round<=3
                ? "약한 야생 포켓몬 "+EnemyCount(round)+"마리 · 일반 공격만 사용 · 승리 보상: "+(HasItemReward(round)?"도구 선택":"추가 2골드")
                : "중앙 야생 포켓몬 1마리 · 임시 편성 · 승리 보상: 도구 선택";
        }
        internal static BattleState CreateBattle(MatchState match,PokemonCatalog catalog,PlayerState player,
            IEnumerable<BattleUnitSetup> allies,int tickRate)
        {
            int round=match.RoundNumber;int count=EnemyCount(round);
            string[] preferred={"caterpie","weedle","caterpie"};
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
                var position=HexCoordinates.MirrorCombat(new BoardPosition(count==1?3:count==2?2+i*2:1+i*2,3));
                var unit=catalog.CreateUnit("wild-"+round+"-"+player.PlayerId+"-"+i,id,"wild:"+player.PlayerId,
                    round>=16?UnitRank.Two:UnitRank.One,0,UnitPlacement.OnBoard(position));
                enemies.Add(new BattleUnitSetup(unit,2,position));
            }
            return BattleStateFactory.Create(match.MatchId+"-wild-"+round+"-"+player.PlayerId,round,
                unchecked(match.MatchSeed^(ulong)round*0x9E3779B97F4A7C15UL),tickRate,catalog,allies.Concat(enemies),
                (unit,definition)=>{
                    var s=RankRules.Default.Apply(definition.BaseStats,unit.Rank);
                    if(unit.OwnerPlayerId==player.PlayerId)return s;
                    float scale=round<=3?.15f+(round-1)*.025f: .65f+(round-9)*.025f;
                    return new PokemonStats(s.MaxHP*scale,s.Attack*scale,s.SpellPower,s.AttackSpeed,
                        s.Armor,s.MagicResistance,s.AttackRange,s.MoveSpeed,
                        round<=3?0:s.MaxEnergy,round<=3?0:s.StartingEnergy,s.AttackHitTime,s.AttackDelivery,s.ProjectileSpeed,s.CritChance,s.CritMultiplier,s.EnergyLockSeconds);
                });
        }
    }
}
