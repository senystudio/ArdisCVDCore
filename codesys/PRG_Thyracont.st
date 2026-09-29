PROGRAM PRG_Thyracont
(* thyracont vacuum gauge *)
VAR
    fbThyracont : FB_ThyracontAscii;
    udiThyracontPort : UDINT := 5;
    udiThyracontAddress : UDINT := 1;
    uValue : U_DWORD_REAL;

END_VAR



(* poll the gauge *)
fbThyracont(
    xEnable := TRUE,
    udiPort := udiThyracontPort,
    udiAddress := udiThyracontAddress,
    udiBaudrate := 9600,
    tPollInterval := T#0MS);

(* pressure and status to the hmi, registers 140..147 *)
uValue.rValue := fbThyracont.rPressureTorr;
awHolding[140] := DWORD_TO_WORD(uValue.dwValue);
awHolding[141] := DWORD_TO_WORD(SHR(uValue.dwValue, 16));
uValue.rValue := fbThyracont.rPressureMbar;
awHolding[142] := DWORD_TO_WORD(uValue.dwValue);
awHolding[143] := DWORD_TO_WORD(SHR(uValue.dwValue, 16));
awHolding[144] := fbThyracont.wStatusFlags;
awHolding[145] := fbThyracont.wErrorCode;
awHolding[146] := DWORD_TO_WORD(fbThyracont.udiCycle);
awHolding[147] := DWORD_TO_WORD(SHR(fbThyracont.udiCycle, 16));
