"""Asset-only staging must preserve real inputs and leave C# runtime/editor state alone."""
from pathlib import Path
import sys,tempfile,unittest
sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'scripts'))
from prepare_godot_asset_import import prepare

class AssetImportStageTests(unittest.TestCase):
    def test_identical_assets_and_settings_without_managed_code_or_editor_cache(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);source=root/'source';source.mkdir()
            (source/'project.godot').write_text('config_version=5\n[rendering]\nrenderer/rendering_method="gl_compatibility"\n[dotnet]\nproject/assembly_name="Forbidden"\n')
            (source/'Main.cs').write_text('compiled elsewhere');(source/'Main.tscn').write_text('not an asset import input')
            (source/'icon.svg').write_bytes(b'<svg/>');(source/'icon.svg.import').write_text('actual import settings')
            (source/'.godot').mkdir();(source/'.godot/stale.png').write_bytes(b'cache')
            target=root/'target';result=prepare(source,target)
            self.assertEqual(result['status'],'staged');self.assertEqual(len(result['assets']),1)
            self.assertEqual((target/'icon.svg').read_bytes(),(source/'icon.svg').read_bytes())
            self.assertEqual((target/'icon.svg.import').read_bytes(),(source/'icon.svg.import').read_bytes())
            self.assertFalse((target/'Main.cs').exists());self.assertFalse((target/'Main.tscn').exists());self.assertFalse((target/'.godot').exists())
            self.assertIn('renderer/rendering_method="gl_compatibility"',(target/'project.godot').read_text());self.assertNotIn('Forbidden',(target/'project.godot').read_text())
    def test_refuses_source_parent_and_nonempty_output(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);source=root/'source';source.mkdir();(source/'project.godot').write_text('[rendering]\n');(source/'icon.svg').write_bytes(b'<svg/>')
            for target in [root,source]:
                with self.assertRaises(ValueError):prepare(source,target)
            target=root/'target';target.mkdir();(target/'keep.txt').write_text('owned by someone else')
            with self.assertRaises(ValueError):prepare(source,target)
            self.assertEqual((target/'keep.txt').read_text(),'owned by someone else')
