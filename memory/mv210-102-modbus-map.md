---
name: mv210-102-modbus-map
description: "МВ210-102 Modbus register map and the three traps that cost an hour of commissioning on 18.08.2026"
metadata:
  type: reference
---

Register map from the [official manual](https://docs.owen.ru/product/mv210-102/doc/rukovodstvo-po-ekspluatacii-mv210-102). Read with FC03 or FC04; write with FC16.

- **Float values are 3 registers apart, not 2**: AI1 4000, AI2 4003, AI3 4006, AI4 4009, AI5 4012, AI6 4015, AI7 4018, AI8 4021 (16#0FA0, 0FA3, 0FA6, 0FA9, 0FAC, 0FAF, 0FB2, 0FB5). The register in the gap after each 2-register float is **not mapped** — any read touching it returns ILLEGAL DATA ADDRESS. So floats can only be read one channel per Modbus channel.
- **Integer values are 8 consecutive registers**: 4064..4071 (16#0FE0..0FE7), one INT per channel. One read gets the whole module — this is what ArdisCVDCore uses.
- **Channel statuses**: 4072..4079 (16#0FE8..0FEF), 1 register each, 0 = good.

Three traps, in the order they bit:

1. **Unit-ID.** CODESYS defaults a ModbusTCPSlave to 255; the module answers only on **1**. Wrong id looks like a healthy TCP connection with 100 % of requests failing.
2. **The stride-3 gap above.** Symptom is identical (ILLEGAL DATA ADDRESS), which is what makes the two easy to confuse.
3. **CODESYS I/O mapping is easy to cross over**: the Value channel is ARRAY[0..n] of one length and Status another, and swapping the two variables gives "The types of channel and the mapped variable do not match" on only one of the two rows.

Scaling depends on module settings, not just on the code: with «Датчик 0…10В», Сдвиг 0, Наклон 1, AIN.L 0, AIN.H 100 and **Положение точки 1**, the integer register is percent-of-range x10, so volts = INT / 100. Положение точки is the parameter that silently breaks this by a decade.

The two cooling modules sit at **192.168.1.13** (U8) and **192.168.1.11** (U9) — not .14, which was a guess that turned out wrong. Owen Configurator lists both by serial number.

See [[ardis-cvdcore-open-work]].
