using UnityEngine;
using PokeChess.Core.Match;

namespace PokeChess.Client.UI
{
    // References the original Sprites/Items assets; Resources includes them in Players.
    public sealed class ItemIconLibrary : ScriptableObject
    {
        public Sprite[] icons;
        public Sprite Get(string definition)
        {
            for(int i=0;i<ItemCatalog.All.Count;i++)
                if(ItemCatalog.All[i].Id==definition)return icons!=null&&i<icons.Length?icons[i]:null;
            return null;
        }
    }
}
