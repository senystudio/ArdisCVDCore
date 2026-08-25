using NModbus;
using System;
using System.Net.Sockets;
using System.Threading;

namespace ArdisCVDCore.modules_hw
{
    /// <summary>
    /// Reads/writes the 6 РРГ-20 gas regulator channels published by PRG_GasFlow
    /// at awHolding[64..99]. The PLC itself talks Modbus RTU to the regulators
    /// over RS485-2; the HMI only exchanges setpoints/measured values with the
    /// PLC over this Modbus TCP connection, same [PLC210] endpoint as
    /// PLC210PidClient/PLC210ThyracontClient use, each with their own socket.
    /// </summary>
    public static class PLC210GasFlowClient
    {
        public sealed class ChannelState
        {
            public string GasName;
            public double FullScaleSccm;
            public double SetpointSccm;
            public double MeasuredSccm;
            public bool Regulating;
            public bool FaultActive;
            public bool CloseConfirmed;
            public bool ClosedByDisable;
            public int FaultCode;

            // awHolding[96] bit i: PRG_GasFlow.st reading MFC_*.xError straight
            // off the Modbus device in the CODESYS tree -- this one regulator is
            // not answering on RS485. MeasuredSccm above is whatever it last
            // said and is never cleared, so it goes stale rather than to zero:
            // nothing may believe the reading while this is set.
            public bool SlaveError;

            public ChannelState Clone()
            {
                return (ChannelState)MemberwiseClone();
            }
        }

        public sealed class State
        {
            public bool Connected;
            public string StatusText;
            public DateTime UpdatedAt;
            public bool BusOpen;
            public bool AnyFault;
            public bool AllFault;
            public bool SubsystemEnabled;
            public uint SweepCounter;
            public ChannelState[] Channels;

            // Temporary diagnostics while commissioning the native Modbus_COM
            // master: awHolding[96..99], see PRG_GasFlow.st.
            public ushort DiagSlaveErrorMask;
            public ushort DiagH2MeasuredRaw;
            public int DiagH2InitState;
            public int DiagCh4InitState;

            public State Clone()
            {
                ChannelState[] channelsCopy = new ChannelState[Channels.Length];
                for (int i = 0; i < Channels.Length; i++)
                    channelsCopy[i] = Channels[i].Clone();

                return new State
                {
                    Connected = Connected,
                    StatusText = StatusText,
                    UpdatedAt = UpdatedAt,
                    BusOpen = BusOpen,
                    AnyFault = AnyFault,
                    AllFault = AllFault,
                    SubsystemEnabled = SubsystemEnabled,
                    SweepCounter = SweepCounter,
                    Channels = channelsCopy,
                    DiagSlaveErrorMask = DiagSlaveErrorMask,
                    DiagH2MeasuredRaw = DiagH2MeasuredRaw,
                    DiagH2InitState = DiagH2InitState,
                    DiagCh4InitState = DiagCh4InitState
                };
            }
        }

        private const byte UnitId = 1;
        private const ushort RegisterStart = 64;
        private const ushort RegisterCount = 36;
        private const ushort ChannelBlockBase = 66;
        private const int ChannelBlockStride = 5;
        private const double Scale = 1000.0;

        public static readonly string[] GasNames = { "H2", "CH4", "N2", "O2", "Ar", "H2 (2)" };
        public static readonly double[] FullScaleSccm = { 1000.0, 100.0, 1000.0, 50.0, 10.0, 1000.0 };

        private static readonly object Sync = new object();
        private static readonly double[] _pendingSetpoint = CreateEmptyPending();

