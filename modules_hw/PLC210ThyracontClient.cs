using NModbus;
using System;
using System.Net.Sockets;
using System.Threading;

namespace ArdisCVDCore.modules_hw
{
    /// <summary>
    /// Reads the Thyracont vacuum sensor values published by the PLC210 project.
    /// The PLC itself talks to the sensor over RS-485; the HMI only reads the
    /// prepared values over Modbus TCP.
    /// </summary>
    public static class PLC210ThyracontClient
    {
        public sealed class State
        {
            public bool Enabled;
            public bool Connected;
            public bool HasValidValue;
            public bool CathodeOn;
            public string StatusText;
            public DateTime UpdatedAt;
            public double PressureTorr;
            public double PressureMbar;
            public ushort PlcStatusFlags;
            public ushort PlcErrorCode;

            public State Clone()
            {
                return (State)MemberwiseClone();
            }
        }

        private const byte UnitId = 1;
        private const ushort RegisterStart = 140;
        private const ushort RegisterCount = 8;
        private const double Scale = 1000.0;

        private static readonly object Sync = new object();

        private static string _host = "192.168.1.10";
        private static int _port = 502;
        private static bool _running;
        private static bool _forceReconnect;
        private static Thread _worker;
        private static TcpClient _tcpClient;
        private static IModbusMaster _master;
        private static State _state = new State
        {
            Enabled = false,
            StatusText = "PLC210 Thyracont disabled"
        };

        public static bool Enabled
        {
            get
            {
                lock (Sync)
                    return _state.Enabled;
            }
        }

        public static void Start(string host, int port)
        {
            lock (Sync)
            {
                _host = string.IsNullOrWhiteSpace(host) ? _host : host;
                _port = port > 0 ? port : _port;
                _forceReconnect = true;
                _state.Enabled = true;
                _state.StatusText = "PLC210 Thyracont connecting";

                if (_running)
                    return;

                _running = true;
                _worker = new Thread(WorkerLoop)
                {
                    IsBackground = true,
                    Name = "PLC210 Thyracont Modbus TCP"
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
                _state.Enabled = false;
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

        private static void WorkerLoop()
        {
            while (true)
            {
                string host;
                int port;
                bool reconnect;

                lock (Sync)
                {
                    if (!_running)
                        break;

                    host = _host;
                    port = _port;
                    reconnect = _forceReconnect;
                    _forceReconnect = false;
                }

                try
                {
                    if (reconnect)
                        Disconnect();

                    EnsureConnected(host, port);
                    ushort[] registers = _master.ReadHoldingRegisters(UnitId, RegisterStart, RegisterCount);
                    State plcState = ParseRegisters(registers);
                    plcState.Enabled = true;
                    plcState.Connected = true;
                    plcState.StatusText = plcState.HasValidValue
                        ? "PLC210 Thyracont connected"
                        : "PLC210 Thyracont has no valid value";
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
                            Enabled = true,
                            Connected = false,
                            HasValidValue = false,
                            StatusText = "PLC210 Thyracont not connected: " + ShortMessage(ex),
                            UpdatedAt = DateTime.Now
                        };
                    }
                }

                Thread.Sleep(300);
            }
        }

        private static State ParseRegisters(ushort[] registers)
        {
            if (registers == null || registers.Length < RegisterCount)
                throw new InvalidOperationException("short Modbus response");

            ushort flags = registers[4];
            return new State
            {
                PressureTorr = ReadFixed(registers, 0),
                PressureMbar = ReadFixed(registers, 2),
                PlcStatusFlags = flags,
                PlcErrorCode = registers[5],
                HasValidValue = (flags & 0x0001) != 0,
                CathodeOn = (flags & 0x0002) != 0
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

        private static double ReadFixed(ushort[] registers, int index)
        {
            unchecked
            {
                uint value = registers[index] | ((uint)registers[index + 1] << 16);
                return (int)value / Scale;
            }
        }

        private static string ShortMessage(Exception ex)
        {
            if (ex == null || string.IsNullOrWhiteSpace(ex.Message))
                return "communication error";

            SlaveException slaveException = ex as SlaveException;
            if (slaveException != null)
            {
                return string.Format(
                    "Modbus exception {0} {1}, func {2}, unit {3}; check CODESYS registers 140..147",
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
