import importlib.util
import json
from pathlib import Path
import tempfile
import unittest


SCRIPT = Path(__file__).resolve().parents[1] / "scripts/import-ntlite-removals.py"


class NTLiteImportTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        for directory in ("scripts", "config", "docs", "src/playbook/Configuration/atlas"):
            (self.root / directory).mkdir(parents=True, exist_ok=True)
        spec = importlib.util.spec_from_file_location("ntlite_import", SCRIPT)
        self.module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(self.module)
        self.module.__file__ = str(self.root / "scripts/import-ntlite-removals.py")
        self.appx = self.root / "src/playbook/Configuration/atlas/appx.yml"
        self.appx.write_text("actions: []\n")
        self.generated = self.appx.with_name("ntlite-appx.yml")

    def run_import(self, *ids):
        data = {"source_sha256": "test", "removals": [
            {"id": ident, "label": ident} for ident in ids
        ]}
        (self.root / "config/ntlite-removals.json").write_text(json.dumps(data))
        self.module.main()
        return self.generated.read_text(), (self.root / "docs/ntlite-removals.md").read_text()

    def test_native_or_unknown_ids_are_reported_without_actions(self):
        actions, report = self.run_import("winre", "vss", "manifestbackup", "unknown")
        self.assertNotIn("!appx:", actions)
        for ident in ("winre", "vss", "manifestbackup", "unknown"):
            self.assertIn("| " + ident + " |", report)
        self.assertIn("mappings: 4", report)

    def test_existing_appx_is_not_duplicated(self):
        original = "actions:\n  - !appx: {name: 'Microsoft.Copilot*', type: family}\n"
        self.appx.write_text(original)
        actions, report = self.run_import("copilotpwa", "calculator")
        self.assertNotIn("Microsoft.Copilot*", actions)
        self.assertIn("Microsoft.WindowsCalculator*", actions)
        self.assertIn("Existing AppX rule", report)
        self.assertEqual(self.appx.read_text(), original)

    def test_defender_choice_is_preserved_and_offline_is_opted_in(self):
        actions, _ = self.run_import("microsoft.sechealthui")
        rules = [line for line in actions.splitlines() if "!appx:" in line]
        self.assertEqual(len(rules), 2)
        self.assertTrue(all("option: 'defender-disable'" in rule for rule in rules))
        self.assertIn("iso: true", actions)
        self.assertTrue(all("iso: true, oobe: true" in rule for rule in rules))

    def test_duplicate_ids_rejected_before_generation(self):
        with self.assertRaisesRegex(ValueError, "Duplicate"):
            self.run_import("calculator", "calculator")
        self.assertFalse(self.generated.exists())

    def test_generation_is_repeatable(self):
        first = self.run_import("calculator", "winre")
        second = self.run_import("calculator", "winre")
        self.assertEqual(first, second)


if __name__ == "__main__":
    unittest.main()
