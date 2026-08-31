VAR_GLOBAL
    awMwPowerRaw       : ARRAY[0..1] OF WORD;
    axMwStatusBlock1   : ARRAY[0..1] OF BOOL;
    axMwStatusBlock2   : ARRAY[0..5] OF BOOL;
    axMwStatusBlock3   : ARRAY[0..7] OF BOOL;

    xMwPreheatValue    : BOOL;
    xMwOnValue         : BOOL;
    xMwResetTrigger    : BOOL;
    wMwSetpointRaw     : WORD;

    xMwPreheatPending  : BOOL;
    xMwOnPending       : BOOL;
    xMwResetPending    : BOOL;
    xMwSetpointPending : BOOL;
END_VAR
