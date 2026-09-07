---
name: pyrometer-mw-rs485-conflict
description: "Why PYRO_RXT hangs on the RS-485 behind MKON — diagnosed 26.08.2026, MW_GEN on the same segment is the trigger"
metadata:
  node_type: memory
  type: project
---

**Symptom:** `PYRO_RXT` stops answering Modbus at random, stays dead until its power is cycled. Not correlated with the microwave firing. Used to require de-energising the whole rig.

**Topology (from the CODESYS device tree, 26.08.2026):** `Ethernet → Modbus_TCP_Master → MKOH (ModbusTCP Slave) → PYRO_RXT + MW_GEN`, both as `Modbus Slave, COM Port`. So the pyrometer and the 10 kW generator share ONE RS-485 segment behind the МКОН gateway. The six `MFC_*` are elsewhere — on the PLC's own `Modbus_COM → Modbus_Master_COM_Port`. There is no `PYRO_SMART` device in the tree at all, so `MainForm.SelectActivePyrometer`'s fallback to SMART is dead code and `PRG_Pyrometers.st` only ever publishes RXT (awHolding[160..167]).

**What the evidence ruled out, in order:**

- *Not a CODESYS error latch.* Status tab showed 3659 requests / 1 error, and after Acknowledge the Request Counter resumed **and the Error Counter climbed with it** — CODESYS polls fine, the device is genuinely silent.
- *Not the МКОН or the bus.* `MW_GEN` on the same segment stayed clean while the pyrometer was dead.
- *Not missing registers.* The user suspected Ch2/Ratio do not exist. Channel list is 0 AdcStatus 16#0000, 1 DevStatus 16#0005, 2 Ch1 16#0008, 3 Ch2 16#000A, 4 Ratio 16#000C, all FC04. The one logged error was on Channel Index 3 = Ch2, but that is coincidence: ~730 polls per channel with a single failure means Ch2 answers normally.
- *Not poll rate.* Slowing all five channels from `t#500ms` to `t#2000ms` changed nothing.
- *Not a powered-down generator.* The generator was under power when the pyrometer died.

**The finding that matters:** with `MW_GEN` deleted from the device tree so it is not polled, **the pyrometer does not drop out at all.** The two devices sharing the RS-485 segment is the cause — most likely a line-turnaround/framing problem (driver release timing, inter-frame silence, or missing fail-safe bias) that leaves the pyrometer's receiver wedged mid-frame.

**The real fix, not yet done. Both PLC RS-485 ports are already taken** — confirmed by the user 26.08.2026: port 1 carries the six MFCs (`Modbus_Master_COM_Port`), port 2 carries the Thyracont gauge (`FB_ThyracontAscii`, a hand-rolled serial driver, which is why it is not a Modbus node in the device tree). So moving the pyrometer onto a free PLC port is NOT an option.

Two remaining routes: (a) a **second МКОН** dedicated to the pyrometer — in CODESYS just another ModbusTCP Slave node, the device instance stays `PYRO_RXT` so no ST or HMI change; (b) fix the shared segment electrically — 120 Ω termination at both physical ends only, fail-safe bias resistors, a **lower baud rate** (the highest-yield free lever for this failure mode), and a longer inter-request pause in the МКОН config. Try (b) before buying.

**Stopgap shipped 26.08.2026** (edited, NOT flashed): `PRG_Pyrometers.st` and `PRG_Microwave.st` pulse `PYRO_RXT.xConfirmError` / `MW_GEN.xConfirmError` once a second while the slave is in error. This does **not** revive a hung pyrometer — Acknowledge does not either — it only means the link comes back by itself once the device is power-cycled, and that the generator reconnects on its own if it was off at PLC start. Both programs also got a 1 s good-scan debounce (`iPyroRxtGoodScans`, `iMwGoodScans`) because the once-a-second confirm makes `xError` blink FALSE for a few scans; without the filter the HMI would flash "Microwave — Idle" and a stale valid temperature on a dead device.

