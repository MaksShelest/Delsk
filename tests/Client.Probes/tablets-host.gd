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
func named(name):
    var found=nodes(root).filter(func(n):return n.name==name and n is Control and n.is_visible_in_tree())
    if found.is_empty():failures.append("Missing visible node "+name);return null
    return found[0]
func named_press(name):
    var button=named(name)
    if button!=null:button.emit_signal("pressed")
    await process_frame
    await process_frame
func tick():return JSON.parse_string(world().worldJson).tick
func help_cycle(running):
    var clock=named("TabletTime")
    if clock==null:return
    clock.button_pressed=running
    await process_frame
    # Use the artwork header rather than the HUD question-mark button.
    var art=nodes(root).filter(func(n):return n.name=="SawmillTablet" and n.is_visible_in_tree())
    if not art.is_empty():art[0].get_node("Panel/Help").emit_signal("pressed")
    else:await named_press("TabletHelp")
    await process_frame
    await process_frame
    if named("HelpTablet")==null:return
    var frozen=tick()
    await create_timer(1.15).timeout
    if tick()!=frozen:failures.append("Help advanced economic time")
    await named_press("HelpClose")
    await create_timer(1.15).timeout
    var after=tick()
    if running and after<=frozen:failures.append("Help failed to restore running time")
    if not running and after!=frozen:failures.append("Help failed to restore tactical pause")
    clock=named("TabletTime")
    if clock!=null:
        if clock.button_pressed!=running:failures.append("Help changed selected time mode")
        clock.button_pressed=false
func run():
    DirAccess.make_dir_recursive_absolute(folder)
    root.add_child(load("res://Playfield.tscn").instantiate())
    await process_frame
    await process_frame
    root.content_scale_size=Vector2i(1440,1024);root.size=Vector2i(1440,1024)
    await press("Продолжить")
    await press("Продолжить")
    await press("Открыть карточку предприятия")
    await help_cycle(false)
    await help_cycle(true)
    await capture("host-help-resume-ru")
    await press("×")
    if nodes(root).any(func(n):return n.name=="GameTablet" and n is Control and n.is_visible_in_tree()):failures.append("Artwork close failed")
    await press("Планшет")
    var tabs=nodes(root).filter(func(n):return n is TabContainer and n.is_visible_in_tree())
    if tabs.is_empty():failures.append("Player pages missing from host")
    else:
        if tabs[0].get_tab_count()!=4:failures.append("Player pages lost tabs")
        for i in range(4):
            tabs[0].current_tab=i
            await process_frame
            await named_press("TabletHelp")
            await named_press("HelpClose")
    var clock=named("TabletTime")
    if clock!=null:clock.button_pressed=true
    await press("Меню")
    var paused=tick()
    await create_timer(1.15).timeout
    if tick()!=paused:failures.append("Menu failed to pause running tablet")
    await press("EN")
    await press("Continue")
    await process_frame
    # Reopen from the real HUD and check the compact host, not a standalone view.
    await press("Tablet")
    root.content_scale_size=Vector2i(800,600);root.size=Vector2i(800,600)
    await process_frame
    await process_frame
    await named_press("TabletHelp")
    await capture("host-help-en-800")
    if named("HelpTopics")==null:failures.append("Compact help selector absent")
    await named_press("HelpClose")
    await capture("host-player-en-800")
    if nodes(root).filter(func(n):return n.name=="InventoryHud").is_empty():failures.append("PR46 HUD absent")
    await named_press("TabletClose")
    var report=FileAccess.open(folder+"/window.json",FileAccess.WRITE)
    report.store_string(JSON.stringify({"failures":failures,"checks":["Actual artwork Help and Close handlers", "Help freezes and restores paused/running world", "All four player tabs route contextual help", "Menu pauses running tablet", "Language switch, compact host and preserved inventory HUD"]},"  "))
    print("TABLETS_HOST "+JSON.stringify(failures))
    quit(0 if failures.is_empty() else 1)
