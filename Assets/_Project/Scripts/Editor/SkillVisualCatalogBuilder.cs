using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using PokeChess.Client.Data;
namespace PokeChess.Editor
{
    public static class SkillVisualCatalogBuilder
    {
        public const string CatalogPath="Assets/_Project/Data/SkillVisualCatalog.asset";
        private const string Art="Assets/_Project/Sprites/Effects/Skill/";
        private static Sprite[] FramesAt(string path)=>AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>()
            .OrderByDescending(s=>s.rect.y).ThenBy(s=>s.rect.x).ToArray();
        private static Sprite[] Frames(string path)=>FramesAt(Art+path);
        public const string HealthCostPath="Assets/_Project/Sprites/Effects/Common/health-cost/health_cost_8f_4x2_64.png";
        private static string[] Skills(params string[] ids)=>ids.SelectMany(id=>new[]{"skill-"+id,"skill-"+id+"-r1","skill-"+id+"-r2","skill-"+id+"-r3"}).ToArray();
        // One-time migration only. Existing authoring changes are never overwritten.
        [MenuItem("PokeChess/Visuals/Create Initial Skill Visual Catalog")]
        public static SkillVisualCatalog CreateInitialCatalog()
        {
            var existing=AssetDatabase.LoadAssetAtPath<SkillVisualCatalog>(CatalogPath);if(existing!=null)return existing;
            var entries=new List<SkillVisualEntry>();
            entries.Add(new SkillVisualEntry{Name="StringShotProjectile",SkillIds=Skills("caterpie").Concat(new[]{"string-shot"}).ToArray(),
                Trigger=SkillVisualTrigger.BeforeEffect,Placement=SkillVisualPlacement.CasterToTarget,
                Frames=Frames("string-shot/string_projectile.png"),SizeMode=SkillVisualSize.Pixels,Size=32});
            entries.Add(new SkillVisualEntry{Name="StringShotBinding",Trigger=SkillVisualTrigger.StatusActive,
                StatusIds=Enumerable.Range(1,3).SelectMany(r=>new[]{"status-caterpie-r"+r+"-s0","string-shot-r"+r}).ToArray(),
                Placement=SkillVisualPlacement.Target,Playback=SkillVisualPlayback.HoldUntilEnd,FollowUnit=true,
                Frames=Frames("string-shot/web_binding.png"),SizeMode=SkillVisualSize.Pixels,Size=32,Offset=new Vector2(0,20)});
            entries.Add(new SkillVisualEntry{Name="SleepPowder",SkillIds=Skills("butterfree"),Trigger=SkillVisualTrigger.Area,
                Placement=SkillVisualPlacement.AreaCells,Frames=Frames("sleep-powder/sleep_powder.png"),
                Offset=new Vector2(0,14),Opacity=.4f,CapOverlappingOpacity=true});
            entries.Add(new SkillVisualEntry{Name="PoisonStingProjectile",SkillIds=Skills("weedle","tentacool","nidoran-f"),
                Trigger=SkillVisualTrigger.BeforeEffect,Placement=SkillVisualPlacement.CasterToTarget,Frames=Frames("poison-sting/poison_sting_projectile.png")});
            entries.Add(new SkillVisualEntry{Name="PoisonStingImpact",SkillIds=Skills("weedle","tentacool","nidoran-f"),
                Trigger=SkillVisualTrigger.Damage,Frames=Frames("poison-sting/poison_sting_impact.png")});
            entries.Add(new SkillVisualEntry{Name="TwineedleLeft",SkillIds=Skills("beedrill"),Trigger=SkillVisualTrigger.Damage,
                StepIndex=0,Frames=Frames("twineedle/poison_hit_left.png"),FramesPerSecond=24});
            entries.Add(new SkillVisualEntry{Name="TwineedleRight",SkillIds=Skills("beedrill"),Trigger=SkillVisualTrigger.Damage,
                StepIndex=1,Frames=Frames("twineedle/poison_hit_right.png"),FramesPerSecond=24});
            entries.Add(new SkillVisualEntry{Name="Gust",SkillIds=Skills("pidgey"),Trigger=SkillVisualTrigger.Area,
                Placement=SkillVisualPlacement.AreaCells,Frames=Frames("gust/gust.png"),Offset=new Vector2(0,14)});
            for(int n=1;n<=3;n++)entries.Add(new SkillVisualEntry{Name="WingAttack"+n,SkillIds=Skills("pidgeotto"),
                Trigger=SkillVisualTrigger.Area,Placement=SkillVisualPlacement.AreaCells,AreaCellIndex=n-1,AlignToDirection=true,
                Frames=Frames("wing-attack/wing_attack_tile_"+n+"_64x64_sheet.png"),Offset=new Vector2(0,14),DelaySeconds=(n-1)*2/12f});
            entries.Add(new SkillVisualEntry{Name="AgilityAfterimage",SkillIds=Skills("pidgeot"),Trigger=SkillVisualTrigger.MovementArrived,
                Placement=SkillVisualPlacement.Caster,SpriteAfterimage=true,Opacity=.4f,TrailCopies=4,FadeSeconds=.28f});
            entries.AddRange(PreparedStatusAndAttackEntries());
            entries.AddRange(PreparedSporeAndGroundEntries());
            entries.AddRange(HydroPumpEntries());
            entries.AddRange(KarateChopEntries());
            entries.Add(SeismicTossEntry());
            entries.AddRange(ConstrictEntries());
            entries.AddRange(SwordsDanceEntries());
            entries.AddRange(BindAndExplosionEntries());
            entries.Add(ThunderShockEntry());
            entries.Add(ThunderboltEntry());
            entries.Add(MinimizeEntry());
            entries.AddRange(PreparedFiveEntries());
            entries.AddRange(PreparedStompAndSonicEntries());
            entries.Add(DrillPeckAfterimageEntry());
            entries.AddRange(TriAttackEntries());
            entries.AddRange(PreparedShellAndSmokeEntries());
            entries.AddRange(VineAndLeafEntries());
            entries.AddRange(SolarFireAndWaterEntries());
            entries.AddRange(SkullBashEntries());
            entries.AddRange(HypnosisAndAmnesiaEntries());
            entries.AddRange(SubmissionEntries());
            entries.AddRange(PsychicEntries());
            entries.AddRange(HighJumpKickEntries());
            entries.AddRange(PreparedScreenAndBeamEntries());
            entries.AddRange(DoubleKickAndBodySlamEntries());
            entries.AddRange(HornEntries());
            entries.AddRange(ThrashEntries());
            entries.AddRange(PreparedTenEntries());
            entries.AddRange(IceBeamEntries());
            entries.Add(RestEntry());
            entries.AddRange(BlizzardEntries());
            entries.AddRange(ThunderEntries());
            entries.AddRange(SkyAttackEntries());
            entries.AddRange(MetronomeEntries());
            foreach(var entry in entries)if(!entry.ScaleUnitSprite&&!entry.Lightning&&!entry.SpriteAfterimage&&!entry.HideUnitSprite&&entry.Frames.Length==0)throw new InvalidOperationException("Missing frames: "+entry.Name);
            var catalog=ScriptableObject.CreateInstance<SkillVisualCatalog>();catalog.Entries=entries.ToArray();
            AssetDatabase.CreateAsset(catalog,CatalogPath);AssetDatabase.SaveAssets();return catalog;
        }
        private static SkillVisualEntry[] MetronomeEntries()
        {
            string[] Branch(int choice)=>Enumerable.Range(1,3).Select(r=>"skill-mew-r"+r+"-choice"+choice).ToArray();
            var all=Enumerable.Range(0,3).SelectMany(Branch).ToArray();
            return new[]{
                new SkillVisualEntry{Name="MetronomeCast",SkillIds=all,Trigger=SkillVisualTrigger.CastStarted,Placement=SkillVisualPlacement.Caster,
                    Frames=Frames("metronome/Metronome_Cast_8F_4x2_64.png"),FramesPerSecond=20,Size=.75f,FollowUnit=true},
                new SkillVisualEntry{Name="MetronomeDamage",SkillIds=Branch(0),Trigger=SkillVisualTrigger.Damage,
                    Frames=Frames("metronome/Metronome_Damage_8F_4x2_64.png"),FramesPerSecond=24,Size=1},
                new SkillVisualEntry{Name="MetronomeHeal",SkillIds=Branch(1),Trigger=SkillVisualTrigger.Heal,
                    Frames=Frames("metronome/Metronome_Heal_8F_4x2_64.png"),FramesPerSecond=24,Size=.75f,FollowUnit=true},
                new SkillVisualEntry{Name="MetronomeStun",StatusIds=Enumerable.Range(1,3).Select(r=>"status-mew-r"+r+"-choice2-s0").ToArray(),Trigger=SkillVisualTrigger.StatusApplied,
                    Frames=Frames("metronome/Metronome_Stun_8F_4x2_64.png"),FramesPerSecond=24,Size=.75f,FollowUnit=true},
                new SkillVisualEntry{Name="MetronomeField3",SkillIds=Enumerable.Range(0,3).Select(c=>"skill-mew-r3-choice"+c).ToArray(),Trigger=SkillVisualTrigger.CastEffect,Placement=SkillVisualPlacement.BoardCells,
                    Frames=Frames("metronome/Metronome_3Star_Field_8F_4x2_64.png"),FramesPerSecond=16,Size=1,Opacity=.3f,Offset=new Vector2(0,5)}
            };
        }
        [MenuItem("PokeChess/Visuals/Register Metronome")]
        public static void RegisterMetronome()
        {
            var catalog=CreateInitialCatalog();var entries=catalog.Entries.ToList();
            foreach(var entry in MetronomeEntries()) {
                if(entry.Frames.Length==0)throw new InvalidOperationException("Missing frames: "+entry.Name);
                if(!entries.Any(e=>e.Name==entry.Name))entries.Add(entry);
            }
            catalog.Entries=entries.ToArray();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
        }
        private static SkillVisualEntry[] SkyAttackEntries()=>new[]{
            new SkillVisualEntry{Name="SkyAttackTakeoff",StatusIds=Enumerable.Range(1,3).Select(r=>"status-moltres-r"+r+"-s1").ToArray(),Trigger=SkillVisualTrigger.StatusApplied,
                Frames=Frames("sky-attack/Phoenix_Takeoff_8F_4x2_64.png"),FramesPerSecond=24,Size=1,Offset=new Vector2(0,14)},
            new SkillVisualEntry{Name="SkyAttackHidden",StatusIds=Enumerable.Range(1,3).Select(r=>"status-moltres-r"+r+"-s1").ToArray(),Trigger=SkillVisualTrigger.StatusActive,HideUnitSprite=true},
            new SkillVisualEntry{Name="SkyAttackDive",SkillIds=Skills("moltres"),Trigger=SkillVisualTrigger.MovementArrived,Placement=SkillVisualPlacement.Caster,
                MovementKind=PokeChess.Core.Battle.SkillMovementKind.JumpToCluster,Frames=Frames("sky-attack/Phoenix_DiveTrail_8F_4x2_64.png"),
                FramesPerSecond=48,DurationSeconds=1/6f,DropFromAboveCells=2,Size=1,Offset=new Vector2(0,14)},
            new SkillVisualEntry{Name="SkyAttackImpact",SkillIds=new[]{"skill-moltres-r1","skill-moltres-r2"},Trigger=SkillVisualTrigger.MovementArrived,Placement=SkillVisualPlacement.Caster,
                MovementKind=PokeChess.Core.Battle.SkillMovementKind.JumpToCluster,Frames=Frames("sky-attack/Phoenix_1Star_Impact_8F_4x2_64.png"),FramesPerSecond=24,DelaySeconds=1/6f,Size=1.25f,Offset=new Vector2(0,5)},
            new SkillVisualEntry{Name="SkyAttackImpact3",SkillIds=new[]{"skill-moltres-r3"},Trigger=SkillVisualTrigger.MovementArrived,Placement=SkillVisualPlacement.Caster,
                MovementKind=PokeChess.Core.Battle.SkillMovementKind.JumpToCluster,Frames=Frames("sky-attack/Phoenix_3Star_Impact_8F_4x2_64.png"),FramesPerSecond=24,DelaySeconds=1/6f,Size=1.5f,Offset=new Vector2(0,5)},
            new SkillVisualEntry{Name="SkyAttackArea3",SkillIds=new[]{"skill-moltres-r3"},Trigger=SkillVisualTrigger.Area,Placement=SkillVisualPlacement.AreaCells,
                Frames=Frames("sky-attack/Phoenix_3Star_AreaTile_8F_4x2_64.png"),FramesPerSecond=24,DelaySeconds=1/6f,Size=1,Offset=new Vector2(0,5),Opacity=.5f},
            new SkillVisualEntry{Name="SkyAttackResidual",StatusIds=ConstrictStatuses("moltres"),Trigger=SkillVisualTrigger.StatusActive,
                Frames=Frames("sky-attack/Phoenix_ResidualFlames_8F_4x2_64.png"),FramesPerSecond=16,Playback=SkillVisualPlayback.Loop,FollowUnit=true,DelaySeconds=.5f,Size=.75f,Offset=new Vector2(0,5),Opacity=.5f}
        };
        [MenuItem("PokeChess/Visuals/Register Sky Attack")]
        public static void RegisterSkyAttack()
        {
            var catalog=CreateInitialCatalog();var entries=catalog.Entries.ToList();
            foreach(var entry in SkyAttackEntries()) {
                if(!entry.HideUnitSprite&&entry.Frames.Length==0)throw new InvalidOperationException("Missing frames: "+entry.Name);
                if(!entries.Any(e=>e.Name==entry.Name))entries.Add(entry);
            }
            catalog.Entries=entries.ToArray();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
        }
        private static SkillVisualEntry[] ThunderEntries()=>new[]{
            new SkillVisualEntry{Name="ThunderStrike",SkillIds=Skills("zapdos"),Trigger=SkillVisualTrigger.Damage,
                Lightning=true,OverheadLightning=true,LightningSeconds=.32f,LightningWidth=.075f,Offset=new Vector2(0,5),
                LightningGlow=new Color(.45f,.3f,1,.7f),LightningCore=new Color(.93f,.96f,1,1)},
            new SkillVisualEntry{Name="ThunderImpact",SkillIds=Skills("zapdos"),Trigger=SkillVisualTrigger.Damage,
                Frames=Frames("thunder/Thunder_FootImpact_8F_4x2_64.png"),FramesPerSecond=24,Size=1,Offset=new Vector2(0,5)}
        };
        [MenuItem("PokeChess/Visuals/Register Thunder")]
        public static void RegisterThunder()
        {
            var catalog=CreateInitialCatalog();var entries=catalog.Entries.ToList();
            foreach(var entry in ThunderEntries()) {
                if(!entry.Lightning&&entry.Frames.Length==0)throw new InvalidOperationException("Missing frames: "+entry.Name);
                if(!entries.Any(e=>e.Name==entry.Name))entries.Add(entry);
            }
            catalog.Entries=entries.ToArray();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
        }
        private static SkillVisualEntry[] BlizzardEntries()=>new[]{
            new SkillVisualEntry{Name="BlizzardImpact",SkillIds=Skills("articuno"),Trigger=SkillVisualTrigger.Damage,
                Frames=Frames("blizzard/Blizzard_1Star_Impact_8f_4x2_64.png"),FramesPerSecond=16,Size=1,FollowUnit=true},
            new SkillVisualEntry{Name="BlizzardMapSnow",SkillIds=new[]{"skill-articuno-r3"},Trigger=SkillVisualTrigger.CastEffect,
                Placement=SkillVisualPlacement.BoardCells,Frames=Frames("blizzard/Blizzard_3Star_MapSnow_8f_4x2_64.png"),
                FramesPerSecond=16,Playback=SkillVisualPlayback.Loop,DurationSeconds=1.5f,Size=1,Offset=new Vector2(0,14),Opacity=.35f}
        };
        [MenuItem("PokeChess/Visuals/Register Blizzard")]
        public static void RegisterBlizzard()
        {
            var catalog=CreateInitialCatalog();var entries=catalog.Entries.ToList();
            foreach(var entry in BlizzardEntries()) {
                if(entry.Frames.Length==0)throw new InvalidOperationException("Missing frames: "+entry.Name);
                if(!entries.Any(e=>e.Name==entry.Name))entries.Add(entry);
            }
            catalog.Entries=entries.ToArray();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
        }
        private static SkillVisualEntry RestEntry()=>new SkillVisualEntry {
            Name="RestHealing",StatusIds=ConstrictStatuses("snorlax"),Trigger=SkillVisualTrigger.StatusActive,
            Frames=Frames("rest/Rest_Heal_Loop_8f_4x2_64.png"),FramesPerSecond=16,Playback=SkillVisualPlayback.Loop,
            FollowUnit=true,Size=.75f,Offset=new Vector2(0,14),Opacity=.7f
        };
        [MenuItem("PokeChess/Visuals/Register Rest")]
        public static void RegisterRest()
        {
            var catalog=CreateInitialCatalog();var entry=RestEntry();
            if(entry.Frames.Length==0)throw new InvalidOperationException("Missing frames: "+entry.Name);
            if(catalog.Entries.Any(e=>e.Name==entry.Name))return;
            catalog.Entries=catalog.Entries.Concat(new[]{entry}).ToArray();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
        }
        private static SkillVisualEntry[] IceBeamEntries()=>new[]{
            new SkillVisualEntry{Name="IceBeamCharge",SkillIds=Skills("lapras"),Trigger=SkillVisualTrigger.CastStarted,Placement=SkillVisualPlacement.Caster,
                Frames=Frames("ice-beam/IceBeam_Charge_8f_4x2_64.png"),FramesPerSecond=20,FollowUnit=true,Size=.75f},
            new SkillVisualEntry{Name="IceBeamLine",SkillIds=Skills("lapras"),Trigger=SkillVisualTrigger.Damage,
                Lightning=true,StraightBeam=true,LightningSeconds=.4f,LightningWidth=.06f,
                LightningGlow=new Color(.15f,.7f,1,.65f),LightningCore=new Color(.88f,.98f,1,1)},
            new SkillVisualEntry{Name="IceBeamImpact",SkillIds=Skills("lapras"),Trigger=SkillVisualTrigger.Damage,
                Frames=Frames("ice-beam/IceBeam_Impact_8f_4x2_64.png"),FramesPerSecond=24,DelaySeconds=.08f,Size=.75f,FollowUnit=true}
        };
        [MenuItem("PokeChess/Visuals/Register Ice Beam")]
        public static void RegisterIceBeam()
        {
            var catalog=CreateInitialCatalog();var entries=catalog.Entries.ToList();
            foreach(var entry in IceBeamEntries()) {
                if(!entry.Lightning&&entry.Frames.Length==0)throw new InvalidOperationException("Missing frames: "+entry.Name);
                if(!entries.Any(e=>e.Name==entry.Name))entries.Add(entry);
            }
            catalog.Entries=entries.ToArray();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
        }
        private static SkillVisualEntry[] PreparedTenEntries()
        {
            SkillVisualEntry Hit(string name,string species,string path,int fps=24)=>new SkillVisualEntry{Name=name,SkillIds=Skills(species),Trigger=SkillVisualTrigger.Damage,Frames=Frames(path),FramesPerSecond=fps,Size=1};
            SkillVisualEntry Flight(string name,string species,string path)=>new SkillVisualEntry{Name=name,SkillIds=Skills(species),Trigger=SkillVisualTrigger.BeforeEffect,Placement=SkillVisualPlacement.CasterToTarget,Frames=Frames(path),FramesPerSecond=24,AlignToDirection=true};
            var fire=Hit("FireBlast","arcanine","fire-blast/FireBlast_12f_6x2_64.png");fire.Trigger=SkillVisualTrigger.Area;fire.Placement=SkillVisualPlacement.AreaCells;fire.Opacity=.7f;
            var normalDream=Hit("DreamEaterHit","gengar","dream-eater/DreamEater_Hit_12f_6x2_64.png");normalDream.NumericCondition=2;
            var sleepDream=Hit("DreamEaterAsleepHit","gengar","dream-eater/DreamEater_AsleepHit_12f_6x2_64.png");sleepDream.NumericCondition=1;
            var normalCut=Hit("GuillotineNormal","pinsir","guillotine/ScissorCut_Normal_12f_6x2_64.png");normalCut.NumericCondition=2;
            var executeCut=Hit("GuillotineExecute","pinsir","guillotine/ScissorCut_Execute_12f_6x2_64.png");executeCut.NumericCondition=1;
            var main=Hit("SlamMain","dratini","slam/ForcefulStrike_MainHit_10f_5x2_64.png");main.StepIndex=0;
            var rear=Hit("SlamRear","dratini","slam/ForcefulStrike_RearShock_8f_4x2_64.png");
            // Main and rear effects share the same step; filter by effect index below.
            main.EffectIndex=0;rear.EffectIndex=2;
            return new[]{fire,normalDream,sleepDream,normalCut,executeCut,main,rear,
                new SkillVisualEntry{Name="TeleportFocus",StatusIds=ConstrictStatuses("abra"),Trigger=SkillVisualTrigger.StatusActive,
                    IntroFrames=Frames("teleport/Focus_Cast_8f_4x2_64.png"),Frames=Frames("teleport/Focus_Loop_8f_4x2_64.png"),FramesPerSecond=24,Playback=SkillVisualPlayback.Loop,FollowUnit=true,Size=1,Opacity=.65f},
                new SkillVisualEntry{Name="TeleportDepart",SkillIds=Skills("abra"),Trigger=SkillVisualTrigger.MovementStarted,Placement=SkillVisualPlacement.Caster,
                    MovementKind=PokeChess.Core.Battle.SkillMovementKind.TeleportSafe,Frames=Frames("teleport/Blink_Depart_10f_5x2_64.png"),FramesPerSecond=24,Size=1},
                new SkillVisualEntry{Name="TeleportArrive",SkillIds=Skills("abra"),Trigger=SkillVisualTrigger.MovementArrived,Placement=SkillVisualPlacement.Caster,
                    MovementKind=PokeChess.Core.Battle.SkillMovementKind.TeleportSafe,Frames=Frames("teleport/Blink_Arrive_10f_5x2_64.png"),FramesPerSecond=24,Size=1},
                new SkillVisualEntry{Name="TeleportMana",SkillIds=Skills("abra"),Trigger=SkillVisualTrigger.MovementArrived,Placement=SkillVisualPlacement.Caster,
                    MovementKind=PokeChess.Core.Battle.SkillMovementKind.TeleportSafe,Frames=Frames("teleport/Mana_Recover_8f_4x2_64.png"),FramesPerSecond=24,DelaySeconds=.2f,Size=.65f,FollowUnit=true,Offset=new Vector2(0,42)},
                new SkillVisualEntry{Name="PsybeamCast",SkillIds=Skills("kadabra"),Trigger=SkillVisualTrigger.CastStarted,Placement=SkillVisualPlacement.Caster,
                    Frames=Frames("psybeam/Psybeam_Cast_8f_4x2_64.png"),FramesPerSecond=24},
                Flight("PsybeamFlight","kadabra","psybeam/Psybeam_Projectile_8f_4x2_64.png"),Hit("PsybeamImpact","kadabra","psybeam/Psybeam_Impact_10f_5x2_64.png"),
                Hit("LowKick","machoke","low-kick/LegSweep_Clean_12f_6x2_64.png"),
                Flight("RockThrowFlight","geodude","rock-throw/RockThrow_Projectile_8f_4x2_64.png"),Hit("RockThrowImpact","geodude","rock-throw/RockThrow_Impact_12f_6x2_64.png"),
                new SkillVisualEntry{Name="ConfuseRayFocus",SkillIds=Skills("gastly"),Trigger=SkillVisualTrigger.CastStarted,Placement=SkillVisualPlacement.Caster,
                    Frames=Frames("confuse-ray/StrangeLight_Focus_8f_4x2_64.png"),FramesPerSecond=24},
                new SkillVisualEntry{Name="ConfuseRayFlight",StatusIds=ConstrictStatuses("gastly"),Trigger=SkillVisualTrigger.StatusApplied,Placement=SkillVisualPlacement.CasterToTarget,
                    Frames=Frames("confuse-ray/StrangeLight_Projectile_8f_4x2_64.png"),FramesPerSecond=24,Playback=SkillVisualPlayback.Loop,DurationSeconds=.2f,AlignToDirection=true},
                new SkillVisualEntry{Name="ConfuseRayImpact",StatusIds=ConstrictStatuses("gastly"),Trigger=SkillVisualTrigger.StatusApplied,
                    Frames=Frames("confuse-ray/StrangeLight_Impact_8f_4x2_64.png"),FramesPerSecond=24,DelaySeconds=.2f,FollowUnit=true},
                new SkillVisualEntry{Name="DreamEaterReturn",SkillIds=Skills("gengar"),Trigger=SkillVisualTrigger.Damage,Placement=SkillVisualPlacement.TargetToCaster,
                    Frames=Frames("dream-eater/DreamEater_Return_8f_4x2_64.png"),FramesPerSecond=24,Playback=SkillVisualPlayback.Loop,DelaySeconds=.25f,DurationSeconds=.3f,AlignToDirection=true},
                new SkillVisualEntry{Name="DreamEaterHeal",SkillIds=Skills("gengar"),Trigger=SkillVisualTrigger.Damage,Placement=SkillVisualPlacement.Caster,
                    Frames=Frames("dream-eater/DreamEater_Heal_8f_4x2_64.png"),FramesPerSecond=24,DelaySeconds=.55f,FollowUnit=true},
                new SkillVisualEntry{Name="SurfWave",SkillIds=Skills("dragonair"),Trigger=SkillVisualTrigger.Area,Placement=SkillVisualPlacement.AreaCells,
                    Frames=Frames("surf/Surf_SingleTile_12f_6x2_64.png"),FramesPerSecond=24,AreaDistanceDelaySeconds=2/24f,Size=1,Opacity=.65f,AlignToDirection=true,Offset=new Vector2(0,14)},
                Hit("SurfHit","dragonair","surf/Surf_HitSplash_8f_4x2_64.png"),
                new SkillVisualEntry{Name="SurfAttackSpeedDown",StatusIds=ConstrictStatuses("dragonair"),Trigger=SkillVisualTrigger.StatusActive,
                    Frames=Frames("surf/Surf_AttackSpeedDown_8f_4x2_64.png"),FramesPerSecond=12,Playback=SkillVisualPlayback.Loop,FollowUnit=true,Offset=new Vector2(0,42)}
            };
        }
        [MenuItem("PokeChess/Visuals/Register Fire Blast Teleport Psybeam Low Kick Rock Throw Confuse Ray Dream Eater Guillotine Slam Surf")]
        public static void RegisterPreparedTen()
        {
            var catalog=CreateInitialCatalog();var entries=catalog.Entries.ToList();
            foreach(var entry in PreparedTenEntries()) {
                if(entry.Frames.Length==0)throw new InvalidOperationException("Missing frames: "+entry.Name);
                if(!entries.Any(e=>e.Name==entry.Name))entries.Add(entry);
            }
            catalog.Entries=entries.ToArray();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
        }
        private static SkillVisualEntry[] ThrashEntries()
        {
            var entries=new List<SkillVisualEntry>{
                new SkillVisualEntry{Name="ThrashBuff",StatusIds=ConstrictStatuses("nidoking"),Trigger=SkillVisualTrigger.StatusActive,
                    IntroFrames=Frames("thrash/Rampage_Cast_8f_4x2_64.png"),Frames=Frames("thrash/Rampage_BuffLoop_8f_4x2_64.png"),
                    FramesPerSecond=24,Playback=SkillVisualPlayback.Loop,FollowUnit=true,Size=1,Offset=new Vector2(0,14),Opacity=.65f},
                new SkillVisualEntry{Name="ThrashImpact",SkillIds=Skills("nidoking"),Trigger=SkillVisualTrigger.AttackSplash,
                    Frames=Frames("thrash/Rampage_Impact_Center_8f_4x2_64.png"),FramesPerSecond=24,Size=1,Offset=new Vector2(0,14)}
            };
            var directions=new[]{"E","NE","NW","W","SW","SE"};
            for(int n=0;n<6;n++)entries.Add(new SkillVisualEntry{Name="ThrashSplash"+n,SkillIds=Skills("nidoking"),Trigger=SkillVisualTrigger.AttackSplash,
                Placement=SkillVisualPlacement.TargetNeighbor,RingIndex=n,Frames=Frames("thrash/Rampage_Splash_"+n+"_"+directions[n]+"_8f_4x2_64.png"),
                FramesPerSecond=24,Size=1,Offset=new Vector2(0,14)});
            return entries.ToArray();
        }
        [MenuItem("PokeChess/Visuals/Register Thrash")]
        public static void RegisterThrash()
        {
            var catalog=CreateInitialCatalog();var entries=catalog.Entries.ToList();
            foreach(var entry in ThrashEntries()) {
                if(entry.Frames.Length==0)throw new InvalidOperationException("Missing frames: "+entry.Name);
                if(!entries.Any(e=>e.Name==entry.Name))entries.Add(entry);
            }
            catalog.Entries=entries.ToArray();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
        }
        private static SkillVisualEntry[] HornEntries()=>new[]{
            new SkillVisualEntry{Name="HornAttack",SkillIds=Skills("nidoran-m"),Trigger=SkillVisualTrigger.Damage,
                Frames=Frames("horn-attack/HornAttack_Clean_12f_6x2_64.png"),FramesPerSecond=24,Size=1,AlignToDirection=true},
            new SkillVisualEntry{Name="HornDrillNormal",SkillIds=Skills("nidorino"),Trigger=SkillVisualTrigger.Damage,NumericCondition=2,
                Frames=Frames("horn-drill/HornDrill_Normal_12f_6x2_64.png"),FramesPerSecond=24,Size=1,AlignToDirection=true},
            new SkillVisualEntry{Name="HornDrillFinisher",SkillIds=Skills("nidorino"),Trigger=SkillVisualTrigger.Damage,NumericCondition=1,
                Frames=Frames("horn-drill/HornDrill_Finisher_12f_6x2_64.png"),FramesPerSecond=24,Size=1,AlignToDirection=true}
        };
        [MenuItem("PokeChess/Visuals/Register Horn Attack And Horn Drill")]
        public static void RegisterHorns()
        {
            var catalog=CreateInitialCatalog();var entries=catalog.Entries.ToList();
            foreach(var entry in HornEntries()) {
                if(entry.Frames.Length==0)throw new InvalidOperationException("Missing frames: "+entry.Name);
                if(!entries.Any(e=>e.Name==entry.Name))entries.Add(entry);
            }
            catalog.Entries=entries.ToArray();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
        }
        private static SkillVisualEntry[] DoubleKickAndBodySlamEntries()
        {
            var kicks=Frames("double-kick/DoubleKick_12f_6x2_64.png");
            return new[]{
                new SkillVisualEntry{Name="DoubleKickFirst",SkillIds=Skills("nidorina"),Trigger=SkillVisualTrigger.Damage,StepIndex=0,
                    Frames=kicks.Where(s=>s.rect.y>=64).ToArray(),FramesPerSecond=25,Size=1},
                new SkillVisualEntry{Name="DoubleKickSecond",SkillIds=Skills("nidorina"),Trigger=SkillVisualTrigger.Damage,StepIndex=1,
                    Frames=kicks.Where(s=>s.rect.y<64).ToArray(),FramesPerSecond=30,Size=1},
                new SkillVisualEntry{Name="BodySlamLanding",SkillIds=Skills("nidoqueen"),Trigger=SkillVisualTrigger.MovementArrived,
                    MovementKind=PokeChess.Core.Battle.SkillMovementKind.JumpToCluster,Placement=SkillVisualPlacement.Caster,
                    Frames=Frames("body-slam/Press_Landing_Impact_12f_6x2_64.png"),FramesPerSecond=24,Size=1,Offset=new Vector2(0,5)},
                new SkillVisualEntry{Name="BodySlamNeighbors",SkillIds=Skills("nidoqueen"),Trigger=SkillVisualTrigger.Area,
                    Placement=SkillVisualPlacement.AreaCells,ExcludeAreaCenter=true,
                    Frames=Frames("body-slam/Press_AoE_Neighbor_8f_4x2_64.png"),FramesPerSecond=24,Size=1,Offset=new Vector2(0,5)},
                new SkillVisualEntry{Name="BodySlamShield",SkillIds=Skills("nidoqueen"),Trigger=SkillVisualTrigger.ShieldActive,DurationSeconds=4,
                    IntroFrames=Frames("body-slam/Press_Shield_Cast_8f_4x2_64.png"),Frames=Frames("body-slam/Press_Shield_Loop_8f_4x2_64.png"),
                    OutroFrames=Frames("body-slam/Press_Shield_Fade_6f_3x2_64.png"),FramesPerSecond=18,Playback=SkillVisualPlayback.Loop,
                    Size=1,Offset=new Vector2(0,14),Opacity=.55f}
            };
        }
        [MenuItem("PokeChess/Visuals/Register Double Kick And Body Slam")]
        public static void RegisterDoubleKickAndBodySlam()
        {
            var catalog=CreateInitialCatalog();var entries=catalog.Entries.ToList();
            foreach(var entry in DoubleKickAndBodySlamEntries()) {
                if(entry.Frames.Length==0)throw new InvalidOperationException("Missing frames: "+entry.Name);
                if(!entries.Any(e=>e.Name==entry.Name))entries.Add(entry);
            }
            catalog.Entries=entries.ToArray();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
        }
        private static SkillVisualEntry[] PreparedScreenAndBeamEntries()
        {
            var entries=new List<SkillVisualEntry>{
                new SkillVisualEntry{Name="LightScreen",StatusIds=ConstrictStatuses("chansey"),Trigger=SkillVisualTrigger.StatusActive,
                    IntroFrames=Frames("light-screen/light_screen_glass_cast_4x2_64.png"),Frames=Frames("light-screen/light_screen_glass_loop_4x2_64.png"),
                    OutroFrames=Frames("light-screen/light_screen_glass_fade_3x2_64.png"),FramesPerSecond=18,Playback=SkillVisualPlayback.Loop,FollowUnit=true,Size=1,Offset=new Vector2(0,14),Opacity=.45f},
                new SkillVisualEntry{Name="Barrier",SkillIds=Skills("mr-mime"),Trigger=SkillVisualTrigger.ShieldActive,DurationSeconds=4,
                    IntroFrames=Frames("barrier/Barrier_Cast_8f_4x2_64.png"),Frames=Frames("barrier/Barrier_Loop_8f_4x2_64.png"),
                    OutroFrames=Frames("barrier/Barrier_Fade_6f_3x2_64.png"),FramesPerSecond=18,Playback=SkillVisualPlayback.Loop,Size=1,Offset=new Vector2(0,14),Opacity=.55f},
                new SkillVisualEntry{Name="DizzyPunch",SkillIds=Skills("kangaskhan"),Trigger=SkillVisualTrigger.Damage,
                    Frames=Frames("dizzy-punch/JamJamPunch_10f_5x2_64.png"),FramesPerSecond=20},
                new SkillVisualEntry{Name="ThunderPunch",SkillIds=Skills("electabuzz"),Trigger=SkillVisualTrigger.Damage,
                    Frames=Frames("thunder-punch/ThunderPunch_Impact_12f_6x2_64.png"),FramesPerSecond=24},
                new SkillVisualEntry{Name="LovelyKissSleep",StatusIds=ConstrictStatuses("jynx"),Trigger=SkillVisualTrigger.StatusApplied,
                    Frames=Frames("lovely-kiss/kiss_sleep_6x2_64.png"),FramesPerSecond=24,FollowUnit=true},
                new SkillVisualEntry{Name="LovelyKissReturn",StatusIds=ConstrictStatuses("jynx"),Trigger=SkillVisualTrigger.StatusApplied,Placement=SkillVisualPlacement.TargetToCaster,
                    Frames=Frames("lovely-kiss/DevilsKiss_ReturnFlight_8f_4x2_64.png"),FramesPerSecond=24,Playback=SkillVisualPlayback.Loop,
                    DelaySeconds=.35f,DurationSeconds=.3f,AlignToDirection=true},
                new SkillVisualEntry{Name="LovelyKissHeal",SkillIds=Skills("jynx"),Trigger=SkillVisualTrigger.Heal,Placement=SkillVisualPlacement.Caster,
                    IntroFrames=Frames("lovely-kiss/DevilsKiss_ReturnAbsorb_6f_3x2_64.png"),Frames=Frames("lovely-kiss/self_heal_4x2_64.png"),
                    FramesPerSecond=24,Playback=SkillVisualPlayback.Loop,DelaySeconds=.65f,DurationSeconds=14/24f,FollowUnit=true},
                new SkillVisualEntry{Name="HyperBeamCharge",SkillIds=Skills("aerodactyl","dragonite"),Trigger=SkillVisualTrigger.CastStarted,Placement=SkillVisualPlacement.Caster,
                    Frames=Frames("hyper-beam/HyperBeam_Charge_12f_6x2_64.png"),FramesPerSecond=15,FollowUnit=true},
                new SkillVisualEntry{Name="HyperBeamRecoil",StatusIds=ConstrictStatuses("aerodactyl","dragonite"),Trigger=SkillVisualTrigger.StatusActive,
                    Frames=Frames("hyper-beam/HyperBeam_Recoil_08f_4x2_64.png"),FramesPerSecond=12,Playback=SkillVisualPlayback.Loop,FollowUnit=true}
            };
            foreach(var species in new[]{"aerodactyl","dragonite"}) {
                int count=species=="aerodactyl"?3:4;
                for(int n=1;n<=count;n++) {
                    var sheet=species=="dragonite"?"HyperBeam_4Tile_Tile"+n:"HyperBeam_Beam_Tile"+n;
                    var frames=Frames("hyper-beam/"+sheet+"_16f_4x4_64.png");
                    if(frames.Length==0)throw new InvalidOperationException("Missing Hyper Beam frames");
                    var first=frames[0];int leading=(3-Mathf.RoundToInt(first.rect.y/64))*4+Mathf.RoundToInt(first.rect.x/64);
                    entries.Add(new SkillVisualEntry{Name="HyperBeam_"+species+"_"+n,SkillIds=Skills(species),Trigger=SkillVisualTrigger.Area,Placement=SkillVisualPlacement.AreaCells,
                        AreaCellIndex=n-1,Frames=frames,FramesPerSecond=24,DelaySeconds=leading/24f,AlignToDirection=true,Size=1,Offset=new Vector2(0,24)});
                }
            }
            return entries.ToArray();
        }
        [MenuItem("PokeChess/Visuals/Register Light Screen Dizzy Punch Barrier Lovely Kiss Thunder Punch Hyper Beam")]
        public static void RegisterScreenAndBeam()
        {
            var catalog=CreateInitialCatalog();var entries=catalog.Entries.ToList();
            foreach(var entry in PreparedScreenAndBeamEntries()) {
                if(entry.Frames.Length==0)throw new InvalidOperationException("Missing frames: "+entry.Name);
                if(!entries.Any(e=>e.Name==entry.Name))entries.Add(entry);
            }
            catalog.Entries=entries.ToArray();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
        }
        private static SkillVisualEntry CommonHealthCostEntry()=>new SkillVisualEntry {
            Name="HealthCost",SkillIds=Skills("poliwrath","machamp","hitmonlee"),Trigger=SkillVisualTrigger.HealthCost,
            Placement=SkillVisualPlacement.Caster,Frames=FramesAt(HealthCostPath),FramesPerSecond=16,FollowUnit=true
        };
        private static SkillVisualEntry[] HighJumpKickEntries()=>new[]{
            new SkillVisualEntry{Name="HighJumpKickAfterimage",SkillIds=Skills("hitmonlee"),Trigger=SkillVisualTrigger.MovementStarted,
                MovementKind=PokeChess.Core.Battle.SkillMovementKind.JumpToTarget,Placement=SkillVisualPlacement.Caster,
                SpriteAfterimage=true,TrailCopies=4,Opacity=.4f,FadeSeconds=.28f},
            new SkillVisualEntry{Name="HighJumpKickImpact",SkillIds=Skills("hitmonlee"),Trigger=SkillVisualTrigger.Damage,
                Frames=Frames("high-jump-kick/knee_impact_12f_6x2_64.png"),FramesPerSecond=24,Size=.75f}
        };
        [MenuItem("PokeChess/Visuals/Register High Jump Kick And Shared Health Cost")]
        public static void RegisterHighJumpKick()
        {
            const string common="Assets/_Project/Sprites/Effects/Common";
            if(!AssetDatabase.IsValidFolder(common))AssetDatabase.CreateFolder("Assets/_Project/Sprites/Effects","Common");
            if(!AssetDatabase.IsValidFolder(common+"/health-cost"))AssetDatabase.CreateFolder(common,"health-cost");
            const string old=Art+"submission/hell_wheel_hp_cost_8f_4x2_64.png";
            if(AssetDatabase.LoadMainAssetAtPath(old)!=null) {
                var error=AssetDatabase.MoveAsset(old,HealthCostPath);if(!string.IsNullOrEmpty(error))throw new InvalidOperationException(error);
            }
            var catalog=CreateInitialCatalog();var entries=catalog.Entries.ToList();var shared=CommonHealthCostEntry();
            if(shared.Frames.Length==0)throw new InvalidOperationException("Missing shared health cost frames");
            entries.RemoveAll(e=>e.Name=="SubmissionHealthCost"||e.Name=="HealthCost");entries.Add(shared);
            foreach(var entry in HighJumpKickEntries()) {
                if(!entry.SpriteAfterimage&&entry.Frames.Length==0)throw new InvalidOperationException("Missing frames: "+entry.Name);
                if(!entries.Any(e=>e.Name==entry.Name))entries.Add(entry);
            }
            catalog.Entries=entries.ToArray();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
        }
        private static SkillVisualEntry[] PsychicEntries()=>new[]{
            new SkillVisualEntry{Name="PsychicArea",SkillIds=Skills("mewtwo"),Trigger=SkillVisualTrigger.Area,Placement=SkillVisualPlacement.AreaCells,
                Frames=Frames("psychic/Psychokinesis_Area_8F_4x2_64.png"),FramesPerSecond=16,Size=1,Offset=new Vector2(0,5),Opacity=.5f},
            new SkillVisualEntry{Name="PsychicEnergy",SkillIds=Skills("slowbro","alakazam","mewtwo"),Trigger=SkillVisualTrigger.Damage,
                Frames=Frames("psychic/psychokinesis_energy_12f_6x2_64.png"),FramesPerSecond=18,FollowUnit=true},
            new SkillVisualEntry{Name="PsychicDefenseBreak",StatusIds=ConstrictStatuses("slowbro","alakazam","mewtwo"),Trigger=SkillVisualTrigger.StatusApplied,
                Frames=Frames("psychic/special_defense_break_8f_4x2_64.png"),FramesPerSecond=18,FollowUnit=true,Offset=new Vector2(0,42)}
        };
        [MenuItem("PokeChess/Visuals/Register Psychic")]
        public static void RegisterPsychic()
        {
            var catalog=CreateInitialCatalog();var entries=catalog.Entries.ToList();
            foreach(var entry in PsychicEntries()) {
                if(entry.Frames.Length==0)throw new InvalidOperationException("Missing frames: "+entry.Name);
                if(!entries.Any(e=>e.Name==entry.Name))entries.Add(entry);
            }
            catalog.Entries=entries.ToArray();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
        }
        private static SkillVisualEntry[] SubmissionEntries()=>new[]{
            new SkillVisualEntry{Name="SubmissionAfterimage",SkillIds=Skills("poliwrath","machamp"),Trigger=SkillVisualTrigger.MovementStarted,
                MovementKind=PokeChess.Core.Battle.SkillMovementKind.CarryTarget,Placement=SkillVisualPlacement.Caster,
                SpriteAfterimage=true,TrailCopies=4,Opacity=.4f,FadeSeconds=.28f},
            new SkillVisualEntry{Name="SubmissionImpact",SkillIds=Skills("poliwrath","machamp"),Trigger=SkillVisualTrigger.Damage,
                Frames=Frames("submission/hell_wheel_impact_12f_6x2_64.png"),FramesPerSecond=24,Size=.75f},
            CommonHealthCostEntry()
        };
        [MenuItem("PokeChess/Visuals/Register Submission")]
        public static void RegisterSubmission()
        {
            var catalog=CreateInitialCatalog();var entries=catalog.Entries.ToList();
            foreach(var entry in SubmissionEntries()) {
                if(!entry.SpriteAfterimage&&entry.Frames.Length==0)throw new InvalidOperationException("Missing frames: "+entry.Name);
                if(!entries.Any(e=>e.Name==entry.Name))entries.Add(entry);
            }
            catalog.Entries=entries.ToArray();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
        }
        private static SkillVisualEntry[] HypnosisAndAmnesiaEntries()=>new[]{
            new SkillVisualEntry{Name="Hypnosis",StatusIds=ConstrictStatuses("poliwag","haunter"),Trigger=SkillVisualTrigger.StatusApplied,
                Frames=Frames("hypnosis/hypnosis_ring_12f_6x2_64.png"),FramesPerSecond=18,FollowUnit=true,Opacity=.9f},
            new SkillVisualEntry{Name="Amnesia",StatusIds=ConstrictStatuses("poliwhirl","slowpoke"),Trigger=SkillVisualTrigger.StatusActive,
                IntroFrames=Frames("amnesia/amnesia_cast_6x2_64.png"),Frames=Frames("amnesia/amnesia_buff_loop_4x2_64.png"),
                OutroFrames=Frames("amnesia/amnesia_fade_3x2_64.png"),FramesPerSecond=18,Playback=SkillVisualPlayback.Loop,
                FollowUnit=true,Offset=new Vector2(0,42),Opacity=.9f}
        };
        [MenuItem("PokeChess/Visuals/Register Hypnosis Amnesia")]
        public static void RegisterHypnosisAndAmnesia()
        {
            var catalog=CreateInitialCatalog();var entries=catalog.Entries.ToList();
            foreach(var entry in HypnosisAndAmnesiaEntries()) {
                if(entry.Frames.Length==0)throw new InvalidOperationException("Missing frames: "+entry.Name);
                if(!entries.Any(e=>e.Name==entry.Name))entries.Add(entry);
            }
            catalog.Entries=entries.ToArray();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
        }
        private static SkillVisualEntry[] SkullBashEntries()=>new[]{
            new SkillVisualEntry{Name="SkullBashDefense",StatusIds=ConstrictStatuses("wartortle"),Trigger=SkillVisualTrigger.StatusActive,
                Frames=Frames("skull-bash/rocket_defense_clean_12f_6x2_64.png"),FramesPerSecond=12,Playback=SkillVisualPlayback.Once,
                FollowUnit=true,Offset=new Vector2(0,42),Opacity=.9f},
            new SkillVisualEntry{Name="SkullBashAfterimage",SkillIds=Skills("wartortle"),Trigger=SkillVisualTrigger.MovementArrived,
                MovementKind=PokeChess.Core.Battle.SkillMovementKind.DashToTarget,Placement=SkillVisualPlacement.Caster,
                SpriteAfterimage=true,DashThroughTarget=true,LungeSeconds=.16f,TrailCopies=4,Opacity=.4f,FadeSeconds=.28f}
        };
        [MenuItem("PokeChess/Visuals/Register Skull Bash")]
        public static void RegisterSkullBash()
        {
            var catalog=CreateInitialCatalog();var entries=catalog.Entries.ToList();
            foreach(var entry in SkullBashEntries()) {
                if(!entry.SpriteAfterimage&&entry.Frames.Length==0)throw new InvalidOperationException("Missing frames: "+entry.Name);
                if(!entries.Any(e=>e.Name==entry.Name))entries.Add(entry);
            }
            catalog.Entries=entries.ToArray();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
        }
        private static SkillVisualEntry[] SolarFireAndWaterEntries()
        {
            var flame=Frames("flamethrower/flamethrower_tiles_12f_6x2_64.png");
            var entries=new List<SkillVisualEntry>{
                new SkillVisualEntry{Name="SolarBeamCharge",SkillIds=Skills("venusaur"),Trigger=SkillVisualTrigger.CastStarted,Placement=SkillVisualPlacement.Caster,
                    Frames=Frames("solar-beam/solar_charge_12f_6x2_64.png"),FramesPerSecond=12,FollowUnit=true},
                new SkillVisualEntry{Name="EmberProjectile",SkillIds=Skills("charmander","growlithe"),Trigger=SkillVisualTrigger.BeforeEffect,Placement=SkillVisualPlacement.CasterToTarget,
                    Frames=Frames("ember/ember_projectile_6x2_64.png"),FramesPerSecond=18},
                new SkillVisualEntry{Name="EmberImpact",SkillIds=Skills("charmander","growlithe"),Trigger=SkillVisualTrigger.Damage,
                    Frames=Frames("ember/ember_impact_6x2_64.png"),FramesPerSecond=24},
                new SkillVisualEntry{Name="Flamethrower",SkillIds=Skills("charizard"),Trigger=SkillVisualTrigger.Area,Placement=SkillVisualPlacement.AreaCells,
                    IntroFrames=flame.Take(6).ToArray(),Frames=Frames("flamethrower/flamethrower_loop_6f_3x2_64.png"),OutroFrames=flame.Skip(6).ToArray(),
                    Playback=SkillVisualPlayback.Loop,DurationSeconds=1f,FramesPerSecond=18,AlignToDirection=true,Size=1f,Opacity=.65f,Offset=new Vector2(0,14)},
                new SkillVisualEntry{Name="WaterGunProjectile",SkillIds=Skills("squirtle"),Trigger=SkillVisualTrigger.BeforeEffect,Placement=SkillVisualPlacement.CasterToTarget,
                    Frames=Frames("water-gun/water_gun_projectile_12f_6x2_64.png"),FramesPerSecond=18},
                new SkillVisualEntry{Name="WaterGunImpact",SkillIds=Skills("squirtle"),Trigger=SkillVisualTrigger.Damage,
                    Frames=Frames("water-gun/water_gun_impact_12f_6x2_64.png"),FramesPerSecond=24}
            };
            for(int n=1;n<=4;n++) {
                var frames=Frames("solar-beam/solar_beam_tile_"+n+"_16f_4x4_64.png");
                if(frames.Length==0)throw new InvalidOperationException("Missing Solar Beam frames");
                var first=frames[0];int leading=(3-Mathf.RoundToInt(first.rect.y/64))*4+Mathf.RoundToInt(first.rect.x/64);
                entries.Add(new SkillVisualEntry{Name="SolarBeamTile"+n,SkillIds=Skills("venusaur"),Trigger=SkillVisualTrigger.Area,Placement=SkillVisualPlacement.AreaCells,
                    AreaCellIndex=n-1,Frames=frames,FramesPerSecond=24,DelaySeconds=leading/24f,AlignToDirection=true,Size=1f,Offset=new Vector2(0,24)});
            }
            return entries.ToArray();
        }
        [MenuItem("PokeChess/Visuals/Register Solar Beam Ember Flamethrower Water Gun")]
        public static void RegisterSolarFireAndWater()
        {
            var catalog=CreateInitialCatalog();var entries=catalog.Entries.ToList();
            foreach(var entry in SolarFireAndWaterEntries()) {
                if(entry.Frames.Length==0)throw new InvalidOperationException("Missing frames: "+entry.Name);
                if(!entries.Any(e=>e.Name==entry.Name))entries.Add(entry);
            }
            catalog.Entries=entries.ToArray();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
        }
        private static SkillVisualEntry[] VineAndLeafEntries()=>new[]{
            new SkillVisualEntry{Name="VineWhipImpact",SkillIds=Skills("bulbasaur"),Trigger=SkillVisualTrigger.Damage,
                Frames=Frames("vine-whip/vine_whip_impact_swing_sheet_6x2_64.png"),FramesPerSecond=24,AlignToDirection=true,TowardCasterCells=.3f},
            new SkillVisualEntry{Name="VineWhipBinding",StatusIds=ConstrictStatuses("bulbasaur"),Trigger=SkillVisualTrigger.StatusActive,
                IntroFrames=Frames("vine-whip/vine_bind_start_spritesheet_6x2_64.png"),Frames=Frames("vine-whip/vine_bind_loop_spritesheet_6x2_64.png"),
                FramesPerSecond=24,Playback=SkillVisualPlayback.Loop,FollowUnit=true,Offset=new Vector2(0,12),Opacity=.8f},
            new SkillVisualEntry{Name="RazorLeafForm",SkillIds=Skills("ivysaur"),Trigger=SkillVisualTrigger.CastStarted,Placement=SkillVisualPlacement.Caster,
                Frames=Frames("razor-leaf/leaf_blade_centered_16frames_4x4_64.png"),FramesPerSecond=40},
            new SkillVisualEntry{Name="RazorLeafFlight",SkillIds=Skills("ivysaur"),Trigger=SkillVisualTrigger.Area,Placement=SkillVisualPlacement.AreaPath,
                Frames=Frames("razor-leaf/leaf_blade_flight_loop_8frames_4x2_64.png"),FramesPerSecond=24,Playback=SkillVisualPlayback.Loop,
                DurationSeconds=.35f,AlignToDirection=true}
        };
        [MenuItem("PokeChess/Visuals/Register Vine Whip Razor Leaf")]
        public static void RegisterVineAndLeaf()
        {
            var catalog=CreateInitialCatalog();var entries=catalog.Entries.ToList();
            foreach(var entry in VineAndLeafEntries()) {
                if(entry.Frames.Length==0)throw new InvalidOperationException("Missing frames: "+entry.Name);
                if(!entries.Any(e=>e.Name==entry.Name))entries.Add(entry);
            }
            catalog.Entries=entries.ToArray();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
        }
        private static SkillVisualEntry[] PreparedShellAndSmokeEntries()=>new[]{
            new SkillVisualEntry{Name="ClampBinding",StatusIds=ConstrictStatuses("shellder"),Trigger=SkillVisualTrigger.StatusActive,
                Frames=Frames("clamp/shell_bind_start_6x2_64.png"),FramesPerSecond=24,Playback=SkillVisualPlayback.HoldUntilEnd,HoldFrame=11,FollowUnit=true,
                ExclusiveGroup="Clamp",Opacity=.8f,Offset=new Vector2(0,12)},
            new SkillVisualEntry{Name="ClampDamage",StatusIds=ConstrictStatuses("shellder"),Trigger=SkillVisualTrigger.StatusPulse,
                Frames=Frames("clamp/shell_damage_loop_6x2_64.png"),FramesPerSecond=24,FollowUnit=true,ExclusiveGroup="Clamp",VisualPriority=1,Offset=new Vector2(0,12)},
            new SkillVisualEntry{Name="SpikeCannonFlight",SkillIds=Skills("cloyster"),Trigger=SkillVisualTrigger.ProjectileFlight,
                Frames=Frames("spike-cannon/spike_cannon_projectile_6x2_64.png"),Playback=SkillVisualPlayback.Loop,FramesPerSecond=24,AlignToDirection=true},
            new SkillVisualEntry{Name="SpikeCannonImpact",SkillIds=Skills("cloyster"),Trigger=SkillVisualTrigger.ProjectileHit,
                Frames=Frames("spike-cannon/spike_cannon_impact_6x2_64.png"),FramesPerSecond=24},
            new SkillVisualEntry{Name="BoneClub",SkillIds=Skills("cubone"),Trigger=SkillVisualTrigger.Damage,
                Frames=Frames("bone-club/bone_swing_spritesheet_6x2_64.png"),FramesPerSecond=24,AlignToDirection=true,Size=1f},
            new SkillVisualEntry{Name="Bonemerang",SkillIds=Skills("marowak"),Trigger=SkillVisualTrigger.PathProjectileFlight,
                Frames=Frames("bonemerang/Bonemerang.png"),FramesPerSecond=24,Playback=SkillVisualPlayback.Loop,AlignToDirection=true},
            new SkillVisualEntry{Name="SmogBurst",SkillIds=Skills("koffing"),Trigger=SkillVisualTrigger.Area,Placement=SkillVisualPlacement.AreaCells,
                Frames=Frames("smog/smog_spritesheet_6x2_64.png"),FramesPerSecond=24,Size=1f,Offset=new Vector2(0,14),Opacity=.5f},
            new SkillVisualEntry{Name="SmogSustain",StatusIds=ConstrictStatuses("koffing"),Trigger=SkillVisualTrigger.StatusActive,
                Frames=Frames("smog/smog_sustain_4x2_64.png"),FramesPerSecond=12,Playback=SkillVisualPlayback.Loop,DelaySeconds=.5f,FollowUnit=true,Opacity=.45f},
            new SkillVisualEntry{Name="SludgeProjectile",SkillIds=Skills("weezing"),Trigger=SkillVisualTrigger.BeforeEffect,Placement=SkillVisualPlacement.CasterToTarget,
                Frames=Frames("sludge/sludge_projectile_sheet_6x2_64.png"),FramesPerSecond=18},
            new SkillVisualEntry{Name="SludgeImpact",SkillIds=Skills("weezing"),Trigger=SkillVisualTrigger.Damage,
                Frames=Frames("sludge/sludge_impact_sheet_6x2_64.png"),FramesPerSecond=24},
            new SkillVisualEntry{Name="SludgeSpread",SkillIds=Skills("weezing"),Trigger=SkillVisualTrigger.Area,Placement=SkillVisualPlacement.AreaCells,
                Frames=Frames("sludge/poison_spread_sheet_6x2_64.png"),FramesPerSecond=24,Size=1f,Offset=new Vector2(0,14),Opacity=.55f},
            new SkillVisualEntry{Name="SludgePoison",StatusIds=ConstrictStatuses("weezing"),Trigger=SkillVisualTrigger.StatusActive,
                Frames=Frames("sludge/poison_loop_sheet_4x2_64.png"),FramesPerSecond=12,Playback=SkillVisualPlayback.Loop,DelaySeconds=.5f,FollowUnit=true,Opacity=.5f},
            new SkillVisualEntry{Name="SmokeScreen",StatusIds=ConstrictStatuses("horsea"),Trigger=SkillVisualTrigger.GroundActive,
                Frames=Frames("smoke-screen/smokescreen_loop_6x2_64.png"),IntroFrames=Frames("smoke-screen/smokescreen_cast_6x2_64.png"),
                OutroFrames=Frames("smoke-screen/smokescreen_fade_6x2_64.png"),FramesPerSecond=24,Playback=SkillVisualPlayback.Loop,
                Size=1f,Offset=new Vector2(0,14),Opacity=.45f}
        };
        [MenuItem("PokeChess/Visuals/Register Clamp Spike Cannon Bone Club Bonemerang Smog Sludge Smoke Screen")]
        public static void RegisterShellAndSmoke()
        {
            var catalog=CreateInitialCatalog();var entries=catalog.Entries.ToList();
            foreach(var entry in PreparedShellAndSmokeEntries()) {
                if(entry.Frames.Length==0)throw new InvalidOperationException("Missing frames: "+entry.Name);
                if(!entries.Any(e=>e.Name==entry.Name))entries.Add(entry);
            }
            catalog.Entries=entries.ToArray();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
        }
        private static SkillVisualEntry[] TriAttackEntries()
        {
            var frames=Frames("tri-attack/tri_attack_all_4x3_64.png");
            if(frames.Length!=12)throw new InvalidOperationException("Tri Attack requires 12 grid frames.");
            var entries=new List<SkillVisualEntry>();
            for(int n=0;n<3;n++) {
                entries.Add(new SkillVisualEntry{Name="TriAttackFlight"+(n+1),SkillIds=Skills("dodrio","porygon"),Trigger=SkillVisualTrigger.ProjectileFlight,
                    StepIndex=n,Frames=frames.Skip(n*4).Take(2).ToArray(),Playback=SkillVisualPlayback.Loop,FramesPerSecond=12,AlignToDirection=true});
                entries.Add(new SkillVisualEntry{Name="TriAttackImpact"+(n+1),SkillIds=Skills("dodrio","porygon"),Trigger=SkillVisualTrigger.ProjectileHit,
                    StepIndex=n,Frames=frames.Skip(n*4+2).Take(2).ToArray(),FramesPerSecond=12});
            }
            return entries.ToArray();
        }
        [MenuItem("PokeChess/Visuals/Register Tri Attack")]
        public static void RegisterTriAttack()
        {
            var catalog=CreateInitialCatalog();var entries=catalog.Entries.ToList();
            foreach(var entry in TriAttackEntries())if(!entries.Any(e=>e.Name==entry.Name))entries.Add(entry);
            catalog.Entries=entries.ToArray();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
        }
        private static SkillVisualEntry DrillPeckAfterimageEntry()=>new SkillVisualEntry {
            Name="DrillPeckAfterimage",SkillIds=Skills("doduo"),Trigger=SkillVisualTrigger.MovementArrived,
            MovementKind=PokeChess.Core.Battle.SkillMovementKind.DashToTarget,Placement=SkillVisualPlacement.Caster,
            SpriteAfterimage=true,DashThroughTarget=true,LungeSeconds=.16f,TrailCopies=4,Opacity=.4f,FadeSeconds=.28f
        };
        [MenuItem("PokeChess/Visuals/Register Drill Peck Afterimage")]
        public static void RegisterDrillPeckAfterimage()
        {
            var catalog=CreateInitialCatalog();if(catalog.Entries.Any(e=>e.Name=="DrillPeckAfterimage"))return;
            catalog.Entries=catalog.Entries.Concat(new[]{DrillPeckAfterimageEntry()}).ToArray();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
        }
        private static SkillVisualEntry[] PreparedStompAndSonicEntries()
        {
            var entries=new List<SkillVisualEntry>{
                new SkillVisualEntry{Name="Stomp",SkillIds=Skills("ponyta"),Trigger=SkillVisualTrigger.Damage,
                    Frames=Frames("stomp/stomp_5x2_64x64_unity.png"),FramesPerSecond=20},
                new SkillVisualEntry{Name="FireSpin",SkillIds=Skills("rapidash"),Trigger=SkillVisualTrigger.Area,StepIndex=1,
                    Placement=SkillVisualPlacement.AreaCells,Frames=Frames("fire-spin/fire-spin.png"),FramesPerSecond=12,
                    Playback=SkillVisualPlayback.Loop,DurationSeconds=3,Size=1f,Offset=new Vector2(0,14),Opacity=.65f},
                new SkillVisualEntry{Name="ThunderWave",StatusIds=ConstrictStatuses("magnemite"),Trigger=SkillVisualTrigger.StatusApplied,
                    Frames=Frames("thunder-wave/thunder_wave_spritesheet_6x2_64.png"),FramesPerSecond=18,Opacity=.85f}
            };
            for(int n=1;n<=3;n++) {
                var frames=Frames("sonic-boom/sonic_boom_tile_"+n+"_4x4_64.png");
                if(frames.Length==0)throw new InvalidOperationException("Missing Sonic Boom frames");
                var first=frames[0];int leading=(3-Mathf.RoundToInt(first.rect.y/64))*4+Mathf.RoundToInt(first.rect.x/64);
                entries.Add(new SkillVisualEntry{Name="SonicBoom"+n,SkillIds=Skills("magneton"),Trigger=SkillVisualTrigger.Area,
                    Placement=SkillVisualPlacement.AreaCells,AreaCellIndex=n-1,AlignToDirection=true,Frames=frames,
                    FramesPerSecond=16,DelaySeconds=leading/16f,Size=1f,Offset=new Vector2(0,14)});
            }
            return entries.ToArray();
        }
        [MenuItem("PokeChess/Visuals/Register Stomp Fire Spin Thunder Wave Sonic Boom")]
        public static void RegisterStompAndSonic()
        {
            var catalog=CreateInitialCatalog();var entries=catalog.Entries.ToList();
            foreach(var entry in PreparedStompAndSonicEntries()) {
                if(entry.Frames.Length==0)throw new InvalidOperationException("Missing frames: "+entry.Name);
                if(!entries.Any(e=>e.Name==entry.Name))entries.Add(entry);
            }
            catalog.Entries=entries.ToArray();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
        }
        private static SkillVisualEntry[] PreparedFiveEntries()
        {
            var entries=new List<SkillVisualEntry>{
                new SkillVisualEntry{Name="Sing",SkillIds=Skills("jigglypuff"),Trigger=SkillVisualTrigger.Area,Placement=SkillVisualPlacement.AreaCells,
                    Frames=Frames("sing/sing.png"),FramesPerSecond=18,Size=1f,Offset=new Vector2(0,14),Opacity=.65f},
                new SkillVisualEntry{Name="Absorb",SkillIds=Skills("oddish","kabuto"),Trigger=SkillVisualTrigger.Damage,
                    Frames=Frames("absorb/absorb_spritesheet_6x2_64.png"),FramesPerSecond=18},
                new SkillVisualEntry{Name="AbsorbHeal",SkillIds=Skills("oddish","kabuto"),Trigger=SkillVisualTrigger.Damage,Placement=SkillVisualPlacement.Caster,
                    Frames=Frames("absorb/heal_spritesheet_6x2_64.png"),FramesPerSecond=18,FollowUnit=true},
                new SkillVisualEntry{Name="PetalDance",SkillIds=Skills("vileplume"),Trigger=SkillVisualTrigger.Area,Placement=SkillVisualPlacement.AreaCells,
                    Frames=Frames("petal-dance/petal_dance_spritesheet_6x2_64.png"),FramesPerSecond=12,Size=1f,Offset=new Vector2(0,14),Opacity=.7f},
                new SkillVisualEntry{Name="PayDayCoin",SkillIds=Skills("meowth"),Trigger=SkillVisualTrigger.BeforeEffect,Placement=SkillVisualPlacement.CasterToTarget,
                    Frames=Frames("pay-day/coin_projectile_centered_4x2_64.png"),FramesPerSecond=16},
                new SkillVisualEntry{Name="PayDayGold",SkillIds=new[]{"bounty"},Trigger=SkillVisualTrigger.GoldAwarded,Placement=SkillVisualPlacement.Caster,
                    Frames=Frames("pay-day/gold_gain_centered_4x2_64.png"),FramesPerSecond=16,Offset=new Vector2(0,42),FollowUnit=true}
            };
            var slapIds=Skills("wigglytuff").Concat(Enumerable.Range(1,3).SelectMany(r=>Enumerable.Range(3,3).Select(n=>"skill-wigglytuff-r"+r+"-hits"+n))).ToArray();
            var slap=Frames("double-slap/double_slap_5hits_spritesheet_5x5_64.png");
            if(slap.Length!=25)throw new InvalidOperationException("Double Slap requires 25 grid frames.");
            for(int n=0;n<5;n++)entries.Add(new SkillVisualEntry{Name="DoubleSlap"+(n+1),SkillIds=slapIds,Trigger=SkillVisualTrigger.Damage,
                StepIndex=n,Frames=slap.Skip(n*5).Take(5).ToArray(),FramesPerSecond=25});
            return entries.ToArray();
        }
        [MenuItem("PokeChess/Visuals/Register Sing Double Slap Absorb Petal Dance Pay Day")]
        public static void RegisterPreparedFive()
        {
            var catalog=CreateInitialCatalog();var entries=catalog.Entries.ToList();
            foreach(var entry in PreparedFiveEntries()) {
                if(entry.Frames.Length==0)throw new InvalidOperationException("Missing frames: "+entry.Name);
                if(!entries.Any(e=>e.Name==entry.Name))entries.Add(entry);
            }
            catalog.Entries=entries.ToArray();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
        }
        private static SkillVisualEntry MinimizeEntry()=>new SkillVisualEntry {
            Name="Minimize",StatusIds=ConstrictStatuses("clefable"),Trigger=SkillVisualTrigger.StatusActive,
            ScaleUnitSprite=true,SpriteScaleSteps=new[]{1f,.8f,.6f,.4f},SpriteScaleStepSeconds=.1f
        };
        [MenuItem("PokeChess/Visuals/Register Minimize")]
        public static void RegisterMinimize()
        {
            var catalog=CreateInitialCatalog();if(catalog.Entries.Any(e=>e.Name=="Minimize"))return;
            catalog.Entries=catalog.Entries.Concat(new[]{MinimizeEntry()}).ToArray();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
        }
        private static SkillVisualEntry ThunderboltEntry()=>new SkillVisualEntry {
            Name="Thunderbolt",SkillIds=Skills("raichu"),Trigger=SkillVisualTrigger.Damage,
            Lightning=true,LightningFromPrimary=true,LightningSeconds=.4f,LightningWidth=.04f,
            LightningGlow=new Color(1,.45f,.015f,.8f),LightningCore=new Color(1,.78f,.12f,1),Offset=new Vector2(0,24)
        };
        [MenuItem("PokeChess/Visuals/Register Thunderbolt")]
        public static void RegisterThunderbolt()
        {
            var catalog=CreateInitialCatalog();if(catalog.Entries.Any(e=>e.Name=="Thunderbolt"))return;
            catalog.Entries=catalog.Entries.Concat(new[]{ThunderboltEntry()}).ToArray();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
        }
        private static SkillVisualEntry ThunderShockEntry()=>new SkillVisualEntry {
            Name="ThunderShock",SkillIds=Skills("pikachu"),Trigger=SkillVisualTrigger.CastEffect,
            Lightning=true,LightningSeconds=.35f,LightningWidth=.035f,Offset=new Vector2(0,24)
        };
        [MenuItem("PokeChess/Visuals/Register Thunder Shock")]
        public static void RegisterThunderShock()
        {
            var catalog=CreateInitialCatalog();if(catalog.Entries.Any(e=>e.Name=="ThunderShock"))return;
            catalog.Entries=catalog.Entries.Concat(new[]{ThunderShockEntry()}).ToArray();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
        }
        private static SkillVisualEntry[] BindAndExplosionEntries()=>new[]{
            new SkillVisualEntry{Name="Bind",StatusIds=ConstrictStatuses("onix"),Trigger=SkillVisualTrigger.StatusActive,
                Frames=Frames("bind/Bind.png"),FramesPerSecond=16,Playback=SkillVisualPlayback.HoldUntilEnd,HoldFrame=7,FollowUnit=true,Size=.75f,Opacity=.8f},
            new SkillVisualEntry{Name="SelfDestructCore",SkillIds=Skills("voltorb","graveler"),Trigger=SkillVisualTrigger.CastEffect,
                Placement=SkillVisualPlacement.Caster,Frames=Frames("self-destruct/self_explosion_sheet_8x64.png"),
                FramesPerSecond=16,Size=1.25f,Offset=new Vector2(0,5)},
            new SkillVisualEntry{Name="SelfDestructOuter",SkillIds=Skills("voltorb","graveler"),Trigger=SkillVisualTrigger.Area,
                Placement=SkillVisualPlacement.AreaCells,ExcludeAreaCenter=true,Frames=Frames("self-destruct/area_explosion_sheet_8x64.png"),
                FramesPerSecond=16,Size=1f,Offset=new Vector2(0,5),Opacity=.8f},
            new SkillVisualEntry{Name="ExplosionCore",SkillIds=Skills("electrode","golem"),Trigger=SkillVisualTrigger.CastEffect,
                Placement=SkillVisualPlacement.Caster,Frames=Frames("explosion/big_explosion_core_spritesheet_6x2_64.png"),
                FramesPerSecond=18,Size=1.5f,Offset=new Vector2(0,5)},
            new SkillVisualEntry{Name="ExplosionOuter",SkillIds=Skills("electrode","golem"),Trigger=SkillVisualTrigger.Area,
                Placement=SkillVisualPlacement.AreaCells,ExcludeAreaCenter=true,Frames=Frames("explosion/big_explosion_outer_spritesheet_6x2_64.png"),
                FramesPerSecond=18,Size=1f,Offset=new Vector2(0,5),Opacity=.8f}
        };
        [MenuItem("PokeChess/Visuals/Register Bind Self Destruct Explosion")]
        public static void RegisterBindAndExplosions()
        {
            var catalog=CreateInitialCatalog();var entries=catalog.Entries.ToList();
            foreach(var entry in BindAndExplosionEntries()) {
                if(entry.Frames.Length==0)throw new InvalidOperationException("Missing frames: "+entry.Name);
                if(!entries.Any(e=>e.Name==entry.Name))entries.Add(entry);
            }
            catalog.Entries=entries.ToArray();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
        }
        private static SkillVisualEntry[] SwordsDanceEntries()
        {
            var statuses=ConstrictStatuses("farfetchd","scyther");
            var names=new[]{"top","upper_right","lower_right","bottom","lower_left","upper_left"};
            var entries=new List<SkillVisualEntry>{new SkillVisualEntry{Name="SwordsDanceBuff",StatusIds=statuses,Trigger=SkillVisualTrigger.StatusActive,
                Frames=Frames("swords-dance/swords-dance.png"),FramesPerSecond=12,Playback=SkillVisualPlayback.Loop,FollowUnit=true,Offset=new Vector2(0,42),Opacity=.85f}};
            for(int n=0;n<6;n++)entries.Add(new SkillVisualEntry{Name="SwordsDanceRing"+(n+1),StatusIds=statuses,Trigger=SkillVisualTrigger.StatusProc,
                Placement=SkillVisualPlacement.CasterRing,RingIndex=n,Frames=Frames("swords-dance/blade_dance_tile_"+(n+1)+"_"+names[n]+"_8x64.png"),
                FramesPerSecond=16,Size=1f,Offset=new Vector2(0,14)});
            return entries.ToArray();
        }
        [MenuItem("PokeChess/Visuals/Register Swords Dance")]
        public static void RegisterSwordsDance()
        {
            var catalog=CreateInitialCatalog();var entries=catalog.Entries.ToList();
            foreach(var entry in SwordsDanceEntries()) {
                if(entry.Frames.Length==0)throw new InvalidOperationException("Missing frames: "+entry.Name);
                if(!entries.Any(e=>e.Name==entry.Name))entries.Add(entry);
            }
            catalog.Entries=entries.ToArray();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
        }
        private static string[] ConstrictStatuses(params string[] species)=>species.SelectMany(id=>Enumerable.Range(1,3).Select(r=>"status-"+id+"-r"+r+"-s0")).ToArray();
        private static SkillVisualEntry[] ConstrictEntries()=>new[]{
            new SkillVisualEntry{Name="ConstrictBinding",StatusIds=ConstrictStatuses("tentacruel","tangela"),Trigger=SkillVisualTrigger.StatusActive,
                Frames=Frames("constrict/constrict.png"),FramesPerSecond=16,Playback=SkillVisualPlayback.HoldUntilEnd,HoldFrame=7,
                FollowUnit=true,Opacity=.75f,ExclusiveGroup="Constrict"},
            new SkillVisualEntry{Name="ConstrictPeriodicDamage",StatusIds=ConstrictStatuses("tangela"),Trigger=SkillVisualTrigger.StatusPulse,
                Frames=Frames("constrict/wrap_damage_spritesheet_4x3_64.png"),FramesPerSecond=24,Opacity=.9f,
                ExclusiveGroup="Constrict",VisualPriority=1,FollowUnit=true}
        };
        [MenuItem("PokeChess/Visuals/Register Constrict")]
        public static void RegisterConstrict()
        {
            var catalog=CreateInitialCatalog();var entries=catalog.Entries.ToList();
            foreach(var entry in ConstrictEntries()) {
                if(entry.Frames.Length==0)throw new InvalidOperationException("Missing frames: "+entry.Name);
                if(!entries.Any(e=>e.Name==entry.Name))entries.Add(entry);
            }
            catalog.Entries=entries.ToArray();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
        }
        private static SkillVisualEntry SeismicTossEntry()=>new SkillVisualEntry {
            Name="SeismicTossLanding",SkillIds=Skills("primeape"),Trigger=SkillVisualTrigger.Area,
            Placement=SkillVisualPlacement.AreaCells,AreaCenterScale=1.5f,
            Frames=Frames("seismic-toss/seismic-toss.png"),FramesPerSecond=16,Size=1f,Offset=new Vector2(0,5)
        };
        [MenuItem("PokeChess/Visuals/Register Seismic Toss")]
        public static void RegisterSeismicToss()
        {
            var catalog=CreateInitialCatalog();var entry=SeismicTossEntry();
            if(entry.Frames.Length==0)throw new InvalidOperationException("Missing frames: "+entry.Name);
            if(catalog.Entries.Any(e=>e.Name==entry.Name))return;
            catalog.Entries=catalog.Entries.Concat(new[]{entry}).ToArray();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
        }
        private static SkillVisualEntry[] KarateChopEntries()=>new[]{
            new SkillVisualEntry{Name="KarateChopHit",SkillIds=Skills("mankey","machop"),Trigger=SkillVisualTrigger.Damage,
                Frames=Frames("karate-chop/karate-chop.png"),FramesPerSecond=16},
            new SkillVisualEntry{Name="KarateChopCriticalReady",Trigger=SkillVisualTrigger.StatusActive,
                StatusIds=new[]{"mankey","machop"}.SelectMany(id=>Enumerable.Range(1,3).Select(r=>"status-"+id+"-r"+r+"-s0")).ToArray(),
                Frames=Frames("karate-chop/karate-chop_buff.png"),FramesPerSecond=12,
                Playback=SkillVisualPlayback.Loop,FollowUnit=true,Size=.5f,Offset=new Vector2(0,42),Opacity=.85f}
        };
        [MenuItem("PokeChess/Visuals/Register Karate Chop")]
        public static void RegisterKarateChop()
        {
            var catalog=CreateInitialCatalog();var entries=catalog.Entries.ToList();
            foreach(var entry in KarateChopEntries()) {
                if(entry.Frames.Length==0)throw new InvalidOperationException("Missing frames: "+entry.Name);
                if(!entries.Any(e=>e.Name==entry.Name))entries.Add(entry);
            }
            catalog.Entries=entries.ToArray();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
        }
        private static SkillVisualEntry[] HydroPumpEntries()=>Enumerable.Range(1,4).Select(n=>new SkillVisualEntry {
            Name="HydroPump"+n,SkillIds=Skills("golduck","seadra","blastoise"),Trigger=SkillVisualTrigger.Area,
            Placement=SkillVisualPlacement.AreaCells,AreaCellIndex=n-1,AlignToDirection=true,
            Frames=Frames("hydro-pump/hydro_pump_tile_"+n+"_sheet_8x64.png"),FramesPerSecond=12,
            Size=1f,Offset=new Vector2(0,5),Opacity=.8f,DelaySeconds=n==4?1/12f:0
        }).ToArray();
        [MenuItem("PokeChess/Visuals/Register Hydro Pump")]
        public static void RegisterHydroPump()
        {
            var catalog=CreateInitialCatalog();var entries=catalog.Entries.ToList();
            foreach(var entry in HydroPumpEntries()) {
                if(entry.Frames.Length==0)throw new InvalidOperationException("Missing frames: "+entry.Name);
                if(!entries.Any(e=>e.Name==entry.Name))entries.Add(entry);
            }
            catalog.Entries=entries.ToArray();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
        }
        private static SkillVisualEntry[] PreparedStatusAndAttackEntries()
        {
            string[] acidStatuses=new[]{"arbok","gloom"}.SelectMany(id=>Enumerable.Range(1,3).Select(rank=>"status-"+id+"-r"+rank+"-s0")).ToArray();
            return new[]{
                new SkillVisualEntry{Name="Glare",SkillIds=Skills("ekans"),Trigger=SkillVisualTrigger.CastEffect,
                    Frames=Frames("glare/snake_glare_spritesheet_4x4_64.png"),FramesPerSecond=16,Opacity=.8f},
                new SkillVisualEntry{Name="AcidProjectile",SkillIds=Skills("arbok","gloom"),Trigger=SkillVisualTrigger.BeforeEffect,
                    Placement=SkillVisualPlacement.CasterToTarget,Frames=Frames("acid/projectile_spritesheet_8x64.png")},
                new SkillVisualEntry{Name="AcidImpact",SkillIds=Skills("arbok","gloom"),Trigger=SkillVisualTrigger.Damage,
                    Frames=Frames("acid/impact_spritesheet_8x64.png"),FramesPerSecond=16},
                new SkillVisualEntry{Name="AcidDefenseBreak",StatusIds=acidStatuses,Trigger=SkillVisualTrigger.StatusApplied,
                    Frames=Frames("acid/defense_break_spritesheet_8x64.png"),FramesPerSecond=16,Offset=new Vector2(0,42)},
                new SkillVisualEntry{Name="AcidDefenseDown",StatusIds=acidStatuses,Trigger=SkillVisualTrigger.StatusActive,
                    Frames=Frames("acid/defense_down_loop_spritesheet_4x64.png"),FramesPerSecond=8,Offset=new Vector2(0,42),
                    Playback=SkillVisualPlayback.Loop,FollowUnit=true,DelaySeconds=.5f,Opacity=.65f},
                new SkillVisualEntry{Name="SandAttack",SkillIds=Skills("sandshrew"),Trigger=SkillVisualTrigger.CastEffect,
                    Frames=Frames("sand-attack/sand_spray_sheet_6x2_64.png"),FramesPerSecond=18,AlignToDirection=true,Opacity=.8f},
                new SkillVisualEntry{Name="Slash",SkillIds=Skills("sandslash","parasect","persian","charmeleon","kabutops"),
                    Trigger=SkillVisualTrigger.Damage,Frames=Frames("slash/slash.png"),FramesPerSecond=24}
            };
        }
        [MenuItem("PokeChess/Visuals/Register Glare Acid Sand Attack Slash")]
        public static void RegisterPreparedSkills()
        {
            var catalog=CreateInitialCatalog();var entries=catalog.Entries.ToList();
            foreach(var entry in PreparedStatusAndAttackEntries())
            {
                if(entry.Frames.Length==0)throw new InvalidOperationException("Missing frames: "+entry.Name);
                if(!entries.Any(e=>e.Name==entry.Name))entries.Add(entry);
            }
            catalog.Entries=entries.ToArray();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
        }
        private static SkillVisualEntry[] PreparedSporeAndGroundEntries()=>new[]{
            new SkillVisualEntry{Name="Spore",SkillIds=Skills("paras"),Trigger=SkillVisualTrigger.CastEffect,
                Frames=Frames("spore/spore.png"),FramesPerSecond=18,Opacity=.65f},
            new SkillVisualEntry{Name="DigIn",SkillIds=Skills("diglett"),Trigger=SkillVisualTrigger.CastEffect,StepIndex=0,
                Placement=SkillVisualPlacement.Caster,Frames=Frames("dig/dig_in.png"),FramesPerSecond=16,Offset=new Vector2(0,6)},
            new SkillVisualEntry{Name="DigHidden",StatusIds=Enumerable.Range(1,3).Select(rank=>"status-diglett-r"+rank+"-s0").ToArray(),
                Trigger=SkillVisualTrigger.StatusActive,Placement=SkillVisualPlacement.Target,HideUnitSprite=true,DelaySeconds=.2f},
            new SkillVisualEntry{Name="DigOut",SkillIds=Skills("diglett"),Trigger=SkillVisualTrigger.MovementArrived,
                MovementKind=PokeChess.Core.Battle.SkillMovementKind.BurrowToTarget,Placement=SkillVisualPlacement.Caster,
                Frames=Frames("dig/dig_out.png"),FramesPerSecond=16,Offset=new Vector2(0,6)},
            new SkillVisualEntry{Name="Earthquake",SkillIds=Skills("dugtrio"),Trigger=SkillVisualTrigger.Area,
                Placement=SkillVisualPlacement.AreaCells,Frames=Frames("earthquake/earthquake_spritesheet_4x3_64.png"),
                FramesPerSecond=18,Size=1f,Offset=new Vector2(0,5),Opacity=.7f,CapOverlappingOpacity=true},
            new SkillVisualEntry{Name="ConfusionHit",SkillIds=Skills("psyduck"),Trigger=SkillVisualTrigger.Damage,
                Frames=Frames("confusion/confusion.png"),FramesPerSecond=18,Opacity=.8f}
        };
        [MenuItem("PokeChess/Visuals/Register Spore Dig Earthquake Confusion")]
        public static void RegisterSporeAndGroundSkills()
        {
            var catalog=CreateInitialCatalog();var entries=catalog.Entries.ToList();
            foreach(var entry in PreparedSporeAndGroundEntries())
            {
                if(!entry.HideUnitSprite&&entry.Frames.Length==0)throw new InvalidOperationException("Missing frames: "+entry.Name);
                if(!entries.Any(e=>e.Name==entry.Name))entries.Add(entry);
            }
            catalog.Entries=entries.ToArray();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
        }
    }
}
