using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace PokeChess.Core.Match
{
    // Pure formatting; the caller chooses the save location and performs IO.
    public static class SimulationReport
    {
        private static string N(double n)=>n.ToString("R",CultureInfo.InvariantCulture);
        private static string Q(string s)
        {
            if(s==null)return "null";
            var b=new StringBuilder("\"");
            foreach(char c in s)
            {
                if(c=='"'||c=='\\')b.Append('\\').Append(c);
                else if(c<32)b.Append("\\u").Append(((int)c).ToString("x4",CultureInfo.InvariantCulture));
                else b.Append(c);
            }
            return b.Append('"').ToString();
        }
        private static string Object(params string[] fields)=>"{"+string.Join(",",fields)+"}";
        private static string F(string key,string value)=>Q(key)+":"+value;
        private static string S(string key,string value)=>F(key,Q(value));
        private static string Num(string key,double value)=>F(key,N(value));
        private static string B(string key,bool value)=>F(key,value ? "true" : "false");
        public static string ToJson(LocalMatchSimulation run)
        {
            if(run==null)throw new ArgumentNullException(nameof(run));
            var m=run.Match;var settings=run.Settings;var flow=run.Flow;
            var final=m.FinalResult;
            var standings=final==null ? "[]" : "["+string.Join(",",final.Standings.Select(p=>Object(S("playerId",p.PlayerId),Num("placement",p.Placement),Num("hp",p.HP),
                B("isWinner",p.IsWinner),B("wasAlive",p.WasAlive),S("eliminationReason",p.EliminationReason?.ToString()),F("eliminationRound",p.EliminationRound?.ToString(CultureInfo.InvariantCulture)??"null"))))+"]";
            var rounds="["+string.Join(",",run.Rounds.Select(r=>Object(Num("round",r.Round),Num("aliveBefore",r.AliveBefore),Num("aliveAfter",r.AliveAfter),
                Num("preparationSeconds",r.PreparationSeconds),Num("combatSeconds",r.CombatSeconds),Num("resultSeconds",r.ResultSeconds),B("settled",r.Settled),
                F("players","["+string.Join(",",r.Players.Select(p=>Object(S("playerId",p.PlayerId),Num("hpBefore",p.HPBefore),Num("hpAfter",p.HPAfter),Num("damage",p.Damage),
                    F("placement",p.Placement?.ToString(CultureInfo.InvariantCulture)??"null"),S("eliminationReason",p.EliminationReason),
                    F("battle",p.Battle==null ? "null" : Object(S("opponentId",p.Battle.OpponentId),B("isShadow",p.Battle.IsShadow),S("outcome",p.Battle.Outcome.ToString()),
                        S("reason",p.Battle.Reason.ToString()),Num("endTick",p.Battle.EndTick),Num("opponentSurvivors",p.Battle.OpponentSurvivors))))))+"]"))))+"]";
            return Object(Num("schemaVersion",1),S("matchId",m.MatchId),S("seed",settings.Seed.ToString(CultureInfo.InvariantCulture)),
                S("botPolicy",LocalMatchSimulation.PolicyVersion),S("content",run.ContentId),Num("playerCount",settings.PlayerCount),
                Num("startingHP",m.Rules.StartingHP),Num("startingGold",m.Rules.StartingGold),Num("maxRounds",settings.MaxRounds),Num("maxCombatTicks",settings.MaxCombatTicks),
                Num("preparationSeconds",settings.FlowRules.PreparationSeconds),Num("resultSeconds",settings.FlowRules.ResultSeconds),Num("tickRate",run.Coordinator.TickRate),
                S("status",run.Status.ToString()),S("error",run.Error),Num("endRound",m.RoundNumber),S("winnerPlayerId",final?.WinnerPlayerId),S("endReason",final?.Reason.ToString()),
                Num("gameSeconds",flow.MatchElapsedSeconds),Num("preparationTotalSeconds",flow.PreparationElapsedSeconds),Num("combatTotalSeconds",flow.CombatElapsedSeconds),
                Num("resultTotalSeconds",flow.ResultElapsedSeconds),Num("executionSeconds",run.ExecutionSeconds),F("standings",standings),F("rounds",rounds));
        }
        private static string Csv(string value)=>"\""+(value??"").Replace("\"","\"\"")+"\"";
        public const string CsvHeader="seed,status,endRound,winner,endReason,gameSeconds,executionSeconds,roundsRecorded,shadowRounds,error";
        public static string ToCsvRow(LocalMatchSimulation run)=>string.Join(",",new[]{run.Settings.Seed.ToString(CultureInfo.InvariantCulture),run.Status.ToString(),run.Match.RoundNumber.ToString(CultureInfo.InvariantCulture),
            Csv(run.Match.FinalResult?.WinnerPlayerId),Csv(run.Match.FinalResult?.Reason.ToString()),N(run.Flow.MatchElapsedSeconds),N(run.ExecutionSeconds),run.Rounds.Count.ToString(CultureInfo.InvariantCulture),
            run.Rounds.Count(r=>r.Players.Any(p=>p.Battle?.IsShadow==true)).ToString(CultureInfo.InvariantCulture),Csv(run.Error)});
        public static string ToCsv(IEnumerable<LocalMatchSimulation> runs)=>CsvHeader+"\n"+string.Join("\n",runs.Select(ToCsvRow))+"\n";
    }

    public sealed class SimulationBatchSummary
    {
        public int Total { get; }
        public int Completed { get; }
        public double IncompleteRate=>Total==0 ? 0 : (Total-Completed)/(double)Total;
        public double? MeanSeconds { get; }
        public double? MedianSeconds { get; }
        public double? MinSeconds { get; }
        public double? MaxSeconds { get; }
        public SimulationBatchSummary(IEnumerable<LocalMatchSimulation> runs)
        {
            var all=(runs??throw new ArgumentNullException(nameof(runs))).ToArray();Total=all.Length;
            var seconds=all.Where(r=>r.Status==SimulationStatus.Completed).Select(r=>r.Flow.MatchElapsedSeconds).OrderBy(s=>s).ToArray();Completed=seconds.Length;
            if(Completed==0)return;
            MeanSeconds=seconds.Average();MedianSeconds=Completed%2==1 ? seconds[Completed/2] : (seconds[Completed/2-1]+seconds[Completed/2])/2;
            MinSeconds=seconds[0];MaxSeconds=seconds[Completed-1];
        }
    }
}
