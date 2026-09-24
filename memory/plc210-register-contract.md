---
name: plc210-register-contract
description: "awHolding map on the PLC210, which HMI client reads which block, which words are free, and the signals that are only an echo"
metadata:
  node_type: memory
  type: reference
  modified: 2026-08-25T00:00:00.000Z
---

`awHolding AT %QW0 : ARRAY[0..239] OF WORD` in `GVL_ModbusHolding.st`, all fixed point x1000 low word first. Eight parallel Modbus TCP clients on the HMI, one socket per block, so a slow block never sets the pace for the others.

| words | owner (PLC) | HMI client |
|---|---|---|
| 0..63 | PLC_PRG reads (HMI writes setpoints/flags) | PLC210PidClient |
| 64..99 | PRG_GasFlow, own 50 ms GasFlowTask | PLC210GasFlowClient |
| 100..137 | PLC_PRG | PLC210PidClient |
| 133/134 | PRG_GasValves | PLC210GasValveClient |
| 135..138 | PRG_VacuumOutputs | PLC210VacuumClient |
| 139 | PLC_PRG — the PLC's own 12 discrete inputs | PLC210PidClient |
| 140..147 | PRG_Thyracont, own 50 ms ThyracontTask | PLC210ThyracontClient |
| 150..157 | PLC_PRG bench levers (DO blink test, MU210 AO1) | — |
| 160..191 | PRG_Pyrometers | PLC210PyrometerClient |
| 192..205 | PRG_Microwave | PLC210MicrowaveClient |
| 206..239 | PRG_Cooling | PLC210CoolingClient |
| 240..247 | PRG_TurboPump | PLC210TurboPumpClient |
| 248..279 | PRG_Alarms reads (HMI writes thresholds) | PLC210AlarmClient |
| 280..295 | PRG_Alarms publishes alarm state | PLC210AlarmClient |

**Исключение из x1000: awHolding[140..143] — сырой IEEE REAL, младшее слово первым (24.09.2026).** Давление Hi-Vac (Torr в 140/141, mbar в 142/143) шло через `F_ToFixed`, и всё ниже ~5E-4 Torr приходило нулём — а экран выводит его как в ArdisCVDMaster, `0.###E-0`, где важен порядок. Теперь `PRG_Thyracont` пишет `uValue : U_DWORD_REAL`, а `PLC210ThyracontClient.ReadFloat` собирает float. ПЛК и HMI обновлять вместе: старый ПЛК с новым HMI даст мусор порядка 1E-42. Копия `PRG_Thyracont.st` в репозитории не совпадает с ПЛК (там `xEnable := FALSE` и порт 5, а датчик живой на другом порту) — в CODESYS переносить только строки 140..143 и VAR, не весь файл.

**awHolding grew to ARRAY[0..295] on 22.09.2026** for the alarm engine. The Modbus server's holding area in the device tree must be grown to 296 words with it, or the HMI gets IllegalDataAddress — the same trap the turbo pump hit.

**awHolding[248] carries 16#A5 in its high byte and that is load-bearing.** PRG_Alarms does not parse 249..279 at all without it. The thresholds live in `GVL_Alarms` as `VAR_GLOBAL RETAIN` so the machine is protected before the HMI connects; awHolding reads zeros after a PLC boot, so parsing it unconditionally would overwrite the surviving RETAIN copy with zeros and disarm every check at exactly the moment RETAIN existed for. The magic word distinguishes "the HMI sent a block with everything off" from "no block has ever arrived". For the same reason the HMI must write 248..279 in ONE FC16 — otherwise the magic can land before the values it commits.

Alarm codes are bit numbers in the 64-bit masks, one byte per subsystem: 0..7 setpoint bands, 8..15 temperatures, 16..23 cooling, 24..31 interlocks, 32..47 microwave generator reasons, 48..63 turbo pump. Full numbering in `codesys/comments.txt`. Link loss and reading validity are deliberately NOT in these masks — they belong to the Connection Status window. See [[alarm-abort-design]] and [[alarm-dead-sensor-open]].

Still free: 148, 149, 158, 159. `PLC210PidClient.OutputRegisterCount` is 40, i.e. it reads 100..139 in one go — that is the cheap place to hang a new HMI-visible flag, no ninth socket.

**awHolding[139], added 25.08.2026:** bit 0..7 = FDI1..FDI8, bit 8..11 = DI9..DI12, 1 = contact closed. The chamber lid switch is one of those bits; which one is chosen HMI-side from `config.ini` `[PLC210] LidInput` (default 1 = FDI1, never confirmed on the machine). The PLC's own I/O node also offers the whole group as one `Bit mask inputs` DWORD channel at `%ID51` — same bit order, provable from the addresses (`%ID51` = bytes 204..207, `Fast input 1` = `%IX204.0`, `Input 9` = `%IX205.0`). Either mapping works; the project currently uses twelve BOOLs in `GVL_PlcIO.st`.

**Signals that are only an echo, not confirmation — never treat as device state:**

- Microwave status bit 0x0020 (`PreheatOn`) is `stMw.xLastPreheatValue`, i.e. what the HMI last asked for, and the preheat counter `awHolding[205]` counts off the same request. Both run happily with the generator powered down. The real "the generator is answering" signal is `CommError` (= `MW_GEN.xError`), exposed HMI-side as `State.GeneratorAnswering`.
- **`awHolding[205]` is SECONDS, and must stay a real timer.** `PLC210MicrowaveClient` reads it as `PreheatElapsedSeconds` and `MainForm` counts it down against `PreheatSeconds = 150`. The CODESYS export committed in `279141b` replaced the TON with `stMw.iPreheatElapsedScans`, a per-scan counter — so preheat appeared to complete in about 5 s instead of 150. Fixed 07.09.2026 by restoring `fbPreheatTimer : TON` with `PT := T#1H` and publishing `TIME_TO_DWORD(fbPreheatTimer.ET) / DWORD#1000`. `ST_MicrowaveState.iPreheatElapsedScans` is left in place but unused. Anything published as a time here must come off a timer, never off a scan count — the task period is not 1 s.
- Measured values freeze rather than fall to zero when a device stops answering: `PRG_GasFlow` keeps the last word each РРГ-20 sent, `PRG_Microwave` the last incident/reflected, and `PLC210GasFlowClient` re-uses the previous `Channels` array when its TCP link drops. Per-regulator truth is `awHolding[96]` bit i (`MFC_*.xError`), exposed as `ChannelState.SlaveError`.

`PRG_Microwave.xMwPreheatTrigger` fires only on a *change* of the request, so a PREHEAT pressed while the generator is off never reaches it when the generator comes back. Not fixed — see [[ardis-cvdcore-open-work]].

See [[mv210-102-modbus-map]] for the МВ210-102 cooling modules, which are separate Modbus TCP slaves and not part of this array.
