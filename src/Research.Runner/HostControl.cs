using System.Text.Json;

/// <summary>
/// Пауза и остановка между тиками, когда полная запись мира выключена.
/// Файл control.json читается так же, как у ResearchObservation: action = paused | running | cancelled.
/// Author: Шелестов Максим Евгеньевич.
/// </summary>
internal static class HostControl
{
    public static void Boundary(string directory, Action? whilePaused)
    {
        while (true)
        {
            string action = Read(directory);
            if (action == "cancelled") throw new ResearchCancelledException();
            if (action != "paused") return;
            whilePaused?.Invoke();
            Thread.Sleep(100);
        }
    }
    private static string Read(string directory)
    {
        string path = Path.Combine(directory, "control.json");
        if (!File.Exists(path)) return "running";
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.TryGetProperty("action", out var value) ? value.GetString() ?? "running" : "running";
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            // Неполный файл во время записи не должен обрывать опыт.
            return "running";
        }
    }
}
