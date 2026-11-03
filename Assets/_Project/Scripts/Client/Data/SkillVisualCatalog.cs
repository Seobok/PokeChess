using System;
using UnityEngine;
namespace PokeChess.Client.Data
{
    public enum SkillVisualTrigger { CastStarted, BeforeEffect, CastEffect, Damage, Heal, Shield, Area, StatusApplied, StatusEnded, StatusActive, MovementArrived, StatusPulse, StatusProc, GoldAwarded, ProjectileFlight, ProjectileHit, PathProjectileFlight, GroundActive, HealthCost, MovementStarted, ShieldActive, AttackSplash }
    public enum SkillVisualPlacement { Caster, Target, AreaCells, CasterToTarget, CasterRing, AreaPath, TargetToCaster, TargetNeighbor, BoardCells }
    public enum SkillVisualPlayback { Once, Loop, HoldUntilEnd }
    public enum SkillVisualSize { Cell, Pixels }

    [Serializable]
    public sealed class SkillVisualEntry
    {
        public string Name;
        public string[] SkillIds=Array.Empty<string>();
        public string[] StatusIds=Array.Empty<string>();
        public SkillVisualTrigger Trigger;
        public SkillVisualPlacement Placement=SkillVisualPlacement.Target;
        public SkillVisualPlayback Playback;
        // -1 matches every stage/repetition. Multiple entries can match the same event.
        public int StepIndex=-1,Iteration=-1,EffectIndex=-1;
        // Ordered line cells: 0 is the nearest cell; -1 means every cell.
        public int AreaCellIndex=-1;
        public float AreaCenterScale=1f;
        public bool ExcludeAreaCenter;
        // Clockwise screen positions, 0=top, 1=upper right ... 5=upper left.
        public int RingIndex;
        public float RingRadius=.85f;
        public bool AlignToDirection;
        public float DelaySeconds; public float AreaDistanceDelaySeconds;
        public float DurationSeconds;
        public Sprite[] Frames=Array.Empty<Sprite>();
        public Sprite[] IntroFrames=Array.Empty<Sprite>(),OutroFrames=Array.Empty<Sprite>();
        public float FramesPerSecond=12,TravelSeconds=.25f;
        public SkillVisualSize SizeMode;
        public float Size=.5f;
        public Vector2 Offset=new Vector2(0,24);
        public float TowardCasterCells;
        [Range(0,1)] public float Opacity=1;
        public bool FollowUnit,CapOverlappingOpacity; // 0=any, 1=matched numeric condition, 2=unmatched.
        public float DropFromAboveCells;
        public int NumericCondition;
        // Higher priority temporarily replaces other visuals in the same group on the same unit.
        public string ExclusiveGroup;
        public int VisualPriority;
        public int HoldFrame=3;
        public bool SpriteAfterimage;
        public bool DashThroughTarget;
        public float LungeSeconds=.16f;
        public bool HideUnitSprite;
        public bool ScaleUnitSprite;
        public float[] SpriteScaleSteps=new[]{1f,.8f,.6f,.4f};
        public float SpriteScaleStepSeconds=.1f;
        public bool Lightning;
        public bool StraightBeam;
        public bool OverheadLightning;
        public float LightningSeconds=.35f,LightningWidth=.035f;
        public bool LightningFromPrimary;
        public Color LightningGlow=new Color(1,.65f,.02f,.65f),LightningCore=new Color(1,1,.72f,1);
        public PokeChess.Core.Battle.SkillMovementKind MovementKind=PokeChess.Core.Battle.SkillMovementKind.Retreat;
        public int TrailCopies=4;
        public float FadeSeconds=.28f;
        public bool Matches(SkillVisualTrigger trigger,string skill,string status,int step,int iteration)
            =>Trigger==trigger && (StepIndex<0||StepIndex==step) && (Iteration<0||Iteration==iteration) &&
                (StatusIds.Length>0?Array.IndexOf(StatusIds,status)>=0:Array.IndexOf(SkillIds,skill)>=0);
    }

    [CreateAssetMenu(menuName="PokeChess/Visuals/Skill Visual Catalog")]
    public sealed class SkillVisualCatalog:ScriptableObject
    {
        public SkillVisualEntry[] Entries=Array.Empty<SkillVisualEntry>();
    }
}
