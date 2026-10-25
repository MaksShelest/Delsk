using Godot;
using Simulation.Hosting;

public partial class Playfield
{
    private EconomyProfile ReadEconomy(string? mapKey=null)
    {
        var args = OS.GetCmdlineUserArgs(); int option = System.Array.IndexOf(args, "--economy");
        if (option >= 0)
        {
            if (option + 1 >= args.Length) throw new System.ArgumentException("--economy requires a JSON path");
            return EconomyProfile.Load(args[option + 1]);
        }
        if(PlayMaps.IsDesigned(mapKey??_mapKey))return EconomyProfile.Read(Text("Play.vnext-supported.json"));
        string project = ProjectSettings.GlobalizePath("res://");
        string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(project, "../../config/economy.json"));
        return System.IO.File.Exists(System.IO.Path.Combine(project,"project.godot")) && System.IO.File.Exists(path)
            ? EconomyProfile.Load(path) : EconomyProfile.Read(Text("Play.economy.json"));
    }
}
