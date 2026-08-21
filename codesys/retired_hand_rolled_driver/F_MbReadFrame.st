FUNCTION F_MbReadFrame : INT
VAR_INPUT
    bAddr : BYTE;
    wReg  : WORD;
END_VAR
VAR_IN_OUT
    aFrame : ARRAY[0..7] OF BYTE;
END_VAR
VAR
    wCrc : WORD;
END_VAR

aFrame[0] := bAddr;
aFrame[1] := BYTE#16#03;
aFrame[2] := WORD_TO_BYTE(SHR(wReg, 8));
aFrame[3] := WORD_TO_BYTE(wReg AND WORD#16#00FF);
aFrame[4] := BYTE#0;
aFrame[5] := BYTE#1;
wCrc := F_ModbusCrc16(iLen := 6, aData := aFrame);
aFrame[6] := WORD_TO_BYTE(wCrc AND WORD#16#00FF);
aFrame[7] := WORD_TO_BYTE(SHR(wCrc, 8));

F_MbReadFrame := 8;
