using Godot;

public static partial class WorldModels
{
    public static Node3D Vehicle(string model, bool cozy = false)
    {
        bool tracked = model == "trakt_d1";
        return WorldProportions.Fit(WorldArt.Vehicle(model, cozy), tracked ? new(.76f, .55f, .86f) : new(.38f, .46f, .76f));
    }
}
