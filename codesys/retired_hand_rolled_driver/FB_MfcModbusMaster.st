FUNCTION_BLOCK FB_MfcModbusMaster
VAR_INPUT
    xEnable        : BOOL;
    udiPort        : UDINT := 4;
    udiBaudrate    : UDINT := 19200;
    arSetpointSccm : ARRAY[0..5] OF REAL;
END_VAR
VAR_OUTPUT
    xBusOpen         : BOOL;
    udiSweepCounter  : UDINT;
    arMeasuredSccm   : ARRAY[0..5] OF REAL;
    axRegulating     : ARRAY[0..5] OF BOOL;
    axFaultActive    : ARRAY[0..5] OF BOOL;
    axCloseConfirmed : ARRAY[0..5] OF BOOL;
    awFaultCode      : ARRAY[0..5] OF WORD;
END_VAR
VAR CONSTANT
    C_REG_MODE         : WORD := 2;
    C_REG_SETPOINT     : WORD := 4;
    C_REG_MEASURED     : WORD := 5;
    C_REG_RAMPTIME     : WORD := 19;
    C_REG_RAMPSTEP     : WORD := 20;
    C_REG_RAMPEN       : WORD := 21;
    C_MODE_REGULATE    : WORD := 19;
    C_MODE_CLOSE       : WORD := 10;
    C_RAMPTIME_DEFAULT : WORD := 200;
    C_RAMPSTEP_DEFAULT : WORD := 10;
    C_RAMPEN_DEFAULT   : WORD := 1;
    C_FUNC_READ        : BYTE := 16#03;
    C_FUNC_WRITE       : BYTE := 16#06;
    C_MAX_RETRIES      : INT := 2;
    C_OPEN_RETRY_TICKS : UDINT := 40;
    C_IO_TIMEOUT_US    : UDINT := 100000;
END_VAR
VAR
    aCfg        : ARRAY[0..5] OF ST_MfcChannelConfig;
    xConfigInit : BOOL;

    aCh : ARRAY[0..5] OF ST_MfcChannelState;

    iBusState         : INT;
    iChannel          : INT;
    hCom              : CAA.HANDLE;
    udiOpenRetryTicks : UDINT;

    fbOpen  : COM.Open;
    fbWrite : COM.Write;
    fbRead  : COM.Read;
    aParams : ARRAY[1..7] OF COM.PARAMETER;
    xOpenExecute  : BOOL;
    xWriteExecute : BOOL;
    xReadExecute  : BOOL;

    aTx     : ARRAY[0..7] OF BYTE;
    aRx     : ARRAY[0..7] OF BYTE;
    iRxSize : INT;

    iRxAccum         : INT;
    iRxIdleScans     : INT;
    xRxRetriggerArmed: BOOL;
    iRxOffset        : INT;
    iAccumStatus     : INT;

    bPendingAddr    : BYTE;
    wPendingReg     : WORD;
    wPendingValue   : WORD;

    wValidateResult : WORD;
    wReadValue      : WORD;
    rRaw            : REAL;
    iRawSetpoint    : INT;

    i : INT;
END_VAR

IF NOT xConfigInit THEN
    aCfg[0].bSlaveAddr := BYTE#1; aCfg[0].sGasName := 'H2';  aCfg[0].rFullScaleSccm := 1000.0;
    aCfg[1].bSlaveAddr := BYTE#2; aCfg[1].sGasName := 'CH4'; aCfg[1].rFullScaleSccm := 100.0;
    aCfg[2].bSlaveAddr := BYTE#3; aCfg[2].sGasName := 'O2';  aCfg[2].rFullScaleSccm := 50.0;
    aCfg[3].bSlaveAddr := BYTE#4; aCfg[3].sGasName := 'N2';  aCfg[3].rFullScaleSccm := 1000.0;
    aCfg[4].bSlaveAddr := BYTE#5; aCfg[4].sGasName := 'Ar';  aCfg[4].rFullScaleSccm := 10.0;
    aCfg[5].bSlaveAddr := BYTE#6; aCfg[5].sGasName := 'H22'; aCfg[5].rFullScaleSccm := 1000.0;
    xConfigInit := TRUE;
