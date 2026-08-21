PROGRAM PRG_GasValves
VAR
    wGpvCommand : WORD;
    wGpvStatus  : WORD;
    i : INT;
END_VAR

wGpvCommand := awHolding[133];

FOR i := 1 TO 8 DO
    axGpvOutput[i] := (wGpvCommand AND SHL(WORD#1, i - 1)) <> WORD#0;
END_FOR;

wGpvStatus := WORD#0;
FOR i := 1 TO 8 DO
    IF axGpvOutput[i] THEN
        wGpvStatus := wGpvStatus OR SHL(WORD#1, i - 1);
    END_IF;
END_FOR;

awHolding[134] := wGpvStatus;
