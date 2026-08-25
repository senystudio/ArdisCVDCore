using NModbus;
using System;
using System.Net.Sockets;
using System.Threading;

namespace ArdisCVDCore.modules_hw
{
    /// <summary>
    /// Reads/writes the 10 kW microwave generator published by PRG_Microwave
    /// at awHolding[192..205]. PLC talks Modbus RTU to the generator (slave
    /// id 9) on RS485-2, same bus as the РРГ-20 gas regulators. Mirrors the
    /// generator's own touch screen: Preheat/Microwave/Reset are three
    /// independent commands, no PLC-side auto-sequencing.
    /// </summary>
    public static class PLC210MicrowaveClient
    {
        public sealed class State
        {
            public bool Connected;
            public string StatusText;
            public DateTime UpdatedAt;

            public double IncidentKw;
            public double ReflectedKw;
            public double SetpointKw;

            public bool Working;
            public bool PreheatOn;
            public bool MicrowaveOn;
            public bool FilamentPreheatDone;
            public bool FaultActive;
            public bool CommError;
            public bool ChamberPressureLow;

            public bool Failure;
            public bool ReflectiveProtection;
            public bool FilamentFlowFault;
            public bool FilamentUnderflowFault;
            public bool MagnetronAbnormal1;
            public bool AnodeFlowFault;
            public bool FireFailure;
            public bool MagnetronTooWarm;
            public bool WaterFlowFault;

            // Snapshot of the live fault bits latched by PRG_Microwave.st at the
            // moment it tripped -- unlike the live booleans above, this doesn't
            // change if the underlying condition clears before the operator looks.
            public ushort FaultReasonBits;
            public int PreheatElapsedSeconds;

            public ushort Heartbeat;
            public short DiagIncidentRaw;
            public short DiagReflectedRaw;

            // Raw awHolding[192..205] words exactly as read over Modbus TCP --
            // for the on-screen register dump, so the operator can cross-check
            // our decoding against CODESYS/the generator's own screen directly.
            public ushort[] RawRegisters;

            /// <summary>
            /// The generator itself is answering, not just the PLC. Everything
            /// PRG_Microwave.st publishes about preheat is an echo of the last
            /// request -- status bit 0x0020 and the awHolding[205] counter alike
            /// -- so none of it means anything unless this is true.
            /// </summary>
            public bool GeneratorAnswering
            {
                get { return Connected && !CommError; }
            }

            public State Clone()
            {
                State copy = (State)MemberwiseClone();
                if (RawRegisters != null)
                    copy.RawRegisters = (ushort[])RawRegisters.Clone();
                return copy;
            }
        }

        private const byte UnitId = 1;
        private const ushort RegisterStart = 192;
        private const ushort RegisterCount = 14;
        private const double Scale = 1000.0;
        private const double ScanSeconds = 0.05;
        public const double MinSetpointKw = 1.0;
        public const double MaxSetpointKw = 10.0;

        private static readonly object Sync = new object();

        private static string _host = "192.168.1.10";
        private static int _port = 502;
        private static bool _running;
        private static bool _forceReconnect;
        private static bool _preheatRequested;
        private static bool _microwaveRequested;
        private static bool _resetPending;
        private static double _setpointKw = MinSetpointKw;
        private static Thread _worker;
        private static TcpClient _tcpClient;
        private static IModbusMaster _master;
        private static State _state = new State
        {
            StatusText = "PLC210 microwave generator disabled"
        };

