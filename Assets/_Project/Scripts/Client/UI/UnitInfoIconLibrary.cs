using UnityEngine;
namespace PokeChess.Client.UI
{
    [System.Serializable]
    public sealed class SkillIconEntry {public string skillId;public Sprite icon;}
    public sealed class UnitInfoIconLibrary : ScriptableObject
    {
        public Sprite[] icons;
        public SkillIconEntry[] skillIcons=System.Array.Empty<SkillIconEntry>();
        public Sprite GetSkillIcon(string id)
        {
            if(skillIcons!=null)foreach(var entry in skillIcons)if(entry!=null&&entry.skillId==id)return entry.icon;
            return null;
        }
    }
}
