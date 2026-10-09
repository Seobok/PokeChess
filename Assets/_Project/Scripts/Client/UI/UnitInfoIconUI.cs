using System;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using PokeChess.Core.Pokemon;
namespace PokeChess.Client.UI
{
    public sealed partial class PlacementSandboxView
    {
        private RectTransform unitInfoRoot,unitInfoTooltip;
        private UnityEngine.UI.Text unitInfoTitle,unitInfoTypes,unitInfoTraits,unitInfoItems,unitInfoTooltipText;
        private readonly UnityEngine.UI.Text[] unitStatValues=new UnityEngine.UI.Text[7];
        private string[] unitStatHints=new string[7];
        private string unitSkillHint;
        private int hoveredUnitInfo=-2;
        private readonly string[] statNames={"체력","공격력","공격속도","방어력","마법저항","사거리","에너지"};
        private static string KoreanInfo(string id)
        {
            switch(id){
                case "bulbasaur":return "이상해씨";case "charmander":return "파이리";case "squirtle":return "꼬부기";case "caterpie":return "캐터피";case "weedle":return "뿔충이";case "mankey":return "망키";case "abra":return "캐이시";case "geodude":return "꼬마돌";case "slowpoke":return "야돈";case "magnemite":return "코일";case "gastly":return "고오스";case "chansey":return "럭키";
                case "normal":return "노말";case "fire":return "불꽃";case "water":return "물";case "grass":return "풀";case "poison":return "독";case "bug":return "벌레";case "fighting":return "격투";case "psychic":return "에스퍼";case "rock":return "바위";case "ground":return "땅";case "electric":return "전기";case "steel":return "강철";case "ghost":return "고스트";
                case "tank":return "탱커";case "support":return "지원";case "carry":return "딜러";case "caster":return "기술 딜러";
                case "guardian":return "수호";case "assault":return "돌격";case "focus":return "집중";case "link":return "연결";
                case "bruiser":return "전사";
                case "growth":return "성장";
                case "harden":return "단단해지기";case "fury-swipes":return "마구할퀴기";case "pin-missile":return "바늘미사일";case "soft-boiled":return "알낳기";case "rock-guard":return "바위 수호";case "shadow-burst":return "그림자 폭발";case "string-shot":return "실뿜기";case "magnet-bomb":return "자석 폭탄";case "aqua-guard":return "물의 수호";case "psychic-burst":return "초능력 폭발";case "flame-drive":return "불꽃 돌진";
                default:return Readable(id);
            }
        }
        private void BuildUnitInfoIcons()
        {
            if(unitInfoRoot!=null)return;
            var library=Resources.Load<UnitInfoIconLibrary>("UnitInfoIcons");
            unitInfoRoot=Rect("UnitInfoIcons",canvasRect,new Vector2(215,200),new Vector2(490,170));
            unitInfoTitle=ItemLabel("Name",unitInfoRoot,"",15,new Vector2(215,26),new Vector2(0,78));
            unitInfoTypes=ItemLabel("Types",unitInfoRoot,"",11,new Vector2(215,18),new Vector2(0,59));
            unitInfoTraits=ItemLabel("Traits",unitInfoRoot,"",10,new Vector2(215,16),new Vector2(0,43));
            for(int i=0;i<7;i++){
                int index=i;var cell=Rect("Stat_"+i,unitInfoRoot,new Vector2(104,20),new Vector2(i%2==0?-53:53,24-i/2*20));
                var hit=cell.gameObject.AddComponent<UnityEngine.UI.Image>();hit.color=new Color(.1f,.17f,.24f,.5f);
                var icon=Rect("Icon",cell,new Vector2(24,20),new Vector2(-39,0)).gameObject.AddComponent<UnityEngine.UI.Image>();icon.sprite=library?.icons!=null&&i<library.icons.Length?library.icons[i]:null;icon.preserveAspect=true;icon.raycastTarget=false;
                unitStatValues[i]=ItemLabel("Value",cell,"",11,new Vector2(72,20),new Vector2(13,0));
                AddUnitInfoHover(cell,index);
            }
            var skill=ItemButton("기술 정보",unitInfoRoot,new Vector2(204,22),new Vector2(0,-58),()=>{ShowUnitInfoHint(-1);});AddUnitInfoHover(skill.transform,-1);
            unitInfoItems=ItemLabel("Equipped",unitInfoRoot,"",10,new Vector2(212,16),new Vector2(0,-76));
            unitInfoTooltip=Panel("UnitInfoTooltip",new Vector2(370,175),new Vector2(175,174));unitInfoTooltip.GetComponent<UnityEngine.UI.Image>().raycastTarget=false;
            unitInfoTooltipText=ItemLabel("Hint",unitInfoTooltip,"",12,new Vector2(346,157),Vector2.zero);unitInfoTooltipText.alignment=TextAnchor.MiddleLeft;unitInfoTooltip.gameObject.SetActive(false);
        }
        private void AddUnitInfoHover(Transform target,int index)
        {
            var trigger=target.gameObject.AddComponent<EventTrigger>();
            var enter=new EventTrigger.Entry{eventID=EventTriggerType.PointerEnter};enter.callback.AddListener(_=>ShowUnitInfoHint(index));
            var exit=new EventTrigger.Entry{eventID=EventTriggerType.PointerExit};exit.callback.AddListener(_=>HideUnitInfoHint());trigger.triggers.Add(enter);trigger.triggers.Add(exit);
        }
        private void ShowUnitInfoHint(int index)
        {hoveredUnitInfo=index;unitInfoTooltip.sizeDelta=new Vector2(370,index<0?175:90);unitInfoTooltipText.rectTransform.sizeDelta=new Vector2(346,index<0?157:72);unitInfoTooltipText.text=index<0?unitSkillHint:unitStatHints[index];unitInfoTooltip.gameObject.SetActive(true);unitInfoTooltip.SetAsLastSibling();}
        private void HideUnitInfoHint(){hoveredUnitInfo=-2;if(unitInfoTooltip!=null)unitInfoTooltip.gameObject.SetActive(false);}
        private void RenderUnitInfoIcons(PokemonDefinition definition,UnitRank rank,PokemonStats stats,double hp,double maxHP,double energy,double maxEnergy,double shield,string[] items,bool baseStats=false)
        {
            BuildUnitInfoIcons();bool has=definition!=null;unitInfoRoot.gameObject.SetActive(has);detailLabel.gameObject.SetActive(!has);
            if(!has){detailLabel.font=itemFont;detailLabel.text="포켓몬을 선택하세요\n보드 또는 벤치의 포켓몬을\n클릭하면 정보를 볼 수 있습니다.";HideUnitInfoHint();return;}
            unitInfoTitle.text=KoreanInfo(definition.Id)+"  "+new string('★',(int)rank);
            unitInfoTypes.text=KoreanInfo(definition.RoleId)+" · "+string.Join(" / ",definition.TypeIds.Select(KoreanInfo));
            unitInfoTraits.text=string.Join(" · ",definition.TraitIds.Select(KoreanInfo));
            string[] values={Number((float)hp)+"/"+Number((float)maxHP),Number(stats.Attack),Number(stats.AttackSpeed),Number(stats.Armor),Number(stats.MagicResistance),stats.AttackRange.ToString(),Number((float)energy)+"/"+Number((float)maxEnergy)};
            string[] hints={"현재 체력 / 최대 체력"+(shield>0?"\n보호막: "+Number((float)shield):""),"기본 공격의 피해량","초당 기본 공격 횟수","물리 피해를 줄이는 능력치","마법 피해를 줄이는 능력치","기본 공격이 닿는 칸 수","현재 에너지 / 기술에 필요한 에너지"};
            for(int i=0;i<7;i++){unitStatValues[i].text=values[i];unitStatHints[i]=statNames[i]+"\n"+hints[i]+(baseStats&&i>0&&i<6?"\n성급 기준 수치 (전투 중 강화 효과 제외)":"");}
            unitInfoItems.text=items.Length==0?"장착 도구 없음":"장착: "+string.Join(" · ",items.Select(PokeChess.Core.Match.ItemCatalog.DisplayName));
            unitSkillHint=ContextSkill(definition,rank,stats);
            unitSkillHint=unitSkillHint.Replace(Readable(definition.SkillId),KoreanInfo(definition.SkillId));
            foreach(var pair in new[]{("Lowest HP ally","체력이 가장 낮은 아군"),("Enemies r","범위 내 적 · 반경 "),("Allies r","범위 내 아군 · 반경 "),(" + splash r"," · 폭발 반경 "),("True damage","고정 피해"),("AttackSpeed","공격속도"),("Attack","공격력"),("SpellPower","주문력"),("Duration","지속 시간")})unitSkillHint=unitSkillHint.Replace(pair.Item1,pair.Item2);
            foreach(var pair in new[]{("Self","자신"),("Target","대상"),("Enemies","적"),("Allies","아군"),("Physical damage","물리 피해"),("Magic damage","마법 피해"),("Shield","보호막"),("Heal","회복"),("cast","시전"),("Stun","기절"),("Taunt","도발"),("Slow","둔화"),("AttackSpeed","공격속도"),("Armor","방어력"),("MagicResistance","마법저항")})unitSkillHint=unitSkillHint.Replace(pair.Item1,pair.Item2);
            if(hoveredUnitInfo!=-2)ShowUnitInfoHint(hoveredUnitInfo);
        }
    }
}
