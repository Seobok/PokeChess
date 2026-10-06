using System;
using System.Collections.Generic;
using System.Linq;
using PokeChess.Core.Battle;

namespace PokeChess.Core.Pokemon
{
    // G4.1 base forms only. Rank skill values are authored independently.
    public static class PrototypeRoster
    {
        public static PokemonDefinition[] CreateDefinitions() => new[] {
            Pokemon("slowpoke","Slowpoke",550,50,.5f,40,1,100,0,"tank","harden","water","psychic"),
            Pokemon("mankey","Mankey",500,50,.65f,20,1,50,0,"melee-dps","fury-swipes","fighting"),
            Pokemon("weedle","Weedle",600,65,.65f,25,3,35,0,"ranged-dps","pin-missile","bug","poison"),
            Pokemon("chansey","Chansey",600,40,.65f,20,2,90,50,"support","soft-boiled","normal")
        };
        private static PokemonDefinition Pokemon(string id,string name,float hp,float attack,float speed,float armor,
            int range,float energy,float starting,string role,string skill,params string[] types) =>
            new PokemonDefinition(id,name,1,new PokemonStats(hp,attack,0,speed,armor,20,range,2,energy,starting,.2f,
                range>1?AttackDeliveryType.Projectile:AttackDeliveryType.Melee,8,.25f,1.3f,1),role,skill,types);
        public static PokemonCatalog CreateCatalog() => new PokemonCatalog(CreateDefinitions());
        public static SkillCatalog CreateSkills() => new SkillCatalog(new[] {
            Buff("harden",.5f,.6f,"harden-r1","harden-r2","harden-r3"),
            Buff("fury-swipes",.3f,.5f,"fury-swipes-r1","fury-swipes-r2","fury-swipes-r3"),
            new SkillDefinition("pin-missile",.35f,.55f,SkillTargetRule.Enemy,TargetLostPolicy.RetargetAtEffect,
                new[]{new SkillEffectDefinition(SkillEffectType.ProjectileDamage,EffectTargetSelector.CastTarget,2,true,
                    DamageType.Physical,projectileSpeed:12,rankValues:new[]{2f,2.3f,3f},valueSource:SkillValueSource.Attack)},
                range:3,energyLock:1),
            new SkillDefinition("soft-boiled",.4f,.5f,SkillTargetRule.LowestHPAlly,TargetLostPolicy.RetargetAtEffect,
                new[]{new SkillEffectDefinition(SkillEffectType.Heal,EffectTargetSelector.CastTarget,175,true,
                    rankValues:new[]{175f,200f,225f})},energyLock:1)
        });
        private static SkillDefinition Buff(string id,float cast,float post,params string[] statuses) =>
            new SkillDefinition(id,cast,post,SkillTargetRule.Self,TargetLostPolicy.Cancel,
                new[]{new SkillEffectDefinition(SkillEffectType.ApplyStatus,EffectTargetSelector.Self,0,
                    statusId:statuses[0],statusTargetTeam:StatusTargetTeam.Ally,rankStatusIds:statuses)},energyLock:1);
        public static StatusCatalog CreateStatuses()
        {
            var entries=new List<StatusEffectDefinition>();
            AddBuffs(entries,"harden",5,CombatStat.Armor,new[]{.6f,.7f,.8f});
            AddBuffs(entries,"fury-swipes",4,CombatStat.AttackSpeed,new[]{.4f,.7f,1f});
            return new StatusCatalog(entries);
        }
        private static void AddBuffs(List<StatusEffectDefinition> entries,string id,float duration,CombatStat stat,float[] values)
        {
            for(int i=0;i<3;i++)entries.Add(new StatusEffectDefinition(id+"-r"+(i+1),duration,StatusTargetTeam.Ally,
                modifiers:new[]{new StatModifierDefinition(stat,StatModifierType.Percentage,values[i])}));
        }
    }
}
