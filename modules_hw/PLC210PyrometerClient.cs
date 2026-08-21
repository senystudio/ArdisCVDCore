using NModbus;
using System;
using System.Net.Sockets;
using System.Threading;

namespace ArdisCVDCore.modules_hw
{
    /// <summary>
    /// Reads the 2 Kelvin pyrometers (RXT-PRO, Smart-Spectrum) published by PRG_Pyrometers
    /// at awHolding[160..191]. Both talk Modbus RTU to the PLC over the same RS485-2 bus as
    /// the RRG-20 gas regulators (slave IDs 7/8); the HMI only reads the prepared values over
    /// this Modbus TCP connection, same [PLC210] endpoint as the other PLC210*Client classes,
    /// each with their own socket.
    /// </summary>
    public static class PLC210PyrometerClient
    {
        public sealed class PyrometerReading
        {
            public double Ch1Temp;
            public double Ch2Temp;
            public double RatioTemp;
            public bool Valid;
            public bool Ch1Overload;
            public bool Ch2Overload;
            public bool CommFault;
            public ushort RawDeviceStatus;
            // Commissioning-only diagnostics from FB_PyrometerModbusMaster (awHolding[168]/[169],
            // see PRG_Pyrometers.st): last driver fault code and raw byte count received on the
            // last poll, before any CRC/address validation -- 0 bytes means the wire is silent.
            public ushort LastFaultCode;
            public ushort LastRxSize;

            public PyrometerReading Clone()
            {
                return (PyrometerReading)MemberwiseClone();
            }
        }

        public sealed class State
        {
            public bool Connected;
            public string StatusText;
            public DateTime UpdatedAt;
            public PyrometerReading Rxt;
            public PyrometerReading Smart;

            public State Clone()
            {
                return new State
                {
                    Connected = Connected,
                    StatusText = StatusText,
                    UpdatedAt = UpdatedAt,
                    Rxt = Rxt.Clone(),
                    Smart = Smart.Clone()
                };
            }
        }

        // Same per-model thresholds as ArdisCVDMaster's two driver classes:
        // Pyrometr_Euromix_Kelvin_1ch_modbus (RXT-PRO, _lowLimit = 200) and
        // Pyrometr_Euromix_Kelvin_modbus (Smart, _lowLimit = 500).
        public const double RxtLowLimit = 200.0;
        public const string RxtLowLimitLabel = "<200";
        public const double SmartLowLimit = 500.0;
        public const string SmartLowLimitLabel = "<500";

        private const byte UnitId = 1;
        private const ushort RegisterStart = 160;
        private const ushort RegisterCount = 32;
        private const int SmartOffset = 16;
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
            StatusText = "PLC210 pyrometers disabled",
            Rxt = new PyrometerReading(),
            Smart = new PyrometerReading()
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
                    Name = "PLC210 Pyrometer Modbus TCP"
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
                    plcState.Connected = true;
                    plcState.StatusText = "PLC210 pyrometers connected";
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
                            StatusText = "PLC210 pyrometers not connected: " + ShortMessage(ex),
                            UpdatedAt = DateTime.Now,
                            Rxt = new PyrometerReading(),
                            Smart = new PyrometerReading()
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

            return new State
            {
                Rxt = ParseReading(registers, 0),
                Smart = ParseReading(registers, SmartOffset)
            };
        }

        private static PyrometerReading ParseReading(ushort[] registers, int baseIndex)
        {
            ushort status = registers[baseIndex + 6];
            return new PyrometerReading
            {
                Ch1Temp = ReadFixed(registers, baseIndex + 0),
                Ch2Temp = ReadFixed(registers, baseIndex + 2),
                RatioTemp = ReadFixed(registers, baseIndex + 4),
                LastFaultCode = registers[baseIndex + 8],
                LastRxSize = registers[baseIndex + 9],
                Valid = (status & 0x0001) != 0,
                Ch1Overload = (status & 0x0002) != 0,
                Ch2Overload = (status & 0x0004) != 0,
                CommFault = (status & 0x0008) != 0,
                RawDeviceStatus = registers[baseIndex + 7]
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
                    "Modbus exception {0} {1}, func {2}, unit {3}; check CODESYS registers 160..191",
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
