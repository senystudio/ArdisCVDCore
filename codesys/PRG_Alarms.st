PROGRAM PRG_Alarms
VAR
    i : INT;
    iBase : INT;
    iBit : INT;
    iWord : INT;

    xFirstScan : BOOL := TRUE;

    axAlarm : ARRAY[0..63] OF BOOL;
    axAbort : ARRAY[0..63] OF BOOL;

    arBandSetpoint : ARRAY[0..6] OF REAL;
    arBandMeasured : ARRAY[0..6] OF REAL;
    axBandGate     : ARRAY[0..6] OF BOOL;

    arWaterTempC : ARRAY[0..3] OF REAL;
    axWaterTempValid : ARRAY[0..3] OF BOOL;

    rSetpoint : REAL;
    rMeasured : REAL;
    rBand     : REAL;
    rReflKw   : REAL;
    rSampleC  : REAL;
    rFlowLpm  : REAL;
    rWaterBar : REAL;
    rCdaBar   : REAL;

    wCommand    : WORD;
    wStatusWord : WORD;
    wMwStatus : WORD;
    wTempValid : WORD;
    wFlowValid : WORD;
    wTurboStatus : WORD;
    wDiscrete : WORD;

    xMwRequested : BOOL;
    xPyroValid : BOOL;
    xTurboAnswering : BOOL;
    xWaterPump : BOOL;
    xFlowArmed : BOOL;
    xLidClosed : BOOL;

    xAnyAlarm : BOOL;
    xAnyAbort : BOOL;

    xReset : BOOL;
    xResetPrev : BOOL;

    fbFlowArm : TON;
END_VAR
VAR CONSTANT
    C_BLOCK_MAGIC  : WORD := WORD#16#A500;
    C_RETAIN_MAGIC : WORD := WORD#16#5A5A;

    C_FLOW_MIN_LOW  : REAL := 1.0;
    C_FLOW_MIN_HIGH : REAL := 5.0;

    C_WATER_ALARM_BAR : REAL := 0.7;
    C_WATER_ABORT_BAR : REAL := 0.5;
    C_CDA_ALARM_BAR   : REAL := 5.0;
    C_CDA_ABORT_BAR   : REAL := 4.0;

    C_CHAMBER_MAX_TORR : REAL := 800.0;
    C_FLOW_ARM_TIME    : TIME := T#4S;

    C_CIRC_STAGE    : INT := 0;
    C_CIRC_CHAMBER  : INT := 1;
    C_CIRC_MWHEAD   : INT := 2;
    C_CIRC_MWPOWER  : INT := 3;
    C_CIRC_INTERNAL : INT := 5;
    C_CIRC_EXTERNAL : INT := 6;
END_VAR

IF xFirstScan THEN
    xFirstScan := FALSE;
    IF wRetainMagic <> C_RETAIN_MAGIC THEN
        xRetainWasBlank := TRUE;
        wRetainMagic := C_RETAIN_MAGIC;
        wParamAlarmEnable := WORD#0;
        wParamAbortEnable := WORD#0;
        wTempEnable := WORD#0;
        wInputEnable := WORD#0;
        uiLidInput := UINT#1;
    ELSE
        xRetainWasBlank := FALSE;
    END_IF;
END_IF;

