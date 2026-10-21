@tool
extends EditorExportPlugin

var _managed_dir := ""
var _assembly := ""

func _get_name() -> String:
    return "Delsk release cleanup"

func _export_begin(features: PackedStringArray, is_debug: bool, path: String, _flags: int) -> void:
    _managed_dir = ""
    if is_debug or not features.has("windows"):
        return
    _assembly = str(ProjectSettings.get_setting("dotnet/project/assembly_name", ""))
    if _assembly != "Delsk.Client":
        return
    for architecture in ["x86_64", "x86_32", "arm64"]:
        if features.has(architecture):
            _managed_dir = ProjectSettings.globalize_path(path).get_base_dir().path_join("data_%s_windows_%s" % [_assembly, architecture])
            break

func _export_end() -> void:
    if _managed_dir.is_empty():
        return
    preload("release_files.gd").new().clean(_managed_dir)
    _managed_dir = ""
