using NModbus;
using System;
using System.Net.Sockets;
using System.Threading;

namespace ArdisCVDCore.modules_hw
{
    /// <summary>
    /// Modbus TCP exchange for the PLC210 PID task.
    /// The HMI sends setpoints/parameters and receives calculated diagnostics;
    /// the PLC project decides which physical outputs are mapped.
    /// </summary>
    /// <summary>
    /// Which lamp of the panel signal tower is lit. One at a time -- the three
    /// relays are independent, but the tower is read at a glance and two lamps
    /// at once say nothing useful.
    /// </summary>
    public enum TrafficLight
    {
        Off = 0,
        Green,
        Yellow,
        Red
    }

    public static class PLC210PidClient
    {
        public sealed class Channel
        {
            public bool Enabled = true;
            public double Setpoint;
            public double Measured;
            public double Kp;
            public double Ki;
            public double Kd;
            public double LowerLimit;
            public double UpperLimit;
            public bool DirectMode;
            public double DirectValue;
            public double FlowCorrection;
            public double PressureCorrection;
            public double TotalGasFlow;

            public Channel Clone()
            {
                return (Channel)MemberwiseClone();
            }
        }

        public sealed class Result
        {
            public double Error;
            public double P;
            public double I;
            public double D;
            public double Output;

            public Result Clone()
            {
                return (Result)MemberwiseClone();
            }
        }

        public sealed class State
        {
            public bool Connected;
            public bool UsingLocalPreview;
            public string StatusText;
            public DateTime UpdatedAt;
            public Result Chamber = new Result();
            public Result Plenum = new Result();
            public bool PlcPressureAvailable;
            public bool PlcPressureValid;
            public double PlcPressureTorr;
            public string PlcPressureSource;
            public string PlcPressureStatusText;

            public State Clone()
            {
                return new State
                {
                    Connected = Connected,
                    UsingLocalPreview = UsingLocalPreview,
                    StatusText = StatusText,
                    UpdatedAt = UpdatedAt,
                    Chamber = Chamber.Clone(),
                    Plenum = Plenum.Clone(),
                    PlcPressureAvailable = PlcPressureAvailable,
                    PlcPressureValid = PlcPressureValid,
                    PlcPressureTorr = PlcPressureTorr,
                    PlcPressureSource = PlcPressureSource,
                    PlcPressureStatusText = PlcPressureStatusText
                };
            }
        }

        private sealed class PreviewController
        {
            // Moving-window integral, offline-preview-only (see the note on Step's
            // "output" below for why this doesn't try to track the PLC exactly):
            // I sums only the last IntegralWindowSize error samples instead of
            // accumulating forever, so old samples age out and the integral
            // self-limits without a separate anti-windup clamp. The real
            // FB_IncrementalPid.st instead accumulates rIntegral without a
            // window, clamped to +-1,000,000.
            private const int IntegralWindowSize = 20;

            private readonly double[] _errorHistory = new double[IntegralWindowSize];
            private double _integral;
            private double _previousError;
            private double _timeSinceDChange;
            private double _d;
            private bool _initialized;

            public Result Step(Channel channel, double dt)
            {
                double error = channel.Measured - channel.Setpoint;

                if (!_initialized)
                {
                    _previousError = error;
                    Array.Clear(_errorHistory, 0, _errorHistory.Length);
                    _timeSinceDChange = 0;
                    _d = 0;
                    _initialized = true;
                }

                Array.Copy(_errorHistory, 0, _errorHistory, 1, _errorHistory.Length - 1);
                _errorHistory[0] = error;

                double errorSum = 0;
                for (int index = 0; index < _errorHistory.Length; index++)
                    errorSum += _errorHistory[index];
                _integral = errorSum * dt;

                double p = channel.Kp * error;
                double i = channel.Ki * _integral;

                // Only recompute D -- against the real elapsed time -- when the
                // error actually changed. Setpoint/Measured only change once a
                // second (pushed by SuperCycle) while this worker loop polls
                // roughly every 200 ms, so recomputing every tick against an
                // unchanged error (divided by a too-short dt) produced a
                // spike-then-zero flicker; hold the last D value in between.
                _timeSinceDChange += dt;
                if (error != _previousError)
                {
                    _d = _timeSinceDChange > 0.001
                        ? channel.Kd * (error - _previousError) / _timeSinceDChange
                        : 0;
                    _previousError = error;
                    _timeSinceDChange = 0;
                }

                double d = _d;

                // Positional form (output = P+I+D of the current error) -- this is
                // a deliberate, SAFE approximation for the offline preview only.
                // The real FB_IncrementalPid.st on the PLC accumulates the full
                // P+I+D onto its running output every scan (matches the original
                // ArdisCVDMaster ChamberPIDCalc: CalcResult += P_Calc + ...), which
                // is fine there because it's driven by real closed-loop feedback.
                // Doing the same here, with no real feedback while disconnected,
                // would let the preview drift away indefinitely, so it stays
                // positional on purpose -- this does NOT need to track the PLC.
                double output = channel.DirectMode
                    ? channel.DirectValue
                    : Clamp(p + i + d, channel.LowerLimit, channel.UpperLimit);

                return new Result
                {
                    Error = error,
                    P = p,
                    I = i,
                    D = d,
                    Output = output
                };
            }

