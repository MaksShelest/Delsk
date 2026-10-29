using Godot;
using Simulation.Core;
using Simulation.Hosting;
using System.Reflection;

public partial class CityExperienceProbe
{
    private void Set(string name,object? value)=>typeof(Playfield).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(_app,value);
    private void Invoke(string name,params object[] args)=>typeof(Playfield).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(_app,args);
    private async Task Prepared(string folder,string name)
    {
        // Explicit fixture attachment, not normal-start evidence: the saved world was prepared
        // through ordinary Core commands by the G2 tests. All subsequent actions use real UI.
        _app.Notification((int)NotificationApplicationFocusOut);Invoke("DisposeModel");Invoke("ResetTablet",true);
        if(Field<SubViewportContainer>(_app,"_view") is {} old){old.GetParent().RemoveChild(old);old.QueueFree();Set("_view",null);Set("_stage",null);}
        var s=CheckpointStore.Load(System.IO.Path.Combine(folder,name+".save.json")).Session;s.SetPaused(true);
        long actor=long.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(folder,name+".actor")));
        Set("_session",s);Set("_scenario",s.Engine.Configuration());Set("_human",new ParticipantId(actor));Set("_scheduler",new TickScheduler(s));
        Set("_trackedId",null);Set("_trackedKind",null);Set("_pending",null);Set("_playerTab",0);Set("_storageCursor",0L);Set("_storageLossUntil",0L);
        var surface=typeof(Playfield).GetField("_surface",BindingFlags.Instance|BindingFlags.NonPublic)!;surface.SetValue(_app,Enum.Parse(surface.FieldType,"Play"));
        Invoke("LoadCityExperience",true);Invoke("CatchPublicCursor");Invoke("RebuildChrome");await Frames(5);
        await Click(Named<Button>("CityNotices"));_app.Notification((int)NotificationApplicationFocusIn);await Frames();Check(s.Paused,"Prepared inbox did not pause");
    }
    private async Task CommitPrepared()
    {
        // Accelerated whole active ticks, same normal command inbox and save/load path.
        var s=Session;string id=s.CommandTape.Last().Command.CommandId;var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));
        s.SetPaused(false);copy.SetPaused(false);int bound=10;
        while(s.Engine.Result(id) is null&&bound-->0){s.Step();copy.Step();}
        Check(bound>0,"Queued native command did not resolve");Check(s.Engine.ComputeStateHash()==copy.Engine.ComputeStateHash(),"Pending UI command diverges after restore");
        s.SetPaused(true);copy.SetPaused(true);_app.Notification((int)NotificationApplicationFocusOut);_app.Notification((int)NotificationApplicationFocusIn);await Frames(4);
    }
    private async Task FormsCase(string fixtures)
    {
        await StartMap("small-river",3);GetWindow().Size=new(800,760);GetWindow().ContentScaleSize=GetWindow().Size;await Frames();
        await Prepared(fixtures,"invoice");var s=Session;var bill=s.Engine.AccountOf(new(1)).Invoices.First();long cash=s.Engine.AccountOf(new(1)).Account.BalanceMinor;
        await Click(Named<Button>("NoticeOpen_bill_"+bill.Id));Check(!s.Engine.AccountOf(new(1)).Invoices.Single(b=>b.Id==bill.Id).Viewed,"Paused view mutated invoice immediately");
        await Number(Named<SpinBox>("AccountAmount"),"0.01");await Click(Named<Button>("TabletHelp"));await Press(Key.Escape);
        Check(Math.Abs(Named<SpinBox>("AccountAmount").Value-.01)<.00001,"Help reset payment draft");
        _app.Notification((int)NotificationApplicationFocusOut);_app.Notification((int)NotificationApplicationFocusIn);await Frames();Check(Math.Abs(Named<SpinBox>("AccountAmount").Value-.01)<.00001,"Refresh reset payment draft");
        await CommitPrepared();var viewed=s.Engine.AccountOf(new(1)).Invoices.Single(b=>b.Id==bill.Id);Check(viewed.Viewed&&viewed.Remaining>0,"View/pay states conflated");Check(s.Engine.AccountOf(new(1)).Account.BalanceMinor==cash,"Viewing charged money");
        await Click(Named<Button>("AccountPayInvoice"));await CommitPrepared();var paid=s.Engine.AccountOf(new(1)).Invoices.Single(b=>b.Id==bill.Id);Check(paid.PrincipalRemaining==bill.PrincipalRemaining-1,"Partial payment did not clear principal first");
        await Capture("invoice-"+_language);_checks.Add("invoice deep link, queued viewed flag, retained draft, actual partial payment and pending-command restore");

        await Prepared(fixtures,"pending-service");s=Session;var order=s.Engine.ServicesOf(new(2))!.OwnOrders.Single();long tick=s.Engine.Tick;
        await Click(Named<Button>("NoticeOpen_service_"+order.Id));await Click(Named<Button>("ServiceAccept_"+order.Id));Check(s.Engine.Tick==tick,"Viewing/queuing acceptance advanced paused time");
        Check(s.CommandTape.TakeLast(2).Select(c=>c.Command.Action.Kind).SequenceEqual(new[]{ActionKind.ViewDeliveryService,ActionKind.AcceptDeliveryService}),"View blocked immediate acceptance");
        await CommitPrepared();var accepted=s.Engine.ServicesOf(new(2))!.OwnOrders.Single();Check(accepted.State=="Accepted"&&accepted.CarrierViewed,"Actual request was not accepted/viewed");
        var quantity=Named<SpinBox>("ServiceQuantity");await Number(quantity,"7");
        _app.Notification((int)NotificationApplicationFocusOut);_app.Notification((int)NotificationApplicationFocusIn);await Frames();Check(Named<SpinBox>("ServiceQuantity").Value==7,"Live service update reset quantity");
        if(All(_app).OfType<SpinBox>().FirstOrDefault(c=>c.IsVisibleInTree()&&c.Name=="ServiceWage_"+order.Id) is {} wage)
        {
            await Number(wage,"37");s.SetPaused(false);for(int i=0;i<3;i++)s.Step();s.SetPaused(true);_app.Notification((int)NotificationApplicationFocusOut);_app.Notification((int)NotificationApplicationFocusIn);await Frames();
            Check(Named<SpinBox>("ServiceWage_"+order.Id).Value==37,"Service progress reset wage draft");_checks.Add("existing numeric-driver wage draft retained without applying a wage command");
        }
        await Capture("service-"+_language);_checks.Add("actual incoming request, view plus acceptance in one paused preparation, service quantity draft");

        await Prepared(fixtures,"service-history");await Click(Named<Button>("NoticeOpen_service_"+Session.Engine.ServicesOf(new(2))!.OwnOrders.Last().Id));
        await Number(Named<SpinBox>("ServiceQuantity"),"7");await Click(Named<Button>("ServiceOrdersOlder"));
        var rows=All(_app).OfType<VBoxContainer>().Count(n=>n.IsVisibleInTree()&&n.Name.ToString().StartsWith("ServiceOrder_",StringComparison.Ordinal));Check(rows>0&&rows<=20,"Order page is not bounded");Check(Named<SpinBox>("ServiceQuantity").Value==7,"Paging reset service draft");
        _checks.Add("older own service history is paged, draft quantity is retained");

        await Prepared(fixtures,"overflow");s=Session;var risk=s.Engine.Observe(new(2)).OwnInventories.SelectMany(i=>i.Overflow??[]).Single();string before=s.Engine.ComputeStateHash();
        await Click(Named<Button>("NoticeOpen_overflow_"+risk.Id));Check(s.Engine.ComputeStateHash()==before,"Reading overflow changed the economy");
        Check(!Field<CityNotice[]>(_app,"_cityNotices").Single(n=>n.Key=="overflow:"+risk.Id).Unread,"Read warning remains new");
        await Number(Named<SpinBox>("StorageQuantity"),"3");var rescue=All(_app).OfType<Button>().Single(b=>b.IsVisibleInTree()&&b.Text.StartsWith(_language=="ru"?"Спасти излишек:":"Rescue overflow:",StringComparison.Ordinal));await Click(rescue);await CommitPrepared();
        int bound2=40;s.SetPaused(false);while(s.Engine.Observe(new(2)).Participant.Busy&&bound2-->0)s.Step();s.SetPaused(true);_app.Notification((int)NotificationApplicationFocusOut);_app.Notification((int)NotificationApplicationFocusIn);await Frames();
        var remains=s.Engine.Observe(new(2)).OwnInventories.SelectMany(i=>i.Overflow??[]).Single();Check(remains.Quantity==risk.Quantity-3&&remains.ExpiresTick==risk.ExpiresTick,"Rescue erased or extended overflow");
        await Capture("overflow-"+_language);_checks.Add("overflow warning to its own storage card, local acknowledgement, physical partial rescue, unchanged expiry");

        await Prepared(fixtures,"built");s=Session;var ready=Field<CityNotice[]>(_app,"_cityNotices").Single(n=>n.Category=="work");before=s.Engine.ComputeStateHash();
        await Click(Named<Button>("NoticeOpen_"+ready.Key.Replace(':','_')));Check(s.Engine.ComputeStateHash()==before,"Opening completed work granted another reward");
        Check(All(_app).OfType<HouseholdObjectCard>().Any(c=>c.IsVisibleInTree()),"Completed work did not open the real object card");
        await Capture("work-"+_language);_checks.Add("real completed construction to its card, read state without repeated economic effect");

        await Prepared(fixtures,"road-built");s=Session;var road=Field<CityNotice[]>(_app,"_cityNotices").Single(n=>n.Target=="road");before=s.Engine.ComputeStateHash();
        await Click(Named<Button>("NoticeOpen_"+road.Key.Replace(':','_')));var cell=road.Position!.Value;
        Check(Field<TabletState>(_app,"_tabletState").Kind==TabletKind.CityMap,"Road completion did not open city map");
        var roadMap=Named<CityMapPanel>("FullCityMap");Check(roadMap.Canvas.State.SelectedKey==$"road:{cell.X}:{cell.Z}","Road notice lost its completed cell");
        var segment=Field<CityMapFrame>(roadMap,"_map").Objects.Single(o=>o.Key==roadMap.Canvas.State.SelectedKey);
        Check(segment.Contributions!.Select(c=>c.Builder.Value).SequenceEqual(new long[]{0,1}),"Road deep link lost city or private contribution");
        Check(s.Engine.ComputeStateHash()==before,"Road notification granted another reward");
        var details=Field<Label>(roadMap,"_details");Check(details.Text.Contains("90%")&&details.Text.Contains("100%"),"Road card does not explain retained revenue rights");await Reveal(details);await Capture("road-work-"+_language);
        _checks.Add("self-funded road completion notice, full-map deep link, retained city/private contributions and no repeated reward");
    }
}
