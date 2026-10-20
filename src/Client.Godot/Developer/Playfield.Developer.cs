using Godot;
using Simulation.Developer;

public partial class Playfield
{
    private DeveloperTools? _developer;
    private DeveloperPanel? _developerPanel;
    private Control? _developerOverlay;

    partial void DeveloperMenu(VBoxContainer menu)
    {
        var button = new Button { Text = L("Разработчик · режим бога и журнал", "Developer · god mode and journal"), FocusMode = FocusModeEnum.None };
        menu.AddChild(button);
        button.Pressed += OpenDeveloperPanel;
    }

    partial void DeveloperDebug(HBoxContainer toolbar)
    {
        var button = new Button { Name = "GodModeButton", Text = L("Режим бога", "God mode"), FocusMode = FocusModeEnum.None };
        toolbar.AddChild(button);
        button.Pressed += OpenDeveloperPanel;
    }

    private void OpenDeveloperPanel()
    {
        StopHeading();
        _waitRelease = true;
        if (_developerOverlay is null)
        {
            _developer = new(_session, _human);
            _developerOverlay = new ColorRect { Name = "DeveloperOverlay", Color = new Color(0.05f, 0.10f, 0.11f, 0.85f), MouseFilter = MouseFilterEnum.Stop };
            _developerOverlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            AddChild(_developerOverlay);
            _developerPanel = new(_developer, _session, _human, _language, Refresh) { Name = "DeveloperPanel" };
            _developerPanel.Closed += CloseDeveloperPanel;
            _developerOverlay.AddChild(_developerPanel);
        }
        _developerOverlay.Visible = true;
        _developerPanel!.RefreshData();
    }

    private void CloseDeveloperPanel()
    {
        _developer?.CancelAdvance();
        if (_developerOverlay is not null) _developerOverlay.Visible = false;
        _waitRelease = true;
    }

    partial void DeveloperInput(InputEventKey key, ref bool handled)
    {
        if (_developerOverlay is not { Visible: true }) return;
        handled = true;
        if (key.Pressed && !key.Echo && key.Keycode == Key.Escape) CloseDeveloperPanel();
    }

    partial void DeveloperFrame()
    {
        if (_developerOverlay is not { Visible: true } || _suspended || _unfocused || _helpOverlay is not null) return;
        long prior = _session.Engine.Tick;
        _developer?.PumpAdvance();
        _developerPanel!.RefreshData();
        if (_session.Engine.Tick != prior) Refresh();
    }

    partial void DeveloperReset()
    {
        _developer?.Dispose(); _developer = null;
        if (_developerOverlay is not null)
        {
            RemoveChild(_developerOverlay); _developerOverlay.QueueFree(); _developerOverlay = null;
        }
        _developerPanel = null;
    }
}
