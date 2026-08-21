PROGRAM PRG_Pyrometers
VAR
    uPyroValue : U_DWORD_REAL;
    rPyroRxtCh1    : REAL;
    rPyroRxtCh2    : REAL;
    rPyroRxtRatio  : REAL;
    wPyroRxtStatus : WORD;
    dwValue : DWORD;
END_VAR

uPyroValue.dwValue := SHL(WORD_TO_DWORD(awPyroRxtCh1Raw[1]), 16) OR WORD_TO_DWORD(awPyroRxtCh1Raw[0]);
rPyroRxtCh1 := uPyroValue.rValue;
uPyroValue.dwValue := SHL(WORD_TO_DWORD(awPyroRxtCh2Raw[1]), 16) OR WORD_TO_DWORD(awPyroRxtCh2Raw[0]);
rPyroRxtCh2 := uPyroValue.rValue;
uPyroValue.dwValue := SHL(WORD_TO_DWORD(awPyroRxtRatioRaw[1]), 16) OR WORD_TO_DWORD(awPyroRxtRatioRaw[0]);
rPyroRxtRatio := uPyroValue.rValue;

dwValue := F_ToFixed(rPyroRxtCh1);
awHolding[160] := DWORD_TO_WORD(dwValue);
awHolding[161] := DWORD_TO_WORD(SHR(dwValue, 16));
dwValue := F_ToFixed(rPyroRxtCh2);
awHolding[162] := DWORD_TO_WORD(dwValue);
awHolding[163] := DWORD_TO_WORD(SHR(dwValue, 16));
dwValue := F_ToFixed(rPyroRxtRatio);
awHolding[164] := DWORD_TO_WORD(dwValue);
awHolding[165] := DWORD_TO_WORD(SHR(dwValue, 16));

wPyroRxtStatus := WORD#0;
IF NOT PYRO_RXT.xError THEN
    wPyroRxtStatus := wPyroRxtStatus OR WORD#16#0001;
END_IF;
IF (wPyroRxtAdcStatus AND WORD#16#0001) <> WORD#0 THEN
    wPyroRxtStatus := wPyroRxtStatus OR WORD#16#0002;
END_IF;
IF (wPyroRxtAdcStatus AND WORD#16#0002) <> WORD#0 THEN
    wPyroRxtStatus := wPyroRxtStatus OR WORD#16#0004;
END_IF;
IF PYRO_RXT.xError THEN
    wPyroRxtStatus := wPyroRxtStatus OR WORD#16#0008;
END_IF;
awHolding[166] := wPyroRxtStatus;
awHolding[167] := wPyroRxtDevStatus;
