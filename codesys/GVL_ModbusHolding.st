VAR_GLOBAL
    (* Moved here from PLC_PRG's VAR so both MainTask (PLC_PRG) and the fast
       ThyracontTask (PRG_Thyracont) can reach it. Same array, same %QW0
       Modbus mapping as before. Ownership split by region, so the two tasks
       never write the same words: PLC_PRG writes 100..137 and 150..157,
       PRG_Thyracont writes only 140..147. One exception, by design:
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
       All values are signed fixed point x1000, low word first. *)
    awHolding AT %QW0 : ARRAY[0..239] OF WORD;
END_VAR
