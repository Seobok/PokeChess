using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using PokeChess.Core.Pokemon;
using PokeChess.Core.Battle;

namespace PokeChess.Content
{
    // Unity authoring lives here; Core continues to consume ordinary immutable models.
    public static class GameContent
    {
        private static RosterData roster;
        private static Dictionary<string,PokemonData> pokemonById;
        private static Dictionary<string,SkillData> skillById;
        private static PokemonCatalog catalog;
        public static RosterData Roster
        {
            get
            {
                if(roster==null)
                {
                    var loaded=Resources.Load<RosterData>("GameRoster");
                    if(loaded==null)throw new InvalidOperationException("Missing Resources/GameRoster.asset");
                    loaded.Validate();
                    pokemonById=loaded.Pokemon.ToDictionary(p=>p.Id,StringComparer.Ordinal);
                    skillById=loaded.Skills.ToDictionary(s=>s.Id,StringComparer.Ordinal);
                    catalog=loaded.CreateCatalog();roster=loaded;
                }
                return roster;
            }
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset(){roster=null;pokemonById=null;skillById=null;catalog=null;}
        public static PokemonCatalog CreateCatalog(){_=Roster;return catalog;}
        public static PokemonDefinition[] CreateDefinitions()=>Roster.CreateDefinitions();
        public static SkillCatalog CreateSkills()=>Roster.CreateSkills();
        public static StatusCatalog CreateStatuses()=>Roster.CreateStatuses();
        public static PokemonData FindPokemon(string id)
        {_=Roster;return id!=null&&pokemonById.TryGetValue(id,out var p)?p:null;}
        public static string DisplayName(string id)
        {
            var p=FindPokemon(id);
            if(p!=null)return string.IsNullOrEmpty(p.LocalizedName)?p.DisplayName:p.LocalizedName;
            return id!=null&&skillById.TryGetValue(id,out var s)?s.DisplayName:null;
        }
        public static string BaseForm(string id)=>FindPokemon(id)==null?id:CreateCatalog().EvolutionRoot(id,out _);
        public static int Stage(string id)
        {if(FindPokemon(id)==null)return 0; CreateCatalog().EvolutionRoot(id,out int stage); return stage;}
    }
}
