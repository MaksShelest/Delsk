using Simulation.Core;

namespace Simulation.Hosting;

public static partial class ScriptedScenarios
{
    public static void ParticipantTrade(GameSession s,ScenarioDefinition d)
    {
        ParticipantId seller=new(1),buyer=new(2);
        var warehouse=s.Engine.Observe(seller).OwnBuildings.Single(b=>b.Kind==BuildingKind.Warehouse);
        var destination=s.Engine.Observe(buyer).OwnBuildings.Single(b=>b.Kind==BuildingKind.Warehouse);
        var shop=s.Engine.Observe(seller).OwnBuildings.Single(b=>b.Kind==BuildingKind.Shop);
        Execute(s,seller,new(ActionKind.Harvest,ItemId:"wood",Quantity:8));Wait(s,()=>!s.Engine.Observe(seller).Participant.Busy);
        Move(s,seller,warehouse.Position);
        Execute(s,seller,new(ActionKind.Transfer,ItemId:"wood",Quantity:8,Source:s.Engine.Observe(seller).Participant.InventoryId,Destination:warehouse.InputId));
        Execute(s,seller,new(ActionKind.CreateSellOffer,Building:shop.Id,Source:warehouse.InputId,ItemId:"wood",Quantity:2,UnitPriceMinor:500));
        var offer=s.Engine.Observe(buyer).MarketOffers.Single();
        Execute(s,buyer,new(ActionKind.SubmitBuyRequest,MarketOfferId:offer.Id,Quantity:2,MaximumPriceMinor:500));
        Wait(s,()=>s.Engine.Inspect().MarketAllocations.Length>0);
        var claim=s.Engine.Inspect().MarketAllocations.Single();
        var guide=s.Engine.NavigatePurchase(buyer,claim.Id);
        if(guide.ReasonCode!="Ready" || guide.Position!=warehouse.Position || s.Engine.NavigatePurchase(new(3),claim.Id).ReasonCode!="NotAvailable")
            throw new InvalidOperationException("Buyer-specific pickup guide differs");
        Move(s,buyer,guide.Position);Execute(s,buyer,new(ActionKind.CollectPurchase,PurchaseId:claim.Id,Quantity:1));
        Execute(s,buyer,new(ActionKind.CollectPurchase,PurchaseId:claim.Id,Quantity:1));
        var terms=new TradeTerms(TradeKind.Sale,seller,buyer,"wood",2,700,warehouse.InputId,FulfillmentTicks:60);
        Execute(s,seller,new(ActionKind.ProposeTrade,Terms:terms));var n=s.Engine.InspectNegotiations().Single();
        Execute(s,buyer,new(ActionKind.CounterTrade,NegotiationId:n.Id,NegotiationVersion:1,Terms:terms with {UnitPriceMinor=600}));
        Execute(s,seller,new(ActionKind.AcceptTrade,NegotiationId:n.Id,NegotiationVersion:2));
        claim=s.Engine.Inspect().MarketAllocations.Single(a=>a.NegotiationId==n.Id);
        Execute(s,buyer,new(ActionKind.CollectPurchase,PurchaseId:claim.Id,Quantity:2));
        Execute(s,buyer,new(ActionKind.ProposeTrade,Terms:new(TradeKind.Purchase,seller,buyer,"wood",4,500,Destination:destination.InputId,FulfillmentTicks:80)));
        n=s.Engine.InspectNegotiations().Single(n=>n.Status==NegotiationStatus.Proposed);
        Execute(s,seller,new(ActionKind.AcceptTrade,NegotiationId:n.Id,NegotiationVersion:1));
        var delivery=s.Engine.Inspect().DeliveryContracts.Single();
        Execute(s,seller,new(ActionKind.Transfer,ItemId:"wood",Quantity:4,Source:warehouse.InputId,Destination:s.Engine.Observe(seller).Participant.InventoryId));
        Move(s,seller,destination.Position);Execute(s,seller,new(ActionKind.DeliverContract,ContractId:delivery.Id,Quantity:4));
        Execute(s,seller,new(ActionKind.ProposeTrade,Terms:new(TradeKind.Support,buyer,seller,Election:new(1),Compensation:[new("wood",2)])));
        n=s.Engine.InspectNegotiations().Single(n=>n.Status==NegotiationStatus.Proposed);
        Execute(s,buyer,new(ActionKind.AcceptTrade,NegotiationId:n.Id,NegotiationVersion:1));
        if(s.Engine.InspectSupport().Commitments.Length!=0)throw new InvalidOperationException("Support signed before compensation");
        Move(s,seller,d.ResourceNodes[0].Position);Execute(s,seller,new(ActionKind.Harvest,ItemId:"wood",Quantity:2));Wait(s,()=>!s.Engine.Observe(seller).Participant.Busy);
        Move(s,seller,d.Support!.TradingPoint);Execute(s,seller,new(ActionKind.Transfer,Source:s.Engine.Observe(seller).Participant.InventoryId,Destination:s.Engine.Observe(seller).Support.OwnSection,ItemId:"wood",Quantity:2));
        Execute(s,seller,new(ActionKind.SignSupport,SupportProposalId:n.ContractId==0?s.Engine.InspectNegotiations().Single(x=>x.Id==n.Id).ContractId:n.ContractId));
        s.AdvanceTo(200);
        if(s.Engine.InspectSupport().Commitments.Single().Status!=PoliticalCommitmentStatus.Executed ||
            s.Engine.Inspect().DeliveryContracts.Single().Status!="Fulfilled" || s.Engine.Inspect().MarketAllocations.Any(a=>a.Status!="Fulfilled"))
            throw new InvalidOperationException("Participant trade did not physically settle");
        Execute(s,seller,new(ActionKind.ContinueAfterVictory));s.AdvanceTo(220);
        if(GameSession.Replay(d,s.CommandTape,220).Engine.ComputeStateHash()!=s.Engine.ComputeStateHash())throw new InvalidOperationException("Participant trade replay differs");
    }
}
