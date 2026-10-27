using System;
using System.Collections.Generic;
using System.Linq;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Board
{
    public enum SkillAreaShape { Radius, Line, Cone, Cross, All, Wave }
    public static class SkillGeometry
    {
        // Round displacement, rather than absolute coordinates, so inversion uses identical ties.
        private static AxialPosition Rounded(int q,int r,int denominator)
        {
            int s=-q-r;
            int x=(int)Math.Round((double)q/denominator,MidpointRounding.AwayFromZero);
            int y=(int)Math.Round((double)r/denominator,MidpointRounding.AwayFromZero);
            int z=(int)Math.Round((double)s/denominator,MidpointRounding.AwayFromZero);
            long ex=Math.Abs((long)x*denominator-q),ey=Math.Abs((long)y*denominator-r),ez=Math.Abs((long)z*denominator-s);
            if(ex>=ey&&ex>=ez)x=-y-z;else if(ey>=ez)y=-x-z;
            return new AxialPosition(x,y);
        }
        public static IReadOnlyList<BoardPosition> Ray(BoardPosition origin,BoardPosition aim,int length)
        {
            var a=HexCoordinates.ToAxial(origin);var b=HexCoordinates.ToAxial(aim);
            return RayCore(origin,new AxialPosition(b.Q-a.Q,b.R-a.R),length,true);
        }
        public static IReadOnlyList<BoardPosition> Ray(BoardPosition origin,AxialPosition direction,int length)
            => RayCore(origin,direction,length,false);
        private static IReadOnlyList<BoardPosition> RayCore(BoardPosition origin,AxialPosition direction,int length,bool connectAim)
        {
            if(length<0||length>56)throw new ArgumentOutOfRangeException(nameof(length));
            var a=HexCoordinates.ToAxial(origin);int distance=HexCoordinates.Distance(new AxialPosition(0,0),direction);
            var result=new List<BoardPosition>();if(distance==0)return result;
            for(int i=1;i<=length;i++) {
                var d=Rounded(direction.Q*i,direction.R*i,distance);
                bool inside=HexCoordinates.TryToOffset(new AxialPosition(a.Q+d.Q,a.R+d.R),out var p)&&HexBoardBounds.Combat.Contains(p);
                var previous=result.Count==0?origin:result[result.Count-1];
                if(connectAim&&i<=distance&&(!inside||HexCoordinates.Distance(previous,p)!=1)) {
                    var target=new AxialPosition(a.Q+direction.Q,a.R+direction.R);
                    int sign=Math.Sign(direction.Q!=0?direction.Q:direction.R);
                    var alternative=HexBoardBounds.Combat.Neighbors(previous)
                        .Where(c=>HexCoordinates.Distance(origin,c)==i&&HexCoordinates.Distance(HexCoordinates.ToAxial(c),target)==distance-i)
                        .OrderBy(c=>RayError(a,HexCoordinates.ToAxial(c),direction,i,distance))
                        .ThenBy(c=>(HexCoordinates.ToAxial(c).Q-a.Q)*sign)
                        .ThenBy(c=>(HexCoordinates.ToAxial(c).R-a.R)*sign).Select(c=>(BoardPosition?)c).FirstOrDefault();
                    if(!alternative.HasValue)break;p=alternative.Value;inside=true;
                }
                if(!inside)break;
                if(!result.Contains(p))result.Add(p);
            }
            return result;
        }
        private static long RayError(AxialPosition origin,AxialPosition point,AxialPosition direction,int index,int distance)
        {
            long q=(long)(point.Q-origin.Q)*distance-(long)direction.Q*index;
            long r=(long)(point.R-origin.R)*distance-(long)direction.R*index;
            return q*q+r*r+(q+r)*(q+r);
        }
        private static IReadOnlyList<BoardPosition> Wave(BoardPosition origin,BoardPosition aim) {
            var a=HexCoordinates.ToAxial(origin);var b=HexCoordinates.ToAxial(aim);
            var direction=new AxialPosition(b.Q-a.Q,b.R-a.R);int turn=direction.R>0||direction.R==0&&direction.Q<0?3:0;
            Func<AxialPosition,AxialPosition> rotate=d=>new AxialPosition(-d.R,d.Q+d.R);
            for(int i=0;i<turn;i++)direction=rotate(direction);
            if(direction.Q==0&&direction.R==0)return Array.Empty<BoardPosition>();
            for(int i=0;i<6&&!(direction.R<0&&Math.Abs(2L*direction.Q+direction.R)<=-direction.R);i++){direction=rotate(direction);turn++;}
            long side=2L*direction.Q+direction.R;var offsets=new List<AxialPosition>();
            if(side==0) {var c=new AxialPosition(1,-2);offsets.Add(c);
                foreach(var d in new[]{new AxialPosition(1,0),new AxialPosition(0,1),new AxialPosition(-1,1),new AxialPosition(-1,0),new AxialPosition(0,-1),new AxialPosition(1,-1)})offsets.Add(new AxialPosition(c.Q+d.Q,c.R+d.R));}
            else for(int r=-3;r<=-1;r++)for(int i=-1;i<=1;i++)offsets.Add(new AxialPosition(side<0?i:-r+i,r));
            var result=new List<BoardPosition>();foreach(var offset in offsets){var d=offset;for(int i=0;i<(6-turn%6)%6;i++)d=rotate(d);
                if(HexCoordinates.TryToOffset(new AxialPosition(a.Q+d.Q,a.R+d.R),out var p)&&HexBoardBounds.Combat.Contains(p))result.Add(p);}
            return result;
        }
        public static IReadOnlyList<BoardPosition> Cells(SkillAreaShape shape,BoardPosition origin,BoardPosition aim,int extent)
        {
            if(!Enum.IsDefined(typeof(SkillAreaShape),shape)||extent<0||extent>56)throw new ArgumentOutOfRangeException();
            if(shape==SkillAreaShape.Line)return Ray(origin,aim,extent);
            if(shape==SkillAreaShape.Wave)return Wave(origin,aim);
            var result=new List<BoardPosition>();var a=HexCoordinates.ToAxial(origin);var b=HexCoordinates.ToAxial(aim);
            long dx=2L*(b.Q-a.Q)+b.R-a.R,dy=b.R-a.R,norm=dx*dx+3*dy*dy;
            int axis=0;long best=-1;
            var directions=new[]{new AxialPosition(1,0),new AxialPosition(0,1),new AxialPosition(-1,1)};
            for(int i=0;i<3;i++){var d=directions[i];long dot=Math.Abs(dx*(2*d.Q+d.R)+3*dy*d.R);if(dot>best){best=dot;axis=i;}}
            foreach(var p in HexBoardBounds.Combat.Cells()) {
                int distance=HexCoordinates.Distance(origin,p);var c=HexCoordinates.ToAxial(p);
                int q=c.Q-a.Q,r=c.R-a.R;bool include=false;
                switch(shape) {
                    case SkillAreaShape.All:include=true;break;
                    case SkillAreaShape.Radius:include=distance<=extent;break;
                    case SkillAreaShape.Cross:include=distance<=extent&&(distance==0||(axis!=0&&r==0)||(axis!=1&&q==0)||(axis!=2&&q+r==0));break;
                    case SkillAreaShape.Cone:
                        long x=2L*q+r,y=r,dot=x*dx+3*y*dy;
                        include=distance>0&&distance<=extent&&norm>0&&dot>=0&&4*dot*dot>=norm*(x*x+3*y*y);break;
                }
                if(include)result.Add(p);
            }
            return result;
        }
    }
}
