from __future__ import annotations

from pathlib import Path
import re
import sys
from urllib.parse import unquote


BUNDLE_ROOT = Path("docs")
RESERVED_NAMES = {"index.md", "log.md"}


def split_frontmatter(text: str) -> tuple[dict[str, object], str]:
    if not text.startswith("---\n"):
        raise ValueError("missing opening frontmatter delimiter")

    parts = text.split("\n---\n", 1)
    if len(parts) != 2:
        raise ValueError("missing closing frontmatter delimiter")

    return parse_simple_yaml(parts[0].removeprefix("---\n")), parts[1]


def parse_simple_yaml(text: str) -> dict[str, object]:
    data: dict[str, object] = {}
    current_key: str | None = None

    for raw_line in text.splitlines():
        line = raw_line.rstrip()
        if not line.strip():
            continue

        list_match = re.match(r"^\s*-\s+(.*)$", line)
        if list_match and current_key:
            value = data.setdefault(current_key, [])
            if not isinstance(value, list):
                raise ValueError(f"frontmatter key {current_key!r} mixes scalar and list values")
            value.append(strip_quotes(list_match.group(1).strip()))
            continue

        key_match = re.match(r"^([A-Za-z0-9_]+):(?:\s*(.*))?$", line)
        if not key_match:
            if line.startswith("  "):
                continue
            raise ValueError(f"unsupported frontmatter line: {line}")

        current_key = key_match.group(1)
        raw_value = (key_match.group(2) or "").strip()
        data[current_key] = strip_quotes(raw_value) if raw_value else []

    return data


def strip_quotes(value: str) -> str:
    if len(value) >= 2 and value[0] == value[-1] and value[0] in {'"', "'"}:
        return value[1:-1]
    return value


def validate_concept(path: Path, errors: list[str]) -> None:
    text = path.read_text(encoding="utf-8")
    try:
        frontmatter, body = split_frontmatter(text)
    except Exception as ex:
        errors.append(f"{path}: {ex}")
        return

    if not str(frontmatter.get("type", "")).strip():
        errors.append(f"{path}: missing non-empty 'type'")

    if not body.strip():
        errors.append(f"{path}: missing Markdown body")

    for schema_ref in frontmatter.get("schema_refs", []):
        if not isinstance(schema_ref, str):
            errors.append(f"{path}: schema_refs entries must be strings")
            continue
        target = (path.parent / schema_ref).resolve()
        if not target.exists():
            errors.append(f"{path}: schema_ref does not exist: {schema_ref}")


def validate_index(path: Path, errors: list[str]) -> None:
    text = path.read_text(encoding="utf-8")

    if path == BUNDLE_ROOT / "index.md":
        try:
            frontmatter, body = split_frontmatter(text)
        except Exception as ex:
            errors.append(f"{path}: {ex}")
            return

        allowed = {"okf_version"}
        extra = set(frontmatter) - allowed
        if frontmatter.get("okf_version") != "0.1":
            errors.append(f"{path}: root index frontmatter must contain okf_version: \"0.1\"")
        if extra:
            errors.append(f"{path}: unexpected root index keys: {sorted(extra)}")
        if not body.strip():
            errors.append(f"{path}: missing Markdown body")
    elif text.startswith("---\n"):
        errors.append(f"{path}: non-root index.md should not contain frontmatter")


def validate_log(path: Path, errors: list[str]) -> None:
    text = path.read_text(encoding="utf-8")
    if not re.search(r"^## \d{4}-\d{2}-\d{2}$", text, flags=re.MULTILINE):
        errors.append(f"{path}: log.md should contain ISO date headings like ## 2026-06-27")


def validate_links(path: Path, errors: list[str]) -> None:
    text = path.read_text(encoding="utf-8")
    for raw_target in re.findall(r"(?<!!)\[[^\]]+\]\(([^)]+)\)", text):
        target_text = raw_target.strip()
        if not target_text or target_text.startswith(("#", "http://", "https://", "mailto:")):
            continue
        if "://" in target_text:
            continue

        target_path_text = unquote(target_text.split("#", 1)[0])
        if not target_path_text:
            continue

        target = (path.parent / target_path_text).resolve()
        if not target.exists():
            errors.append(f"{path}: broken link: {target_text}")


def main() -> int:
    errors: list[str] = []

    if not BUNDLE_ROOT.exists():
        errors.append("missing docs/ OKF bundle directory")
    else:
        for path in sorted(BUNDLE_ROOT.rglob("*.md")):
            relative = path.relative_to(BUNDLE_ROOT)
            if path.name == "index.md":
                validate_index(relative_to_cwd(path), errors)
            elif path.name == "log.md":
                validate_log(relative_to_cwd(path), errors)
            else:
                validate_concept(relative_to_cwd(path), errors)
            validate_links(relative_to_cwd(path), errors)

    if errors:
        print("\n".join(errors))
        return 1

    print("OKF validation passed.")
    return 0


def relative_to_cwd(path: Path) -> Path:
    return Path(path)


if __name__ == "__main__":
    sys.exit(main())

