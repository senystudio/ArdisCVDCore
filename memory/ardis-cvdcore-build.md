---
name: ardis-cvdcore-build
description: "How to build and smoke-test ArdisCVDCore, including the NModbus reference trap"
metadata: 
  node_type: memory
  type: project
  originSessionId: 16643833-33f2-49b6-b3fc-cccf7a716994
  modified: 2026-08-13T07:39:49.014Z
---

.NET Framework 4.7.2 WinForms, no solution file. **The project is under git since 21.08.2026** — the older note here saying there is no version control is wrong and was written before the first commit.

**The project moved machines.** As of 22.09.2026 it lives on macOS at `/Users/sennix/Desktop/ArdisCVDCore`, not at `c:\Users\PAVLOV\Desktop\ArdisCVDCore`. Every Windows path in these memory files predates the move. The `~/.claude/projects/.../memory` junction described in [[memory-location]] does **not** exist on this machine — that path is an empty real directory, so write memory straight into the repo's `memory/`.

On Windows, build with VS MSBuild, not `dotnet`:

```
& 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe' `
    'ArdisCVDCore.csproj' /t:Rebuild /p:Configuration=Debug
```

On the macOS machine Mono's `msbuild` and `dotnet` are both on PATH, but a .NET Framework WinForms target will not produce a runnable app there — building and smoke testing stay on the Windows side. Do not promise a screenshot from macOS.

**NModbus used to resolve by accident.** The old csproj pointed `HintPath` at a solution-level `..\packages\` folder that does not exist; the build only succeeded because a stale `NModbus.dll` happened to sit in `bin\`. It is now vendored at `lib\NModbus.dll`. If the reference ever breaks again, that is the first thing to check rather than a NuGet restore.

`res\*.png` and `res\ardis.ico` are `EmbeddedResource` and read back through `Res.cs` as `ArdisCVDCore.res.<file>` — the design files arrived without their `.resx`, so images do not come from a ComponentResourceManager.

**Smoke testing works and is worth doing:** launch the exe, wait ~6 s, screenshot the primary screen with `Graphics.CopyFromScreen`, and drive the menus with `mouse_event` from `user32`. The PLC at 192.168.1.10 is sometimes live, in which case real gas flows, pressures and PID output appear. Forms can also be instantiated directly by loading the exe with `Assembly.LoadFrom` and pumping `Application.DoEvents`, which checks each window constructs and ticks without needing the menus.

Beware: PowerShell flattens nested arrays, so a table of replacements built as `@(@('a','b'),@('c','d'))` silently iterates six strings instead of three pairs and the edits appear to succeed while doing nothing. Use the Edit tool or a hashtable.

See [[ardis-cvdcore-open-work]].
