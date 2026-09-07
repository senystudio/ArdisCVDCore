---
name: ardis-reference-projects
description: Where the two older Ardis apps live on disk and what each is used for when porting behaviour into ArdisCVDCore
metadata: 
  node_type: memory
  type: reference
  originSessionId: 16643833-33f2-49b6-b3fc-cccf7a716994
  modified: 2026-08-13T07:38:46.372Z
---

ArdisCVDCore is a rewrite that borrows behaviour from two older WinForms apps. Both are outside the working directory and neither is in version control, so read them rather than guess:

- `C:\Users\PAVLOV\Documents\ArdisCVDMaster1\ArdisCVDMaster` — the full old machine app (`ArdisControlForm`, `modules_hw/`, `modules_logic/ErrorDispatcher.cs`, `ProcessParametersForm.cs`). This is the authority on how a feature *used to* work: sensor scaling, alarm maths, stopwatch format, Manual Mode gating. It talks to an ICP DAS PAC and PC-side PID, so its hardware layer never transfers directly — ArdisCVDCore talks only to a PLC210 over Modbus TCP.
- `C:\Users\PAVLOV\Documents\Plc210PressurePid` — snapshot of ArdisCVDCore itself from just before the redesign (04.08.2026). Useful when a control was dropped in the redesign and its old wiring is needed; the pressure-chart series config and the original PID panel layout came from here.

The user supplies new screen designs as bare `*.Designer.cs` files dropped into the project root, in namespace `ArdisCVDMaster` — rename the namespace, keep the class, and check whether a matching `.resx` is needed (usually not).

See [[ardis-cvdcore-working-style]] and [[ardis-cvdcore-open-work]].
