---
type: Reference
title: OKF Rules
description: Local OKF rules for this repository, adapted from the supplied OKF specification.
tags:
  - okf
  - documentation
  - validation
timestamp: 2026-07-08T00:00:00Z
version: 0.1.0
---

# Local Bundle Root

This repository uses `docs/` as the OKF bundle root. The supplied OKF guideline
recommended `okf/`, but the project owner explicitly requested `docs/`.

# Required Rules

- Root `docs/index.md` declares `okf_version: "0.1"`.
- `docs/log.md` uses newest-first ISO date headings.
- Subdirectory `index.md` files are navigation only and have no frontmatter.
- Every other `.md` file under `docs/` has YAML frontmatter.
- Every concept frontmatter has a non-empty `type`.
- Important relations use relative Markdown links.

# Validation

Run:

```powershell
python tests\validate_okf.py
```

# Links

- Knowledge system: [Knowledge System](../architecture/knowledge_system.md)

