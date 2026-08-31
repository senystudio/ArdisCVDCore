PROGRAM PRG_Microwave
VAR CONSTANT
    C_MIN_SETPOINT_KW : REAL := 1.0;
    C_MAX_SETPOINT_KW : REAL := 10.0;
    C_MIN_CHAMBER_PRESSURE_TORR : REAL := 9.0;
END_VAR
VAR
    stMw : ST_MicrowaveState;

    xMwPreheatRequest : BOOL;
    xMwOnRequest      : BOOL;
    xMwResetRequest   : BOOL;
    xMwOnAllowed      : BOOL;

    rChamberPressureTorr : REAL;
    xChamberPressureLow  : BOOL;

    xMwWorking      : BOOL;
    xMwFailure      : BOOL;
    xMwReflective   : BOOL;
    xMwPreheatDone  : BOOL;
    xMwCommError    : BOOL;
    xMwFaultActive  : BOOL;

    xMwFilamentFlowFault  : BOOL;
    xMwFilamentUnderflow  : BOOL;
    xMwMagnetronAbnormal1 : BOOL;
    xMwAnodeFlowFault     : BOOL;
    xMwFireFailure        : BOOL;
    xMwMagnetronTooWarm   : BOOL;
    xMwWaterFlowFault     : BOOL;

    wLiveFaultBits : WORD;

    rSetpointKw  : REAL;
    iRawSetpoint : INT;
    rIncidentKw  : REAL;
    rReflectedKw : REAL;
    dwValue      : DWORD;
    wStatus      : WORD;
    fbCommOk       : TON;
    fbConfirmTick  : TON;
    fbPreheatTimer : TON;
END_VAR

