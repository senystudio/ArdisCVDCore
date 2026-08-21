VAR_GLOBAL
    (* Mapped in device tree I/O mapping to Read Input Registers channels
       on PYRO_RXT/PYRO_SMART. Registers: 0x0000 ADC status, 0x0005 device
       status, 0x0008/0x000A/0x000C Ch1/Ch2/ratio temp (float, low word
       first). *)
    awPyroRxtCh1Raw     : ARRAY[0..1] OF WORD;
    awPyroRxtCh2Raw     : ARRAY[0..1] OF WORD;
    awPyroRxtRatioRaw   : ARRAY[0..1] OF WORD;
    wPyroRxtAdcStatus   : WORD;
    wPyroRxtDevStatus   : WORD;

    awPyroSmartCh1Raw   : ARRAY[0..1] OF WORD;
    awPyroSmartCh2Raw   : ARRAY[0..1] OF WORD;
    awPyroSmartRatioRaw : ARRAY[0..1] OF WORD;
    wPyroSmartAdcStatus : WORD;
    wPyroSmartDevStatus : WORD;
END_VAR
