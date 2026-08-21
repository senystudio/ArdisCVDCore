FUNCTION F_ModbusCrc16 : WORD
VAR_INPUT
    iLen : INT;
END_VAR
VAR_IN_OUT
    aData : ARRAY[0..7] OF BYTE;
END_VAR
VAR
    wCrc : WORD;
    i    : INT;
    j    : INT;
END_VAR

wCrc := WORD#16#FFFF;
FOR i := 0 TO iLen - 1 DO
    wCrc := wCrc XOR BYTE_TO_WORD(aData[i]);
    FOR j := 0 TO 7 DO
        IF (wCrc AND WORD#16#0001) = WORD#16#0001 THEN
            wCrc := SHR(wCrc, 1) XOR WORD#16#A001;
        ELSE
            wCrc := SHR(wCrc, 1);
        END_IF;
    END_FOR;
END_FOR;

F_ModbusCrc16 := wCrc;
