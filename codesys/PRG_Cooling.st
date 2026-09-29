PROGRAM PRG_Cooling
(* cooling: water temperature and flow per circuit, water and air pressure *)
VAR CONSTANT
    (* scaling to volts and sensor units *)
    C_INT_TO_VOLT : REAL := 100.0;

    C_TEMP_ZERO  : REAL := 1.0;
    C_TEMP_SPAN  : REAL := 25.0;

    C_FLOW_ZERO  : REAL := 1.0;
    C_FLOW_SPAN  : REAL := 4.0;

    C_WATER_BAR_PER_VOLT : REAL := 1.666;
    C_CDA_BAR_PER_VOLT   : REAL := 1.0;

    (* circuit indices *)
    C_STAGE    : INT := 0;
    C_CHAMBER  : INT := 1;
    C_MWHEAD   : INT := 2;
    C_MWPOWER  : INT := 3;
    C_TUNER    : INT := 4;
    C_INTERNAL : INT := 5;
    C_EXTERNAL : INT := 6;

    C_CIRCUIT_MAX : INT := 6;

    (* module channels *)
    C_MOD2_BASE : INT := 8;
    C_WATER_CH  : INT := 14;
    C_CDA_CH    : INT := 15;

    (* flow channel of each circuit, temperature on the next one *)
    C_FLOW_CH : ARRAY[0..6] OF INT := [0, 2, 4, 6, 12, 8, 10];

    (* status bits for air and water pressure *)
    C_CDA_BIT   : INT := 8;
    C_WATER_BIT : INT := 9;
END_VAR
VAR
    i          : INT;
    iFlowCh    : INT;
    iTempCh    : INT;
    dwValue    : DWORD;
    wTempOk    : WORD;
    wFlowOk    : WORD;

    arChanVolt : ARRAY[0..15] OF REAL;
    axChanOk   : ARRAY[0..15] OF BOOL;

    arTempC    : ARRAY[0..6] OF REAL;
    arFlowLpm  : ARRAY[0..6] OF REAL;
    rWaterBar  : REAL;
    rCdaBar    : REAL;
END_VAR

(* both modules to volts, bad channels read as zero *)
FOR i := 0 TO 7 DO
    axChanOk[i] := (awCoolMod1Status[i] = WORD#16#0000);
    IF axChanOk[i] THEN
        arChanVolt[i] := INT_TO_REAL(WORD_TO_INT(awCoolMod1Value[i])) / C_INT_TO_VOLT;
    ELSE
        arChanVolt[i] := 0.0;
    END_IF;

    axChanOk[C_MOD2_BASE + i] := (awCoolMod2Status[i] = WORD#16#0000);
    IF axChanOk[C_MOD2_BASE + i] THEN
        arChanVolt[C_MOD2_BASE + i] := INT_TO_REAL(WORD_TO_INT(awCoolMod2Value[i])) / C_INT_TO_VOLT;
    ELSE
        arChanVolt[C_MOD2_BASE + i] := 0.0;
    END_IF;
END_FOR;

(* temperature and flow per circuit *)
wTempOk := WORD#0;
wFlowOk := WORD#0;
FOR i := 0 TO C_CIRCUIT_MAX DO
    iFlowCh := C_FLOW_CH[i];
    iTempCh := iFlowCh + 1;

    IF axChanOk[iTempCh] THEN
        wTempOk := wTempOk OR SHL(WORD#1, i);
    END_IF;
    IF axChanOk[iFlowCh] THEN
        wFlowOk := wFlowOk OR SHL(WORD#1, i);
    END_IF;

    IF arChanVolt[iTempCh] > C_TEMP_ZERO THEN
        arTempC[i] := (arChanVolt[iTempCh] - C_TEMP_ZERO) * C_TEMP_SPAN;
    ELSE
        arTempC[i] := 0.0;
    END_IF;

    IF arChanVolt[iFlowCh] > C_FLOW_ZERO THEN
        arFlowLpm[i] := (arChanVolt[iFlowCh] - C_FLOW_ZERO) * C_FLOW_SPAN;
    ELSE
        arFlowLpm[i] := 0.0;
    END_IF;
END_FOR;

(* compressed air and water pressure *)
rCdaBar := arChanVolt[C_CDA_CH] / C_CDA_BAR_PER_VOLT;
IF axChanOk[C_CDA_CH] THEN
    wFlowOk := wFlowOk OR SHL(WORD#1, C_CDA_BIT);
END_IF;

rWaterBar := arChanVolt[C_WATER_CH] / C_WATER_BAR_PER_VOLT;
IF axChanOk[C_WATER_CH] THEN
    wFlowOk := wFlowOk OR SHL(WORD#1, C_WATER_BIT);
END_IF;

(* everything to the hmi, registers 206..239 *)
FOR i := 0 TO C_CIRCUIT_MAX DO
    dwValue := F_ToFixed(arTempC[i]);
    awHolding[206 + 2*i] := DWORD_TO_WORD(dwValue);
    awHolding[207 + 2*i] := DWORD_TO_WORD(SHR(dwValue, 16));

    dwValue := F_ToFixed(arFlowLpm[i]);
    awHolding[220 + 2*i] := DWORD_TO_WORD(dwValue);
    awHolding[221 + 2*i] := DWORD_TO_WORD(SHR(dwValue, 16));
END_FOR;

dwValue := F_ToFixed(rWaterBar);
awHolding[234] := DWORD_TO_WORD(dwValue);
awHolding[235] := DWORD_TO_WORD(SHR(dwValue, 16));

dwValue := F_ToFixed(rCdaBar);
awHolding[236] := DWORD_TO_WORD(dwValue);
awHolding[237] := DWORD_TO_WORD(SHR(dwValue, 16));

awHolding[238] := wTempOk;
awHolding[239] := wFlowOk;
