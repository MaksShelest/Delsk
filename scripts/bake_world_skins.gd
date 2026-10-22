extends SceneTree
var output := "res://out"
var manifest := []
func _initialize() -> void:
 for arg in OS.get_cmdline_user_args():
  if arg.begins_with("--output="):output=arg.trim_prefix("--output=")
 call_deferred("bake")
func bake() -> void:
 DirAccess.make_dir_recursive_absolute(output)
 var files=DirAccess.get_files_at("res://art_sources");files.sort()
 for file in files:
  if not file.ends_with(".tscn") or file.begins_with("Joined"):continue
  var root=load("res://art_sources/"+file).instantiate()
  var groups={};collect(root,Transform3D.IDENTITY,groups)
  var combined=ArrayMesh.new()
  for group in groups.values():
   group.tool.set_material(group.material)
   group.tool.commit(combined)
  var key=file.trim_suffix(".tscn")
  var err=ResourceSaver.save(combined,output.path_join(key+".res"),ResourceSaver.FLAG_COMPRESS)
  if err!=OK:push_error("Failed baking "+key);root.free();quit(1);return
  var bounds=combined.get_aabb()
  manifest.append({"key":key,"surfaces":combined.get_surface_count(),"bounds":[bounds.position.x,bounds.position.y,bounds.position.z,bounds.size.x,bounds.size.y,bounds.size.z]})
  root.free();print("BAKED ",key," surfaces=",combined.get_surface_count())
 var report=FileAccess.open(output.path_join("bake_manifest.json"),FileAccess.WRITE)
 report.store_string(JSON.stringify(manifest,"  "));report.close()
 quit()
func collect(node:Node, transform:Transform3D, groups:Dictionary) -> void:
 var combined=transform
 if node is Node3D:combined=transform*node.transform
 if node is MeshInstance3D and node.mesh!=null:
  for i in range(node.mesh.get_surface_count()):
   var material=node.material_override if node.material_override!=null else node.mesh.surface_get_material(i)
   var id=material.get_instance_id() if material!=null else 0
   if not groups.has(id):
    var tool=SurfaceTool.new();tool.begin(Mesh.PRIMITIVE_TRIANGLES)
    groups[id]={"tool":tool,"material":material}
   groups[id].tool.append_from(node.mesh,i,combined)
 for child in node.get_children():collect(child,combined,groups)
