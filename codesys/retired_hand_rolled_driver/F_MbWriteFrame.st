FUNCTION F_MbWriteFrame : INT
VAR_INPUT
    bAddr  : BYTE;
    wReg   : WORD;
    wValue : WORD;
END_VAR
VAR_IN_OUT
    aFrame : ARRAY[0..7] OF BYTE;
END_VAR
VAR
    wCrc : WORD;
END_VAR

aFrame[0] := bAddr;
aFrame[1] := BYTE#16#06;
aFrame[2] := WORD_TO_BYTE(SHR(wReg, 8));
aFrame[3] := WORD_TO_BYTE(wReg AND WORD#16#00FF);
aFrame[4] := WORD_TO_BYTE(SHR(wValue, 8));
aFrame[5] := WORD_TO_BYTE(wValue AND WORD#16#00FF);
wCrc := F_ModbusCrc16(iLen := 6, aData := aFrame);
aFrame[6] := WORD_TO_BYTE(wCrc AND WORD#16#00FF);
aFrame[7] := WORD_TO_BYTE(SHR(wCrc, 8));

F_MbWriteFrame := 8;
