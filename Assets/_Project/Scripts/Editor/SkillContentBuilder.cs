using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using UnityEditor;
using UnityEngine;
using PokeChess.Content;
using PokeChess.Core.Battle;
using PokeChess.Core.Board;
using PokeChess.Core.Pokemon;
using PokemonAnimationLibrary = PokeChess.Client.Data.PokemonAnimationLibrary;

namespace PokeChess.Editor
{
    // Explicit recipes consume a checked-in source extract. Never infer game mechanics from prose at runtime.
    public static class SkillContentBuilder
    {
        [Serializable] public sealed class Magnitudes {public float[] Values;}
        [Serializable] public sealed class Row {
            public int Dex,Cost,AttackRange;public string Id,Name,Skill,Effect,Notes,Role;
            public Magnitudes[] Damage,Ratios;public float[] HP,Attack;
            public float AttackSpeed,MaxEnergy,StartingEnergy,Armor,MagicResistance;
        }
        [Serializable] public sealed class Family {public string[] Ids;}
        [Serializable] public sealed class Plan {public Row[] Rows;public Family[] Families;}
        private const string Content="Assets/_Project/Content/";
        private static readonly List<SkillData> skills=new List<SkillData>();
        private static readonly List<StatusData> statuses=new List<StatusData>();
        private static readonly List<string> defaults=new List<string>(),fallbacks=new List<string>();
        private static string prefix;private static int rank,serial;
        private static string Signature(object value)
        {
            if(value==null)return "null";var type=value.GetType();
            if(value is string text)return text;
            if(type.IsPrimitive||type.IsEnum)return Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture);
            if(value is System.Collections.IEnumerable sequence)return "["+string.Join(",",sequence.Cast<object>().Select(Signature))+"]";
            return type.Name+"{"+string.Join(";",type.GetProperties().Where(p=>p.GetIndexParameters().Length==0&&p.Name!="Id"&&p.Name!="StackGroup")
                .OrderBy(p=>p.Name,StringComparer.Ordinal).Select(p=>{
                    var item=p.GetValue(value);
                    if(p.Name=="VariantIds")return p.Name+"="+string.Join(",",((IEnumerable<string>)item).Select(id=>Signature(skills.First(s=>s.Id==id).ToCore())));
                    if(p.Name=="RankStatusIds"||p.Name=="StatusChoices")return p.Name+"="+string.Join(",",((IEnumerable<string>)item).Select(id=>id==null?"null":Signature(statuses.First(s=>s.Id==id).ToCore())));
                    if(p.Name=="StatusId")return p.Name+"="+(item==null?"null":Signature(statuses.First(s=>s.Id==(string)item).ToCore()));
                    return p.Name+"="+Signature(item);
                }))+"}";
        }
        private static T Asset<T>(string folder,string id) where T:ScriptableObject
        {
            Directory.CreateDirectory(Content+folder);var path=Content+folder+"/"+id+".asset";
            var value=AssetDatabase.LoadAssetAtPath<T>(path);if(value==null){value=ScriptableObject.CreateInstance<T>();value.name=id;AssetDatabase.CreateAsset(value,path);}return value;
        }
        private static float Ratio(Row row,int index=0)
        {
            return Value(row,row.Ratios,index,"ratio");
        }
        private static float DamageValue(Row row,int index=0)=>Value(row,row.Damage,index,"damage");
        private static float Value(Row row,Magnitudes[] values,int index,string kind)
        {
            if(values==null||index<0||index>=values.Length||values[index].Values==null||values[index].Values.Length!=3)
                throw new InvalidOperationException(row.Id+": missing typed "+kind+" "+index);
            return values[index].Values[rank];
        }
        private static SkillSpatialData Area(int extent,SkillAreaCenter center=SkillAreaCenter.Caster,SkillAreaShape shape=SkillAreaShape.Radius,int max=0,bool self=false)=>
            new SkillSpatialData{Extent=extent,Center=center,Shape=shape,MaxTargets=max,IncludeSelf=self};
        private static SkillEffectData Damage(Row row,SkillSpatialData area=null,float? value=null)=>new SkillEffectData{Type=SkillEffectType.Damage,
            TargetSelector=area==null?EffectTargetSelector.CastTarget:EffectTargetSelector.Spatial,Spatial=area,BaseValue=value??DamageValue(row),
            RankValues=Enumerable.Repeat(value??DamageValue(row),3).ToArray(),ValueSource=SkillValueSource.Flat,PowerScaling=SkillPowerScaling.TotalSpellPower,
            DamageType=row.Effect.Contains("특수")?DamageType.Magic:DamageType.Physical,SpellPowerScalable=true};
        private static SkillEffectData Numeric(SkillEffectData e,Action<SkillNumericData> set){e.HasNumeric=true;set(e.Numeric);return e;}
        private static StatusData State(StatusBehavior behavior,float duration,StatusTargetTeam team=StatusTargetTeam.Enemy,float value=0,
            CrowdControlKind cc=CrowdControlKind.None,params StatModifierData[] modifiers)
        {
            var s=Asset<StatusData>("Statuses","status-"+prefix.Substring("skill-".Length)+"-s"+(serial++));s.Id=s.name;s.Duration=duration;s.TargetTeam=team;s.Behavior=behavior;s.CrowdControl=cc;
            s.Modifiers=modifiers;s.StackGroup=s.Id;s.StackPolicy=StackPolicy.Refresh;s.SourceRule=SourceStackRule.PerSource;s.MaxStack=1;
            s.FixedSpellDamage=cc==CrowdControlKind.Root&&value>0;s.EffectValue=value;s.RecoveryFraction=0;s.EnergyRefundFraction=0;s.RestReduction=.3f;s.ReflectionMultiplier=0;statuses.Add(s);EditorUtility.SetDirty(s);return s;
        }
        private static StatModifierData Mod(CombatStat stat,float value,StatModifierType type=StatModifierType.Percentage)=>new StatModifierData{Stat=stat,Type=type,Value=value};
        private static SkillEffectData StatusEffect(StatusData status,SkillSpatialData area=null,bool self=false)=>new SkillEffectData{Type=SkillEffectType.ApplyStatus,
            TargetSelector=self?EffectTargetSelector.Self:area==null?EffectTargetSelector.CastTarget:EffectTargetSelector.Spatial,Spatial=area,
            Status=status,StatusTargetTeam=status.TargetTeam,RankStatuses=new[]{status,status,status}};
        private static SkillEffectData CC(CrowdControlKind cc,float seconds,SkillSpatialData area=null)=>StatusEffect(State(StatusBehavior.None,seconds,cc:cc),area);
        private static SkillEffectData Debuff(StatusBehavior behavior,float seconds,SkillSpatialData area=null,float value=0)=>StatusEffect(State(behavior,seconds,value:value),area);
        private static SkillEffectData Buff(float seconds,params StatModifierData[] modifiers)=>StatusEffect(State(StatusBehavior.None,seconds,StatusTargetTeam.Ally,modifiers:modifiers),self:true);
        private static SkillEffectData Move(SkillMovementKind kind,int distance=1,params SkillEffectData[] impact)=>new SkillEffectData{Type=SkillEffectType.Move,
            TargetSelector=kind==SkillMovementKind.JumpToCluster?EffectTargetSelector.Self:EffectTargetSelector.CastTarget,
            Movement=new SkillMovementData{Kind=kind,Distance=distance,ClusterRadius=kind==SkillMovementKind.JumpToCluster?impact.Where(e=>e.Spatial?.Shape==SkillAreaShape.Radius).Select(e=>e.Spatial.Extent).DefaultIfEmpty(1).First():1,ImpactEffects=impact}};
        private static SkillEffectData Hp(SkillEffectType type,float ratio,bool target=false,int radius=-1)=>new SkillEffectData{Type=type,BaseValue=ratio,
            RankValues=new[]{ratio,ratio,ratio},ValueSource=target?SkillValueSource.TargetMaxHP:SkillValueSource.CasterMaxHP,
            TargetSelector=radius<0?EffectTargetSelector.Self:EffectTargetSelector.Spatial,Spatial=radius<0?null:Area(radius,self:true)};
        private static SkillStepData Step(float delay,params SkillEffectData[] effects)=>new SkillStepData{Delay=delay,Effects=effects};
        private static StatusData Slow(){var s=State(StatusBehavior.None,4,cc:CrowdControlKind.Slow);s.SlowFraction=.3f;return s;}
        private static SkillEffectData Ground(StatusData status,int radius,float seconds)=>new SkillEffectData{Type=SkillEffectType.GroundStatus,
            TargetSelector=EffectTargetSelector.Spatial,Spatial=Area(radius,SkillAreaCenter.CastTarget),Status=status,BaseValue=seconds,RankValues=new[]{seconds,seconds,seconds}};
        private static void Repeat(SkillData skill,SkillEffectData[] hit,int count,float interval=.2f,SkillEffectData[] last=null,bool reselect=false)
        {
            var steps=Enumerable.Range(0,count).Select(i=>new SkillStepData{Delay=i*interval,Targeting=reselect?SkillStepTargeting.Reselect:SkillStepTargeting.KeepTarget,
                Effects=i==count-1&&last!=null?hit.Concat(last).ToArray():hit}).ToArray();skill.Steps=steps;
        }
        private static SkillData Leaf(Row r,int star,string suffix="")
        {
            rank=star;prefix="skill-"+r.Id+"-r"+(star+1)+suffix;serial=0;
            var s=Asset<SkillData>("Skills",prefix);s.Id=prefix;s.DisplayName=r.Skill;s.Description=r.Effect;s.Notes=r.Notes;
            s.Effects=Array.Empty<SkillEffectData>();s.Steps=Array.Empty<SkillStepData>();s.VariantIds=Array.Empty<string>();s.VariantMode=SkillVariantMode.Random;
            s.TargetRule=SkillTargetRule.Enemy;s.TargetLostPolicy=TargetLostPolicy.Cancel;s.Range=3;
            s.CastTime=.4f;s.PostCastTime=.4f;s.Interruptible=true;s.HasEnergyLock=true;s.EnergyLock=1;s.HasActivation=false;
            skills.Add(s);EditorUtility.SetDirty(s);return s;
        }
        private static void Recipe(Row r,SkillData s)
        {
            var e=new List<SkillEffectData>();SkillSpatialData area=null;float duration;
            switch(r.Skill) {
                case "실뿜기":e.Add(CC(CrowdControlKind.Root,3));e.Add(StatusEffect(State(StatusBehavior.None,3,modifiers:new[]{Mod(CombatStat.AttackSpeed,-Ratio(r))})));break;
                case "단단해지기":s.TargetRule=SkillTargetRule.Self;e.Add(Buff(4,Mod(CombatStat.Armor,Ratio(r)),Mod(CombatStat.DamageReduction,Ratio(r,1),StatModifierType.Flat)));break;
                case "수면가루":case "노래하기":s.TargetRule=SkillTargetRule.Self;e.Add(Debuff(StatusBehavior.Sleep,r.Dex==12?new[]{1.5f,1.75f,2}[rank]:new[]{2f,2.25f,2.5f}[rank],Area(2)));break;
                case "독침":e.Add(Damage(r));e.Add(Debuff(StatusBehavior.Poison,4));break;
                case "더블니들":Repeat(s,new[]{Damage(r),Debuff(StatusBehavior.Poison,4)},2);break;
                case "두번차기":Repeat(s,new[]{Damage(r)},2);break;
                case "바람일으키기":case "날개치기":s.Range=r.Dex==16?2:3;e.Add(Damage(r,Area(s.Range,shape:SkillAreaShape.Line)));break;
                case "고속이동":s.Range=56;e.Add(Buff(5,Mod(CombatStat.AttackSpeed,Ratio(r)),Mod(CombatStat.AttackRange,1,StatModifierType.Flat)));e.Add(Move(SkillMovementKind.Retreat));break;
                case "뱀눈초리":e.Add(Debuff(StatusBehavior.Paralysis,3));e.Add(StatusEffect(State(StatusBehavior.None,3,modifiers:new[]{Mod(CombatStat.Attack,-Ratio(r))})));break;
                case "용해액":s.Range=3;area=Area(3,shape:SkillAreaShape.Line);e.Add(Damage(r,area));e.Add(StatusEffect(State(StatusBehavior.None,3,modifiers:new[]{Mod(CombatStat.Armor,-.25f)}),area));break;
                case "모래뿌리기":e.Add(Debuff(StatusBehavior.Blind,new[]{2f,3,4}[rank]));break;
                case "베어가르기":e.Add(Damage(r));e.Add(Debuff(StatusBehavior.Bleed,4));break;
                case "버섯포자":case "최면술":e.Add(Debuff(StatusBehavior.Sleep,r.Skill=="버섯포자"?new[]{2.5f,2.75f,3}[rank]:new[]{2f,2.25f,2.5f}[rank]));break;
                case "구멍파기":s.CastTime=0;s.Steps=new[]{Step(0,StatusEffect(State(StatusBehavior.Untargetable,1,StatusTargetTeam.Ally),self:true)),Step(1,Move(SkillMovementKind.BurrowToTarget,1,Damage(r)))};break;
                case "지진":s.TargetRule=SkillTargetRule.Self;area=Area(2);e.Add(Damage(r,area));e.Add(CC(CrowdControlKind.Stun,1,area));break;
                case "염동력":case "잼잼펀치":case "환상빔":e.Add(Damage(r));e.Add(Debuff(StatusBehavior.Confusion,r.Dex==64?new[]{1f,2,3}[rank]:new[]{1f,1.5f,2}[rank]));break;
                case "하이드로펌프":s.Range=4;area=Area(4,shape:SkillAreaShape.Line);e.Add(Damage(r,area));var push=Move(SkillMovementKind.Knockback);push.TargetSelector=EffectTargetSelector.Spatial;push.Spatial=area;e.Add(push);break;
                case "태권당수":e.Add(Damage(r));e.Add(StatusEffect(State(StatusBehavior.GuaranteedCritical,3600,StatusTargetTeam.Ally),self:true));break;
                case "지구던지기":e.Add(Move(SkillMovementKind.Throw,2,Damage(r),Damage(r,Area(1,SkillAreaCenter.CastTarget),DamageValue(r,1))));break;
                case "휘감기":s.Range=3;if(r.Dex==114){e.Add(StatusEffect(State(StatusBehavior.None,2,value:DamageValue(r),cc:CrowdControlKind.Root),Area(3,max:2)));}else{area=Area(3,max:2);e.Add(Damage(r,area));e.Add(CC(CrowdControlKind.Root,2,area));}break;
                case "조이기":e.Add(Damage(r));e.Add(CC(CrowdControlKind.Root,2));break;
                case "칼춤":s.TargetRule=SkillTargetRule.Self;var dance=State(StatusBehavior.SwordDance,6,StatusTargetTeam.Ally,Ratio(r,1),modifiers:new[]{Mod(CombatStat.AttackSpeed,Ratio(r))});e.Add(StatusEffect(dance,self:true));break;
                case "자폭":case "대폭발":s.TargetRule=SkillTargetRule.Self;e.Add(Hp(SkillEffectType.HealthCost,r.Skill=="자폭"?.2f:.3f));e.Add(Damage(r,Area(r.Skill=="자폭"?1:2)));break;
                case "전기쇼크":case "번개펀치":e.Add(Damage(r));e.Add(Debuff(StatusBehavior.Paralysis,r.Dex==25?2:new[]{1f,1.5f,2}[rank]));break;
                case "10만볼트":e.Add(Damage(r));e.Add(Damage(r,new SkillSpatialData{Mode=SpatialTargetMode.ChainEnemies,MaxTargets=2,IncludePrimary=false},DamageValue(r,1)));break;
                case "작아지기":s.TargetRule=SkillTargetRule.Self;e.Add(Buff(4,Mod(CombatStat.Evasion,Ratio(r),StatModifierType.Flat)));break;
                case "연속뺨치기":var variants=new List<string>();foreach(int hits in new[]{3,4,5}){var leaf=Leaf(r,rank,"-hits"+hits);Repeat(leaf,new[]{Damage(r)},hits,last:new[]{Move(SkillMovementKind.Knockback)});variants.Add(leaf.Id);}s.VariantIds=variants.ToArray();break;
                case "흡수":e.Add(Numeric(Damage(r),n=>n.Lifesteal=.6f));break;
                case "꽃잎댄스":s.TargetRule=SkillTargetRule.Self;s.CastTime=0;s.Steps=new[]{Step(1,Damage(r,Area(2))),Step(2,Damage(r,Area(2))),Step(3,Damage(r,Area(2)),StatusEffect(State(StatusBehavior.ActionRecovery,1,StatusTargetTeam.Ally),self:true))};break;
                case "고양이돈받기":e.Add(Numeric(Damage(r),n=>n.Bounty=true));break;
                case "짓밟기":case "뼈다귀치기":case "힘껏치기":e.Add(Damage(r));e.Add(CC(CrowdControlKind.Stun,.8f));if(r.Dex==147){var back=Damage(r,value:DamageValue(r,1));back.TargetSelector=EffectTargetSelector.BehindCastTarget;e.Add(back);}break;
                case "회오리불꽃":area=Area(rank==2?2:1,SkillAreaCenter.CastTarget);s.Steps=new[]{new SkillStepData{Lifetime=SkillStepLifetime.Independent,Count=3,Interval=1,FirstDelay=1,Effects=new[]{Damage(r,area)}},Step(0,Ground(Slow(),area.Extent,3))};break;
                case "전기자석파":e.Add(Debuff(StatusBehavior.Paralysis,new[]{3f,3.25f,3.5f}[rank],new SkillSpatialData{Mode=SpatialTargetMode.ChainEnemies,MaxTargets=2,IncludePrimary=true}));break;
                case "소닉붐":s.Range=3;e.Add(Numeric(Damage(r,Area(3,shape:SkillAreaShape.Line)),n=>n.MagicPenetration=.3f));break;
                case "회전부리":e.Add(Move(SkillMovementKind.DashToTarget,1,Damage(r)));break;
                case "트라이어택":var states=new[]{State(StatusBehavior.Burn,r.Dex==85?new[]{1f,1.5f,2}[rank]:new[]{2f,3,99}[rank]),State(StatusBehavior.Freeze,r.Dex==85?new[]{1f,1.5f,2}[rank]:new[]{2f,3,99}[rank]),State(StatusBehavior.Paralysis,r.Dex==85?new[]{1f,1.5f,2}[rank]:new[]{2f,3,99}[rank])};
                    area=r.Dex==137&&rank==2?Area(56,shape:SkillAreaShape.All):null;var random=new SkillEffectData{Type=SkillEffectType.RandomStatus,TargetSelector=area==null?EffectTargetSelector.CastTarget:EffectTargetSelector.Spatial,Spatial=area,ChoiceStatuses=states};
                    if(r.Dex==85)Numeric(random,n=>n.Chance=.3f);var bolts=Damage(r,area);bolts.Type=SkillEffectType.ProjectileDamage;bolts.ProjectileGroupSize=3;Repeat(s,new[]{bolts},3);s.Steps[2].Effects=new[]{Damage(r,area)};var finalBolt=s.Steps[2].Effects[0];finalBolt.Type=SkillEffectType.ProjectileDamage;finalBolt.ProjectileGroupSize=3;finalBolt.PathImpactEffects=r.Dex==137&&rank==2?states.Select(t=>StatusEffect(t)).ToArray():new[]{random};
                    if(area!=null)foreach(var child in finalBolt.PathImpactEffects){child.TargetSelector=EffectTargetSelector.CastTarget;child.Spatial=null;}break;
                case "껍질끼우기":e.Add(Damage(r));e.Add(StatusEffect(State(StatusBehavior.None,2,value:DamageValue(r,1),cc:CrowdControlKind.Root)));break;
                case "가시대포":var spike=Damage(r);spike.Type=SkillEffectType.ProjectileDamage;Repeat(s,new[]{spike},4,reselect:true);break;
                case "뼈다귀부메랑":s.Range=4;var bone=Damage(r,Area(4,shape:SkillAreaShape.Line));bone.Type=SkillEffectType.PathProjectileDamage;bone.Returns=true;bone.ProjectileSpeed=8;e.Add(bone);break;
                case "스모그":area=Area(2,SkillAreaCenter.CastTarget);e.Add(Damage(r,area));e.Add(Debuff(StatusBehavior.Poison,4,area));break;
                case "오물공격":e.Add(Damage(r));e.Add(Debuff(StatusBehavior.Poison,4,Area(1,SkillAreaCenter.CastTarget)));break;
                case "연막":e.Add(Ground(State(StatusBehavior.Blind,4),2,new[]{1.5f,2,2.5f}[rank]));break;
                case "덩굴채찍":e.Add(Damage(r));e.Add(CC(CrowdControlKind.Root,1));break;
                case "잎날가르기":s.Range=7;e.Add(Damage(r,Area(7,shape:SkillAreaShape.Line,max:3)));break;
                case "솔라빔":s.CastTime=1;s.Range=4;e.Add(Damage(r,Area(4,shape:SkillAreaShape.Line)));break;
                case "불꽃세례":e.Add(Damage(r));e.Add(Debuff(StatusBehavior.Burn,3));break;
                case "화염방사":s.Range=3;area=Area(3,shape:SkillAreaShape.Cone);e.Add(Damage(r,area));e.Add(Debuff(StatusBehavior.Burn,3,area));break;
                case "물대포":e.Add(Damage(r));e.Add(Move(SkillMovementKind.Knockback));break;
                case "로켓박치기":s.CastTime=0;s.Steps=new[]{Step(0,Buff(1,Mod(CombatStat.Armor,.4f))),Step(1,Move(SkillMovementKind.DashToTarget,1,Damage(r)))};break;
                case "망각술":s.TargetRule=SkillTargetRule.Self;e.Add(Buff(5,Mod(CombatStat.MagicResistance,Ratio(r))));break;
                case "지옥의바퀴":var wheel=new[]{Damage(r),CC(CrowdControlKind.Stun,1),Hp(SkillEffectType.HealthCost,.08f)};if(r.Dex==62)e.Add(Move(SkillMovementKind.CarryTarget,2,wheel));else e.AddRange(wheel);break;
                case "사이코키네시스":area=r.Dex==150?Area(new[]{2,3,9}[rank],SkillAreaCenter.CastTarget):null;e.Add(Damage(r,area));e.Add(StatusEffect(State(StatusBehavior.None,r.Dex==150?new[]{4f,4,99}[rank]:3,modifiers:new[]{Mod(CombatStat.MagicResistance,r.Dex==150?-Ratio(r):-.25f)}),area));break;
                case "무릎차기":s.TargetRule=SkillTargetRule.FarthestEnemy;s.Range=56;e.Add(Move(SkillMovementKind.JumpToTarget,1,Damage(r),Hp(SkillEffectType.HealthCost,.08f)));break;
                case "빛의장막":s.TargetRule=SkillTargetRule.Self;e.Add(StatusEffect(State(StatusBehavior.None,4,StatusTargetTeam.Ally,modifiers:new[]{Mod(CombatStat.MagicResistance,Ratio(r))}),Area(56,shape:SkillAreaShape.All,self:true)));break;
                case "배리어":s.TargetRule=SkillTargetRule.Self;var shield=Hp(SkillEffectType.Shield,Ratio(r),true);shield.TargetSelector=EffectTargetSelector.Spatial;shield.Spatial=new SkillSpatialData{Mode=SpatialTargetMode.NearestAllies,MaxTargets=2};e.Add(Numeric(shield,n=>{n.HasShieldDuration=true;n.ShieldDuration=4;}));break;
                case "악마의키스":e.Add(Debuff(StatusBehavior.Sleep,new[]{2.5f,2.75f,3}[rank]));e.Add(Hp(SkillEffectType.Heal,Ratio(r)));break;
                case "파괴광선":s.CastTime=.8f;s.Range=r.Dex==142?3:4;e.Add(Damage(r,Area(s.Range,shape:SkillAreaShape.Line)));e.Add(StatusEffect(State(StatusBehavior.AttackRecovery,1,StatusTargetTeam.Ally),self:true));break;
                case "누르기":var guard=Numeric(Hp(SkillEffectType.Shield,Ratio(r)),n=>{n.HasShieldDuration=true;n.ShieldDuration=4;});e.Add(Move(SkillMovementKind.JumpToCluster,1,Damage(r,Area(1)),CC(CrowdControlKind.Stun,1,Area(1)),guard));break;
                case "뿔찌르기":e.Add(Damage(r));e.Add(Move(SkillMovementKind.Knockback));break;
                case "뿔드릴":e.Add(Numeric(Damage(r),n=>{n.Condition=SkillValueCondition.TargetLowHP;n.Threshold=.25f;n.ConditionalValues=new[]{DamageValue(r,1),DamageValue(r,1),DamageValue(r,1)};}));break;
                case "난동부리기":s.TargetRule=SkillTargetRule.Self;e.Add(StatusEffect(State(StatusBehavior.AttackSplash,3,StatusTargetTeam.Ally,.5f,modifiers:new[]{Mod(CombatStat.AttackSpeed,Ratio(r))}),self:true));break;
                case "불대문자":area=Area(rank==2?2:1,SkillAreaCenter.CastTarget,SkillAreaShape.Cross);e.Add(Damage(r,area));e.Add(Debuff(StatusBehavior.Burn,4,area));break;
                case "순간이동":s.TargetRule=SkillTargetRule.Self;s.CastTime=0;s.PostCastTime=0;var focus=State(StatusBehavior.Focus,2,StatusTargetTeam.Ally);focus.EnergyRefundFraction=new[]{.3f,.4f,.5f}[rank];e.Add(StatusEffect(focus,self:true));break;
                case "안다리걸기":e.Add(Damage(r));e.Add(Numeric(CC(CrowdControlKind.Stun,1),n=>{n.Condition=SkillValueCondition.TargetHigherMaxHP;n.ConditionalValues=new float[3];}));break;
                case "돌떨구기":e.Add(Damage(r));e.Add(Numeric(CC(CrowdControlKind.Stun,1),n=>n.Chance=.25f));break;
                case "이상한빛":e.Add(Debuff(StatusBehavior.Confusion,new[]{2f,2.5f,3}[rank],Area(1,SkillAreaCenter.CastTarget,max:3),Ratio(r)));break;
                case "꿈먹기":e.Add(Numeric(Damage(r),n=>{n.Condition=SkillValueCondition.TargetSleeping;n.ConditionalValues=new[]{DamageValue(r,1),DamageValue(r,1),DamageValue(r,1)};n.Lifesteal=.6f;}));break;
                case "가위자르기":e.Add(Numeric(Damage(r),n=>n.ExecuteThreshold=.25f));break;
                case "파도타기":s.Range=4;area=Area(3,shape:SkillAreaShape.Wave);e.Add(Damage(r,area));var wave=Move(SkillMovementKind.Knockback);wave.TargetSelector=EffectTargetSelector.Spatial;wave.Spatial=area;e.Add(wave);e.Add(StatusEffect(State(StatusBehavior.None,3,modifiers:new[]{Mod(CombatStat.AttackSpeed,-Ratio(r))}),area));break;
                case "냉동빔":area=rank==2?Area(56,shape:SkillAreaShape.All):null;e.Add(Damage(r,area));e.Add(Debuff(StatusBehavior.Freeze,new[]{2f,4,99}[rank],area));break;
                case "잠자기":s.TargetRule=SkillTargetRule.Self;s.CastTime=0;s.PostCastTime=0;var rest=State(StatusBehavior.RestSleep,2,StatusTargetTeam.Ally);rest.RecoveryFraction=Ratio(r);rest.RestReduction=Ratio(r,1);rest.ReflectionMultiplier=rank==2?9.99f:0;e.Add(StatusEffect(rest,self:true));break;
                case "눈보라":area=Area(56,shape:SkillAreaShape.All);s.Range=56;e.Add(Damage(r,area));e.Add(Numeric(Debuff(StatusBehavior.Freeze,new[]{1.5f,1.5f,99}[rank],area),n=>n.Chance=new[]{.3f,.3f,1}[rank]));break;
                case "번개":e.Add(Damage(r));e.Add(Debuff(StatusBehavior.Paralysis,new[]{3f,3,99}[rank]));var chain=Damage(r,new SkillSpatialData{Mode=SpatialTargetMode.ChainEnemies,MaxTargets=rank==0?2:rank==1?3:0,IncludePrimary=false,ChainRange=56},DamageValue(r,1));chain.Type=SkillEffectType.ChainDamage;chain.ChainDelay=.15f;chain.ChainSequential=rank==2;chain.PathImpactEffects=new[]{Debuff(StatusBehavior.Paralysis,new[]{3f,3,99}[rank])};e.Add(chain);break;
                case "불새":s.CastTime=0;var dive=Move(SkillMovementKind.JumpToCluster,1,Damage(r,Area(new[]{2,3,9}[rank])),Debuff(StatusBehavior.Burn,new[]{3f,3,99}[rank],Area(new[]{2,3,9}[rank])));dive.Movement.HasTravelTime=true;dive.Movement.TravelTime=0;s.Steps=new[]{Step(0,StatusEffect(State(StatusBehavior.Untargetable,1.5f,StatusTargetTeam.Ally),self:true)),Step(1.5f,dive)};break;
                case "손가락흔들기":if(r.Dex!=151)break;var branches=new List<string>();for(int branch=0;branch<3;branch++){var leaf=Leaf(r,rank,"-choice"+branch);leaf.Range=56;leaf.Effects=branch==0?new[]{Damage(r,Area(rank==2?9:2,SkillAreaCenter.CastTarget))}:branch==1?new[]{Hp(SkillEffectType.Heal,Ratio(r),true,56)}:new[]{CC(CrowdControlKind.Stun,new[]{1.5f,1.75f,99}[rank],Area(rank==2?9:2,SkillAreaCenter.CastTarget))};if(branch==1)leaf.TargetRule=SkillTargetRule.Self;branches.Add(leaf.Id);}s.VariantIds=branches.ToArray();break;
                default:throw new InvalidOperationException("No recipe: "+r.Id+"/"+r.Skill);
            }
            s.Effects=e.ToArray();if(s.Steps.Length>0)s.Effects=Array.Empty<SkillEffectData>();
            if(s.TargetRule==SkillTargetRule.Self)s.TargetLostPolicy=TargetLostPolicy.ContinueWithoutTarget;
            EditorUtility.SetDirty(s);
        }
        [MenuItem("PokeChess/Content/Apply Skill Plan")]
        public static void Apply()
        {
            skills.Clear();statuses.Clear();defaults.Clear();fallbacks.Clear();
            var plan=JsonUtility.FromJson<Plan>(File.ReadAllText(Content+"SkillPlan.json"));
            if(plan.Rows.Length!=109||plan.Rows.Select(r=>r.Id).Distinct().Count()!=109)throw new InvalidOperationException("Invalid source plan");
            var roster=AssetDatabase.LoadAssetAtPath<RosterData>("Assets/_Project/Resources/GameRoster.asset");
            var existing=roster.Pokemon.ToDictionary(p=>p.Id);var pokemon=new List<PokemonData>();var bases=new Dictionary<string,SkillData>();
            var library=PokemonAnimationLibrary.Load();
            foreach(var r in plan.Rows) {
                var baseSkill=Asset<SkillData>("Skills","skill-"+r.Id);baseSkill.Id=baseSkill.name;baseSkill.DisplayName=r.Skill;baseSkill.Description=r.Effect;baseSkill.Notes=r.Notes;
                baseSkill.Effects=Array.Empty<SkillEffectData>();baseSkill.Steps=Array.Empty<SkillStepData>();baseSkill.HasActivation=false;baseSkill.VariantMode=SkillVariantMode.Rank;
                baseSkill.VariantIds=r.Id=="clefairy"?Array.Empty<string>():Enumerable.Range(0,3).Select(i=>{var leaf=Leaf(r,i);Recipe(r,leaf);return leaf.Id;}).ToArray();skills.Add(baseSkill);bases[r.Id]=baseSkill;EditorUtility.SetDirty(baseSkill);
                var p=Asset<PokemonData>("Pokemon",r.Id);bool isNew=!existing.ContainsKey(r.Id);
                p.Id=r.Id;p.DisplayName=char.ToUpperInvariant(r.Id[0])+r.Id.Substring(1);p.LocalizedName=r.Name;p.Cost=r.Cost;p.Skill=baseSkill;
                if(isNew){p.Stats=new PokemonStatsData();p.RoleId="melee-dps";p.TypeIds=Array.Empty<string>();p.TraitIds=Array.Empty<string>();}
                p.Stats.MaxHP=r.HP[0];p.Stats.Attack=r.Attack[0];p.Stats.SpellPower=100;
                p.Stats.AttackSpeed=r.AttackSpeed;p.Stats.MaxEnergy=r.MaxEnergy;p.Stats.StartingEnergy=r.StartingEnergy;
                p.Stats.Armor=r.Armor;p.Stats.MagicResistance=r.MagicResistance;p.Stats.AttackRange=r.AttackRange;
                p.Stats.AttackDelivery=r.AttackRange>1?AttackDeliveryType.Projectile:AttackDeliveryType.Melee;
                p.RankHP=r.HP.ToArray();p.RankAttack=r.Attack.ToArray();p.HasProvisionalStats=false;
                if(p.HasProvisionalStats)defaults.Add(r.Id);
                p.Evolutions=Array.Empty<EvolutionData>();
                var visual=Asset<PokemonVisualData>("Visuals",r.Id);var available=library.Animations.Where(a=>a.PokemonId==r.Id).ToArray();
                if(available.Length==0){var fallback=existing.Values.First(v=>v.Visual!=null);visual.AnimationId=fallback.Visual.AnimationId;visual.Idle=fallback.Visual.Idle;visual.Walk=fallback.Visual.Walk;visual.Attack=fallback.Visual.Attack;visual.Hurt=fallback.Visual.Hurt;visual.Skill=fallback.Visual.Skill;fallbacks.Add(r.Id);}
                else {visual.AnimationId=r.Id;Func<string,string> animation=n=>available.Any(a=>a.Name==n)?n:available[0].Name;
                    visual.Idle=animation("Idle");visual.Walk=animation("Walk");visual.Attack=animation("Attack");visual.Hurt=animation("Hurt");visual.Skill=animation("Charge");}
                p.Visual=visual;EditorUtility.SetDirty(visual);EditorUtility.SetDirty(p);pokemon.Add(p);
            }
            var clefairy=bases["clefairy"];clefairy.VariantMode=SkillVariantMode.Random;clefairy.VariantIds=plan.Rows.Where(r=>r.Cost<5&&r.Id!="clefairy")
                .GroupBy(r=>r.Skill+"|"+Signature(bases[r.Id].ToCore())).Select(g=>bases[g.First().Id].Id).ToArray();EditorUtility.SetDirty(clefairy);
            var byId=pokemon.ToDictionary(p=>p.Id);foreach(var family in plan.Families)for(int i=0;i<family.Ids.Length-1;i++) {
                var p=byId[family.Ids[i]];p.Evolutions=new[]{new EvolutionData{Target=byId[family.Ids[i+1]],PointCost=1}};EditorUtility.SetDirty(p);
            }
            var evolved=new HashSet<string>(plan.Families.SelectMany(f=>f.Ids.Skip(1)));roster.Pokemon=pokemon.ToArray();roster.ShopPokemon=pokemon.Where(p=>!evolved.Contains(p.Id)).ToArray();
            roster.Skills=skills.Distinct().ToArray();roster.Statuses=statuses.Distinct().ToArray();EditorUtility.SetDirty(roster);
            AssetDatabase.SaveAssets();GameContent.Reset();
            foreach(var skill in skills)try{skill.ToCore();}catch(Exception ex){throw new InvalidOperationException(skill.Id+": invalid authored skill",ex);}
            roster.Validate();RosterContentValidation.Validate();
            Directory.CreateDirectory("TestResults/SkillContent");File.WriteAllText("TestResults/SkillContent/authoring-report.json",JsonUtility.ToJson(new Report{Pokemon=pokemon.Count,Shop=roster.ShopPokemon.Length,
                Skills=skills.Count,Statuses=statuses.Count,DefaultStats=defaults.ToArray(),FallbackVisuals=fallbacks.ToArray()},true));
            Debug.Log("Applied skill plan: "+pokemon.Count+" forms, "+roster.ShopPokemon.Length+" shop roots, "+skills.Count+" skills.");
        }
        [Serializable] public sealed class Report {public int Pokemon,Shop,Skills,Statuses;public string[] DefaultStats,FallbackVisuals;}
    }
}