            public void Reset()
            {
                _integral = 0;
                _previousError = 0;
                _timeSinceDChange = 0;
                _d = 0;
                _initialized = false;
                Array.Clear(_errorHistory, 0, _errorHistory.Length);
            }
        }

        private const byte UnitId = 1;
        private const ushort InputRegisterStart = 0;
        private const ushort InputRegisterCount = 64;
        private const ushort OutputRegisterStart = 100;
        private const ushort OutputRegisterCount = 38;

        // Chamber pressure (rChMeasuredMv from MV210) and its status word,
        // published by PLC_PRG at awHolding[130..132] -- free registers inside
        // the 100..137 block already read every cycle below, so no extra
        // Modbus round trip. NOT awHolding[140..147]: that block is the
        // Thyracont vacuum gauge (fbThyracont), a different sensor entirely.
        private const int ChamberPressureOffset = 30;
        private const int ChamberPressureStatusOffset = 32;

        private const double Scale = 1000.0;

        private static readonly object Sync = new object();
        private static readonly PreviewController ChamberPreview = new PreviewController();
        private static readonly PreviewController PlenumPreview = new PreviewController();

        private static Channel _chamber = new Channel();
        private static Channel _plenum = new Channel();

        // False until the first SetChannels call. Guards against writing the
        // Channel class defaults (zero gains, zero Upper/LowerLimit) the moment
        // the worker thread connects -- that would clamp the PLC's output to 0
        // immediately, before the application has ever sent a real value.
        private static bool _channelsReady;

        // Defaults to false (not disabled=true) so the gas regulators stay
        // fail-closed on the PLC (awHolding[1] bit3 clear) until an operator
        // deliberately enables them from the Gas Section window each session.
        private static bool _gasSubsystemEnabled;

        // Traffic light lamps, awHolding[1] bits 4..6 (PLC_PRG.st decodes them
        // into xTrafficLightGreen/Yellow/Red and reassembles them as the
        // MU210-402's output bitmask: DO1 green, DO2 yellow, DO3 red).
        private static TrafficLight _trafficLight = TrafficLight.Off;

        private static State _state = new State
        {
            UsingLocalPreview = true,
            StatusText = "PLC210 PID not connected: offline, local preview"
        };

        private static string _host = "192.168.1.10";
        private static int _port = 502;
        private static bool _running;
        private static bool _forceReconnect;
        private static bool _resetRequested;
        private static int _heartbeat;
        private static Thread _worker;
        private static TcpClient _tcpClient;
        private static IModbusMaster _master;

        public static void Start(string host, int port)
        {
            lock (Sync)
            {
                _host = host;
                _port = port;
                _forceReconnect = true;

                if (_running)
                    return;

                _running = true;
                _worker = new Thread(WorkerLoop)
                {
                    IsBackground = true,
                    Name = "PLC210 PID Modbus TCP"
                };
                _worker.Start();
            }
        }

        public static void ConfigureEndpoint(string host, int port)
        {
            lock (Sync)
            {
                if (_host == host && _port == port)
                    return;

                _host = host;
                _port = port;
                _forceReconnect = true;
            }
        }

        public static void Stop()
        {
            Thread worker;
            lock (Sync)
            {
                _running = false;
                worker = _worker;
            }

            if (worker != null && worker.IsAlive)
                worker.Join(1200);

            Disconnect();
        }

