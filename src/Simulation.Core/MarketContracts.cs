using System.Collections.Immutable;

namespace Simulation.Core;

public record MarketSettings(int AuctionRoundTicks = 5, int AuctionRounds = 2, long MinimumRaiseMinor = 1);
public enum MarketOfferStatus { Collecting, Auction, Closed }
public record PublicMarketBid(ParticipantId Buyer, long Quantity, long CurrentPriceMinor);
public record MarketOfferView(long Id, ParticipantId Seller, BuildingId Shop, Cell Position, string ItemId,
    long Quantity, long BasePriceMinor, long CreatedTick, long CutoffTick, int Round,
    MarketOfferStatus Status, long AllocatedQuantity, long? ClosedTick, ImmutableArray<PublicMarketBid> Bids);
public record MarketBidView(long OfferId, ParticipantId Buyer, long Quantity, long CurrentPriceMinor,
    long MaximumPriceMinor, string MoneyHold);
// P14 allocates a conditional pickup claim. Physical collection/deadline/refund arrive in P15.
public record MarketAllocationView(long Id, long OfferId, ParticipantId Seller, ParticipantId Buyer,
    InventoryId SourceInventory, string ItemId, long Quantity, long UnitPriceMinor,
    AccountId EscrowAccount, string StockHold, long CreatedTick, string Status = "AwaitingPickup");
internal record SavedMarketOffer(long Id, long Seller, long Shop, long Inventory, string Item, long Quantity,
    long BasePrice, long CreatedTick, long Cutoff, int Round, MarketOfferStatus Status,
    long Allocated, long? ClosedTick, string StockHold, MarketBidView[] Bids);

internal sealed class MarketOfferState(long id, long seller, long shop, long inventory, string item,
    long quantity, long price, long createdTick, long cutoff)
{
    public long Id { get; } = id;
    public long Seller { get; } = seller;
    public long Shop { get; } = shop;
    public long Inventory { get; } = inventory;
    public string Item { get; } = item;
    public long Quantity { get; } = quantity;
    public long BasePrice { get; } = price;
    public long CreatedTick { get; } = createdTick;
    public long Cutoff { get; set; } = cutoff;
    public int Round { get; set; }
    public MarketOfferStatus Status { get; set; } = MarketOfferStatus.Collecting;
    public long Allocated { get; set; }
    public long? ClosedTick { get; set; }
    public string StockHold => $"market-offer:{Id}";
    public Dictionary<long, MarketBidView> Bids { get; } = [];
}
