PROGRAM PRG_GasFlow
VAR CONSTANT
    aFullScaleSccm : ARRAY[0..5] OF REAL := [1000.0, 100.0, 50.0, 1000.0, 10.0, 1000.0];
    C_INIT_DELAY_SCANS : INT := 3;
END_VAR
VAR
    xGasSubsystemEnable : BOOL;
    arSetpointSccm      : ARRAY[0..5] OF REAL;
    axSlaveError        : ARRAY[0..5] OF BOOL;
    aChState            : ARRAY[0..5] OF ST_GasChannelState;

    wGasStatus  : WORD;
    wChStatus   : WORD;
    dwValue     : DWORD;
    iBase       : INT;
    xAnyFault   : BOOL;
    xAllFault   : BOOL;
    rRaw        : REAL;
    iRawSetpoint: INT;
    i           : INT;
END_VAR

xGasSubsystemEnable := (awHolding[0] = WORD#1) AND ((awHolding[1] AND WORD#16#0008) <> WORD#0);

axSlaveError[0] := MFC_H2.xError;
axSlaveError[1] := MFC_CH4.xError;
axSlaveError[2] := MFC_O2.xError;
axSlaveError[3] := MFC_N2.xError;
axSlaveError[4] := MFC_Ar.xError;
axSlaveError[5] := MFC_H22.xError;

FOR i := 0 TO 5 DO
    arSetpointSccm[i] := F_ReadFixed(awHolding[66 + i*5], awHolding[67 + i*5]);
END_FOR;

FOR i := 0 TO 5 DO
    CASE aChState[i].eInit OF

        INIT_START:
            awRampTimeValue[i] := WORD#200;
            axRampTimeTrigger[i] := TRUE;
            aChState[i].iInitDelay := 0;
            aChState[i].eInit := INIT_RAMPTIME_HOLD;

        INIT_RAMPTIME_HOLD:
            axRampTimeTrigger[i] := FALSE;
            aChState[i].iInitDelay := aChState[i].iInitDelay + 1;
            IF aChState[i].iInitDelay >= C_INIT_DELAY_SCANS THEN
                awRampStepValue[i] := WORD#10;
                axRampStepTrigger[i] := TRUE;
                aChState[i].iInitDelay := 0;
                aChState[i].eInit := INIT_RAMPSTEP_HOLD;
            END_IF;

        INIT_RAMPSTEP_HOLD:
            axRampStepTrigger[i] := FALSE;
            aChState[i].iInitDelay := aChState[i].iInitDelay + 1;
            IF aChState[i].iInitDelay >= C_INIT_DELAY_SCANS THEN
                awRampEnValue[i] := WORD#1;
                axRampEnTrigger[i] := TRUE;
                aChState[i].iInitDelay := 0;
                aChState[i].eInit := INIT_RAMPEN_HOLD;
            END_IF;

        INIT_RAMPEN_HOLD:
            axRampEnTrigger[i] := FALSE;
            aChState[i].iInitDelay := aChState[i].iInitDelay + 1;
            IF aChState[i].iInitDelay >= C_INIT_DELAY_SCANS THEN
                awModeValue[i] := WORD#19;
                axModeTrigger[i] := TRUE;
                aChState[i].iInitDelay := 0;
                aChState[i].eInit := INIT_MODE_HOLD;
            END_IF;

        INIT_MODE_HOLD:
            axModeTrigger[i] := FALSE;
            aChState[i].iInitDelay := aChState[i].iInitDelay + 1;
            IF aChState[i].iInitDelay >= C_INIT_DELAY_SCANS THEN
                aChState[i].xRegulating := TRUE;
                aChState[i].xFaultActive := FALSE;
                aChState[i].xCloseConfirmed := FALSE;
                aChState[i].eInit := INIT_DONE;
            END_IF;

        INIT_DONE:
            IF (NOT xGasSubsystemEnable) OR axSlaveError[i] THEN
                aChState[i].xRegulating := FALSE;
                aChState[i].xFaultActive := xGasSubsystemEnable;
                awModeValue[i] := WORD#10;
                axModeTrigger[i] := TRUE;
                aChState[i].iInitDelay := 0;
                aChState[i].eInit := CLOSE_HOLD;
            END_IF;

        CLOSE_HOLD:
            axModeTrigger[i] := FALSE;
            aChState[i].iInitDelay := aChState[i].iInitDelay + 1;
            IF aChState[i].iInitDelay >= C_INIT_DELAY_SCANS THEN
                aChState[i].xCloseConfirmed := TRUE;
                aChState[i].eInit := CLOSED;
            END_IF;

        CLOSED:
            IF xGasSubsystemEnable AND (NOT axSlaveError[i]) THEN
                aChState[i].eInit := INIT_START;
            END_IF;

        ELSE
            aChState[i].eInit := INIT_START;
    END_CASE;

    rRaw := arSetpointSccm[i] / aFullScaleSccm[i] * 10000.0;
    IF rRaw < 0.0 THEN
        rRaw := 0.0;
    END_IF;
    iRawSetpoint := REAL_TO_INT(rRaw + 0.5);
    IF iRawSetpoint > 10000 THEN
        iRawSetpoint := 10000;
    ELSIF iRawSetpoint < 0 THEN
        iRawSetpoint := 0;
    END_IF;

    IF iRawSetpoint <> aChState[i].iLastSetpointRaw THEN
        awSetpointRaw[i] := INT_TO_WORD(iRawSetpoint);
        axSetpointTrigger[i] := TRUE;
        aChState[i].iLastSetpointRaw := iRawSetpoint;
    ELSE
        axSetpointTrigger[i] := FALSE;
    END_IF;
END_FOR;

xAnyFault := FALSE;
xAllFault := TRUE;
FOR i := 0 TO 5 DO
    iBase := 66 + i*5;

    dwValue := F_ToFixed(UINT_TO_REAL(WORD_TO_UINT(awMeasuredRaw[i])) / 10000.0 * aFullScaleSccm[i]);
    awHolding[iBase + 2] := DWORD_TO_WORD(dwValue);
    awHolding[iBase + 3] := DWORD_TO_WORD(SHR(dwValue, 16));

    wChStatus := WORD#0;
    IF aChState[i].xRegulating THEN
        wChStatus := wChStatus OR WORD#16#0001;
    END_IF;
    IF aChState[i].xFaultActive THEN
        wChStatus := wChStatus OR WORD#16#0002;
        xAnyFault := TRUE;
    ELSE
        xAllFault := FALSE;
    END_IF;
    IF aChState[i].xCloseConfirmed THEN
        wChStatus := wChStatus OR WORD#16#0004;
    END_IF;
    IF (NOT xGasSubsystemEnable) AND (NOT aChState[i].xFaultActive) THEN
        wChStatus := wChStatus OR WORD#16#0008;
    END_IF;
    awHolding[iBase + 4] := wChStatus;
END_FOR;

wGasStatus := WORD#0;
IF NOT xAllFault THEN
    wGasStatus := wGasStatus OR WORD#16#0001;
END_IF;
IF xAnyFault THEN
    wGasStatus := wGasStatus OR WORD#16#0002;
END_IF;
IF xAllFault THEN
    wGasStatus := wGasStatus OR WORD#16#0004;
END_IF;
IF xGasSubsystemEnable THEN
    wGasStatus := wGasStatus OR WORD#16#0008;
END_IF;
awHolding[64] := wGasStatus;
awHolding[65] := awHolding[65] + WORD#1;

wChStatus := WORD#0;
FOR i := 0 TO 5 DO
    IF axSlaveError[i] THEN
        wChStatus := wChStatus OR SHL(WORD#1, i);
    END_IF;
END_FOR;
awHolding[96] := wChStatus;
awHolding[97] := awMeasuredRaw[0];
CASE aChState[0].eInit OF
    INIT_START:         awHolding[98] := WORD#0;
    INIT_RAMPTIME_HOLD: awHolding[98] := WORD#1;
    INIT_RAMPSTEP_HOLD: awHolding[98] := WORD#2;
    INIT_RAMPEN_HOLD:   awHolding[98] := WORD#3;
    INIT_MODE_HOLD:     awHolding[98] := WORD#4;
    INIT_DONE:          awHolding[98] := WORD#5;
    CLOSE_HOLD:         awHolding[98] := WORD#6;
    CLOSED:             awHolding[98] := WORD#7;
END_CASE;

CASE aChState[1].eInit OF
    INIT_START:         awHolding[99] := WORD#0;
    INIT_RAMPTIME_HOLD: awHolding[99] := WORD#1;
    INIT_RAMPSTEP_HOLD: awHolding[99] := WORD#2;
    INIT_RAMPEN_HOLD:   awHolding[99] := WORD#3;
    INIT_MODE_HOLD:     awHolding[99] := WORD#4;
    INIT_DONE:          awHolding[99] := WORD#5;
    CLOSE_HOLD:         awHolding[99] := WORD#6;
    CLOSED:             awHolding[99] := WORD#7;
END_CASE;
