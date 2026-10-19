using Simulation.Core;

namespace Simulation.Hosting;

public static partial class ScriptedScenarios
{
    /// <summary>Integration fixture with explicit startup funding; all work uses ordinary validators.</summary>
    public static void FoodChain(GameSession s,ScenarioDefinition d)
    {
        var mill=Build(1,"sawmill",1);Supply(1,mill,"wood",20);Produce(1,mill,10);
        BuildStore(1,"shop",5);BuildStore(2,"shop",6);BuildStore(3,"shop",7);
        var sugar=Build(2,"sugar-factory",2);Supply(2,sugar,"beet",6);Produce(2,sugar,2);
        var dairy=Build(3,"dairy",3);Supply(3,dairy,"milk",4);Produce(3,dairy,2);
        var factory=Build(1,"confectionery",4);
        // Supplier's own output is sold once, paid pickup must physically reach the recipient.
        var terms=new TradeTerms(TradeKind.Sale,new(2),new(1),"sugar",2,1500,sugar.OutputId,FulfillmentTicks:180);
        Execute(s,new(2),new(ActionKind.ProposeTrade,Terms:terms));var talk=s.Engine.InspectNegotiations().Single();
        Execute(s,new(1),new(ActionKind.AcceptTrade,NegotiationId:talk.Id,NegotiationVersion:1));
        var claim=s.Engine.Inspect().MarketAllocations.Single();Move(s,new(1),Location(sugar));
        Execute(s,new(1),new(ActionKind.CollectPurchase,PurchaseId:claim.Id,Quantity:2));Move(s,new(1),Location(factory));
        Transfer(1,Carrier(1),factory.InputId,"sugar",2);
        Supply(1,factory,"grain",4);Supply(1,factory,"milk",2);Produce(1,factory,2);
        Sell(1,factory,"pastry",2);Sell(3,dairy,"cheese",2);
        // Alternative method unlocks at L2, retaining the old active recipe until explicitly selected.
        Supply(1,mill,"wood",16);Produce(1,mill,8);
        terms=new(TradeKind.Sale,new(1),new(2),"plank",8,1500,mill.OutputId,FulfillmentTicks:180);
        Execute(s,new(1),new(ActionKind.ProposeTrade,Terms:terms));talk=s.Engine.InspectNegotiations().Single(n=>n.Status==NegotiationStatus.Proposed);
        Execute(s,new(2),new(ActionKind.AcceptTrade,NegotiationId:talk.Id,NegotiationVersion:1));claim=s.Engine.Inspect().MarketAllocations.Single(c=>c.ItemId=="plank");
        Move(s,new(2),Location(mill));Execute(s,new(2),new(ActionKind.CollectPurchase,PurchaseId:claim.Id,Quantity:8));Move(s,new(2),Location(sugar));Transfer(2,Carrier(2),sugar.InputId,"plank",8);
        Execute(s,new(2),new(ActionKind.UpgradeBuilding,Building:new(sugar.Id.Value)));
        Execute(s,new(2),new(ActionKind.SetRecipe,Enterprise:sugar.Id,RecipeId:"sugar-2"));
        sugar=Enterprise(2,sugar.Id);Supply(2,sugar,"beet",5);Produce(2,sugar,1);Sell(2,sugar,"sugar",2);
        s.Engine.ValidateInvariants();
        if(s.Engine.Inspect().Flows.Single(f=>f.ItemId=="pastry").Exported!=2 || s.Engine.Inspect().MarketAllocations.Any(a=>a.Status!="Fulfilled"))throw new InvalidOperationException("Food chain did not settle");

        Cell Location(EnterpriseView e)=>s.Engine.Inspect().Inventories.Single(i=>i.Id==e.InputId).Position;
        InventoryId Carrier(long actor)=>s.Engine.Observe(new(actor)).Participant.InventoryId;
        EnterpriseView Enterprise(long actor,EnterpriseId id)=>s.Engine.Observe(new(actor)).OwnEnterprises.Single(e=>e.Id==id);
        void Harvest(long actor,string item,long q)
        {
            var node=d.ResourceNodes.First(n=>n.ItemId==item && (n.Rules!.Public || n.Rules.InitialOwner==actor));
            Move(s,new(actor),node.Position);Execute(s,new(actor),new(ActionKind.Harvest,ItemId:item,Quantity:q));Wait(s,()=>!s.Engine.Observe(new(actor)).Participant.Busy);
        }
        void Transfer(long actor,InventoryId source,InventoryId destination,string item,long q)=>Execute(s,new(actor),new(ActionKind.Transfer,Source:source,Destination:destination,ItemId:item,Quantity:q));
        BuildingView BuildStore(long actor,string type,long plot)
        {
            var level=d.BuildingTypes!.Single(t=>t.Id==type).Levels[0];
            foreach(var material in level.Materials)
            {
                if(material.ItemId=="wood")Harvest(actor,"wood",material.Quantity);
                else {var source=s.Engine.Observe(new(actor)).OwnBuildings.Single(b=>b.TypeId=="sawmill");Move(s,new(actor),source.Position);Transfer(actor,source.OutputId!.Value,Carrier(actor),material.ItemId,material.Quantity);}
            }
            var pos=d.Plots!.Single(p=>p.Id==plot).Position;Move(s,new(actor),pos);Execute(s,new(actor),new(ActionKind.BuyPlot,Plot:new(plot)));
            Execute(s,new(actor),new(ActionKind.Build,Target:pos,BuildingTypeId:type));return s.Engine.Observe(new(actor)).OwnBuildings.Single(b=>b.Position==pos);
        }
        EnterpriseView Build(long actor,string type,long plot)
        {
            var b=BuildStore(actor,type,plot);return Enterprise(actor,new(b.Id.Value));
        }
        void Supply(long actor,EnterpriseView e,string item,long q){Harvest(actor,item,q);Move(s,new(actor),Location(e));Transfer(actor,Carrier(actor),e.InputId,item,q);}
        void Produce(long actor,EnterpriseView e,long batches)
        {
            long expected=Enterprise(actor,e.Id).CompletedBatches+batches;
            Execute(s,new(actor),new(ActionKind.SetProductionEnabled,Enterprise:e.Id,Enabled:true));Wait(s,()=>Enterprise(actor,e.Id).CompletedBatches==expected);
            Execute(s,new(actor),new(ActionKind.SetProductionEnabled,Enterprise:e.Id,Enabled:false));
        }
        void Sell(long actor,EnterpriseView e,string item,long q)
        {
            Move(s,new(actor),Location(e));Transfer(actor,e.OutputId,Carrier(actor),item,q);Move(s,new(actor),d.CapitalDemand!.Terminal);
            long limit=s.Engine.Tick+600;
            while(q>0)
            {
                if(s.Engine.Tick>=limit)throw new InvalidOperationException("No funded demand for food output");
                var offer=s.Engine.Observe(new(actor)).CapitalOffers.FirstOrDefault(o=>o.ItemId==item && o.RemainingQuantity>0);
                if(offer is null){s.Step();continue;}
                long take=Math.Min(q,offer.RemainingQuantity);Execute(s,new(actor),new(ActionKind.SellAvailableToCapital,ItemId:item,Quantity:take,OfferId:offer.Id));Wait(s,()=>!s.Engine.Observe(new(actor)).Participant.Busy);q-=take;
            }
        }
    }
}
