---
type: Plan
title: Defer Android TV Work Implementation Plan
description: Splits deferred Android TV work from the active TV integration plan behind an explicit user-resume gate.
tags:
  - tv
  - android-tv
  - backlog
  - planning
timestamp: 2026-07-19T00:00:00Z
version: 1.0.0
---

# Defer Android TV Work Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove Android TV work from the active TV integration plan, retain it in a durable backlog, and make explicit user approval the only resume gate.

**Architecture:** The active TV plan stays responsible for backend, worker/export, operational, and backend-documentation work. A separate backlog document owns every Android app, Android fixture, Android CI, APK, and Android-only documentation task. The current implementation plan will link to that backlog and state that generic requests to continue TV work do not authorize Android work.

**Tech Stack:** Markdown, OKF documentation, Git.

---

### Task 1: Create The Deferred Android TV Backlog

**Files:**
- Create: `docs/backlog/android_tv_tv_integration.md`
- Test: `tests/validate_okf.py`

- [ ] **Step 1: Write the deferred-work inventory**

Create the backlog with a top-level gate and these exact deferred groups:

```markdown
# Android TV Integration Backlog

> **Hard gate:** Do not start or resume any item in this document unless the
> user explicitly asks to resume Android TV work. A generic request to continue
> TV integration does not satisfy this gate.

## Deferred Work

1. Shared Android fixtures and Android contract tests formerly in Task 12.
2. Android TV models, parsing, and read-only transport formerly in Task 13.
3. Android TV lifecycle/progress/provider presentation formerly in Task 14.
4. Android CI triggers, Gradle tests, APK validation, and Android-only release
   checks formerly in Tasks 15 and 17.
5. Android TV client and Android read-only decision documentation formerly in
   Task 16.
```

- [ ] **Step 2: Validate the new backlog document**

Run:

```powershell
python tests\validate_okf.py
```

Expected: exit code `0`.

- [ ] **Step 3: Commit the backlog**

```powershell
git add docs/backlog/android_tv_tv_integration.md
git commit -m "docs: backlog Android TV work"
```

### Task 2: Remove Android Work From The Active TV Plan

**Files:**
- Modify: `docs/superpowers/plans/2026-07-13-tv-phase-1-read-model.md`
- Modify: `docs/superpowers/plans/2026-07-19-defer-android-tv-work.md`
- Test: `tests/validate_okf.py`

- [ ] **Step 1: Add the active-plan scope guard**

At the top of `2026-07-13-tv-phase-1-read-model.md`, add this exact scope note
after the architecture summary:

```markdown
> **Android TV scope:** Android TV implementation, Android fixtures, Android
> CI, APK validation, and Android-only documentation are deferred to
> `docs/backlog/android_tv_tv_integration.md`. Do not start or resume them
> without an explicit user request to resume Android TV work.
```

- [ ] **Step 2: Move Android-only task material to the backlog**

Remove Task 12, Task 13, and Task 14 from the active plan. Remove Android-only
steps from Tasks 15, 16, and 17, while retaining their backend, worker, deploy,
and OKF steps. Add the removed task names, source/test paths, commands, and
acceptance checks to `docs/backlog/android_tv_tv_integration.md` under matching
sections so future work has its original execution detail.

- [ ] **Step 3: Renumber and cross-check the remaining active tasks**

Renumber only headings and internal task references that become inaccurate after
the removal. Preserve all committed Task 1 through Task 9 history and leave the
current backend Task 10 and Task 11 requirements intact. Ensure the release
gate no longer requires Android Gradle or APK commands.

- [ ] **Step 4: Validate the active and deferred plans**

Run:

```powershell
python tests\validate_okf.py
rg -n -i "android" docs\superpowers\plans\2026-07-13-tv-phase-1-read-model.md
```

Expected: OKF validation exits `0`; the active-plan search returns only the
explicit Android scope guard and no executable Android task, command, or path.

- [ ] **Step 5: Commit the plan split**

```powershell
git add docs/superpowers/plans docs/backlog/android_tv_tv_integration.md
git commit -m "docs: remove Android TV work from active plan"
```
