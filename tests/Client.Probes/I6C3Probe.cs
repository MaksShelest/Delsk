using Godot;
using Simulation.Core;
using Simulation.Hosting;
using System.Reflection;
using System.Text.Json;

/// <summary>Изолированный smoke художественного мира I6-C3. В обычный клиент не входит.</summary>
public partial class I6C3Probe : Control
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private string _output = "";
    private string _shots = "";
    private readonly List<string> _checks = [];
    private static T Field<T>(object o, string key) => (T)o.GetType().GetField(key, Flags)!.GetValue(o)!;
    private static void Set(object o, string key, object value) => o.GetType().GetField(key, Flags)!.SetValue(o, value);
    private static void Call(object o, string method, params object[] args) => o.GetType().GetMethod(method, Flags)!.Invoke(o, args);
    private void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); _checks.Add(text); GD.Print("I6C3 " + text); }
    private async Task Frames(int n = 3) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private async Task Shot(string name)
    {
        await Frames(2);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Check(GetViewport().GetTexture().GetImage().SavePng(_shots + "/" + name + ".png") == Error.Ok, "capture " + name);
    }
    private static IEnumerable<Node> Walk(Node node)
    {
        yield return node;
        foreach (var child in node.GetChildren()) foreach (var found in Walk(child)) yield return found;
    }
    private static int Triangles(Node node) => Walk(node).OfType<MeshInstance3D>().Sum(mesh => mesh.Mesh is null ? 0 : mesh.Mesh.GetFaces().Length / 3);
    private static int Baked(string key)
    {
        string path = "res://skins/cozy/" + key + ".res";
        return ResourceLoader.Exists(path) ? GD.Load<Mesh>(path).GetFaces().Length / 3 : -1;
    }
    private static string? SkinId(Node node)
    {
        if (node.HasMeta("skin_id")) return node.GetMeta("skin_id").AsString();
        foreach (var child in node.GetChildren()) { var found = SkinId(child); if (found is not null) return found; }
        return null;
    }
    private static bool HasName(Node node, string name) => node.Name == name || node.GetChildren().Any(child => HasName(child, name));
    private static Aabb Bounds(Node3D node)
    {
        var boxes = Walk(node).OfType<MeshInstance3D>().Select(mesh => node.GlobalTransform.AffineInverse() * mesh.GlobalTransform * mesh.GetAabb()).ToArray();
        return boxes.Aggregate((a, b) => a.Merge(b));
    }
    private void Frame(PresentationStage stage, Camera3D camera, Vector3 focus, float distance)
    {
        Set(stage, "_follow", false);
        Set(stage, "_cameraDistance", distance);
        Set(stage, "_cameraTarget", distance);
        camera.Position = focus + new Vector3(0, distance, distance * 0.7f);
        camera.LookAt(focus, Vector3.Up);
    }
    private static Node3D ResourceAt(PresentationStage stage, string item, int x, int z) => stage.GetChildren().OfType<Node3D>().First(node =>
        node.HasMeta("resource_item") && node.GetMeta("resource_item").AsString() == item && Mathf.IsEqualApprox(node.Position.X, x) && Mathf.IsEqualApprox(node.Position.Z, z));
    private void AimEditor(MapEditor editor, int x, int z, float distance)
    {
        Set(editor, "_focus", new Vector3(x, 0, z));
        Set(editor, "_distance", distance);
        Set(editor, "_distanceTarget", distance);
        Set(editor, "_yaw", 0f);
        Set(editor, "_pitch", Mathf.DegToRad(48));
        Set(editor, "_distanceGlide", false);
        Call(editor, "ApplyCamera");
    }

    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            _output = args[Array.IndexOf(args, "--output") + 1];
            string root = args[Array.IndexOf(args, "--repository") + 1];
            _shots = root + "/docs/verification/i6c3";
            System.IO.Directory.CreateDirectory(_output);
            System.IO.Directory.CreateDirectory(_shots);
            GetWindow().Size = new(1100, 720);
            GetWindow().ContentScaleSize = GetWindow().Size;
            var app = GD.Load<PackedScene>("res://Playfield.tscn").Instantiate<Playfield>();
            AddChild(app);
            await Frames(2);
            app.SetProcess(false);
            Call(app, "DismissMaps");
            Set(app, "_mapKey", PlayMaps.CompactValley);
            Set(app, "_newNpcCount", 3);
            Call(app, "Start", "economy", false);
            GameSession? session = null;
            for (int i = 0; i < 90 && session is null; i++) { await Frames(1); session = app.GetType().GetField("_session", Flags)!.GetValue(app) as GameSession; }
            Check(session is not null, "ordinary compact valley session started");
            session!.SetPaused(true);
            app.SetProcess(false);
            string hash = session.Engine.ComputeStateHash();
            var scenario = session.Engine.Configuration();
            Check(scenario.Layout?.SourceId == PlayMaps.CompactValleyScenarioId, "the live map is the current small valley");
            for (int i = 0; i < 20 && Field<PresentationStage?>(app, "_stage") is null; i++) await Frames(1);
            var stage = Field<PresentationStage>(app, "_stage")!;
            Call(app, "Refresh");
            await Frames(4);
            int built = stage.StaticBuildCount;
            Call(app, "Refresh");
            await Frames(2);
            Check(stage.StaticBuildCount == built, "a second refresh does not rebuild the static world");
            Check(session.Engine.ComputeStateHash() == hash, "presentation does not change the world");
            var camera = Field<Camera3D>(stage, "_camera");
            var layout = scenario.Layout!;
            Cell Of(string item) => layout.ResourceDistricts.Where(d => d.Item == item).SelectMany(d => d.Cells).First();
            var wood = Of("wood"); var grain = Of("grain"); var beet = Of("beet"); var milk = Of("milk");
            var stone = Of("stone"); var sand = Of("sand"); var iron = Of("iron_ore"); var copper = Of("copper_ore");
            var costs = new Dictionary<string, int>();
            var chosen = new Dictionary<string, string>();
            foreach (var item in new[] { "wood", "grain", "beet", "milk", "stone", "sand", "iron_ore", "copper_ore" })
            {
                var cell = Of(item);
                var node = ResourceAt(stage, item, cell.X, cell.Z);
                costs[item] = Triangles(node);
                chosen[item] = SkinId(node) ?? node.GetMeta("resource_state").AsString();
                Check(node.GetMeta("skin_set").AsString() == "cozy", item + " is drawn with the cozy choice");
            }
            Check(chosen["grain"] == "WheatField" && chosen["beet"] == "BeetField", "grain and beet use the approved fields");
            Check(chosen["milk"] is "Pasture_1Cow" or "Pasture_2Cow", "milk uses the approved pasture");
            Check(costs["wood"] > 20 && costs["wood"] < 4000 && costs["stone"] < 4000 && costs["sand"] < 4000 && costs["iron_ore"] < 4000 && costs["copper_ore"] < 4000, "forest and deposits stay the new small meshes");
            var grainNodes = stage.GetChildren().OfType<Node3D>().Where(node => node.HasMeta("resource_item") && node.GetMeta("resource_item").AsString() == "grain").Take(2).ToArray();
            Check(grainNodes.Length == 2 && ReferenceEquals(Walk(grainNodes[0]).OfType<MeshInstance3D>().First().Mesh, Walk(grainNodes[1]).OfType<MeshInstance3D>().First().Mesh), "grain cells share one mesh");
            int forestCells = stage.GetChildren().OfType<Node3D>().Count(node => node.HasMeta("resource_item") && node.GetMeta("resource_item").AsString() == "wood");
            Check(forestCells == layout.ResourceDistricts.Where(d => d.Item == "wood").Sum(d => d.Cells.Length), "trees stay on the real wood cells");
            var baked = new Dictionary<string, int>
            {
                ["Forest_Rich_Full"] = Baked("Forest_Rich_Full"),
                ["WheatField"] = Baked("WheatField"),
                ["BeetField"] = Baked("BeetField"),
                ["Pasture_2Cow"] = Baked("Pasture_2Cow")
            };
            Check(costs["grain"] == baked["WheatField"] && costs["beet"] == baked["BeetField"], "the live fields are the approved meshes, not a replacement");
            Frame(stage, camera, new(scenario.MapWidth / 2f, 0, scenario.MapHeight / 2f), 18f);
            await Shot("01-valley-cozy");
            Frame(stage, camera, new(wood.X, 0.4f, wood.Z), 4.2f);
            await Shot("02-forest");
            Frame(stage, camera, new(grain.X, 0.2f, grain.Z), 3.4f);
            await Shot("03-grain");
            Frame(stage, camera, new(beet.X, 0.2f, beet.Z), 3.4f);
            await Shot("04-beet");
            Frame(stage, camera, new(milk.X, 0.3f, milk.Z), 3.6f);
            await Shot("05-pasture");
            Frame(stage, camera, new(stone.X, 0.2f, stone.Z), 3.2f);
            await Shot("06-stone");
            Frame(stage, camera, new(sand.X, 0.2f, sand.Z), 3.2f);
            await Shot("07-sand");
            Frame(stage, camera, new(iron.X, 0.2f, iron.Z), 3.2f);
            await Shot("08-iron");
            Frame(stage, camera, new(copper.X, 0.2f, copper.Z), 3.2f);
            await Shot("09-copper");
            var person = stage.GetNode<Node3D>("Actor_1/Appearance");
            var body = Bounds(person);
            Check(body.Position.Y >= -0.02f && body.Position.Y <= 0.04f && body.Size.Y >= 0.18f && body.Size.Y <= 0.34f, "cozy person is about two thirds of the old height and stands on the ground: " + body);
            var caption = person.GetNode<Label3D>("Caption");
            Check(Mathf.Abs(caption.Position.Y - WorldProportions.CaptionHeight) < 0.02f, "the caption stays just above the shorter head");
            Set(stage, "_follow", true);
            Set(stage, "_cameraDistance", PresentationStage.CloseView);
            Set(stage, "_cameraTarget", PresentationStage.CloseView);
            Call(stage, "FollowHuman");
            Check(camera.Position.Y >= 0.35f, "the close camera stays above the ground");
            var head = camera.UnprojectPosition(person.GlobalPosition + new Vector3(0, body.Size.Y, 0));
            var feet = camera.UnprojectPosition(person.GlobalPosition);
            var view = camera.GetViewport().GetVisibleRect().Size;
            Check(head.X > 8 && feet.X > 8 && head.X < view.X - 8 && feet.X < view.X - 8 && head.Y > 8 && feet.Y > 8 && head.Y < view.Y - 8 && feet.Y < view.Y - 8, "the close camera keeps the person in frame");
            await Shot("10-person-cozy");
            stage.ZoomView(0.01f);
            Check(Mathf.IsEqualApprox(Field<float>(stage, "_cameraTarget"), PresentationStage.CloseView), "zoom stops at the new close limit");
            Set(app, "_appearance", new AppearancePreferences { WorldSet = "classic", PeopleSet = "classic" });
            Call(app, "SaveAppearance");
            await Frames(3);
            Check(session.Engine.ComputeStateHash() == hash, "classic skin does not change the world");
            person = stage.GetNode<Node3D>("Actor_1/Appearance");
            body = Bounds(person);
            Check(body.Size.Y >= 0.18f && body.Size.Y <= 0.34f && person.GetMeta("skin_set").AsString() == "classic", "classic person uses the same smaller proportions");
            Check(ResourceAt(stage, "wood", wood.X, wood.Z).GetMeta("skin_set").AsString() == "classic", "classic forest uses the shared factory");
            Check(SkinId(ResourceAt(stage, "grain", grain.X, grain.Z)) is null && ResourceAt(stage, "grain", grain.X, grain.Z).GetMeta("skin_set").AsString() == "classic", "classic grain is the previous crop, not the cozy field");
            Check(ResourceAt(stage, "stone", stone.X, stone.Z).GetMeta("resource_state").AsString() is "full" or "thin" or "spent", "classic stone stays the new deposit");
            Frame(stage, camera, new(wood.X, 0.4f, wood.Z), 4.2f);
            await Shot("11-forest-classic");
            Frame(stage, camera, new(grain.X, 0.2f, grain.Z), 3.4f);
            await Shot("20-grain-classic");
            Frame(stage, camera, new(milk.X, 0.3f, milk.Z), 3.6f);
            await Shot("21-pasture-classic");
            Set(stage, "_follow", true);
            Set(stage, "_cameraDistance", PresentationStage.CloseView);
            Set(stage, "_cameraTarget", PresentationStage.CloseView);
            Call(stage, "FollowHuman");
            await Shot("12-person-classic");
            Set(app, "_appearance", new AppearancePreferences());
            Call(app, "SaveAppearance");
            await Frames(3);
            Check(ResourceAt(stage, "wood", wood.X, wood.Z).GetMeta("skin_set").AsString() == "cozy", "cozy can be selected again");
            void Expect(string item, string cls, string? bakedId)
            {
                var first = WorldSkins.Equipment(item, 1);
                var second = WorldSkins.Equipment(item, 2);
                Check(first.GetMeta("equipment_class").AsString() == cls && second.GetMeta("equipment_class").AsString() == cls, item + " stays " + cls);
                Check(SkinId(first) == bakedId, item + " mesh is " + (bakedId ?? "procedural") + " not a shared fallback");
                Check(!HasName(first, "Upgrade") && HasName(second, "Upgrade"), item + " level 2 adds gear without becoming another machine");
                first.Free(); second.Free();
            }
            Expect("grain", "extract_field", "FieldHarvester");
            Expect("beet", "extract_field", "FieldHarvester");
            Expect("wood", "extract_forest", "ForestHarvester");
            Expect("milk", "extract_dairy", null);
            Expect("stone", "extract_mine", null);
            Expect("sand", "extract_mine", null);
            Expect("iron_ore", "extract_mine", null);
            Expect("copper_ore", "extract_mine", null);
            Set(app, "_appearance", new AppearancePreferences { WorldSet = "classic", PeopleSet = "classic" });
            Call(app, "SaveAppearance");
            var classicMine = WorldSkins.Equipment("milk", 1);
            Check(classicMine.GetMeta("equipment_class").AsString() == "extract_dairy" && SkinId(classicMine) is null, "classic dairy is not the combine");
            classicMine.Free();
            Set(app, "_appearance", new AppearancePreferences());
            Call(app, "SaveAppearance");
            await Frames(2);
            var dealer = layout.CivicObjects.Single(site => site.Kind == "dealership");
            Frame(stage, camera, new(dealer.Entry.X, 0.4f, dealer.Entry.Z), 8f);
            await Shot("13-dealer-live");
            var lineup = new Node3D { Name = "I6C3Lineup" };
            stage.AddChild(lineup);
            string[] models = ["sputnik20", "muravey70", "eger70", "vector200", "trakt_d1"];
            var cars = new Vector3(14f, 0, 16.2f);
            for (int i = 0; i < models.Length; i++)
            {
                var node = WorldModels.Vehicle(models[i], true);
                node.Name = "Car" + i;
                node.Position = cars + new Vector3(i * 1.45f, 0, 0);
                lineup.AddChild(node);
            }
            string[] machines = ["grain", "wood", "milk", "stone"];
            var yard = new Vector3(14f, 0, 19.2f);
            for (int i = 0; i < machines.Length; i++)
            {
                var node = WorldSkins.Equipment(machines[i], 1);
                node.Name = "Class" + i;
                node.Position = yard + new Vector3(i * 1.7f, 0, 0);
                lineup.AddChild(node);
                var upgraded = WorldSkins.Equipment(machines[i], 2);
                upgraded.Name = "Level" + i;
                upgraded.Position = yard + new Vector3(i * 1.7f, 0, 1.55f);
                lineup.AddChild(upgraded);
            }
            void Show(string prefix)
            {
                foreach (var node in lineup.GetChildren().OfType<Node3D>())
                    node.Visible = node.Name.ToString().StartsWith(prefix);
            }
            Show("Car");
            Frame(stage, camera, cars + new Vector3(2.9f, 0.3f, 0), 5.2f);
            await Shot("14-vehicles");
            Show("Class");
            Frame(stage, camera, yard + new Vector3(2.55f, 0.35f, 0), 5.4f);
            await Shot("19-equipment-classes");
            Show("Class");
            foreach (var node in lineup.GetChildren().OfType<Node3D>())
                if (node.Name.ToString().StartsWith("Level")) node.Visible = true;
            Frame(stage, camera, yard + new Vector3(2.55f, 0.4f, 0.75f), 6.2f);
            await Shot("15-equipment-levels");
            lineup.QueueFree();
            string same = session.Engine.ComputeStateHash();
            Check(same == hash, "placing inspection models does not change the economy");
            app.QueueFree();
            await Frames(3);
            var editor = GD.Load<PackedScene>("res://MapEditor.tscn").Instantiate<MapEditor>();
            AddChild(editor);
            await Frames(8);
            Check(editor.Draft.Id == PlayMaps.CompactValleyScenarioId, "the editor opens the same valley");
            editor.SetPreview(true);
            await Frames(3);
            AimEditor(editor, wood.X, wood.Z, 6f);
            await Shot("16-editor-forest");
            AimEditor(editor, grain.X, grain.Z, 5f);
            await Shot("17-editor-grain");
            AimEditor(editor, stone.X, stone.Z, 5f);
            await Shot("18-editor-stone");
            Set(editor, "_distance", 0.2f);
            Call(editor, "ApplyCamera");
            Check(editor.PreviewCameraPosition.DistanceTo(editor.PreviewFocus) >= 2.9f, "the editor camera keeps its own close limit");
            var report = new
            {
                status = "succeeded",
                map = layout.SourceId,
                participants = scenario.Participants.Length,
                triangles = costs,
                chosen,
                fieldLod = WorldSkins.FieldLodReady,
                baked,
                forestCells,
                personHeight = body.Size.Y,
                checks = _checks,
                scope = "Linux software GL, llvmpipe and synthetic input on the ordinary Small valley. Triangle counts compare one live cell with the previous baked stamp. Not a physical Windows, Android or FPS measurement. The vehicle and level lineup is placed with the same factories beside the dealership because a natural start does not exhibit every model."
            };
            string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            await System.IO.File.WriteAllTextAsync(_output + "/native.json", json);
            await System.IO.File.WriteAllTextAsync(_shots + "/native.json", json);
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            if (_output.Length > 0) System.IO.File.WriteAllText(_output + "/failure.txt", error.ToString());
            GetTree().Quit(1);
        }
    }
}