        public static void SetChannels(Channel chamber, Channel plenum, bool reset)
        {
            lock (Sync)
            {
                _chamber = chamber.Clone();
                _plenum = plenum.Clone();
                _channelsReady = true;
                _resetRequested |= reset;
                if (reset)
                {
                    ChamberPreview.Reset();
                    PlenumPreview.Reset();
                }
            }
        }

        public static State GetState()
        {
            lock (Sync)
                return _state.Clone();
        }

        /// <summary>
        /// The chamber channel settings currently being pushed to the PLC, or
        /// null while SET has never been pressed.
        /// </summary>
        /// <remarks>
        /// The setpoint and the gains are owned by whoever last called
        /// <see cref="SetChannels"/>, but the Pressure Trend and PID Viewer
        /// windows need to read them too, and they are separate windows now.
        /// Reading them back off the client keeps that from turning into
        /// MainForm handing its private fields around.
        /// </remarks>
        public static Channel GetChamberChannel()
        {
            lock (Sync)
                return _channelsReady ? _chamber.Clone() : null;
        }

        /// <summary>
        /// Lights one lamp of the panel signal tower, or none.
        /// </summary>
        /// <remarks>
        /// Goes out in the flags word, which is written every cycle whether or
        /// not SET has ever been pressed, so the tower follows the system status
        /// from the moment the application connects.
        /// </remarks>
        public static void SetTrafficLight(TrafficLight lamp)
        {
            lock (Sync)
                _trafficLight = lamp;
        }

        public static void SetGasSubsystemEnabled(bool enabled)
        {
            lock (Sync)
                _gasSubsystemEnabled = enabled;
        }

        public static bool GetGasSubsystemEnabled()
        {
            lock (Sync)
                return _gasSubsystemEnabled;
        }

        private static void WorkerLoop()
        {
            DateTime lastTick = DateTime.UtcNow;

            while (true)
            {
                Channel chamber;
                Channel plenum;
                bool channelsReady;
                bool gasSubsystemEnabled;
                string host;
                int port;
                bool reset;
                bool reconnect;
                TrafficLight trafficLight;
                bool stopping;

                lock (Sync)
                {
                    stopping = !_running;

                    // On the way out, run one more full cycle with every lamp
                    // cleared instead of leaving immediately: the PLC holds the
                    // last flags word it was given, so a tower left lit would go
                    // on asserting a verdict nobody is producing any more.
                    // Skipped when there is no live socket -- there is nothing to
                    // turn off, and reconnecting purely to do it would add the
                    // connect timeout to every shutdown.
                    if (stopping && !IsConnected())
                        break;

                    chamber = _chamber.Clone();
                    plenum = _plenum.Clone();
                    channelsReady = _channelsReady;
                    gasSubsystemEnabled = _gasSubsystemEnabled;
                    trafficLight = stopping ? TrafficLight.Off : _trafficLight;
                    host = _host;
                    port = _port;
                    reset = _resetRequested;
                    _resetRequested = false;
                    reconnect = _forceReconnect;
                    _forceReconnect = false;
                }

                DateTime now = DateTime.UtcNow;
                double dt = Math.Max(0.05, Math.Min(1.0, (now - lastTick).TotalSeconds));
                lastTick = now;

                Result localChamber = ChamberPreview.Step(chamber, dt);
                Result localPlenum = PlenumPreview.Step(plenum, dt);

                try
                {
                    if (reconnect)
                        Disconnect();

                    EnsureConnected(host, port);

                    // Protocol version + flags (registers 0-1, including the gas
                    // subsystem enable bit) reach the PLC every cycle regardless of
                    // channelsReady -- unlike the PID channel registers below, this
                    // 2-word block can't clamp anything to zero, so gating it behind
                    // "has SET been pressed on the Chamber panel" only meant the gas
                    // enable checkbox (and the protocol handshake itself) silently
                    // had no effect until an unrelated button on a different panel
                    // was clicked once.
                    ushort[] flagsRegisters = { 1, ComputeFlags(chamber, plenum, reset, gasSubsystemEnabled, trafficLight) };
                    _master.WriteMultipleRegisters(UnitId, InputRegisterStart, flagsRegisters);

                    // Skip writing the channel value registers until the application
                    // has committed real values via SetChannels -- otherwise the very
                    // first cycle after connecting would write the Channel class
                    // defaults (zero gains, zero Upper/LowerLimit) and clamp the
                    // PLC's output to 0. Reading still proceeds so the UI can show
                    // connection status and live pressure before that.
                    if (channelsReady)
                    {
                        ushort[] writeRegisters = BuildInputRegisters(chamber, plenum, reset, gasSubsystemEnabled, trafficLight);
                        _master.WriteMultipleRegisters(UnitId, InputRegisterStart, writeRegisters);
                    }

                    ushort[] readRegisters = _master.ReadHoldingRegisters(
                        UnitId,
                        OutputRegisterStart,
                        OutputRegisterCount);

                    State plcState = ParseOutputRegisters(readRegisters);
                    plcState.Connected = true;
                    plcState.UsingLocalPreview = false;
                    plcState.StatusText = "PLC210 PID connected";
                    plcState.UpdatedAt = DateTime.Now;

                    lock (Sync)
                        _state = plcState;
                }
                catch (Exception ex)
                {
                    Disconnect();
                    lock (Sync)
                    {
                        _state = new State
                        {
                            Connected = false,
                            UsingLocalPreview = true,
                            StatusText = "PLC210 PID not connected: " + ShortMessage(ex),
                            UpdatedAt = DateTime.Now,
                            Chamber = localChamber,
                            Plenum = localPlenum
                        };
                    }
                }

                if (stopping)
                    break;

                Thread.Sleep(200);
            }
        }

