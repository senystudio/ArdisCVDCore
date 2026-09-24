---
name: logging
description: "Логирование ArdisCVDCore (23.09.2026) — точная копия механики ArdisCVDMaster: NLog-журнал ошибок, ProcessLogs/*.dat, скрытый configBackup.bin; где что вызывается и сознательные отличия"
metadata:
  type: project
---

Сделано 23.09.2026 по требованию «в точности как в референсе, только значения подгоняй к этому проекту». Пользователь явно отверг расширения (журнал действий оператора, аварий, запуска) — в референсе их нет, значит и у нас нет. Не добавлять без запроса.

**Что делает референс — проверено по реальным файлам** в `C:\Users\sennix\Desktop\ArdisCVDMaster\ArdisCVDMaster\bin\x86\Release` в виртуалке, не только по коду:

1. `Logs\logfile.log` (NLog, ротация по дням `logfile.2026-09-22.0.log`). Только ошибки модулей, строка вида `2026-09-23 08:46:25.825 System.Exception: Turbo pump: …` — это `nlogger.Error(new Exception("<Модуль>: " + msg))`, layout `${message}` даёт `ex.ToString()`. Одна строка на обрыв: счётчик `errConnCount`, пишет при `< 2`, сбрасывается после удачного обмена. Плюс `"Crash! Reason: "` в `Program.cs`.
2. `ProcessLogs\Ardis_LogFile  N d.M.yyyy H.m.s.dat` (два пробела после LogFile). Снимок раз в тик SuperCycle во время Manual Run, список до 3600, выгрузка в `hh:00:00` + `N++`, при Stop — хвост от последнего `hh:00:00` (`CreateLogFileEnded`). Строки кончаются лишними табами после Warnings/Errors — повторено сознательно.
3. `configBackup.bin` — скрытый, XOR `"Ardis"`, `createdTime\nMWPower|<TimeSpan>\n`. Счётчик идёт по **команде** оператора МВ (`OpTimeLogging`), пишется при закрытии, время суммируется с файлом. Читается референсным `OpTimeViewer` (там он за паролем Service Mode) — своего окна просмотра у нас нет.

**Где у нас:** `data/Logger.cs` (только `WriteError`), `data/ProcessLogger.cs` (`ProcessSample.Capture` из `GetState()` клиентов + вся механика .dat и .bin), `NLog.config` байт в байт из референса (с дублирующимся target — так и работает), `lib/NLog.dll` из `packages/NLog.5.3.2/lib/net46`. В каждом из 10 клиентов `modules_hw/PLC210*Client.cs` — `_errConnCount` в `catch` WorkerLoop. Приборы за ПЛК пишут переход «работал → отвалился» в `LogDeviceFaults(plcState)`: МВ (`CommError`, плюс «MWPower internal error» по `FaultReportable`), привод турбо (`DriveAnswering`), каждый РРГ (`SlaveError`), Thyracont (`HasValidValue`), пирометры RXT/Smart (`Valid`), каналы МВ210 (одной строкой списком).

**Решения пользователя:** в .dat только наши колонки (без Rod, Plenum, RF, Plasma, DC Bias; Tuner встал в ряд контуров охлаждения); **никаких MessageBox** — в референсе `WriteError` открывал окно, у нас 10 клиентов и при потере ПЛК было бы 10 окон; наработка только МВ (RFPower из референса выкинут).

**Сознательные отличия от референса (все ради того, чтобы не терять данные):**
- «MWPower internal error» пишется один раз на появление, а не каждый цикл, как в `MWPower_Opto.DataHandling`.
- Запись файлов в `try/catch` с логом «ProcessLogger: …» — в референсе заблокированный Excel'ем файл уронил бы HMI.
- `CreateLogFileEnded` очищает список после выгрузки, поэтому после Stop та же сессия при закрытии второй раз не пишется (в референсе писалась под следующим N). Закрытие ждёт записи (`Wait(5000)`), иначе фоновая задача умирает вместе с процессом.
- Файлы кладутся в `Application.StartupPath`, а не в текущую директорию.

**Известная особенность, оставленная по решению пользователя:** если тик таймера пропустит ровно `:00`, почасовой файл за этот час не запишется — данные уйдут только при Stop/закрытии.

Колонки .dat: Time; 7 температур в порядке `CircuitNames`; Sample Temp_ch1/ch2/sum (активный пирометр); 7 протоков; SetIncPower/IncPower/ReflPower в Вт; ControlByte (бит0 Preheat, бит1 MW); Error_1/Error_2 = байты `FaultReasonBits`; WaterPressure; CDAPressure; SetChamberPressure (`ChamberPid.Setpoint`); ChamberPressure; HiVacChamberPressure; HiVacPumpEn/Speed (турбо); газы в порядке референса H2,N2,CH4,O2,Ar,H2_2 — по **имени** из `GasNames`, так что спорный порядок N2/O2 из [[ardis-cvdcore-open-work]] в лог не переносится, лог пишет то же, что экран; DOutput 1 = GPV1..8, DOutput 2 = VPV1..8, DOutput 3 = вода/форвак/турбо/МВ/Preheat, DInput 1 = FDI1..8; Warnings/Errors = `AlarmMask`/`AbortMask` 8 байт.

**Выключатель (23.09.2026): File → Start logging / Stop logging.** С 24.09.2026 при запуске программы логирование **включено** по просьбе пользователя: `Logger.Enabled = true` в инициализаторе поля, а пункты меню выставляет `SetLoggingMenu(Logger.Enabled)` в конструкторе `MainForm` (дизайнер не трогали, там Stop по-прежнему `Enabled = false` — его перекрывает конструктор). Один флаг `Logger.Enabled` управляет и `logfile.log` (`WriteError` молча выходит), и `.dat` (`Record` в `SuperCycle_Tick` гейтится им). Start logging посреди сессии начинает новый файл (`BeginSession`: новое `StartTime`, `N = 0`); Stop logging сразу пишет накопленное (`CreateLogFileEnded`). `CreateLogFileEnded` теперь сам очищает список, поэтому дубликатов не бывает при любом порядке Stop/Stop logging/закрытия. **Наработка МВ (`configBackup.bin`) выключателем не управляется** и считает всегда — это учёт часов оборудования, не лог; решение принято мной, пользователю сообщено. Пока логирование выключено, `errConnCount` и переходы приборов продолжают отслеживаться, просто не пишутся — обрыв, начавшийся при выключенном логе, после включения в журнал не попадёт.

Логи в `bin/Debug` добавлены в `.gitignore`. См. [[alarm-abort-design]], [[ardis-cvdcore-build]].
