using System;
using System.Collections.Generic;
using System.Linq;
using PokeChess.Core.Battle;
using PokeChess.Core.Board;
using PokeChess.Core.Pokemon;
namespace PokeChess.Core.Tests
{
    internal sealed class CombatSandboxScenario
    {
        public readonly string Name;
        public readonly BattleState Battle;
        public readonly BattleSimulation Simulation;
        public CombatSandboxScenario(string name,BattleState battle,BattleSimulation simulation)
        {Name=name;Battle=battle;Simulation=simulation;}
    }
    internal static class CombatSandboxScenarios
    {
        public static readonly string[] Names={"MeleeDuel","RangedDuel","MixedTeams","CrowdedBoard","TargetTie",
            "OffensiveSkills","HealingShield","CrowdControls","StatusStacks","OvertimeDraw"};
        public static CombatSandboxScenario Create(int index,ulong seed,bool reverse=false)
        {
            bool ranged=index==1;bool skilled=index>=5&&index<=8;
            var skills=new List<SkillDefinition>();var statuses=new List<StatusEffectDefinition>();
            var effects=new List<SkillEffectDefinition>();
            if(index==5) {
                effects.Add(new SkillEffectDefinition(SkillEffectType.Damage,EffectTargetSelector.CastTarget,30,true));
                effects.Add(new SkillEffectDefinition(SkillEffectType.ProjectileDamage,EffectTargetSelector.CastTarget,20,true,DamageType.True));
            }
            if(index==6) {
                effects.Add(new SkillEffectDefinition(SkillEffectType.Heal,EffectTargetSelector.Self,35,true));
                effects.Add(new SkillEffectDefinition(SkillEffectType.Shield,EffectTargetSelector.Self,25));
            }
            if(index==7) {
                effects.Add(new SkillEffectDefinition(SkillEffectType.Damage,EffectTargetSelector.CastTarget,15));
                foreach(CrowdControlKind cc in Enum.GetValues(typeof(CrowdControlKind)))if(cc!=CrowdControlKind.None) {
                    var id=cc.ToString();statuses.Add(new StatusEffectDefinition(id,.5f,crowdControl:cc,slowFraction:cc==CrowdControlKind.Slow?.35f:0));
                    effects.Add(Status(id,EffectTargetSelector.CastTarget,StatusTargetTeam.Enemy));
                }
            }
            if(index==8) {
                effects.Add(new SkillEffectDefinition(SkillEffectType.Damage,EffectTargetSelector.CastTarget,5));
                statuses.Add(Buff("none",CombatStat.Attack,5,StackPolicy.None,4));
                statuses.Add(Buff("refresh",CombatStat.SpellPower,10,StackPolicy.Refresh,4));
                statuses.Add(Buff("stack",CombatStat.AttackSpeed,.1f,StackPolicy.Stack,4,StatModifierType.Percentage));
                statuses.Add(Buff("weak",CombatStat.Armor,10,StackPolicy.Strongest,6,group:"armor"));
                statuses.Add(Buff("strong",CombatStat.Armor,30,StackPolicy.Strongest,1.2f,group:"armor"));
                statuses.Add(new StatusEffectDefinition("debuff",4,modifiers:new[]{new StatModifierDefinition(CombatStat.Armor,StatModifierType.Flat,-20)}));
                foreach(var d in statuses)effects.Add(Status(d.Id,d.TargetTeam==StatusTargetTeam.Ally?EffectTargetSelector.Self:EffectTargetSelector.CastTarget,d.TargetTeam));
            }
            if(skilled)skills.Add(new SkillDefinition("skill",.15f,.1f,index==6?SkillTargetRule.Self:SkillTargetRule.Enemy,
                TargetLostPolicy.Cancel,effects,range:3));
            var definitions=new List<PokemonDefinition>();
            foreach(bool projectile in new[]{false,true}) {
                float hp=index==9?100000:index==6?600:index==8?800:300;
                float attack=index==9?1:index==6?15:index==8?20:40;
                float energy=skilled?index==8?20:40:0;
                var stats=new PokemonStats(hp,attack,50,index==8?2:1.2f,20,15,projectile?3:1,2,energy,energy,.15f,
                    projectile?AttackDeliveryType.Projectile:AttackDeliveryType.Melee,6,.25f,1.5f);
                definitions.Add(new PokemonDefinition(projectile?"r":"m",projectile?"Ranged":"Melee",1,stats,"sandbox",skilled?"skill":"missing"));
            }
            var catalog=new PokemonCatalog(definitions);var setup=new List<BattleUnitSetup>();
            Action<string,int,int,int,bool> add=(id,team,x,y,projectile)=>setup.Add(new BattleUnitSetup(
                catalog.CreateUnit(id,projectile?"r":"m","p"+team,UnitRank.One,0,UnitPlacement.Unplaced),team,new BoardPosition(x,y)));
            if(index==4) {
                var source=new BoardPosition(3,3);
                var pair=HexBoardBounds.Combat.Cells().Where(c=>!c.Equals(source))
                    .GroupBy(c=>(HexCoordinates.Distance(source,c),CombatTargetSelector.CenterDistanceScore(c)))
                    .Where(g=>g.Count()>=2).OrderBy(g=>g.Key.Item1).ThenBy(g=>g.Key.Item2).First().Take(2).ToArray();
                add("a",1,source.Column,source.Row,false);add("y",2,pair[0].Column,pair[0].Row,false);add("z",2,pair[1].Column,pair[1].Row,false);
            } else if(index==3) {
                for(int row=0;row<5;row++)for(int col=0;col<2;col++) {
                    add("a"+row+col,1,col,row,(row+col)%3==0);add("z"+row+col,2,6-col,row,(row+col)%3==0);
                }
            } else if(index==2||skilled) {
                for(int row=0;row<4;row++){add("a"+row,1,row%2,row*2,row%2==1);add("z"+row,2,6-row%2,row*2,row%2==1);}
            } else {add("a",1,0,0,ranged);add("z",2,6,7,ranged);}
            var battle=BattleStateFactory.Create(Names[index],1,seed,30,catalog,reverse?setup.AsEnumerable().Reverse():setup);
            var simulation=new BattleSimulation(battle,skillCatalog:new SkillCatalog(skills),statusCatalog:new StatusCatalog(statuses));
            return new CombatSandboxScenario(Names[index],battle,simulation);
        }
        private static SkillEffectDefinition Status(string id,EffectTargetSelector selector,StatusTargetTeam team)=>
            new SkillEffectDefinition(SkillEffectType.ApplyStatus,selector,0,statusId:id,statusTargetTeam:team);
        private static StatusEffectDefinition Buff(string id,CombatStat stat,float value,StackPolicy policy,float duration,
            StatModifierType type=StatModifierType.Flat,string group=null)=>new StatusEffectDefinition(id,duration,StatusTargetTeam.Ally,
                stackPolicy:policy,sourceRule:SourceStackRule.Shared,maxStack:3,
                modifiers:new[]{new StatModifierDefinition(stat,type,value)},stackGroup:group);
    }
}
