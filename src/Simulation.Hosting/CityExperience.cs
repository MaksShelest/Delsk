using Simulation.Core;
namespace Simulation.Hosting;

public sealed record CityReadMemory(string[] Seen,int TutorialPage=0);
public sealed class CityReadState
{
    private readonly List<string> _seen=[];
    public int TutorialPage {get;set;}
    public bool Seen(string key)=>_seen.Contains(key,StringComparer.Ordinal);
    public void MarkSeen(string key){if(Seen(key))return;_seen.Add(key);if(_seen.Count>512)_seen.RemoveAt(0);}
    public CityReadMemory Capture()=>new([.._seen],Math.Clamp(TutorialPage,0,4));
    public static CityReadState Restore(CityReadMemory memory)
    {
        if(memory.Seen is null||memory.Seen.Length>512||memory.Seen.Any(s=>s is null||s.Length>160)||memory.TutorialPage is <0 or >4)throw new ArgumentException("Invalid city UI memory");
        var state=new CityReadState{TutorialPage=memory.TutorialPage};foreach(var k in memory.Seen)state.MarkSeen(k);return state;
    }
}
public sealed record CityNotice(string Key,string Category,string Target,long Id,string Item,long Quantity,long Deadline,
    bool Unread,bool Resolved=false,GameAction? ViewAction=null,Cell? Position=null);

/// <summary>Notifications are projections, not a second event bus or economic journal.</summary>
public static class CityNotices
{
    public static CityNotice[] Read(ScenarioDefinition initial,PlayerView own,CityReadState read)
    {
        long actor=own.Participant.Id.Value;var notices=new List<CityNotice>();
        foreach(var bill in own.PersonalAccount?.Invoices.Take(20)??[])
            notices.Add(new("bill:"+bill.Id,"bill","account",bill.Id,"",bill.Remaining,bill.DueTick,!bill.Viewed,bill.Remaining==0,new(ActionKind.ViewInvoice,OfferId:bill.Id)));
        foreach(var order in own.Services?.OwnOrders.Where(o=>o.Customer.Value==actor||o.Carrier.Value==actor).OrderByDescending(o=>o.CreatedTick).Take(30)??[])
        {
            bool viewed=order.Carrier.Value==actor?order.CarrierViewed:order.CustomerViewed;
            notices.Add(new("service:"+order.Id,"service","services",order.Id,order.Quote.Terms.ItemId,order.Quote.Terms.Quantity,order.Active?(order.State=="Accepted"?order.DeadlineTick:order.RespondByTick):0,!viewed,!order.Active,new(ActionKind.ViewDeliveryService,ServiceId:order.Id)));
        }
        foreach(var b in own.Buildings.Where(b=>b.OwnerId.Value==actor))
        {
            var seed=initial.Buildings?.FirstOrDefault(s=>s.Id==b.Id.Value);
            if(seed is not null&&b.Level<=seed.Level)continue;
            string key=$"building:{b.Id.Value}:{b.Level}";notices.Add(new(key,"work","building",b.Id.Value,b.TypeId,b.Level,0,!read.Seen(key),true,Position:b.Position));
        }
        foreach(var e in own.Enterprises.Where(e=>e.OwnerId.Value==actor&&!e.Workshops.IsDefault))foreach(var w in e.Workshops.Where(w=>w.Id!=e.Id.Value))
        {string key="workshop:"+w.Id;notices.Add(new(key,"work","building",e.Id.Value,"workshop",1,0,!read.Seen(key),true));}
        foreach(var receipt in own.Demolition?.Receipts.Where(r=>r.Owner==actor)??[])
        {string key="demolition:"+receipt.Target+":"+receipt.Tick;notices.Add(new(key,"work","holdings",receipt.Target,"demolition",0,0,!read.Seen(key),true));}
        foreach(var t in own.ProductionContent?.Technologies.Where(t=>t.Version>1)??[])
        {
            var e=own.Enterprises.FirstOrDefault(e=>e.OwnerId.Value==actor&&e.Workshops.Any(w=>w.Id==t.Workshop));if(e is null)continue;
            string key=$"technology:{t.Workshop}:{t.Version}";notices.Add(new(key,"work","building",e.Id.Value,"workshop",t.Version,0,!read.Seen(key),true));
        }
        foreach(var order in own.Roads?.Orders.Where(o=>o.Assignee==actor&&o.PaidTick>0)??[])
        {string key="road-work:"+order.Id;notices.Add(new(key,"work","road",order.Id,"road",order.Level,0,!read.Seen(key),true,Position:order.Cell));}
        foreach(var quest in own.Development?.Quests.Where(q=>q.Owner.Value==actor&&q.Status=="Completed")??[])
        {string key="quest:"+quest.Id;notices.Add(new(key,"work","quests",quest.Id,quest.ItemId,quest.Delivered,0,!read.Seen(key),true));}
        foreach(var inventory in own.Inventories.Where(i=>i.OwnerId.Value==actor))foreach(var lot in inventory.Overflow??[])
        {
            if(lot.Owner!=actor)continue;string key="overflow:"+lot.Id;var building=own.Buildings.FirstOrDefault(b=>b.InputId==inventory.Id||b.OutputId==inventory.Id);
            var ground=own.Storage?.Ground.FirstOrDefault(g=>g.InventoryId==inventory.Id.Value);
            string target=building is not null?"building":ground is not null?"ground":"fleet";long id=building?.Id.Value??ground?.PlotId??inventory.Id.Value;
            notices.Add(new(key,"overflow",target,id,lot.ItemId,lot.Quantity,lot.ExpiresTick,!read.Seen(key),Position:inventory.Position));
        }
        return notices.OrderByDescending(n=>n.Unread).ThenBy(n=>n.Category=="overflow"?0:n.Category=="service"?1:n.Category=="bill"?2:3)
            .ThenBy(n=>n.Deadline==0?long.MaxValue:n.Deadline).ThenBy(n=>n.Key,StringComparer.Ordinal).Take(100).ToArray();
    }
}

