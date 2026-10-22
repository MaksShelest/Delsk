#!/usr/bin/env python3
"""Bake editable art to material-batched native meshes. Автор: Шелестов Максим Евгеньевич."""
import argparse,shutil,subprocess,tempfile
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--godot',required=True);a=p.parse_args()
root=Path(__file__).resolve().parents[1];output=root/'src/Client.Godot/skins/cozy'
with tempfile.TemporaryDirectory(prefix='delsk-skin-bake-') as temp:
 stage=Path(temp);shutil.copytree(root/'art/cozy_world_v4/models',stage/'art_sources')
 (stage/'project.godot').write_text('config_version=5\n[application]\nconfig/name="Delsk skin bake"\n')
 shutil.copy2(root/'scripts/bake_world_skins.gd',stage/'bake.gd')
 subprocess.run([a.godot,'--headless','--path',str(stage),'--script','res://bake.gd','--','--output='+str(output)],check=True,timeout=90)
