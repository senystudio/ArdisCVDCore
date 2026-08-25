PROGRAM PLC_PRG
VAR
    xDoBlinkTest : BOOL;
    wBenchMuAo1Output : ARRAY[0..0] OF WORD;
    xTrafficLightGreen  : BOOL;
    xTrafficLightYellow : BOOL;
    xTrafficLightRed    : BOOL;
    wTrafficLightMask   : WORD;
  
    xHmiWriteSeen         : BOOL;
    udiHmiStaleCycles     : UDINT;
    xHmiLinkLost          : BOOL;
    
    awMvAi1Value  : ARRAY[0..1] OF WORD;
    awMvAi1Status : ARRAY[0..0] OF WORD;
    uMvAi1Value   : U_DWORD_REAL;
    wMvAi1Status  : WORD;
    xMvAi1Ok      : BOOL;
    rMvAi1Percent : REAL;
    rMvAi1Voltage : REAL;
    rChMeasuredMv : REAL;
	
	wDiscreteInputs : WORD;
    iDiIndex        : INT;
    
    rChamberMuAo1Command : REAL;
    xChamberMuAo1Clamped : BOOL;
    uiChamberMuAo1Command : UINT;
    fbChamber : FB_IncrementalPid;
    fbPlenum  : FB_IncrementalPid;
    xProtocolOk    : BOOL;
    xChamberEnable : BOOL;
    xPlenumEnable  : BOOL;
    xReset         : BOOL;
    udiCycle       : UDINT;
    rChSetpoint : REAL;
    rChMeasured : REAL;
    rChKp       : REAL;
    rChKi       : REAL;
    rChKd       : REAL;
    rChLower    : REAL;
    rChUpper    : REAL;
    rChDirect   : REAL;
    rPlSetpoint : REAL;
    rPlMeasured : REAL;
    rPlKp       : REAL;
    rPlKi       : REAL;
    rPlKd       : REAL;
    rPlLower    : REAL;
    rPlUpper    : REAL;
    rPlDirect   : REAL;
    dwValue     : DWORD;
    xDoBlinkTestEnable : BOOL;
    xBenchMuAo1Enable : BOOL;
    xBenchMuAo1Clamped : BOOL;
    uiBenchMuAo1Command : UINT;
    uiBenchMuAo1Applied : UINT;
    awMvAi1Raw : ARRAY[0..1] OF WORD;
END_VAR
VAR CONSTANT
    (* MainTask cycles, not seconds: 15 x 200 ms = 3 s of silence before the
       link counts as lost, against an HMI that writes every ~200 ms. This is
       hostage to the task period -- at a 20 ms MainTask it would fire after
       300 ms -- so if MainTask is ever retimed, retime this with it. *)
    HMI_STALE_CYCLES  : UDINT := UDINT#15;
END_VAR

