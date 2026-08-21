FUNCTION_BLOCK FB_ThyracontAscii
VAR_INPUT
    xEnable : BOOL := TRUE;
    udiPort : UDINT := 1;
    udiAddress : UDINT := 1;
    udiBaudrate : UDINT := 9600;
    tPollInterval : TIME := T#1S;
END_VAR
VAR_OUTPUT
    xValid : BOOL;
    xConnected : BOOL;
    rPressureMbar : REAL;
    rPressureTorr : REAL;
    wStatusFlags : WORD;
    wErrorCode : WORD;
    udiCycle : UDINT;
END_VAR
VAR
    fbOpen : COM.Open;
    fbWrite : COM.Write;
    fbRead : COM.Read;
    aParams : ARRAY[1..7] OF COM.PARAMETER;
    hCom : CAA.HANDLE;
    (* aTx[0..2] (ADR) and aTx[8] (checksum) are overwritten every scan below
       from udiAddress -- the "001"/'D' baked into this literal is only the
       switch-position-1 default. *)
    aTx : ARRAY[0..9] OF BYTE := [BYTE#48, BYTE#48, BYTE#49, BYTE#48, BYTE#77, BYTE#86, BYTE#48, BYTE#48, BYTE#68, BYTE#13];
    aRx : ARRAY[0..63] OF BYTE;
    aAdr : ARRAY[0..2] OF BYTE;
    iAddr : INT;
    iChecksum : INT;
    iState : INT;
    udiPollTicks : UDINT;
    udiPollTicksMax : UDINT := 5;
    xOpenExecute : BOOL;
    xWriteExecute : BOOL;
    xReadExecute : BOOL;
    i : INT;
    j : INT;
    iSize : INT;
    iFrameStart : INT;
    iDataLen : INT;
    iDataStart : INT;
    iDigit : INT;
    iSign : INT;
    iExpSign : INT;
    iExp : INT;
    rParsed : REAL;
    rDiv : REAL;
    xFraction : BOOL;
    xExponent : BOOL;
    xHasDigit : BOOL;
END_VAR

IF NOT xEnable THEN
    xOpenExecute := FALSE;
    xWriteExecute := FALSE;
    xReadExecute := FALSE;
    xConnected := FALSE;
    xValid := FALSE;
    wStatusFlags := WORD#0;
    wErrorCode := WORD#0;
    iState := 0;
    RETURN;
END_IF;

aParams[1].udiParameterId := COM.CAA_Parameter_Constants.udiPort;
aParams[1].udiValue := udiPort;
aParams[2].udiParameterId := COM.CAA_Parameter_Constants.udiBaudrate;
aParams[2].udiValue := udiBaudrate;
(* Confirmed via CODESYS Library Manager, CAA SerialCom (COM) docs, PARITY/
   STOPBIT enums: PARITY EVEN=0/ODD=1/NONE=2, STOPBIT ONESTOPBIT=0/
   ONE5STOPBITS=1/TWOSTOPBITS=2. So parity=2 = NONE and stopbits=0 =
   ONESTOPBIT -- exactly what the sensor needs (no parity, 1 stop bit;
   matches ArdisCVDMaster_cfg.xml HiVacuumSensorSerialPortParameters and the
   Thyracont manual). *)
aParams[3].udiParameterId := COM.CAA_Parameter_Constants.udiParity;
aParams[3].udiValue := 2;   (* NONE *)
aParams[4].udiParameterId := COM.CAA_Parameter_Constants.udiStopBits;
aParams[4].udiValue := 0;   (* ONESTOPBIT *)
aParams[5].udiParameterId := COM.CAA_Parameter_Constants.udiTimeout;
aParams[5].udiValue := 200000;
aParams[6].udiParameterId := COM.CAA_Parameter_Constants.udiByteSize;
aParams[6].udiValue := 8;
aParams[7].udiParameterId := COM.CAA_Parameter_Constants.udiBinary;
aParams[7].udiValue := 1;

(* Derive the 3-digit ASCII address and request checksum from udiAddress
   every scan, so the rotary-switch position is a one-line change at the
   call site instead of a hand-edited byte array + recomputed checksum. *)
iAddr := UDINT_TO_INT(udiAddress);
IF (iAddr < 1) OR (iAddr > 16) THEN
    iAddr := 1;
END_IF;
aAdr[0] := INT_TO_BYTE(iAddr / 100 MOD 10 + 48);
aAdr[1] := INT_TO_BYTE(iAddr / 10 MOD 10 + 48);
aAdr[2] := INT_TO_BYTE(iAddr MOD 10 + 48);
aTx[0] := aAdr[0];
aTx[1] := aAdr[1];
aTx[2] := aAdr[2];

iChecksum := 0;
FOR i := 0 TO 7 DO
    iChecksum := iChecksum + BYTE_TO_INT(aTx[i]);
END_FOR;
aTx[8] := INT_TO_BYTE(iChecksum MOD 64 + 64);

(* tPollInterval <= 0 means "no idle at all" -- go straight back to state 10
   the very next scan after a poll finishes, instead of waiting a tick.
   Only saves one scan's worth of time per cycle (the write/read stages
   still each cost their own scan), but it's free. *)
IF tPollInterval <= T#0MS THEN
    udiPollTicksMax := 0;
ELSIF tPollInterval <= T#200MS THEN
    udiPollTicksMax := 1;
ELSE
    udiPollTicksMax := TIME_TO_UDINT(tPollInterval) / 200;
    IF udiPollTicksMax < 1 THEN
        udiPollTicksMax := 1;
    END_IF;
END_IF;

CASE iState OF
    0:
        xOpenExecute := TRUE;
        xConnected := FALSE;
        xValid := FALSE;
        wErrorCode := WORD#1;
        IF fbOpen.xDone THEN
            hCom := fbOpen.hCom;
            xOpenExecute := FALSE;
            xConnected := TRUE;
            wErrorCode := WORD#0;
            iState := 10;
        ELSIF fbOpen.xError THEN
            xOpenExecute := FALSE;
            wErrorCode := WORD#2;
            iState := 50;
        END_IF;

    10:
        udiPollTicks := 0;
        FOR i := 0 TO 63 DO
            aRx[i] := BYTE#0;
        END_FOR;
        xWriteExecute := TRUE;
        iState := 20;

    20:
        IF fbWrite.xDone THEN
            xWriteExecute := FALSE;
            xReadExecute := TRUE;
            iState := 30;
        ELSIF fbWrite.xError THEN
            xWriteExecute := FALSE;
            xValid := FALSE;
            wErrorCode := WORD#3;
            iState := 50;
        END_IF;

    30:
        IF fbRead.xDone THEN
            xReadExecute := FALSE;
            iSize := UDINT_TO_INT(fbRead.szSize);
            iFrameStart := -1;

            IF iSize >= 10 THEN
                (* Match ADR + AC='1' (a reply) + "MV". The access code must be
                   '1' here, not '0' -- '0' is the code WE sent in aTx (a read
                   request); a real response comes back with AC='1'. Matching
                   '0' here would only ever re-find the outgoing request and
                   could never recognize an actual reply. *)
                FOR i := 0 TO iSize - 8 DO
                    IF (aRx[i] = aAdr[0]) AND (aRx[i + 1] = aAdr[1]) AND
                       (aRx[i + 2] = aAdr[2]) AND (aRx[i + 3] = BYTE#49) AND
                       (aRx[i + 4] = BYTE#77) AND (aRx[i + 5] = BYTE#86) THEN
                        iFrameStart := i;
                        EXIT;
                    END_IF;
                END_FOR;
            END_IF;

            IF iFrameStart < 0 THEN
                xValid := FALSE;
                wErrorCode := WORD#5;
                iState := 50;
            ELSE
                iDataLen := (BYTE_TO_INT(aRx[iFrameStart + 6]) - 48) * 10 +
                            (BYTE_TO_INT(aRx[iFrameStart + 7]) - 48);
                iDataStart := iFrameStart + 8;

                IF (iDataLen <= 0) OR ((iDataStart + iDataLen) >= iSize) THEN
                    xValid := FALSE;
                    wErrorCode := WORD#6;
                    iState := 50;
                ELSE
                    rParsed := 0.0;
                    rDiv := 10.0;
                    iSign := 1;
                    iExpSign := 1;
                    iExp := 0;
                    xFraction := FALSE;
                    xExponent := FALSE;
                    xHasDigit := FALSE;

                    FOR i := 0 TO iDataLen - 1 DO
                        j := iDataStart + i;
                        IF (aRx[j] >= BYTE#48) AND (aRx[j] <= BYTE#57) THEN
                            iDigit := BYTE_TO_INT(aRx[j]) - 48;
                            xHasDigit := TRUE;
                            IF xExponent THEN
                                iExp := iExp * 10 + iDigit;
                            ELSIF xFraction THEN
                                rParsed := rParsed + INT_TO_REAL(iDigit) / rDiv;
                                rDiv := rDiv * 10.0;
                            ELSE
                                rParsed := rParsed * 10.0 + INT_TO_REAL(iDigit);
                            END_IF;
                        ELSIF aRx[j] = BYTE#45 THEN
                            IF xExponent THEN
                                iExpSign := -1;
                            ELSE
                                iSign := -1;
                            END_IF;
                        ELSIF (aRx[j] = BYTE#46) OR (aRx[j] = BYTE#44) THEN
                            xFraction := TRUE;
                        ELSIF (aRx[j] = BYTE#69) OR (aRx[j] = BYTE#101) THEN
                            xExponent := TRUE;
                        END_IF;
                    END_FOR;

                    IF xHasDigit THEN
                        IF iExp > 38 THEN
                            iExp := 38;
                        END_IF;

                        FOR i := 1 TO iExp DO
                            IF iExpSign > 0 THEN
                                rParsed := rParsed * 10.0;
                            ELSE
                                rParsed := rParsed / 10.0;
                            END_IF;
                        END_FOR;

                        rPressureMbar := rParsed * INT_TO_REAL(iSign);
                        rPressureTorr := rPressureMbar * 0.7500637;
                        xValid := TRUE;
                        xConnected := TRUE;
                        wErrorCode := WORD#0;
                        udiCycle := udiCycle + 1;
                    ELSE
                        xValid := FALSE;
                        wErrorCode := WORD#7;
                    END_IF;

                    iState := 50;
                END_IF;
            END_IF;
        ELSIF fbRead.xError THEN
            xReadExecute := FALSE;
            xValid := FALSE;
            wErrorCode := WORD#4;
            iState := 50;
        END_IF;

    50:
        udiPollTicks := udiPollTicks + 1;
        IF udiPollTicks >= udiPollTicksMax THEN
            iState := 10;
        END_IF;

ELSE
    iState := 0;
END_CASE;

wStatusFlags := WORD#0;
IF xValid THEN
    wStatusFlags := wStatusFlags OR WORD#16#0001;
END_IF;

fbOpen(
    xExecute := xOpenExecute,
    usiListLength := 7,
    pParameterList := ADR(aParams));

fbWrite(
    xExecute := xWriteExecute,
    udiTimeOut := 100000,
    hCom := hCom,
    pBuffer := ADR(aTx),
    szSize := SIZEOF(aTx));

fbRead(
    xExecute := xReadExecute,
    udiTimeOut := 300000,
    hCom := hCom,
    pBuffer := ADR(aRx),
    szBuffer := SIZEOF(aRx));
