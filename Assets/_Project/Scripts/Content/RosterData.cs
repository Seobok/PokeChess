using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using PokeChess.Core.Pokemon;
using PokeChess.Core.Battle;

namespace PokeChess.Content
{
    [CreateAssetMenu(menuName="PokeChess/Content/Roster")]
    public sealed class RosterData : ScriptableObject
    {
        public PokemonData[] Pokemon=Array.Empty<PokemonData>();
        public PokemonData[] ShopPokemon=Array.Empty<PokemonData>();
        public SkillData[] Skills=Array.Empty<SkillData>();
        public StatusData[] Statuses=Array.Empty<StatusData>();
        public void Validate()
        {
            Unique(Pokemon,p=>p.Id,"Pokemon"); Unique(Skills,s=>s.Id,"Skills"); Unique(Statuses,s=>s.Id,"Statuses");
            Unique(ShopPokemon,p=>p.Id,"ShopPokemon");
            var pokemon=new HashSet<PokemonData>(Pokemon); var skills=new HashSet<SkillData>(Skills);
            var statuses=new HashSet<StatusData>(Statuses);
            foreach(var p in Pokemon)
            {
                if(!skills.Contains(p.Skill))throw new InvalidOperationException(p.Id+": unregistered skill");
                if(p.Visual==null||string.IsNullOrWhiteSpace(p.Visual.AnimationId)||
                    new[]{p.Visual.Idle,p.Visual.Walk,p.Visual.Attack,p.Visual.Hurt,p.Visual.Skill}.Any(string.IsNullOrWhiteSpace))
                    throw new InvalidOperationException(p.Id+": missing visual mapping");
                foreach(var e in p.Evolutions)
                    if(e==null||!pokemon.Contains(e.Target))throw new InvalidOperationException(p.Id+": unknown evolution");
                foreach(var trait in p.TraitIds)
                    if(!SynergyTraits.All.Contains(trait))throw new InvalidOperationException(p.Id+": unknown synergy "+trait);
            }
            foreach(var p in ShopPokemon)
                if(!pokemon.Contains(p))throw new InvalidOperationException("Unregistered shop Pokemon: "+p.Id);
            foreach(var s in Skills)
            {
                if(s.RankOverrides==null||s.RankOverrides.Any(r=>r==null)||s.RankOverrides.Select(r=>r.Rank).Distinct().Count()!=s.RankOverrides.Length||
                    s.RankOverrides.Any(r=>(int)r.Rank<1||(int)r.Rank>3)||!s.Ranked&&s.RankOverrides.Length>0)
                    throw new InvalidOperationException(s.Id+": invalid rank overrides");
            }
            foreach(var s in Skills)foreach(var e in s.AllEffects)
            {
                if(e==null)throw new InvalidOperationException(s.Id+": null effect");
                if(e.Type==SkillEffectType.RandomStatus&&(e.ChoiceStatuses.Length==0||e.ChoiceStatuses.Any(x=>x==null||!statuses.Contains(x))))throw new InvalidOperationException(s.Id+": invalid random statuses");
                if(e.Type==SkillEffectType.GroundStatus&&(e.Status==null||!statuses.Contains(e.Status)))throw new InvalidOperationException(s.Id+": missing ground status");
                if(e.Type==SkillEffectType.ApplyStatus&&(e.Status==null||!statuses.Contains(e.Status)||
                    e.RankStatuses==null||e.RankStatuses.Length!=3||e.RankStatuses.Any(r=>!statuses.Contains(r))))
                    throw new InvalidOperationException(s.Id+": missing status reference");
            }
            var runtime=Skills.SelectMany(s=>s.ToDefinitions()).ToArray();
            var runtimeById=runtime.ToDictionary(s=>s.Id,StringComparer.Ordinal);
            foreach(var s in runtime)ValidateVariants(s,runtimeById,new HashSet<string>(),0);
            var catalog=CreateCatalog(); CreateSkills(); var statusCatalog=CreateStatuses();
            foreach(var skill in Skills)for(int rank=1;rank<=3;rank++)
                SkillDescription.Format(skill,(UnitRank)rank,100,100,statusCatalog);
            foreach(var p in Pokemon)catalog.EvolutionRoot(p.Id,out _);
        }
        private static void ValidateVariants(SkillDefinition skill,Dictionary<string,SkillDefinition> skills,HashSet<string> path,int depth)
        {
            if(depth>=8||!path.Add(skill.Id))throw new InvalidOperationException("Cyclic/deep skill variants: "+skill.Id);
            foreach(var id in skill.VariantIds)
            {
                if(!skills.TryGetValue(id,out var next))throw new InvalidOperationException(skill.Id+": unknown variant "+id);
                ValidateVariants(next,skills,new HashSet<string>(path),depth+1);
            }
        }
        public SkillData FindSkill(string id)=>Skills.FirstOrDefault(s=>s.Id==id||s.Ranked&&Enumerable.Range(1,3).Any(r=>s.RankId(r)==id));
        private static void Unique<T>(T[] items,Func<T,string> id,string label) where T:UnityEngine.Object
        {
            if(items==null||items.Any(i=>i==null))throw new InvalidOperationException(label+": null entry");
            if(items.Any(i=>string.IsNullOrWhiteSpace(id(i)))||items.Select(id).Distinct(StringComparer.Ordinal).Count()!=items.Length)
                throw new InvalidOperationException(label+": empty or duplicate ID");
        }
        [ContextMenu("Validate Roster")]
        private void ValidateInEditor(){Validate(); Debug.Log("Roster validated: "+Pokemon.Length+" Pokemon",this);}
        public PokemonCatalog CreateCatalog() => new PokemonCatalog(Pokemon.Select(p => p.ToCore()).Concat(new[]
        {
            SynergySystem.NatureGuardianDefinition(2),
            SynergySystem.NatureGuardianDefinition(4)
        }));
        public PokemonDefinition[] CreateDefinitions()=>ShopPokemon.Select(p=>p.ToCore()).ToArray();
        public SkillCatalog CreateSkills()=>new SkillCatalog(Skills.SelectMany(s=>s.ToDefinitions()));
        public StatusCatalog CreateStatuses()=>new StatusCatalog(Statuses.Select(s=>s.ToCore()));
    }
}
