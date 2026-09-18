PROGRAM PRG_TurboPump
VAR
    xCommandValid    : BOOL;
    xRunRequest      : BOOL;
    wCommandWanted   : WORD;
    wCommandSent     : WORD;
    xCommandIssued   : BOOL;

    wLatchedStatus : WORD;
    wLinkStatus    : WORD;
END_VAR
VAR CONSTANT
    C_CMD_RUN  : WORD := WORD#16#0008;
    C_CMD_STOP : WORD := WORD#16#0003;
END_VAR

xCommandValid := (awHolding[240] AND WORD#16#8000) <> WORD#0;
xRunRequest := (awHolding[240] AND WORD#16#0001) <> WORD#0;

IF xRunRequest THEN
    wCommandWanted := C_CMD_RUN;
ELSE
    wCommandWanted := C_CMD_STOP;
END_IF;

IF xCommandValid AND ((NOT xCommandIssued) OR (wCommandWanted <> wCommandSent)) THEN
    wTurboCommandValue := wCommandWanted;
    xTurboCommandTrigger := TRUE;
    wCommandSent := wCommandWanted;
    xCommandIssued := TRUE;
ELSE
    xTurboCommandTrigger := FALSE;
END_IF;

wLatchedStatus := WORD#0;
IF wCommandSent = C_CMD_RUN THEN
    wLatchedStatus := wLatchedStatus OR WORD#16#0001;
END_IF;
IF xCommandIssued THEN
    wLatchedStatus := wLatchedStatus OR WORD#16#0002;
END_IF;

wLinkStatus := WORD#0;
IF NOT TURBO_PUMP.xError THEN
    wLinkStatus := wLinkStatus OR WORD#16#0001;
END_IF;

awHolding[241] := wLatchedStatus;
awHolding[242] := awTurboStatusRaw[0];
awHolding[243] := awTurboStatusRaw[1];
awHolding[244] := awTurboStatusRaw[2];
awHolding[245] := awTurboStatusRaw[3];
awHolding[246] := wTurboTempRaw AND WORD#16#00FF;
awHolding[247] := wLinkStatus;
