using NModbus;
using System;
using System.Globalization;
using System.Net.Sockets;
using System.Threading;

namespace ArdisCVDCore.modules_hw
{
    public enum TrafficLight
    {
        Off = 0,
        Green,
        Yellow,
        Red,
        GreenYellow,
        RedYellow
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
            public bool SmartMode;
            public double SmartKp;
            public ushort PresetId;
            public double PresetOutput;
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
            public ushort ChamberPresetDoneId;

            public ushort DiscreteInputs;

            public bool LidClosed
            {
                get { return IsDiscreteInputOn(DiscreteInputs, LidInputChannel); }
            }

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
                    PlcPressureStatusText = PlcPressureStatusText,
                    ChamberPresetDoneId = ChamberPresetDoneId,
                    DiscreteInputs = DiscreteInputs
                };
            }
        }

        private sealed class PreviewController
        {
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

        private const ushort OutputRegisterCount = 40;

        private const int ChamberPressureOffset = 30;
        private const int ChamberPressureStatusOffset = 32;
        private const int ChamberPresetDoneOffset = 14;

        private const int ChamberPresetOutputRegister = 56;
        private const int ChamberPresetIdRegister = 58;

        private const double Scale = 1000.0;

        private const int DiscreteInputsOffset = 39;

        public const int DiscreteInputCount = 12;

        public const int DefaultLidInputChannel = 1;

        private static int _lidInputChannel = DefaultLidInputChannel;

        public static int LidInputChannel
        {
            get { return _lidInputChannel; }
        }

        public static void SetLidInputChannel(int channel)
        {
            _lidInputChannel = channel >= 1 && channel <= DiscreteInputCount
                ? channel
                : DefaultLidInputChannel;
        }

        public static bool IsDiscreteInputOn(ushort mask, int channel)
        {
            return channel >= 1
                && channel <= DiscreteInputCount
                && (mask & (1 << (channel - 1))) != 0;
        }

        public static string DiscreteInputName(int channel)
        {
            if (channel < 1 || channel > DiscreteInputCount)
                return "no input";

            return (channel <= 8 ? "FDI" : "DI") + channel.ToString(CultureInfo.InvariantCulture);
        }

        private static readonly object Sync = new object();
        private static readonly PreviewController ChamberPreview = new PreviewController();
        private static readonly PreviewController PlenumPreview = new PreviewController();

        private static Channel _chamber = new Channel();
        private static Channel _plenum = new Channel();

        private static bool _channelsReady;

        private static bool _gasSubsystemEnabled;

        private static TrafficLight _trafficLight = TrafficLight.Off;

        private static State _state = new State
        {
            UsingLocalPreview = true,
            StatusText = "PLC210 PID not connected: offline, local preview"
        };

        private static string _host = "192.168.1.10";
        private static int _port = 502;
        private static bool _running;
        private static int _errConnCount;
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

            lock (Sync)
            {
                _state = new State
                {
                    Connected = false,
                    UsingLocalPreview = true,
                    StatusText = "PLC210 PID disconnected",
                    UpdatedAt = DateTime.Now,
                    Chamber = _state.Chamber,
                    Plenum = _state.Plenum
                };
            }
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

        public static void RequestReset()
        {
            lock (Sync)
            {
                _resetRequested = true;
                ChamberPreview.Reset();
                PlenumPreview.Reset();
            }
        }

        public static State GetState()
        {
            lock (Sync)
                return _state.Clone();
        }

        public static Channel GetChamberChannel()
        {
            lock (Sync)
                return _channelsReady ? _chamber.Clone() : null;
        }

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

                    ushort[] flagsRegisters = { 1, ComputeFlags(chamber, plenum, reset, gasSubsystemEnabled, trafficLight) };
                    _master.WriteMultipleRegisters(UnitId, InputRegisterStart, flagsRegisters);

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

                    _errConnCount = 0;
                }
                catch (Exception ex)
                {
                    _errConnCount++;
                    if (_errConnCount < 2)
                        Logger.WriteError(new Exception("PLC PID: " + ex.Message));

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
                case TrafficLight.GreenYellow: flags |= 0x0030; break;
                case TrafficLight.RedYellow: flags |= 0x0060; break;
            }

            return flags;
        }

        private static ushort[] BuildInputRegisters(Channel chamber, Channel plenum, bool reset,
            bool gasSubsystemEnabled, TrafficLight trafficLight)
        {
            ushort[] registers = new ushort[InputRegisterCount];
            registers[0] = 1;
            registers[1] = ComputeFlags(chamber, plenum, reset, gasSubsystemEnabled, trafficLight);

            _heartbeat++;
            SetInt32(registers, 2, _heartbeat);

            WriteChannel(registers, 4, chamber);
            WriteChannel(registers, 30, plenum);
            SetFixed(registers, ChamberPresetOutputRegister, chamber.PresetOutput);
            registers[ChamberPresetIdRegister] = chamber.PresetId;
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
            SetFixed(registers, start + 23, channel.SmartKp);
            registers[start + 25] = channel.SmartMode ? (ushort)1 : (ushort)0;
        }

        private static State ParseOutputRegisters(ushort[] registers)
        {
            if (registers == null || registers.Length < OutputRegisterCount)
                throw new InvalidOperationException("short Modbus reply");

            if (registers[0] != 1)
                throw new InvalidOperationException("incompatible PLC project version");

            ushort mvStatus = registers[ChamberPressureStatusOffset];

            return new State
            {
                Chamber = ReadResult(registers, 4),
                Plenum = ReadResult(registers, 20),
                PlcPressureAvailable = true,
                PlcPressureValid = mvStatus == 0,
                PlcPressureTorr = ReadFixed(registers, ChamberPressureOffset),
                PlcPressureSource = "PLC (MV210 via MV210 AI1)",
                PlcPressureStatusText = mvStatus == 0 ? "OK" : "MV210 status " + mvStatus.ToString("X4"),
                ChamberPresetDoneId = registers[ChamberPresetDoneOffset],
                DiscreteInputs = registers[DiscreteInputsOffset]
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
