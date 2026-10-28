namespace Simulation.Hosting;
public readonly record struct ScreenInsets(double Left,double Top,double Right,double Bottom)
{
    public static ScreenInsets Calculate(double viewportWidth,double viewportHeight,double windowX,double windowY,double windowWidth,double windowHeight,double safeX,double safeY,double safeWidth,double safeHeight)
    {
        if(viewportWidth<=0||viewportHeight<=0||windowWidth<=0||windowHeight<=0||safeWidth<=0||safeHeight<=0)return default;
        double left=Math.Max(windowX,safeX),top=Math.Max(windowY,safeY),right=Math.Min(windowX+windowWidth,safeX+safeWidth),bottom=Math.Min(windowY+windowHeight,safeY+safeHeight);
        if(right<=left||bottom<=top)return default;
        return new((left-windowX)*viewportWidth/windowWidth,(top-windowY)*viewportHeight/windowHeight,(windowX+windowWidth-right)*viewportWidth/windowWidth,(windowY+windowHeight-bottom)*viewportHeight/windowHeight);
    }
}
