using System.Collections.Generic;

namespace ArdisCVDCore
{
    public sealed class AlarmEntry
    {
        public int Code;
        public string Section;
        public string Description;
        public string AlarmRecommendation;
        public string AbortRecommendation;

        public AlarmEntry(int code, string section, string description,
            string alarmRecommendation, string abortRecommendation)
        {
            Code = code;
            Section = section;
            Description = description;
            AlarmRecommendation = alarmRecommendation;
            AbortRecommendation = abortRecommendation;
        }
    }

    public static class AlarmCatalog
    {
        public const int CodeCount = 64;

        public const int ChamberPressure = 0;
        public const int GasFirst = 1;
        public const int ReflectedPower = 7;
        public const int SampleTemperature = 8;
        public const int WaterTempFirst = 9;
        public const int FlowFirst = 16;
        public const int WaterPressure = 22;
        public const int CdaPressure = 23;
        public const int ChamberOpen = 24;
        public const int ChamberOverPressure = 25;
        public const int MicrowaveFirst = 32;
        public const int TurboFirst = 48;

        private static readonly Dictionary<int, AlarmEntry> Entries = Build();

        public static AlarmEntry Get(int code)
        {
            AlarmEntry entry;
            return Entries.TryGetValue(code, out entry) ? entry : null;
        }

        public static string Describe(int code)
        {
            AlarmEntry entry = Get(code);
            return entry != null ? entry.Description : "Unassigned alarm code " + code;
        }

        private static Dictionary<int, AlarmEntry> Build()
        {
            Dictionary<int, AlarmEntry> map = new Dictionary<int, AlarmEntry>();

            Add(map, ChamberPressure, "Chamber", "Chamber pressure is outside the alarm window",
                "Check the throttle valve and the pumping speed",
                "Microwave and gas stopped. Match the pressure setpoint to the actual pumping");

            AddGas(map, GasFirst + 0, "H2");
            AddGas(map, GasFirst + 1, "CH4");
            AddGas(map, GasFirst + 2, "N2");
            AddGas(map, GasFirst + 3, "O2");
            AddGas(map, GasFirst + 4, "Ar");
            AddGas(map, GasFirst + 5, "H2 (2)");

            Add(map, ReflectedPower, "Microwave", "Reflected power reached its limit",
                "Retune the applicator, check the plasma",
                "Microwave stopped. Retune before restarting");

            Add(map, SampleTemperature, "Pyrometer", "Sample temperature is outside the alarm window",
                string.Empty, null);

            AddWaterTemp(map, WaterTempFirst + 0, "Internal");
            AddWaterTemp(map, WaterTempFirst + 1, "External");
            AddWaterTemp(map, WaterTempFirst + 2, "Stage");
            AddWaterTemp(map, WaterTempFirst + 3, "Chamber");

            AddFlow(map, FlowFirst + 0, "Stage", "1");
            AddFlow(map, FlowFirst + 1, "Chamber", "1");
            AddFlow(map, FlowFirst + 2, "MW head", "1");
            AddFlow(map, FlowFirst + 3, "MW power", "1");
            AddFlow(map, FlowFirst + 4, "Internal", "5");
            AddFlow(map, FlowFirst + 5, "External", "5");

            Add(map, WaterPressure, "Cooling", "Cooling water pressure is low",
                "Below 0.7 bar. Check the supply pressure and the filters",
                "Below 0.5 bar. Everything stopped, restore the water supply");

            Add(map, CdaPressure, "Cooling", "Compressed air pressure is low",
                "Below 5 bar. Check the compressor",
                "Below 4 bar. The pneumatic valves can no longer be trusted");

            Add(map, ChamberOpen, "Chamber", "The chamber lid is open",
                "Close the lid before starting a process",
                "Everything stopped. Close the lid");

            Add(map, ChamberOverPressure, "Chamber", "Chamber pressure is above 800 Torr",
                "Every gas setpoint has been forced to zero. Check the venting", null);

            AddMicrowave(map, MicrowaveFirst + 0, "no cooling water at the generator",
                "Start the water pump, then press RESET on the generator");
            AddMicrowave(map, MicrowaveFirst + 1, "arc or fire detected",
                "Inspect the applicator and the waveguide before restarting");
            AddMicrowave(map, MicrowaveFirst + 2, "the magnetron is overheating",
                "Let it cool down and check the MW head cooling circuit");
            AddMicrowave(map, MicrowaveFirst + 3, "anode flow fault",
                "Check the anode cooling circuit");
            AddMicrowave(map, MicrowaveFirst + 4, "magnetron malfunction",
                "The generator reports an internal magnetron fault, call service");
            AddMicrowave(map, MicrowaveFirst + 5, "reflected power protection tripped",
                "Retune the applicator before restarting");
            AddMicrowave(map, MicrowaveFirst + 6, "filament underflow",
                "The generator reports a filament supply fault, call service");
            AddMicrowave(map, MicrowaveFirst + 7, "filament flow fault",
                "The generator reports a filament supply fault, call service");

            Add(map, MicrowaveFirst + 8, "Microwave", "The generator is not answering",
                "Check the RS-485 link and that the generator is powered", null);

            AddMicrowave(map, MicrowaveFirst + 9, "generator fault",
                "The generator reports a general fault, read its own indicator");

            AddTurbo(map, TurboFirst + 0, "the turbo pump failed to start",
                "Check the backing pressure and the backing valve");
            AddTurbo(map, TurboFirst + 1, "the turbo pump drive is overloaded",
                "The rotor is working against a gas load, check the backing line");
            AddTurbo(map, TurboFirst + 2, "turbo pump drive overcurrent",
                "Stop the pump and look for a mechanical problem");
            AddTurbo(map, TurboFirst + 3, "the turbo pump is overheating",
                "Check the MW power cooling circuit and the backing pressure");
            AddTurbo(map, TurboFirst + 4, "turbo pump drive timeout",
                "The drive stopped answering the PLC, check the RS-485 link");
            AddTurbo(map, TurboFirst + 5, "turbo pump watchdog fault",
                "The drive reports its own watchdog, power cycle it");

            return map;
        }

