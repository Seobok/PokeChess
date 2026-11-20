using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using PokeChess.Core.Battle;
using PokeChess.Core.Pokemon;
namespace PokeChess.Content
{
    public static class SkillDescription
    {
        private static readonly Regex Tokens=new Regex(@"\{(value|ratio|duration):(\d+)\}|\{(range|castTime|postCastTime)\}");
        public static IEnumerable<SkillEffectDefinition> Flatten(SkillDefinition skill)=>skill.Effects.SelectMany(Flatten);
        private static IEnumerable<SkillEffectDefinition> Flatten(SkillEffectDefinition effect)
        {
            yield return effect;
            if(effect.Movement!=null)foreach(var e in effect.Movement.ImpactEffects.SelectMany(Flatten))yield return e;
            foreach(var e in effect.PathImpactEffects.SelectMany(Flatten))yield return e;
        }
        public static string Format(SkillData data,UnitRank rank,PokemonStats stats,StatusCatalog statuses)
            =>Format(data,rank,stats.SpellPower,stats.Attack,statuses);
        public static string Format(SkillData data,UnitRank rank,float spellPower,float attack,StatusCatalog statuses)
        {
            if(string.IsNullOrEmpty(data.DescriptionTemplate))return data.Description;
            var skill=data.ToCore(rank);var effects=Flatten(skill).ToArray();
            string text=Tokens.Replace(data.DescriptionTemplate,m=>
            {
                if(m.Groups[3].Success)
                {
                    switch(m.Groups[3].Value){case "range":return skill.Range.ToString(CultureInfo.InvariantCulture);
                        case "castTime":return Number(skill.CastTime);default:return Number(skill.PostCastTime);}
                }
                int index=int.Parse(m.Groups[2].Value,CultureInfo.InvariantCulture);
                if(index>=effects.Length)throw new InvalidOperationException(data.Id+": description effect index "+index+" is out of range");
                var e=effects[index];
                switch(m.Groups[1].Value)
                {
                    case "duration":
                        if(e.Type!=SkillEffectType.ApplyStatus)throw new InvalidOperationException(data.Id+": duration token needs a status effect");
                        return Number(statuses.Get(e.ResolveStatusId(rank)).Duration);
                    case "ratio":return Number(e.RankValues[(int)rank-1]*100);
                    default:
                        if(e.ValueSource==SkillValueSource.CasterMaxHP||e.ValueSource==SkillValueSource.TargetMaxHP||e.ValueSource==SkillValueSource.CasterMaxEnergy)
                            throw new InvalidOperationException(data.Id+": HP/energy coefficients require ratio tokens");
                        return Number(e.ResolveValue(rank,spellPower,attack));
                }
            });
            if(Regex.IsMatch(text,@"\{[^{}]*\}"))throw new InvalidOperationException(data.Id+": unknown description token");
            return text;
        }
        private static string Number(float value)=>value.ToString("0.##",CultureInfo.InvariantCulture);
    }
}
