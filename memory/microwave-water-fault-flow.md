---
name: microwave-water-fault-flow
description: "How the no-cooling-water fault behaves end to end: when it is shown, what drops Preheat/MW, and what clears it"
metadata:
  type: project
---

**Переделано 11.11.2026, см. раздел в конце: всё, что ниже про `CheckMicrowaveWater`, латч `LatchWaterFault` и кнопку RESET в секции МВ, больше не действует.**

Agreed with the user 04.09 and corrected 07.09.2026. The generator latches its water-flow fault until a RESET; the PLC has no latch of its own (`xMwFaultActive` is a live OR of the coils, `stMw.wFaultReason` self-clears), so everything below is HMI-side.

**When it is shown.** A water fault is only reported once the operator has asked for something — `State.FaultReportable` hides it while `Idle` (neither Preheat nor Microwave requested). That was the user's request: an idle generator sitting on a latched water fault is noise.

**What happens on the press.** `MainForm.CheckMicrowaveWater`, called each `SuperCycle` tick, sees `WaterFlowFault && !Idle`, drops both Preheat and Microwave, and shows a one-line MessageBox ("No cooling water at the microwave generator."). `_waterFaultWarned` keeps it to one dialog per press.

**The latch, and why it exists.** Dropping Preheat makes the generator `Idle`, which would immediately re-hide the fault — the error flashed up and vanished, which is the bug the user reported on 07.09. `PLC210MicrowaveClient.LatchWaterFault()` sets `_waterFaultLatched`, copied into every parsed `State`, and `FaultReportable` ignores the Idle rule while it is set. So the error stays on the Status plate from the press until the pump is started.

**What clears it.** `Water_Btn_Click` calls `ClearWaterFaultLatch()` the moment Pump On is pressed, and the worker clears it by itself whenever the generator stops reporting `WaterFlowFault`.

**Pump On also auto-resets the generator.** Five `SuperCycle` ticks (5 s) after Pump On, if the PLC confirms the pump is running, `ClearMicrowaveFaultOnWater` sends `RequestMicrowave(false)` then `RequestReset()`. The delay is deliberate: reset the instant the button is pressed and the pump has not built flow yet, so the generator re-latches. MICROWAVE is dropped with it because the HMI's ON request survives a fault and the generator's ON coil still holds TRUE — a bare RESET could resume generation unattended. Both flags are read under one lock in the worker, so they leave in a single command word.

The flow sensors on the МВ210-102 modules are deliberately NOT used for this — the user chose the Pump On button as the trigger. See [[plc210-register-contract]] and [[ardis-cvdcore-open-work]].

**11.11.2026: отключение только МВ и единый Reset (не собрано, не прошито).** Пользователь жаловался на цепочку: MW без воды → полный Abort закрывал все GPV/VPV и глушил насосы → после включения воды висела «невидимая жёлтая ошибка», ничего не включалось → PREHEAT оживал только после STOP. Разбор и решения:
- **Почему ничего не включалось.** Во время Abort ПЛК ставит запрет повторного пуска на любую поднятую просьбу HMI, а снимает его, только когда просьба побывает в 0. Кнопки HMI переключаются по *фактическому* состоянию, поэтому после Abort каждое нажатие снова слало «включить», и просьба в 0 не падала никогда. Это и была жёлтая строка «Waiting to be switched on again after an abort» (бит 0x0010 в 292), которой нет в Fault Status. STOP помогал, потому что шлёт явный false.
- **Ошибки генератора больше не Abort (решение пользователя).** Коды 32..39 и 41 стали Alarm. `PRG_Alarms` при ошибке генератора во время запроса Preheat/MW ставит `xMwTripActive` и копит биты в `wMwTripLatched` (бит k = код 32+k), оба в `GVL_Alarms`. Пока стоит trip, `PRG_Microwave` и `xMwRequested` гасят Preheat/MW, а `xMwInhibit` держится, пока бит HMI в 192 не равен 0. Код 40 (нет связи с генератором) по-прежнему живой Alarm без trip. В статусе 292 бит 0x0020 = trip, HMI: `PLC210AlarmClient.State.MicrowaveTripped`.
- **Единый Reset.** Кнопка RESET в секции МВ удалена из дизайнера (`button2`), STOP (`button9`) растянут на всю ширину (8,120; 365×26), так попросил пользователь. Reset теперь только в Fault Status: `SystemStatus.ResetFaults()` = `PLC210AlarmClient.RequestReset()` + сброс генератора. Сброс аварий ушёл с бита 0x0004 в `awHolding[1]` на бит 0x0002 в `awHolding[271]`, его пишет клиент аварий одним циклом записи. Причина: старый бит заодно сбрасывал PID камеры (выход падал на L). Reset в ПЛК снимает trip МВ всегда, а Abort — только если причины Abort уже нет. Кнопка доступна при Abort, при trip МВ или когда генератор отвечает и сообщает ошибку.
- **Во время Abort (решение пользователя)** нажатие на включение GPV, VPV, форвакуумного и турбонасоса, Set газа > 0, PREHEAT, MICROWAVE не шлёт ничего и показывает окно «Abort is active. Open Fault Status and press Reset first.» (`MainForm.BlockedUntilReset`). При trip МВ так же блокируются PREHEAT и MICROWAVE. Водяной насос и выключения не блокируются.
- **До пуска.** PREHEAT/MICROWAVE не нажимаются, если генератор отвечает и показывает ошибку (`BlockedByGeneratorFault`): для воды — «Start the water pump, then open Fault Status and press Reset». Так MW без воды вообще не стартует.
- **На фронте trip** HMI один раз сбрасывает свои просьбы Preheat/MW и показывает окно с причиной из `AlarmCatalog` (`MainForm.UpdateAlarms`, `_microwaveTripHandled`). `CheckMicrowaveWater` и `_waterFaultWarned` удалены. `LatchWaterFault` больше никто не зовёт, но `WaterFaultLatched`/`ClearWaterFaultLatch`/`FaultReportable` оставлены: их использует `MainForm.Recipe.cs`.
- Автосброс генератора через 5 с после Pump On оставлен: он сбрасывает только генератор, trip в ПЛК всё равно снимается кнопкой Reset.
