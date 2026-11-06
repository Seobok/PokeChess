using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using PokeChess.Content;
using PokeChess.Client.Data;
using PokeChess.Core.Battle;
using PokeChess.Core.Board;
using PokeChess.Core.Pokemon;
namespace PokeChess.Client.UI
{
    public sealed class EffectLabView:MonoBehaviour
    {
        public RosterData Roster;
        public SkillVisualCatalog Visuals;
        public EffectLabSession Session {get;private set;}
        private Font font;
        private RectTransform canvas,board;
        private UnityEngine.UI.Text summary,detail,log,notice,selection,count,description,statusPageLabel;
        private UnityEngine.UI.InputField search;
        private UnityEngine.UI.Button pause,speed,auto,basic,movement,healthLock;
        private readonly List<UnityEngine.UI.Button> rows=new List<UnityEngine.UI.Button>();
        private readonly Dictionary<BoardPosition,UnityEngine.UI.Image> cells=new Dictionary<BoardPosition,UnityEngine.UI.Image>();
        private sealed class Fighter {public RectTransform Root;public PokemonSpriteView Sprite;public UnityEngine.UI.Text Vitals;}
        private readonly Dictionary<string,Fighter> fighters=new Dictionary<string,Fighter>();
        private readonly List<UnityEngine.UI.Button> activeStatus=new List<UnityEngine.UI.Button>();
        private string inspected="target",chosenPokemon,chosenStatus;
        private BoardPosition selectedCell=new BoardPosition(4,4);
        private int tab,page,statusPage;
        private bool running,allDefinitions;
        private float multiplier=1;
        private double accumulator;
        private readonly List<string> rowIds=new List<string>();
        private BattleState renderedBattle;
        private static Color Bg=>new Color(.045f,.065f,.105f);
        private static Color PanelColor=>new Color(.075f,.11f,.17f);
        private static Color Accent=>new Color(.25f,.82f,.77f);
        private void Start()
        {
            if(Roster==null)Roster=GameContent.Roster;
            font=Font.CreateDynamicFontFromOSFont(new[]{"Malgun Gothic","Arial Unicode MS","Arial"},15);
            Session=new EffectLabSession(Roster);Session.SetPreserveHealth(true);chosenPokemon=Session.Layout[0].Pokemon;
            chosenStatus=Roster.Statuses.FirstOrDefault(s=>s.Behavior==StatusBehavior.Poison)?.Id??Roster.Statuses.First().Id;
            Build();Session.Changed+=Capture;RefreshList();Capture();Render();
        }
        private static Vector2 Point(int col,int row)=>new Vector2(-285+col*86+(row%2)*43,190-row*54);
        private void Update()
        {
            if(Session==null)return;
            if(running&&Session.Battle.Result==BattleResult.InProgress)
            {
                accumulator+=Math.Min(.25,Time.unscaledDeltaTime)*multiplier;
                int ticks=0;while(accumulator>=1d/30&&ticks++<120){Session.Step();accumulator-=1d/30;}
            }
            Render();
        }
        private void Capture()
        {
            if(board!=null)SkillVisualBattleView.Get(board,Visuals).CaptureSimulation(Session.Battle,Session.Simulation,Point,.8f);
        }
        private void Act(Action action)
        {
            try{action();notice.text="";Capture();Render();}
            catch(Exception e){notice.text=e.Message;}
        }
        private RectTransform Rect(string name,Transform parent,float x,float y,float w,float h)
        {
            var node=new GameObject(name,typeof(RectTransform));var r=node.GetComponent<RectTransform>();r.SetParent(parent,false);
            r.anchorMin=r.anchorMax=new Vector2(0,1);r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h);return r;
        }
        private RectTransform Centered(string name,Transform parent,Vector2 position,Vector2 size)
        {
            var r=Rect(name,parent,0,0,size.x,size.y);r.anchorMin=r.anchorMax=r.pivot=new Vector2(.5f,.5f);r.anchoredPosition=position;return r;
        }
        private UnityEngine.UI.Image Fill(RectTransform r,Color color)
        {var image=r.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=color;image.raycastTarget=false;return image;}
        private UnityEngine.UI.Text Text(string name,Transform parent,string value,float x,float y,float w,float h,int size=14,Color? color=null)
        {
            var text=Rect(name,parent,x,y,w,h).gameObject.AddComponent<UnityEngine.UI.Text>();text.font=font;text.text=value;text.fontSize=size;
            text.color=color??new Color(.86f,.9f,.96f);text.raycastTarget=false;text.horizontalOverflow=HorizontalWrapMode.Wrap;text.verticalOverflow=VerticalWrapMode.Truncate;return text;
        }
        private UnityEngine.UI.Button Button(string name,Transform parent,string value,float x,float y,float w,float h,Action click)
        {
            var rect=Rect(name,parent,x,y,w,h);var image=Fill(rect,new Color(.13f,.2f,.29f));image.raycastTarget=true;
            var button=rect.gameObject.AddComponent<UnityEngine.UI.Button>();button.targetGraphic=image;button.onClick.AddListener(()=>Act(click));
            var label=Text("Label",rect,value,8,0,w-16,h,14);label.alignment=TextAnchor.MiddleCenter;return button;
        }
        private void Caption(UnityEngine.UI.Button button,string value)=>button.GetComponentInChildren<UnityEngine.UI.Text>().text=value;
        private void Build()
        {
            var node=new GameObject("EffectLabCanvas",typeof(RectTransform),typeof(Canvas),typeof(UnityEngine.UI.CanvasScaler),typeof(UnityEngine.UI.GraphicRaycaster));
            node.transform.SetParent(transform,false);node.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
            var scaler=node.GetComponent<UnityEngine.UI.CanvasScaler>();scaler.uiScaleMode=UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1536,900);scaler.matchWidthOrHeight=.5f;
            canvas=node.GetComponent<RectTransform>();
            Fill(Rect("Background",canvas,0,0,1536,900),Bg);
            if(FindFirstObjectByType<EventSystem>()==null)new GameObject("EffectLabEvents",typeof(EventSystem),typeof(InputSystemUIInputModule));
            Text("Title",canvas,"EFFECT LAB",24,16,420,42,28,Accent);
            Text("Subtitle",canvas,"스킬과 상태를 직접 적용하고, 실제 수치와 연출을 확인하세요.",26,62,1000,24,15);
            Button("ReloadContent",canvas,"목록 새로고침",1350,28,160,34,()=>{running=false;Session.Reset();RefreshList();});
            BuildCatalog();BuildBoard();BuildInspector();BuildControls();
        }
        private void BuildCatalog()
        {
            var panel=Rect("Catalog",canvas,20,102,312,736);Fill(panel,PanelColor);
            Button("PokemonTab",panel,"포켓몬",12,12,90,32,()=>{tab=0;page=0;RefreshList();});
            Button("SkillTab",panel,"기술",111,12,90,32,()=>{tab=1;page=0;RefreshList();});
            Button("StatusTab",panel,"상태",210,12,90,32,()=>{tab=2;page=0;RefreshList();});
            var input=Rect("Search",panel,12,56,288,36);var image=Fill(input,new Color(.035f,.06f,.10f));image.raycastTarget=true;
            search=input.gameObject.AddComponent<UnityEngine.UI.InputField>();search.targetGraphic=image;
            var label=Text("Value",input,"",10,0,270,36);label.alignment=TextAnchor.MiddleLeft;search.textComponent=label;
            var placeholder=Text("Placeholder",input,"이름 또는 ID 검색",10,0,270,36,14,new Color(.5f,.6f,.7f));placeholder.alignment=TextAnchor.MiddleLeft;search.placeholder=placeholder;
            search.onValueChanged.AddListener(_=>{page=0;RefreshList();});
            Button("DetailDefinitions",panel,"대표 / 전체 정의",12,102,158,28,()=>{allDefinitions=!allDefinitions;page=0;RefreshList();});
            count=Text("Count",panel,"",180,106,120,24,12);
            for(int i=0;i<10;i++){int index=i;var button=Button("Row"+i,panel,"",12,142+i*39,288,35,()=>Choose(index));button.GetComponentInChildren<UnityEngine.UI.Text>().alignment=TextAnchor.MiddleLeft;rows.Add(button);}
            Button("Previous",panel,"이전",12,540,88,30,()=>{page=Math.Max(0,page-1);RefreshList();});
            Button("Next",panel,"다음",212,540,88,30,()=>{page++;RefreshList();});
            selection=Text("Selection",panel,"",12,582,288,68,13,Accent);
            Button("ReplaceCaster",panel,"시전자 교체",12,654,136,32,()=>{running=false;Session.ReplaceCaster(chosenPokemon);inspected=Session.CasterId;RefreshList();});
            Button("AddAlly",panel,"아군 추가",12,696,136,28,()=>{running=false;inspected=Session.Add(chosenPokemon,1,selectedCell);});
            Button("AddEnemy",panel,"적군 추가",164,696,136,28,()=>{running=false;inspected=Session.Add(chosenPokemon,2,selectedCell);});
        }
        private void BuildBoard()
        {
            var panel=Rect("BoardPanel",canvas,350,102,816,590);Fill(panel,PanelColor);
            Text("BoardTitle",panel,"전투 보드  /  클릭: 유닛 관찰 또는 빈 칸 선택",18,14,760,27,16,Accent);
            description=Text("SkillDescription",panel,"",18,48,780,64,13);
            board=Centered("Board",panel,new Vector2(0,-14),new Vector2(800,510));
            foreach(var position in HexBoardBounds.Combat.Cells())
            {
                var p=position;var rect=Centered("Cell_"+p.Column+"_"+p.Row,board,Point(p.Column,p.Row),new Vector2(78,46));
                var image=Fill(rect,new Color(.105f,.16f,.23f));image.raycastTarget=true;
                var button=rect.gameObject.AddComponent<UnityEngine.UI.Button>();button.targetGraphic=image;
                button.onClick.AddListener(()=>{selectedCell=p;var unit=Session.Battle.Units.FirstOrDefault(u=>u.Position.Equals(p)&&u.IsOnBoard);if(unit!=null)inspected=unit.UnitInstanceId;Render();});
                cells[p]=image;
                var text=Text("Coordinate",rect,p.Column+","+p.Row,4,29,68,14,10,new Color(.4f,.5f,.6f));text.alignment=TextAnchor.MiddleCenter;
            }
            Text("BoardHelp",panel,"청색: 아군 팀   ·   적색: 상대 팀   ·   금색 칸: 실제 적용 범위\n대상은 스킬의 실제 선택 규칙을 따릅니다. 배치를 바꿔 사거리·범위를 시험하세요.",18,532,780,48,13);
        }
        private void BuildInspector()
        {
            var panel=Rect("Inspector",canvas,1184,102,332,736);Fill(panel,PanelColor);
            Text("InspectorTitle",panel,"선택 유닛 / 적용 결과",14,12,304,28,17,Accent);
            detail=Text("Details",panel,"",14,50,304,220,14);
            Button("SetCaster",panel,"이 유닛을 시전자로",14,278,304,30,()=>{running=false;Session.SetCaster(inspected);RefreshList();});
            Button("Rank",panel,"성급 변경  1 → 2 → 3",14,318,304,30,()=>{running=false;var p=Session.Layout.Single(u=>u.Id==inspected);Session.SetRank(inspected,(UnitRank)((int)p.Rank%3+1));});
            Button("MoveUnit",panel,"선택한 빈 칸으로 배치",14,358,304,30,()=>{running=false;Session.Move(inspected,selectedCell);});
            Button("RemoveUnit",panel,"유닛 제거",14,398,304,30,()=>{running=false;Session.Remove(inspected);inspected=Session.CasterId;});
            Text("StatusesTitle",panel,"활성 상태  ·  클릭하면 해당 상태 해제",14,446,304,24,13,Accent);
            for(int i=0;i<6;i++){var button=Button("ActiveStatus"+i,panel,"",14,478+i*34,304,30,()=>{});activeStatus.Add(button);}
            Button("PreviousStatuses",panel,"이전",14,692,66,28,()=>statusPage=Math.Max(0,statusPage-1));
            statusPageLabel=Text("StatusPage",panel,"",90,698,140,24,12);
            Button("NextStatuses",panel,"다음",252,692,66,28,()=>statusPage++);
        }
        private void BuildControls()
        {
            summary=Text("Summary",canvas,"",364,706,800,44,14,Accent);
            pause=Button("PlayPause",canvas,"재생",350,760,100,36,()=>{running=!running;accumulator=0;});
            Button("Step",canvas,"1틱 진행",460,760,100,36,()=>{running=false;Session.Step();});
            Button("Cast",canvas,"스킬 사용",570,760,110,36,()=>{Session.Cast();running=true;});
            Button("Reset",canvas,"다시 시작",690,760,110,36,()=>{running=false;accumulator=0;Session.Reset();ClearFighters();});
            speed=Button("Speed",canvas,"속도 ×1",810,760,100,36,()=>{multiplier=multiplier==.25f?.5f:multiplier==.5f?1:multiplier==1?2:multiplier==2?4:.25f;});
            healthLock=Button("HealthLock",canvas,"HP 고정 ON",1050,760,116,36,()=>Session.SetPreserveHealth(!Session.PreserveHealth));
            auto=Button("AutoSkill",canvas,"자동 기술 OFF",920,760,120,36,()=>Session.AutoCast=!Session.AutoCast);
            Button("ApplyStatus",canvas,"선택 상태 부여",350,808,160,32,()=>Session.ApplyStatus(chosenStatus,inspected));
            Button("ClearStatus",canvas,"상태 모두 해제",520,808,150,32,()=>Session.ClearStatuses(inspected));
            Button("Hit",canvas,"피격 100",680,808,110,32,()=>Session.Hit(inspected,100));
            Button("Heal",canvas,"체력 회복",800,808,110,32,()=>Session.Heal(inspected));
            basic=Button("Basic",canvas,"기본 공격 OFF",920,808,120,32,()=>Session.BasicAttacks=!Session.BasicAttacks);
            movement=Button("Movement",canvas,"이동 OFF",1050,808,116,32,()=>Session.Movement=!Session.Movement);
            notice=Text("Notice",canvas,"",350,850,1160,30,14,new Color(1,.65f,.38f));
            var logPanel=Rect("EventPanel",canvas,20,850,312,32);Fill(logPanel,PanelColor);
            Button("EventLog",logPanel,"이벤트 로그 열기 / 닫기",0,0,312,32,()=>log.transform.parent.gameObject.SetActive(!log.transform.parent.gameObject.activeSelf));
            var overlay=Rect("LogOverlay",canvas,360,170,796,510);Fill(overlay,new Color(.025f,.04f,.07f,.97f));
            Text("LogTitle",overlay,"적용 이벤트  /  최근 22개",18,12,620,30,18,Accent);
            Button("CloseLog",overlay,"닫기",670,12,110,30,()=>overlay.gameObject.SetActive(false));
            log=Text("Log",overlay,"",18,54,760,442,14);overlay.gameObject.SetActive(false);
        }
        private string PokemonName(string id)
        {var p=Roster.Pokemon.FirstOrDefault(v=>v.Id==id);return p==null?id:string.IsNullOrEmpty(p.LocalizedName)?p.DisplayName:p.LocalizedName;}
        private string StatusName(StatusData status)
        {
            string kind=StatusKind(status.Behavior,status.CrowdControl);
            if(kind=="버프")kind=string.Join("+",status.Modifiers.Select(m=>m.Stat.ToString()));
            return kind+" · "+status.Duration.ToString("0.##")+"s / "+status.Id;
        }
        private static string StatusKind(StatusBehavior behavior,CrowdControlKind control)
        {
            switch(behavior)
            {
                case StatusBehavior.Sleep:return "수면";
                case StatusBehavior.RestSleep:return "잠자기";
                case StatusBehavior.Freeze:return "빙결";
                case StatusBehavior.Paralysis:return "마비";
                case StatusBehavior.Confusion:return "혼란";
                case StatusBehavior.Poison:return "독";
                case StatusBehavior.Burn:return "화상";
                case StatusBehavior.Blind:return "실명";
                case StatusBehavior.Bleed:return "출혈";
                case StatusBehavior.Focus:return "집중";
                case StatusBehavior.Untargetable:return "대상 지정 불가";
                case StatusBehavior.None:break;
                default:return behavior.ToString();
            }
            switch(control)
            {
                case CrowdControlKind.Stun:return "기절";
                case CrowdControlKind.Root:return "속박";
                case CrowdControlKind.Silence:return "침묵";
                case CrowdControlKind.Disarm:return "무장 해제";
                case CrowdControlKind.Taunt:return "도발";
                case CrowdControlKind.Slow:return "둔화";
                case CrowdControlKind.None:return "버프";
                default:return control.ToString();
            }
        }
        private void RefreshList()
        {
            if(search==null)return;var options=new List<KeyValuePair<string,string>>();
            if(tab==0)options.AddRange(Roster.Pokemon.Select(p=>new KeyValuePair<string,string>(p.Id,PokemonName(p.Id)+"  · "+p.Id)));
            if(tab==1)
            {
                var skills=allDefinitions?Roster.Skills:Roster.Pokemon.Select(p=>p.Skill).Distinct();
                options.AddRange(skills.Select(s=>new KeyValuePair<string,string>(s.Id,s.DisplayName+"  · "+s.Id)));
            }
            if(tab==2)
            {
                IEnumerable<StatusData> statuses=Roster.Statuses;
                if(!allDefinitions)statuses=statuses.GroupBy(s=>s.Behavior+"/"+s.CrowdControl+"/"+string.Join(",",s.Modifiers.Select(m=>m.Stat+":"+m.Type))).Select(g=>g.First());
                options.AddRange(statuses.Select(s=>new KeyValuePair<string,string>(s.Id,StatusName(s))));
            }
            string query=search.text.Trim();var filtered=options.Where(p=>query.Length==0||(p.Key+" "+p.Value).IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0).ToArray();
            int pages=Math.Max(1,(filtered.Length+9)/10);page=Mathf.Clamp(page,0,pages-1);count.text=filtered.Length+"개 · "+(page+1)+"/"+pages;
            rowIds.Clear();var visible=filtered.Skip(page*10).Take(10).ToArray();
            for(int i=0;i<rows.Count;i++){bool show=i<visible.Length;rows[i].gameObject.SetActive(show);if(show){rowIds.Add(visible[i].Key);Caption(rows[i],visible[i].Value);}}
            UpdateSelection();
        }
        private void Choose(int index)
        {
            if(index>=rowIds.Count)return;string id=rowIds[index];
            if(tab==0)chosenPokemon=id;
            if(tab==1){Session.SkillId=id;Session.Note("시험 기술 선택: "+Roster.Skills.Single(s=>s.Id==id).DisplayName);}
            if(tab==2)chosenStatus=id;UpdateSelection();
        }
        private void UpdateSelection()
        {
            selection.text="배치 포켓몬: "+PokemonName(chosenPokemon)+"\n기술: "+Roster.Skills.FirstOrDefault(s=>s.Id==Session.SkillId)?.DisplayName+"\n상태: "+(chosenStatus??"없음");
        }
        private void Render()
        {
            if(Session==null||board==null)return;var battle=Session.Battle;
            if(renderedBattle!=battle){ClearFighters();renderedBattle=battle;}
            if(Session.Unit(inspected)==null)inspected=Session.CasterId;
            foreach(var pair in cells)
            {
                bool area=Session.LastArea.Contains(pair.Key)&&battle.CurrentTick-Session.LastAreaTick<30;
                pair.Value.color=area?new Color(.44f,.31f,.11f):pair.Key.Equals(selectedCell)?new Color(.14f,.35f,.37f):new Color(.105f,.16f,.23f);
            }
            foreach(var key in fighters.Keys.Where(k=>Session.Unit(k)==null).ToArray()){Destroy(fighters[key].Root.gameObject);fighters.Remove(key);}
            foreach(var unit in battle.Units)
            {
                if(!fighters.TryGetValue(unit.UnitInstanceId,out var fighter))
                {
                    var root=Centered("Unit_"+unit.UnitInstanceId,board,Vector2.zero,new Vector2(80,64));
                    var sprite=Centered("Sprite",root,Vector2.zero,Vector2.one).gameObject.AddComponent<PokemonSpriteView>();sprite.Initialize(unit.DefinitionId);
                    var label=Text("Vitals",root,"",-12,54,104,34,10);label.alignment=TextAnchor.MiddleCenter;
                    fighter=new Fighter{Root=root,Sprite=sprite,Vitals=label};fighters.Add(unit.UnitInstanceId,fighter);
                }
                var action=Session.Simulation.GetRuntime(unit.UnitInstanceId);var location=Point(unit.Position.Column,unit.Position.Row);Vector2 facing=Vector2.zero;
                if(action.MoveDestination.HasValue){var to=Point(action.MoveDestination.Value.Column,action.MoveDestination.Value.Row);facing=to-location;location=Vector2.Lerp(location,to,(float)(battle.CurrentTick-action.StartTick)/Math.Max(1,action.EndTick-action.StartTick));}
                else{var target=Session.Unit(action.SkillCast?.TargetId??unit.CurrentTargetId);if(target!=null)facing=Point(target.Position.Column,target.Position.Row)-location;}
                location=ThrowPresentation.Local(battle,unit.UnitInstanceId,location,Point);
                fighter.Root.anchoredPosition=location;fighter.Sprite.ShowCombat(battle,unit,action,facing,.8f);
                fighter.Vitals.text=(unit.UnitInstanceId==Session.CasterId?"▶ ":"")+PokemonName(unit.DefinitionId)+"\n"+Math.Ceiling(unit.CurrentHP)+" / "+Math.Floor(unit.CurrentEnergy)+"E";
                fighter.Vitals.color=unit.TeamId==1?new Color(.4f,.86f,1):new Color(1,.6f,.55f);
            }
            var visuals=SkillVisualBattleView.Get(board,Visuals);
            visuals.RenderSimulation(battle,Session.Simulation,battle.CurrentTick,Point,.8f);
            visuals.SortCombatDepth(fighters.Values.Select(f=>f.Root));
            var selected=Session.Unit(inspected);var stats=selected.EffectiveStats;
            detail.text=PokemonName(selected.DefinitionId)+" · "+(int)selected.Rank+"성 / "+(selected.TeamId==1?"아군":"적군")+"\n"+
                "HP  "+selected.CurrentHP.ToString("0.#")+" / "+selected.Stats.MaxHP+"\n보호막  "+selected.CurrentShield.ToString("0.#")+"   마나 "+selected.CurrentEnergy.ToString("0.#")+"\n"+
                "공격 "+stats.Attack.ToString("0.#")+"   주문력 "+stats.SpellPower.ToString("0.#")+"\n"+
                "방어 "+stats.Armor.ToString("0.#")+"   특방 "+stats.MagicResistance.ToString("0.#")+"\n"+
                "공속 "+stats.AttackSpeed.ToString("0.##")+"   사거리 "+battle.StatusEffects.AttackRange(selected)+"\n행동 "+selected.ActionState+" / 대상 "+(selected.CurrentTargetId??"없음")+"\n선택 칸 "+selectedCell.Column+", "+selectedCell.Row;
            var statuses=battle.StatusEffects.Active.Where(s=>s.TargetId==inspected).ToArray();
            int statusPages=Math.Max(1,(statuses.Length+activeStatus.Count-1)/activeStatus.Count);
            statusPage=Mathf.Clamp(statusPage,0,statusPages-1);statusPageLabel.text=statuses.Length+"개 · "+(statusPage+1)+"/"+statusPages;
            for(int i=0;i<activeStatus.Count;i++)
            {
                int index=statusPage*activeStatus.Count+i;
                var button=activeStatus[i];button.gameObject.SetActive(index<statuses.Length);if(index>=statuses.Length)continue;var status=statuses[index];
                Caption(button,StatusKind(status.Definition.Behavior,status.Definition.CrowdControl)+" ×"+status.StackCount+" · "+((status.ExpireTick-battle.CurrentTick)/(double)battle.TickRate).ToString("0.00")+"s");
                button.onClick.RemoveAllListeners();long id=status.InstanceId;button.onClick.AddListener(()=>Act(()=>Session.RemoveStatus(id)));
            }
            var skill=Roster.Skills.FirstOrDefault(s=>s.Id==Session.SkillId);
            var statusData=Roster.Statuses.FirstOrDefault(s=>s.Id==chosenStatus);
            description.text=tab==2&&statusData!=null?StatusName(statusData)+"\n"+
                string.Join(" / ",statusData.Modifiers.Select(m=>m.Stat+" "+m.Type+" "+m.Value.ToString("0.##"))):
                (skill?.Description??"")+"\n충전 "+(skill?.CastTime??0).ToString("0.##")+"s / 후딜 "+(skill?.PostCastTime??0).ToString("0.##")+"s";
            bool hasVisual=Visuals!=null&&Visuals.Entries.Any(e=>e.SkillIds.Any(id=>id==Session.SkillId||id.StartsWith(Session.SkillId+"-r",StringComparison.Ordinal)));
            summary.text=(battle.CurrentTick/30d).ToString("0.00")+"s · Tick "+battle.CurrentTick+" · "+battle.Result+(Session.Requested?" · 시전 대기":"")+"\n"+(skill?.DisplayName??Session.SkillId)+"  /  "+(hasVisual?"전용 연출 등록됨":"전용 연출 미등록 · 효과 수치/상태는 적용됨");
            Caption(pause,running?"일시정지":"재생");Caption(speed,"속도 ×"+multiplier);Caption(auto,"자동 기술 "+(Session.AutoCast?"ON":"OFF"));Caption(basic,"기본 공격 "+(Session.BasicAttacks?"ON":"OFF"));Caption(movement,"이동 "+(Session.Movement?"ON":"OFF"));Caption(healthLock,"HP 고정 "+(Session.PreserveHealth?"ON":"OFF"));
            log.text=string.Join("\n",Session.Log.Reverse().Take(22));UpdateSelection();
        }
        private void ClearFighters(){foreach(var fighter in fighters.Values)Destroy(fighter.Root.gameObject);fighters.Clear();}
        private void OnDestroy(){if(Session!=null)Session.Changed-=Capture;if(font!=null)Destroy(font);}
    }
}
