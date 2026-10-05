using System;
using System.Linq;
using PokeChess.Core.Battle;
using UnityEngine;
namespace PokeChess.Client.Data
{
    [CreateAssetMenu(menuName="PokeChess/Data/Status Catalog")]
    public sealed class StatusCatalogAsset : ScriptableObject
    {
        [SerializeField] private StatusEffectDefinitionAsset[] definitions=Array.Empty<StatusEffectDefinitionAsset>();
        public StatusCatalog ToCatalog()
        {
            if(definitions==null||definitions.Any(d=>d==null))throw new ArgumentException("Invalid status asset entries.");
            return new StatusCatalog(definitions.Select(d=>d.ToDefinition()));
        }
    }
}
