using UnityEngine;
using PokeChess.Core.Match;

namespace PokeChess.Client.UI
{
    // Icon slots follow ItemCatalog.All. The Resources asset includes their sprites in player builds.
    public sealed class ItemIconLibrary : ScriptableObject
    {
        public Sprite[] icons;
        public Sprite evolutionPoint;
        public Sprite[] rewardFlight, rewardArrival;
        public Sprite Get(string definition)
        {
            for(int i=0;i<ItemCatalog.All.Count;i++)
                if(ItemCatalog.All[i].Id==definition)return icons!=null&&i<icons.Length?icons[i]:null;
            return null;
        }
    }
}
