namespace Simulation.Core;

/// <summary>
/// Права на природные клетки новых обычных партий.
/// Поле отсутствует в исторических профилях: признак Public там по-прежнему ограничивает ручную добычу.
/// Версия 1: ничейная клетка доступна для ручной добычи независимо от Public, её можно купить,
/// а аренда отключена. Public не переписывается и остаётся географией.
/// </summary>
public sealed record ResourceRightsRules(int Version = 1)
{
    public void Validate()
    {
        if (Version != 1) throw new ArgumentException("Unsupported resource rights version");
    }

    public static bool Applies(ResourceRightsRules? rules) => rules?.Version == 1;

    public static bool Applies(EconomicRules? rules) => Applies(rules?.ResourceRights);

    /// <summary>Ручная добыча. Владелец важнее общедоступности. Ничейная клетка при версии 1 доступна всем.</summary>
    public static bool Manual(bool open, bool Public, long? owner, long? tenant, long? leaseUntil, long actor, long tick)
    {
        if (owner == actor) return true;
        if (owner is not null) return false;
        if (tenant == actor && leaseUntil is { } until && tick < until) return true;
        if (tenant is not null) return false;
        return open || Public;
    }

    public static bool Purchasable(bool open, bool Public, long? owner, long? tenant) =>
        owner is null && tenant is null && (open || !Public);

    public static bool Leasable(bool open, bool Public, long? owner, long? tenant) =>
        !open && !Public && owner is null && tenant is null;
}
