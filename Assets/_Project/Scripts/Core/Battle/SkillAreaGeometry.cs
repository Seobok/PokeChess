using System;
using System.Collections.Generic;
using System.Linq;
using PokeChess.Core.Board;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Battle
{
    public sealed class SkillAreaGeometry
    {
        public int EffectIndex { get; }
        public SkillAreaShape Shape { get; }
        public BoardPosition Origin { get; }
        public BoardPosition Center { get; }
        public BoardPosition Aim { get; }
        public IReadOnlyList<BoardPosition> Cells { get; }
        public SkillAreaGeometry(int index,SkillAreaShape shape,BoardPosition origin,BoardPosition center,BoardPosition aim,int extent)
        {EffectIndex=index;Shape=shape;Origin=origin;Center=center;Aim=aim;
         Cells=Array.AsReadOnly(SkillGeometry.Cells(shape,center,shape==SkillAreaShape.Cross?origin:aim,extent).ToArray());}
    }
    public readonly struct SkillAreaEvent
    {
        public string CasterId { get; }
        public long CastId { get; }
        public int StepIndex { get; }
        public int Iteration { get; }
        public SkillAreaGeometry Area { get; }
        internal SkillAreaEvent(string caster,long castId,int step,int iteration,SkillAreaGeometry area)
        {CasterId=caster;CastId=castId;StepIndex=step;Iteration=iteration;Area=area;}
    }
}
