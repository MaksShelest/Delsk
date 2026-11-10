extends SceneTree
var output := "res://out"
var manifest := []
const PIVOTS := ["Hips", "LeftLeg", "RightLeg", "Torso", "LeftArm", "RightArm", "Head"]
func _initialize() -> void:
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--output="): output = arg.trim_prefix("--output=")
	call_deferred("bake")
func bake() -> void:
	DirAccess.make_dir_recursive_absolute(output)
	var files = DirAccess.get_files_at("res://art_sources")
	files.sort()
	for file in files:
		if not file.ends_with(".tscn") or file.begins_with("Joined"): continue
		if file.begins_with("NPC_"): bake_character(file)
		else: bake_prop(file)
	var report = FileAccess.open(output.path_join("bake_manifest.json"), FileAccess.WRITE)
	report.store_string(JSON.stringify(manifest, "  "))
	report.close()
	quit()
func bake_prop(file: String) -> void:
	var root = load("res://art_sources/" + file).instantiate()
	var groups := {}
	collect_world(root, Transform3D.IDENTITY, groups)
	var combined := ArrayMesh.new()
	for group in groups.values():
		group.tool.set_material(group.material)
		group.tool.commit(combined)
	var key := file.trim_suffix(".tscn")
	var err := ResourceSaver.save(combined, output.path_join(key + ".res"), ResourceSaver.FLAG_COMPRESS)
	if err != OK:
		push_error("Failed baking " + key)
		root.free()
		quit(1)
		return
	var bounds := combined.get_aabb()
	manifest.append({"key": key, "surfaces": combined.get_surface_count(), "bounds": [bounds.position.x, bounds.position.y, bounds.position.z, bounds.size.x, bounds.size.y, bounds.size.z]})
	root.free()
	print("BAKED ", key, " surfaces=", combined.get_surface_count())
func bake_character(file: String) -> void:
	var source: Node3D = load("res://art_sources/" + file).instantiate()
	get_root().add_child(source)
	var rig := source.get_node("VisualRig") as Node3D
	prepare_body(rig)
	var key := file.trim_suffix(".tscn")
	var scene_root := Node3D.new()
	scene_root.name = key
	# Исходное лицо смотрит в +Z. Игровой корень считает вперёд −Z, поэтому геометрия разворачивается локально.
	var facing := Node3D.new()
	facing.name = "Facing"
	facing.rotation_degrees = Vector3(0, 180, 0)
	scene_root.add_child(facing)
	var visual := attach(rig, facing, scene_root)
	add_nose(rig, visual, scene_root)
	claim(scene_root, scene_root)
	var packed := PackedScene.new()
	var err := packed.pack(scene_root)
	if err != OK:
		push_error("Failed packing " + key)
		quit(1)
		return
	err = ResourceSaver.save(packed, output.path_join(key + ".res"), ResourceSaver.FLAG_COMPRESS)
	if err != OK:
		push_error("Failed saving " + key)
		quit(1)
		return
	var check := (load(output.path_join(key + ".res")) as PackedScene).instantiate()
	for joint in ["LeftLeg", "RightLeg", "LeftArm", "RightArm", "Hips", "Head", "Nose", "Facing"]:
		if check.find_child(joint, true, false) == null:
			push_error(key + " lost " + joint)
			quit(1)
			return
	var surfaces := 0
	for mesh in meshes_of(check): surfaces += mesh.get_surface_count()
	check.free()
	manifest.append({"key": key, "rig": true, "surfaces": surfaces, "facing": "-Z"})
	source.free()
	scene_root.free()
	print("BAKED RIG ", key, " surfaces=", surfaces)
func prepare_body(rig: Node3D) -> void:
	var hips := rig.get_node("Hips") as Node3D
	var torso := hips.get_node("Torso") as Node3D
	var loose: Array[Node] = []
	for child in rig.get_children():
		if child == hips or str(child.name) == "Head": continue
		if child is MeshInstance3D: loose.append(child)
	for child in loose: child.reparent(hips, true)
	var head := rig.get_node_or_null("Head") as Node3D
	if head: head.reparent(torso, true)
func attach(source: Node3D, dest_parent: Node3D, scene_root: Node) -> Node3D:
	var pivot := Node3D.new()
	pivot.name = source.name
	pivot.transform = source.transform
	dest_parent.add_child(pivot)
	var groups := {}
	collect_owned(source, Transform3D.IDENTITY, groups)
	if groups.size() > 0:
		var combined := ArrayMesh.new()
		for group in groups.values():
			group.tool.set_material(group.material)
			group.tool.commit(combined)
		var inst := MeshInstance3D.new()
		inst.name = "Model"
		inst.mesh = combined
		pivot.add_child(inst)
	for child in source.get_children():
		if child is Node3D and str(child.name) in PIVOTS:
			attach(child, pivot, scene_root)
	return pivot
func add_nose(source_rig: Node3D, dest_rig: Node3D, scene_root: Node) -> void:
	var nose := source_rig.find_child("Nose", true, false) as Node3D
	var source_head := source_rig.find_child("Head", true, false) as Node3D
	var dest_head := dest_rig.find_child("Head", true, false) as Node3D
	if nose == null or source_head == null or dest_head == null:
		push_error("Character face was not found")
		quit(1)
		return
	var marker := Node3D.new()
	marker.name = "Nose"
	marker.position = source_head.to_local(nose.global_position)
	dest_head.add_child(marker)
func claim(node: Node, scene_root: Node) -> void:
	if node != scene_root: node.owner = scene_root
	for child in node.get_children(): claim(child, scene_root)
func meshes_of(node: Node) -> Array:
	var found: Array = []
	if node is MeshInstance3D and node.mesh != null: found.append(node.mesh)
	for child in node.get_children(): found.append_array(meshes_of(child))
	return found
func collect_owned(node: Node, relative: Transform3D, groups: Dictionary) -> void:
	for child in node.get_children():
		if not child is Node3D: continue
		if str(child.name) in PIVOTS: continue
		var next: Transform3D = relative * child.transform
		if child is MeshInstance3D and child.mesh != null: append_mesh(child, next, groups)
		collect_owned(child, next, groups)
func collect_world(node: Node, transform: Transform3D, groups: Dictionary) -> void:
	var combined := transform
	if node is Node3D: combined = transform * node.transform
	if node is MeshInstance3D and node.mesh != null: append_mesh(node, combined, groups)
	for child in node.get_children(): collect_world(child, combined, groups)
func append_mesh(node: MeshInstance3D, transform: Transform3D, groups: Dictionary) -> void:
	for i in range(node.mesh.get_surface_count()):
		var material = node.material_override if node.material_override != null else node.mesh.surface_get_material(i)
		var id := material.get_instance_id() if material != null else 0
		if not groups.has(id):
			var tool := SurfaceTool.new()
			tool.begin(Mesh.PRIMITIVE_TRIANGLES)
			groups[id] = {"tool": tool, "material": material}
		groups[id].tool.append_from(node.mesh, i, transform)
