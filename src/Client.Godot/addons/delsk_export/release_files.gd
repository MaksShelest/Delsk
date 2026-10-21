@tool
extends RefCounted
# Kept separate from the editor lifecycle so file preservation has native tests.
func clean(managed_dir: String) -> void:
    if not FileAccess.file_exists(managed_dir.path_join("Delsk.Client.dll")) or not FileAccess.file_exists(managed_dir.path_join("Delsk.Client.deps.json")):
        return
    var dependencies = JSON.parse_string(FileAccess.get_file_as_string(managed_dir.path_join("Delsk.Client.deps.json")))
    if not dependencies is Dictionary or not dependencies.has("libraries") or not dependencies["libraries"] is Dictionary:
        return
    var owns_client := false
    for library in dependencies["libraries"]:
        var name := str(library).to_lower()
        if name.begins_with("simulation.developer/"):
            return # Failed export can leave the old debug package. Preserve it.
        if name.begins_with("delsk.client/"):
            owns_client = true
    if not owns_client:
        return
    for name in ["Simulation.Developer.dll", "Simulation.Developer.pdb"]:
        var file := managed_dir.path_join(name)
        if FileAccess.file_exists(file):
            var error := DirAccess.remove_absolute(file)
            if error != OK:
                push_error("Delsk: cannot remove stale Developer export file: " + name)
            else:
                print("Delsk release cleanup: removed " + name)
