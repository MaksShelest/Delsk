namespace Simulation.Hosting;

/// <summary>
/// Декоративное расположение. Ключ — идентификатор карты, клетка, ресурс и индекс.
/// Экономический PRNG не используется, поэтому кадр HUD не двигает растения.
/// </summary>
public static class WorldArtScatter
{
    /// <summary>
    /// Экспериментальные колосья, свёкла и коровы I6-C3. По умолчанию выключено:
    /// обычная игра берёт прежние WheatField, BeetField и Pasture.
    /// </summary>
    public static bool ExperimentalCrops { get; set; }

    public readonly record struct Mark(float X, float Z, float Yaw, float Scale, float Lean, int Variant, int Tint);

    public static string EquipmentClass(string? item) => item switch
    {
        "wood" => "extract_forest",
        "milk" => "extract_dairy",
        "stone" or "sand" or "iron_ore" or "copper_ore" => "extract_mine",
        "grain" or "beet" => "extract_field",
        _ => ""
    };

    public static Mark[] Layout(string? mapKey, int x, int z, string item, string state)
    {
        mapKey ??= "";
        if (item == "wood") return Forest(mapKey, x, z, state);
        if (item == "grain") return Crop(mapKey, x, z, item, state, 16);
        if (item == "beet") return Crop(mapKey, x, z, item, state, 5);
        if (item == "milk") return Pasture(mapKey, x, z, state);
        if (item is "stone" or "sand" or "iron_ore" or "copper_ore") return Deposit(mapKey, x, z, item, state);
        return [];
    }

    public static uint Mix(string mapKey, int x, int z, string salt, int n)
    {
        unchecked
        {
            uint h = 2166136261;
            void Add(uint v) { h ^= v; h *= 16777619; }
            foreach (char c in mapKey) Add(c);
            Add((uint)x);
            Add(0x9e3779b9);
            Add((uint)z);
            Add(0x85ebca6b);
            foreach (char c in salt) Add(c);
            Add((uint)n);
            h ^= h >> 16;
            h *= 0x7feb352d;
            h ^= h >> 15;
            h *= 0x846ca68b;
            h ^= h >> 16;
            return h;
        }
    }