xThresholdsOk := (awHolding[248] AND WORD#16#FF00) = C_BLOCK_MAGIC;

IF xThresholdsOk THEN
    wParamAlarmEnable := awHolding[249];
    wParamAbortEnable := awHolding[250];
    FOR i := 0 TO 6 DO
        abParamAlarmPct[i] := WORD_TO_BYTE(awHolding[251 + i] AND WORD#16#00FF);
        abParamAbortPct[i] := WORD_TO_BYTE(SHR(awHolding[251 + i], 8));
    END_FOR;
    wMwReflAlarmWatt := awHolding[258];
    wMwReflAbortWatt := awHolding[259];

    wTempEnable := awHolding[260];
    wSampleTargetC := awHolding[261];
    bSampleAlarmPct := WORD_TO_BYTE(awHolding[262] AND WORD#16#00FF);
    abWaterTargetC[0] := WORD_TO_BYTE(awHolding[263] AND WORD#16#00FF);
    abWaterTargetC[1] := WORD_TO_BYTE(SHR(awHolding[263], 8));
    abWaterTargetC[2] := WORD_TO_BYTE(awHolding[264] AND WORD#16#00FF);
    abWaterTargetC[3] := WORD_TO_BYTE(SHR(awHolding[264], 8));
    FOR i := 0 TO 3 DO
        abWaterAlarmPct[i] := WORD_TO_BYTE(awHolding[265 + i] AND WORD#16#00FF);
        abWaterAbortPct[i] := WORD_TO_BYTE(SHR(awHolding[265 + i], 8));
    END_FOR;

    wInputEnable := awHolding[269];
    wInputReaction := awHolding[270];
    xRunActive := (awHolding[271] AND WORD#16#0001) <> WORD#0;
    uiLidInput := WORD_TO_UINT(SHR(awHolding[271], 8));
    IF (uiLidInput < UINT#1) OR (uiLidInput > UINT#12) THEN
        uiLidInput := UINT#1;
    END_IF;
ELSE
    xRunActive := FALSE;
END_IF;

FOR i := 0 TO 63 DO
    axAlarm[i] := FALSE;
    axAbort[i] := FALSE;
END_FOR;

wMwStatus := awHolding[195];
wCommand := awHolding[192];
xMwRequested := ((wCommand AND WORD#16#0003) <> WORD#0) AND (NOT xAbortActive) AND (NOT xMwInhibit);
rReflKw := F_ReadFixed(awHolding[199], awHolding[200]);

wTempValid := awHolding[238];
wFlowValid := awHolding[239];
wTurboStatus := awHolding[245];
xTurboAnswering := (awHolding[247] AND WORD#16#0001) <> WORD#0;
wDiscrete := awHolding[139];
xLidClosed := (wDiscrete AND SHL(WORD#1, UINT_TO_INT(uiLidInput) - 1)) <> WORD#0;

xWaterPump := xWaterPumpOutput;
fbFlowArm(IN := xWaterPump, PT := C_FLOW_ARM_TIME);
xFlowArmed := fbFlowArm.Q;

xPyroValid := (awHolding[166] AND WORD#16#0001) <> WORD#0;

arBandSetpoint[0] := F_ReadFixed(awHolding[4], awHolding[5]);
arBandMeasured[0] := F_ReadFixed(awHolding[130], awHolding[131]);
axBandGate[0] := TRUE;

FOR i := 0 TO 5 DO
    iBase := 66 + i*5;
    arBandSetpoint[i + 1] := F_ReadFixed(awHolding[iBase], awHolding[iBase + 1]);
    arBandMeasured[i + 1] := F_ReadFixed(awHolding[iBase + 2], awHolding[iBase + 3]);
    axBandGate[i + 1] := axGpvOutput[i + 1] AND axGpvOutput[8]
        AND ((awHolding[96] AND SHL(WORD#1, i)) = WORD#0);
END_FOR;

FOR i := 0 TO 6 DO
    rSetpoint := arBandSetpoint[i];
    rMeasured := arBandMeasured[i];

    IF axBandGate[i] AND (rSetpoint > 0.0) THEN
        IF (wParamAlarmEnable AND SHL(WORD#1, i)) <> WORD#0 THEN
            rBand := rSetpoint * BYTE_TO_REAL(abParamAlarmPct[i]) / 100.0;
            IF (rMeasured >= rSetpoint + rBand) OR (rMeasured <= rSetpoint - rBand) THEN
                axAlarm[i] := TRUE;
            END_IF;
        END_IF;
        IF (wParamAbortEnable AND SHL(WORD#1, i)) <> WORD#0 THEN
            rBand := rSetpoint * BYTE_TO_REAL(abParamAbortPct[i]) / 100.0;
            IF (rMeasured >= rSetpoint + rBand) OR (rMeasured <= rSetpoint - rBand) THEN
                axAbort[i] := TRUE;
            END_IF;
        END_IF;
    END_IF;
END_FOR;

IF (wParamAlarmEnable AND WORD#16#0080) <> WORD#0 THEN
    IF rReflKw * 1000.0 >= UINT_TO_REAL(WORD_TO_UINT(wMwReflAlarmWatt)) THEN
        axAlarm[7] := TRUE;
    END_IF;
END_IF;
IF (wParamAbortEnable AND WORD#16#0080) <> WORD#0 THEN
    IF rReflKw * 1000.0 >= UINT_TO_REAL(WORD_TO_UINT(wMwReflAbortWatt)) THEN
        axAbort[7] := TRUE;
    END_IF;
END_IF;

rSampleC := F_ReadFixed(awHolding[160], awHolding[161]);
IF xPyroValid AND ((wTempEnable AND WORD#16#0001) <> WORD#0)
    AND (wSampleTargetC > WORD#0) THEN
    rSetpoint := UINT_TO_REAL(WORD_TO_UINT(wSampleTargetC));
    rBand := rSetpoint * BYTE_TO_REAL(bSampleAlarmPct) / 100.0;
    IF (rSampleC >= rSetpoint + rBand) OR (rSampleC <= rSetpoint - rBand) THEN
        axAlarm[8] := TRUE;
    END_IF;
END_IF;

arWaterTempC[0] := F_ReadFixed(awHolding[206 + 2*C_CIRC_INTERNAL], awHolding[207 + 2*C_CIRC_INTERNAL]);
arWaterTempC[1] := F_ReadFixed(awHolding[206 + 2*C_CIRC_EXTERNAL], awHolding[207 + 2*C_CIRC_EXTERNAL]);
arWaterTempC[2] := F_ReadFixed(awHolding[206 + 2*C_CIRC_STAGE], awHolding[207 + 2*C_CIRC_STAGE]);
arWaterTempC[3] := F_ReadFixed(awHolding[206 + 2*C_CIRC_CHAMBER], awHolding[207 + 2*C_CIRC_CHAMBER]);

axWaterTempValid[0] := (wTempValid AND SHL(WORD#1, C_CIRC_INTERNAL)) <> WORD#0;
axWaterTempValid[1] := (wTempValid AND SHL(WORD#1, C_CIRC_EXTERNAL)) <> WORD#0;
axWaterTempValid[2] := (wTempValid AND SHL(WORD#1, C_CIRC_STAGE)) <> WORD#0;
axWaterTempValid[3] := (wTempValid AND SHL(WORD#1, C_CIRC_CHAMBER)) <> WORD#0;

FOR i := 0 TO 3 DO
    IF xWaterPump AND axWaterTempValid[i] AND (abWaterTargetC[i] > BYTE#0) THEN
        rSetpoint := BYTE_TO_REAL(abWaterTargetC[i]);
        rMeasured := arWaterTempC[i];
        IF (wTempEnable AND SHL(WORD#1, i + 1)) <> WORD#0 THEN
            rBand := rSetpoint * BYTE_TO_REAL(abWaterAlarmPct[i]) / 100.0;
            IF (rMeasured >= rSetpoint + rBand) OR (rMeasured <= rSetpoint - rBand) THEN
                axAlarm[9 + i] := TRUE;
            END_IF;
        END_IF;
        IF (wTempEnable AND SHL(WORD#1, i + 9)) <> WORD#0 THEN
            rBand := rSetpoint * BYTE_TO_REAL(abWaterAbortPct[i]) / 100.0;
            IF (rMeasured >= rSetpoint + rBand) OR (rMeasured <= rSetpoint - rBand) THEN
                axAbort[9 + i] := TRUE;
            END_IF;
        END_IF;
    END_IF;
END_FOR;

FOR i := 0 TO 5 DO
    CASE i OF
        0: iBase := C_CIRC_STAGE;
        1: iBase := C_CIRC_CHAMBER;
        2: iBase := C_CIRC_MWHEAD;
        3: iBase := C_CIRC_MWPOWER;
        4: iBase := C_CIRC_INTERNAL;
    ELSE
        iBase := C_CIRC_EXTERNAL;
    END_CASE;

    IF i < 4 THEN
        rSetpoint := C_FLOW_MIN_LOW;
    ELSE
        rSetpoint := C_FLOW_MIN_HIGH;
    END_IF;

    rFlowLpm := F_ReadFixed(awHolding[220 + 2*iBase], awHolding[221 + 2*iBase]);

    IF xWaterPump AND xFlowArmed
        AND ((wFlowValid AND SHL(WORD#1, iBase)) <> WORD#0)
        AND ((wInputEnable AND SHL(WORD#1, i)) <> WORD#0)
        AND (rFlowLpm <= rSetpoint) THEN
        IF (wInputReaction AND SHL(WORD#1, i)) <> WORD#0 THEN
            axAbort[16 + i] := TRUE;
        ELSE
            axAlarm[16 + i] := TRUE;
        END_IF;
    END_IF;
END_FOR;

rWaterBar := F_ReadFixed(awHolding[234], awHolding[235]);
IF ((wFlowValid AND WORD#16#0200) <> WORD#0)
    AND ((wInputEnable AND WORD#16#0040) <> WORD#0) THEN
    IF ((wInputReaction AND WORD#16#0040) <> WORD#0) AND (rWaterBar < C_WATER_ABORT_BAR) THEN
        axAbort[22] := TRUE;
    ELSIF rWaterBar < C_WATER_ALARM_BAR THEN
        axAlarm[22] := TRUE;
    END_IF;
END_IF;

rCdaBar := F_ReadFixed(awHolding[236], awHolding[237]);
IF (wFlowValid AND WORD#16#0100) <> WORD#0 THEN
    IF rCdaBar <= C_CDA_ABORT_BAR THEN
        axAbort[23] := TRUE;
    ELSIF rCdaBar <= C_CDA_ALARM_BAR THEN
        axAlarm[23] := TRUE;
    END_IF;
END_IF;

IF ((wInputEnable AND WORD#16#0080) <> WORD#0) AND (NOT xLidClosed) THEN
    IF (wInputReaction AND WORD#16#0080) <> WORD#0 THEN
        axAbort[24] := TRUE;
    ELSE
        axAlarm[24] := TRUE;
    END_IF;
END_IF;

IF arBandMeasured[0] > C_CHAMBER_MAX_TORR THEN
    axAlarm[25] := TRUE;
END_IF;

IF xMwRequested THEN
    IF (wMwStatus AND WORD#16#8000) <> WORD#0 THEN axAbort[32] := TRUE; END_IF;
    IF (wMwStatus AND WORD#16#2000) <> WORD#0 THEN axAbort[33] := TRUE; END_IF;
    IF (wMwStatus AND WORD#16#4000) <> WORD#0 THEN axAbort[34] := TRUE; END_IF;
    IF (wMwStatus AND WORD#16#1000) <> WORD#0 THEN axAbort[35] := TRUE; END_IF;
    IF (wMwStatus AND WORD#16#0800) <> WORD#0 THEN axAbort[36] := TRUE; END_IF;
    IF (wMwStatus AND WORD#16#0004) <> WORD#0 THEN axAbort[37] := TRUE; END_IF;
    IF (wMwStatus AND WORD#16#0400) <> WORD#0 THEN axAbort[38] := TRUE; END_IF;
    IF (wMwStatus AND WORD#16#0200) <> WORD#0 THEN axAbort[39] := TRUE; END_IF;
    IF (wMwStatus AND WORD#16#0010) <> WORD#0 THEN axAlarm[40] := TRUE; END_IF;
    IF (wMwStatus AND WORD#16#0002) <> WORD#0 THEN axAbort[41] := TRUE; END_IF;
END_IF;

IF xTurboAnswering THEN
    IF (wTurboStatus AND WORD#16#0001) <> WORD#0 THEN axAlarm[48] := TRUE; END_IF;
    IF (wTurboStatus AND WORD#16#0002) <> WORD#0 THEN axAlarm[49] := TRUE; END_IF;
    IF (wTurboStatus AND WORD#16#0020) <> WORD#0 THEN axAlarm[50] := TRUE; END_IF;
    IF (wTurboStatus AND WORD#16#0040) <> WORD#0 THEN axAlarm[51] := TRUE; END_IF;
    IF (wTurboStatus AND WORD#16#0080) <> WORD#0 THEN axAlarm[52] := TRUE; END_IF;
    IF wTurboStatus = WORD#16#0108 THEN axAlarm[53] := TRUE; END_IF;
END_IF;

FOR i := 0 TO 3 DO
    awAlarmMask[i] := WORD#0;
    awAbortMask[i] := WORD#0;
END_FOR;

xAnyAlarm := FALSE;
xAnyAbort := FALSE;

FOR i := 0 TO 63 DO
    iWord := i / 16;
    iBit := i MOD 16;
    IF axAlarm[i] THEN
        awAlarmMask[iWord] := awAlarmMask[iWord] OR SHL(WORD#1, iBit);
        xAnyAlarm := TRUE;
    END_IF;
    IF axAbort[i] THEN
        awAbortMask[iWord] := awAbortMask[iWord] OR SHL(WORD#1, iBit);
        xAnyAbort := TRUE;
    END_IF;
END_FOR;

xReset := (awHolding[1] AND WORD#16#0004) <> WORD#0;

IF xAnyAbort AND (NOT xAbortActive) THEN
    xAbortActive := TRUE;
    uiAbortCount := uiAbortCount + UINT#1;
    FOR i := 0 TO 3 DO
        awAbortLatched[i] := awAbortMask[i];
    END_FOR;
END_IF;

IF xAbortActive THEN
    wGpvInhibit := wGpvInhibit OR awHolding[133];
    wVpvInhibit := wVpvInhibit OR awHolding[135];
    IF (awHolding[137] AND WORD#16#0002) <> WORD#0 THEN xForeVacInhibit := TRUE; END_IF;
    IF (awHolding[240] AND WORD#16#0001) <> WORD#0 THEN xTurboInhibit := TRUE; END_IF;
    IF (awHolding[192] AND WORD#16#0003) <> WORD#0 THEN xMwInhibit := TRUE; END_IF;
END_IF;

wGpvInhibit := wGpvInhibit AND awHolding[133];
wVpvInhibit := wVpvInhibit AND awHolding[135];
IF (awHolding[137] AND WORD#16#0002) = WORD#0 THEN xForeVacInhibit := FALSE; END_IF;
IF (awHolding[240] AND WORD#16#0001) = WORD#0 THEN xTurboInhibit := FALSE; END_IF;
IF (awHolding[192] AND WORD#16#0003) = WORD#0 THEN xMwInhibit := FALSE; END_IF;

IF xReset AND (NOT xResetPrev) AND (NOT xAnyAbort) THEN
    xAbortActive := FALSE;
    FOR i := 0 TO 3 DO
        awAbortLatched[i] := WORD#0;
    END_FOR;
END_IF;
xResetPrev := xReset;

FOR i := 0 TO 3 DO
    awHolding[280 + i] := awAlarmMask[i];
    awHolding[284 + i] := awAbortMask[i];
    awHolding[288 + i] := awAbortLatched[i];
END_FOR;

wStatusWord := WORD#0;
IF xAbortActive THEN
    wStatusWord := wStatusWord OR WORD#16#0001;
END_IF;
IF xAnyAlarm THEN
    wStatusWord := wStatusWord OR WORD#16#0002;
END_IF;
IF xThresholdsOk THEN
    wStatusWord := wStatusWord OR WORD#16#0004;
END_IF;
IF xRetainWasBlank THEN
    wStatusWord := wStatusWord OR WORD#16#0008;
END_IF;
IF (wGpvInhibit <> WORD#0) OR (wVpvInhibit <> WORD#0)
    OR xForeVacInhibit OR xTurboInhibit OR xMwInhibit THEN
    wStatusWord := wStatusWord OR WORD#16#0010;
END_IF;
awHolding[292] := wStatusWord;
awHolding[294] := wGpvInhibit;
awHolding[295] := wVpvInhibit;
awHolding[293] := UINT_TO_WORD(uiAbortCount);
