"""Check bilingual documentation coverage, navigation and local Markdown targets.

Run from any directory: python scripts/Check-Documentation.py
Historical local evidence under output/artifacts and absolute Windows paths is
reported separately, since it is intentionally not distributed with the source.
"""

from pathlib import Path
import os
import re
import subprocess
import sys
from urllib.parse import unquote


ROOT = Path(__file__).resolve().parents[1]
ENGLISH_ORIGINALS = {"README.md", "THIRD_PARTY_NOTICES.md", "AddIn/Resources/README.md"}
EXCLUDED_PARTS = {"bin", "obj", "out", "artifacts", "__pycache__"}


def original_documents():
    if (ROOT / ".git").exists():
        result = subprocess.run(
            ["git", "ls-files", "--cached", "--others", "--exclude-standard", "-z", "--", "*.md"],
            cwd=ROOT, check=True, stdout=subprocess.PIPE,
        )
        candidates = [ROOT / name for name in result.stdout.decode("utf-8").split("\0") if name]
        candidates = [p for p in candidates if len(p.relative_to(ROOT).parts) == 1
                      or p.relative_to(ROOT).parts[0] in {"docs", "AddIn", "Installer", "Mcp"}]
    else:
        candidates = list(ROOT.glob("*.md"))
        for folder in ("docs", "AddIn", "Installer", "Mcp"):
            candidates.extend((ROOT / folder).rglob("*.md"))
    return sorted(
        (p for p in candidates
         if p.name != "AGENTS.md"
         and not p.name.endswith((".en.md", ".ja.md"))
         and not (set(p.relative_to(ROOT).parts) & EXCLUDED_PARTS)),
        key=lambda p: p.relative_to(ROOT).as_posix().lower(),
    )


def pair(path):
    relative = path.relative_to(ROOT).as_posix()
    if relative in ENGLISH_ORIGINALS:
        return path.with_suffix(".ja.md"), path
    return path, path.with_suffix(".en.md")


def main():
    errors = []
    evidence = set()
    documents = set()
    originals = original_documents()
    indexes = [(ROOT / "docs/INDEX.md").read_text(encoding="utf-8"),
               (ROOT / "docs/INDEX.en.md").read_text(encoding="utf-8")]
    for original in originals:
        japanese, english = pair(original)
        for path, other in ((japanese, english), (english, japanese)):
            documents.add(path)
            if not path.is_file():
                errors.append(f"Missing language edition: {path.relative_to(ROOT)}")
                continue
            source = path.read_text(encoding="utf-8")
            if "<!-- mechcue-docs-language -->" not in source[:1000]:
                errors.append(f"Missing language navigation: {path.relative_to(ROOT)}")
            if f"]({other.name})" not in source[:1000]:
                errors.append(f"Missing reciprocal link: {path.relative_to(ROOT)}")
        if original.name != "INDEX.md":
            for content in indexes:
                for edition in (japanese, english):
                    target = Path(os.path.relpath(edition, ROOT / 'docs')).as_posix()
                    if f"]({target})" not in content:
                        errors.append(f"Missing index entry: {edition.relative_to(ROOT)}")

    documents.add(ROOT / "README.en.md")  # Preserve links from the former English URL.
    documents.update((ROOT / ".github/ISSUE_TEMPLATE").glob("*.md"))
    checked_links = 0
    for path in sorted(documents):
        if not path.is_file():
            continue
        source = path.read_text(encoding="utf-8")
        source = re.sub(r"^```.*?^```[^\n]*$", "", source, flags=re.M | re.S)
        for match in re.finditer(r"\[[^\]\n]*\]\(([^)\n]+)\)", source):
            target = match.group(1).strip().strip("<>")
            if re.match(r"^[A-Za-z]:[/\\]", target):
                evidence.add(target)
                continue
            if re.match(r"^[a-zA-Z][a-zA-Z0-9+.-]*:", target):
                continue
            target = unquote(target.split("#", 1)[0])
            if not target:
                continue
            resolved = (path.parent / target).resolve()
            try:
                relative = resolved.relative_to(ROOT)
            except ValueError:
                errors.append(f"Link outside source tree: {path.relative_to(ROOT)}: {target}")
                continue
            if relative.parts[0] in {"output", "artifacts", "tmp"}:
                evidence.add(relative.as_posix())
                continue
            checked_links += 1
            if not resolved.exists():
                errors.append(f"Broken link: {path.relative_to(ROOT)}: {target}")

    for error in errors:
        print(error)
    print(f"{len(originals)} bilingual document pairs; {checked_links} local links checked; "
          f"{len(evidence)} historical local evidence references excluded; {len(errors)} errors.")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