    private static float Range(uint h, float a, float b) => a + (h % 10001) / 10000f * (b - a);
    private static float Dist(Mark mark, float x, float z)
    {
        float dx = mark.X - x, dz = mark.Z - z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    private static Mark[] Forest(string mapKey, int x, int z, string state)
    {
        bool cut = state.Contains("Cut", StringComparison.Ordinal);
        bool young = state.Contains("Young", StringComparison.Ordinal);
        bool sparse = state.Contains("Sparse", StringComparison.Ordinal);
        var list = new List<Mark>();
        if (!cut && !sparse)
        {
            Side(list, mapKey, x, z, 1, 0, young);
            Side(list, mapKey, x, z, -1, 0, young);
            Side(list, mapKey, x, z, 0, 1, young);
            Side(list, mapKey, x, z, 0, -1, young);
        }
        int interior = cut ? (sparse ? 2 : 3) : sparse ? (young ? 2 : 3) : (young ? 2 : 3);
        int salt = 1;
        while (list.Count < (cut ? interior : interior + (sparse ? 0 : 4)) && salt < 40 && list.Count(m => Math.Abs(m.X) < 0.28f && Math.Abs(m.Z) < 0.28f) < interior)
        {
            uint h = Mix(mapKey, x, z, "wood", salt);
            float px = Range(h, -0.28f, 0.28f);
            float pz = Range(Mix(mapKey, x, z, "wood-z", salt), -0.28f, 0.28f);
            if (list.All(m => Dist(m, px, pz) > (cut ? 0.12f : 0.16f)))
                list.Add(Tree(h, px, pz, list.Count, cut, young));
            salt++;
        }
        if (state == "Forest_Rich_Cut")
        {
            uint h = Mix(mapKey, x, z, "sapling", 1);
            list.Add(new(Range(h, -0.1f, 0.1f), Range(Mix(mapKey, x, z, "sapling", 2), -0.1f, 0.1f), Range(h, 0, 360), 0.42f, 0, 1, 2));
        }
        return list.ToArray();
    }

    private static void Side(List<Mark> list, string mapKey, int x, int z, int sx, int sz, bool young)
    {
        int keyX = sx != 0 ? (sx > 0 ? x + 1 : x) : x;
        int keyZ = sz != 0 ? (sz > 0 ? z + 1 : z) : z;
        uint h = Mix(mapKey, keyX, keyZ, sx != 0 ? "vx" : "hz", 3);
        float along = Range(h, -0.16f, 0.16f);
        float px = sx != 0 ? 0.30f * sx : along;
        float pz = sz != 0 ? 0.30f * sz : along;
        if (list.Any(m => Dist(m, px, pz) < 0.14f)) return;
        list.Add(Tree(h, px, pz, list.Count, false, young));
    }

    private static Mark Tree(uint h, float x, float z, int index, bool cut, bool young)
    {
        float scale = cut ? Range(h, 0.85f, 1.15f) : young ? Range(h, 0.48f, 0.74f) : Range(h, 0.82f, 1.16f);
        return new(x, z, Range(Mix(h), 0, 360), scale, Range(h >> 3, -6f, 6f), index % 4, (int)(h % 3));
    }

    private static uint Mix(uint h) { unchecked { return h * 0x9e3779b9; } }

    private static Mark[] Crop(string mapKey, int x, int z, string item, string state, int count)
    {
        if (state == "EmptyField") count = item == "beet" ? 0 : 6;
        var list = new List<Mark>();
        for (int i = 0; i < count; i++)
        {
            uint h = Mix(mapKey, x, z, item, i + 1);
            float radius = 0.06f + 0.32f * MathF.Sqrt((i + 0.5f) / Math.Max(1, count));
            float angle = i * 2.399963f + Range(h, -0.45f, 0.45f);
            float px = Math.Clamp(MathF.Cos(angle) * radius + Range(h >> 1, -0.03f, 0.03f), -0.4f, 0.4f);
            float pz = Math.Clamp(MathF.Sin(angle) * radius + Range(Mix(mapKey, x, z, item + "-z", i + 1), -0.03f, 0.03f), -0.4f, 0.4f);
            float scale = state == "EmptyField" ? Range(h, 0.28f, 0.45f) : Range(h, 0.78f, 1.18f);
            list.Add(new(px, pz, Range(h, 0, 360), scale, Range(h >> 4, -11f, 11f), i % 3, (int)(h % 3)));
        }
        return list.ToArray();
    }

    private static Mark[] Pasture(string mapKey, int x, int z, string state)
    {
        var list = new List<Mark>();
        int cows = state == "Pasture_2Cow" ? 2 : state == "Pasture_1Cow" ? 1 : 0;
        for (int i = 0; i < cows; i++)
        {
            uint h = Mix(mapKey, x, z, "cow", i + 1);
            float px = (i == 0 ? -0.14f : 0.16f) + Range(h, -0.05f, 0.05f);
            float pz = (i == 0 ? 0.08f : -0.12f) + Range(Mix(mapKey, x, z, "cow-z", i + 1), -0.05f, 0.05f);
            list.Add(new(px, pz, Range(h, 0, 360), Range(h, 0.92f, 1.08f), 0, i % 3, (int)(h % 2)));
        }
        for (int i = 0; i < 6; i++)
        {
            uint h = Mix(mapKey, x, z, "grass", i + 1);
            list.Add(new(Range(h, -0.4f, 0.4f), Range(Mix(mapKey, x, z, "grass-z", i + 1), -0.4f, 0.4f), Range(h, 0, 360), Range(h, 0.7f, 1.15f), 0, 10 + i % 2, (int)(h % 3)));
        }
        return list.ToArray();
    }

    private static Mark[] Deposit(string mapKey, int x, int z, string item, string state)
    {
        int count = state == "spent" ? 2 : state == "thin" ? 2 : 4;
        var list = new List<Mark>();
        for (int i = 0; i < count; i++)
        {
            uint h = Mix(mapKey, x, z, item, i + 1);
            float angle = i * 2.399963f + Range(h, -0.4f, 0.4f);
            float radius = count == 2 ? 0.12f : 0.08f + i * 0.07f;
            float scale = state == "spent" ? Range(h, 0.35f, 0.55f) : Range(h, 0.72f, 1.25f);
            list.Add(new(Math.Clamp(MathF.Cos(angle) * radius, -0.34f, 0.34f), Math.Clamp(MathF.Sin(angle) * radius, -0.32f, 0.32f), Range(h, 0, 360), scale, Range(h, -8f, 8f), i % 3, (int)(h % 3)));
        }
        return list.ToArray();
    }
}
