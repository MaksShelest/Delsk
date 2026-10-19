namespace Simulation.Core;

public record DeliverySettings(int ContractTtlTicks = 180, int FineBasisPoints = 500, int OfferTtlTicks = 180);
public record SupplyRequest(long BuyOfferId, long CapitalOfferId, ParticipantId Supplier, long Quantity,
    long BondMinor, string BondHold, long SubmittedTick);
public record BuyOfferView(long Id, ParticipantId Buyer, InventoryId DestinationInventory, Cell Position,
    string ItemId, long Quantity, long RemainingQuantity, long UnitPriceMinor, long CreatedTick,
    long ExpiresAtTick, long? CutoffTick, int Window, long AllocatedQuantity, long ReleasedQuantity, string Status);
public record DeliveryContractView(long Id, long BuyOfferId, long CapitalOfferId, ParticipantId? Buyer,
    ParticipantId Supplier, InventoryId? DestinationInventory, Cell Position, string ItemId,
    long Quantity, long UnitPriceMinor, AccountId BuyerAccount, AccountId EscrowAccount, AccountId BondAccount,
    long BondMinor, string CapacityHold, long CreatedTick, long DeadlineTick,
    long DeliveredQuantity = 0, long UnfulfilledQuantity = 0, long PaidMinor = 0, long RefundedMinor = 0,
    long FineMinor = 0, long BondReturnedMinor = 0, string Status = "Active", long? ClosedTick = null, long PreparedQuantity = 0, long InTransitQuantity = 0,long NegotiationId=0)
{
    public long RemainingQuantity => ClosedTick is null ? checked(Quantity - DeliveredQuantity) : 0;
    public long UnprovidedQuantity => checked(RemainingQuantity - PreparedQuantity - InTransitQuantity);
}
internal record SavedBuyOffer(BuyOfferView View, SupplyRequest[] Requests);
internal sealed class BuyOfferState(BuyOfferView view)
{
    public BuyOfferView View { get; set; } = view;
    public string BudgetHold => $"buy-offer:{View.Id}";
    public Dictionary<long, SupplyRequest> Requests { get; } = [];
}
