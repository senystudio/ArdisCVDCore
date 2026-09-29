using ArdisCVDCore.modules_hw;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace ArdisCVDCore
{
    public enum StatusLevel
    {
        Ok = 0,
        Warning = 1,
        Error = 2
    }

    public sealed class StatusLine
    {
        public string Section;
        public string Text;
        public StatusLevel Level;
        public bool ModuleLost;

        public StatusLine(string section, StatusLevel level, string text)
        {
            Section = section;
            Level = level;
            Text = text;
        }
    }

    public static class SystemStatus
    {
        public static List<StatusLine> Collect()
        {
            List<StatusLine> lines = CollectConnections();
            AddAlarmEngine(lines);
            return lines;
        }

        public static List<StatusLine> CollectConnections()
        {
            List<StatusLine> lines = new List<StatusLine>();

            AddPlc(lines);
            AddHiVac(lines);
            AddGasFlow(lines);
            AddPyrometers(lines);
            AddMicrowave(lines);
            AddCooling(lines);
            AddTurboPump(lines);

            return lines;
        }

        public static StatusLevel Worst(IEnumerable<StatusLine> lines)
        {
            StatusLevel worst = StatusLevel.Ok;
            foreach (StatusLine line in lines)
                if (line.Level > worst)
                    worst = line.Level;
            return worst;
        }

        public static StatusLevel AlarmLevel()
        {
            List<StatusLine> lines = new List<StatusLine>();
            AddAlarmEngine(lines);
            return Worst(lines);
        }

        public static bool AnyModuleLost(IEnumerable<StatusLine> lines)
        {
            foreach (StatusLine line in lines)
                if (line.ModuleLost)
                    return true;
            return false;
        }

        public static TrafficLight TowerLamp(IEnumerable<StatusLine> lines)
        {
            if (!PLC210AlarmClient.GetState().Connected)
                return TrafficLight.Yellow;

            bool moduleLost = AnyModuleLost(lines);
            switch (AlarmLevel())
            {
                case StatusLevel.Error: return moduleLost ? TrafficLight.RedYellow : TrafficLight.Red;
                case StatusLevel.Warning: return TrafficLight.Yellow;
                default: return moduleLost ? TrafficLight.GreenYellow : TrafficLight.Green;
            }
        }

        public static string Describe(StatusLevel level)
        {
            switch (level)
            {
                case StatusLevel.Error: return "Status: Error";
                case StatusLevel.Warning: return "Status: Warning";
                default: return "Status: OK";
            }
        }

        private static void AddAlarmEngine(ICollection<StatusLine> lines)
        {
            PLC210AlarmClient.State state = PLC210AlarmClient.GetState();

            if (!state.Connected)
                return;

            if (!state.ThresholdsAccepted)
            {
                lines.Add(new StatusLine("Alarm engine", StatusLevel.Warning,
                    "The PLC has not taken the thresholds yet"));
                return;
            }

            if (state.AbortActive)
            {
                lines.Add(new StatusLine("Alarm engine", StatusLevel.Error,
                    "ABORT latched, see Fault Status"));
                return;
            }

            if (state.AnyAlarm)
            {
                lines.Add(new StatusLine("Alarm engine", StatusLevel.Warning,
                    "Alarm active, see Fault Status"));
                return;
            }

            if (state.WaitingForFreshPress)
            {
                lines.Add(new StatusLine("Alarm engine", StatusLevel.Warning,
                    "Waiting to be switched on again after an abort"));
                return;
            }

            lines.Add(new StatusLine("Alarm engine", StatusLevel.Ok,
                state.RetainWasBlank ? "Armed, the PLC booted without stored thresholds" : "Armed"));
        }

        public static bool PlcConnected()
        {
            PLC210PidClient.State pid = PLC210PidClient.GetState();
            return pid.Connected && !pid.UsingLocalPreview
                && PLC210GasValveClient.GetState().Connected
                && PLC210VacuumClient.GetState().Connected
                && PLC210AlarmClient.GetState().Connected;
        }

        private static void AddPlc(ICollection<StatusLine> lines)
        {
            PLC210PidClient.State pid = PLC210PidClient.GetState();

            if (!PlcConnected())
            {
                lines.Add(new StatusLine("PLC", StatusLevel.Error, "Not connected") { ModuleLost = true });
                return;
            }

            if (!pid.PlcPressureAvailable)
            {
                lines.Add(new StatusLine("PLC", StatusLevel.Warning,
                    "No chamber pressure reading from the PLC") { ModuleLost = true });
                return;
            }

            if (!pid.PlcPressureValid)
            {
                lines.Add(new StatusLine("PLC", StatusLevel.Warning,
                    string.IsNullOrWhiteSpace(pid.PlcPressureStatusText)
                        ? "Chamber pressure reading not valid"
                        : pid.PlcPressureStatusText) { ModuleLost = true });
                return;
            }

            lines.Add(new StatusLine("PLC", StatusLevel.Ok, "Connected"));
        }

        private static void AddHiVac(ICollection<StatusLine> lines)
        {
            PLC210ThyracontClient.State state = PLC210ThyracontClient.GetState();

            if (!state.Enabled)
            {
                lines.Add(new StatusLine("Hi-Vac gauge", StatusLevel.Ok, "Disabled"));
                return;
            }

            if (!state.Connected)
            {
                lines.Add(new StatusLine("Hi-Vac gauge", StatusLevel.Error, "Not connected") { ModuleLost = true });
                return;
            }

            if (!state.HasValidValue)
            {
                lines.Add(new StatusLine("Hi-Vac gauge", StatusLevel.Warning,
                    state.PlcErrorCode != 0
                        ? "No valid reading, gauge error code " + state.PlcErrorCode.ToString(CultureInfo.InvariantCulture)
                        : "No valid reading") { ModuleLost = true });
                return;
            }

            lines.Add(new StatusLine("Hi-Vac gauge", StatusLevel.Ok, "Connected"));
        }

        private static void AddGasFlow(ICollection<StatusLine> lines)
        {
            PLC210GasFlowClient.State state = PLC210GasFlowClient.GetState();

            if (!state.Connected)
            {
                lines.Add(new StatusLine("Gas regulators", StatusLevel.Error, "Not connected") { ModuleLost = true });
                return;
            }

            if (state.AllFault)
            {
                lines.Add(new StatusLine("Gas regulators", StatusLevel.Error, "All gas channels faulted") { ModuleLost = true });
                return;
            }

            lines.Add(new StatusLine("Gas regulators", StatusLevel.Ok, "Connected"));

            for (int i = 0; i < state.Channels.Length; i++)
            {
                PLC210GasFlowClient.ChannelState channel = state.Channels[i];
                if (channel.FaultActive)
                {
                    lines.Add(new StatusLine("Gas " + channel.GasName, StatusLevel.Warning,
                        (channel.CloseConfirmed ? "Fault — closed (" : "Fault — closing… (")
                        + FaultCodeText(channel.FaultCode) + ")") { ModuleLost = true });
                }
                else if (channel.ClosedByDisable)
                {
                    lines.Add(new StatusLine("Gas " + channel.GasName, StatusLevel.Warning,
                        "Closed (subsystem disabled)"));
                }
            }
        }

        private static string FaultCodeText(int code)
        {
            switch (code)
            {
                case 0: return "no fault";
                case 1: return "write timeout";
                case 2: return "read timeout";
                case 3: return "CRC fail";
                case 4: return "device exception";
                case 5: return "unexpected length";
                case 6: return "unexpected slave address";
                case 7: return "unexpected function code";
                case 8: return "echo content mismatch";
                default: return "code " + code.ToString(CultureInfo.InvariantCulture);
            }
        }

        private static void AddPyrometers(ICollection<StatusLine> lines)
        {
            PLC210PyrometerClient.State state = PLC210PyrometerClient.GetState();

            if (!state.Connected)
            {
                lines.Add(new StatusLine("Pyrometer", StatusLevel.Error, "Not connected") { ModuleLost = true });
                return;
            }

            PLC210PyrometerClient.PyrometerReading active = MainForm.SelectActivePyrometer(state);
            if (active == null)
            {
                lines.Add(new StatusLine("Pyrometer", StatusLevel.Warning,
                    "Neither pyrometer is returning a valid reading") { ModuleLost = true });
                return;
            }

            if (active.Ch1Overload || active.Ch2Overload)
            {
                lines.Add(new StatusLine("Pyrometer", StatusLevel.Warning,
                    "Overload on " + (active.Ch1Overload && active.Ch2Overload
                        ? "Ch1 and Ch2"
                        : active.Ch1Overload ? "Ch1" : "Ch2")));
                return;
            }

            lines.Add(new StatusLine("Pyrometer", StatusLevel.Ok, "Connected"));
        }

        private static void AddMicrowave(ICollection<StatusLine> lines)
        {
            PLC210MicrowaveClient.State state = PLC210MicrowaveClient.GetState();

            if (!state.Connected)
            {
                lines.Add(new StatusLine("Microwave Power Supply", StatusLevel.Error, "Not connected"));
                return;
            }

            if (!state.GeneratorAnswering)
            {
                lines.Add(new StatusLine("Microwave Power Supply", StatusLevel.Warning, "Not connected"));
                return;
            }

            lines.Add(new StatusLine("Microwave Power Supply", StatusLevel.Ok, "Connected"));
        }

        private static void AddCooling(ICollection<StatusLine> lines)
        {
            PLC210CoolingClient.State state = PLC210CoolingClient.GetState();

            if (!state.Connected)
            {
                lines.Add(new StatusLine("Cooling System", StatusLevel.Error, "Not connected") { ModuleLost = true });
                return;
            }

            List<string> dead = new List<string>();
            for (int i = 0; i < PLC210CoolingClient.CircuitCount; i++)
            {
                if (!state.TempValid[i] && !state.FlowValid[i])
                    dead.Add(PLC210CoolingClient.CircuitNames[i]);
                else if (!state.TempValid[i])
                    dead.Add(PLC210CoolingClient.CircuitNames[i] + " temp");
                else if (!state.FlowValid[i])
                    dead.Add(PLC210CoolingClient.CircuitNames[i] + " flow");
            }

            if (!state.WaterPressureValid)
                dead.Add("water pressure");
            if (!state.CdaPressureValid)
                dead.Add("CDA pressure");

            if (dead.Count > 0)
            {
                lines.Add(new StatusLine("Cooling System", StatusLevel.Warning,
                    "No valid reading: " + string.Join(", ", dead.ToArray())) { ModuleLost = true });
                return;
            }

            lines.Add(new StatusLine("Cooling System", StatusLevel.Ok, "Connected"));
        }

        private static void AddTurboPump(ICollection<StatusLine> lines)
        {
            PLC210TurboPumpClient.State state = PLC210TurboPumpClient.GetState();

            if (!state.Connected)
            {
                lines.Add(new StatusLine("Turbo pump", StatusLevel.Error, "Not connected"));
                return;
            }

            if (!state.DriveAnswering)
            {
                lines.Add(new StatusLine("Turbo pump", StatusLevel.Warning, "Drive not answering"));
                return;
            }

            if (!state.Working)
            {
                lines.Add(new StatusLine("Turbo pump", StatusLevel.Ok, "Stopped"));
                return;
            }

            lines.Add(new StatusLine("Turbo pump", StatusLevel.Ok,
                (state.AtNormalSpeed ? "At speed, " : "Spinning up, ")
                + state.SpeedHz.ToString(CultureInfo.InvariantCulture) + " Hz, "
                + state.DriveCurrent.ToString("F2", CultureInfo.InvariantCulture) + " A, "
                + state.TemperatureC.ToString(CultureInfo.InvariantCulture) + " °C"));
        }

        public static string FaultReason(PLC210MicrowaveClient.State state)
        {
            if (state.WaterFaultLatched)
                return "no water flow";

            ushort bits = state.FaultReasonBits;
            if (state.Idle)
                bits = (ushort)(bits & 0xFDFF);
            if ((bits & 0x0200) != 0) return "no water flow";
            if ((bits & 0x0080) != 0) return "arc/fire detected";
            if ((bits & 0x0100) != 0) return "magnetron overheating";
            if ((bits & 0x0040) != 0) return "anode flow fault";
            if ((bits & 0x0020) != 0) return "magnetron anode overvoltage";
            if ((bits & 0x0002) != 0) return "reflected power protection";
            if ((bits & 0x0010) != 0) return "filament underflow";
            if ((bits & 0x0008) != 0) return "filament flow fault";
            if ((bits & 0x0004) != 0) return "communication error";
            if ((bits & 0x0001) != 0) return "generator fault";
            return "unknown cause";
        }
    }
}
