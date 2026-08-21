    PROGRAM PLC_PRG
    VAR
        (* Modbus Holding Registers: awHolding now lives in GVL_ModbusHolding
        (shared with PRG_Thyracont on the fast ThyracontTask). Normal PID
        exchange does not map these values to physical outputs. The
        xDoBlinkTest variable below is a manual bench output test and must be
        mapped to a free DO channel in CODESYS I/O Mapping. The
        wBenchMuAo1Output variable is a manual bench analog-output command
        and must be mapped to the MU210 AO1 output/channel in CODESYS I/O
        Mapping or in the Modbus TCP Client slave channel mapping. *)
        xDoBlinkTest : BOOL;
        wBenchMuAo1Output : ARRAY[0..0] OF WORD;

        (* Traffic light (MU210-402 relay module, Ethernet Modbus TCP,
           192.168.1.15, no native OWEN device profile -- added as a generic
           Modbus TCP Slave). ArdisCVDMaster's PLC210PidClient writes these
           into awHolding[1] bits 4-6 every cycle unconditionally (same flags
           word as xChamberEnable/xPlenumEnable/xReset below), so the light
           reflects ErrorDispatcher.errSwitchStatus even before the PID panel
           has been touched. Physical wiring (confirmed on the panel, see
           terminal labels DO1A/B/C..DO4A/B/C): DO1=green, DO2=yellow,
           DO3=red, DO4..DO16=spare/unused -- common (+) is jumpered across
           the used channels' COM terminal.

           Output goes out as ONE bitmask word (wTrafficLightMask), not three
           coils: the MU210-402 answers FC05 (Write Single Coil) with Modbus
           exception 01 ILLEGAL FUNCTION -- confirmed live, the module simply
           does not implement coil functions, like most OWEN Mx210 modules.
           Its discrete outputs are driven by a holding register bitmask via
           FC16/FC06 instead. Bit0=DO1(green), bit1=DO2(yellow),
           bit2=DO3(red). Writing the whole word also forces DO4..DO16 to 0,
           which is fine here -- nothing else is wired to this module. Map
           wTrafficLightMask to a single Modbus channel (see comment on the
           mask assembly further down). *)
        xTrafficLightGreen  : BOOL;
        xTrafficLightYellow : BOOL;
        xTrafficLightRed    : BOOL;
        wTrafficLightMask   : WORD;

        (* HMI link watchdog. The lamp bits below are plain holding registers:
           when the HMI dies its TCP socket drops, but the PLC keeps the last
           flags word it was given, so a green tower goes on asserting "all
           good" with nobody left producing that verdict. Detected here without
           any change on the HMI side, by a probe bit the PLC arms and the HMI
           unknowingly clears: bit 15 of awHolding[1] is not part of the
           protocol (ComputeFlags in PLC210PidClient.cs only ever sets bits
           0..6), and the HMI rewrites that whole word every exchange, ~200 ms,
           whether or not SET has been pressed. So the PLC sets bit 15 at the
           end of every cycle and looks at it again on the next one: cleared =
           the HMI wrote in between, still set = nobody did. The heartbeat at
           awHolding[2..3] is NOT used for this -- the HMI only writes that
           block once SET has been pressed on the Chamber panel, so it would
           read as "no link" through an entire healthy session before that.

           While the link is stale the tower ignores the stale lamp bits and
           blinks yellow instead -- "running, but unattended". Same state at
           power-up, before the HMI has ever connected. Nothing else changes:
           the PID keeps running exactly as it did before, which is a policy
           decision nobody has taken yet. *)
        xHmiWriteSeen         : BOOL;
        udiHmiWritesSeen      : UDINT;
        fbHmiStale            : TON;
        fbBlink               : TON;
        xHmiLinkLost          : BOOL;
        xTrafficLightBlinkOn  : BOOL := TRUE;

        (* Bench lever: not written by anything, so it can be forced from
           the CODESYS online view to make the tower blink on demand. Use
           it to separate "the watchdog never fires" from "the blink never
           reaches the lamp" -- those are different faults with the same
           symptom. Leave it FALSE in production. *)
        xForceHmiLinkLost     : BOOL;
        
        awMvAi1Value  : ARRAY[0..1] OF WORD;
        awMvAi1Status : ARRAY[0..0] OF WORD;
        uMvAi1Value   : U_DWORD_REAL;
        wMvAi1Status  : WORD;
        xMvAi1Ok      : BOOL;
        rMvAi1Percent : REAL;
        rMvAi1Voltage : REAL;
        rChMeasuredMv : REAL;
        
        rChamberMuAo1Command : REAL;
        xChamberMuAo1Clamped : BOOL;
        uiChamberMuAo1Command : UINT;

        fbChamber : FB_IncrementalPid;
        fbPlenum  : FB_IncrementalPid;
        (* fbThyracont moved to PRG_Thyracont (own fast ThyracontTask). *)

        xProtocolOk    : BOOL;
        xChamberEnable : BOOL;
        xPlenumEnable  : BOOL;
        xReset         : BOOL;
        udiCycle       : UDINT;

        rChSetpoint : REAL;
        rChMeasured : REAL;
        rChKp       : REAL;
        rChKi       : REAL;
        rChKd       : REAL;
        rChLower    : REAL;
        rChUpper    : REAL;
        rChDirect   : REAL;

        rPlSetpoint : REAL;
        rPlMeasured : REAL;
        rPlKp       : REAL;
        rPlKi       : REAL;
        rPlKd       : REAL;
        rPlLower    : REAL;
        rPlUpper    : REAL;
        rPlDirect   : REAL;
        dwValue     : DWORD;
        xDoBlinkTestEnable : BOOL;
        xBenchMuAo1Enable : BOOL;
        xBenchMuAo1Clamped : BOOL;
        uiBenchMuAo1Command : UINT;
        uiBenchMuAo1Applied : UINT;
        awMvAi1Raw : ARRAY[0..1] OF WORD;
    END_VAR

    VAR CONSTANT
        (* Real time, not cycle counts: the first version counted MainTask
           cycles assuming 200 ms, which makes the blink rate a hostage to
           the task period -- at a 20 ms task it would toggle every 40 ms,
           far too fast for the relay module (and for the Modbus master's
           own polling interval) to reproduce, which looks exactly like
           "not blinking". 1 s on / 1 s off is slow enough to survive any
           plausible task and bus period. 3 s of silence before the link
           counts as lost -- the HMI writes every ~200 ms. *)
        HMI_STALE_TIME    : TIME := T#3S;
        BLINK_HALF_PERIOD : TIME := T#1S;
    END_VAR

