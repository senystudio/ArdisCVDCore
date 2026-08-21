FUNCTION_BLOCK FB_IncrementalPid
VAR_INPUT
    xEnable       : BOOL;
    xReset        : BOOL;
    xDirectMode   : BOOL;
    rSetpoint     : REAL;
    rMeasured     : REAL;
    rKp           : REAL;
    rKi           : REAL;
    rKd           : REAL;
    rLowerLimit   : REAL;
    rUpperLimit   : REAL;
    rDirectValue  : REAL;
    rCycleSeconds : REAL := 0.2;
END_VAR
VAR_OUTPUT
    rError  : REAL;
    rP      : REAL;
    rI      : REAL;
    rD      : REAL;
    rOutput : REAL;
END_VAR
VAR
    rPreviousError : REAL;
    xInitialized   : BOOL;
    rMin           : REAL;
    rMax           : REAL;
	arErrorBuffer      : ARRAY[0..99] OF REAL;
    i                  : INT;
    rErrAverageFirst   : REAL;
    rErrAverageLast    : REAL;
    rErrAverageIntegral : REAL;
END_VAR

IF rLowerLimit <= rUpperLimit THEN
    rMin := rLowerLimit;
    rMax := rUpperLimit;
ELSE
    rMin := rUpperLimit;
    rMax := rLowerLimit;
END_IF;

rError := rMeasured - rSetpoint;

IF NOT xEnable THEN
    rP := 0.0;
    rI := 0.0;
    rD := 0.0;
    rOutput := rMin;
    rPreviousError := rError;
    xInitialized := FALSE;
    RETURN;
END_IF;

IF xReset OR NOT xInitialized THEN
    rPreviousError := rError;
    rOutput := rMin;

    FOR i := 0 TO 99 DO
        arErrorBuffer[i] := rError;
    END_FOR;

    xInitialized := TRUE;
END_IF;

rP := rKp * rError;

IF rCycleSeconds > 0.001 THEN
    FOR i := 0 TO 98 DO
        arErrorBuffer[i] := arErrorBuffer[i + 1];
    END_FOR;
    arErrorBuffer[99] := rError;

    rErrAverageFirst := 0.0;
    FOR i := 87 TO 91 DO
        rErrAverageFirst := rErrAverageFirst + arErrorBuffer[i];
    END_FOR;
    rErrAverageFirst := rErrAverageFirst / 5.0;

    rErrAverageLast := 0.0;
    FOR i := 98 TO 99 DO
        rErrAverageLast := rErrAverageLast + arErrorBuffer[i];
    END_FOR;
    rErrAverageLast := rErrAverageLast / 2.0;

    rD := rKd * (rErrAverageLast - rErrAverageFirst);

    rErrAverageIntegral := 0.0;
    FOR i := 80 TO 99 DO
        rErrAverageIntegral := rErrAverageIntegral + arErrorBuffer[i];
    END_FOR;
    rErrAverageIntegral := rErrAverageIntegral / 20.0;

    rI := rKi * rErrAverageIntegral;
ELSE
    rD := 0.0;
    rI := 0.0;
END_IF;

rPreviousError := rError;

IF xDirectMode THEN
    rOutput := rDirectValue;
ELSE
    rOutput := rOutput + rP + rI + rD;
END_IF;

IF rOutput > rMax THEN
    rOutput := rMax;
ELSIF rOutput < rMin THEN
    rOutput := rMin;
END_IF;