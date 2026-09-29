using NModbus;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;

namespace ArdisCVDCore.modules_hw
{
    public static class PLC210CoolingClient
    {
        public const int CircuitCount = 7;

        public static readonly string[] CircuitNames =
        {
            "Stage", "Chamber", "MW Head", "MW Power", "Tuner", "Internal", "External"
        };

        public const int InternalCircuit = 5;

        public const int StageCircuit = 0;

        public sealed class State
        {
            public bool Connected;
            public string StatusText;
            public DateTime UpdatedAt;

            public double[] TempC = new double[CircuitCount];
            public double[] FlowLpm = new double[CircuitCount];
            public bool[] TempValid = new bool[CircuitCount];
            public bool[] FlowValid = new bool[CircuitCount];

            public double WaterPressureBar;
            public bool WaterPressureValid;

            public double CdaPressureBar;
            public bool CdaPressureValid;

            public State Clone()
            {
                State copy = (State)MemberwiseClone();
                copy.TempC = (double[])TempC.Clone();
                copy.FlowLpm = (double[])FlowLpm.Clone();
                copy.TempValid = (bool[])TempValid.Clone();
                copy.FlowValid = (bool[])FlowValid.Clone();
                return copy;
            }
        }

        private const byte UnitId = 1;
        private const ushort BlockStart = 206;
        private const ushort BlockCount = 34;

        private const int TempOffset = 0;
        private const int FlowOffset = 14;
        private const int WaterOffset = 28;
        private const int CdaOffset = 30;
        private const int TempMaskOffset = 32;
        private const int FlowMaskOffset = 33;

        private const double Scale = 1000.0;

        private static readonly object Sync = new object();

        private static string _host = "192.168.1.10";
        private static int _port = 502;
        private static bool _running;
        private static int _errConnCount;
        private static bool[] _tempAnswered;
        private static bool[] _flowAnswered;
        private static bool _waterPressureAnswered = true;
        private static bool _cdaPressureAnswered = true;
        private static bool _forceReconnect;
        private static Thread _worker;
        private static TcpClient _tcpClient;
        private static IModbusMaster _master;
        private static State _state = new State
        {
            StatusText = "PLC210 cooling inputs disabled"
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
                    Name = "PLC210 Cooling Modbus TCP"
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
                    StatusText = "PLC210 cooling inputs disconnected",
                    UpdatedAt = DateTime.Now
                };
            }
        }

        public static State GetState()
        {
            lock (Sync)
                return _state.Clone();
        }

        private static void LogDeviceFaults(State plcState)
        {
            List<string> lost = new List<string>();

            if (_tempAnswered == null)
            {
                _tempAnswered = new bool[CircuitCount];
                _flowAnswered = new bool[CircuitCount];
                for (int i = 0; i < CircuitCount; i++)
                {
                    _tempAnswered[i] = true;
                    _flowAnswered[i] = true;
                }
            }

            for (int i = 0; i < CircuitCount; i++)
            {
                if (_tempAnswered[i] && !plcState.TempValid[i])
                    lost.Add(CircuitNames[i] + " temp");
                if (_flowAnswered[i] && !plcState.FlowValid[i])
                    lost.Add(CircuitNames[i] + " flow");
                _tempAnswered[i] = plcState.TempValid[i];
                _flowAnswered[i] = plcState.FlowValid[i];
            }

            if (_waterPressureAnswered && !plcState.WaterPressureValid)
                lost.Add("water pressure");
            if (_cdaPressureAnswered && !plcState.CdaPressureValid)
                lost.Add("CDA pressure");
            _waterPressureAnswered = plcState.WaterPressureValid;
            _cdaPressureAnswered = plcState.CdaPressureValid;

            if (lost.Count > 0)
                Logger.WriteError(new Exception("Cooling: no valid reading: " + string.Join(", ", lost.ToArray())));
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

                    ushort[] block = _master.ReadHoldingRegisters(UnitId, BlockStart, BlockCount);

                    ushort tempMask = block[TempMaskOffset];
                    ushort flowMask = block[FlowMaskOffset];

                    State plcState = new State
                    {
                        Connected = true,
                        StatusText = "PLC210 cooling inputs connected",
                        UpdatedAt = DateTime.Now,
                        WaterPressureBar = ReadFixed(block, WaterOffset),
                        WaterPressureValid = (flowMask & 0x0200) != 0,
                        CdaPressureBar = ReadFixed(block, CdaOffset),
                        CdaPressureValid = (flowMask & 0x0100) != 0
                    };

                    for (int i = 0; i < CircuitCount; i++)
                    {
                        plcState.TempC[i] = ReadFixed(block, TempOffset + 2 * i);
                        plcState.FlowLpm[i] = ReadFixed(block, FlowOffset + 2 * i);
                        plcState.TempValid[i] = (tempMask & (1 << i)) != 0;
                        plcState.FlowValid[i] = (flowMask & (1 << i)) != 0;
                    }

                    lock (Sync)
                        _state = plcState;

                    LogDeviceFaults(plcState);
                    _errConnCount = 0;
                }
                catch (Exception ex)
                {
                    _errConnCount++;
                    if (_errConnCount < 2)
                        Logger.WriteError(new Exception("Cooling: " + ex.Message));

                    Disconnect();
                    lock (Sync)
                    {
                        _state = new State
                        {
                            Connected = false,
                            StatusText = "PLC210 cooling inputs not connected: " + ShortMessage(ex),
                            UpdatedAt = DateTime.Now
                        };
                    }
                }

                Thread.Sleep(1000);
            }
        }

        public static double CircuitPowerWatt(State state, int circuit)
        {
            if (state == null || circuit < 0 || circuit >= CircuitCount)
                return 0;

            if (!state.FlowValid[circuit] || !state.TempValid[circuit] || !state.TempValid[InternalCircuit])
                return 0;

            double deltaT = state.TempC[circuit] - state.TempC[InternalCircuit];
            return 1.17 * (state.FlowLpm[circuit] * 60) * deltaT;
        }

        private static double ReadFixed(ushort[] registers, int index)
        {
            unchecked
            {
                int value = (int)(((uint)registers[index + 1] << 16) | registers[index]);
                return value / Scale;
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
                    "Modbus exception {0} {1}, func {2}, unit {3}; check CODESYS registers 206..239",
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