xMwPreheatRequest := (awHolding[192] AND WORD#16#0001) <> WORD#0;
xMwOnRequest      := (awHolding[192] AND WORD#16#0002) <> WORD#0;
xMwResetRequest   := (awHolding[192] AND WORD#16#0004) <> WORD#0;

fbCommOk(IN := NOT MW_GEN.xError, PT := T#4S);

(* NOT reversed -- confirmed only on axMwStatusBlock2 (see below), applying
   that to this block too was wrong (it misread coil 19 "working" as coil 20
   "failure" the moment Microwave actually started). *)
xMwWorking     := axMwStatusBlock1[0];
xMwFailure     := axMwStatusBlock1[1];
(* Reversed: index 0 = last coil in the block (39), not first (34) -- confirmed
   empirically (fault fired exactly when preheat completed, not on reflection). *)
xMwReflective  := axMwStatusBlock2[5];
xMwPreheatDone := axMwStatusBlock2[0];
xMwCommError   := NOT fbCommOk.Q;

xMwFilamentFlowFault  := axMwStatusBlock3[7];
xMwFilamentUnderflow  := axMwStatusBlock3[6];
xMwMagnetronAbnormal1 := axMwStatusBlock3[4];
xMwAnodeFlowFault     := axMwStatusBlock3[3];
xMwFireFailure        := axMwStatusBlock3[2];
xMwMagnetronTooWarm   := axMwStatusBlock3[1];
xMwWaterFlowFault     := axMwStatusBlock3[0];

xMwFaultActive := xMwFailure OR xMwReflective OR xMwCommError
    OR xMwFilamentFlowFault OR xMwFilamentUnderflow OR xMwMagnetronAbnormal1
    OR xMwAnodeFlowFault OR xMwFireFailure OR xMwMagnetronTooWarm OR xMwWaterFlowFault;

wLiveFaultBits := WORD#0;
IF xMwFailure THEN wLiveFaultBits := wLiveFaultBits OR WORD#16#0001; END_IF;
IF xMwReflective THEN wLiveFaultBits := wLiveFaultBits OR WORD#16#0002; END_IF;
IF xMwCommError THEN wLiveFaultBits := wLiveFaultBits OR WORD#16#0004; END_IF;
IF xMwFilamentFlowFault THEN wLiveFaultBits := wLiveFaultBits OR WORD#16#0008; END_IF;
IF xMwFilamentUnderflow THEN wLiveFaultBits := wLiveFaultBits OR WORD#16#0010; END_IF;
IF xMwMagnetronAbnormal1 THEN wLiveFaultBits := wLiveFaultBits OR WORD#16#0020; END_IF;
IF xMwAnodeFlowFault THEN wLiveFaultBits := wLiveFaultBits OR WORD#16#0040; END_IF;
IF xMwFireFailure THEN wLiveFaultBits := wLiveFaultBits OR WORD#16#0080; END_IF;
IF xMwMagnetronTooWarm THEN wLiveFaultBits := wLiveFaultBits OR WORD#16#0100; END_IF;
IF xMwWaterFlowFault THEN wLiveFaultBits := wLiveFaultBits OR WORD#16#0200; END_IF;

(* Always mirrors the live cause while faulted -- self-correcting every scan,
   no latch/edge-detection to get out of sync with reality. Goes to 0 on its
   own once the fault is actually gone; Reset doesn't touch it at all. *)
IF xMwFaultActive THEN
    stMw.wFaultReason := wLiveFaultBits;
ELSE
    stMw.wFaultReason := WORD#0;
END_IF;

IF xMwResetRequest AND (NOT stMw.xLastResetRequest) THEN
    xMwResetPending := TRUE;
END_IF;
stMw.xLastResetRequest := xMwResetRequest;

IF xMwPreheatRequest <> stMw.xLastPreheatValue THEN
    xMwPreheatValue := xMwPreheatRequest;
    xMwPreheatPending := TRUE;
    stMw.xLastPreheatValue := xMwPreheatRequest;
END_IF;

fbPreheatTimer(IN := xMwPreheatRequest AND (NOT xMwPreheatDone), PT := T#1H);

(* awHolding[130/131] is the chamber pressure PLC_PRG.st already measures and
   publishes (MV210, Torr) -- same array, no new channel needed. Below this
   threshold Microwave is refused regardless of what the operator asked for,
   and cut immediately if it drops while already running. *)
rChamberPressureTorr := F_ReadFixed(awHolding[130], awHolding[131]);
xChamberPressureLow := rChamberPressureTorr < C_MIN_CHAMBER_PRESSURE_TORR;

xMwOnAllowed := xMwOnRequest AND (NOT xChamberPressureLow);

IF xMwOnAllowed <> stMw.xLastOnValue THEN
    xMwOnValue := xMwOnAllowed;
    xMwOnPending := TRUE;
    stMw.xLastOnValue := xMwOnAllowed;
END_IF;

rSetpointKw := F_ReadFixed(awHolding[193], awHolding[194]);
IF rSetpointKw < C_MIN_SETPOINT_KW THEN
    rSetpointKw := C_MIN_SETPOINT_KW;
ELSIF rSetpointKw > C_MAX_SETPOINT_KW THEN
    rSetpointKw := C_MAX_SETPOINT_KW;
END_IF;
iRawSetpoint := REAL_TO_INT(rSetpointKw * 100.0 + 0.5);

IF iRawSetpoint <> stMw.iLastSetpointRaw THEN
    wMwSetpointRaw := INT_TO_WORD(iRawSetpoint);
    xMwSetpointPending := TRUE;
    stMw.iLastSetpointRaw := iRawSetpoint;
END_IF;

rIncidentKw  := INT_TO_REAL(WORD_TO_INT(awMwPowerRaw[0])) / 100.0;
rReflectedKw := INT_TO_REAL(WORD_TO_INT(awMwPowerRaw[1])) / 1000.0;

dwValue := F_ToFixed(rIncidentKw);
awHolding[197] := DWORD_TO_WORD(dwValue);
awHolding[198] := DWORD_TO_WORD(SHR(dwValue, 16));
dwValue := F_ToFixed(rReflectedKw);
awHolding[199] := DWORD_TO_WORD(dwValue);
awHolding[200] := DWORD_TO_WORD(SHR(dwValue, 16));

wStatus := WORD#0;
IF xMwWorking THEN wStatus := wStatus OR WORD#16#0001; END_IF;
IF xMwFailure THEN wStatus := wStatus OR WORD#16#0002; END_IF;
IF xMwReflective THEN wStatus := wStatus OR WORD#16#0004; END_IF;
IF xMwPreheatDone THEN wStatus := wStatus OR WORD#16#0008; END_IF;
IF xMwCommError THEN wStatus := wStatus OR WORD#16#0010; END_IF;
IF stMw.xLastPreheatValue THEN wStatus := wStatus OR WORD#16#0020; END_IF;
IF stMw.xLastOnValue THEN wStatus := wStatus OR WORD#16#0040; END_IF;
IF xMwFaultActive THEN wStatus := wStatus OR WORD#16#0080; END_IF;
IF xChamberPressureLow THEN wStatus := wStatus OR WORD#16#0100; END_IF;
IF xMwFilamentFlowFault THEN wStatus := wStatus OR WORD#16#0200; END_IF;
IF xMwFilamentUnderflow THEN wStatus := wStatus OR WORD#16#0400; END_IF;
IF xMwMagnetronAbnormal1 THEN wStatus := wStatus OR WORD#16#0800; END_IF;
IF xMwAnodeFlowFault THEN wStatus := wStatus OR WORD#16#1000; END_IF;
IF xMwFireFailure THEN wStatus := wStatus OR WORD#16#2000; END_IF;
IF xMwMagnetronTooWarm THEN wStatus := wStatus OR WORD#16#4000; END_IF;
IF xMwWaterFlowFault THEN wStatus := wStatus OR WORD#16#8000; END_IF;
awHolding[195] := wStatus;

awHolding[201] := awHolding[201] + WORD#1;
awHolding[202] := awMwPowerRaw[0];
awHolding[203] := awMwPowerRaw[1];
awHolding[204] := stMw.wFaultReason;
awHolding[205] := DWORD_TO_WORD(TIME_TO_DWORD(fbPreheatTimer.ET) / DWORD#1000);

fbConfirmTick(IN := MW_GEN.xError AND (NOT fbConfirmTick.Q), PT := T#4S);
MW_GEN.xConfirmError := fbConfirmTick.Q;
