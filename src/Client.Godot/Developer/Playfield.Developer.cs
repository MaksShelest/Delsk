using Godot;
using Simulation.Developer;

public partial class Playfield
{
    private DeveloperTools? _developer;
    private DeveloperPanel? _developerPanel;
    private Control? _developerOverlay;
    private ulong _nextAdvanceRefresh;

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
        ApplyPause();
        _developerPanel!.RefreshData();
    }

    private void CloseDeveloperPanel()
    {
        _developer?.CancelAdvance();
        if (_developerOverlay is not null) _developerOverlay.Visible = false;
        _waitRelease = true;
        ApplyPause();
        Refresh();
    }

    partial void DeveloperInput(InputEventKey key, ref bool handled)
    {
        if (_developerOverlay is not { Visible: true }) return;
        handled = true;
        if (key.Pressed && !key.Echo && key.Keycode == Key.Escape) CloseDeveloperPanel();
    }

    partial void DeveloperFrame()
    {
        if (_developerOverlay is not { Visible: true } || _suspended || _helpOverlay is not null || _developer?.AdvanceTarget is null) return;
        long prior = _session.Engine.Tick;
        string state = _developer.AdvanceStatus;
        _developer.PumpAdvance();
        ulong now = Time.GetTicksMsec();
        if (now >= _nextAdvanceRefresh || state != _developer.AdvanceStatus)
        {
            _nextAdvanceRefresh = now + 200;
            _developerPanel!.RefreshAdvance();
            if (_session.Engine.Tick != prior || state != _developer.AdvanceStatus) Refresh();
        }
    }

    partial void DeveloperOwnsClock(ref bool overlay, ref bool advancing)
    {
        overlay = _developerOverlay is { Visible: true };
        advancing = overlay && _developer?.AdvanceTarget.HasValue == true;
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
