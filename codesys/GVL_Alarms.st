VAR_GLOBAL RETAIN
    wRetainMagic      : WORD;
    wParamAlarmEnable : WORD;
    wParamAbortEnable : WORD;
    abParamAlarmPct   : ARRAY[0..6] OF BYTE;
    abParamAbortPct   : ARRAY[0..6] OF BYTE;
    wMwReflAlarmWatt  : WORD;
    wMwReflAbortWatt  : WORD;
    wTempEnable       : WORD;
    wSampleTargetC    : WORD;
    bSampleAlarmPct   : BYTE;
    abWaterTargetC    : ARRAY[0..3] OF BYTE;
    abWaterAlarmPct   : ARRAY[0..3] OF BYTE;
    abWaterAbortPct   : ARRAY[0..3] OF BYTE;
    wInputEnable      : WORD;
    wInputReaction    : WORD;
    uiLidInput        : UINT;
END_VAR

VAR_GLOBAL
    awAlarmMask     : ARRAY[0..3] OF WORD;
    awAbortMask     : ARRAY[0..3] OF WORD;
    awAbortLatched  : ARRAY[0..3] OF WORD;
    xAbortActive    : BOOL;
    xRunActive      : BOOL;
    wGpvInhibit     : WORD;
    wVpvInhibit     : WORD;
    xForeVacInhibit : BOOL;
    xTurboInhibit   : BOOL;
    xMwInhibit      : BOOL;
    xThresholdsOk   : BOOL;
    xRetainWasBlank : BOOL;
    uiAbortCount    : UINT;
END_VAR
