PROGRAM PRG_Thyracont
VAR
    fbThyracont : FB_ThyracontAscii;
    (* PLC210-11-CS Information tab, "Port ID in CODESYS": RS232-1=1,
       RS485-1=5, RS485-2=4, RS485-3=10 (PLC210-4G only). The Thyracont
       sensor is wired to RS485-1, so this must be 5, not 1 -- 1 would
       silently open RS232-1 (the system console port) instead. *)
    udiThyracontPort : UDINT := 5;
    udiThyracontAddress : UDINT := 1;
    dwValue : DWORD;
END_VAR

(* Runs on its own fast cyclic task (ThyracontTask, 50 ms) instead of the
   200 ms MainTask. FB_ThyracontAscii advances one state per call, so a full
   write->read->parse poll needs 3-4 calls: on MainTask that was 600-800 ms
   per reading, which beat against the HMI's 1 s chart timer and produced
   1-2 s staircase plateaus. At 50 ms per call the same poll completes in
   roughly 150-300 ms (serial round-trip included), so every 1 s chart tick
   sees a fresh value. *)
(* DISABLED AGAIN 2026-07-22: RS485-1 (port 5) handed back to RXT-PRO, now
   via a hand-rolled driver (PRG_Pyrometers.fbPyroRxt, FB_PyrometerModbusMaster)
   instead of the native Modbus_COM master -- xEnable := FALSE keeps
   FB_ThyracontAscii off COM.Open (see its own "IF NOT xEnable" guard) so
   the port stays free for fbPyroRxt's own COM.Open. Physically swap the
   cable back to Thyracont and flip this to TRUE only for the "is the port
   itself alive" A/B test (see PRG_Pyrometers.st header comment); otherwise
   leave it FALSE while fbPyroRxt owns the port. *)
fbThyracont(
    xEnable := FALSE,
    udiPort := udiThyracontPort,
    udiAddress := udiThyracontAddress,
    udiBaudrate := 9600,
    tPollInterval := T#0MS);

dwValue := F_ToFixed(fbThyracont.rPressureTorr);
awHolding[140] := DWORD_TO_WORD(dwValue);
awHolding[141] := DWORD_TO_WORD(SHR(dwValue, 16));
dwValue := F_ToFixed(fbThyracont.rPressureMbar);
awHolding[142] := DWORD_TO_WORD(dwValue);
awHolding[143] := DWORD_TO_WORD(SHR(dwValue, 16));
awHolding[144] := fbThyracont.wStatusFlags;
awHolding[145] := fbThyracont.wErrorCode;
awHolding[146] := DWORD_TO_WORD(fbThyracont.udiCycle);
awHolding[147] := DWORD_TO_WORD(SHR(fbThyracont.udiCycle, 16));
