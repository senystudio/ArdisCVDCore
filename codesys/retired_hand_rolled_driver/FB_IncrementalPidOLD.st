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
    rFlowCorrection     : REAL := 1.0;
    rPressureCorrection : REAL := 1.0;
    rTotalGasFlow       : REAL := 0.0;
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
    rIntegral      : REAL;
    rPreviousError : REAL;
    xInitialized   : BOOL;
    rMin           : REAL;
    rMax           : REAL;
    rFlowScale     : REAL;
    rPressureScale : REAL;
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
    rIntegral := 0.0;
    rPreviousError := rError;
    xInitialized := FALSE;
    RETURN;
END_IF;

IF xReset OR NOT xInitialized THEN
    rIntegral := 0.0;
    rPreviousError := rError;
    rOutput := rMin;
    xInitialized := TRUE;
END_IF;

rP := rKp * rError;

IF rCycleSeconds > 0.001 THEN
    rIntegral := rIntegral + rError * rCycleSeconds;
    rD := rKd * (rError - rPreviousError) / rCycleSeconds;
ELSE
    rD := 0.0;
END_IF;

IF (rFlowCorrection > 0.001) AND (rTotalGasFlow > 50.0) THEN
    IF rTotalGasFlow <= 1100.0 THEN
        rFlowScale := ((rTotalGasFlow - 50.0) / (1100.0 - 50.0)) * (rFlowCorrection - 1.0) + 1.0;
    ELSE
        rFlowScale := rFlowCorrection;
    END_IF;

    IF rFlowScale > 0.001 THEN
        rP := rP / rFlowScale;
        rD := rD / rFlowScale;
    END_IF;
END_IF;

IF (rPressureCorrection > 0.001) AND (rMeasured > 10.0) THEN
    IF rMeasured < 500.0 THEN
        rPressureScale := ((rMeasured - 10.0) / (500.0 - 10.0)) * (rPressureCorrection - 1.0) + 1.0;
    ELSE
        rPressureScale := rPressureCorrection;
    END_IF;

    IF rPressureScale > 0.001 THEN
        rP := rP / rPressureScale;
        rD := rD / rPressureScale;
    END_IF;
END_IF;

IF rIntegral > 1000000.0 THEN
    rIntegral := 1000000.0;
ELSIF rIntegral < -1000000.0 THEN
    rIntegral := -1000000.0;
END_IF;

rI := rKi * rIntegral;
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
