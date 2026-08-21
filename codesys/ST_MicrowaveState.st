TYPE ST_MicrowaveState :
STRUCT
    xLastPreheatValue    : BOOL;
    xLastOnValue         : BOOL;
    xLastResetRequest    : BOOL;
    iLastSetpointRaw     : INT;
    wFaultReason         : WORD;
    iResetHoldScans      : INT;
    iPreheatElapsedScans : INT;
END_STRUCT
END_TYPE
