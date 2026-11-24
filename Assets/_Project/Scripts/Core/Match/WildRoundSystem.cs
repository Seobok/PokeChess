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
        public const string BasicAttackSkillId = "wild-basic-attack";
        public const string BossId = "gyarados";
        public static int BossStage(int round)=>Math.Max(0,(round-9)/6);
        public static float BossVisualScale(int round)=>1f+.07f*Math.Min(6,BossStage(round));
        public static PokemonStats BossStats(int round)
        {
            int stage=BossStage(round);
            return new PokemonStats((float)Math.Round(1800*Math.Pow(1.55,stage)),
                (float)Math.Round(85*Math.Pow(1.22,stage)),100,.7f+.04f*Math.Min(10,stage),
                20+5*Math.Min(9,stage),20+5*Math.Min(9,stage),1,2,0,0,.3f);
        }
        public static PokemonDefinition BossDefinition()=>new PokemonDefinition(BossId,"갸라도스",0,
            BossStats(9),"wild",BasicAttackSkillId,new[]{"water","flying"},isBoss:true);
        // Keep the tutorial's existing baseline until encounter balance is designed.
        public static IEnumerable<PokemonDefinition> TutorialDefinitions(PokemonDefinition baseline)
        {
            if(baseline==null)yield break;
            yield return new PokemonDefinition("magikarp", "잉어킹", 0, baseline.BaseStats,
                "wild", BasicAttackSkillId, new[] { "water" });
            yield return new PokemonDefinition("rattata", "꼬렛", 0, baseline.BaseStats,
                "wild", BasicAttackSkillId, new[] { "normal" });
        }
        public static bool IsWild(MatchState match)=>match.Rules.EnableWildRounds&&IsWildRound(match.RoundNumber);
        public static bool IsWildRound(int round)=>round>=1&&(round<=3||round>=9&&(round-9)%6==0);
        public static int ItemRewardCount(int round)
            => !IsWildRound(round) ? 0 : round <= 2 ? 1 : round == 3 ? 2 : 3;
        public static int EnemyCount(int round)=>round==1?2:round<=3?3:1;
        public static string Pattern(int round)=>round<=3?"Tutorial":"Single";
        public static string Preview(int round)
        {
            string encounter = round <= 3
                ? $"약한 야생 포켓몬 {EnemyCount(round)}마리 · 일반 공격만 사용"
                : $"갸라도스 1마리 · 강화 {BossStage(round)+1}단계 · 일반 공격만 사용";
            return $"{encounter} · 처치 시 무작위 재료 드롭 · 총 {ItemRewardCount(round)}개";
        }
        internal static BattleState CreateBattle(MatchState match,PokemonCatalog catalog,PlayerState player,
            IEnumerable<BattleUnitSetup> allies,int tickRate)
        {
            int round=match.RoundNumber;int count=EnemyCount(round);
            string[] preferred={"caterpie","weedle","caterpie"};
            var first=player.Units.FirstOrDefault();
            var enemies=new List<BattleUnitSetup>();
            for(int i=0;i<count;i++){
                string id=round<=2?"magikarp":round==3?"rattata":BossId;
                if(!catalog.TryGet(id,out _))id=preferred[i];
                if(!catalog.TryGet(id,out _)){
                    id=first?.DefinitionId;
                    if(id==null||!catalog.TryGet(id,out _))
                        id=new[]{"bulbasaur","charmander","squirtle","pikachu"}.FirstOrDefault(candidate=>catalog.TryGet(candidate,out _));
                }
                if(id==null)throw new InvalidOperationException("No wild encounter definition available.");
                var position=HexCoordinates.MirrorCombat(new BoardPosition(count==1?3:count==2?2+i*2:1+i*2,3));
                var unit=catalog.CreateUnit("wild-"+round+"-"+player.PlayerId+"-"+i,id,"wild:"+player.PlayerId,
                    id==BossId?UnitRank.One:round>=16?UnitRank.Two:UnitRank.One,0,UnitPlacement.OnBoard(position));
                enemies.Add(new BattleUnitSetup(unit,2,position));
            }
            var battle=BattleStateFactory.Create(match.MatchId+"-wild-"+round+"-"+player.PlayerId,round,
                unchecked(match.MatchSeed^(ulong)round*0x9E3779B97F4A7C15UL),tickRate,catalog,allies.Concat(enemies),
                (unit,definition)=>{
                    var s=RankRules.Default.Apply(definition,unit.Rank);
                    if(unit.OwnerPlayerId==player.PlayerId)return s;
                    if(definition.Id==BossId)return BossStats(round);
                    float scale=round<=3?.15f+(round-1)*.025f: .65f+(round-9)*.025f;
                    return new PokemonStats(s.MaxHP*scale,s.Attack*scale,s.SpellPower,s.AttackSpeed,
                        s.Armor,s.MagicResistance,s.AttackRange,s.MoveSpeed,
                        round<=3?0:s.MaxEnergy,round<=3?0:s.StartingEnergy,s.AttackHitTime,s.AttackDelivery,s.ProjectileSpeed,s.CritChance,s.CritMultiplier,s.EnergyLockSeconds);
                }, guardianOne: SynergyPreparation.PreparationGuardian(player, catalog));
            battle.IsPvP=false;return battle;
        }
    }
}