        private static string _host = "192.168.1.10";
        private static int _port = 502;
        private static bool _running;
        private static bool _forceReconnect;
        private static Thread _worker;
        private static TcpClient _tcpClient;
        private static IModbusMaster _master;
        private static State _state = new State
        {
            StatusText = "PLC210 gas regulators disabled",
            Channels = BuildDefaultChannels()
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
                    Name = "PLC210 GasFlow Modbus TCP"
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

        public static void RequestSetpoint(int channelIndex, double sccm)
        {
            lock (Sync)
                _pendingSetpoint[channelIndex] = Clamp(sccm, 0, FullScaleSccm[channelIndex]);
        }

        private static void WorkerLoop()
        {
            while (true)
            {
                string host;
                int port;
                bool reconnect;
                double[] pending = null;

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

                    lock (Sync)
                    {
                        pending = (double[])_pendingSetpoint.Clone();
                        for (int i = 0; i < pending.Length; i++)
                            _pendingSetpoint[i] = double.NaN;
                    }

                    for (int i = 0; i < pending.Length; i++)
                    {
                        if (!double.IsNaN(pending[i]))
                            _master.WriteMultipleRegisters(UnitId, (ushort)(ChannelBlockBase + i * ChannelBlockStride), ToFixedWords(pending[i]));
                    }

                    ushort[] registers = _master.ReadHoldingRegisters(UnitId, RegisterStart, RegisterCount);
                    State plcState = ParseRegisters(registers);
                    plcState.Connected = true;
                    plcState.StatusText = "PLC210 gas regulators connected";
                    plcState.UpdatedAt = DateTime.Now;

                    lock (Sync)
                        _state = plcState;
                }
                catch (Exception ex)
                {
                    // Re-queue only the writes that didn't make it out this cycle, so a
                    // transient TCP hiccup doesn't silently drop an operator's SET click.
                    if (pending != null)
                    {
                        lock (Sync)
                        {
                            for (int i = 0; i < pending.Length; i++)
                                if (!double.IsNaN(pending[i]) && double.IsNaN(_pendingSetpoint[i]))
                                    _pendingSetpoint[i] = pending[i];
                        }
                    }

                    Disconnect();
                    lock (Sync)
                    {
                        _state = new State
                        {
                            Connected = false,
                            StatusText = "PLC210 gas regulators not connected: " + ShortMessage(ex),
                            UpdatedAt = DateTime.Now,
                            Channels = _state.Channels
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

            ushort busStatus = registers[0];
            uint sweepCounter = registers[1];
            ushort slaveErrorMask = registers[32];

            ChannelState[] channels = new ChannelState[GasNames.Length];
            for (int i = 0; i < channels.Length; i++)
            {
                int baseIndex = (ChannelBlockBase - RegisterStart) + i * ChannelBlockStride;
                double setpoint = ReadFixed(registers, baseIndex + 0);
                double measured = ReadFixed(registers, baseIndex + 2);
                ushort status = registers[baseIndex + 4];

                channels[i] = new ChannelState
                {
                    GasName = GasNames[i],
                    FullScaleSccm = FullScaleSccm[i],
                    SetpointSccm = setpoint,
                    MeasuredSccm = measured,
                    Regulating = (status & 0x0001) != 0,
                    FaultActive = (status & 0x0002) != 0,
                    CloseConfirmed = (status & 0x0004) != 0,
                    ClosedByDisable = (status & 0x0008) != 0,
                    FaultCode = status >> 8,
                    SlaveError = (slaveErrorMask & (1 << i)) != 0
                };
            }

            return new State
            {
                BusOpen = (busStatus & 0x0001) != 0,
                AnyFault = (busStatus & 0x0002) != 0,
                AllFault = (busStatus & 0x0004) != 0,
                SubsystemEnabled = (busStatus & 0x0008) != 0,
                SweepCounter = sweepCounter,
                Channels = channels,
                DiagSlaveErrorMask = slaveErrorMask,
                DiagH2MeasuredRaw = registers[33],
                DiagH2InitState = (short)registers[34],
                DiagCh4InitState = (short)registers[35]
            };
        }

        private static ChannelState[] BuildDefaultChannels()
        {
            ChannelState[] channels = new ChannelState[GasNames.Length];
            for (int i = 0; i < channels.Length; i++)
            {
                channels[i] = new ChannelState
                {
                    GasName = GasNames[i],
                    FullScaleSccm = FullScaleSccm[i]
                };
            }
            return channels;
        }

        private static double[] CreateEmptyPending()
        {
            double[] pending = new double[GasNames.Length];
            for (int i = 0; i < pending.Length; i++)
                pending[i] = double.NaN;
            return pending;
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

        private static ushort[] ToFixedWords(double sccm)
        {
            double scaled = Math.Round(sccm * Scale);
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
                    "Modbus exception {0} {1}, func {2}, unit {3}; check CODESYS registers 64..99",
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
