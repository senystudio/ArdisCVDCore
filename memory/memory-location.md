---
name: memory-location
description: "Project memory lives in the repo at ArdisCVDCore/memory/; the ~/.claude path is a Windows junction to it"
metadata:
  type: project
---

Since 07.09.2026 the memory files physically live in the repository, at `c:\Users\PAVLOV\Desktop\ArdisCVDCore\memory\`. The user asked for them to be kept there ("перенеси всю память проекта в корневую папку этого проекта").

The harness path `C:\Users\PAVLOV\.claude\projects\c--Users-PAVLOV-Desktop-ArdisCVDCore\memory` is now a Windows directory junction pointing at that folder, so reads and writes through either path hit the same files. Nothing about how memories are written changes — one file per fact, plus the pointer line in `MEMORY.md`.

**Why:** the user wants the project knowledge to sit next to the code rather than in a per-machine profile directory, where it is visible and copyable with the project.

**How to apply:** edit and create memory files under the repo path. If the junction is ever lost — a fresh machine, a re-clone, `.claude` wiped — recreate it with `New-Item -ItemType Junction -Path <profile memory path> -Target c:\Users\PAVLOV\Desktop\ArdisCVDCore\memory`, or just copy the folder back. See [[ardis-cvdcore-working-style]].
