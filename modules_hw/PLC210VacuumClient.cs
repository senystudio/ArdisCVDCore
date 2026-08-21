using NModbus;
using System;
using System.Net.Sockets;
using System.Threading;

namespace ArdisCVDCore.modules_hw
{
    public static class PLC210VacuumClient
    {
        public const int ValveCount = 8;

        public sealed class State
        {
            public bool Connected;
            public string StatusText;
            public DateTime UpdatedAt;
            public bool[] ValveOn = new bool[ValveCount];
            public bool WaterPumpOn;
            public bool ForeVacPumpOn;

            public State Clone()
            {
                State copy = (State)MemberwiseClone();
                copy.ValveOn = (bool[])ValveOn.Clone();
                return copy;
            }
        }

        private const byte UnitId = 1;
        private const ushort ValveCommandRegister = 135;
        private const ushort PumpCommandRegister = 137;
        private const ushort BlockStart = 135;
        private const ushort BlockCount = 4;

        private static readonly object Sync = new object();
        private static readonly bool[] _requestedValve = new bool[ValveCount];
        private static bool _requestedWaterPump;
        private static bool _requestedForeVacPump;

        private static string _host = "192.168.1.10";
        private static int _port = 502;
        private static bool _running;
        private static bool _forceReconnect;
        private static Thread _worker;
        private static TcpClient _tcpClient;
        private static IModbusMaster _master;
        private static State _state = new State
        {
            StatusText = "PLC210 vacuum outputs disabled"
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
                    Name = "PLC210 Vacuum Modbus TCP"
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

        public static void RequestValve(int index, bool on)
        {
            lock (Sync)
                _requestedValve[index] = on;
        }

        public static void RequestWaterPump(bool on)
        {
            lock (Sync)
                _requestedWaterPump = on;
        }

        public static void RequestForeVacPump(bool on)
        {
            lock (Sync)
                _requestedForeVacPump = on;
        }

        private static void WorkerLoop()
        {
            while (true)
            {
                string host;
                int port;
                bool reconnect;
                bool[] requestedValve;
                bool requestedWaterPump;
                bool requestedForeVacPump;

                lock (Sync)
                {
                    if (!_running)
                        break;

                    host = _host;
                    port = _port;
                    reconnect = _forceReconnect;
                    _forceReconnect = false;
                    requestedValve = (bool[])_requestedValve.Clone();
                    requestedWaterPump = _requestedWaterPump;
                    requestedForeVacPump = _requestedForeVacPump;
                }

                try
                {
                    if (reconnect)
                        Disconnect();

                    EnsureConnected(host, port);

                    ushort valveCommand = 0;
                    for (int i = 0; i < ValveCount; i++)
                        if (requestedValve[i])
                            valveCommand |= (ushort)(1 << i);

                    ushort pumpCommand = 0;
                    if (requestedWaterPump)
                        pumpCommand |= 0x0001;
                    if (requestedForeVacPump)
                        pumpCommand |= 0x0002;

                    _master.WriteSingleRegister(UnitId, ValveCommandRegister, valveCommand);
                    _master.WriteSingleRegister(UnitId, PumpCommandRegister, pumpCommand);

                    ushort[] block = _master.ReadHoldingRegisters(UnitId, BlockStart, BlockCount);
                    ushort valveStatus = block[1];
                    ushort pumpStatus = block[3];

                    State plcState = new State
                    {
                        Connected = true,
                        StatusText = "PLC210 vacuum outputs connected",
                        UpdatedAt = DateTime.Now,
                        WaterPumpOn = (pumpStatus & 0x0001) != 0,
                        ForeVacPumpOn = (pumpStatus & 0x0002) != 0
                    };
                    for (int i = 0; i < ValveCount; i++)
                        plcState.ValveOn[i] = (valveStatus & (1 << i)) != 0;

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
                            StatusText = "PLC210 vacuum outputs not connected: " + ShortMessage(ex),
                            UpdatedAt = DateTime.Now
                        };
                    }
                }

                Thread.Sleep(300);
            }
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
                    "Modbus exception {0} {1}, func {2}, unit {3}; check CODESYS registers 135..138",
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
