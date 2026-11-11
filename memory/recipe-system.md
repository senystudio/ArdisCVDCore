---
name: recipe-system
description: "Перенос Recipe Editor и выполнения рецептов из ArdisCVDMaster (начат 11.11.2026): все решения пользователя по объёму, железу, формату, МВ, авариям, блокировкам, Stop"
metadata:
  type: project
---

11.11.2026 пользователь попросил перенести Recipe Editor из ArdisCVDMaster «полностью, такой же дизайн, но под наши датчики». Источник: `/Users/sennix/Desktop/ArdisCVDMaster/ArdisCVDMaster/RecipeEditor.cs`, `RecipeEditor.Designer.cs`, `RecipieSelector.cs`, `data/StepData.cs`; автомат — `ArdisControlForm.cs` (SuperCycle_Tick 420..640, AutomodeOn 1095..1230, RecipeOpen 1416, PauseOn 2140) и `modules_logic/{Gases,ChamberPressure,MWPower,Valves}.cs` (CalculateAutomode: Init + (End-Init)/StepSeconds·tick).

**Решения пользователя (все — его ответы, не мои догадки):**
- Объём: Editor + выполнение рецепта в Automatic Mode. Recipe Recorder НЕ переносится.
- Строки без железа (Plenum, DC Bias U/I, Rod Pos/speed, Tuner left/right/Autotune, Bubbler, GPV9–16, VPV3, VPV8, входы PS1/PS2/Rod Line): **скрыть, места оставить** (Visible = false, раскладка Master не двигается).
- Chamber PID direct mode у нас ЕСТЬ (`ChamberPid.DirectMode/DirectValue`, 0..5000) — показать и выполнять.
- Формат: совместимый XML `List<StepItemData>` как в Master, старые рецепты открываются.
- МВ в редакторе в **kW** (MW Inc Init/End); Reflected alarm/abort в W, как в Process Parameters.
- МВ в шаге: рецепт **сам включает Preheat, шаг ждёт** (таймер стоит) до FilamentPreheatDone, потом MICROWAVE и дальше.
- Alarm/Abort шага **заменяют пороги в ПЛК** на старте каждого шага; после рецепта возвращаются пороги Process Parameters.
- Блокировки (VPV4+VPV5, турбо без VPV7/форвакуума, форвакуум off при крутящемся турбо): **пауза + сообщение**, запрещённая команда не выполняется.
- Турбо, ждущий давления < 2 Torr: **рецепт ждёт сам** (таймер шага стоит, надпись waiting), запускает турбо при выполнении условий.
- Stop без «Continue Manual Mode»: **как Master, но безопасно** — МВ/Preheat off, газы 0, все клапаны закрыть, турбо стоп, вода off; форвакуум off только если турбо стоит, иначе остаётся с сообщением. С галкой — переход в Manual Run, всё как есть.
- Ручные группы во время рецепта **выключены**; доступны Pause/Stop.
- Предустановка PID камеры (см. [[chamber-pid-preset]]): взводится **на старте шага**, если уставка давления или газы изменились относительно предыдущего шага (по Init).

См. [[ardis-reference-projects]], [[ardis-cvdcore-working-style]].

**Что сделано 11.11.2026 (отредактировано, НЕ собрано):**
- `data/StepData.cs` — `StepItemData` + `DataSerializer` из Master без комментариев и без кода MemoryMappedFile. XML совместим (корень `ArrayOfStepItemData`).
- `RecipeEditor.Designer.cs` / `RecipeSelector.Designer.cs` — копии из Master, изменён только namespace. `RecipeEditor.cs` — логика Master; подгонка в `AdaptToMachine()`. **В дизайнере Master строки AR и H22, GPV5/GPV6 и GCF AR уже были скрыты** — у нас эти газы и клапаны есть, поэтому показаны. Скрыты: Bias U/I, DC Bias Enable, Tuner/Autotune (+ то, что было скрыто в Master: Plenum, Rod, Bubbler, GPV9–16, VPV3/8, PS1/PS2/Rod Line, abort для Sample, GCF H22). Пределы газов = `PLC210GasFlowClient.FullScaleSccm`, давление ≤ 500 Torr (как на главном окне), MW Inc 0..10 kW.
- Исправлены баги Master: H22 abort брался из H2_AbortEn, GPV15 сохранялся из Valve_14, первый «Insert Step After» сразу вставлял шаг, часы > 23 терялись (StepDuration.Hours), TimeSpan.Parse падал на ≥ 24 ч, нет исключений на значениях вне диапазона (clamp).
- Рецепты лежат в `<exe>\recipes\` (`RecipeEditor.RecipeFolder`); меню File → Recipe Editor добавляется кодом (`AddRecipeEditorMenu`).
- Исполнитель — `MainForm.Recipe.cs` (partial MainForm), такт = SuperCycle 1 с, `RecipeTick()` вызывается до `PushChamberChannel`. Рампа как в Master: Init + (End−Init)·k/N, шаг длится max(1, N) тактов; таймер шага стоит на паузе и при ожидании (preheat, турбо, VPV4/5 переключение). Open отказывает загрузить рецепт, если MW > 10 kW (подсказка: рецепт Master в Ваттах), газ выше шкалы или давление > 500.
- Ожидание VPV4↔VPV5: при смене шаг сначала закрывает одну, открывает другую только когда обратная связь показала закрытой — моё решение, у пользователя не спрошено.
- Газ-поток для PID: рецепт пишет рампу в спиннеры `_gasSetpoint`, затем `UpdateChamberGasFlow()` (механизм сдвига выхода PID от другой сессии 11.11.2026); предустановка `ArmChamberPreset()` взводится, когда фактическая маска газовых клапанов совпала с шагом.

**Ответы пользователя 11.11.2026:**
- Прогрев — **отдельная галка «MW Preheat»** в шаге (Logic Output, на месте скрытого DC Bias Enable; создаётся кодом в `AdaptToMachine`). Поле XML `MWPower.Preheat_Enable` — новое, Master его просто игнорирует, старые рецепты читаются как false. Логика: MWPower Enable → прогрев + ожидание + MICROWAVE; только MW Preheat → MICROWAVE off, Preheat держится (без ожидания); ни одной галки → MICROWAVE и Preheat off. Блокировки прогрева: Abort, МВ-трип, неизвестное состояние аварий, генератор не отвечает (форвакуум/вода — только для MICROWAVE).
- Входы в новом шаге — **выключены, как в Master** (оставлено сознательно, хотя шаг заменяет пороги и без галок ПЛК входы не проверяет).
- PID Direct после рецепта — **оставлять как есть** (как в последнем шаге).
- Не обсуждалось: GCF шага только хранится.

**МВ-трип (11.11.2026, по договорённости с сессией ardiscvdcore-29):** неисправность генератора теперь не Abort, а отдельный трип `PLC210AlarmClient.State.MicrowaveTripped` (бит 0x0020 awHolding[292]), сброс — `PLC210AlarmClient.RequestReset()` из Fault Status. Пока трип или Abort активны, HMI **не должен** слать RequestPreheat(true)/RequestMicrowave(true): ПЛК держит `xMwInhibit`, пока бит в awHolding[192] равен 1, и Preheat «залипает» до STOP. Поэтому `DescribeMicrowaveHardBlocker` первым делом проверяет AbortActive и MicrowaveTripped (→ пауза с сообщением), а при снятии паузы сбрасывается `_autoMicrowaveRequested`.
