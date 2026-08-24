PROGRAM PRG_Cooling
VAR CONSTANT
    (* Cooling loop acquisition: seven water temperatures, seven water flows,
       plus water and CDA pressure, off the two МВ210-102 modules described in
       GVL_CoolingIO. Runs on MainTask -- these are slow thermal signals, there
       is nothing to gain from a fast task like the Thyracont gauge has.

       Conversion happens here rather than in the HMI, the same way chamber
       pressure (PLC_PRG) and gas flow (PRG_GasFlow) already publish Torr and
       sccm rather than raw counts: the PLC stays the single place that knows
       what a sensor's volts mean, and any future interlock can use the same
       numbers without duplicating the arithmetic.

       All circuits carry the same 1..5 V transmitters. ArdisCVDMaster's
       ICPDAC_IO.cs had two families -- 1..5 V on Stage/Chamber/MW Head/MW
       Power and 0.5..2.5 V on Internal/External -- and that split was carried
       over here until the first live reading disproved it: with the pump off
       every one of the flow channels sat at exactly 1.00 V, and every
       temperature channel between 1.98 and 2.05 V on the same room-temperature
       water. A 0.5..2.5 V transmitter would rest at 0.5 V and would have made
       Internal/External read 76 degC and 10 l/min, which is precisely the
       nonsense the old constants produced on screen. Whatever the old machine
       had, this one is uniform.

       Below the transmitter's 1 V zero the reading is published as 0, not as a
       negative number -- a disconnected or unpowered transmitter sits near 0 V
       and would otherwise read as a large negative temperature.

       STILL UNCONFIRMED: the flow full scale. A zero-flow reading proves the
       electrical range but says nothing about litres per volt, so C_FLOW_SPAN
       is the old machine's 4 l/min per volt for all seven. If the Internal,
       External or Tuner flowmeter is a wider-range unit, that one number is
       what to change -- read it off the meter's own label. The Tuner circuit
       has never been read live at all: it was wired to module 2 after
       commissioning, so its 1..5 V family is assumed from the other six rather
       than measured.

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

    (* Two DIFFERENT transmitters, which is the trap on this pair.

       Water line: 10 V full scale = 6 bar, so volts / 1.666 is bar. Confirmed
       against the machine -- 2.83 V on AI7 gives the 1.7..1.8 bar the loop
       actually runs at.

       CDA line: 1 bar per volt, i.e. a 0..10 bar transmitter, NOT the 6 bar
       one. Seen live at 7.00 V on AI8 against a shop-air line the operator
       expects at about 7 bar; a 6 bar unit would have pegged at 10 V long
       before that. This is also why ArdisCVDMaster published the CDA channel
       raw and had its "/= 1.666" commented out (ICPDAC_IO.cs:517) while
       scaling the water channel on the very next line: on a 1 bar/V
       transmitter the unscaled volts already read as bar, so the old display
       was accidentally right and nobody chased it. Reading it as 6 bar/10 V,
       which this file did at first, showed 4.2 instead of 7.0.

       ASSUMED, worth a look at the transmitter label: exactly 0..10 bar. The
       7.00 V reading fixes the ratio at roughly 1 bar/V but cannot tell a
       10 bar unit from, say, a 10.5 bar one. *)
    C_WATER_BAR_PER_VOLT : REAL := 1.666;
    C_CDA_BAR_PER_VOLT   : REAL := 1.0;

    (* Cooling circuit index, in the order the HMI lists them and the order
       registers 206..233 are published in. *)
    C_STAGE    : INT := 0;
    C_CHAMBER  : INT := 1;
    C_MWHEAD   : INT := 2;
    C_MWPOWER  : INT := 3;
    C_TUNER    : INT := 4;
    C_INTERNAL : INT := 5;
    C_EXTERNAL : INT := 6;

    C_CIRCUIT_MAX : INT := 6;      (* last circuit index, i.e. seven circuits *)

    (* Channel index in the combined 16-channel view built below: 0..7 are
       module 1's AI1..AI8, 8..15 are module 2's. *)
    C_MOD2_BASE : INT := 8;
    C_WATER_CH  : INT := 14;   (* module 2, AI7 *)
    C_CDA_CH    : INT := 15;   (* module 2, AI8 *)

    (* Flow channel of each circuit, in HMI/register order; the circuit's
       temperature is always the next channel up, because the sensors are
       combined flow+temperature units wired to adjacent inputs.

       This is a table rather than the arithmetic it used to be, because the
       HMI order and the wiring order are no longer the same thing. Module 2
       carries three combined sensors, and the third of them -- Tuner -- sits
       above Internal and External electrically while the HMI wants it listed
       directly under MW Power:

         circuit            module / input      flat channel
         0  Stage           U8  AI1 / AI2        0 / 1
         1  Chamber         U8  AI3 / AI4        2 / 3
         2  MW Head         U8  AI5 / AI6        4 / 5
         3  MW Power        U8  AI7 / AI8        6 / 7
         4  Tuner           U9  AI5 / AI6       12 / 13
         5  Internal        U9  AI1 / AI2        8 / 9
         6  External        U9  AI3 / AI4       10 / 11

       Which physical sensor is which named circuit is still taken from the
       wiring diagram's numbering rather than from a label on the machine --
       this table is the one place to change it if the machine disagrees. *)
    C_FLOW_CH : ARRAY[0..6] OF INT := [0, 2, 4, 6, 12, 8, 10];

    (* Bit positions in awHolding[239]. Bits 0..6 are the seven circuits'
       flows, so the two pressures -- which sat on bits 6 and 7 while there
       were only six circuits -- moved up out of the way. *)
    C_CDA_BIT   : INT := 8;
    C_WATER_BIT : INT := 9;
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

    arTempC    : ARRAY[0..6] OF REAL;
    arFlowLpm  : ARRAY[0..6] OF REAL;
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

(* ---- Seven circuits, flow and temperature on adjacent channels ---------- *)
wTempOk := WORD#0;
wFlowOk := WORD#0;
FOR i := 0 TO C_CIRCUIT_MAX DO
    iFlowCh := C_FLOW_CH[i];
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

(* ---- Water and CDA pressure, module 2 AI7 and AI8 ----------------------- *)
(* Both in bar, but on their own scales -- see the constants above; the CDA
   line is a 0..10 bar transmitter and the water line a 0..6 bar one, and
   reading both as 6 bar is what made this pair look swapped on screen.
   ArdisCVDMaster published CDA raw in volts and let the operator read the
   gauge that way; here it is a pressure like any other and is scaled, so
   nothing downstream has to remember which of the two is the odd one out. *)
rCdaBar := arChanVolt[C_CDA_CH] / C_CDA_BAR_PER_VOLT;
IF axChanOk[C_CDA_CH] THEN
    wFlowOk := wFlowOk OR SHL(WORD#1, C_CDA_BIT);
END_IF;

rWaterBar := arChanVolt[C_WATER_CH] / C_WATER_BAR_PER_VOLT;
IF axChanOk[C_WATER_CH] THEN
    wFlowOk := wFlowOk OR SHL(WORD#1, C_WATER_BIT);
END_IF;

(* ---- Publish, awHolding[206..239] ----------------------------------------- *)
(* Fixed point via F_ToFixed (signed, x1000, low word first) -- the same
   convention every other block uses and PLC210CoolingClient reads back. Seven
   temperatures then seven flows, whichever module they arrived on. The seventh
   circuit pushed the flow block from 218 to 220 and everything after it by four
   words, so PLC210CoolingClient's offsets moved with it -- the two must be
   flashed and rebuilt together. *)
FOR i := 0 TO C_CIRCUIT_MAX DO
    dwValue := F_ToFixed(arTempC[i]);
    awHolding[206 + 2*i] := DWORD_TO_WORD(dwValue);
    awHolding[207 + 2*i] := DWORD_TO_WORD(SHR(dwValue, 16));

    dwValue := F_ToFixed(arFlowLpm[i]);
    awHolding[220 + 2*i] := DWORD_TO_WORD(dwValue);
    awHolding[221 + 2*i] := DWORD_TO_WORD(SHR(dwValue, 16));
END_FOR;

dwValue := F_ToFixed(rWaterBar);
awHolding[234] := DWORD_TO_WORD(dwValue);
awHolding[235] := DWORD_TO_WORD(SHR(dwValue, 16));

dwValue := F_ToFixed(rCdaBar);
awHolding[236] := DWORD_TO_WORD(dwValue);
awHolding[237] := DWORD_TO_WORD(SHR(dwValue, 16));

awHolding[238] := wTempOk;
awHolding[239] := wFlowOk;
