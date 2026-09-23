using NModbus;
using System;
using System.Net.Sockets;
using System.Threading;

namespace ArdisCVDCore.modules_hw
{
    /// <summary>
    /// The turbo pump's KYKY TD drive module, as published by the PLC210
    /// project at registers 240..247. The PLC talks Modbus RTU to the drive;
    /// the HMI only asks for run/stop and reads back what the drive says.
    /// </summary>
    /// <remarks>
    /// Register meanings are in GVL_ModbusHolding.st and the drive's own
    /// register addresses in GVL_TurboPumpIO.st. The values here are raw
    /// device units -- Hz, 0.01 V, 0.01 A, degC -- not the x1000 fixed point
    /// the pressure and cooling blocks use.
    /// </remarks>
    public static class PLC210TurboPumpClient
    {
        /// <summary>
        /// Rated output frequency of the drive, used as the full scale of the
        /// speed bar on the mimic diagram.
        /// </summary>
        /// <remarks>
        /// Per-model, from the TD manual's performance table: TD-25J/TD-25
        /// 600 Hz, TD-80 1200 Hz, TD-150J/TD-150 704/850 Hz, TD-300 950 Hz.
        /// 850 is the TD-150 fitted here, and is the same number
        /// ArdisCVDMaster's drawRod scaled its bar against.
        /// </remarks>
        public const int RatedSpeedHz = 850;

        public sealed class State
        {
            public bool Connected;
            public bool DriveAnswering;
            public bool RunLatched;
            public bool CommandIssued;
            public string StatusText;
            public DateTime UpdatedAt;

            public int SpeedHz;
            public double DriveVoltage;
            public double DriveCurrent;
            public int TemperatureC;
            public ushort DriveStatusWord;

            public bool StartFailure;
            public bool Overload;
            public bool AtNormalSpeed;
            public bool HighSpeed;
            public bool Working;
            public bool Overcurrent;
            public bool Overheating;
            public bool Timeout;
            public bool WatchdogFault;

            public bool FaultActive
            {
                get
                {
                    return StartFailure || Overload || Overcurrent || Overheating
                        || Timeout || WatchdogFault;
                }
            }

            /// <summary>The first fault the drive is reporting, or null.</summary>
            public string FaultText
            {
                get
                {
                    if (Overheating) return "overheating";
                    if (Overcurrent) return "overcurrent";
                    if (Overload) return "overload";
                    if (StartFailure) return "failed to start";
                    if (Timeout) return "spin-up timed out";
                    if (WatchdogFault) return "drive watchdog";
                    return null;
                }
            }

            public State Clone()
            {
                return (State)MemberwiseClone();
            }
        }

        private const byte UnitId = 1;
        private const ushort CommandRegister = 240;
        private const ushort BlockStart = 240;
        private const ushort BlockCount = 8;

        private const ushort CommandRun = 0x0001;
        private const ushort CommandValid = 0x8000;

        // SHI = 0x01 with SLO = 0x08 is the drive's watchdog fault, spelled out
        // as a whole word in the TD manual rather than as a status bit -- on the
        // bits alone it would read as the harmless "high speed, not working".
        private const ushort WatchdogStatusWord = 0x0108;

        private static readonly object Sync = new object();

        private static string _host = "192.168.1.10";
        private static int _port = 502;
        private static bool _running;
        private static bool _forceReconnect;
        private static Thread _worker;
        private static TcpClient _tcpClient;
        private static IModbusMaster _master;

        // Null until the drive has been read back once. The pump latches its own
        // state, so a freshly started HMI must not command anything before it
        // knows what the pump is doing -- otherwise reopening this window while
        // the pump runs would spin it down. On the first good read it adopts the
        // drive's own "working" bit and only then starts commanding.
        private static bool? _requestedRun;

        private static State _state = new State
        {
            StatusText = "PLC210 turbo pump disabled"
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
                    Name = "PLC210 Turbo Pump Modbus TCP"
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

            lock (Sync)
            {
                _state = new State
                {
                    Connected = false,
                    StatusText = "PLC210 turbo pump disconnected",
                    UpdatedAt = DateTime.Now
                };
            }
        }

        public static State GetState()
        {
            lock (Sync)
                return _state.Clone();
        }

        public static void RequestRun(bool on)
        {
            lock (Sync)
                _requestedRun = on;
        }

        private static void WorkerLoop()
        {
            while (true)
            {
                string host;
                int port;
                bool reconnect;
                bool? requestedRun;

                lock (Sync)
                {
                    if (!_running)
                        break;

                    host = _host;
                    port = _port;
                    reconnect = _forceReconnect;
                    _forceReconnect = false;
                    requestedRun = _requestedRun;
                }

                try
                {
                    if (reconnect)
                        Disconnect();

                    EnsureConnected(host, port);

                    ushort command = 0;
                    if (requestedRun.HasValue)
                    {
                        command |= CommandValid;
                        if (requestedRun.Value)
                            command |= CommandRun;
                    }

                    _master.WriteSingleRegister(UnitId, CommandRegister, command);

                    ushort[] block = _master.ReadHoldingRegisters(UnitId, BlockStart, BlockCount);
                    State plcState = ParseRegisters(block);
                    plcState.Connected = true;
                    plcState.StatusText = plcState.DriveAnswering
                        ? "PLC210 turbo pump connected"
                        : "PLC210 turbo pump: the drive is not answering the PLC";
                    plcState.UpdatedAt = DateTime.Now;

                    lock (Sync)
                    {
                        _state = plcState;

                        if (!_requestedRun.HasValue && plcState.DriveAnswering)
                            _requestedRun = plcState.Working;
                    }
                }
                catch (Exception ex)
                {
                    Disconnect();
                    lock (Sync)
                    {
                        _state = new State
                        {
                            Connected = false,
                            StatusText = "PLC210 turbo pump not connected: " + ShortMessage(ex),
                            UpdatedAt = DateTime.Now
                        };
                    }
                }

                Thread.Sleep(300);
            }
        }

        private static State ParseRegisters(ushort[] registers)
        {
            if (registers == null || registers.Length < BlockCount)
                throw new InvalidOperationException("short Modbus response");

            ushort latched = registers[1];
            ushort status = registers[5];

            return new State
            {
                RunLatched = (latched & 0x0001) != 0,
                CommandIssued = (latched & 0x0002) != 0,
                SpeedHz = registers[2],
                DriveVoltage = registers[3] / 100.0,
                DriveCurrent = registers[4] / 100.0,
                DriveStatusWord = status,
                TemperatureC = registers[6] & 0x00FF,
                DriveAnswering = (registers[7] & 0x0001) != 0,

                // TD manual 4.2.2 e), status low byte.
                StartFailure = (status & 0x0001) != 0,
                Overload = (status & 0x0002) != 0,
                AtNormalSpeed = (status & 0x0004) != 0,
                HighSpeed = (status & 0x0008) != 0,
                Working = (status & 0x0010) != 0,
                Overcurrent = (status & 0x0020) != 0,
                Overheating = (status & 0x0040) != 0,
                Timeout = (status & 0x0080) != 0,
                WatchdogFault = status == WatchdogStatusWord
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

        private static string ShortMessage(Exception ex)
        {
            if (ex == null || string.IsNullOrWhiteSpace(ex.Message))
                return "communication error";

            SlaveException slaveException = ex as SlaveException;
            if (slaveException != null)
            {
                return string.Format(
                    "Modbus exception {0} {1}, func {2}, unit {3}; check CODESYS registers 240..247",
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
