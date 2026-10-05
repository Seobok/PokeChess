using System;
using System.Linq;
using PokeChess.Core.Battle;
using UnityEngine;
namespace PokeChess.Client.Data
{
    [CreateAssetMenu(menuName="PokeChess/Data/Skill Catalog")]
    public sealed class SkillCatalogAsset : ScriptableObject
    {
        [SerializeField] private SkillDefinitionAsset[] definitions=Array.Empty<SkillDefinitionAsset>();
        public SkillCatalog ToCatalog()
        {
            if(definitions==null||definitions.Any(d=>d==null))throw new ArgumentException("Invalid skill asset entries.");
            return new SkillCatalog(definitions.Select(d=>d.ToDefinition()));
        }
    }
}
