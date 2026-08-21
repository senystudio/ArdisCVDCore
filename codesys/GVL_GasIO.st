VAR_GLOBAL
    awMeasuredRaw     : ARRAY[0..5] OF WORD;
    awSetpointRaw     : ARRAY[0..5] OF WORD;
    axSetpointTrigger : ARRAY[0..5] OF BOOL;

    awModeValue       : ARRAY[0..5] OF WORD;
    axModeTrigger     : ARRAY[0..5] OF BOOL;

    awRampTimeValue   : ARRAY[0..5] OF WORD;
    axRampTimeTrigger : ARRAY[0..5] OF BOOL;

    awRampStepValue   : ARRAY[0..5] OF WORD;
    axRampStepTrigger : ARRAY[0..5] OF BOOL;

    awRampEnValue     : ARRAY[0..5] OF WORD;
    axRampEnTrigger   : ARRAY[0..5] OF BOOL;
END_VAR
