FUNCTION F_MbReadStep : INT
VAR_INPUT
    xReadDone   : BOOL;
    xReadError  : BOOL;
    udiReadSize : UDINT;
END_VAR
VAR_IN_OUT
    xReadExecute    : BOOL;
    xRetriggerArmed : BOOL;
    iRxAccum        : INT;
    iRxIdleScans    : INT;
END_VAR
VAR CONSTANT
    C_MAX_FRAME       : INT := 8;
    C_IDLE_SCAN_LIMIT : INT := 2;
    C_NODATA_LIMIT    : INT := 3;
END_VAR

IF xRetriggerArmed THEN
    xRetriggerArmed := FALSE;
    xReadExecute := TRUE;
    F_MbReadStep := 0;
    RETURN;
END_IF;

IF xReadError THEN
    xReadExecute := FALSE;
    F_MbReadStep := 2;
    RETURN;
END_IF;

IF NOT xReadDone THEN
    F_MbReadStep := 0;
    RETURN;
END_IF;

IF udiReadSize > UDINT#0 THEN
    iRxAccum := iRxAccum + UDINT_TO_INT(udiReadSize);
    iRxIdleScans := 0;
ELSE
    iRxIdleScans := iRxIdleScans + 1;
END_IF;

IF iRxAccum >= C_MAX_FRAME THEN
    xReadExecute := FALSE;
    F_MbReadStep := 1;
    RETURN;
END_IF;

IF (iRxAccum > 0) AND (iRxIdleScans >= C_IDLE_SCAN_LIMIT) THEN
    xReadExecute := FALSE;
    F_MbReadStep := 1;
    RETURN;
END_IF;

IF (iRxAccum = 0) AND (iRxIdleScans >= C_NODATA_LIMIT) THEN
    xReadExecute := FALSE;
    F_MbReadStep := 2;
    RETURN;
END_IF;

xReadExecute := FALSE;
xRetriggerArmed := TRUE;
F_MbReadStep := 0;