xProtocolOk := awHolding[0] = WORD#1;
xChamberEnable := xProtocolOk AND (awHolding[1] AND WORD#16#0001) <> WORD#0;
xPlenumEnable := xProtocolOk AND (awHolding[1] AND WORD#16#0002) <> WORD#0;
xReset := (awHolding[1] AND WORD#16#0004) <> WORD#0;
xTrafficLightGreen  := (awHolding[1] AND WORD#16#0010) <> WORD#0;
xTrafficLightYellow := (awHolding[1] AND WORD#16#0020) <> WORD#0;
xTrafficLightRed    := (awHolding[1] AND WORD#16#0040) <> WORD#0;

(* HMI link watchdog -- see the declarations above for how the probe bit
   works. Read before the bit is re-armed at the end of this block.
   udiHmiWritesSeen is pure diagnostics: watch it in the online view and it
   runs while the HMI is alive, stops dead when it is not. If it never
   moves at all, the probe bit is not surviving the Modbus slave device's
   process image and this whole approach is the wrong one -- say so and it
   gets rebuilt on the awHolding[2..3] heartbeat instead. *)
xHmiWriteSeen := (awHolding[1] AND WORD#16#8000) = WORD#0;

IF xHmiWriteSeen THEN
    udiHmiWritesSeen := udiHmiWritesSeen + UDINT#1;
END_IF;

fbHmiStale(IN := NOT xHmiWriteSeen, PT := HMI_STALE_TIME);
xHmiLinkLost := fbHmiStale.Q OR xForceHmiLinkLost;

(* Free-running blink. The TON retriggers itself: Q is true for exactly one
   cycle each half period, which flips the lamp state. *)
fbBlink(IN := NOT fbBlink.Q, PT := BLINK_HALF_PERIOD);
IF fbBlink.Q THEN
    xTrafficLightBlinkOn := NOT xTrafficLightBlinkOn;
END_IF;

(* Re-arm the probe. Only bit 15 is touched, so the protocol bits 0..6 stay
   exactly as the HMI wrote them, and PRG_GasFlow -- which reads bit 3 of this
   same word on its own task -- is unaffected. *)
awHolding[1] := awHolding[1] OR WORD#16#8000;

(* Assemble the MU210-402 output bitmask. Map wTrafficLightMask to that
   module's single "discrete output state" holding register, written with
   FC16 (Write Multiple Registers, length 1) or FC06 (Write Single
   Register) -- NOT FC05, which the module rejects with ILLEGAL FUNCTION. *)
wTrafficLightMask := WORD#0;
IF xHmiLinkLost THEN
    (* The lamp bits are stale by definition here, so they are not consulted
       at all: blinking yellow is the whole verdict. *)
    IF xTrafficLightBlinkOn THEN
        wTrafficLightMask := WORD#16#0002;
    END_IF;
ELSE
    IF xTrafficLightGreen THEN
        wTrafficLightMask := wTrafficLightMask OR WORD#16#0001;
    END_IF;
    IF xTrafficLightYellow THEN
        wTrafficLightMask := wTrafficLightMask OR WORD#16#0002;
    END_IF;
    IF xTrafficLightRed THEN
        wTrafficLightMask := wTrafficLightMask OR WORD#16#0004;
    END_IF;
END_IF;

rChamberMuAo1Command := fbChamber.rOutput / 10.0;
xChamberMuAo1Clamped := rChamberMuAo1Command > 500.0;

IF xChamberMuAo1Clamped THEN
    uiChamberMuAo1Command := UINT#500;
ELSE
    uiChamberMuAo1Command := REAL_TO_UINT(rChamberMuAo1Command);
END_IF;

wMvAi1Status := awMvAi1Status[0];
xMvAi1Ok := wMvAi1Status = WORD#16#0000;

uMvAi1Value.dwValue := SHL(WORD_TO_DWORD(awMvAi1Value[1]), 16) OR WORD_TO_DWORD(awMvAi1Value[0]);	
rMvAi1Percent := uMvAi1Value.rValue;
rMvAi1Voltage := LIMIT(0.0, rMvAi1Percent / 10.0, 10.0);

IF xMvAi1Ok THEN
    rChMeasuredMv := LIMIT(0.0, rMvAi1Percent * 10.0, 1000.0);
END_IF;

rChSetpoint := F_ReadFixed(awHolding[4], awHolding[5]);
rChMeasured := rChMeasuredMv;
rChKp := F_ReadFixed(awHolding[8], awHolding[9]);
rChKi := F_ReadFixed(awHolding[10], awHolding[11]);
rChKd := F_ReadFixed(awHolding[12], awHolding[13]);
rChLower := F_ReadFixed(awHolding[14], awHolding[15]);
rChUpper := F_ReadFixed(awHolding[16], awHolding[17]);
rChDirect := F_ReadFixed(awHolding[18], awHolding[19]);

rPlSetpoint := F_ReadFixed(awHolding[30], awHolding[31]);
rPlMeasured := F_ReadFixed(awHolding[32], awHolding[33]);
rPlKp := F_ReadFixed(awHolding[34], awHolding[35]);
rPlKi := F_ReadFixed(awHolding[36], awHolding[37]);
rPlKd := F_ReadFixed(awHolding[38], awHolding[39]);
rPlLower := F_ReadFixed(awHolding[40], awHolding[41]);
rPlUpper := F_ReadFixed(awHolding[42], awHolding[43]);
rPlDirect := F_ReadFixed(awHolding[44], awHolding[45]);

fbChamber(
    xEnable := xChamberEnable,
    xReset := xReset,
    xDirectMode := awHolding[20] <> WORD#0,
    rSetpoint := rChSetpoint,
    rMeasured := rChMeasured,
    rKp := rChKp,
    rKi := rChKi,
    rKd := rChKd,
    rLowerLimit := rChLower,
    rUpperLimit := rChUpper,
    rDirectValue := rChDirect,
    rCycleSeconds := 0.2);

fbPlenum(
    xEnable := xPlenumEnable,
    xReset := xReset,
    xDirectMode := awHolding[46] <> WORD#0,
    rSetpoint := rPlSetpoint,
    rMeasured := rPlMeasured,
    rKp := rPlKp,
    rKi := rPlKi,
    rKd := rPlKd,
    rLowerLimit := rPlLower,
    rUpperLimit := rPlUpper,
    rDirectValue := rPlDirect,
    rCycleSeconds := 0.2);

udiCycle := udiCycle + 1;
xDoBlinkTestEnable := awHolding[150] = WORD#16#A55A;
xDoBlinkTest := xDoBlinkTestEnable AND ((udiCycle MOD UDINT#10) < UDINT#5);

awHolding[100] := WORD#1;
IF xProtocolOk THEN
    awHolding[101] := WORD#16#0003;
ELSE
    awHolding[101] := WORD#0;
END_IF;
awHolding[102] := DWORD_TO_WORD(udiCycle);
awHolding[103] := DWORD_TO_WORD(SHR(udiCycle, 16));

dwValue := F_ToFixed(fbChamber.rError);
awHolding[104] := DWORD_TO_WORD(dwValue);
awHolding[105] := DWORD_TO_WORD(SHR(dwValue, 16));
dwValue := F_ToFixed(fbChamber.rP);
awHolding[106] := DWORD_TO_WORD(dwValue);
awHolding[107] := DWORD_TO_WORD(SHR(dwValue, 16));
dwValue := F_ToFixed(fbChamber.rI);
awHolding[108] := DWORD_TO_WORD(dwValue);
awHolding[109] := DWORD_TO_WORD(SHR(dwValue, 16));
dwValue := F_ToFixed(fbChamber.rD);
awHolding[110] := DWORD_TO_WORD(dwValue);
awHolding[111] := DWORD_TO_WORD(SHR(dwValue, 16));
dwValue := F_ToFixed(fbChamber.rOutput);
awHolding[112] := DWORD_TO_WORD(dwValue);
awHolding[113] := DWORD_TO_WORD(SHR(dwValue, 16));

dwValue := F_ToFixed(fbPlenum.rError);
awHolding[120] := DWORD_TO_WORD(dwValue);
awHolding[121] := DWORD_TO_WORD(SHR(dwValue, 16));
dwValue := F_ToFixed(fbPlenum.rP);
awHolding[122] := DWORD_TO_WORD(dwValue);
awHolding[123] := DWORD_TO_WORD(SHR(dwValue, 16));
dwValue := F_ToFixed(fbPlenum.rI);
awHolding[124] := DWORD_TO_WORD(dwValue);
awHolding[125] := DWORD_TO_WORD(SHR(dwValue, 16));
dwValue := F_ToFixed(fbPlenum.rD);
awHolding[126] := DWORD_TO_WORD(dwValue);
awHolding[127] := DWORD_TO_WORD(SHR(dwValue, 16));
dwValue := F_ToFixed(fbPlenum.rOutput);
awHolding[128] := DWORD_TO_WORD(dwValue);
awHolding[129] := DWORD_TO_WORD(SHR(dwValue, 16));

(* Chamber pressure as actually measured from MV210 (rChMeasuredMv), published
   so the HMI can read it -- previously nothing exposed this over Modbus.
   130/131 and 132 are free registers inside the 100..137 block the HMI
   already reads every cycle, so no extra Modbus round trip is needed. *)
dwValue := F_ToFixed(rChMeasuredMv);
awHolding[130] := DWORD_TO_WORD(dwValue);
awHolding[131] := DWORD_TO_WORD(SHR(dwValue, 16));
awHolding[132] := wMvAi1Status;

(* Thyracont poll + awHolding[140..147] publishing moved to PRG_Thyracont,
   which runs on its own fast cyclic task (ThyracontTask, 50 ms) so the
   sensor updates several times per second instead of once per 600-800 ms. *)

uiBenchMuAo1Command := WORD_TO_UINT(awHolding[154]);
xBenchMuAo1Enable := awHolding[155] = WORD#16#A55A;
xBenchMuAo1Clamped := uiBenchMuAo1Command > UINT#500;

IF xBenchMuAo1Clamped THEN
    uiBenchMuAo1Command := UINT#500;
END_IF;

IF xBenchMuAo1Enable THEN
    uiBenchMuAo1Applied := uiBenchMuAo1Command;
ELSE
    uiBenchMuAo1Applied := UINT#0;
END_IF;


IF xBenchMuAo1Enable THEN
    awHolding[157] := awHolding[157] OR WORD#16#0001;
END_IF;

IF xBenchMuAo1Clamped THEN
    awHolding[157] := awHolding[157] OR WORD#16#0002;
END_IF;


IF fbChamber.rOutput <= 0.0 THEN
    uiChamberMuAo1Command := UINT#0;
    xChamberMuAo1Clamped := FALSE;
ELSE
    rChamberMuAo1Command := fbChamber.rOutput / 10.0;
    xChamberMuAo1Clamped := rChamberMuAo1Command > 500.0;

    IF xChamberMuAo1Clamped THEN
        uiChamberMuAo1Command := UINT#500;
    ELSE
        uiChamberMuAo1Command := REAL_TO_UINT(rChamberMuAo1Command);
    END_IF;
END_IF;

IF xBenchMuAo1Enable THEN
    uiBenchMuAo1Applied := uiBenchMuAo1Command;
ELSIF xChamberEnable THEN
    uiBenchMuAo1Applied := uiChamberMuAo1Command;
ELSE
    uiBenchMuAo1Applied := UINT#0;
END_IF;

wBenchMuAo1Output[0] := UINT_TO_WORD(uiBenchMuAo1Applied);
awHolding[156] := wBenchMuAo1Output[0];

