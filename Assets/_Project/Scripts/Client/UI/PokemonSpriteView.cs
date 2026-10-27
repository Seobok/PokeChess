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
        private string pokemon;
        private string animationPokemon;
        private int direction;
        private CombatActionState previousAction;
        private long idleStart, hurtStart=-1000,deathStart=-1;
        private float previousHP=-1;
        private const float PixelScale=1.5f;
        private const float UnitSizeMultiplier=1.5f;
        public string PokemonId=>pokemon;
        public bool Visible=>image!=null&&image.enabled&&image.color.a>0;
        public void Initialize(string id,string instanceId=null)
        {
            if(instanceId!=null&&instanceId.StartsWith("wild-",StringComparison.Ordinal))
            {
                var parts=instanceId.Split('-');
                if(parts.Length>1&&int.TryParse(parts[1],out int round))
                    id=round==1||round==2?"magikarp":round==3?"rattata":id;
            }
            pokemon=id;library=PokemonAnimationLibrary.Load();
            animationPokemon=library?.Find(id,"Idle")!=null ? id : PokeChess.Content.GameContent.BaseForm(id);
            image=GetComponent<UnityEngine.UI.Image>()??gameObject.AddComponent<UnityEngine.UI.Image>();
            image.raycastTarget=false;image.color=Color.white;
            statusView=GetComponent<StatusSpriteView>()??gameObject.AddComponent<StatusSpriteView>();statusView.Clear();
            direction=0;previousHP=-1;deathStart=-1;hurtStart=-1000;
        }
        public bool ShowIdle(double seconds)
        {statusView?.Clear();return Draw("Idle",seconds,true,1);}
        public void ShowCombat(BattleState battle,UnitCombatState unit,UnitActionRuntime action,Vector2 facing)
        {
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
            Draw(name,time,loop,alpha);
            statusView.Render(battle.StatusEffects.Active.Where(s=>s.TargetId==unit.UnitInstanceId).Select(s=>s.Definition.Behavior),
                battle.CurrentTick/(double)battle.TickRate,image,unit.IsAlive&&image.enabled,PixelScale*UnitSizeMultiplier/2.25f);
        }
        private bool Draw(string name,double seconds,bool loop,float alpha)
            =>DrawScaled(name,seconds,loop,alpha,PixelScale);
        public void ShowPlayback(PokeChess.Core.Match.CombatUnitView unit,double tick,int tickRate,Vector2 facing,float pixelScale=1f)
        {
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
            DrawScaled(name,Math.Max(0,seconds),name=="Idle"||name=="Walk",alpha,pixelScale);
            var behaviors=unit.statusDetails!=null
                ?unit.statusDetails.Where(s=>s.endTick>tick).Select(s=>(StatusBehavior)s.behavior)
                :(unit.statuses??Array.Empty<string>()).Select(id=>PokeChess.Content.GameContent.Roster.Statuses.FirstOrDefault(s=>s.Id==id)?.Behavior??StatusBehavior.None);
            statusView.Render(behaviors,tick/tickRate,image,unit.hp>0&&unit.onBoard&&image.enabled,pixelScale*UnitSizeMultiplier/2.25f);
        }
        private bool DrawScaled(string name,double seconds,bool loop,float alpha,float scale)
        {
            scale*=UnitSizeMultiplier;
            var anim=library==null?null:library.Find(animationPokemon,name);
            if(anim==null){image.enabled=false;return false;}
            int index=(anim.Directions==1?0:direction)*anim.FrameCount+anim.FrameAt(seconds,loop);
            image.enabled=true;image.sprite=anim.Frames[index];
            var tint=PokeChess.Content.GameContent.FindPokemon(pokemon)?.Visual?.Tint ?? Color.white;
            image.color=new Color(tint.r,tint.g,tint.b,alpha);
            image.rectTransform.sizeDelta=anim.Frames[index].rect.size*scale;
            image.rectTransform.anchoredPosition=anim.GroundOffsets[index]*scale;
            return true;
        }
        private string AttackAnimation()
            =>PokeChess.Content.GameContent.FindPokemon(pokemon)?.Visual?.Attack ?? "Attack";
        private string SkillAnimation()
            =>PokeChess.Content.GameContent.FindPokemon(pokemon)?.Visual?.Skill ?? "Charge";
    }
}
