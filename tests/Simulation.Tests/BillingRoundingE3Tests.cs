using Simulation.Core;
using Simulation.Hosting;
using Xunit;
using static Simulation.Tests.BillingE3Tests;

namespace Simulation.Tests;

public class BillingRoundingE3Tests
{
    [Fact]public void EveryWholeCentIsBilledAndTenSmallPrivatePassagesPreserveNinetyTenWithSignedRemainders()
    {
        var s=new GameSession(new(Prepared()));Step(s,new(ActionKind.SetAutoPay,Enabled:false));Step(s,new(ActionKind.SetAutoPay,Enabled:false),2);
        var paver=RoadE2Tests.Paver(s);Step(s,RoadE2Tests.Work(paver));s.AdvanceTo(s.Engine.Tick+24);StorageC1Tests.Move(s,new(14,2));Step(s,new(ActionKind.LeaveVehicle));
        Car(s,"sputnik20",2);StorageC1Tests.Move(s,new(10,1),2);s.AdvanceTo((s.Engine.Tick/120+1)*120);
        long first=s.Engine.InspectBilling()!.Invoices.Where(i=>i.Payer.Value==2).Sum(i=>i.PrincipalMinor);
        long city=s.Engine.InspectBilling()!.Invoices.Where(i=>i.Payer.Value==2).SelectMany(i=>i.Claims).Where(c=>c.Recipient.Value==0).Sum(c=>c.PrincipalMinor);
        long builder=s.Engine.InspectBilling()!.Invoices.Where(i=>i.Payer.Value==2).SelectMany(i=>i.Claims).Where(c=>c.Recipient.Value==1).Sum(c=>c.PrincipalMinor);
        for(int n=0;n<10;n++)
        {
            StorageC1Tests.Move(s,new(11,1),2);StorageC1Tests.Move(s,new(10,1),2);s.AdvanceTo((s.Engine.Tick/120+1)*120);
            Assert.Equal(first+3*(n+1),s.Engine.InspectBilling()!.Invoices.Where(i=>i.Payer.Value==2).Sum(i=>i.PrincipalMinor));
            Assert.Equal(0,s.Engine.InspectBilling()!.Accruals.Where(a=>a.Payer.Value==2).Sum(a=>a.Units));
        }
        var claims=s.Engine.InspectBilling()!.Invoices.Where(i=>i.Payer.Value==2).SelectMany(i=>i.Claims).ToArray();
        Assert.Equal(city+21,claims.Where(c=>c.Recipient.Value==0).Sum(c=>c.PrincipalMinor));Assert.Equal(builder+9,claims.Where(c=>c.Recipient.Value==1).Sum(c=>c.PrincipalMinor));
        Assert.All(s.Engine.InspectBilling()!.Accruals.Where(a=>a.Payer.Value==2),a=>Assert.Equal(0,a.Units));Exact(s,"small-rounding");
    }
}
