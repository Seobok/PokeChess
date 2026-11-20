using System;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using PokeChess.Content;
using PokeChess.Core.Battle;
using PokeChess.Core.Pokemon;
namespace PokeChess.Editor
{
    [CustomEditor(typeof(SkillData))]
    public sealed class SkillAuthoringEditor:UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root=new VisualElement();
            root.Add(new HelpBox("공통 효과의 Rank Values / Rank Statuses는 1·2·3성 순서입니다. 구조가 달라질 때만 Rank Overrides를 사용하세요. 실행 ID는 자동 생성됩니다.",HelpBoxMessageType.Info));
            InspectorElement.FillDefaultInspector(root,serializedObject,this);
            var result=new HelpBox("검증 및 미리보기를 눌러 현재 설정을 확인하세요.",HelpBoxMessageType.Info);root.Add(result);
            root.Add(new Button(()=>
            {
                try
                {
                    serializedObject.ApplyModifiedProperties();var data=(SkillData)target;data.ToDefinitions().ToArray();
                    var roster=GameContent.Roster;roster.Validate();var pokemon=roster.Pokemon.FirstOrDefault(p=>p.Skill==data);
                    result.text=pokemon==null?"스킬 정의 검증 완료":string.Join("\n",Enumerable.Range(1,3).Select(r=>r+"성 · "+
                        SkillDescription.Format(data,(UnitRank)r,RankRules.Default.Apply(pokemon.ToCore(),(UnitRank)r),roster.CreateStatuses())));
                    result.messageType=HelpBoxMessageType.Info;
                }
                catch(Exception ex){result.text=ex.Message;result.messageType=HelpBoxMessageType.Error;}
            }){text="검증 및 설명 미리보기"});return root;
        }
    }
    [CustomPropertyDrawer(typeof(SkillEffectData))]
    public sealed class SkillEffectDataDrawer:PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root=new Foldout{text=property.displayName,value=property.isExpanded};
            var body=new VisualElement();root.Add(body);
            Action draw=()=>
            {
                body.Clear();
                Action<string> add=name=>body.Add(new PropertyField(property.FindPropertyRelative(name)));
                add("Type");add("TargetSelector");
                var type=(SkillEffectType)property.FindPropertyRelative("Type").enumValueIndex;
                var target=(EffectTargetSelector)property.FindPropertyRelative("TargetSelector").enumValueIndex;
                if(target==EffectTargetSelector.Spatial)add("Spatial");
                if(target==EffectTargetSelector.EnemiesAroundCastTarget||target==EffectTargetSelector.AlliesAroundCaster||target==EffectTargetSelector.EnemiesAroundCaster)add("Radius");
                bool damage=type==SkillEffectType.Damage||type==SkillEffectType.ProjectileDamage||type==SkillEffectType.PathProjectileDamage||type==SkillEffectType.ChainDamage;
                if(damage||type==SkillEffectType.Heal||type==SkillEffectType.Shield||type==SkillEffectType.HealthCost||type==SkillEffectType.EnergyRefund||type==SkillEffectType.GroundStatus)
                {
                    add("RankValues");add("BaseValue");
                    if(type!=SkillEffectType.GroundStatus){add("ValueSource");add("SpellPowerScalable");
                        if(property.FindPropertyRelative("SpellPowerScalable").boolValue){add("PowerScaling");add("ApIncrement");add("ApCap");}}
                }
                if(damage)add("DamageType");
                if(type==SkillEffectType.ApplyStatus){add("Status");add("RankStatuses");add("StatusTargetTeam");}
                if(type==SkillEffectType.GroundStatus)add("Status");
                if(type==SkillEffectType.RandomStatus)add("ChoiceStatuses");
                if(type==SkillEffectType.Move)add("Movement");
                if(type==SkillEffectType.ProjectileDamage||type==SkillEffectType.PathProjectileDamage)
                {add("ProjectileSpeed");add("HasImpactRadius");if(property.FindPropertyRelative("HasImpactRadius").boolValue)add("ImpactRadius");add("PathImpactEffects");}
                if(type==SkillEffectType.ProjectileDamage)add("ProjectileGroupSize");
                if(type==SkillEffectType.PathProjectileDamage)add("Returns");
                if(type==SkillEffectType.ChainDamage){add("ChainDelay");add("ChainSequential");add("PathImpactEffects");}
                add("HasNumeric");if(property.FindPropertyRelative("HasNumeric").boolValue)add("Numeric");
                body.Bind(property.serializedObject);
            };
            draw();
            foreach(var name in new[]{"Type","TargetSelector","SpellPowerScalable","HasImpactRadius","HasNumeric"})
                root.TrackPropertyValue(property.FindPropertyRelative(name),_=>draw());
            return root;
        }
    }
    [CustomPropertyDrawer(typeof(SkillRankOverride))]
    public sealed class SkillRankOverrideDrawer:PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root=new Foldout{text=property.displayName,value=property.isExpanded};var body=new VisualElement();root.Add(body);
            Action draw=()=>
            {
                body.Clear();body.Add(new PropertyField(property.FindPropertyRelative("Rank")));
                Action<string,string[]> group=(flag,names)=>
                {body.Add(new PropertyField(property.FindPropertyRelative(flag)));if(property.FindPropertyRelative(flag).boolValue)
                    foreach(var name in names)body.Add(new PropertyField(property.FindPropertyRelative(name)));};
                group("OverrideTiming",new[]{"CastTime","PostCastTime","Interruptible","HasEnergyLock","EnergyLock"});
                group("OverrideTargeting",new[]{"TargetRule","TargetLostPolicy","Range"});
                group("OverrideActivation",new[]{"HasActivation","Activation"});
                group("OverrideEffects",new[]{"Effects","Steps"});
                group("OverrideVariants",new[]{"VariantIds","VariantMode"});body.Bind(property.serializedObject);
            };
            draw();foreach(var flag in new[]{"OverrideTiming","OverrideTargeting","OverrideActivation","OverrideEffects","OverrideVariants"})root.TrackPropertyValue(property.FindPropertyRelative(flag),_=>draw());return root;
        }
    }
}