        private static bool IsConnected()
        {
            return _tcpClient != null && _tcpClient.Connected && _master != null;
        }

        private static void EnsureConnected(string host, int port)
        {
            if (IsConnected())
                return;

            Disconnect();

            TcpClient client = new TcpClient
            {
                ReceiveTimeout = 700,
                SendTimeout = 700,
                NoDelay = true
            };

            IAsyncResult connect = client.BeginConnect(host, port, null, null);
            if (!connect.AsyncWaitHandle.WaitOne(700))
            {
                client.Close();
                throw new TimeoutException("no response from " + host + ":" + port);
            }

            client.EndConnect(connect);
            _tcpClient = client;
            _master = new ModbusFactory().CreateMaster(client);
            _master.Transport.ReadTimeout = 700;
            _master.Transport.WriteTimeout = 700;
            _master.Transport.Retries = 0;
        }

        private static void Disconnect()
        {
            try
            {
                if (_master != null)
                    _master.Dispose();
            }
            catch
            {
            }

            try
            {
                if (_tcpClient != null)
                    _tcpClient.Close();
            }
            catch
            {
            }

            _master = null;
            _tcpClient = null;
        }

        private static ushort ComputeFlags(Channel chamber, Channel plenum, bool reset,
            bool gasSubsystemEnabled, TrafficLight trafficLight)
        {
            ushort flags = 0;
            if (chamber.Enabled)
                flags |= 0x0001;
            if (plenum.Enabled)
                flags |= 0x0002;
            if (reset)
                flags |= 0x0004;
            if (gasSubsystemEnabled)
                flags |= 0x0008;

            switch (trafficLight)
            {
                case TrafficLight.Green: flags |= 0x0010; break;
                case TrafficLight.Yellow: flags |= 0x0020; break;
                case TrafficLight.Red: flags |= 0x0040; break;
            }

            return flags;
        }

        private static ushort[] BuildInputRegisters(Channel chamber, Channel plenum, bool reset,
            bool gasSubsystemEnabled, TrafficLight trafficLight)
        {
            ushort[] registers = new ushort[InputRegisterCount];
            registers[0] = 1; // Protocol version.
            // Same word as the flags-only write above, and it lands on top of it
            // in the same cycle -- so the lamp bits have to be here too, or the
            // tower would go dark again the moment SET has been pressed once.
            registers[1] = ComputeFlags(chamber, plenum, reset, gasSubsystemEnabled, trafficLight);

            _heartbeat++;
            SetInt32(registers, 2, _heartbeat);

            WriteChannel(registers, 4, chamber);
            WriteChannel(registers, 30, plenum);
            return registers;
        }

