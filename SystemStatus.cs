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

        public StatusLine(string section, StatusLevel level, string text)
        {
            Section = section;
            Level = level;
            Text = text;
        }
    }

    /// <summary>
    /// Rolls the eight Modbus clients up into one verdict for the Status plate on
    /// the main window, plus the per-section detail lines behind it.
    /// </summary>
    /// <remarks>
    /// Before the redesign each section window printed its own fault text. The
    /// sections are on one screen now and there is no room for seven status
    /// strings, so the main window shows only OK / Warning / Error and clicking
    /// it opens <see cref="StatusForm"/> with the full list.
    ///
    /// Error means the operator has lost control of something: a dead Modbus
    /// connection, or a generator that has tripped and needs RESET. Warning
    /// means the plant is still under control but a reading or a channel is not
    /// trustworthy.
    /// </remarks>
    public static class SystemStatus
    {
        public static List<StatusLine> Collect()
        {
            List<StatusLine> lines = new List<StatusLine>();

            AddPlc(lines);
            AddChamberLid(lines);
            AddHiVac(lines);
            AddGasFlow(lines);
            AddPyrometers(lines);
            AddMicrowave(lines);
            AddCooling(lines);

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

        public static string Describe(StatusLevel level)
        {
            switch (level)
            {
                case StatusLevel.Error: return "Status: Error";
                case StatusLevel.Warning: return "Status: Warning";
                default: return "Status: OK";
            }
        }

        /// <summary>
        /// The chamber lid switch, on one of the controller's own discrete
        /// inputs. First in the list: an open lid outranks anything else on it.
        /// </summary>
        /// <remarks>
        /// Error rather than Warning, because the switch is wired so that a
        /// closed lid holds its input on -- which makes "open" also what a cut
        /// wire, an unmapped channel or a PLC that has not been reflashed looks
        /// like. That is the right way round for an interlock, and all of those
        /// are things somebody has to walk over and look at.
        /// </remarks>
        private static void AddChamberLid(ICollection<StatusLine> lines)
        {
            PLC210PidClient.State state = PLC210PidClient.GetState();

            if (!state.Connected)
            {
                lines.Add(new StatusLine("Chamber lid", StatusLevel.Error, "Not connected"));
                return;
            }

            lines.Add(state.LidClosed
                ? new StatusLine("Chamber lid", StatusLevel.Ok, "Closed")
                : new StatusLine("Chamber lid", StatusLevel.Error, "OPEN"));
        }

        private static void AddPlc(ICollection<StatusLine> lines)
        {
            PLC210PidClient.State pid = PLC210PidClient.GetState();
            PLC210GasValveClient.State valves = PLC210GasValveClient.GetState();
            PLC210VacuumClient.State vacuum = PLC210VacuumClient.GetState();

            if (!pid.Connected || pid.UsingLocalPreview || !valves.Connected || !vacuum.Connected)
            {
                lines.Add(new StatusLine("PLC", StatusLevel.Error, "Not connected"));
                return;
            }

            if (!pid.PlcPressureAvailable)
            {
                lines.Add(new StatusLine("PLC", StatusLevel.Warning,
                    "No chamber pressure reading from the PLC"));
                return;
            }

            if (!pid.PlcPressureValid)
            {
                lines.Add(new StatusLine("PLC", StatusLevel.Warning,
                    string.IsNullOrWhiteSpace(pid.PlcPressureStatusText)
                        ? "Chamber pressure reading not valid"
                        : pid.PlcPressureStatusText));
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
                lines.Add(new StatusLine("Hi-Vac gauge", StatusLevel.Error, "Not connected"));
                return;
            }

            if (!state.HasValidValue)
            {
                lines.Add(new StatusLine("Hi-Vac gauge", StatusLevel.Warning,
                    state.PlcErrorCode != 0
                        ? "No valid reading, gauge error code " + state.PlcErrorCode.ToString(CultureInfo.InvariantCulture)
                        : "No valid reading"));
                return;
            }

            lines.Add(new StatusLine("Hi-Vac gauge", StatusLevel.Ok,
                string.Format(CultureInfo.InvariantCulture, "{0:G3} Torr / {1:G3} mbar",
                    state.PressureTorr, state.PressureMbar)));
        }

        private static void AddGasFlow(ICollection<StatusLine> lines)
        {
            PLC210GasFlowClient.State state = PLC210GasFlowClient.GetState();

            if (!state.Connected)
            {
                lines.Add(new StatusLine("Gas regulators", StatusLevel.Error, "Not connected"));
                return;
            }

            if (state.AllFault)
            {
                lines.Add(new StatusLine("Gas regulators", StatusLevel.Error, "All gas channels faulted"));
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
                        + FaultCodeText(channel.FaultCode) + ")"));
                }
                else if (channel.ClosedByDisable)
                {
                    lines.Add(new StatusLine("Gas " + channel.GasName, StatusLevel.Warning,
                        "Closed (subsystem disabled)"));
                }
            }
        }

        // Mirrors F_MbValidateResponse.st's fault codes -- kept in sync with that
        // file's WORD#n return values.
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
                lines.Add(new StatusLine("Pyrometers", StatusLevel.Error, "Not connected"));
                return;
            }

            PLC210PyrometerClient.PyrometerReading active = MainForm.SelectActivePyrometer(state);
            if (active == null)
            {
                lines.Add(new StatusLine("Pyrometers", StatusLevel.Warning,
                    "Neither pyrometer is returning a valid reading"));
                return;
            }

            if (active.Ch1Overload || active.Ch2Overload)
            {
                lines.Add(new StatusLine("Pyrometers", StatusLevel.Warning,
                    "Overload on " + (active.Ch1Overload && active.Ch2Overload
                        ? "Ch1 and Ch2"
                        : active.Ch1Overload ? "Ch1" : "Ch2")));
                return;
            }

            lines.Add(new StatusLine("Pyrometers", StatusLevel.Ok, "Connected"));
        }

        private static void AddMicrowave(ICollection<StatusLine> lines)
        {
            PLC210MicrowaveClient.State state = PLC210MicrowaveClient.GetState();

            // A generator that is simply switched off is not something the
            // operator has lost control of -- it is how the machine sits between
            // runs -- so it no longer turns the Status plate red. The Microwave
            // Section carries a grey "Not connected" instead, and this line stays
            // only so the detail list still says what is going on. Checked ahead
            // of FaultActive because CommError sets that flag too, and the rest
            // of the fault bits are stale coil reads once the generator is quiet.
            if (!state.Connected)
            {
                lines.Add(new StatusLine("Microwave", StatusLevel.Error, "Not connected"));
                return;
            }

            if (!state.GeneratorAnswering)
            {
                lines.Add(new StatusLine("Microwave", StatusLevel.Warning, "Not connected"));
                return;
            }

            if (state.FaultActive)
            {
                lines.Add(new StatusLine("Microwave", StatusLevel.Error,
                    "Fault — " + FaultReason(state) + ", press RESET"));
                return;
            }

            if (state.ChamberPressureLow)
            {
                lines.Add(new StatusLine("Microwave", StatusLevel.Warning,
                    "Chamber pressure too low (<9 Torr), Microwave blocked"));
                return;
            }

            lines.Add(new StatusLine("Microwave", StatusLevel.Ok, DescribeRunState(state)));
        }

        /// <summary>
        /// Cooling loop: reports channels the МВ210-102 modules cannot vouch
        /// for, and nothing else.
        /// </summary>
        /// <remarks>
        /// No threshold on the readings themselves. "How little flow is too
        /// little" and "what happens then" are policy decisions nobody has made
        /// yet, and the generator already has its own no-water interlock in
        /// hardware (see FaultReason, bit 0x0200) -- so inventing a second,
        /// softer one here would only produce nuisance warnings.
        /// </remarks>
        private static void AddCooling(ICollection<StatusLine> lines)
        {
            PLC210CoolingClient.State state = PLC210CoolingClient.GetState();

            if (!state.Connected)
            {
                lines.Add(new StatusLine("Cooling", StatusLevel.Error, "Not connected"));
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
                lines.Add(new StatusLine("Cooling", StatusLevel.Warning,
                    "No valid reading: " + string.Join(", ", dead.ToArray())));
                return;
            }

            lines.Add(new StatusLine("Cooling", StatusLevel.Ok, "Connected"));
        }

        public static string DescribeRunState(PLC210MicrowaveClient.State state)
        {
            if (state.MicrowaveOn)
                return "Microwave on";

            if (state.PreheatOn)
                return state.FilamentPreheatDone
                    ? "Preheated, ready"
                    : "Preheating filament... " + state.PreheatElapsedSeconds.ToString(CultureInfo.InvariantCulture) + "s";

            return "Idle";
        }

        /// <summary>
        /// Decodes the reason PRG_Microwave.st latched at the moment it tripped
        /// (awHolding[204]) -- not the live status bits, which may already have
        /// cleared by the time the operator looks at the screen. More than one
        /// bit can be set, so the order below is roughly most-serious first.
        /// </summary>
        public static string FaultReason(PLC210MicrowaveClient.State state)
        {
            ushort bits = state.FaultReasonBits;
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
