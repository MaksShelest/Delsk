extends SceneTree
func walk(path):
    var result=[]
    var directory=DirAccess.open(path)
    if directory==null:return result
    for name in directory.get_files():result.append(path.path_join(name))
    for name in directory.get_directories():result.append_array(walk(path.path_join(name)))
    return result
func _initialize():
    var args=OS.get_cmdline_user_args()
    if not "--pack" in args or not "--report" in args:
        push_error("Expected --pack PATH --report PATH");quit(1);return
    var pack=args[args.find("--pack")+1]
    var report_path=args[args.find("--report")+1]
    if not ProjectSettings.load_resource_pack(pack):
        print("PACK_INSPECTION failed to load");quit(1);return
    var files=walk("res://")
    var forbidden=files.filter(func(p):return "developer/" in p.to_lower() or "simulation.developer" in p.to_lower() or "visualmodelsprobe" in p.to_lower() or "tabletdesignprobe" in p.to_lower() or "hudgodprobe" in p.to_lower() or "source.zip" in p.to_lower() or "demo.gd" in p.to_lower() or "/obj/" in p.to_lower())
    var license_present=FileAccess.file_exists("res://delsk_sawmill_ui/assets/fonts/OFL.txt")
    var sawmill_present=ResourceLoader.exists("res://delsk_sawmill_ui/SawmillTablet.tscn")
    var bakery_present=ResourceLoader.exists("res://delsk_bakery_ui/BakeryTablet.tscn")
    var audio_license=FileAccess.file_exists("res://audio/LICENSE.txt")
    var audio_present=ResourceLoader.exists("res://audio/music.wav")
    var kit_paths=["res://tablets_v1/screens/dairy.tscn","res://tablets_v1/screens/sugar_factory.tscn","res://tablets_v1/screens/confectionery.tscn","res://tablets_v1/theme.tres","res://tablets_v1/assets/icons/stepper.svg"]
    var kit_present=kit_paths.all(func(p):return ResourceLoader.exists(p)) and FileAccess.file_exists("res://tablets_v1/LICENSE.txt")
    var report={"kit_present":kit_present,"bakery_present":bakery_present,"audio_license":audio_license,"audio_present":audio_present,"license_present":license_present,"sawmill_present":sawmill_present,"files":files,"forbidden":forbidden,"playfield_present":ResourceLoader.exists("res://Playfield.tscn")}
    var out=FileAccess.open(report_path,FileAccess.WRITE)
    out.store_string(JSON.stringify(report,"  "));print("PACK_INSPECTION files=",files.size()," kit=",kit_present," forbidden=",forbidden.size())
    quit(0 if forbidden.is_empty() and report.playfield_present and license_present and sawmill_present and bakery_present and audio_license and audio_present and kit_present else 1)