END_IF;

IF NOT xEnable THEN
    FOR i := 0 TO 5 DO
        IF (aCh[i].iSubState <> 900) AND (aCh[i].iSubState <> 901) AND (aCh[i].iSubState <> 902) THEN
            aCh[i].iSubState := 900;
            aCh[i].iRetryCount := 0;
        END_IF;
    END_FOR;
END_IF;

aParams[1].udiParameterId := COM.CAA_Parameter_Constants.udiPort;
aParams[1].udiValue := udiPort;
aParams[2].udiParameterId := COM.CAA_Parameter_Constants.udiBaudrate;
aParams[2].udiValue := udiBaudrate;
aParams[3].udiParameterId := COM.CAA_Parameter_Constants.udiParity;
aParams[3].udiValue := 2;
aParams[4].udiParameterId := COM.CAA_Parameter_Constants.udiStopBits;
aParams[4].udiValue := 0;
aParams[5].udiParameterId := COM.CAA_Parameter_Constants.udiTimeout;
aParams[5].udiValue := 200000;
aParams[6].udiParameterId := COM.CAA_Parameter_Constants.udiByteSize;
aParams[6].udiValue := 8;
aParams[7].udiParameterId := COM.CAA_Parameter_Constants.udiBinary;
aParams[7].udiValue := 1;

