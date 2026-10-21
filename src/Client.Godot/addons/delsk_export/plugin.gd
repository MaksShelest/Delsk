@tool
extends EditorPlugin

var _cleanup: EditorExportPlugin

func _enter_tree() -> void:
    _cleanup = preload("release_cleanup.gd").new()
    add_export_plugin(_cleanup)

func _exit_tree() -> void:
    if _cleanup != null:
        remove_export_plugin(_cleanup)
        _cleanup = null
