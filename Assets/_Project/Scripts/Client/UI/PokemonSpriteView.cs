using System;
using System.Linq;
using UnityEngine;
using PokeChess.Client.Data;
using PokeChess.Core.Battle;
using PokeChess.Core.Pokemon;

namespace PokeChess.Client.UI
{
    // Presentation only: never triggers damage, healing, or simulation state changes.
    public sealed class PokemonSpriteView : MonoBehaviour
    {
        private UnityEngine.UI.Image image;
        private StatusSpriteView statusView;
        private PokemonAnimationLibrary library;
        private Material hardenMaterial;
        private Material originalMaterial;
        private string pokemon;
        private string animationPokemon;
        private int direction;
        private CombatActionState previousAction;
        private long idleStart, hurtStart=-1000,deathStart=-1;
        private float previousHP=-1;
        private bool skillHidden;
        private float drawnAlpha=1;
        private float skillScale=1;
        public Vector2 SkillLungeOffset {get;private set;}
        private const float PixelScale=1.5f;
        private const float UnitSizeMultiplier=1.5f;
        public string PokemonId=>pokemon;
        public string UnitInstanceId {get;private set;}
        public UnityEngine.UI.Image CharacterImage=>image;
        public bool Visible=>image!=null&&image.enabled&&image.color.a>0;
        public void Initialize(string id,string instanceId=null)
        {
            UnitInstanceId=instanceId;
            SetSkillLungeOffset(Vector2.zero);SetSkillScale(1);skillHidden=false;drawnAlpha=1;
            if(instanceId!=null&&instanceId.StartsWith("wild-",StringComparison.Ordinal))
            {
                var parts=instanceId.Split('-');
                if(parts.Length>1&&int.TryParse(parts[1],out int round))
                    id=round==1||round==2?"magikarp":round==3?"rattata":id;
            }
            pokemon=id;library=PokemonAnimationLibrary.Load();
            animationPokemon=library?.Find(id,"Idle")!=null ? id : PokeChess.Content.GameContent.BaseForm(id);
            image=GetComponent<UnityEngine.UI.Image>()??gameObject.AddComponent<UnityEngine.UI.Image>();
            SetHardened(false);
            image.raycastTarget=false;image.color=Color.white;
            statusView=GetComponent<StatusSpriteView>()??gameObject.AddComponent<StatusSpriteView>();statusView.Clear();
            direction=0;previousHP=-1;deathStart=-1;hurtStart=-1000;
        }
        public bool ShowIdle(double seconds)
        {statusView?.Clear();SetSkillLungeOffset(Vector2.zero);SetSkillHidden(false);SetSkillScale(1);SetHardened(false);return Draw("Idle",seconds,true,1);}
        public void ShowCombat(BattleState battle,UnitCombatState unit,UnitActionRuntime action,Vector2 facing,float pixelScale=PixelScale)
        {
            UnitInstanceId=unit.UnitInstanceId;
            if(facing.sqrMagnitude>.001f)
                direction=((int)Math.Round(Math.Atan2(facing.x,-facing.y)/(Math.PI/4))+8)%8;
            if(previousHP>=0 && unit.CurrentHP<previousHP && unit.IsAlive)hurtStart=battle.CurrentTick;
            previousHP=unit.CurrentHP;
            double elapsed=(battle.CurrentTick-action.StartTick)/(double)battle.TickRate;
            string name=unit.ActionState==CombatActionState.Moving?"Walk":unit.ActionState==CombatActionState.Attacking?
                AttackAnimation():unit.ActionState==CombatActionState.Casting?
                SkillAnimation():"Idle";
            if(unit.ActionState!=previousAction){idleStart=battle.CurrentTick;previousAction=unit.ActionState;}
            double time=elapsed;
            var anim=library==null?null:library.Find(animationPokemon,name);
            if(anim!=null && (unit.ActionState==CombatActionState.Attacking||unit.ActionState==CombatActionState.Casting))
            {
                var cast=unit.ActionState==CombatActionState.Casting?action.SkillCast:null;
                long start=cast?.StartTick??action.StartTick;
                long effect=cast?.EffectTick??action.EffectTick;
                long end=cast?.EndTick??action.EndTick;
                time=anim.ActionTime((battle.CurrentTick-start)/(double)battle.TickRate,
                    (effect-start)/(double)battle.TickRate,(end-start)/(double)battle.TickRate);
            }
            bool loop=name=="Idle"||name=="Walk";
            if(name=="Idle")time=(battle.CurrentTick-idleStart)/(double)battle.TickRate;
            float alpha=1;
            if(!unit.IsAlive)
            {
                if(deathStart<0)deathStart=battle.CurrentTick;
                name="Hurt";time=(battle.CurrentTick-deathStart)/(double)battle.TickRate;loop=false;
                alpha=Mathf.Clamp01(1-(float)time/.45f);
            }
            // Hurt never replaces an attack/cast: the synchronized effect pose keeps priority.
            else if(name=="Idle" && battle.CurrentTick-hurtStart<battle.TickRate/6d)
            {name="Hurt";time=(battle.CurrentTick-hurtStart)/(double)battle.TickRate;loop=false;}
            SetHardened(unit.IsAlive && unit.ActionState!=CombatActionState.Casting &&
                battle.StatusEffects.Active.Any(s=>s.TargetId==unit.UnitInstanceId &&
                    s.StartTick<=battle.CurrentTick && s.ExpireTick>battle.CurrentTick && IsHarden(s.Definition.Id)));
            DrawScaled(name,time,loop,alpha,pixelScale);
            statusView.Render(battle.StatusEffects.Active.Where(s=>s.TargetId==unit.UnitInstanceId).Select(s=>s.Definition.Behavior),
                battle.CurrentTick/(double)battle.TickRate,image,unit.IsAlive&&image.enabled,pixelScale*UnitSizeMultiplier/2.25f,battle.StatusEffects.Active.Where(s=>s.TargetId==unit.UnitInstanceId&&s.StartTick<=battle.CurrentTick&&s.ExpireTick>battle.CurrentTick).Select(s=>s.Definition.CrowdControl));
        }
        private bool Draw(string name,double seconds,bool loop,float alpha)
            =>DrawScaled(name,seconds,loop,alpha,PixelScale);
        public void ShowPlayback(PokeChess.Core.Match.CombatUnitView unit,double tick,int tickRate,Vector2 facing,float pixelScale=1f)
        {
            UnitInstanceId=unit.id;
            if(facing.sqrMagnitude>.001f)direction=((int)Math.Round(Math.Atan2(facing.x,-facing.y)/(Math.PI/4))+8)%8;
            if(previousHP>=0&&unit.hp<previousHP&&unit.hp>0)hurtStart=(long)tick;previousHP=unit.hp;
            string name=unit.action==CombatActionState.Moving?"Walk":unit.action==CombatActionState.Attacking?
                AttackAnimation():
                unit.action==CombatActionState.Casting?SkillAnimation():"Idle";
            if(previousAction!=unit.action){idleStart=(long)tick;previousAction=unit.action;}
            double seconds=(tick-unit.startTick)/tickRate;var animation=library?.Find(animationPokemon,name);
            if(animation!=null&&(unit.action==CombatActionState.Attacking||unit.action==CombatActionState.Casting))seconds=animation.ActionTime(seconds,(unit.effectTick-unit.startTick)/(double)tickRate,(unit.endTick-unit.startTick)/(double)tickRate);
            if(name=="Idle")seconds=(tick-idleStart)/tickRate;
            float alpha=1;if(unit.hp<=0){if(deathStart<0)deathStart=(long)tick;name="Hurt";seconds=(tick-deathStart)/tickRate;alpha=Mathf.Clamp01(1-(float)seconds/.45f);}
            else if(name=="Idle"&&tick-hurtStart<tickRate/6d){name="Hurt";seconds=(tick-hurtStart)/tickRate;}
            bool hardened=unit.statusDetails!=null
                ?unit.statusDetails.Any(s=>s.startTick<=tick && s.endTick>tick && IsHarden(s.definition))
                :(unit.statuses??Array.Empty<string>()).Any(IsHarden);
            SetHardened(unit.hp>0 && unit.onBoard && unit.action!=CombatActionState.Casting && hardened);
            DrawScaled(name,Math.Max(0,seconds),name=="Idle"||name=="Walk",alpha,pixelScale);
            var behaviors=unit.statusDetails!=null
                ?unit.statusDetails.Where(s=>s.endTick>tick).Select(s=>(StatusBehavior)s.behavior)
                :(unit.statuses??Array.Empty<string>()).Select(id=>PokeChess.Content.GameContent.Roster.Statuses.FirstOrDefault(s=>s.Id==id)?.Behavior??StatusBehavior.None);
            var crowdControls=unit.statusDetails!=null?unit.statusDetails.Where(s=>s.startTick<=tick&&s.endTick>tick).Select(s=>(CrowdControlKind)s.crowdControl):(unit.statuses??Array.Empty<string>()).Select(id=>PokeChess.Content.GameContent.Roster.Statuses.FirstOrDefault(s=>s.Id==id)?.CrowdControl??CrowdControlKind.None); statusView.Render(behaviors,tick/tickRate,image,unit.hp>0&&unit.onBoard&&image.enabled,pixelScale*UnitSizeMultiplier/2.25f,crowdControls);
        }
        private bool DrawScaled(string name,double seconds,bool loop,float alpha,float scale)
        {
            scale*=UnitSizeMultiplier*skillScale;
            var anim=library==null?null:library.Find(animationPokemon,name);
            if(anim==null){image.enabled=false;return false;}
            int index=(anim.Directions==1?0:direction)*anim.FrameCount+anim.FrameAt(seconds,loop);
            image.enabled=true;image.sprite=anim.Frames[index];
            var tint=PokeChess.Content.GameContent.FindPokemon(pokemon)?.Visual?.Tint ?? Color.white;
            drawnAlpha=alpha;
            image.color=new Color(tint.r,tint.g,tint.b,skillHidden?0:alpha);
            image.rectTransform.sizeDelta=anim.Frames[index].rect.size*scale;
            image.rectTransform.anchoredPosition=anim.GroundOffsets[index]*scale+SkillLungeOffset;
            return true;
        }
        private string AttackAnimation()
            =>PokeChess.Content.GameContent.FindPokemon(pokemon)?.Visual?.Attack ?? "Attack";
        private string SkillAnimation()
            =>PokeChess.Content.GameContent.FindPokemon(pokemon)?.Visual?.Skill ?? "Charge";
        public void SetSkillLungeOffset(Vector2 value)
        {
            if(image!=null)image.rectTransform.anchoredPosition+=value-SkillLungeOffset;
            SkillLungeOffset=value;
        }
        public void SetSkillScale(float value)
        {
            value=Mathf.Max(.01f,value);if(Mathf.Approximately(skillScale,value))return;
            if(image!=null){float ratio=value/skillScale;image.rectTransform.sizeDelta*=ratio;image.rectTransform.anchoredPosition=(image.rectTransform.anchoredPosition-SkillLungeOffset)*ratio+SkillLungeOffset;}
            skillScale=value;
        }
        public void SetSkillHidden(bool hidden)
        {
            skillHidden=hidden;
            if(image!=null){var color=image.color;color.a=hidden?0:drawnAlpha;image.color=color;}
        }
        // Match the buff, rather than the species: Metronome can also grant Harden.
        private static bool IsHarden(string id)
            =>id!=null && (id.StartsWith("status-metapod-r",StringComparison.Ordinal) ||
                id.StartsWith("status-kakuna-r",StringComparison.Ordinal)) && id.EndsWith("-s0",StringComparison.Ordinal);
        private void SetHardened(bool active)
        {
            if(image==null)return;
            if(active && hardenMaterial==null)
            {
                var shader=Resources.Load<Shader>("HardenSprite");
                if(shader==null)return;
                originalMaterial=image.material;
                hardenMaterial=new Material(shader){hideFlags=HideFlags.HideAndDontSave};
            }
            if(hardenMaterial!=null)image.material=active?hardenMaterial:originalMaterial;
        }
        private void OnDestroy()
        {
            if(hardenMaterial!=null)
            {
                if(Application.isPlaying)Destroy(hardenMaterial);
                else DestroyImmediate(hardenMaterial);
            }
        }
    }
}