        private static void Add(Dictionary<int, AlarmEntry> map, int code, string section,
            string description, string alarmRecommendation, string abortRecommendation)
        {
            map[code] = new AlarmEntry(code, section, description,
                alarmRecommendation,
                abortRecommendation ?? alarmRecommendation);
        }

        private static void AddGas(Dictionary<int, AlarmEntry> map, int code, string gas)
        {
            Add(map, code, "Gas " + gas,
                gas + " flow is outside the alarm window",
                "Check the regulator and the line pressure",
                "Everything stopped. The regulator is not holding its setpoint");
        }

        private static void AddWaterTemp(Dictionary<int, AlarmEntry> map, int code, string circuit)
        {
            Add(map, code, "Cooling",
                circuit + " water temperature is outside the alarm window",
                "Check the flow through the " + circuit.ToLowerInvariant() + " circuit",
                "Everything stopped. Restore cooling before restarting");
        }

        private static void AddFlow(Dictionary<int, AlarmEntry> map, int code, string circuit, string limit)
        {
            Add(map, code, "Cooling",
                "No flow in the " + circuit + " circuit",
                "Below " + limit + " l/min. Check the valve, the filter and the pump",
                "Below " + limit + " l/min. Everything stopped, restore the flow");
        }

        private static void AddMicrowave(Dictionary<int, AlarmEntry> map, int code,
            string reason, string recommendation)
        {
            Add(map, code, "Microwave",
                "Generator fault: " + reason,
                recommendation,
                "Everything stopped. " + recommendation);
        }

        private static void AddTurbo(Dictionary<int, AlarmEntry> map, int code,
            string reason, string recommendation)
        {
            Add(map, code, "Turbo pump",
                char.ToUpperInvariant(reason[0]) + reason.Substring(1),
                recommendation, recommendation);
        }
    }
}
