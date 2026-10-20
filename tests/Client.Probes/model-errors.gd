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
func press(text):
    var found=nodes(root).filter(func(n):return n is Button and n.is_visible_in_tree() and n.text==text)
    if found.is_empty():failures.append("Missing button "+text);return
    if found[0].disabled:failures.append("Disabled "+text);return
    found[0].emit_signal("pressed")
    await process_frame
    await process_frame
func capture(name):
    await process_frame
    await RenderingServer.frame_post_draw
    root.get_texture().get_image().save_png(folder+"/"+name+".png")
func run():
    DirAccess.make_dir_recursive_absolute(folder)
    root.add_child(load("res://Playfield.tscn").instantiate())
    await process_frame
    await process_frame
    await capture("settings")
    var choices=nodes(root).filter(func(n):return n is CheckButton and n.text=="Гибрид · локальная модель")
    if choices.is_empty():failures.append("Missing hybrid selector")
    else:choices[0].button_pressed=true
    await press("Проверить runtime и модель")
    await create_timer(.3).timeout
    await press("Начать")
    await create_timer(3).timeout
    var retry="Повторить запрос (одна попытка)"
    await capture("error")
    await press(retry)
    await create_timer(.5).timeout
    await capture("retry")
    await press("Продолжить алгоритмом · до конца партии")
    await create_timer(1.2).timeout
    await capture("fallback")
    await press("Меню")
    # Exit saves the real session. The parent verifies saved metadata/trace.
    var report=FileAccess.open(folder+"/window.json",FileAccess.WRITE)
    report.store_string(JSON.stringify({"failures":failures,"fixture":"HTTP tags succeeds; inference returns503, no real model"},"  "))
    print("PACKAGE6_MODEL_WINDOW "+JSON.stringify(failures))
    await press("Выйти из игры")
    quit(0 if failures.is_empty() else 1)
