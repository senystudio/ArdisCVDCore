using NModbus;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;

namespace ArdisCVDCore.modules_hw
{
    public static class PLC210DebugClient
    {
        public sealed class State
        {
            public bool Connected;
            public string StatusText;
            public DateTime UpdatedAt;
            public ushort[] Registers;
            public ushort CommandSent;
            public ushort CommandEcho;
            public string LastCommandName;
            public DateTime LastCommandAt;

            public bool LastCommandDone
            {
                get { return CommandSent != 0 && CommandEcho == CommandSent; }
            }

            public State Clone()
            {
                State copy = (State)MemberwiseClone();
                copy.Registers = Registers == null ? null : (ushort[])Registers.Clone();
                return copy;
            }
        }

        public const int RegisterTotal = 296;

        public const byte CmdResetAllMfc = 1;
        public const byte CmdResetMfcFirst = 2;
        public const byte CmdReinitGas = 20;
        public const byte CmdReboot = 30;

        public static readonly string[] MfcDevices = { "MFC_H2", "MFC_CH4", "MFC_O2", "MFC_N2", "MFC_Ar", "MFC_H22" };

        private const byte UnitId = 1;
        private const ushort CommandRegister = 148;
        private const ushort EchoRegister = 149;
        private const int ChunkSize = 100;
        private const int PollMs = 500;

        private static readonly object Sync = new object();
        private static readonly Queue<KeyValuePair<byte, string>> _commands = new Queue<KeyValuePair<byte, string>>();

        private static string _host = "192.168.1.10";
        private static int _port = 502;
        private static int _users;
        private static bool _running;
        private static byte _sequence;
        private static bool _sequenceSeeded;
        private static Thread _worker;
        private static TcpClient _tcpClient;
        private static IModbusMaster _master;
        private static State _state = new State { StatusText = "Debug link stopped" };

        public static void Start(string host, int port)
        {
            lock (Sync)
            {
                _host = string.IsNullOrWhiteSpace(host) ? _host : host;
                _port = port > 0 ? port : _port;
                _users++;

                if (_running)
                    return;

                _running = true;
                _worker = new Thread(WorkerLoop)
                {
                    IsBackground = true,
                    Name = "PLC210 Debug Modbus TCP"
                };
                _worker.Start();
            }
        }

        public static void Stop()
        {
            Thread worker;
            lock (Sync)
            {
                if (_users > 0)
                    _users--;
                if (_users > 0 || !_running)
                    return;

                _running = false;
                worker = _worker;
            }

            if (worker != null && worker.IsAlive)
                worker.Join(1500);

            Disconnect();

            lock (Sync)
            {
                _commands.Clear();
                _state = new State { StatusText = "Debug link stopped", UpdatedAt = DateTime.Now };
            }
        }

        public static State GetState()
        {
            lock (Sync)
                return _state.Clone();
        }

        public static void Send(byte code, string name)
        {
            lock (Sync)
                _commands.Enqueue(new KeyValuePair<byte, string>(code, name));
        }

        private static void WorkerLoop()
        {
            while (true)
            {
                string host;
                int port;
                KeyValuePair<byte, string>? command = null;

                lock (Sync)
                {
                    if (!_running)
                        break;

                    host = _host;
                    port = _port;
                    if (_commands.Count > 0)
                        command = _commands.Peek();
                }

                try
                {
                    EnsureConnected(host, port);

                    if (!_sequenceSeeded)
                    {
                        _sequence = (byte)(_master.ReadHoldingRegisters(UnitId, CommandRegister, 1)[0] >> 8);
                        _sequenceSeeded = true;
                    }

                    ushort sentWord = 0;
                    if (command.HasValue)
                    {
                        byte sequence = NextSequence();
                        sentWord = (ushort)((sequence << 8) | command.Value.Key);
                        _master.WriteSingleRegister(UnitId, CommandRegister, sentWord);

                        lock (Sync)
                            _commands.Dequeue();
                    }

                    ushort[] registers = new ushort[RegisterTotal];
                    for (int start = 0; start < RegisterTotal; start += ChunkSize)
                    {
                        ushort count = (ushort)Math.Min(ChunkSize, RegisterTotal - start);
                        ushort[] chunk = _master.ReadHoldingRegisters(UnitId, (ushort)start, count);
                        Array.Copy(chunk, 0, registers, start, count);
                    }

                    lock (Sync)
                    {
                        State next = _state.Clone();
                        next.Connected = true;
                        next.StatusText = "Debug link connected";
                        next.UpdatedAt = DateTime.Now;
                        next.Registers = registers;
                        next.CommandEcho = registers[EchoRegister];
                        if (command.HasValue)
                        {
                            next.CommandSent = sentWord;
                            next.LastCommandName = command.Value.Value;
                            next.LastCommandAt = DateTime.Now;
                        }
                        _state = next;
                    }
                }
                catch (Exception ex)
                {
                    Disconnect();
                    lock (Sync)
                    {
                        State next = _state.Clone();
                        next.Connected = false;
                        next.StatusText = "Debug link not connected: " + ShortMessage(ex);
                        next.UpdatedAt = DateTime.Now;
                        _state = next;
                    }
                }

                Thread.Sleep(PollMs);
            }
        }

        private static byte NextSequence()
        {
            _sequence++;
            if (_sequence == 0)
                _sequence = 1;
            return _sequence;
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
            _sequenceSeeded = false;
        }

        private static string ShortMessage(Exception ex)
        {
            if (ex == null || string.IsNullOrWhiteSpace(ex.Message))
                return "communication error";

            string message = ex.Message.Replace("\r", " ").Replace("\n", " ");
            return message.Length <= 80 ? message : message.Substring(0, 80);
        }
    }
}
