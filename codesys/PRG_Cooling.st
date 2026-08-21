PROGRAM PRG_Cooling
VAR CONSTANT
    (* Cooling loop acquisition: six water temperatures, six water flows, plus
       water and CDA pressure, off the two МВ210-102 modules described in
       GVL_CoolingIO. Runs on MainTask -- these are slow thermal signals, there
       is nothing to gain from a fast task like the Thyracont gauge has.

       Conversion happens here rather than in the HMI, the same way chamber
       pressure (PLC_PRG) and gas flow (PRG_GasFlow) already publish Torr and
       sccm rather than raw counts: the PLC stays the single place that knows
       what a sensor's volts mean, and any future interlock can use the same
       numbers without duplicating the arithmetic.

       All six circuits carry the same 1..5 V transmitters. ArdisCVDMaster's
       ICPDAC_IO.cs had two families -- 1..5 V on Stage/Chamber/MW Head/MW
       Power and 0.5..2.5 V on Internal/External -- and that split was carried
       over here until the first live reading disproved it: with the pump off
       every one of the six flow channels sat at exactly 1.00 V, and all six
       temperature channels between 1.98 and 2.05 V on the same room-temperature
       water. A 0.5..2.5 V transmitter would rest at 0.5 V and would have made
       Internal/External read 76 degC and 10 l/min, which is precisely the
       nonsense the old constants produced on screen. Whatever the old machine
       had, this one is uniform.

       Below the transmitter's 1 V zero the reading is published as 0, not as a
       negative number -- a disconnected or unpowered transmitter sits near 0 V
       and would otherwise read as a large negative temperature.

       STILL UNCONFIRMED: the flow full scale. A zero-flow reading proves the
       electrical range but says nothing about litres per volt, so C_FLOW_SPAN
       is the old machine's 4 l/min per volt for all six. If the Internal or
       External flowmeter is a wider-range unit, that one number is what to
       change -- read it off the meter's own label.

       NOTE on a bug NOT carried over: ICPDAC_IO.cs line 445 zeroed
       TempExternalH2O in the else branch of the TempInternal test, so a dead
       internal sensor silently blanked the external one instead. *)

    (* Integer register counts per volt. The МВ210-102 publishes each channel
       as an INT of percent-of-range x10 (AIN.L 0 / AIN.H 100, Положение точки
       1), and the channels are set to 0..10 V, so 1000 counts is 10 V and one
       count is 10 mV. See GVL_CoolingIO for why the integer registers are read
       rather than the floats. *)
    C_INT_TO_VOLT : REAL := 100.0;

    C_TEMP_ZERO  : REAL := 1.0;    (* 0 degC at 1 V *)
    C_TEMP_SPAN  : REAL := 25.0;   (* degC per volt above that: 100 degC over 4 V *)

    C_FLOW_ZERO  : REAL := 1.0;    (* zero flow at 1 V *)
    C_FLOW_SPAN  : REAL := 4.0;    (* 16 l/min over 4.0 V -- see header *)

    (* Pressure transmitters, both of them: 10 V full scale = 6 bar, so volts /
       1.666 is bar. ArdisCVDMaster carried exactly this constant and had it
       commented out on the CDA channel, which is why that one used to be
       published raw; two named constants rather than one shared, so the day the
       CDA line gets a wider transmitter only its own number changes. *)
    C_WATER_BAR_PER_VOLT : REAL := 1.666;
    C_CDA_BAR_PER_VOLT   : REAL := 1.666;

    (* Cooling circuit index, in the order the HMI lists them and the order
       registers 206..229 are published in. FlowTemp1..4 on the diagram are
       assumed to be Stage / Chamber / MW Head / MW Power and FlowTemp5..6
       Internal / External -- the diagram itself only numbers them. If the
       machine is labelled differently, the two index tables below are the only
       thing to change. *)
    C_STAGE    : INT := 0;
    C_CHAMBER  : INT := 1;
    C_MWHEAD   : INT := 2;
    C_MWPOWER  : INT := 3;
    C_INTERNAL : INT := 4;
    C_EXTERNAL : INT := 5;

    (* Channel index in the combined 16-channel view built below: 0..7 are
       module 1's AI1..AI8, 8..15 are module 2's. *)
    C_MOD2_BASE : INT := 8;
    C_CDA_CH    : INT := 12;   (* module 2, AI5 *)
    C_WATER_CH  : INT := 13;   (* module 2, AI6 *)
END_VAR
VAR
    i          : INT;
    iFlowCh    : INT;
    iTempCh    : INT;
    dwValue    : DWORD;
    wTempOk    : WORD;
    wFlowOk    : WORD;

    (* Both modules unpacked into one flat view so the per-circuit code below
       reads as "which channel carries this circuit" and never has to branch on
       which module a signal happens to live on. *)
    arChanVolt : ARRAY[0..15] OF REAL;
    axChanOk   : ARRAY[0..15] OF BOOL;

    arTempC    : ARRAY[0..5] OF REAL;
    arFlowLpm  : ARRAY[0..5] OF REAL;
    rWaterBar  : REAL;
    rCdaBar    : REAL;
