---
name: microwave-water-fault-flow
description: "How the no-cooling-water fault behaves end to end: when it is shown, what drops Preheat/MW, and what clears it"
metadata:
  type: project
---

Agreed with the user 04.09 and corrected 07.09.2026. The generator latches its water-flow fault until a RESET; the PLC has no latch of its own (`xMwFaultActive` is a live OR of the coils, `stMw.wFaultReason` self-clears), so everything below is HMI-side.

**When it is shown.** A water fault is only reported once the operator has asked for something — `State.FaultReportable` hides it while `Idle` (neither Preheat nor Microwave requested). That was the user's request: an idle generator sitting on a latched water fault is noise.

**What happens on the press.** `MainForm.CheckMicrowaveWater`, called each `SuperCycle` tick, sees `WaterFlowFault && !Idle`, drops both Preheat and Microwave, and shows a one-line MessageBox ("No cooling water at the microwave generator."). `_waterFaultWarned` keeps it to one dialog per press.

**The latch, and why it exists.** Dropping Preheat makes the generator `Idle`, which would immediately re-hide the fault — the error flashed up and vanished, which is the bug the user reported on 07.09. `PLC210MicrowaveClient.LatchWaterFault()` sets `_waterFaultLatched`, copied into every parsed `State`, and `FaultReportable` ignores the Idle rule while it is set. So the error stays on the Status plate from the press until the pump is started.

**What clears it.** `Water_Btn_Click` calls `ClearWaterFaultLatch()` the moment Pump On is pressed, and the worker clears it by itself whenever the generator stops reporting `WaterFlowFault`.

**Pump On also auto-resets the generator.** Five `SuperCycle` ticks (5 s) after Pump On, if the PLC confirms the pump is running, `ClearMicrowaveFaultOnWater` sends `RequestMicrowave(false)` then `RequestReset()`. The delay is deliberate: reset the instant the button is pressed and the pump has not built flow yet, so the generator re-latches. MICROWAVE is dropped with it because the HMI's ON request survives a fault and the generator's ON coil still holds TRUE — a bare RESET could resume generation unattended. Both flags are read under one lock in the worker, so they leave in a single command word.

The flow sensors on the МВ210-102 modules are deliberately NOT used for this — the user chose the Pump On button as the trigger. See [[plc210-register-contract]] and [[ardis-cvdcore-open-work]].
