PROGRAM PRG_VacuumOutputs
VAR
    wVpvCommand  : WORD;
    wVpvStatus   : WORD;
    wPumpCommand : WORD;
    wPumpStatus  : WORD;
    i : INT;
END_VAR

wVpvCommand := awHolding[135];

FOR i := 1 TO 8 DO
    axVpvOutput[i] := (wVpvCommand AND SHL(WORD#1, i - 1)) <> WORD#0;
END_FOR;

wVpvStatus := WORD#0;
FOR i := 1 TO 8 DO
    IF axVpvOutput[i] THEN
        wVpvStatus := wVpvStatus OR SHL(WORD#1, i - 1);
    END_IF;
END_FOR;

awHolding[136] := wVpvStatus;

wPumpCommand := awHolding[137];
xWaterPumpOutput := (wPumpCommand AND WORD#16#0001) <> WORD#0;
xForeVacPumpOutput := (wPumpCommand AND WORD#16#0002) <> WORD#0;

wPumpStatus := WORD#0;
IF xWaterPumpOutput THEN
    wPumpStatus := wPumpStatus OR WORD#16#0001;
END_IF;
IF xForeVacPumpOutput THEN
    wPumpStatus := wPumpStatus OR WORD#16#0002;
END_IF;

awHolding[138] := wPumpStatus;