END_VAR

(* ---- Unpack both modules, AI1..AI8 each -------------------------------- *)
(* One register per channel, signed: WORD_TO_INT reinterprets the bit pattern
   rather than clamping, so a channel reading below its low end still arrives
   as the negative number the module sent and is caught by the clamps below. *)
FOR i := 0 TO 7 DO
    axChanOk[i] := (awCoolMod1Status[i] = WORD#16#0000);
    IF axChanOk[i] THEN
        arChanVolt[i] := INT_TO_REAL(WORD_TO_INT(awCoolMod1Value[i])) / C_INT_TO_VOLT;
    ELSE
        (* Hold nothing over from a bad reading -- the HMI blanks the field on
           the strength of the validity bit, so this value is only a fallback. *)
        arChanVolt[i] := 0.0;
    END_IF;

    axChanOk[C_MOD2_BASE + i] := (awCoolMod2Status[i] = WORD#16#0000);
    IF axChanOk[C_MOD2_BASE + i] THEN
        arChanVolt[C_MOD2_BASE + i] := INT_TO_REAL(WORD_TO_INT(awCoolMod2Value[i])) / C_INT_TO_VOLT;
    ELSE
        arChanVolt[C_MOD2_BASE + i] := 0.0;
    END_IF;
END_FOR;

(* ---- Six circuits, flow and temperature on adjacent channels ------------ *)
(* FlowTemp1..4 fill module 1 (channels 0..7), FlowTemp5..6 open module 2
   (channels 8..11); flow is always the lower channel of the pair. *)
wTempOk := WORD#0;
wFlowOk := WORD#0;
FOR i := 0 TO 5 DO
    IF i < 4 THEN
        iFlowCh := 2*i;
    ELSE
        iFlowCh := C_MOD2_BASE + 2*(i - 4);
    END_IF;
    iTempCh := iFlowCh + 1;

    IF axChanOk[iTempCh] THEN
        wTempOk := wTempOk OR SHL(WORD#1, i);
    END_IF;
    IF axChanOk[iFlowCh] THEN
        wFlowOk := wFlowOk OR SHL(WORD#1, i);
    END_IF;

    IF arChanVolt[iTempCh] > C_TEMP_ZERO THEN
        arTempC[i] := (arChanVolt[iTempCh] - C_TEMP_ZERO) * C_TEMP_SPAN;
    ELSE
        arTempC[i] := 0.0;
    END_IF;

    IF arChanVolt[iFlowCh] > C_FLOW_ZERO THEN
        arFlowLpm[i] := (arChanVolt[iFlowCh] - C_FLOW_ZERO) * C_FLOW_SPAN;
    ELSE
        arFlowLpm[i] := 0.0;
    END_IF;
END_FOR;

(* ---- CDA and water pressure, module 2 AI5 and AI6 ----------------------- *)
(* Both in bar. ArdisCVDMaster published CDA as raw volts and let the operator
   read the gauge that way; on this machine it is a pressure like any other and
   is scaled here, so nothing downstream has to remember which of the two
   pressures is the odd one out. *)
rCdaBar := arChanVolt[C_CDA_CH] / C_CDA_BAR_PER_VOLT;
IF axChanOk[C_CDA_CH] THEN
    wFlowOk := wFlowOk OR SHL(WORD#1, 6);
END_IF;

rWaterBar := arChanVolt[C_WATER_CH] / C_WATER_BAR_PER_VOLT;
IF axChanOk[C_WATER_CH] THEN
    wFlowOk := wFlowOk OR SHL(WORD#1, 7);
END_IF;

(* ---- Publish, awHolding[206..235] ----------------------------------------- *)
(* Fixed point via F_ToFixed (signed, x1000, low word first) -- the same
   convention every other block uses and PLC210CoolingClient reads back. The
   register layout is unchanged by the rewiring: this block still publishes six
   temperatures then six flows, whichever module they arrived on. *)
FOR i := 0 TO 5 DO
    dwValue := F_ToFixed(arTempC[i]);
    awHolding[206 + 2*i] := DWORD_TO_WORD(dwValue);
    awHolding[207 + 2*i] := DWORD_TO_WORD(SHR(dwValue, 16));

    dwValue := F_ToFixed(arFlowLpm[i]);
    awHolding[218 + 2*i] := DWORD_TO_WORD(dwValue);
    awHolding[219 + 2*i] := DWORD_TO_WORD(SHR(dwValue, 16));
END_FOR;

dwValue := F_ToFixed(rWaterBar);
awHolding[230] := DWORD_TO_WORD(dwValue);
awHolding[231] := DWORD_TO_WORD(SHR(dwValue, 16));

dwValue := F_ToFixed(rCdaBar);
awHolding[232] := DWORD_TO_WORD(dwValue);
awHolding[233] := DWORD_TO_WORD(SHR(dwValue, 16));

awHolding[234] := wTempOk;
awHolding[235] := wFlowOk;
