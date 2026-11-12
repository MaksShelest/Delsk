using Simulation.Core;

namespace Simulation.Hosting;

public readonly record struct PedestrianPose(float X, float Z, float FaceX, float FaceZ);

/// <summary>Чистая поза по авторитетному отрезку тика. Не двигает мир и не выходит за известные клетки.</summary>
public static class PedestrianPoseMath
{
    public static PedestrianPose At(PedestrianMotion motion, float phase)
    {
        if(phase<0)phase=0;if(phase>1)phase=1;
        float start=motion.StartMilli;
        float end=motion.Crosses?1000f+motion.EndMilli:motion.EndMilli;
        if(end<start)end=start;
        float dist=start+(end-start)*phase;
        float x,z,faceX,faceZ;
        if(!motion.Crosses||dist<=1000f)
        {
            float milli=Math.Min(dist,1000f);
            (x,z)=Along(motion.Ax,motion.Az,motion.Bx,motion.Bz,milli);
            faceX=motion.Bx-motion.Ax;faceZ=motion.Bz-motion.Az;
        }
        else
        {
            (x,z)=Along(motion.Bx,motion.Bz,motion.Cx,motion.Cz,dist-1000f);
            faceX=motion.Cx-motion.Bx;faceZ=motion.Cz-motion.Bz;
        }
        if(motion.Corner&&dist>780f&&dist<1220f)
        {
            float u=(dist-780f)/440f;
            var p0=Along(motion.Ax,motion.Az,motion.Bx,motion.Bz,780f);
            var p1=Along(motion.Bx,motion.Bz,motion.Cx,motion.Cz,220f);
            float v=1f-u;
            float cx=v*v*p0.X+2f*v*u*motion.Bx+u*u*p1.X;
            float cz=v*v*p0.Z+2f*v*u*motion.Bz+u*u*p1.Z;
            if(Inside(cx,cz,motion))
            {
                x=cx;z=cz;
                faceX=v*(motion.Bx-p0.X)+u*(p1.X-motion.Bx);
                faceZ=v*(motion.Bz-p0.Z)+u*(p1.Z-motion.Bz);
            }
        }
        if(Math.Abs(faceX)+Math.Abs(faceZ)<0.001f){faceX=motion.Bx-motion.Ax;faceZ=motion.Bz-motion.Az;}
        return new(x,z,faceX,faceZ);
    }

    private static (float X,float Z) Along(int ax,int az,int bx,int bz,float milli)
    {
        float t=milli/1000f;
        return (ax+(bx-ax)*t,az+(bz-az)*t);
    }

    private static bool Inside(float x,float z,PedestrianMotion motion)
        => Cell(x,z,motion.Ax,motion.Az)||Cell(x,z,motion.Bx,motion.Bz)||motion.Corner&&Cell(x,z,motion.Cx,motion.Cz);

    private static bool Cell(float x,float z,int cx,int cz)
        => x>=cx-0.501f&&x<=cx+0.501f&&z>=cz-0.501f&&z<=cz+0.501f;
}

public readonly record struct LandParcel(int X, int Z, long Owner, string State);
public readonly record struct LandBorder(float X1, float Z1, float X2, float Z2, long Owner, string State);

/// <summary>Внешний контур смежных клеток одного владельца и состояния. Внутренние рёбра не рисуются.</summary>
public static class LandOutline
{
    public static LandBorder[] Borders(IReadOnlyList<LandParcel> cells, bool ownershipLayer)
    {
        var shown=cells.Where(c=>ownershipLayer?c.State is "plot-own" or "plot-foreign" or "plot-reserved" or "plot-free" or "shop-plot":c.State=="plot-own").ToArray();
        var keys=shown.ToDictionary(c=>(c.X,c.Z),c=>c.State+":"+c.Owner);
        var edges=new List<LandBorder>();
        foreach(var cell in shown.OrderBy(c=>c.Z).ThenBy(c=>c.X))
        {
            string key=cell.State+":"+cell.Owner;
            void Add(int nx,int nz,float x1,float z1,float x2,float z2)
            {
                if(keys.TryGetValue((nx,nz),out var other)&&other==key)return;
                float inset=cell.State=="plot-own"?0f:0.04f;
                float mx=(x1+x2)/2f,mz=(z1+z2)/2f,pull=cell.State=="plot-own"?0f:inset;
                float ox=(cell.X-mx)*pull*2f,oz=(cell.Z-mz)*pull*2f;
                if(cell.State=="plot-reserved")
                {
                    edges.Add(Dash(x1,z1,x2,z2,ox,oz,.12f,.42f,cell));
                    edges.Add(Dash(x1,z1,x2,z2,ox,oz,.58f,.88f,cell));
                    return;
                }
                edges.Add(new(x1+ox,z1+oz,x2+ox,z2+oz,cell.Owner,cell.State));
            }
            Add(cell.X,cell.Z-1,cell.X-.5f,cell.Z-.5f,cell.X+.5f,cell.Z-.5f);
            Add(cell.X+1,cell.Z,cell.X+.5f,cell.Z-.5f,cell.X+.5f,cell.Z+.5f);
            Add(cell.X,cell.Z+1,cell.X+.5f,cell.Z+.5f,cell.X-.5f,cell.Z+.5f);
            Add(cell.X-1,cell.Z,cell.X-.5f,cell.Z+.5f,cell.X-.5f,cell.Z-.5f);
        }
        return edges.ToArray();
    }

    private static LandBorder Dash(float x1,float z1,float x2,float z2,float ox,float oz,float from,float to,LandParcel cell)
        => new(x1+(x2-x1)*from+ox,z1+(z2-z1)*from+oz,x1+(x2-x1)*to+ox,z1+(z2-z1)*to+oz,cell.Owner,cell.State);
}

public static class RoadCardText
{
    public static string Header(string language)
        => PlayerText.Local(language,"Вклады по ступеням и будущие права на доход:","Contributions by stage and future revenue rights:");

    public static string Line(string language,string stage,string owner,RoadContribution contribution)
    {
        string share=contribution.Builder.Value==0
            ? PlayerText.Local(language,"городу 100%","city 100%")
            : PlayerText.Local(language,"исполнителю 90%, городу 10%","builder 90%, city 10%");
        return stage+" · "+owner+" · t"+contribution.Tick.ToString(System.Globalization.CultureInfo.InvariantCulture)
            +PlayerText.Local(language," · денежный вклад "," · cash contribution ")
            +PlayerText.Money(contribution.PriceMinor)+" · "+share;
    }
}
