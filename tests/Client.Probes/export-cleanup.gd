extends SceneTree
# Tests the cleanup boundary with dependency manifests from actual Windows publishes.
var failures: Array[String] = []
var checks: Array[String] = []
func require(value: bool, message: String) -> void:
    if not value: failures.append(message)
func _initialize() -> void:
    call_deferred("run")
func run() -> void:
    for i in range(4): await process_frame
    var args := OS.get_cmdline_user_args()
    var output := args[args.find("--output") + 1]
    var debug_deps := FileAccess.get_file_as_string(args[args.find("--debug-deps") + 1])
    var release_deps := FileAccess.get_file_as_string(args[args.find("--release-deps") + 1])
    var folder := output.path_join("data_Delsk.Client_windows_x86_64")
    DirAccess.make_dir_recursive_absolute(folder)
    for name in ["Delsk.Client.dll", "Simulation.Developer.dll", "Simulation.Developer.pdb", "notes.txt"]:
        var f := FileAccess.open(folder.path_join(name), FileAccess.WRITE)
        f.store_string("owned fixture " + name)
    var outside := output.path_join("Simulation.Developer.dll")
    var external := FileAccess.open(outside, FileAccess.WRITE);external.store_string("not in managed bundle");external.close()
    var manifest := folder.path_join("Delsk.Client.deps.json")
    var f := FileAccess.open(manifest, FileAccess.WRITE);f.store_string(debug_deps);f.close()
    var cleaner = load("res://addons/delsk_export/release_files.gd").new()
    cleaner.clean(folder)
    require(FileAccess.file_exists(folder.path_join("Simulation.Developer.dll")), "Failed release destroyed old debug bundle")
    checks.append("old debug dependency manifest preserved")
    f = FileAccess.open(manifest, FileAccess.WRITE);f.store_string("{}");f.close()
    cleaner.clean(folder)
    require(FileAccess.file_exists(folder.path_join("Simulation.Developer.dll")), "Invalid manifest caused deletion")
    checks.append("invalid or unrelated manifest preserved")
    f = FileAccess.open(manifest, FileAccess.WRITE);f.store_string(release_deps);f.close()
    DirAccess.rename_absolute(folder.path_join("Delsk.Client.dll"), folder.path_join("client.tmp"))
    cleaner.clean(folder)
    require(FileAccess.file_exists(folder.path_join("Simulation.Developer.dll")), "Incomplete bundle caused deletion")
    DirAccess.rename_absolute(folder.path_join("client.tmp"), folder.path_join("Delsk.Client.dll"))
    checks.append("incomplete bundle preserved")
    cleaner.clean(folder)
    require(not FileAccess.file_exists(folder.path_join("Simulation.Developer.dll")), "Stale DLL remains")
    require(not FileAccess.file_exists(folder.path_join("Simulation.Developer.pdb")), "Stale PDB remains")
    require(FileAccess.file_exists(folder.path_join("notes.txt")) and FileAccess.file_exists(outside), "Unrelated files deleted")
    require(FileAccess.file_exists(folder.path_join("Delsk.Client.dll")), "Client assembly deleted")
    checks.append("only stale Developer DLL/PDB removed from own release bundle")
    f = FileAccess.open(output.path_join("cleanup-check.json"), FileAccess.WRITE)
    f.store_string(JSON.stringify({"status":"passed" if failures.is_empty() else "failed", "checks":checks, "failures":failures}, "  "))
    f.close()
    cleaner = null
    print("EXPORT_CLEANUP ", JSON.stringify(failures))
    for i in range(2): await process_frame
    quit(0 if failures.is_empty() else 1)
