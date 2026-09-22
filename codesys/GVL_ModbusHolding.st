VAR_GLOBAL
    (* Moved here from PLC_PRG's VAR so both MainTask (PLC_PRG) and the fast
       ThyracontTask (PRG_Thyracont) can reach it. Same array, same %QW0
       Modbus mapping as before. Ownership split by region, so the two tasks
       never write the same words: PLC_PRG writes 100..137, 139 and
       150..157, PRG_Thyracont writes only 140..147.
       139 is the controller's own twelve discrete inputs as a bitmask
       (bit 0..7 = FDI1..FDI8, bit 8..11 = DI9..DI12, 1 = contact closed).
       It sits directly under the Thyracont block so the HMI's PID client
       picks it up by reading two words further than the 100..137 it already
       reads, rather than opening a ninth connection for one bit; the chamber
       lid switch is one of those bits, and which one is chosen HMI-side from
       config.ini. See GVL_PlcIO.st. One exception, by design:
       PLC_PRG also sets bit 15 of awHolding[1] every cycle -- that bit is
       not part of the HMI protocol and is used as the HMI link watchdog
       probe (the HMI clears it implicitly by rewriting the flags word);
       PRG_GasFlow only reads bit 3 of that word, so the two do not
       collide.
       64..99 is owned by PRG_GasFlow, on its own GasFlowTask (50 ms) --
       6 RRG-20 gas regulators over RS485-2, addressed via the native
       Modbus_COM master device in the tree (see GVL_GasIO for the mapped
       channel variables and PRG_GasFlow for the register layout).
       160..191 is owned by PRG_Pyrometers, on MainTask -- 2 Kelvin pyrometers
       (RXT-PRO, Smart-Spectrum). RXT-PRO is on RS485-1 / port 5 (borrowed
       from Thyracont, disabled, see PRG_Thyracont.st), native Modbus_COM
       master, slave id 8, 9600 baud. 160..167 is RXT-PRO, live. 176..183
       reserved for Smart-Spectrum, not wired yet. See GVL_PyroIO for the
       mapped channel variables and PRG_Pyrometers for the register
       layout.
       192..205 is owned by PRG_Microwave, on MainTask.
       206..239 is owned by PRG_Cooling, on MainTask -- seven water
       temperatures, seven water flows and the water/CDA pressures off two
       МВ210-102 analogue input modules. The circuits use combined
       flow+temperature sensors, so a circuit's two signals sit on adjacent
       channels of one module rather than one module per quantity; that is
       PRG_Cooling's problem, and the layout below is unaffected by it. See
       GVL_CoolingIO for the mapped channel variables:
         206..219  temperature, degC   (Stage, Chamber, MW Head, MW Power,
                                        Tuner, Internal, External -- 2 words
                                        each)
         220..233  flow, l/min         (same seven circuits, 2 words each)
         234/235   water pressure, bar
         236/237   CDA pressure, bar
         238       bitmask, temperature channel i reading good
         239       bitmask, flow channel i good; bit8 CDA, bit9 water
       The block grew by four words when the second МВ210-102 turned out to
       carry a third combined sensor -- the Tuner circuit. Everything from the
       flows down shifted, and the two pressure validity bits moved off bits
       6/7, which the seventh circuit now occupies.
       240..247 is owned by PRG_TurboPump, on MainTask -- the KYKY TD drive
       module of the turbo pump, Modbus RTU slave 16 behind a Modbus_COM
       master. See GVL_TurboPumpIO for the mapped channel variables and
       comments.txt for the rest.
         240       command from the HMI: bit0 run, bit15 command valid
         241       bit0 run latched by the PLC, bit1 a command has been sent
         242       rotation speed, Hz        (drive register 16#1000)
         243       drive voltage, 0.01 V     (16#1001)
         244       drive current, 0.01 A     (16#1002)
         245       drive status word, raw    (16#1003)
         246       motor temperature, degC   (16#1004, low byte)
         247       bit0 the drive is answering
       That block is in raw device units, not fixed point.
       248..295 belongs to PRG_Alarms, on MainTask. 248..279 is written by the
       HMI and holds the alarm thresholds; 280..295 is published by the PLC and
       holds the alarm state. Everything here is small integers in device units
       -- percent, degC, Watt -- not fixed point, because none of it needs a
       fraction and packing two per word keeps the block to one Modbus write.
       The authoritative copy of the thresholds lives in RETAIN inside
       GVL_Alarms; this block only carries them across. See comments.txt.
         248       16#A500 OR version. PRG_Alarms ignores 249..279 entirely
                   unless the high byte is 16#A5, which is what lets the RETAIN
                   copy survive a PLC boot with the HMI down: an untouched
                   block reads 0 and is not mistaken for "all alarms disabled".
                   The HMI writes 248..279 in ONE FC16 so the magic cannot
                   arrive before the values it commits.
         249       alarm-enable bits, group Parameters
         250       abort-enable bits, same bit order
                   bit0 chamber pressure, bit1..6 H2 CH4 N2 O2 Ar H2(2),
                   bit7 reflected MW power
         251..257  percent band per signal, low byte alarm, high byte abort,
                   in the bit order above: 251 chamber, 252..257 the six gases
         258       reflected MW power, alarm, Watt
         259       reflected MW power, abort, Watt
         260       temperature enables: bit0 sample alarm,
                   bit1..4 alarm  for internal, external, stage, chamber H2O,
                   bit8..11 abort for the same four
         261       sample target, degC (pyrometer, 0..1500)
         262       sample percent band, alarm, low byte
         263       water targets, degC: low byte internal, high byte external
         264       water targets, degC: low byte stage,    high byte chamber
         265..268  water percent band, low byte alarm, high byte abort,
                   in the order internal, external, stage, chamber
         269       Inputs group, check enabled, bit0..7 in screen order:
                   stage flow, chamber flow, MW head flow, MW power flow,
                   internal flow, external flow, water pressure, chamber open
         270       Inputs group, reaction, same bit order, 0 alarm, 1 abort
         271       bit0 the HMI reports a run in progress; high byte carries
                   which of the twelve discrete inputs is the chamber lid
                   switch, 1..12, because that choice lives in the HMI's
                   config.ini and PRG_Alarms cannot guess it. Out of range
                   falls back to 1.
         272..274  reserved for the gas correction factors, unused
         275..279  reserved
         280..283  active alarm mask, 64 bits, code n is bit n
         284..287  active abort mask, same numbering
         288..291  abort reason latched at the moment it tripped
         292       bit0 abort active, bit1 any alarm active,
                   bit2 thresholds accepted, bit3 RETAIN was blank at boot,
                   bit4 something is still waiting to be switched on again
         293       abort counter, increments once per latch
         294       gas valves waiting for a fresh press, bit0..7 = GPV1..8
         295       vacuum valves waiting for a fresh press, bit0..7 = VPV1..8
       The flow thresholds of the Inputs group are NOT here: they are fixed in
       PRG_Alarms at 1 lpm for stage, chamber, MW head and MW power and 5 lpm
       for internal and external, and the water pressure limits are fixed at
       0.7 and 0.5 bar. The screen states them in the caption of each check
       box, and the operator can only arm a check and pick its reaction.
       All values are signed fixed point x1000, low word first, except where
       a block says otherwise. *)
    awHolding AT %QW0 : ARRAY[0..295] OF WORD;
END_VAR
