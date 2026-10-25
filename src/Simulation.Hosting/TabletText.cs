namespace Simulation.Hosting;

public static class TabletText
{
    public static string Reason(string language, string code)
    {
        var (ru, en) = code switch
        {
            "PersonalMode" => ("Личный режим", "Personal mode"),
            "Working" => ("Выпускаем продукцию", "Producing goods"),
            "Ready" => ("Готово к работе", "Ready to work"),
            "PreparingDemolition" => ("Подготовка к сносу: новые партии запрещены", "Preparing for demolition: no new batches"),
            "NoReadyWorkshopSlot" => ("Нет свободного готового места; развитие относится к D2", "No vacant ready place; expansion belongs to D2"),
            "InvalidStaffTransfer" => ("Недостаточно сотрудников у источника или мест у получателя", "Not enough source staff or destination places"),
            "WorkshopExpansionNotAvailable" => ("Проекты расширения пока недоступны", "Expansion projects are not available yet"),
            "UserPaused" => ("Остановлено владельцем", "Stopped by owner"),
            "NoInputs" => ("Недостаточно сырья", "Not enough input materials"),
            "NoStock" => ("Нет свободного товара для перевозки", "No unreserved goods to transport"),
            "NoOutputSpace" => ("Нет места для готовой продукции", "Finished-goods storage is full"),
            "NoDestinationSpace" => ("В пункте назначения нет свободного места", "Destination storage is full"),
            "NoCapacity" => ("Недостаточно свободного места", "Not enough storage capacity"),
            "NoMoney" => ("Недостаточно свободных денег", "Not enough available money"),
            "NoStaff" or "BelowMinimumCrew" or "BelowMinimumStaff" => ("Недостаточно работников; найм идёт постепенно", "Not enough workers; hiring takes time"),
            "NoPaidOffer" => ("Назначьте положительную зарплату", "Set a positive wage"),
            "NoCargoSpace" => ("Груз не помещается в машину", "Cargo does not fit in the truck"),
            "NoActiveContract" => ("Нет действующего договора или выбранного маршрута", "No active contract or selected route"),
            "NoUnassignedQuota" => ("Весь объём договора уже назначен", "The contract quantity is already assigned"),
            "ArrivalAfterDeadline" => ("Машина не успевает к сроку доставки", "The truck cannot arrive before the delivery deadline"),
            "IncompatibleRecipe" => ("Товар маршрута не подходит текущему рецепту", "Route goods do not match the current recipe"),
            "Loading" => ("Подготовка рейса и погрузки", "Preparing the trip and loading"),
            "Outbound" => ("Везём груз получателю", "Delivering cargo to the recipient"),
            "ReturningEmpty" => ("Возвращаемся без груза", "Returning empty"),
            "ReturningCargo" => ("Возвращаем непринятый груз", "Returning unaccepted cargo"),
            "FetchingEmpty" => ("Едем за сырьём без груза", "Driving empty to collect inputs"),
            "ReturningInput" => ("Везём сырьё на предприятие", "Bringing inputs to the enterprise"),
            "Finishing" => ("Завершаем рейс", "Finishing the trip"),
            "Waiting" => ("Ожидаем доступной работы", "Waiting for available work"),
            "Disabled" or "OutputDisabled" => ("Исходящие поставки выключены", "Outbound deliveries are disabled"),
            "InboundDisabled" => ("Подвоз сырья выключен", "Inbound supply is disabled"),
            _ => (PlayerText.Reason(language, code), PlayerText.Reason(language, code))
        };
        return PlayerText.Local(language, ru, en);
    }
}
