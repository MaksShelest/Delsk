using Simulation.Hosting;

/// <summary>Short player-facing instructions for the same goal and destination.</summary>
internal static class StarterGoalUi
{
    public static string Title(StarterStep step, string language) => step.Id switch
    {
        "program" => PlayerText.Local(language, "Получите помощь на старт", "Get help to start"),
        "program-land" => PlayerText.Local(language, "Купите участок 2×3", "Buy a 2×3 parcel"),
        "program-supply" => PlayerText.Local(language, "Заберите материалы", "Collect your materials"),
        "materials" => PlayerText.Local(language, "Соберите материалы для стройки", "Gather building materials"),
        _ => PlayerText.Local(language, step.TitleRu, step.TitleEn)
    };

    public static string Next(StarterStep step, string language)
    {
        string L(string ru, string en) => PlayerText.Local(language, ru, en);
        return step.Id switch
        {
            "program" => L("Программа оплатит участок, стройку и первую партию.", "The program covers land, construction and the first batch."),
            "program-land" => L("На отмеченной клетке можно купить сразу все шесть клеток.", "Buy all six cells from the marked spot."),
            "program-supply" => L("На терминале заберите материалы и сырьё для первой партии.", "Pick up building materials and your first batch of inputs at the terminal."),
            "find" => L("Встаньте на отмеченную клетку и начните добычу.", "Go to the marked cell and start gathering."),
            "harvest" => L("Дождитесь окончания добычи, чтобы товар попал в рюкзак.", "Wait for gathering to finish so the goods reach your backpack."),
            "sell" => L("На терминале выберите заказ на ваш товар.", "At the terminal, choose an order for your goods."),
            "materials" or "build" => L("Сложите материалы на участке и начните стройку.", "Bring the materials to the site and start building."),
            _ => PlayerText.Local(language, step.DetailRu, step.DetailEn)
        };
    }

    public static bool CanOpen(StarterStep step) => step.Id == "program" || step.X >= 0 || step.TargetId > 0;

    public static string Action(StarterStep step, string language, bool showingRoute = false)
    {
        if (step.Id == "program") return PlayerText.Local(language, "Открыть программу", "Open support");
        if (step.X < 0 && step.TargetId > 0) return PlayerText.Local(language, "Открыть", "Open");
        return showingRoute ? PlayerText.Local(language, "Скрыть путь", "Hide route") : PlayerText.Local(language, "Показать путь", "Show route");
    }
}
