using System.Collections.Generic;

namespace PokeChess.Core.Pokemon
{
    // Initial validation content. Cost, skill, types and traits stay in the original family.
    // HP/attack multipliers are provisional balance values, separate from star rank.
    public static class PrototypeEvolutionRoster
    {
        private static readonly string[][] paths = {
            new[] { "bulbasaur", "ivysaur", "venusaur" },
            new[] { "squirtle", "wartortle", "blastoise" },
            new[] { "charmander", "charmeleon", "charizard" },
            new[] { "slowpoke", "slowbro" },
            new[] { "mankey", "primeape" },
            new[] { "weedle", "kakuna", "beedrill" },
            new[] { "geodude", "graveler", "golem" },
            new[] { "gastly", "haunter", "gengar" },
            new[] { "caterpie", "metapod", "butterfree" },
            new[] { "magnemite", "magneton" },
            new[] { "abra", "kadabra", "alakazam" }
        };
        public static string BaseForm(string id)
        {
            foreach (var path in paths) foreach (var form in path) if (id == form) return path[0];
            return id;
        }
        public static int Stage(string id)
        {
            foreach (var path in paths) for (int i = 0; i < path.Length; i++) if (path[i] == id) return i;
            return 0;
        }
        public static IEnumerable<PokemonDefinition> WithEvolutions(IEnumerable<PokemonDefinition> bases)
        {
            foreach (var definition in bases)
            {
                string[] lineage = null;
                foreach (var path in paths) if (path[0] == definition.Id) { lineage = path; break; }
                if (lineage == null) { yield return definition; continue; }
                for (int i = 0; i < lineage.Length; i++)
                {
                    var s = definition.BaseStats; float multiplier = i == 0 ? 1 : i == 1 ? 1.25f : 1.6f;
                    var stats = i == 0 ? s : new PokemonStats(s.MaxHP * multiplier, s.Attack * multiplier,
                        s.SpellPower, s.AttackSpeed, s.Armor, s.MagicResistance, s.AttackRange, s.MoveSpeed,
                        s.MaxEnergy, s.StartingEnergy, s.AttackHitTime, s.AttackDelivery, s.ProjectileSpeed,
                        s.CritChance, s.CritMultiplier, s.EnergyLockSeconds);
                    string id = lineage[i];
                    string name = char.ToUpperInvariant(id[0]) + id.Substring(1);
                    yield return new PokemonDefinition(id, name, definition.Cost, stats, definition.RoleId,
                        definition.SkillId, definition.TypeIds, definition.TraitIds,
                        i < lineage.Length - 1 ? new[] { new EvolutionOption(lineage[i + 1], 1) } : null);
                }
            }
        }
    }
}
