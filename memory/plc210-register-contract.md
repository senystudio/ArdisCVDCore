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

Still free: 148, 149, 158, 159. `PLC210PidClient.OutputRegisterCount` is 40, i.e. it reads 100..139 in one go — that is the cheap place to hang a new HMI-visible flag, no ninth socket.

**awHolding[139], added 25.08.2026:** bit 0..7 = FDI1..FDI8, bit 8..11 = DI9..DI12, 1 = contact closed. The chamber lid switch is one of those bits; which one is chosen HMI-side from `config.ini` `[PLC210] LidInput` (default 1 = FDI1, never confirmed on the machine). The PLC's own I/O node also offers the whole group as one `Bit mask inputs` DWORD channel at `%ID51` — same bit order, provable from the addresses (`%ID51` = bytes 204..207, `Fast input 1` = `%IX204.0`, `Input 9` = `%IX205.0`). Either mapping works; the project currently uses twelve BOOLs in `GVL_PlcIO.st`.

**Signals that are only an echo, not confirmation — never treat as device state:**

- Microwave status bit 0x0020 (`PreheatOn`) is `stMw.xLastPreheatValue`, i.e. what the HMI last asked for, and the preheat counter `awHolding[205]` counts off the same request. Both run happily with the generator powered down. The real "the generator is answering" signal is `CommError` (= `MW_GEN.xError`), exposed HMI-side as `State.GeneratorAnswering`.
- **`awHolding[205]` is SECONDS, and must stay a real timer.** `PLC210MicrowaveClient` reads it as `PreheatElapsedSeconds` and `MainForm` counts it down against `PreheatSeconds = 150`. The CODESYS export committed in `279141b` replaced the TON with `stMw.iPreheatElapsedScans`, a per-scan counter — so preheat appeared to complete in about 5 s instead of 150. Fixed 07.09.2026 by restoring `fbPreheatTimer : TON` with `PT := T#1H` and publishing `TIME_TO_DWORD(fbPreheatTimer.ET) / DWORD#1000`. `ST_MicrowaveState.iPreheatElapsedScans` is left in place but unused. Anything published as a time here must come off a timer, never off a scan count — the task period is not 1 s.
- Measured values freeze rather than fall to zero when a device stops answering: `PRG_GasFlow` keeps the last word each РРГ-20 sent, `PRG_Microwave` the last incident/reflected, and `PLC210GasFlowClient` re-uses the previous `Channels` array when its TCP link drops. Per-regulator truth is `awHolding[96]` bit i (`MFC_*.xError`), exposed as `ChannelState.SlaveError`.

`PRG_Microwave.xMwPreheatTrigger` fires only on a *change* of the request, so a PREHEAT pressed while the generator is off never reaches it when the generator comes back. Not fixed — see [[ardis-cvdcore-open-work]].

See [[mv210-102-modbus-map]] for the МВ210-102 cooling modules, which are separate Modbus TCP slaves and not part of this array.