public sealed record CityLesson(string TitleRu,string TitleEn,string BodyRu,string BodyEn,string Destination);
public static class CityJourney
{
    public static readonly CityLesson[] Steps=[
        new("Заработок","Earn the starting budget","Найдите общий ресурс на карте, подойдите и добудьте. Вместимость считается по объёму. Продайте столице по действующему предложению; учитывайте путь и срок. Не тратьте весь запас денег до расчёта стройки.","Find a public resource on the map, walk there and harvest. Capacity is measured by volume. Sell against a live capital order; include travel and expiry. Budget the whole building project before spending your reserve.","map"),
        new("Первое предприятие","First enterprise","Купите разрешённый участок, доставьте материалы и оплатите строительство. В карточке отдельно видны вход, выход, работники и зарплата. Открытое объявление о покупке ещё не означает поставленное сырьё. Действия на месте требуют подхода к входу.","Buy an eligible plot, bring materials and fund construction. The card separates input, output, workers and wages. An open buy offer does not mean materials have arrived. Local actions require reaching the entrance.","holdings"),
        new("Цехи и задания","Workshops and quests","Оснастите доступный цех и назначьте существующих работников. Для рецепта нужны опыт, сырьё, деньги и спрос. Задания принимаются явно; награда связана с реальным результатом. Новое здание не заменяет обслуживание старого.","Outfit an eligible workshop and assign existing staff. Recipes require experience, inputs, money and demand. Accept quests explicitly; rewards follow real results. Keep serving older factories after expanding.","quests"),
        new("Транспорт и доставка","Vehicles and delivery","Купите подходящую машину в конечном ассортименте. Большой кузов не ускоряет погрузку. Включите приём заказов либо выберите перевозчика: проверьте рейсы, ETA, срок и цену. Ответ — 10 активных тиков. Погрузка и выгрузка выполняются на месте; оплата идёт по доставленным частям.","Buy a suitable vehicle from finite stock. A larger body does not load faster. Offer delivery or choose a carrier: inspect trips, ETA, deadline and price. Response lasts 10 active ticks. Load and unload at the site; payment follows delivered parts.","fleet"),
        new("Счета и переполнение","Invoices and overflow","Проезд создаёт начисления, стоянка — нет. Просмотр счёта не является оплатой; автоплата расходует только свободные деньги. Долг не мешает зарабатывать. У излишка свой срок хранения: вывезите его или освободите место до потери. События ведут к нужной карточке.","Travel accrues road charges; parking does not. Viewing an invoice does not pay it; auto pay uses only free money. Debt does not prevent earning. Each overflow lot has an expiry: move it or free space before loss. Notices link to the relevant card.","notices")];
}
