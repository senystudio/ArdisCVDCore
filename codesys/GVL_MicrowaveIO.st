VAR_GLOBAL
    awMwPowerRaw       : ARRAY[0..1] OF WORD;
    axMwStatusBlock1   : ARRAY[0..1] OF BOOL;
    axMwStatusBlock2   : ARRAY[0..5] OF BOOL;
    axMwStatusBlock3   : ARRAY[0..7] OF BOOL;

    xMwPreheatValue    : BOOL;
    xMwPreheatTrigger  : BOOL;
    xMwOnValue         : BOOL;
    xMwOnTrigger       : BOOL;
    xMwResetTrigger    : BOOL;
    wMwSetpointRaw     : WORD;
    xMwSetpointTrigger : BOOL;
END_VAR
