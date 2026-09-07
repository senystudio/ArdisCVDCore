---
name: ardis-cvdcore-build
description: "How to build and smoke-test ArdisCVDCore, including the NModbus reference trap"
metadata: 
  node_type: memory
  type: project
  originSessionId: 16643833-33f2-49b6-b3fc-cccf7a716994
  modified: 2026-08-13T07:39:49.014Z
---

.NET Framework 4.7.2 WinForms, no solution file, no version control in the working directory — so there is no git history to fall back on and overwriting a file loses it.

Build with VS MSBuild, not `dotnet`:

```
& 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe' `
    'c:\Users\PAVLOV\Desktop\ArdisCVDCore\ArdisCVDCore.csproj' /t:Rebuild /p:Configuration=Debug
```

**NModbus used to resolve by accident.** The old csproj pointed `HintPath` at a solution-level `..\packages\` folder that does not exist; the build only succeeded because a stale `NModbus.dll` happened to sit in `bin\`. It is now vendored at `lib\NModbus.dll`. If the reference ever breaks again, that is the first thing to check rather than a NuGet restore.

`res\*.png` and `res\ardis.ico` are `EmbeddedResource` and read back through `Res.cs` as `ArdisCVDCore.res.<file>` — the design files arrived without their `.resx`, so images do not come from a ComponentResourceManager.

**Smoke testing works and is worth doing:** launch the exe, wait ~6 s, screenshot the primary screen with `Graphics.CopyFromScreen`, and drive the menus with `mouse_event` from `user32`. The PLC at 192.168.1.10 is sometimes live, in which case real gas flows, pressures and PID output appear. Forms can also be instantiated directly by loading the exe with `Assembly.LoadFrom` and pumping `Application.DoEvents`, which checks each window constructs and ticks without needing the menus.

Beware: PowerShell flattens nested arrays, so a table of replacements built as `@(@('a','b'),@('c','d'))` silently iterates six strings instead of three pairs and the edits appear to succeed while doing nothing. Use the Edit tool or a hashtable.

See [[ardis-cvdcore-open-work]].