**Планировщик шины на ModbusChannel, 31.08.2026 (отредактировано, НЕ прошито).** После трёх неудачных итераций по таймерам пользователь сформулировал требование точно: за цикл 200 мс сначала опрашиваются ТРИ канала пирометра (Ch1 16#0008, Ch2 16#000A, Ratio 16#000C) и пока не получены все три ответа — ничего больше не делать, затем по порядку все каналы MW. Он же подсказал правильный механизм: триггер канала `Application`.

**Проверено по документации CODESYS, не выдумано:** триггер канала бывает Cyclic / Rising edge / **Application**; при Application запрос запускается блоком `ModbusChannel` из драйверной библиотеки. Интерфейс: `slave` (ссылка на экземпляр слейва), `iChannelIndex : INT` (номер канала из первой колонки списка каналов, 0-based), `xExecute`, `xAbort`; выходы `xBusy`, **`xDone`**, `xError`, `xAborted`, `ModbusError`. Это и есть та обратная связь «ответ получен», которой не было у Rising edge — раньше приходилось угадывать таймерами.

**Типы совпали (проверено на вкладке IEC Objects):** `PYRO_RXT` имеет тип `ModbusTCPSlaveUnit_Diag` (вариант _Diag потому, что в PLC Settings включён «Enable diagnosis for devices»), а `ModbusTCPSlaveUnit` наследуется от `ModbusTCPSlaveBase` — именно этот тип ждёт вход `slave` у `IoDrvModbusTCP.ModbusChannel`. В проекте стоит IoDrvModbusTCP 3.5.17.0, блок в ней есть. Значит слейвы за шлюзом МКОН передаются в блок напрямую.

**Скорость поднята на 115200 (31.08.2026)** — пользователь сам это сделал. Это снимает прежний потолок: при 9600 семь транзакций не влезали в 200 мс, теперь время в эфире копеечное и бюджет определяется откликом самих приборов плюс тактами задачи.

**Устройство `PRG_McohBus.st`:** автомат из четырёх состояний (IDLE/START/WAIT/ABORT), два экземпляра `ModbusChannel` — один привязан к PYRO_RXT, второй к MW_GEN, оба вызываются в конце программы каждый цикл. Шаги 0–2 пирометр, 3–6 чтения MW (Power 0, Status1 1, Status2 2, Status3 7), 7–10 записи по одной и только если стоит соответствующий `*Pending` (Reset 5, Run 4, Setpoint 6, Preheat 3). На каждом шаге ждём `xDone` или `xError`; если шаг завис дольше `C_STEP_TIMEOUT` 100 мс — `xAbort`, счётчик `udiStepTimeout`, идём дальше, чтобы мёртвый прибор не вешал весь цикл. Диагностика в онлайне: `udiCycles`, `udiStepErrors`, `udiStepTimeout`, `tCycleElapsed` и `tCycleWorst` — последние два показывают реальную длительность прохода, по ним и подбирается период задачи.

**Такты задачи — главный расход времени.** Блоку нужен спад `xExecute` между запросами, поэтому один шаг стоит примерно три такта задачи плюс само время транзакции. При задаче 10 мс семь чтений дают ~210 мс и в бюджет не влезают; при 5 мс выходит ~125 мс. Поэтому MKOHTask ставится на 5 мс, и проверяется по `tCycleWorst`. MKOHTask остаётся задачей цикла шины (самая быстрая в проекте при `<unspecified>`), иначе драйвер не будет тикать достаточно часто и `xDone` придёт с задержкой.

**Переменные-триггеры больше не нужны:** при Application строки Trigger в соотнесении исчезают, поэтому из `GVL_PyroIO` убраны все пять `xPyro*Trigger`, из `GVL_MicrowaveIO` — `xMwPowerTrigger`, `xMwStatus1..3Trigger`, `xMwPreheatTrigger`, `xMwOnTrigger`, `xMwSetpointTrigger`. **`xMwResetTrigger` ОСТАВЛЕН** — он был привязан дважды, и второй строкой идёт записываемое ЗНАЧЕНИЕ катушки `Mw_ResetCmd` (%QB1009), которое никуда не делось; планировщик ставит его в TRUE перед выполнением канала Reset.

**Карта каналов MW_GEN (со скриншота 28.08.2026):** 0 Mw_PowerReadings FC03 16#0004 len 2; 1 Mw_StatusBlock1 FC01 16#0013 len 2 (катушки 19–20); 2 Mw_StatusBlock2 FC01 16#0022 len 6 (катушки 34–39); 7 Mw_StatusBlock3 FC01 16#0015 len 8 (катушки 21–28); записи 3 Mw_PreheatCmd FC05 16#0000, 4 Mw_RunCmd FC05 16#0002, 5 Mw_ResetCmd FC05 16#0003, 6 Mw_SetpointCmd FC06 16#0065. Блоки 1 и 3 лежат ПОДРЯД (19–28), весь диапазон 19–39 читается одним FC01 из 21 катушки. Слить их в один канал = 4 транзакции MW превращаются в 2, но раскладка бит подобрана опытным путём (блок 2 читается задом наперёд, index 0 = катушка 39), так что после слияния её надо пересверять на стенде. Не сделано, предложено пользователю.

**Скорость МКОН 9600 — это главный физический потолок.** Одна транзакция при 8N1 это 25–40 мс (запрос 8 байт, отклик прибора, ответ, пауза 3.5 символа). Пакет MW из 4 транзакций занимает ~130 мс из каждых 200, то есть шина загружена на 60–80%. Поэтому кадр пирометра стоит в слоте 3 (t=150 мс) — после пакета MW, а не между: до следующего пакета остаётся ~20 мс запаса. Это работает, но впритык, и любая лишняя задержка (медленный отклик прибора, своя пауза между запросами в настройках МКОН) съест запас. Настоящий выход один из двух: слить катушки в один запрос (пакет MW станет ~65 мс) или поднять скорость до 38400 (пакет ~50 мс). Пользователю задан вопрос, тянут ли генератор и пирометр больше 9600.

See [[ardis-cvdcore-open-work]] and [[plc210-register-contract]].
