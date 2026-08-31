PROGRAM PRG_McohBus
VAR CONSTANT
    C_CYCLE_TIME   : TIME := T#200MS;
    C_STEP_TIMEOUT : TIME := T#100MS;

    C_PYRO_CH1    : INT := 0;
    C_PYRO_CH2    : INT := 1;
    C_PYRO_RATIO  : INT := 2;

    C_MW_POWER    : INT := 0;
    C_MW_STATUS1  : INT := 1;
    C_MW_STATUS2  : INT := 2;
    C_MW_STATUS3  : INT := 7;
    C_MW_PREHEAT  : INT := 3;
    C_MW_RUN      : INT := 4;
    C_MW_RESET    : INT := 5;
    C_MW_SETPOINT : INT := 6;

    C_ST_IDLE  : INT := 0;
    C_ST_START : INT := 1;
    C_ST_WAIT  : INT := 2;
    C_ST_ABORT : INT := 3;

    C_LAST_STEP : INT := 10;
END_VAR
VAR
    fbPyroCh : IoDrvModbusTCP.ModbusChannel;
    fbMwCh   : IoDrvModbusTCP.ModbusChannel;

    fbPeriod      : TON;
    fbStepTimeout : TON;
    fbCycleWatch  : TON;

    iState     : INT;
    iStep      : INT;
    iPyroIndex : INT;
    iMwIndex   : INT;
    xPyroExec  : BOOL;
    xMwExec    : BOOL;
    xAbortStep : BOOL;
    xSkipStep  : BOOL;
    xStepDone  : BOOL;
    xStepError : BOOL;
    xAdvance   : BOOL;

    udiCycles      : UDINT;
    udiStepErrors  : UDINT;
    udiStepTimeout : UDINT;
    tCycleElapsed  : TIME;
    tCycleWorst    : TIME;
END_VAR

fbPeriod(IN := NOT fbPeriod.Q, PT := C_CYCLE_TIME);
fbStepTimeout(IN := xPyroExec OR xMwExec, PT := C_STEP_TIMEOUT);
fbCycleWatch(IN := iState <> C_ST_IDLE, PT := T#5S);

xStepDone := (xPyroExec AND (fbPyroCh.xDone OR fbPyroCh.xError))
          OR (xMwExec AND (fbMwCh.xDone OR fbMwCh.xError));
xStepError := (xPyroExec AND fbPyroCh.xError) OR (xMwExec AND fbMwCh.xError);
xAdvance := FALSE;

CASE iState OF

    C_ST_IDLE:
        IF fbPeriod.Q THEN
            iStep := 0;
            iState := C_ST_START;
        END_IF;

    C_ST_START:
        xSkipStep := FALSE;
        CASE iStep OF
            0:
                iPyroIndex := C_PYRO_CH1;
                xPyroExec := TRUE;
            1:
                iPyroIndex := C_PYRO_CH2;
                xPyroExec := TRUE;
            2:
                iPyroIndex := C_PYRO_RATIO;
                xPyroExec := TRUE;
            3:
                iMwIndex := C_MW_POWER;
                xMwExec := TRUE;
            4:
                iMwIndex := C_MW_STATUS1;
                xMwExec := TRUE;
            5:
                iMwIndex := C_MW_STATUS2;
                xMwExec := TRUE;
            6:
                iMwIndex := C_MW_STATUS3;
                xMwExec := TRUE;
            7:
                IF xMwResetPending THEN
                    xMwResetPending := FALSE;
                    xMwResetTrigger := TRUE;
                    iMwIndex := C_MW_RESET;
                    xMwExec := TRUE;
                ELSE
                    xSkipStep := TRUE;
                END_IF;
            8:
                IF xMwOnPending THEN
                    xMwOnPending := FALSE;
                    iMwIndex := C_MW_RUN;
                    xMwExec := TRUE;
                ELSE
                    xSkipStep := TRUE;
                END_IF;
            9:
                IF xMwSetpointPending THEN
                    xMwSetpointPending := FALSE;
                    iMwIndex := C_MW_SETPOINT;
                    xMwExec := TRUE;
                ELSE
                    xSkipStep := TRUE;
                END_IF;
            10:
                IF xMwPreheatPending THEN
                    xMwPreheatPending := FALSE;
                    iMwIndex := C_MW_PREHEAT;
                    xMwExec := TRUE;
                ELSE
                    xSkipStep := TRUE;
                END_IF;
            ELSE
                xSkipStep := TRUE;
        END_CASE;

        IF xSkipStep THEN
            xAdvance := TRUE;
        ELSE
            iState := C_ST_WAIT;
        END_IF;

    C_ST_WAIT:
        IF xStepDone THEN
            IF xStepError THEN
                udiStepErrors := udiStepErrors + UDINT#1;
            END_IF;
            xPyroExec := FALSE;
            xMwExec := FALSE;
            xAdvance := TRUE;
        ELSIF fbStepTimeout.Q THEN
            udiStepTimeout := udiStepTimeout + UDINT#1;
            xPyroExec := FALSE;
            xMwExec := FALSE;
            xAbortStep := TRUE;
            iState := C_ST_ABORT;
        END_IF;

    C_ST_ABORT:
        IF (NOT fbPyroCh.xBusy) AND (NOT fbMwCh.xBusy) THEN
            xAbortStep := FALSE;
            xAdvance := TRUE;
        END_IF;

    ELSE
        xPyroExec := FALSE;
        xMwExec := FALSE;
        xAbortStep := FALSE;
        iState := C_ST_IDLE;

END_CASE;

IF xAdvance THEN
    IF iStep >= C_LAST_STEP THEN
        tCycleElapsed := fbCycleWatch.ET;
        IF tCycleElapsed > tCycleWorst THEN
            tCycleWorst := tCycleElapsed;
        END_IF;
        udiCycles := udiCycles + UDINT#1;
        iState := C_ST_IDLE;
    ELSE
        iStep := iStep + 1;
        iState := C_ST_START;
    END_IF;
END_IF;

fbPyroCh(slave := PYRO_RXT, xExecute := xPyroExec, xAbort := xAbortStep, iChannelIndex := iPyroIndex);
fbMwCh(slave := MW_GEN, xExecute := xMwExec, xAbort := xAbortStep, iChannelIndex := iMwIndex);