        private static void WriteChannel(ushort[] registers, int start, Channel channel)
        {
            SetFixed(registers, start + 0, channel.Setpoint);
            SetFixed(registers, start + 2, channel.Measured);
            SetFixed(registers, start + 4, channel.Kp);
            SetFixed(registers, start + 6, channel.Ki);
            SetFixed(registers, start + 8, channel.Kd);
            SetFixed(registers, start + 10, channel.LowerLimit);
            SetFixed(registers, start + 12, channel.UpperLimit);
            SetFixed(registers, start + 14, channel.DirectValue);
            registers[start + 16] = channel.DirectMode ? (ushort)1 : (ushort)0;
            SetFixed(registers, start + 17, channel.FlowCorrection);
            SetFixed(registers, start + 19, channel.PressureCorrection);
            SetFixed(registers, start + 21, channel.TotalGasFlow);
        }

        private static State ParseOutputRegisters(ushort[] registers)
        {
            if (registers == null || registers.Length < OutputRegisterCount)
                throw new InvalidOperationException("short Modbus reply");

            if (registers[0] != 1)
                throw new InvalidOperationException("incompatible PLC project version");

            // wMvAi1Status == 0 means PLC_PRG's xMvAi1Ok was true when it
            // captured this reading (see PLC_PRG.st: xMvAi1Ok := wMvAi1Status
            // = WORD#16#0000).
            ushort mvStatus = registers[ChamberPressureStatusOffset];

            return new State
            {
                Chamber = ReadResult(registers, 4),
                Plenum = ReadResult(registers, 20),
                PlcPressureAvailable = true,
                PlcPressureValid = mvStatus == 0,
                PlcPressureTorr = ReadFixed(registers, ChamberPressureOffset),
                PlcPressureSource = "PLC (MV210 via MV210 AI1)",
                PlcPressureStatusText = mvStatus == 0 ? "OK" : "MV210 status " + mvStatus.ToString("X4")
            };
        }

        private static Result ReadResult(ushort[] registers, int start)
        {
            return new Result
            {
                Error = ReadFixed(registers, start + 0),
                P = ReadFixed(registers, start + 2),
                I = ReadFixed(registers, start + 4),
                D = ReadFixed(registers, start + 6),
                Output = ReadFixed(registers, start + 8)
            };
        }

        private static void SetFixed(ushort[] registers, int index, double value)
        {
            double scaled = Math.Round(value * Scale);
            if (scaled > int.MaxValue)
                scaled = int.MaxValue;
            else if (scaled < int.MinValue)
                scaled = int.MinValue;

            SetInt32(registers, index, (int)scaled);
        }

        private static double ReadFixed(ushort[] registers, int index)
        {
            return ReadInt32(registers, index) / Scale;
        }

        private static void SetInt32(ushort[] registers, int index, int value)
        {
            unchecked
            {
                registers[index] = (ushort)(value & 0xFFFF);
                registers[index + 1] = (ushort)((uint)value >> 16);
            }
        }

        private static int ReadInt32(ushort[] registers, int index)
        {
            unchecked
            {
                uint value = registers[index] | ((uint)registers[index + 1] << 16);
                return (int)value;
            }
        }

        private static double Clamp(double value, double min, double max)
        {
            if (min > max)
            {
                double temp = min;
                min = max;
                max = temp;
            }

            return Math.Max(min, Math.Min(max, value));
        }

        private static string ShortMessage(Exception ex)
        {
            if (ex == null || string.IsNullOrWhiteSpace(ex.Message))
                return "communication error";

            SlaveException slaveException = ex as SlaveException;
            if (slaveException != null)
            {
                return string.Format(
                    "Modbus exception {0} {1}, func {2}, unit {3}; check CODESYS register map 0..147",
                    slaveException.SlaveExceptionCode,
                    DescribeSlaveExceptionCode(slaveException.SlaveExceptionCode),
                    slaveException.FunctionCode,
                    slaveException.SlaveAddress);
            }

            string message = ex.Message.Replace("\r", " ").Replace("\n", " ");
            return message.Length <= 80 ? message : message.Substring(0, 80);
        }

        private static string DescribeSlaveExceptionCode(byte code)
        {
            switch (code)
            {
                case 1:
                    return "IllegalFunction";
                case 2:
                    return "IllegalDataAddress";
                case 3:
                    return "IllegalDataValue";
                case 4:
                    return "SlaveDeviceFailure";
                case 5:
                    return "Acknowledge";
                case 6:
                    return "SlaveDeviceBusy";
                default:
                    return "Unknown";
            }
        }
    }
}
