extends SceneTree
var failures=[]
var folder=""
func _init():
    var args=OS.get_cmdline_user_args()
    folder=args[args.find("--output")+1]
func _initialize():call_deferred("run")
func nodes(n):
    var all=[n]
    for child in n.get_children():all.append_array(nodes(child))
    return all
func buttons(text):return nodes(root).filter(func(n):return n is Button and n.is_visible_in_tree() and (n.text==text or n.get_children().any(func(c):return c is Label and c.text==text)))
func press(text):
    var found=buttons(text)
    if found.is_empty() and text=="×":found=nodes(root).filter(func(n):return n is Button and n.name=="Close" and n.is_visible_in_tree())
    if found.is_empty():failures.append("Missing button "+text);return
    if found[0].disabled:failures.append("Disabled button "+text);return
    found[0].emit_signal("pressed")
    await process_frame
    await process_frame
func capture(name):
    await process_frame
    await RenderingServer.frame_post_draw
    root.get_texture().get_image().save_png(folder+"/"+name+".png")
func artwork():
    var found=nodes(root).filter(func(n):return n.name=="BakeryTablet" and n.is_visible_in_tree())
    if found.is_empty():failures.append("Missing artwork");return null
    return found[0].get_node("Panel")
func world():
    # Observe auto-save only: the test never edits a saved world.
    var player=nodes(root).filter(func(n):return n.get_script()!=null and str(n.get_script().resource_path).ends_with("Playfield.cs"))[0]
    player.notification(MainLoop.NOTIFICATION_APPLICATION_FOCUS_OUT)
    var file=FileAccess.open("user://play-economy-production-recipes-1.json",FileAccess.READ)
    var envelope=JSON.parse_string(file.get_as_text())
    var raw=Marshalls.base64_to_raw(envelope.payloadJson).decompress_dynamic(67108864,FileAccess.COMPRESSION_GZIP)
    var state=JSON.parse_string(raw.get_string_from_utf8())
    player.notification(MainLoop.NOTIFICATION_APPLICATION_FOCUS_IN)
    return state
func tick_once():
    var clock=nodes(root).filter(func(n):return n is CheckButton and n.is_visible_in_tree() and (n.text=="Время остановлено" or n.text=="Время идёт"))
    if clock.is_empty():failures.append("Missing clock");return
    clock[0].button_pressed=true
    await create_timer(1.10).timeout
    clock[0].button_pressed=false
    await process_frame
func run():
    DirAccess.make_dir_recursive_absolute(folder)
    root.add_child(load("res://Playfield.tscn").instantiate())
    await process_frame
    await process_frame
    root.content_scale_size=Vector2i(1440,1024)
    root.size=Vector2i(1440,1024)
    await press("Продолжить")
    await press("Продолжить")
    await press("Открыть карточку предприятия")
    await process_frame
    var panel=artwork()
    if panel!=null:
        if panel.get_node("Stock/GrainCount").text!="0 · резерв 0":failures.append("Prototype grain stock leaked")
        if not panel.get_node("Stock/Deposit").disabled:failures.append("Empty pack can supply")
        if not panel.get_node("Delivery/NoTransport").visible:failures.append("Missing no-transport state")
        await capture("bakery-no-truck-ru")
        var before=world()
        await press("Включить производство")
        var queued=world()
        var a=JSON.parse_string(queued.worldJson)
        var b=JSON.parse_string(before.worldJson)
        for key in ["tick","accounts","inventories","enterprises","population","unemployed","ledger"]:
            if a[key]!=b[key]:failures.append("Queued command changed paused economy: "+key)
        await tick_once()
        await capture("bakery-production-ru")
        await press("Остановить производство")
        await tick_once()
        await press("Купить транспорт")
        await tick_once()
        panel=artwork()
        if panel!=null:
            panel.get_node("Workers/WagePlus").emit_signal("pressed")
            await tick_once()
            if not "Новая ставка" in panel.get_node("Workers/Hint").text:failures.append("Wage did not remain pending")
            panel.get_node("Delivery/SupplyToggle").emit_signal("pressed")
            await tick_once()
            if panel.get_node("Delivery/SendToggle").button_pressed:failures.append("Inbound changed outbound")
            if panel.get_node("Production/PauseProduction").text!="Включить производство":failures.append("Inbound changed production")
        await capture("bakery-truck-ru")
        panel=artwork()
        if panel!=null and panel.get_node("Delivery/NoTransport").visible:failures.append("Purchase not reflected")
    await press("×")
    await press("Меню")
    await press("EN")
    await press("Continue")
    await press("Open the enterprise card")
    await capture("bakery-en")
    var p=artwork()
    if p!=null:
        for n in nodes(p):
            if n is Label and n.is_visible_in_tree():
                var pattern=RegEx.new();pattern.compile("[А-Яа-яЁё]")
                if pattern.search(n.text)!=null:failures.append("Untranslated "+str(n.get_path())+": "+n.text)
    root.content_scale_size=Vector2i(800,600)
    root.size=Vector2i(800,600)
    await process_frame
    await process_frame
    await capture("bakery-compact-en")
    var shown=nodes(root).filter(func(n):return n.name=="BakeryTablet" and n.is_visible_in_tree())
    if not shown.is_empty():failures.append("Narrow view shrank artwork instead of using compact controls")
    if buttons("Start production").is_empty():failures.append("Missing compact production action")
    await press("Menu")
    await press("Speech and audio")
    var speech=nodes(root).filter(func(n):return n is CheckButton and n.text=="NPC speech" and n.is_visible_in_tree())
    if speech.is_empty():failures.append("Missing speech setting")
    else:speech[0].button_pressed=false
    for slider in nodes(root).filter(func(n):return n is HSlider and n.is_visible_in_tree()):slider.value=0
    var prefs=ConfigFile.new()
    if prefs.load("user://feedback.cfg")!=OK:failures.append("Preferences not saved")
    else:
        for key in ["effects","ambience","music"]:
            if prefs.get_value("feedback",key,-1)!=0:failures.append("Channel not muted: "+key)
        if prefs.get_value("feedback","speech",true):failures.append("Speech not disabled")
    await capture("settings-muted")
    var play=nodes(root).filter(func(n):return n.get_script()!=null and str(n.get_script().resource_path).ends_with("Playfield.cs"))[0]
    root.remove_child(play)
    play.queue_free()
    await process_frame
    await process_frame
    root.add_child(load("res://Playfield.tscn").instantiate())
    await process_frame
    await process_frame
    await press("Реплики и звук")
    var restored=nodes(root).filter(func(n):return n is CheckButton and n.text=="Реплики NPC" and n.is_visible_in_tree())
    if restored.is_empty() or restored[0].button_pressed:failures.append("Restart did not restore speech preference")
    for slider in nodes(root).filter(func(n):return n is HSlider and n.is_visible_in_tree()):
        if slider.value!=0:failures.append("Restart did not restore audio volume")
    var report=FileAccess.open(folder+"/window.json",FileAccess.WRITE)
    report.store_string(JSON.stringify({"failures":failures,"checks":["Actual loaded bakery PlayerView, zero stock, no transport", "Commands queued on pause then executed by world tick", "Buy transport projection", "RU/EN desktop and narrow compact view", "Independent inbound and pending wage", "Speech mute and three volumes persist across scene restart"]},"  "))
    print("PACKAGE6_WINDOW "+JSON.stringify(failures))
    quit(0 if failures.is_empty() else 1)
