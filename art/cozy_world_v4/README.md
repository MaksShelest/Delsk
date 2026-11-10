# Cozy world model sources

Автор: Шелестов Максим Евгеньевич.

Native editable model scenes from the approved design v3, with v4 rain-slope fixes. Ten named cosmetic characters, six forest stages, seamless fields, pastures, factories, shop, capital and vehicles. They do not contain simulation rules. Names are display-only.

The three-cell town hall is an art source. In the game every skin is uniformly fitted to the existing logical footprint; cosmetic settings do not change map access or collisions.

Run `scripts/bake_world_skins.gd` with a Godot headless editor after staging these sources as described by `scripts/build_world_skins.py`. Props combine repeated details by material. Characters keep the VisualRig pivots (hips, legs, torso, arms, head) so the client can swing them, and a local yaw turns the original +Z face onto the game's −Z forward. Sources stay outside the exported Godot project.