        public static void Start(string host, int port)
        {
            lock (Sync)
            {
                _host = string.IsNullOrWhiteSpace(host) ? _host : host;
                _port = port > 0 ? port : _port;
                _forceReconnect = true;

                if (_running)
                    return;

                _running = true;
                _worker = new Thread(WorkerLoop)
                {
                    IsBackground = true,
                    Name = "PLC210 Microwave Modbus TCP"
                };
                _worker.Start();
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

        public static State GetState()
        {
            lock (Sync)
                return _state.Clone();
        }

        public static void RequestPreheat(bool on)
        {
            lock (Sync)
                _preheatRequested = on;
        }

        public static void RequestMicrowave(bool on)
        {
            lock (Sync)
                _microwaveRequested = on;
        }

        public static void RequestSetpoint(double kw)
        {
            lock (Sync)
                _setpointKw = Clamp(kw, MinSetpointKw, MaxSetpointKw);
        }

        public static void RequestReset()
        {
            lock (Sync)
                _resetPending = true;
        }

        private static void WorkerLoop()
        {
            while (true)
            {
                string host;
                int port;
                bool reconnect;
                bool preheatRequested;
                bool microwaveRequested;
                bool resetPulse;
                double setpointKw;

                lock (Sync)
                {
                    if (!_running)
                        break;

                    host = _host;
                    port = _port;
                    reconnect = _forceReconnect;
                    _forceReconnect = false;
                    preheatRequested = _preheatRequested;
                    microwaveRequested = _microwaveRequested;
                    setpointKw = _setpointKw;
                    resetPulse = _resetPending;
                    _resetPending = false;
                }

                try
                {
                    if (reconnect)
                        Disconnect();

                    EnsureConnected(host, port);

                    ushort command = 0;
                    if (preheatRequested)
                        command |= 0x0001;
                    if (microwaveRequested)
                        command |= 0x0002;
                    if (resetPulse)
                        command |= 0x0004;

                    ushort[] setpointWords = ToFixedWords(setpointKw);
                    ushort[] writeRegisters = { command, setpointWords[0], setpointWords[1] };
                    _master.WriteMultipleRegisters(UnitId, RegisterStart, writeRegisters);

                    ushort[] registers = _master.ReadHoldingRegisters(UnitId, RegisterStart, RegisterCount);
                    State plcState = ParseRegisters(registers);
                    plcState.Connected = true;
                    plcState.StatusText = "PLC210 microwave generator connected";
                    plcState.UpdatedAt = DateTime.Now;

                    lock (Sync)
                        _state = plcState;
                }
                catch (Exception ex)
                {
                    if (resetPulse)
                    {
                        lock (Sync)
                            _resetPending = true;
                    }

                    Disconnect();
                    lock (Sync)
                    {
                        _state = new State
                        {
                            Connected = false,
                            StatusText = "PLC210 microwave generator not connected: " + ShortMessage(ex),
                            UpdatedAt = DateTime.Now
                        };
                    }
                }

                Thread.Sleep(500);
            }
        }

        private static State ParseRegisters(ushort[] registers)
        {
            if (registers == null || registers.Length < RegisterCount)
                throw new InvalidOperationException("short Modbus response");

            ushort status = registers[3];
            return new State
            {
                SetpointKw = ReadFixed(registers, 1),
                IncidentKw = ReadFixed(registers, 5),
                ReflectedKw = ReadFixed(registers, 7),
                Working = (status & 0x0001) != 0,
                Failure = (status & 0x0002) != 0,
                ReflectiveProtection = (status & 0x0004) != 0,
                FilamentPreheatDone = (status & 0x0008) != 0,
                CommError = (status & 0x0010) != 0,
                PreheatOn = (status & 0x0020) != 0,
                MicrowaveOn = (status & 0x0040) != 0,
                FaultActive = (status & 0x0080) != 0,
                ChamberPressureLow = (status & 0x0100) != 0,
                FilamentFlowFault = (status & 0x0200) != 0,
                FilamentUnderflowFault = (status & 0x0400) != 0,
                MagnetronAbnormal1 = (status & 0x0800) != 0,
                AnodeFlowFault = (status & 0x1000) != 0,
                FireFailure = (status & 0x2000) != 0,
                MagnetronTooWarm = (status & 0x4000) != 0,
                WaterFlowFault = (status & 0x8000) != 0,
                FaultReasonBits = registers[12],
                PreheatElapsedSeconds = (int)(registers[13] * ScanSeconds),
                Heartbeat = registers[9],
                DiagIncidentRaw = (short)registers[10],
                DiagReflectedRaw = (short)registers[11],
                RawRegisters = (ushort[])registers.Clone()
            };
        }

        private static void EnsureConnected(string host, int port)
        {
            if (_tcpClient != null && _tcpClient.Connected && _master != null)
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

        private static ushort[] ToFixedWords(double kw)
        {
            double scaled = Math.Round(kw * Scale);
            if (scaled > int.MaxValue)
                scaled = int.MaxValue;
            else if (scaled < int.MinValue)
                scaled = int.MinValue;

            int value = (int)scaled;
            unchecked
            {
                return new ushort[] { (ushort)(value & 0xFFFF), (ushort)((uint)value >> 16) };
            }
        }

        private static double ReadFixed(ushort[] registers, int index)
        {
            unchecked
            {
                uint value = registers[index] | ((uint)registers[index + 1] << 16);
                return (int)value / Scale;
            }
        }

        private static double Clamp(double value, double min, double max)
        {
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
                    "Modbus exception {0} {1}, func {2}, unit {3}; check CODESYS registers 192..205",
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
                case 1: return "IllegalFunction";
                case 2: return "IllegalDataAddress";
                case 3: return "IllegalDataValue";
                case 4: return "SlaveDeviceFailure";
                case 5: return "Acknowledge";
                case 6: return "SlaveDeviceBusy";
                default: return "Unknown";
            }
        }
    }
}
