PROGRAM PRG_Debug
(* debug commands from the hmi system settings window *)
VAR
    wCommand     : WORD;
    wLastCommand : WORD;
    byCode       : BYTE;
    xFirstScan   : BOOL := TRUE;

    axMfcResetRequest : ARRAY[0..5] OF BOOL;
    afbMfcReset       : ARRAY[0..5] OF TP;
    xRebootRequest    : BOOL;
    fbRebootPulse     : TP;
    i : INT;
END_VAR
VAR CONSTANT
    C_RESET_ALL_MFC   : BYTE := 1;
    C_RESET_MFC_FIRST : BYTE := 2;
    C_REINIT_GAS      : BYTE := 20;
    C_REBOOT          : BYTE := 30;
END_VAR

(* register 148: low byte command, high byte sequence; run once per new word *)
wCommand := awHolding[148];
IF xFirstScan THEN
    wLastCommand := wCommand;
    xFirstScan := FALSE;
END_IF;

FOR i := 0 TO 5 DO
    axMfcResetRequest[i] := FALSE;
END_FOR;
xRebootRequest := FALSE;

IF wCommand <> wLastCommand THEN
    wLastCommand := wCommand;
    byCode := WORD_TO_BYTE(wCommand AND WORD#16#00FF);

    IF byCode = C_RESET_ALL_MFC THEN
        FOR i := 0 TO 5 DO
            axMfcResetRequest[i] := TRUE;
        END_FOR;
    ELSIF (byCode >= C_RESET_MFC_FIRST) AND (byCode <= C_RESET_MFC_FIRST + 5) THEN
        axMfcResetRequest[BYTE_TO_INT(byCode - C_RESET_MFC_FIRST)] := TRUE;
    ELSIF byCode = C_REINIT_GAS THEN
        uiDbgGasReinitCount := uiDbgGasReinitCount + 1;
    ELSIF byCode = C_REBOOT THEN
        xRebootRequest := TRUE;
    END_IF;
END_IF;

(* echo of the last accepted command *)
awHolding[149] := wLastCommand;

(* mfc link reset, 500 ms pulse per controller *)
FOR i := 0 TO 5 DO
    afbMfcReset[i](IN := axMfcResetRequest[i], PT := T#500MS);
END_FOR;
MFC_H2.xReset  := afbMfcReset[0].Q;
MFC_CH4.xReset := afbMfcReset[1].Q;
MFC_O2.xReset  := afbMfcReset[2].Q;
MFC_N2.xReset  := afbMfcReset[3].Q;
MFC_Ar.xReset  := afbMfcReset[4].Q;
MFC_H22.xReset := afbMfcReset[5].Q;

(* controller reboot: xDbgReboot is bound to the watchdog node reboot channel *)
fbRebootPulse(IN := xRebootRequest, PT := T#2S);
xDbgReboot := fbRebootPulse.Q;
