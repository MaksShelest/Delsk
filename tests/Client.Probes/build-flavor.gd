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
    var args=OS.get_cmdline_user_args()
    var expected=args[args.find("--expect-debug-ui")+1]=="yes"
    root.add_child(load("res://Playfield.tscn").instantiate())
    await process_frame
    await process_frame
    await press("Продолжить")
    await press("Продолжить")
    await press("Меню")
    for language in ["ru","en"]:
        var debug=buttons("Debug-режим" if language=="ru" else "Debug mode")
        if (not debug.is_empty())!=expected:failures.append("Debug menu presence mismatch in "+language)
        if expected and not debug.is_empty():
            debug[0].emit_signal("pressed")
            await process_frame
            await process_frame
            var hint=nodes(root).filter(func(n):return n.name=="DebugBuildHint" and n.is_visible_in_tree())
            if hint.is_empty():failures.append("Ordinary Debug lacks Developer build explanation")
            if not buttons("Режим бога" if language=="ru" else "God mode").is_empty():failures.append("God mode leaked to ordinary Debug")
            await capture("ordinary-debug-"+language)
            var escape=InputEventKey.new();escape.keycode=KEY_ESCAPE;escape.pressed=true
            Input.parse_input_event(escape)
            await process_frame
            await process_frame
            escape=InputEventKey.new();escape.keycode=KEY_ESCAPE;escape.pressed=false
            Input.parse_input_event(escape)
        else:await capture("release-menu-"+language)
        if language=="ru":await press("EN")
    var report=FileAccess.open(folder+"/window.json",FileAccess.WRITE)
    report.store_string(JSON.stringify({"failures":failures,"expectDebugUi":expected,"checks":["Actual menu visibility RU/EN", "Ordinary Debug explanation and no intervention controls" if expected else "Release has no Debug menu"]},"  "))
    print("BUILD_FLAVOR "+JSON.stringify(failures))
    quit(0 if failures.is_empty() else 1)
