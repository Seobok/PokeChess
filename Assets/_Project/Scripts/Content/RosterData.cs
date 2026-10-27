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
            foreach(var s in Skills) {
                foreach(var id in s.VariantIds??Array.Empty<string>())if(!Skills.Any(v=>v.Id==id))throw new InvalidOperationException(s.Id+": unknown variant "+id);
                ValidateVariants(s,new HashSet<string>(),0);
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
            var catalog=CreateCatalog(); CreateSkills(); CreateStatuses();
            foreach(var p in Pokemon)catalog.EvolutionRoot(p.Id,out _);
        }
        private void ValidateVariants(SkillData skill,HashSet<string> path,int depth) {
            if(depth>=8||!path.Add(skill.Id))throw new InvalidOperationException("Cyclic/deep skill variants: "+skill.Id);
            foreach(var id in skill.VariantIds??Array.Empty<string>())ValidateVariants(Skills.Single(s=>s.Id==id),new HashSet<string>(path),depth+1);
        }
        private static void Unique<T>(T[] items,Func<T,string> id,string label) where T:UnityEngine.Object
        {
            if(items==null||items.Any(i=>i==null))throw new InvalidOperationException(label+": null entry");
            if(items.Any(i=>string.IsNullOrWhiteSpace(id(i)))||items.Select(id).Distinct(StringComparer.Ordinal).Count()!=items.Length)
                throw new InvalidOperationException(label+": empty or duplicate ID");
        }
        [ContextMenu("Validate Roster")]
        private void ValidateInEditor(){Validate(); Debug.Log("Roster validated: "+Pokemon.Length+" Pokemon",this);}
        public PokemonCatalog CreateCatalog()=>new PokemonCatalog(Pokemon.Select(p=>p.ToCore()));
        public PokemonDefinition[] CreateDefinitions()=>ShopPokemon.Select(p=>p.ToCore()).ToArray();
        public SkillCatalog CreateSkills()=>new SkillCatalog(Skills.Select(s=>s.ToCore()));
        public StatusCatalog CreateStatuses()=>new StatusCatalog(Statuses.Select(s=>s.ToCore()));
    }
}
