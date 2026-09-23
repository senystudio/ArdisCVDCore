using NModbus;
using System;
using System.Net.Sockets;
using System.Threading;

namespace ArdisCVDCore.modules_hw
{
    public static class PLC210AlarmClient
    {
        public sealed class State
        {
            public bool Connected;
            public string StatusText = "PLC210 alarms not started";
            public DateTime UpdatedAt;

            public ulong AlarmMask;
            public ulong AbortMask;
            public ulong AbortLatched;

            public bool AbortActive;
            public bool AnyAlarm;
            public bool ThresholdsAccepted;
            public bool RetainWasBlank;
            public bool WaitingForFreshPress;

            public ushort GasValveInhibit;
            public ushort VacuumValveInhibit;
            public int AbortCount;

            public State Clone()
            {
                return (State)MemberwiseClone();
            }

            public bool IsAlarm(int code)
            {
                return code >= 0 && code < 64 && (AlarmMask & (1UL << code)) != 0;
            }

            public bool IsAbort(int code)
            {
                return code >= 0 && code < 64 && (AbortMask & (1UL << code)) != 0;
            }

            public bool WasAbortCause(int code)
            {
                return code >= 0 && code < 64 && (AbortLatched & (1UL << code)) != 0;
            }
        }

        private const byte UnitId = 1;
        private const ushort ThresholdStart = 248;
        private const ushort ThresholdCount = 32;
        private const ushort StateStart = 280;
        private const ushort StateCount = 16;

        private static readonly object Sync = new object();

        private static string _host = "192.168.1.10";
        private static int _port = 502;
        private static bool _running;
        private static bool _forceReconnect;
        private static Thread _worker;
        private static TcpClient _tcpClient;
        private static IModbusMaster _master;

        private static ushort[] _thresholds;

        private static State _state = new State();

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
                    Name = "PLC210 Alarms Modbus TCP"
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
                    StatusText = "PLC210 alarms disconnected",
                    UpdatedAt = DateTime.Now
                };
            }
        }

        public static State GetState()
        {
            lock (Sync)
                return _state.Clone();
        }

        public static void PushThresholds(ushort[] block)
        {
            if (block == null || block.Length != ThresholdCount)
                throw new ArgumentException("the threshold block must be exactly "
                    + ThresholdCount + " words", "block");

            ushort[] copy = (ushort[])block.Clone();
            lock (Sync)
                _thresholds = copy;
        }

        private static void WorkerLoop()
        {
            while (true)
            {
                string host;
                int port;
                bool reconnect;
                ushort[] thresholds;

                lock (Sync)
                {
                    if (!_running)
                        break;

                    host = _host;
                    port = _port;
                    reconnect = _forceReconnect;
                    _forceReconnect = false;
                    thresholds = _thresholds;
                }

                try
                {
                    if (reconnect)
                        Disconnect();

                    EnsureConnected(host, port);

                    if (thresholds != null)
                        _master.WriteMultipleRegisters(UnitId, ThresholdStart, thresholds);

                    ushort[] block = _master.ReadHoldingRegisters(UnitId, StateStart, StateCount);
                    State plcState = ParseRegisters(block);
                    plcState.Connected = true;
                    plcState.StatusText = plcState.ThresholdsAccepted
                        ? "PLC210 alarms connected"
                        : "PLC210 alarms connected, the PLC has not taken the thresholds yet";
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
                            StatusText = "PLC210 alarms not connected: " + ShortMessage(ex),
                            UpdatedAt = DateTime.Now
                        };
                    }
                }

                Thread.Sleep(500);
            }
        }

        private static State ParseRegisters(ushort[] registers)
        {
            if (registers == null || registers.Length < StateCount)
                throw new InvalidOperationException("short Modbus response");

            ushort status = registers[12];

            return new State
            {
                AlarmMask = ReadMask(registers, 0),
                AbortMask = ReadMask(registers, 4),
                AbortLatched = ReadMask(registers, 8),

                AbortActive = (status & 0x0001) != 0,
                AnyAlarm = (status & 0x0002) != 0,
                ThresholdsAccepted = (status & 0x0004) != 0,
                RetainWasBlank = (status & 0x0008) != 0,
                WaitingForFreshPress = (status & 0x0010) != 0,

                AbortCount = registers[13],
                GasValveInhibit = registers[14],
                VacuumValveInhibit = registers[15]
            };
        }

        private static ulong ReadMask(ushort[] registers, int offset)
        {
            ulong value = 0;
            for (int i = 0; i < 4; i++)
                value |= (ulong)registers[offset + i] << (i * 16);
            return value;
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
            string message = ex.Message ?? string.Empty;
            int stop = message.IndexOf('\n');
            if (stop > 0)
                message = message.Substring(0, stop);
            return message.Trim();
        }
    }
}
