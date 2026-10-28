using Godot;
using Simulation.Core;
using Simulation.Hosting;
public partial class ServicePanel
{
    private void ShowRun(Container row,PlayerView view,ServiceOrder order,ServiceRunView run,ServiceExecutionObservation execution)
    {
        var r=run.Run;bool carrier=order.Carrier==view.Participant.Id;
        var bar=new ProgressBar{Name="ServiceDelivered_"+order.Id,MaxValue=order.Quote.Terms.Quantity,Value=r.Delivered,ShowPercentage=true};row.AddChild(bar);
        row.AddChild(new Label{Name="ServiceProgress_"+order.Id,AutowrapMode=TextServer.AutowrapMode.WordSmart,Text=
            L("Рейс ","Trip ")+r.TripNumber+" / "+order.Quote.Trips+L(" · доставлено "," · delivered ")+r.Delivered+" / "+order.Quote.Terms.Quantity+
            L(" · в кузове "," · aboard ")+r.Cargo+L(" · у источника "," · at source ")+run.SourceRemaining+L(" · возвращено "," · returned ")+r.Returned+"\n"+
            Stage(r.Stage)+" · "+TabletText.Reason(_language,r.Reason)+"\n"+L("Машина ","Vehicle ")+Coordinates(run.Position)+" → "+Coordinates(run.Target)+
            L(" · возврат: #"," · return: #")+r.ReturnInventory.Value+"\n"+L("Выплачено ","Paid ")+PlayerText.Money(r.PaidMinor)+L(" · резерв "," · escrow ")+PlayerText.Money(run.FeeRemaining)+
            L(" · возвращено денег "," · refunded ")+PlayerText.Money(r.RefundedMinor)+L(". Просрочка уменьшает только позднюю часть на 20%.",". Only late receipts lose 20%.")});
        if(order.State!="Accepted")return;
        bool handling=r.Stage is "Loading" or "Unloading" or "UnloadingReturn";
        if(carrier&&view.Fleet?.Vehicles.SingleOrDefault(v=>v.Vehicle.Id==run.Vehicle) is {} car)
        {
            row.AddChild(new Label{Text=run.Automatic?L("Числовой водитель: ","Numeric driver: ")+run.Headcount+" / 1":L("Личное управление владельцем","Owner drives personally")});
            if(car.Vehicle.Driver==0&&(!handling||run.Automatic))
            {
                Button(row,"ServiceAutomation_"+order.Id,run.Automatic?L("Остановить числовую команду","Stop numeric team"):L("Назначить числового водителя (найм и зарплата)","Assign numeric driver (hiring and wages)"),new(ActionKind.SetServiceAutomation,VehicleId:run.Vehicle,Enabled:!run.Automatic));
                if(!run.Automatic)
                {
                    if(view.Participant.Position!=car.Position)Button(row,"ServiceWalk_"+order.Id,L("Подойти к машине ","Walk to vehicle ")+Coordinates(car.Position),new(ActionKind.MoveTo,Target:car.Position));
                    else Button(row,"ServiceBoard_"+order.Id,L("Сесть за руль","Board vehicle"),new(ActionKind.BoardVehicle,VehicleId:run.Vehicle));
                }
            }
            if(execution.Crews.SingleOrDefault(c=>c.Vehicle==run.Vehicle) is {} crew)
            {
                row.AddChild(new Label{Text=L("Зарплата за рабочий тик: ","Wage per working tick: ")+PlayerText.Money(crew.Group.WageMinor)+" · "+TabletText.Reason(_language,crew.Group.ReadinessReason)});
                var wage=new SpinBox{Name="ServiceWage_"+order.Id,MinValue=1,MaxValue=1000000,Value=crew.Group.PendingWageMinor??crew.Group.WageMinor};row.AddChild(wage);
                var change=new Button{Name="ServiceSetWage_"+order.Id,Text=L("Изменить зарплату у машины","Set wage at vehicle")};change.Pressed+=()=>_submit(new(ActionKind.SetWage,Group:crew.Group.Id,WageMinor:(long)wage.Value));row.AddChild(change);_buttons.Add((change,new(ActionKind.SetWage,Group:crew.Group.Id)));
            }
            if(!run.Automatic&&car.Vehicle.Driver==view.Participant.Id.Value&&!view.Participant.Busy)
            {
                if(handling)Button(row,"ServiceStop_"+order.Id,L("Остановить погрузку/разгрузку","Stop handling"),new(ActionKind.StopDeliveryServiceHandling,ServiceId:order.Id));
                else
                {
                    Button(row,"ServiceNavigate_"+order.Id,L("Маршрут к следующей точке ","Navigate to next stop ")+Coordinates(run.Target),new(ActionKind.NavigateDeliveryService,ServiceId:order.Id));
                    if(run.AtTarget)Button(row,(r.Cargo>0?"ServiceUnload_":"ServicePickup_")+order.Id,r.Cargo>0?L("Выложить","Unload"):L("Забрать","Pick up"),new(r.Cargo>0?ActionKind.UnloadDeliveryService:ActionKind.PickupDeliveryService,ServiceId:order.Id));
                    Button(row,"ServiceLeave_"+order.Id,L("Выйти из машины","Leave vehicle"),new(ActionKind.LeaveVehicle,VehicleId:run.Vehicle));
                }
            }
        }
        if(r.Cancellation=="")
        {
            Button(row,"ServiceCancel_"+order.Id,L("Отменить: вернуть текущий груз","Cancel: return current cargo"),new(ActionKind.CancelDeliveryService,ServiceId:order.Id));
            if(r.Cargo>0)Button(row,"ServiceFinishCurrent_"+order.Id,L("Отменить следующие рейсы, довезти текущий","Cancel further trips, deliver current cargo"),new(ActionKind.CancelDeliveryService,ServiceId:order.Id,Enabled:true));
        }
        if(!carrier&&view.Tick>order.DeadlineTick+execution.Rules.ForceCloseAfterTicks)
            Button(row,"ServiceForceClose_"+order.Id,L("Потребовать закрытия и возврата остатка","Demand closure and return remainder"),new(ActionKind.ForceCloseDeliveryService,ServiceId:order.Id));
    }
    private string Stage(string stage)=>stage switch
    {
        "AwaitingPickup" or "ToSource"=>L("К источнику","To source"),"Loading"=>L("Погрузка","Loading"),"ToDestination" or "AwaitingUnload"=>L("К получателю","To recipient"),
        "Unloading"=>L("Разгрузка","Unloading"),"Returning"=>L("Возврат груза","Returning cargo"),"UnloadingReturn"=>L("Возврат: разгрузка","Unloading return"),"Completed"=>L("Выполнен","Completed"),_=>L("Закрыт","Closed")
    };
}
