using Godot;
using Simulation.Hosting;

public partial class Playfield
{
    private Label? _storageWarning;
    private long _storageCursor,_storageLossUntil;
    private string _storageLoss="";
    private void StorageNotice(PlayerView view)
    {
        if(_storageWarning is null)return;
        foreach(var e in _session.Engine.StorageEventsOf(_human,_storageCursor))
        {_storageCursor=e.Sequence;if(e.Type=="OverflowLost"){_storageLoss=L("Потерян излишек: ","Overflow lost: ")+PlayerText.Item(_language,e.ItemId)+" ×"+e.Quantity;_storageLossUntil=view.Tick+30;}}
        var lots=view.Inventories.SelectMany(i=>i.Overflow??[]).ToArray();
        _storageWarning.Text=lots.Length>0?L("Переполнение! Откройте склад и освободите место или вывезите товар. До первой потери: ","Overflow! Open storage and free space or move goods. First loss in: ")+Math.Max(0,lots.Min(l=>l.ExpiresTick)-view.Tick)+L(" активных тиков."," active ticks."):view.Tick<_storageLossUntil?_storageLoss:"";
        _storageWarning.Visible=_storageWarning.Text.Length>0&&_surface==Surface.Play;
    }
}
