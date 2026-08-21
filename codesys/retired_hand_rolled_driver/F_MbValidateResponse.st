FUNCTION F_MbValidateResponse : WORD
VAR_INPUT
    iSize        : INT;
    bExpectAddr  : BYTE;
    bExpectFunc  : BYTE;
    wExpectReg   : WORD;
    wExpectValue : WORD;
    xIsWrite     : BOOL;
END_VAR
VAR_IN_OUT
    aFrame : ARRAY[0..7] OF BYTE;
END_VAR
VAR_OUTPUT
    wReadValue : WORD;
END_VAR
VAR
    wCrcCalc   : WORD;
    wCrcRecv   : WORD;
    iExpectLen : INT;
END_VAR

wReadValue := WORD#0;

IF xIsWrite THEN
    iExpectLen := 8;
ELSE
    iExpectLen := 7;
END_IF;

IF (iSize <> iExpectLen) AND (iSize <> 5) THEN
    F_MbValidateResponse := WORD#5;
    RETURN;
END_IF;

wCrcCalc := F_ModbusCrc16(iLen := iSize - 2, aData := aFrame);
wCrcRecv := BYTE_TO_WORD(aFrame[iSize - 2]) OR SHL(BYTE_TO_WORD(aFrame[iSize - 1]), 8);
IF wCrcCalc <> wCrcRecv THEN
    F_MbValidateResponse := WORD#3;
    RETURN;
END_IF;

IF aFrame[0] <> bExpectAddr THEN
    F_MbValidateResponse := WORD#6;
    RETURN;
END_IF;

IF aFrame[1] = (bExpectFunc OR BYTE#16#80) THEN
    F_MbValidateResponse := WORD#4;
    RETURN;
END_IF;

IF aFrame[1] <> bExpectFunc THEN
    F_MbValidateResponse := WORD#7;
    RETURN;
END_IF;

IF xIsWrite THEN
    IF (aFrame[2] <> WORD_TO_BYTE(SHR(wExpectReg, 8))) OR
       (aFrame[3] <> WORD_TO_BYTE(wExpectReg AND WORD#16#00FF)) OR
       (aFrame[4] <> WORD_TO_BYTE(SHR(wExpectValue, 8))) OR
       (aFrame[5] <> WORD_TO_BYTE(wExpectValue AND WORD#16#00FF)) THEN
        F_MbValidateResponse := WORD#8;
        RETURN;
    END_IF;
ELSE
    wReadValue := SHL(BYTE_TO_WORD(aFrame[3]), 8) OR BYTE_TO_WORD(aFrame[4]);
END_IF;

F_MbValidateResponse := WORD#0;
