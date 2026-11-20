using System;
using UnityEngine;
using PokeChess.Core.Battle;
namespace PokeChess.Client.Data
{
    public enum StatusVisualPosition { Feet, Overlay, Head }
    [Serializable] public sealed class StatusVisual
    {
        public StatusBehavior Behavior; public CrowdControlKind CrowdControl;
        public StatusVisualPosition Position;
        public Sprite[] Frames=Array.Empty<Sprite>();
        public float FramesPerSecond=12,Opacity=1;
    }
    [Serializable] public sealed class StatusOverheadIcon
    {
        public string Key;
        public Sprite Sprite;
    }

    public sealed class StatusVisualLibrary:ScriptableObject
    {
        public StatusVisual[] Effects=Array.Empty<StatusVisual>();
        public StatusOverheadIcon[] OverheadIcons = Array.Empty<StatusOverheadIcon>();
    }
}
