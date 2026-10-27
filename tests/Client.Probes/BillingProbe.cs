using Godot;
using Simulation.Core;
using Simulation.Hosting;
using System.Text.Json;

/// <summary>Isolated ordinary E3 UI using actual paid travel/invoices and persisted commands.</summary>
public partial class BillingProbe : Control
{
    private GameSession _session=null!;private PersonalAccountPanel _panel=null!;private ScrollContainer _scroll=null!;
    private readonly List<string> _checks=[];
    private static IEnumerable<Node> All(Node root){yield return root;foreach(Node child in root.GetChildren())foreach(var node in All(child))yield return node;}
    private static void Check(bool value,string text){if(!value)throw new InvalidOperationException(text);}
    private async Task Frames(int n=3){for(int i=0;i<n;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
    private T Named<T>(string name)where T:Node=>All(this).OfType<T>().Single(n=>n.Name==name);
    private async Task Click(string name)
    {
        var button=Named<BaseButton>(name);_scroll.EnsureControlVisible(button);await Frames();Check(!button.Disabled&&button.IsVisibleInTree(),"Unavailable "+name);
        var point=button.GetGlobalRect().GetCenter();int window=GetWindow().GetWindowId();
        Input.ParseInputEvent(new InputEventMouseMotion{WindowId=window,Position=point,GlobalPosition=point});
        Input.ParseInputEvent(new InputEventMouseButton{WindowId=window,Position=point,GlobalPosition=point,ButtonIndex=MouseButton.Left,Pressed=true});await Frames(1);
        Input.ParseInputEvent(new InputEventMouseButton{WindowId=window,Position=point,GlobalPosition=point,ButtonIndex=MouseButton.Left,Pressed=false});await Frames();
    }
    private void Refresh()=>_panel.ShowData(_session.Engine.AccountOf(new(1)),false);
    private async Task Commit()
    {
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(_session));_session.SetPaused(false);copy.SetPaused(false);_session.Step();copy.Step();
        Check(_session.Engine.ComputeStateHash()==copy.Engine.ComputeStateHash(),"Native command restore differs");_session.SetPaused(true);Refresh();await Frames();
    }
    public override async void _Ready()
    {
        try
        {
            var args=OS.GetCmdlineUserArgs();string output=args[Array.IndexOf(args,"--output")+1],fixtures=args[Array.IndexOf(args,"--fixtures")+1];Theme=TabletDesign.Theme;
            _scroll=new(){HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};_scroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);AddChild(_scroll);
            foreach(string language in new[]{"ru","en"})
            {
                GetWindow().Size=language=="ru"?new(800,720):new(1100,800);GetWindow().ContentScaleSize=GetWindow().Size;
                foreach(Node child in _scroll.GetChildren()){_scroll.RemoveChild(child);child.QueueFree();}await Frames();
                _session=CheckpointStore.Decode(System.IO.File.ReadAllText(fixtures+"/before-penalty.save.json"));_session.SetPaused(true);
                _panel=new(language,a=>_session.Send(new(1),a),(offset,bills)=>_session.Engine.AccountOf(new(1),offset,20,bills)){SizeFlagsHorizontal=SizeFlags.ExpandFill};_scroll.AddChild(TabletDesign.Wrap(_panel));Refresh();await Frames();
                var invoices=_session.Engine.AccountOf(new(1)).Invoices;var invoice=invoices.First(i=>i.PrincipalRemaining>1);
                int selected=Array.FindIndex(invoices.ToArray(),i=>i.Id==invoice.Id);var selector=Named<OptionButton>("AccountInvoice");selector.Select(selected);selector.EmitSignal(OptionButton.SignalName.ItemSelected,(long)selected);await Frames();
                long id=invoice.Id,tick=_session.Engine.Tick,balance=_session.Engine.Observe(new(1)).Account.BalanceMinor;
                Check(!invoice.Viewed&&invoice.PrincipalRemaining>1,"Need actual new unpaid invoice");
                await Click("AccountViewInvoice");Check(!_session.Engine.AccountOf(new(1)).Invoices.Single(i=>i.Id==id).Viewed&&_session.Engine.Tick==tick,"Paused view click changed simulation");await Commit();
                Check(_session.Engine.AccountOf(new(1)).Invoices.Single(i=>i.Id==id).Viewed,"Viewed mark not persisted");
                Check(_session.Engine.Observe(new(1)).Account.BalanceMinor==balance,"Viewing paid a bill");
                Named<SpinBox>("AccountAmount").Value=0.01;await Click("AccountPayInvoice");Check(_session.Engine.Observe(new(1)).Account.BalanceMinor==balance,"Paused payment changed cash");await Commit();
                var partial=_session.Engine.AccountOf(new(1)).Invoices.Single(i=>i.Id==id);Check(partial.PrincipalRemaining==invoice.PrincipalRemaining-1&&partial.PenaltyMinor==invoice.PenaltyMinor,"Partial principal-first failed");
                string file=output+"/partial-"+language+".save.json";CheckpointStore.Save(_session,file);_session=CheckpointStore.Decode(System.IO.File.ReadAllText(file));_session.SetPaused(true);Refresh();await Frames();
                Check(_session.Engine.AccountOf(new(1)).Invoices.Single(i=>i.Id==id).PrincipalRemaining==partial.PrincipalRemaining,"Partial payment was repeated on load");
                await Click("AccountAutoPay");Check(!_session.Engine.AccountOf(new(1)).AutoPay,"Paused toggle changed saved preference");await Commit();
                Check(_session.Engine.AccountOf(new(1)).AutoPay&&_session.Engine.AccountOf(new(1)).DebtMinor==0,"Auto pay did not settle actual free funds");
                Check(Named<Button>("AccountPayInvoice").Disabled,"Paid invoice remains payable");
                _scroll.ScrollVertical=0;await Frames();await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);GetViewport().GetTexture().GetImage().SavePng(output+"/account-"+language+".png");
                _checks.Add(language+": native view, paused pending commands, 0.01 partial principal, real cash, disk restore, auto toggle and paid state");
            }
            System.IO.File.WriteAllText(output+"/billing-native.json",JsonSerializer.Serialize(new{status="succeeded",checks=_checks},SimulationJson.Options));
            foreach(Node child in GetChildren()){RemoveChild(child);child.QueueFree();}await Frames();GetTree().Quit();
        }
        catch(Exception ex){GD.PrintErr(ex);GetTree().Quit(2);}
    }
}
