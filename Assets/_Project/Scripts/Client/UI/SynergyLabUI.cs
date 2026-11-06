using System;
using System.Linq;
using PokeChess.Core.Battle;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;
using PokeChess.Content;
namespace PokeChess.Client.UI
{
    public sealed partial class PlacementSandboxView
    {
        public bool SynergyLabMode { get; private set; }
        public string SynergyLabTrait { get; private set; }
        public int SynergyLabThreshold { get; private set; }
        public void ResetSynergyLab(string trait,int threshold)
        {
            if(!SynergyTraits.All.Contains(trait)||!SynergyTraits.Thresholds(trait).Contains(threshold))throw new ArgumentException("Unknown synergy tier.");
            if(IsOnlineMatch)throw new InvalidOperationException("The lab uses an isolated local match.");
            CancelDrag("시너지 실험 초기화");Simulation?.Cancel();Simulation=null;
            synergyHovered=null;synergyTooltip.gameObject.SetActive(false);synergySettings.gameObject.SetActive(false);
            catalog=GameContent.CreateCatalog();
            var definitions=GameContent.CreateDefinitions();
            var members=definitions.Where(d=>d.TraitIds.Contains(trait)).GroupBy(d=>SynergyTraits.Family(d.Id)).Select(g=>g.OrderBy(d=>d.Cost).ThenBy(d=>SynergyTraits.Dex(d.Id)).First()).OrderBy(d=>d.Cost).ThenBy(d=>SynergyTraits.Dex(d.Id)).Take(threshold).ToList();
            int native=members.Count;
            if(native<threshold)
            {
                if(!SynergyTraits.Origins.Contains(trait))throw new InvalidOperationException("Not enough families for this trait.");
                members.AddRange(definitions.Where(d=>!d.TraitIds.Contains(trait)).GroupBy(d=>SynergyTraits.Family(d.Id)).Select(g=>g.First()).OrderBy(d=>d.Cost).ThenBy(d=>SynergyTraits.Dex(d.Id)).Take(threshold-native));
            }
            if(members.Count!=threshold)throw new InvalidOperationException("Preset cannot reach its requested threshold.");
            Match=MatchStateFactory.CreateWithPool("synergy-lab",new[]{"p1","p2"},catalog,definitions.Select(d=>d.Id),rules:new MatchRules(startingHP:1000,startingGold:100,startingLevel:10,enableWildRounds:false),matchSeed:123);
            // Two compact rows keep the preset editable while leaving space for terrain.
            var cells=new[]{new BoardPosition(1,3),new BoardPosition(2,3),new BoardPosition(3,3),new BoardPosition(4,3),new BoardPosition(5,3),new BoardPosition(1,2),new BoardPosition(2,2),new BoardPosition(3,2),new BoardPosition(4,2),new BoardPosition(5,2)};
            for(int i=0;i<members.Count;i++)
            {
                var d=members[i];string id="lab-ally-"+i;
                SharedPoolSystem.RegisterUnit(Match,catalog.CreateUnit(id,d.Id,"p1",UnitRank.One,0,UnitPlacement.OnBoard(cells[i])),d.Id);
            }
            // Give guardians a visible non-member to protect without changing their synergy count.
            if(trait==SynergyTraits.Guardian)SharedPoolSystem.RegisterUnit(Match,catalog.CreateUnit("lab-beneficiary","caterpie","p1",UnitRank.One,0,UnitPlacement.OnBoard(new BoardPosition(2,2))),"caterpie");
            // Three duplicate defenders deliberately count as only one evolution family.
            for(int i=0;i<3;i++)SharedPoolSystem.RegisterUnit(Match,catalog.CreateUnit("lab-enemy-"+i,"slowpoke","p2",UnitRank.Two,0,UnitPlacement.OnBoard(new BoardPosition(1+i*2,3))),"slowpoke");
            Match.TransitionTo(MatchPhase.Starting);Match.TransitionTo(MatchPhase.Preparation);
            for(int i=native;i<members.Count;i++)
            {
                var player=Match.GetPlayer("p1");string item="item:emblem-"+trait+":lab:"+i;
                player.AddInventoryItem(item);new ItemSystem().Equip(Match,"p1","lab-ally-"+i,item,player.ItemRevision);
            }
            AttachMinimumMatch();automaticRoundFlow=false;shopCollapsed=true;shopPhase=Match.Phase;SynergyLabMode=true;
            SynergyLabTrait=trait;SynergyLabThreshold=threshold;LastResult=null;
            Feedback(SynergyTraits.Name(trait)+" ("+threshold+") · 배치 후 상단 전투 시작 · PvP / 자동 라운드 대기");
            Match.Pool.AssertConservation(Match);Render();
        }
        public bool StartSynergyLabCombat()
        {
            bool started=RoundLoop.StartCombat(Match.RoundNumber);RoundFlow.RefreshState();Render();return started;
        }
        public void StepSynergyLab(int ticks=1)
        {
            if(ticks<1||ticks>1350)throw new ArgumentOutOfRangeException(nameof(ticks));
            for(int i=0;i<ticks&&Match.Phase==MatchPhase.Combat;i++)RoundLoop.Step();
            RoundFlow.RefreshState();Render();
        }
        public bool NextSynergyLabRound()
        {
            if(Match.Phase!=MatchPhase.Result)return false;
            bool result=RoundLoop.NextRound(Match.RoundNumber);RoundFlow.RefreshState();Render();return result;
        }
    }
}
