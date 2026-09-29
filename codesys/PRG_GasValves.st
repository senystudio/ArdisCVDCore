PROGRAM PRG_GasValves
(* gas valves gpv1..gpv8 *)
VAR
    wGpvCommand : WORD;
    wGpvStatus  : WORD;
    i : INT;
END_VAR

(* command from the hmi, all closed on abort *)
IF xAbortActive THEN
    wGpvCommand := WORD#0;
ELSE
    wGpvCommand := awHolding[133] AND (NOT wGpvInhibit);
END_IF;

(* to the outputs *)
FOR i := 1 TO 8 DO
    axGpvOutput[i] := (wGpvCommand AND SHL(WORD#1, i - 1)) <> WORD#0;
END_FOR;

(* actual output state back to the hmi *)
wGpvStatus := WORD#0;
FOR i := 1 TO 8 DO
    IF axGpvOutput[i] THEN
        wGpvStatus := wGpvStatus OR SHL(WORD#1, i - 1);
    END_IF;
END_FOR;

awHolding[134] := wGpvStatus;