xProtocolOk := awHolding[0] = WORD#1;
xChamberEnable := xProtocolOk AND (awHolding[1] AND WORD#16#0001) <> WORD#0;
xPlenumEnable := xProtocolOk AND (awHolding[1] AND WORD#16#0002) <> WORD#0;
xReset := (awHolding[1] AND WORD#16#0004) <> WORD#0;
xTrafficLightGreen  := (awHolding[1] AND WORD#16#0010) <> WORD#0;
xTrafficLightYellow := (awHolding[1] AND WORD#16#0020) <> WORD#0;
xTrafficLightRed    := (awHolding[1] AND WORD#16#0040) <> WORD#0;


xHmiWriteSeen := (awHolding[1] AND WORD#16#8000) = WORD#0;

IF xHmiWriteSeen THEN
    udiHmiStaleCycles := UDINT#0;
ELSIF udiHmiStaleCycles < HMI_STALE_CYCLES THEN
    udiHmiStaleCycles := udiHmiStaleCycles + UDINT#1;
END_IF;

(* This assignment was missing: the counter above ran, but nothing ever turned
   it into the flag the tower is driven from, so xHmiLinkLost stayed FALSE for
   ever and the lost-link branch below was dead code. *)
xHmiLinkLost := udiHmiStaleCycles >= HMI_STALE_CYCLES;

(* Cleared every cycle. Without this the ELSE branch below ORs onto last
   cycle's value, so a lamp that came on once could never go off again. *)
wTrafficLightMask := WORD#0;
IF xHmiLinkLost THEN
    (* The lamp bits are stale by definition here, so they are not consulted at
       all: all three lamps together is the whole verdict. The HMI itself never
       commands that combination -- ComputeFlags in PLC210PidClient.cs lights
       exactly one -- so it cannot be mistaken for a state the operator put the
       tower into. *)
    wTrafficLightMask := WORD#16#0007;
ELSE
    IF xTrafficLightGreen THEN
        wTrafficLightMask := wTrafficLightMask OR WORD#16#0001;
    END_IF;
    IF xTrafficLightYellow THEN
        wTrafficLightMask := wTrafficLightMask OR WORD#16#0002;
    END_IF;
    IF xTrafficLightRed THEN
        wTrafficLightMask := wTrafficLightMask OR WORD#16#0004;
    END_IF;
END_IF;


awHolding[1] := awHolding[1] OR WORD#16#8000;

rChamberMuAo1Command := fbChamber.rOutput / 10.0;
xChamberMuAo1Clamped := rChamberMuAo1Command > 500.0;

IF xChamberMuAo1Clamped THEN
    uiChamberMuAo1Command := UINT#500;
ELSE
    uiChamberMuAo1Command := REAL_TO_UINT(rChamberMuAo1Command);
END_IF;

wMvAi1Status := awMvAi1Status[0];
xMvAi1Ok := wMvAi1Status = WORD#16#0000;

uMvAi1Value.dwValue := SHL(WORD_TO_DWORD(awMvAi1Value[1]), 16) OR WORD_TO_DWORD(awMvAi1Value[0]);	
rMvAi1Percent := uMvAi1Value.rValue;
rMvAi1Voltage := LIMIT(0.0, rMvAi1Percent / 10.0, 10.0);

IF xMvAi1Ok THEN
    rChMeasuredMv := LIMIT(0.0, rMvAi1Percent * 10.0, 1000.0);
END_IF;

rChSetpoint := F_ReadFixed(awHolding[4], awHolding[5]);
rChMeasured := rChMeasuredMv;
rChKp := F_ReadFixed(awHolding[8], awHolding[9]);
rChKi := F_ReadFixed(awHolding[10], awHolding[11]);
rChKd := F_ReadFixed(awHolding[12], awHolding[13]);
rChLower := F_ReadFixed(awHolding[14], awHolding[15]);
rChUpper := F_ReadFixed(awHolding[16], awHolding[17]);
rChDirect := F_ReadFixed(awHolding[18], awHolding[19]);

rPlSetpoint := F_ReadFixed(awHolding[30], awHolding[31]);
rPlMeasured := F_ReadFixed(awHolding[32], awHolding[33]);
rPlKp := F_ReadFixed(awHolding[34], awHolding[35]);
rPlKi := F_ReadFixed(awHolding[36], awHolding[37]);
rPlKd := F_ReadFixed(awHolding[38], awHolding[39]);
rPlLower := F_ReadFixed(awHolding[40], awHolding[41]);
rPlUpper := F_ReadFixed(awHolding[42], awHolding[43]);
rPlDirect := F_ReadFixed(awHolding[44], awHolding[45]);

fbChamber(
    xEnable := xChamberEnable,
    xReset := xReset,
    xDirectMode := awHolding[20] <> WORD#0,
    rSetpoint := rChSetpoint,
    rMeasured := rChMeasured,
    rKp := rChKp,
    rKi := rChKi,
    rKd := rChKd,
    rLowerLimit := rChLower,
    rUpperLimit := rChUpper,
    rDirectValue := rChDirect,
    rCycleSeconds := 0.2);

fbPlenum(
    xEnable := xPlenumEnable,
    xReset := xReset,
    xDirectMode := awHolding[46] <> WORD#0,
    rSetpoint := rPlSetpoint,
    rMeasured := rPlMeasured,
    rKp := rPlKp,
    rKi := rPlKi,
    rKd := rPlKd,
    rLowerLimit := rPlLower,
    rUpperLimit := rPlUpper,
    rDirectValue := rPlDirect,
    rCycleSeconds := 0.2);

udiCycle := udiCycle + 1;
xDoBlinkTestEnable := awHolding[150] = WORD#16#A55A;
xDoBlinkTest := xDoBlinkTestEnable AND ((udiCycle MOD UDINT#10) < UDINT#5);

awHolding[100] := WORD#1;
IF xProtocolOk THEN
    awHolding[101] := WORD#16#0003;
ELSE
    awHolding[101] := WORD#0;
END_IF;
awHolding[102] := DWORD_TO_WORD(udiCycle);
awHolding[103] := DWORD_TO_WORD(SHR(udiCycle, 16));

dwValue := F_ToFixed(fbChamber.rError);
awHolding[104] := DWORD_TO_WORD(dwValue);
awHolding[105] := DWORD_TO_WORD(SHR(dwValue, 16));
dwValue := F_ToFixed(fbChamber.rP);
awHolding[106] := DWORD_TO_WORD(dwValue);
awHolding[107] := DWORD_TO_WORD(SHR(dwValue, 16));
dwValue := F_ToFixed(fbChamber.rI);
awHolding[108] := DWORD_TO_WORD(dwValue);
awHolding[109] := DWORD_TO_WORD(SHR(dwValue, 16));
dwValue := F_ToFixed(fbChamber.rD);
awHolding[110] := DWORD_TO_WORD(dwValue);
awHolding[111] := DWORD_TO_WORD(SHR(dwValue, 16));
dwValue := F_ToFixed(fbChamber.rOutput);
awHolding[112] := DWORD_TO_WORD(dwValue);
awHolding[113] := DWORD_TO_WORD(SHR(dwValue, 16));

dwValue := F_ToFixed(fbPlenum.rError);
awHolding[120] := DWORD_TO_WORD(dwValue);
awHolding[121] := DWORD_TO_WORD(SHR(dwValue, 16));
dwValue := F_ToFixed(fbPlenum.rP);
awHolding[122] := DWORD_TO_WORD(dwValue);
awHolding[123] := DWORD_TO_WORD(SHR(dwValue, 16));
dwValue := F_ToFixed(fbPlenum.rI);
awHolding[124] := DWORD_TO_WORD(dwValue);
awHolding[125] := DWORD_TO_WORD(SHR(dwValue, 16));
dwValue := F_ToFixed(fbPlenum.rD);
awHolding[126] := DWORD_TO_WORD(dwValue);
awHolding[127] := DWORD_TO_WORD(SHR(dwValue, 16));
dwValue := F_ToFixed(fbPlenum.rOutput);
awHolding[128] := DWORD_TO_WORD(dwValue);
awHolding[129] := DWORD_TO_WORD(SHR(dwValue, 16));

dwValue := F_ToFixed(rChMeasuredMv);
awHolding[130] := DWORD_TO_WORD(dwValue);
awHolding[131] := DWORD_TO_WORD(SHR(dwValue, 16));
awHolding[132] := wMvAi1Status;

wDiscreteInputs := WORD#0;
FOR iDiIndex := 1 TO 8 DO
    IF axFastDigitalIn[iDiIndex] THEN
        wDiscreteInputs := wDiscreteInputs OR SHL(WORD#1, iDiIndex - 1);
    END_IF;
END_FOR;
FOR iDiIndex := 9 TO 12 DO
    IF axDigitalIn[iDiIndex] THEN
        wDiscreteInputs := wDiscreteInputs OR SHL(WORD#1, iDiIndex - 1);
    END_IF;
END_FOR;
awHolding[139] := wDiscreteInputs;


uiBenchMuAo1Command := WORD_TO_UINT(awHolding[154]);
xBenchMuAo1Enable := awHolding[155] = WORD#16#A55A;
xBenchMuAo1Clamped := uiBenchMuAo1Command > UINT#500;

IF xBenchMuAo1Clamped THEN
    uiBenchMuAo1Command := UINT#500;
END_IF;

IF xBenchMuAo1Enable THEN
    uiBenchMuAo1Applied := uiBenchMuAo1Command;
ELSE
    uiBenchMuAo1Applied := UINT#0;
END_IF;


IF xBenchMuAo1Enable THEN
    awHolding[157] := awHolding[157] OR WORD#16#0001;
END_IF;

IF xBenchMuAo1Clamped THEN
    awHolding[157] := awHolding[157] OR WORD#16#0002;
END_IF;


IF fbChamber.rOutput <= 0.0 THEN
    uiChamberMuAo1Command := UINT#0;
    xChamberMuAo1Clamped := FALSE;
ELSE
    rChamberMuAo1Command := fbChamber.rOutput / 10.0;
    xChamberMuAo1Clamped := rChamberMuAo1Command > 500.0;

    IF xChamberMuAo1Clamped THEN
        uiChamberMuAo1Command := UINT#500;
    ELSE
        uiChamberMuAo1Command := REAL_TO_UINT(rChamberMuAo1Command);
    END_IF;
END_IF;

IF xBenchMuAo1Enable THEN
    uiBenchMuAo1Applied := uiBenchMuAo1Command;
ELSIF xChamberEnable THEN
    uiBenchMuAo1Applied := uiChamberMuAo1Command;
ELSE
    uiBenchMuAo1Applied := UINT#0;
END_IF;

wBenchMuAo1Output[0] := UINT_TO_WORD(uiBenchMuAo1Applied);
awHolding[156] := wBenchMuAo1Output[0];

