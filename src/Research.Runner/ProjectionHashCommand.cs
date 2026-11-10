using Simulation.Hosting;

/// <summary>
/// Служебная команда лаборатории: подтвердить хеш уже существующего профиля после правки поддержанного поля.
/// Не создаёт правил и не выбирает экономику за исследователя.
/// </summary>
internal static class ProjectionHashCommand
{
    public static int Run(string[] args)
    {
        int index = Array.IndexOf(args, "--refresh-projection-hash");
        if (index < 0 || index + 2 >= args.Length)
        {
            Console.Error.WriteLine("Нужны входной и выходной JSON: --refresh-projection-hash INPUT OUTPUT");
            return 2;
        }
        string input = Path.GetFullPath(args[index + 1]);
        string output = Path.GetFullPath(args[index + 2]);
        if (!File.Exists(input))
        {
            Console.Error.WriteLine("Файл профиля не найден");
            return 2;
        }
        string updated = EconomyProfile.RefreshProjectionPayload(File.ReadAllText(input));
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        string temporary = output + ".tmp";
        File.WriteAllText(temporary, updated);
        File.Move(temporary, output, true);
        return 0;
    }
}
