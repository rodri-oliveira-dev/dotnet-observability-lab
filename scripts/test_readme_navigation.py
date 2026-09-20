"""Guard bilingual README navigation, executable parity and repository links."""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]
FILES = (ROOT / "README.md", ROOT / "README.pt-BR.md")
BADGE_IMAGES = re.compile(r"!\[[^\]]+\]\((https://[^)]+)\)")
MARKDOWN_TARGETS = re.compile(r"\]\(([^)]+)\)")
BASH_BLOCKS = re.compile(r"```bash\n([\s\S]*?)\n```")
ESSENTIAL_LINKS = (
    "README.md", "README.pt-BR.md", "LICENSE", "global.json",
    "docs/architecture/README.md", "docs/architecture/views.c4",
    "docs/adr/README.md", "docs/events/ValueReceived.v1.md",
    "docs/scenarios.md", "docs/runtime-verification.md", "docs/ci.md",
    "docs/README.md", "scripts/runtime_http_smoke.py",
)


class ReadmeNavigationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.readmes = [file.read_text(encoding="utf-8") for file in FILES]

    def test_language_selector_is_first_visible_element_in_both_directions(self):
        english, portuguese = self.readmes
        for content in self.readmes:
            first_line = content.splitlines()[0]
            self.assertIn("](README.md)", first_line)
            self.assertIn("](README.pt-BR.md)", first_line)
            self.assertTrue(first_line.startswith("["))
            self.assertLess(content.index("](README.md)"), content.index("# dotnet-observability-lab"))
        self.assertIn("**English", english.splitlines()[0])
        self.assertIn("**Português (Brasil)", portuguese.splitlines()[0])

    def test_badge_images_and_actual_workflow_destinations_match(self):
        english, portuguese = self.readmes
        self.assertEqual(BADGE_IMAGES.findall(english), BADGE_IMAGES.findall(portuguese))
        for content in self.readmes:
            for workflow in ("ingestion-integration.yml", "codeql.yml"):
                badge = ("https://github.com/rodri-oliveira-dev/dotnet-observability-lab/"
                         f"actions/workflows/{workflow}/badge.svg?branch=main")
                page = ("https://github.com/rodri-oliveira-dev/dotnet-observability-lab/"
                        f"actions/workflows/{workflow}")
                self.assertIn(badge, content)
                self.assertIn(f"]({page})", content)
                self.assertTrue((ROOT / ".github" / "workflows" / workflow).is_file())

    def test_executable_code_blocks_have_exact_parity(self):
        english, portuguese = self.readmes
        commands = BASH_BLOCKS.findall(english)
        self.assertEqual(len(commands), 4)
        self.assertEqual(commands, BASH_BLOCKS.findall(portuguese))
        self.assertEqual(commands[0].count("aspire secret set"), 5)

    def test_essential_links_present_and_all_relative_targets_exist(self):
        for content in self.readmes:
            for path in ESSENTIAL_LINKS:
                self.assertIn(f"]({path})", content, path)
            for target in MARKDOWN_TARGETS.findall(content):
                if target.startswith(("https://", "http://", "#", "mailto:")):
                    continue
                relative = target.split("#", 1)[0]
                self.assertTrue((ROOT / relative).is_file(), relative)

    def test_pending_runtime_and_diagram_status_not_reported_as_complete(self):
        for content in self.readmes:
            self.assertIn("npm ci && npm run architecture:dev", content)
            self.assertIn("docs/architecture/rendered.md#c4-level-2---containers", content)
            self.assertIn("docs/architecture/rendered.md", content)
            self.assertIn("architecture-preview.yml", content)
            self.assertIn("docs/runtime-verification.md", content)
            self.assertNotIn("83.23%", content)


if __name__ == "__main__":
    unittest.main()