CASE iBusState OF

    0:
        xOpenExecute := TRUE;
        xBusOpen := FALSE;
        IF fbOpen.xDone THEN
            hCom := fbOpen.hCom;
            xOpenExecute := FALSE;
            xBusOpen := TRUE;
            iChannel := 0;
            FOR i := 0 TO 5 DO
                IF xEnable THEN
                    aCh[i].iSubState := 100;
                ELSE
                    aCh[i].iSubState := 900;
                END_IF;
                aCh[i].iRetryCount := 0;
            END_FOR;
            iBusState := 10;
        ELSIF fbOpen.xError THEN
            xOpenExecute := FALSE;
            udiOpenRetryTicks := 0;
            iBusState := 5;
        END_IF;

    5:
        xBusOpen := FALSE;
        udiOpenRetryTicks := udiOpenRetryTicks + 1;
        IF udiOpenRetryTicks >= C_OPEN_RETRY_TICKS THEN
            iBusState := 0;
        END_IF;

    10:
        CASE aCh[iChannel].iSubState OF

            100:
                bPendingAddr := aCfg[iChannel].bSlaveAddr;
                wPendingReg := C_REG_RAMPTIME;
                wPendingValue := C_RAMPTIME_DEFAULT;
                F_MbWriteFrame(bAddr := bPendingAddr, wReg := wPendingReg, wValue := wPendingValue, aFrame := aTx);
                xWriteExecute := TRUE;
                aCh[iChannel].iSubState := 101;

            101:
                IF fbWrite.xDone THEN
                    xWriteExecute := FALSE;
                    xReadExecute := TRUE;
                    iRxAccum := 0;
                    iRxIdleScans := 0;
                    xRxRetriggerArmed := FALSE;
                    aCh[iChannel].iSubState := 102;
                ELSIF fbWrite.xError THEN
                    xWriteExecute := FALSE;
                    aCh[iChannel].wFaultCode := WORD#1;
                    aCh[iChannel].iSubState := F_MbRetryOrFault(iIssueState := 100, iFaultState := 900, iMaxRetries := C_MAX_RETRIES, iRetryCount := aCh[iChannel].iRetryCount);
                END_IF;

            102:
                iAccumStatus := F_MbReadStep(xReadDone := fbRead.xDone, xReadError := fbRead.xError, udiReadSize := fbRead.szSize,
                    xReadExecute := xReadExecute, xRetriggerArmed := xRxRetriggerArmed, iRxAccum := iRxAccum, iRxIdleScans := iRxIdleScans);
                IF iAccumStatus = 1 THEN
                    iRxSize := iRxAccum;
                    wValidateResult := F_MbValidateResponse(iSize := iRxSize, bExpectAddr := bPendingAddr, bExpectFunc := C_FUNC_WRITE,
                        wExpectReg := wPendingReg, wExpectValue := wPendingValue, xIsWrite := TRUE, aFrame := aRx, wReadValue => wReadValue);
                    IF wValidateResult = WORD#0 THEN
                        aCh[iChannel].iRetryCount := 0;
                        aCh[iChannel].iSubState := 110;
                    ELSE
                        aCh[iChannel].wFaultCode := wValidateResult;
                        aCh[iChannel].iSubState := F_MbRetryOrFault(iIssueState := 100, iFaultState := 900, iMaxRetries := C_MAX_RETRIES, iRetryCount := aCh[iChannel].iRetryCount);
                    END_IF;
                ELSIF iAccumStatus = 2 THEN
                    aCh[iChannel].wFaultCode := WORD#2;
                    aCh[iChannel].iSubState := F_MbRetryOrFault(iIssueState := 100, iFaultState := 900, iMaxRetries := C_MAX_RETRIES, iRetryCount := aCh[iChannel].iRetryCount);
                END_IF;

            110:
                bPendingAddr := aCfg[iChannel].bSlaveAddr;
                wPendingReg := C_REG_RAMPSTEP;
                wPendingValue := C_RAMPSTEP_DEFAULT;
                F_MbWriteFrame(bAddr := bPendingAddr, wReg := wPendingReg, wValue := wPendingValue, aFrame := aTx);
                xWriteExecute := TRUE;
                aCh[iChannel].iSubState := 111;

            111:
                IF fbWrite.xDone THEN
                    xWriteExecute := FALSE;
                    xReadExecute := TRUE;
                    iRxAccum := 0;
                    iRxIdleScans := 0;
                    xRxRetriggerArmed := FALSE;
                    aCh[iChannel].iSubState := 112;
                ELSIF fbWrite.xError THEN
                    xWriteExecute := FALSE;
                    aCh[iChannel].wFaultCode := WORD#1;
                    aCh[iChannel].iSubState := F_MbRetryOrFault(iIssueState := 110, iFaultState := 900, iMaxRetries := C_MAX_RETRIES, iRetryCount := aCh[iChannel].iRetryCount);
                END_IF;

            112:
                iAccumStatus := F_MbReadStep(xReadDone := fbRead.xDone, xReadError := fbRead.xError, udiReadSize := fbRead.szSize,
                    xReadExecute := xReadExecute, xRetriggerArmed := xRxRetriggerArmed, iRxAccum := iRxAccum, iRxIdleScans := iRxIdleScans);
                IF iAccumStatus = 1 THEN
                    iRxSize := iRxAccum;
                    wValidateResult := F_MbValidateResponse(iSize := iRxSize, bExpectAddr := bPendingAddr, bExpectFunc := C_FUNC_WRITE,
                        wExpectReg := wPendingReg, wExpectValue := wPendingValue, xIsWrite := TRUE, aFrame := aRx, wReadValue => wReadValue);
                    IF wValidateResult = WORD#0 THEN
                        aCh[iChannel].iRetryCount := 0;
                        aCh[iChannel].iSubState := 120;
                    ELSE
                        aCh[iChannel].wFaultCode := wValidateResult;
                        aCh[iChannel].iSubState := F_MbRetryOrFault(iIssueState := 110, iFaultState := 900, iMaxRetries := C_MAX_RETRIES, iRetryCount := aCh[iChannel].iRetryCount);
                    END_IF;
                ELSIF iAccumStatus = 2 THEN
                    aCh[iChannel].wFaultCode := WORD#2;
                    aCh[iChannel].iSubState := F_MbRetryOrFault(iIssueState := 110, iFaultState := 900, iMaxRetries := C_MAX_RETRIES, iRetryCount := aCh[iChannel].iRetryCount);
                END_IF;

            120:
                bPendingAddr := aCfg[iChannel].bSlaveAddr;
                wPendingReg := C_REG_RAMPEN;
                wPendingValue := C_RAMPEN_DEFAULT;
                F_MbWriteFrame(bAddr := bPendingAddr, wReg := wPendingReg, wValue := wPendingValue, aFrame := aTx);
                xWriteExecute := TRUE;
                aCh[iChannel].iSubState := 121;

            121:
                IF fbWrite.xDone THEN
                    xWriteExecute := FALSE;
                    xReadExecute := TRUE;
                    iRxAccum := 0;
                    iRxIdleScans := 0;
                    xRxRetriggerArmed := FALSE;
                    aCh[iChannel].iSubState := 122;
                ELSIF fbWrite.xError THEN
                    xWriteExecute := FALSE;
                    aCh[iChannel].wFaultCode := WORD#1;
                    aCh[iChannel].iSubState := F_MbRetryOrFault(iIssueState := 120, iFaultState := 900, iMaxRetries := C_MAX_RETRIES, iRetryCount := aCh[iChannel].iRetryCount);
                END_IF;

            122:
                iAccumStatus := F_MbReadStep(xReadDone := fbRead.xDone, xReadError := fbRead.xError, udiReadSize := fbRead.szSize,
                    xReadExecute := xReadExecute, xRetriggerArmed := xRxRetriggerArmed, iRxAccum := iRxAccum, iRxIdleScans := iRxIdleScans);
                IF iAccumStatus = 1 THEN
                    iRxSize := iRxAccum;
                    wValidateResult := F_MbValidateResponse(iSize := iRxSize, bExpectAddr := bPendingAddr, bExpectFunc := C_FUNC_WRITE,
                        wExpectReg := wPendingReg, wExpectValue := wPendingValue, xIsWrite := TRUE, aFrame := aRx, wReadValue => wReadValue);
                    IF wValidateResult = WORD#0 THEN
                        aCh[iChannel].iRetryCount := 0;
                        aCh[iChannel].iSubState := 130;
                    ELSE
                        aCh[iChannel].wFaultCode := wValidateResult;
                        aCh[iChannel].iSubState := F_MbRetryOrFault(iIssueState := 120, iFaultState := 900, iMaxRetries := C_MAX_RETRIES, iRetryCount := aCh[iChannel].iRetryCount);
                    END_IF;
                ELSIF iAccumStatus = 2 THEN
                    aCh[iChannel].wFaultCode := WORD#2;
                    aCh[iChannel].iSubState := F_MbRetryOrFault(iIssueState := 120, iFaultState := 900, iMaxRetries := C_MAX_RETRIES, iRetryCount := aCh[iChannel].iRetryCount);
                END_IF;

            130:
                bPendingAddr := aCfg[iChannel].bSlaveAddr;
                wPendingReg := C_REG_MODE;
                wPendingValue := C_MODE_REGULATE;
                F_MbWriteFrame(bAddr := bPendingAddr, wReg := wPendingReg, wValue := wPendingValue, aFrame := aTx);
                xWriteExecute := TRUE;
                aCh[iChannel].iSubState := 131;

            131:
                IF fbWrite.xDone THEN
                    xWriteExecute := FALSE;
                    xReadExecute := TRUE;
                    iRxAccum := 0;
                    iRxIdleScans := 0;
                    xRxRetriggerArmed := FALSE;
                    aCh[iChannel].iSubState := 132;
                ELSIF fbWrite.xError THEN
                    xWriteExecute := FALSE;
                    aCh[iChannel].wFaultCode := WORD#1;
                    aCh[iChannel].iSubState := F_MbRetryOrFault(iIssueState := 130, iFaultState := 900, iMaxRetries := C_MAX_RETRIES, iRetryCount := aCh[iChannel].iRetryCount);
                END_IF;

            132:
                iAccumStatus := F_MbReadStep(xReadDone := fbRead.xDone, xReadError := fbRead.xError, udiReadSize := fbRead.szSize,
                    xReadExecute := xReadExecute, xRetriggerArmed := xRxRetriggerArmed, iRxAccum := iRxAccum, iRxIdleScans := iRxIdleScans);
                IF iAccumStatus = 1 THEN
                    iRxSize := iRxAccum;
                    wValidateResult := F_MbValidateResponse(iSize := iRxSize, bExpectAddr := bPendingAddr, bExpectFunc := C_FUNC_WRITE,
                        wExpectReg := wPendingReg, wExpectValue := wPendingValue, xIsWrite := TRUE, aFrame := aRx, wReadValue => wReadValue);
                    IF wValidateResult = WORD#0 THEN
                        aCh[iChannel].iRetryCount := 0;
                        aCh[iChannel].wFaultCode := WORD#0;
                        aCh[iChannel].xFaultActive := FALSE;
                        aCh[iChannel].xCloseConfirmed := FALSE;
                        aCh[iChannel].xRegulating := TRUE;
                        aCh[iChannel].iSubState := 200;
                    ELSE
                        aCh[iChannel].wFaultCode := wValidateResult;
                        aCh[iChannel].iSubState := F_MbRetryOrFault(iIssueState := 130, iFaultState := 900, iMaxRetries := C_MAX_RETRIES, iRetryCount := aCh[iChannel].iRetryCount);
                    END_IF;
                ELSIF iAccumStatus = 2 THEN
                    aCh[iChannel].wFaultCode := WORD#2;
                    aCh[iChannel].iSubState := F_MbRetryOrFault(iIssueState := 130, iFaultState := 900, iMaxRetries := C_MAX_RETRIES, iRetryCount := aCh[iChannel].iRetryCount);
                END_IF;

            200:
                rRaw := arSetpointSccm[iChannel] / aCfg[iChannel].rFullScaleSccm * 10000.0;
                IF rRaw < 0.0 THEN
                    rRaw := 0.0;
                END_IF;
                iRawSetpoint := REAL_TO_INT(rRaw + 0.5);
                IF iRawSetpoint > 10000 THEN
                    iRawSetpoint := 10000;
                ELSIF iRawSetpoint < 0 THEN
                    iRawSetpoint := 0;
                END_IF;

                bPendingAddr := aCfg[iChannel].bSlaveAddr;
                wPendingReg := C_REG_SETPOINT;
                wPendingValue := INT_TO_WORD(iRawSetpoint);
                F_MbWriteFrame(bAddr := bPendingAddr, wReg := wPendingReg, wValue := wPendingValue, aFrame := aTx);
                xWriteExecute := TRUE;
                aCh[iChannel].iSubState := 201;

            201:
                IF fbWrite.xDone THEN
                    xWriteExecute := FALSE;
                    xReadExecute := TRUE;
                    iRxAccum := 0;
                    iRxIdleScans := 0;
                    xRxRetriggerArmed := FALSE;
                    aCh[iChannel].iSubState := 202;
                ELSIF fbWrite.xError THEN
                    xWriteExecute := FALSE;
                    aCh[iChannel].wFaultCode := WORD#1;
                    aCh[iChannel].iSubState := F_MbRetryOrFault(iIssueState := 200, iFaultState := 900, iMaxRetries := C_MAX_RETRIES, iRetryCount := aCh[iChannel].iRetryCount);
                END_IF;

            202:
                iAccumStatus := F_MbReadStep(xReadDone := fbRead.xDone, xReadError := fbRead.xError, udiReadSize := fbRead.szSize,
                    xReadExecute := xReadExecute, xRetriggerArmed := xRxRetriggerArmed, iRxAccum := iRxAccum, iRxIdleScans := iRxIdleScans);
                IF iAccumStatus = 1 THEN
                    iRxSize := iRxAccum;
                    wValidateResult := F_MbValidateResponse(iSize := iRxSize, bExpectAddr := bPendingAddr, bExpectFunc := C_FUNC_WRITE,
                        wExpectReg := wPendingReg, wExpectValue := wPendingValue, xIsWrite := TRUE, aFrame := aRx, wReadValue => wReadValue);
                    IF wValidateResult = WORD#0 THEN
                        aCh[iChannel].iRetryCount := 0;
                        aCh[iChannel].iSubState := 210;
                    ELSE
                        aCh[iChannel].wFaultCode := wValidateResult;
                        aCh[iChannel].iSubState := F_MbRetryOrFault(iIssueState := 200, iFaultState := 900, iMaxRetries := C_MAX_RETRIES, iRetryCount := aCh[iChannel].iRetryCount);
                    END_IF;
                ELSIF iAccumStatus = 2 THEN
                    aCh[iChannel].wFaultCode := WORD#2;
                    aCh[iChannel].iSubState := F_MbRetryOrFault(iIssueState := 200, iFaultState := 900, iMaxRetries := C_MAX_RETRIES, iRetryCount := aCh[iChannel].iRetryCount);
                END_IF;

            210:
                bPendingAddr := aCfg[iChannel].bSlaveAddr;
                wPendingReg := C_REG_MEASURED;
                F_MbReadFrame(bAddr := bPendingAddr, wReg := wPendingReg, aFrame := aTx);
                xReadExecute := TRUE;
                iRxAccum := 0;
                iRxIdleScans := 0;
                xRxRetriggerArmed := FALSE;
                aCh[iChannel].iSubState := 211;

            211:
                iAccumStatus := F_MbReadStep(xReadDone := fbRead.xDone, xReadError := fbRead.xError, udiReadSize := fbRead.szSize,
                    xReadExecute := xReadExecute, xRetriggerArmed := xRxRetriggerArmed, iRxAccum := iRxAccum, iRxIdleScans := iRxIdleScans);
                IF iAccumStatus = 1 THEN
                    iRxSize := iRxAccum;
                    wValidateResult := F_MbValidateResponse(iSize := iRxSize, bExpectAddr := bPendingAddr, bExpectFunc := C_FUNC_READ,
                        wExpectReg := wPendingReg, wExpectValue := WORD#0, xIsWrite := FALSE, aFrame := aRx, wReadValue => wReadValue);
                    IF wValidateResult = WORD#0 THEN
                        aCh[iChannel].iRetryCount := 0;
                        aCh[iChannel].rMeasuredSccm := UINT_TO_REAL(WORD_TO_UINT(wReadValue)) / 10000.0 * aCfg[iChannel].rFullScaleSccm;
                        aCh[iChannel].iSubState := 200;
                    ELSE
                        aCh[iChannel].wFaultCode := wValidateResult;
                        aCh[iChannel].iSubState := F_MbRetryOrFault(iIssueState := 210, iFaultState := 900, iMaxRetries := C_MAX_RETRIES, iRetryCount := aCh[iChannel].iRetryCount);
                    END_IF;
                    iChannel := (iChannel + 1) MOD 6;
                    IF iChannel = 0 THEN
                        udiSweepCounter := udiSweepCounter + 1;
                    END_IF;
                ELSIF iAccumStatus = 2 THEN
                    aCh[iChannel].wFaultCode := WORD#2;
                    aCh[iChannel].iSubState := F_MbRetryOrFault(iIssueState := 210, iFaultState := 900, iMaxRetries := C_MAX_RETRIES, iRetryCount := aCh[iChannel].iRetryCount);
                    iChannel := (iChannel + 1) MOD 6;
                    IF iChannel = 0 THEN
                        udiSweepCounter := udiSweepCounter + 1;
                    END_IF;
                END_IF;

            900:
                aCh[iChannel].xFaultActive := xEnable;
                aCh[iChannel].xRegulating := FALSE;
                bPendingAddr := aCfg[iChannel].bSlaveAddr;
                wPendingReg := C_REG_MODE;
                wPendingValue := C_MODE_CLOSE;
                F_MbWriteFrame(bAddr := bPendingAddr, wReg := wPendingReg, wValue := wPendingValue, aFrame := aTx);
                xWriteExecute := TRUE;
                aCh[iChannel].iSubState := 901;

            901:
                IF fbWrite.xDone THEN
                    xWriteExecute := FALSE;
                    xReadExecute := TRUE;
                    iRxAccum := 0;
                    iRxIdleScans := 0;
                    xRxRetriggerArmed := FALSE;
                    aCh[iChannel].iSubState := 902;
                ELSIF fbWrite.xError THEN
                    xWriteExecute := FALSE;
                    aCh[iChannel].wFaultCode := WORD#1;
                    aCh[iChannel].iSubState := 900;
                    iChannel := (iChannel + 1) MOD 6;
                    IF iChannel = 0 THEN
                        udiSweepCounter := udiSweepCounter + 1;
                    END_IF;
                END_IF;

            902:
                iAccumStatus := F_MbReadStep(xReadDone := fbRead.xDone, xReadError := fbRead.xError, udiReadSize := fbRead.szSize,
                    xReadExecute := xReadExecute, xRetriggerArmed := xRxRetriggerArmed, iRxAccum := iRxAccum, iRxIdleScans := iRxIdleScans);
                IF iAccumStatus = 1 THEN
                    iRxSize := iRxAccum;
                    wValidateResult := F_MbValidateResponse(iSize := iRxSize, bExpectAddr := bPendingAddr, bExpectFunc := C_FUNC_WRITE,
                        wExpectReg := wPendingReg, wExpectValue := wPendingValue, xIsWrite := TRUE, aFrame := aRx, wReadValue => wReadValue);
                    IF wValidateResult = WORD#0 THEN
                        aCh[iChannel].xCloseConfirmed := TRUE;
                        IF xEnable THEN
                            aCh[iChannel].iSubState := 100;
                            aCh[iChannel].iRetryCount := 0;
                        ELSE
                            aCh[iChannel].iSubState := 900;
                        END_IF;
                    ELSE
                        aCh[iChannel].wFaultCode := wValidateResult;
                        aCh[iChannel].iSubState := 900;
                    END_IF;
                    iChannel := (iChannel + 1) MOD 6;
                    IF iChannel = 0 THEN
                        udiSweepCounter := udiSweepCounter + 1;
                    END_IF;
                ELSIF iAccumStatus = 2 THEN
                    aCh[iChannel].wFaultCode := WORD#2;
                    aCh[iChannel].iSubState := 900;
                    iChannel := (iChannel + 1) MOD 6;
                    IF iChannel = 0 THEN
                        udiSweepCounter := udiSweepCounter + 1;
                    END_IF;
                END_IF;

            ELSE
                aCh[iChannel].iSubState := 900;
        END_CASE;

    ELSE
        iBusState := 0;
END_CASE;

fbOpen(
    xExecute := xOpenExecute,
    usiListLength := 7,
    pParameterList := ADR(aParams));

fbWrite(
    xExecute := xWriteExecute,
    udiTimeOut := C_IO_TIMEOUT_US,
    hCom := hCom,
    pBuffer := ADR(aTx),
    szSize := SIZEOF(aTx));

iRxOffset := iRxAccum;
IF iRxOffset < 0 THEN
    iRxOffset := 0;
ELSIF iRxOffset > 7 THEN
    iRxOffset := 7;
END_IF;

fbRead(
    xExecute := xReadExecute,
    udiTimeOut := C_IO_TIMEOUT_US,
    hCom := hCom,
    pBuffer := ADR(aRx[iRxOffset]),
    szBuffer := SIZEOF(aRx) - INT_TO_UDINT(iRxOffset));

FOR i := 0 TO 5 DO
    arMeasuredSccm[i]   := aCh[i].rMeasuredSccm;
    axRegulating[i]     := aCh[i].xRegulating;
    axFaultActive[i]    := aCh[i].xFaultActive;
    axCloseConfirmed[i] := aCh[i].xCloseConfirmed;
    awFaultCode[i]      := aCh[i].wFaultCode;
END_FOR;
