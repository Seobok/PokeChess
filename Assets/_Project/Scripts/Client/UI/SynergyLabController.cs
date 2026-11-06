using System;
using UnityEngine;
using UnityEngine.UI;
using PokeChess.Core.Battle;
using PokeChess.Core.Match;
namespace PokeChess.Client.UI
{
    // Scene-only controls over the same host simulation used by the game.
    [RequireComponent(typeof(PlacementSandboxView))]
    public sealed class SynergyLabController:MonoBehaviour
    {
        public string InitialSynergy=SynergyTraits.Guardian;
        public int InitialThreshold=4;
        public PlacementSandboxView View { get; private set; }
        public bool Running { get; private set; }
        public int Speed { get; private set; }=1;
        private int traitIndex,tierIndex;
        private double accumulator;
        private UnityEngine.UI.Text title,playLabel,speedLabel;
        private Font font;
        private void Start()
        {
            View=GetComponent<PlacementSandboxView>();font=Font.CreateDynamicFontFromOSFont(new[]{"Malgun Gothic","Arial"},14);
            BuildToolbar();Select(InitialSynergy,InitialThreshold);
        }
        private void Update()
        {
            if(View==null)return;
            if(Running&&View.Match.Phase==MatchPhase.Combat){accumulator+=Time.unscaledDeltaTime*30*Speed;int count=Math.Min(90,(int)accumulator);if(count>0){accumulator-=count;View.StepSynergyLab(count);}}
            if(View.Match.Phase!=MatchPhase.Combat)Running=false;
            RefreshToolbar();
        }
        public void RefreshToolbar()
        {
            if(View==null||title==null)return;
            title.text=SynergyTraits.Name(View.SynergyLabTrait)+" ("+View.SynergyLabThreshold+") / R"+View.Match.RoundNumber+" / "+View.Match.Phase;
            playLabel.text=View.Match.Phase==MatchPhase.Preparation?"전투 시작":View.Match.Phase==MatchPhase.Result?"다음 라운드":Running?"일시정지":"계속";
            speedLabel.text="속도 "+Speed+"x";
        }
        public void Select(string trait,int threshold)
        {
            traitIndex=System.Linq.Enumerable.ToList(SynergyTraits.All).IndexOf(trait);
            tierIndex=Array.IndexOf(SynergyTraits.Thresholds(trait),threshold);
            if(traitIndex<0||tierIndex<0)throw new ArgumentException("Unknown preset.");
            Running=false;accumulator=0;View.ResetSynergyLab(trait,threshold);RefreshToolbar();
        }
        public void ChangeTrait(int direction)
        {
            int index=(traitIndex+direction+SynergyTraits.All.Count)%SynergyTraits.All.Count;
            string trait=SynergyTraits.All[index];Select(trait,SynergyTraits.Thresholds(trait)[0]);
        }
        public void ChangeTier()
        {
            string trait=SynergyTraits.All[traitIndex];var thresholds=SynergyTraits.Thresholds(trait);Select(trait,thresholds[(tierIndex+1)%thresholds.Length]);
        }
        public void PlayPause()
        {
            if(View.Match.Phase==MatchPhase.Result){View.NextSynergyLabRound();return;}
            if(View.Match.Phase==MatchPhase.Preparation){Running=View.StartSynergyLabCombat();return;}
            Running=!Running;
        }
        public void Step()
        {
            Running=false;if(View.Match.Phase==MatchPhase.Preparation)View.StartSynergyLabCombat();View.StepSynergyLab();RefreshToolbar();
        }
        private static RectTransform Rect(string name,Transform parent,Vector2 size,Vector2 position)
        {
            var go=new GameObject(name,typeof(RectTransform));var rt=go.GetComponent<RectTransform>();rt.SetParent(parent,false);rt.anchorMin=rt.anchorMax=rt.pivot=new Vector2(.5f,.5f);rt.sizeDelta=size;rt.anchoredPosition=position;return rt;
        }
        private UnityEngine.UI.Text Text(string name,Transform parent,string value,int size,Vector2 dimensions,Vector2 position)
        {
            var t=Rect(name,parent,dimensions,position).gameObject.AddComponent<UnityEngine.UI.Text>();t.font=font;t.fontSize=size;t.color=Color.white;t.alignment=TextAnchor.MiddleCenter;t.text=value;t.raycastTarget=false;return t;
        }
        private UnityEngine.UI.Text Button(Transform parent,string name,string label,float x,float width,Action action)
        {
            var rt=Rect(name,parent,new Vector2(width,29),new Vector2(x,-9));var image=rt.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=new Color(.1f,.32f,.37f);var button=rt.gameObject.AddComponent<UnityEngine.UI.Button>();button.targetGraphic=image;button.onClick.AddListener(()=>{try{action();RefreshToolbar();}catch(Exception e){Debug.LogException(e);}});return Text("Label",rt,label,13,new Vector2(width-6,27),Vector2.zero);
        }
        private void BuildToolbar()
        {
            var rt=Rect("SynergyLabCanvas",transform,new Vector2(1280,720),Vector2.zero);var canvas=rt.gameObject.AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=100;
            var scaler=rt.gameObject.AddComponent<UnityEngine.UI.CanvasScaler>();scaler.uiScaleMode=UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1280,720);scaler.screenMatchMode=UnityEngine.UI.CanvasScaler.ScreenMatchMode.Expand;rt.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            var bar=Rect("Toolbar",rt,new Vector2(1280,68),new Vector2(0,326));bar.gameObject.AddComponent<UnityEngine.UI.Image>().color=new Color(.035f,.075f,.12f);
            Text("Heading",bar,"SYNERGY LAB · 시너지 실험실",13,new Vector2(295,22),new Vector2(-472,19));
            title=Text("Preset",bar,"",15,new Vector2(600,22),new Vector2(105,19));
            Button(bar,"Previous","< 이전 시너지",-548,145,()=>ChangeTrait(-1));Button(bar,"Next","다음 시너지 >",-395,145,()=>ChangeTrait(1));Button(bar,"Tier","단계 변경",-260,115,ChangeTier);
            Button(bar,"Reset","배치 초기화",-135,125,()=>Select(View.SynergyLabTrait,View.SynergyLabThreshold));playLabel=Button(bar,"Play","전투 시작",0,125,PlayPause);Button(bar,"Step","1틱 진행",130,115,Step);
            speedLabel=Button(bar,"Speed","속도 1x",250,115,()=>Speed=Speed==1?4:Speed==4?8:1);
            Text("Hint",bar,"드래그로 배치 / 좌측 시너지에 마우스 / 하단에서 도구 장착",11,new Vector2(310,40),new Vector2(460,-8));
        }
    }
}
