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
var checks=[]
func frames():
    for i in range(4):await process_frame
func chord(code=KEY_ENTER,pressed=true,echo=false,alt=true):
    var key=InputEventKey.new()
    key.keycode=code;key.physical_keycode=code;key.pressed=pressed;key.echo=echo;key.alt_pressed=alt
    Input.parse_input_event(key)
    await frames()
func is_full():return DisplayServer.window_get_mode() in [DisplayServer.WINDOW_MODE_FULLSCREEN,DisplayServer.WINDOW_MODE_EXCLUSIVE_FULLSCREEN]
func preference(expected):
    var config=ConfigFile.new()
    if config.load("user://display.cfg")!=OK or config.get_value("display","fullscreen",not expected)!=expected:failures.append("Display preference not saved")
func fills_window():
    # Default keep would leave translation/letterboxing at a mismatched aspect.
    var filled=root.get_stretch_transform()*root.get_visible_rect()
    var size=Vector2(DisplayServer.window_get_size())
    if filled.position.length()>2 or filled.size.distance_to(size)>2:failures.append("Letterbox: "+str(filled)+" window="+str(size))
    var hud=nodes(root).filter(func(n):return n is PanelContainer and n.is_visible_in_tree() and n.get_global_rect().position.y==0 and n.get_global_rect().size.y==54)
    if not hud.is_empty() and abs(hud[0].get_global_rect().size.x-root.get_visible_rect().size.x)>2:failures.append("HUD fails to fill viewport")
func start_scene():
    root.add_child(load("res://Playfield.tscn").instantiate())
    await frames()
func drop_scene():
    var play=nodes(root).filter(func(n):return n.get_script()!=null and str(n.get_script().resource_path).ends_with("Playfield.cs"))[0]
    root.remove_child(play);play.queue_free()
    await frames()
func check_full():
    if not is_full():failures.append("Alt+Enter failed to enter fullscreen")
    await create_timer(.2).timeout
    if DisplayServer.window_get_size()!=DisplayServer.screen_get_size():failures.append("Fullscreen does not cover screen")
    fills_window();preference(true)
func run():
    DirAccess.make_dir_recursive_absolute(folder)
    await start_scene()
    if is_full():failures.append("Windowed preference ignored")
    for size in [Vector2i(1280,720),Vector2i(1000,800),Vector2i(800,600)]:
        root.size=size
        await frames()
        fills_window()
    checks.append("expand viewport fills three window aspects without letterbox")
    var hybrid=buttons("Гибрид · локальная модель")[0]
    hybrid.button_pressed=true
    await frames()
    var field=nodes(root).filter(func(n):return n is LineEdit and n.is_visible_in_tree())
    if field.is_empty():failures.append("Focused form case not exercised")
    else:field[0].grab_focus()
    await chord(KEY_ENTER,true,false,false)
    await chord(KEY_ENTER,false,false,false)
    if is_full():failures.append("Plain Enter toggled fullscreen")
    await chord()
    await check_full()
    await chord(KEY_ENTER,true,true)
    await chord(KEY_ENTER,false)
    if not is_full():failures.append("Echo or release toggled fullscreen")
    await capture("fullscreen-maps-ru")
    if not field.is_empty() and field[0].text!="qwen3.5:4b":failures.append("Shortcut edited focused field")
    hybrid.button_pressed=false
    checks.append("Alt+Enter before focused form; plain Enter, echo and release guarded")
    # Restore actual fullscreen from saved preferences when the host is recreated.
    DisplayServer.window_set_mode(DisplayServer.WINDOW_MODE_WINDOWED)
    await drop_scene()
    await start_scene()
    await check_full()
    checks.append("Fullscreen preference restored after host restart")
    await chord(KEY_KP_ENTER)
    await chord(KEY_KP_ENTER,false)
    if is_full():failures.append("Alt+keypad Enter failed to exit")
    preference(false)
    await drop_scene()
    await start_scene()
    if is_full():failures.append("Windowed preference not restored")
    checks.append("Keypad exit and windowed preference restored")
    await press("Продолжить")
    await press("Продолжить")
    await press("Открыть карточку предприятия")
    var before=JSON.parse_string(world().worldJson)
    await chord()
    await check_full()
    await chord(KEY_ENTER,false)
    var after=JSON.parse_string(world().worldJson)
    for key in ["tick","accounts","inventories","enterprises","population","ledger"]:
        if before[key]!=after[key]:failures.append("Fullscreen changed paused world: "+key)
    await capture("fullscreen-production-ru")
    checks.append("Production tablet fullscreen keeps queued commands and paused economy unchanged")
    await press("?")
    await chord()
    await chord(KEY_ENTER,false)
    if is_full():failures.append("Help blocked fullscreen shortcut")
    var close=nodes(root).filter(func(n):return n.name=="HelpClose" and n.is_visible_in_tree())
    if close.is_empty():failures.append("Help lost while toggling display")
    else:close[0].emit_signal("pressed")
    await frames()
    checks.append("Help overlay shortcut preserves the open overlay")
    await press("Меню")
    await press("EN")
    await chord()
    await check_full()
    await chord(KEY_ENTER,false)
    await capture("fullscreen-menu-en")
    await press("Continue")
    await press("Tablet")
    await frames()
    fills_window()
    await capture("fullscreen-player-en")
    checks.append("Menu and English player tablet fill fullscreen")
    var report=FileAccess.open(folder+"/window.json",FileAccess.WRITE)
    report.store_string(JSON.stringify({"failures":failures,"checks":checks,"screen":DisplayServer.screen_get_size(),"scope":"Actual Linux X11 window/input and real Playfield; not Windows/Android hardware acceptance"},"  "))
    print("FULLSCREEN_WINDOW "+JSON.stringify(failures))
    quit(0 if failures.is_empty() else 1)
