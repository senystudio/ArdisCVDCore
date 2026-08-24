VAR_GLOBAL
    (* Cooling loop analogue inputs: two МВ210-102 (8 fast AI each, Ethernet
       Modbus TCP), U8 and U9 on the wiring diagram. Module 1 (U8) is at
       192.168.1.13:502 and module 2 (U9) at 192.168.1.11:502; set both in the
       device tree -- nothing in this project hard-codes them. This is the same
       arrangement as the existing MV210 that carries chamber pressure on AI1
       (see PLC_PRG.st) and the MU210-402 traffic light; that MV210 is a third,
       separate module and is not touched here.

       Channel layout taken from the wiring diagram, NOT from the old ICP DAS
       card. The seven cooling circuits use combined flow+temperature sensors
       (FlowTemp1..7 on the diagram, four wires each: +24 V, flow signal,
       temperature signal, GND), so flow and temperature of one circuit sit on
       two ADJACENT channels of the SAME module -- they are not split one
       module per quantity the way the i8017HW used to have them. Between the
       two modules that is 14 of the 16 channels; the two pressure
       transmitters, one wire each, take the last two:

         Module 1 -- U8, connector J7      Module 2 -- U9, connector J8
           AI1  Flow1     (pins 1/3)         AI1  Flow5      (pins 1/3)
           AI2  Temp1     (pins 4/6)         AI2  Temp5      (pins 4/6)
           AI3  Flow2     (pins 7/9)         AI3  Flow6      (pins 7/9)
           AI4  Temp2     (pins 10/12)       AI4  Temp6      (pins 10/12)
           AI5  Flow3     (pins 13/15)       AI5  Flow7      (pins 13/15)
           AI6  Temp3     (pins 16/18)       AI6  Temp7      (pins 16/18)
           AI7  Flow4     (pins 19/21)       AI7  Water pres.(pins 19/21)
           AI8  Temp4     (pins 22/24)       AI8  CDA pres.  (pins 22/24)

       Module 2 used to carry two combined sensors with CDA and water pressure
       on AI5/AI6 and AI7/AI8 spare. It actually has three, so the pressures
       moved down to AI7/AI8 and the module is now full. That is what took the
       machine from six cooling circuits to seven; the added one is Tuner.

       Water first, CDA second -- confirmed live, not read off the diagram.
       Wired the other way round the HMI showed 1.7 bar on the air line and
       4.2 bar on the water, when the loop actually runs at 1.7..1.8 bar and
       the air line at about 7. Note also that these two are NOT the same
       transmitter: 0..6 bar on the water, 0..10 bar on the CDA. See
       PRG_Cooling's C_WATER_BAR_PER_VOLT / C_CDA_BAR_PER_VOLT.

       Confirmed live on module 1 at commissioning: with the pump off the four
       flow channels all sat at exactly 1.00 V (their zero) and the four
       temperature channels at 1.98..2.03 V, i.e. 24.5..25.8 degC -- room
       temperature water. The interleaving above is real, not just drawn.
       Module 2's AI5/AI6, the Tuner sensor, have not been read live.

       Each sensor goes to that channel's U terminal (AIn-U) with its return on
       AIn-G; the current terminal AIn-I is unused on every channel. Circuit
       numbering FlowTemp1..7 is mapped to the seven named circuits in
       PRG_Cooling (C_STAGE..C_EXTERNAL) by the C_FLOW_CH table -- that is the
       one place to change if the sensors turn out to be numbered in a
       different order on the machine. Note that the table is not in numeric
       order: FlowTemp7 is Tuner, which the HMI lists directly under MW Power
       rather than after External.

       Waveguide/Rod, which the old machine had as a seventh circuit, is not on
       this diagram at all: no sensor, no channel, and its row is hidden in the
       HMI. The seventh circuit here is Tuner, which is a different thing.

       ---- What is read, and why the integer registers ----------------------

       The МВ210-102 publishes every channel three ways. The float area is the
       obvious choice and is NOT what this reads, for a concrete reason found
       the hard way during commissioning: the floats are 3 registers apart, not
       2 --

         AI1 4000 (16#0FA0)   AI3 4006 (16#0FA6)   AI5 4012 (16#0FAC)   AI7 4018 (16#0FB2)
         AI2 4003 (16#0FA3)   AI4 4009 (16#0FA9)   AI6 4015 (16#0FAF)   AI8 4021 (16#0FB5)

       -- and the register in the gap after each pair is not mapped. Any read
       that touches one comes back ILLEGAL DATA ADDRESS, so the floats can only
       be fetched one channel per Modbus channel: 8 channels per module instead
       of 1, plus 16 more GVL variables, for no gain here.

       The integer area is 8 consecutive registers, one INT per channel, so a
       single read brings back the whole module:

         Целочисленные значения  4064..4071  16#0FE0..16#0FE7  (INT, 1 reg each)
         Статусы каналов         4072..4079  16#0FE8..16#0FEF  (1 reg each, 0 = good)

       Cost of the integer form is quantisation at 0.1 % of range = 10 mV,
       which is 0.25 degC on the 1..5 V circuits, 0.5 degC on the 0.5..2.5 V
       ones and 0.04..0.2 l/min on the flows -- below what these sensors are
       worth arguing about on a water loop.

       ---- Module settings this code depends on ------------------------------

       Every channel: "Датчик 0…10В", Сдвиг 0, Наклон 1, AIN.L 0, AIN.H 100,
       Положение точки 1. That combination is what makes the integer register
       read as percent-of-range x10, hence volts = INT / 100 in PRG_Cooling.
       Положение точки is the one module parameter the scaling actually hangs
       on: change it and every cooling reading moves by a decade with nothing
       in the code to notice. Module 2's AI5 and AI6 were spare and must be set
       up the same way as the rest now that they carry the Tuner sensor.

       0..10 V is not a choice: the МВ210-102 sensor-type list offers only
       0…1 В and 0…10 В for voltage, and the transmitters here swing to 5 V, so
       a 1..5 V transmitter only uses 40 % of the span and a 0.5..2.5 V one
       20 %. That costs nothing worth chasing -- the reading is limited by the
       sensor and by noise on the cable, not by the unused part of the range.

       Сдвиг/Наклон could pre-scale the reading into degC or l/min (every
       conversion in PRG_Cooling is linear). They are deliberately left at 0/1
       -- putting the numbers there scatters them across sixteen dialogs of a
       configurator that is neither next to the code nor in version control, it
       cannot express the "below the low end, publish 0" clamp, and it would
       leave PRG_Cooling silently wrong the day a module is swapped for one
       with default settings. All scaling stays in PRG_Cooling, in one place.

       ---- Modbus channels to configure --------------------------------------

       Two channels per module, Read Input Registers (FC04 -- these are input
       registers, not holding), Cyclic at 1000 ms, Error handling "Keep last
       value". Both slaves get the identical pair, and now every channel that
       comes back is used.

         slave     channel          FC  READ offset  length  maps to
         MB210_2   CoolModNValue    04  16#0FE0      8       awCoolMod1Value
         MB210_2   CoolModNStatus   04  16#0FE8      8       awCoolMod1Status
         MB210_3   CoolModNValue    04  16#0FE0      8       awCoolMod2Value
         MB210_3   CoolModNStatus   04  16#0FE8      8       awCoolMod2Status

       Unit-ID must be 1 on both slaves, not the CODESYS default of 255.

       KNOWN GAP, deliberately deferred: PRG_Cooling trusts the status word
       alone, and neither Error handling option can express "the module is
       gone" -- "Set to zero" writes 0, which this code reads as healthy, and
       "Keep last value" freezes the last healthy status. Distinguishing a lost
       module needs the slave's own diagnostic from the ModbusTCPSlave IEC
       Objects tab ANDed into the validity bits. Hence "Keep last value" for
       now: frozen real readings beat fabricated zeroes. *)

    awCoolMod1Value  : ARRAY[0..7] OF WORD;
    awCoolMod1Status : ARRAY[0..7] OF WORD;

    awCoolMod2Value  : ARRAY[0..7] OF WORD;
    awCoolMod2Status : ARRAY[0..7] OF WORD;
END_VAR
