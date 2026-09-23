using ArdisCVDCore.modules_hw;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ArdisCVDCore
{
    public sealed class ProcessSample
    {
        public DateTime MeasurementTime;
        public double[] CoolingTemp = new double[PLC210CoolingClient.CircuitCount];
        public double[] CoolingFlow = new double[PLC210CoolingClient.CircuitCount];
        public double SampleTempCh1;
        public double SampleTempCh2;
        public double SampleTempSum;
        public double SetIncPowerW;
        public double IncPowerW;
        public double ReflPowerW;
        public byte MWControl;
        public byte MWError1;
        public byte MWError2;
        public double WaterPressure;
        public double CDAPressure;
        public double SetChamberPressure;
        public double ChamberPressure;
        public double HiVacChamberPressure;
        public bool HiVacPumpEn;
        public double HiVacPumpSpeed;
        public double[] GasSet = new double[PLC210GasFlowClient.GasNames.Length];
        public double[] GasFlow = new double[PLC210GasFlowClient.GasNames.Length];
        public bool[] GasValves = new bool[PLC210GasValveClient.ValveCount];
        public bool[] VacValves = new bool[PLC210VacuumClient.ValveCount];
        public bool[] Pumps = new bool[8];
        public bool[] DInputs = new bool[8];
        public byte[] WarningsInput = new byte[8];
        public byte[] ErrorsInput = new byte[8];

        public static ProcessSample Capture(DateTime now)
        {
            ProcessSample s = new ProcessSample { MeasurementTime = now };

            PLC210CoolingClient.State cooling = PLC210CoolingClient.GetState();
            for (int i = 0; i < PLC210CoolingClient.CircuitCount; i++)
            {
                s.CoolingTemp[i] = cooling.TempC[i];
                s.CoolingFlow[i] = cooling.FlowLpm[i];
            }
            s.WaterPressure = cooling.WaterPressureBar;
            s.CDAPressure = cooling.CdaPressureBar;

            PLC210PyrometerClient.PyrometerReading pyro = MainForm.SelectActivePyrometer(PLC210PyrometerClient.GetState());
            if (pyro != null)
            {
                s.SampleTempCh1 = pyro.Ch1Temp;
                s.SampleTempCh2 = pyro.Ch2Temp;
                s.SampleTempSum = pyro.RatioTemp;
            }

            PLC210MicrowaveClient.State mw = PLC210MicrowaveClient.GetState();
            s.SetIncPowerW = mw.SetpointKw * 1000.0;
            s.IncPowerW = mw.IncidentKw * 1000.0;
            s.ReflPowerW = mw.ReflectedKw * 1000.0;
            s.MWControl = (byte)((mw.PreheatOn ? 0x01 : 0) | (mw.MicrowaveOn ? 0x02 : 0));
            s.MWError1 = (byte)(mw.FaultReasonBits & 0xFF);
            s.MWError2 = (byte)(mw.FaultReasonBits >> 8);

            s.SetChamberPressure = ChamberPid.Setpoint;
            s.ChamberPressure = PLC210PidClient.GetState().PlcPressureTorr;
            s.HiVacChamberPressure = PLC210ThyracontClient.GetState().PressureTorr;

            PLC210TurboPumpClient.State turbo = PLC210TurboPumpClient.GetState();
            s.HiVacPumpEn = turbo.Working;
            s.HiVacPumpSpeed = turbo.SpeedHz;

            PLC210GasFlowClient.State gas = PLC210GasFlowClient.GetState();
            for (int i = 0; i < s.GasSet.Length && gas.Channels != null && i < gas.Channels.Length; i++)
            {
                s.GasSet[i] = gas.Channels[i].SetpointSccm;
                s.GasFlow[i] = gas.Channels[i].MeasuredSccm;
            }

            PLC210GasValveClient.State gasValves = PLC210GasValveClient.GetState();
            Array.Copy(gasValves.ValveOn, s.GasValves, s.GasValves.Length);

            PLC210VacuumClient.State vacuum = PLC210VacuumClient.GetState();
            Array.Copy(vacuum.ValveOn, s.VacValves, s.VacValves.Length);

            s.Pumps[0] = vacuum.WaterPumpOn;
            s.Pumps[1] = vacuum.ForeVacPumpOn;
            s.Pumps[2] = turbo.Working;
            s.Pumps[3] = mw.MicrowaveOn;
            s.Pumps[4] = mw.PreheatOn;

            ushort inputs = PLC210PidClient.GetState().DiscreteInputs;
            for (int i = 0; i < s.DInputs.Length; i++)
                s.DInputs[i] = (inputs & (1 << i)) != 0;

            PLC210AlarmClient.State alarms = PLC210AlarmClient.GetState();
            s.WarningsInput = BitConverter.GetBytes(alarms.AlarmMask);
            s.ErrorsInput = BitConverter.GetBytes(alarms.AbortMask);

            return s;
        }
    }

    public static class ProcessLogger
    {
        public const int MaxSamples = 3600;

        private static readonly object Sync = new object();
        private static readonly byte[] Key = Encoding.UTF8.GetBytes("Ardis");
        private static readonly string _hiddenLog = Path.Combine(Application.StartupPath, "configBackup.bin");
        private static readonly string _logDirectory = Path.Combine(Application.StartupPath, "ProcessLogs");

        public static DateTime StartTime = DateTime.Now;
        public static int LogIndex;
        public static readonly List<ProcessSample> Samples = new List<ProcessSample>();

        private static Dictionary<string, TimeStruct> _currentSession;
        private static Dictionary<string, TimeStruct> _lastSession;
        private static bool _isNewLogFile;
        private static DateTime _createdTime;

        public sealed class TimeStruct
        {
            public DateTime Started;
            public TimeSpan Accumulated;
            public bool Running;

            public TimeStruct(DateTime started, TimeSpan accumulated)
            {
                Started = started;
                Accumulated = accumulated;
                Running = false;
            }
        }

        public static void Init()
        {
            _currentSession = new Dictionary<string, TimeStruct>(1);
            _lastSession = new Dictionary<string, TimeStruct>(1);
            _currentSession.Add("MWPower", new TimeStruct(DateTime.Now, TimeSpan.Zero));
            _lastSession.Add("MWPower", new TimeStruct(DateTime.Now, TimeSpan.Zero));
            LogIndex = 0;
        }

        public static void OpTimeLogging(bool on, params string[] par)
        {
            foreach (string s in par)
            {
                if (_currentSession[s].Running == false & on)
                {
                    _currentSession[s] = new TimeStruct(DateTime.Now, _currentSession[s].Accumulated);
                    _currentSession[s].Running = true;
                }
                else if (_currentSession[s].Running == true & on == false)
                {
                    _currentSession[s] = new TimeStruct(DateTime.Now, (DateTime.Now - _currentSession[s].Started) + _currentSession[s].Accumulated);
                    _currentSession[s].Running = false;
                }
            }
        }

        public static byte[] XorEncryptDecrypt(byte[] data, byte[] key)
        {
            byte[] result = new byte[data.Length];
            for (int i = 0; i < data.Length; i++)
                result[i] = (byte)(data[i] ^ key[i % key.Length]);
            return result;
        }

        public static void LogBinary()
        {
            try
            {
                if (!File.Exists(_hiddenLog))
                {
                    File.Create(_hiddenLog).Close();
                    _isNewLogFile = true;
                }

                ReadTimeBinary();

                File.SetAttributes(_hiddenLog, FileAttributes.Normal);

                lock (Sync)
                {
                    using (FileStream fs = new FileStream(_hiddenLog, FileMode.Create, FileAccess.Write))
                    using (BinaryWriter writer = new BinaryWriter(fs))
                    {
                        string text = "";
                        if (_isNewLogFile)
                            _createdTime = DateTime.Now;

                        text += _createdTime.ToString() + "\n";
                        foreach (string p in _currentSession.Keys)
                        {
                            text += p + "|" + (_currentSession[p].Accumulated + _lastSession[p].Accumulated).ToString() + '\n';
                            _currentSession[p].Accumulated = new TimeSpan();
                        }

                        byte[] encryptedBytes = XorEncryptDecrypt(Encoding.UTF8.GetBytes(text), Key);
                        writer.Write(encryptedBytes.Length);
                        writer.Write(encryptedBytes);
                    }
                }

                File.SetAttributes(_hiddenLog, FileAttributes.Hidden);
            }
            catch (Exception e)
            {
                Logger.WriteError(new Exception("ProcessLogger: " + e.Message));
            }
        }

        private static void ReadTimeBinary()
        {
            if (_isNewLogFile)
                return;

            try
            {
                using (FileStream fs = new FileStream(_hiddenLog, FileMode.Open, FileAccess.Read))
                using (BinaryReader reader = new BinaryReader(fs))
                {
                    bool createdTimeRead = false;
                    int length = reader.ReadInt32();
                    byte[] readEncrypted = reader.ReadBytes(length);
                    string restoredText = Encoding.UTF8.GetString(XorEncryptDecrypt(readEncrypted, Key));
                    Dictionary<string, string> dictionary = new Dictionary<string, string>();
                    foreach (string part in restoredText.Split('\n'))
                    {
                        if (!createdTimeRead && DateTime.TryParse(part, out _createdTime))
                            createdTimeRead = true;

                        string trimmedPart = part.Trim();
                        if (string.IsNullOrEmpty(trimmedPart))
                            continue;
                        int index = trimmedPart.IndexOf('|');
                        if (index == -1)
                            continue;
                        dictionary[trimmedPart.Substring(0, index).Trim()] = trimmedPart.Substring(index + 1).Trim();
                    }

                    foreach (string p in _currentSession.Keys)
                    {
                        string stored;
                        TimeSpan a;
                        if (dictionary.TryGetValue(p, out stored) && TimeSpan.TryParse(stored, out a))
                            _lastSession[p] = new TimeStruct(_currentSession[p].Started, a);
                    }
                }
            }
            catch
            {
            }
        }

        public static void BeginSession()
        {
            Samples.Clear();
            StartTime = DateTime.Now;
            LogIndex = 0;
        }

        public static void Record(ProcessSample sample)
        {
            Samples.Add(sample);
            if (Samples.Count > MaxSamples)
                Samples.RemoveAt(0);

            if (sample.MeasurementTime.Minute == 0 & sample.MeasurementTime.Second == 0)
                CreateLogFile();
        }

        public static void CreateLogFile()
        {
            List<ProcessSample> copyList = new List<ProcessSample>(Samples);
            if (copyList.Count == 0)
                return;

            WriteFile(copyList, 0);
            LogIndex++;
        }

        public static Task CreateLogFileEnded()
        {
            List<ProcessSample> copyList = new List<ProcessSample>(Samples);
            if (copyList.Count == 0)
                return null;

            Samples.Clear();

            int zero = 0;
            if (copyList[0].MeasurementTime.Minute != 0 & copyList[0].MeasurementTime.Second != 0)
            {
                for (int i = 0; i < copyList.Count; i++)
                {
                    if (copyList[i].MeasurementTime.Minute == 0 & copyList[i].MeasurementTime.Second == 0)
                        zero = i;
                }
            }

            Task write = WriteFile(copyList, zero);
            LogIndex++;
            return write;
        }

        private static Task WriteFile(List<ProcessSample> copyList, int from)
        {
            string filename = Path.Combine(_logDirectory, "Ardis_LogFile  " + LogIndex.ToString() + " " + StartTime.Day.ToString() + "." + StartTime.Month.ToString()
                + "." + StartTime.Year.ToString() + " " + StartTime.Hour.ToString() + "."
                + StartTime.Minute.ToString() + "." + StartTime.Second.ToString());

            StringWriter sw = new StringWriter(NumberFormatInfo.InvariantInfo);
            sw.WriteLine(Header());
            for (int i = from; i < copyList.Count; i++)
                sw.WriteLine(Row(copyList[i]));

            try
            {
                if (!Directory.Exists(_logDirectory))
                    Directory.CreateDirectory(_logDirectory);

                TextWriter tw = new StreamWriter(filename + ".dat", false);
                return Task.Run(() =>
                {
                    try
                    {
                        tw.Write(sw);
                    }
                    catch (Exception e)
                    {
                        Logger.WriteError(new Exception("ProcessLogger: " + e.Message));
                    }
                    finally
                    {
                        tw.Close();
                    }
                });
            }
            catch (Exception e)
            {
                Logger.WriteError(new Exception("ProcessLogger: " + e.Message));
                return null;
            }
        }

        private static string Header()
        {
            return "Time"
                + "\t" + "StageTemp" + "\t" + "Chamber_Temp" + "\t" + "MWHead_Temp" + "\t" + "MWPower_Temp" + "\t" + "Tuner_Temp" + "\t" + "Inner_Temp" + "\t" + "Outer_Temp" + "\t" + "Sample Temp_ch1" + "\t" + "Sample Temp_ch2" + "\t" + "Sample Temp_sum"
                + "\t" + "Stage_Flow" + "\t" + "Chamber_Flow" + "\t" + "MWHead_Flow" + "\t" + "MWPower_Flow" + "\t" + "Tuner_Flow" + "\t" + "Inner_Flow" + "\t" + "Outer_Flow"
                + "\t" + "SetIncPower" + "\t" + "IncPower" + "\t" + "ReflPower" + "\t" + "ControlByte" + "\t" + "Error_1" + "\t" + "Error_2"
                + "\t" + "WaterPressure"
                + "\t" + "CDAPressure"
                + "\t" + "SetChamberPressure" + "\t" + "ChamberPressure" + "\t" + "HiVacChamberPressure"
                + "\t" + "HiVacPumpEn" + "\t" + "HiVacPumpSpeed"
                + "\t" + "SetH2" + "\t" + "H2Flow"
                + "\t" + "SetN2" + "\t" + "N2Flow"
                + "\t" + "SetCH4" + "\t" + "CH4Flow"
                + "\t" + "SetO2" + "\t" + "O2Flow"
                + "\t" + "SetAr" + "\t" + "ArFlow"
                + "\t" + "SetH2_2" + "\t" + "H2_2Flow"
                + "\t" + "DOutput 1" + "\t" + "DOutput 2" + "\t" + "DOutput 3" + "\t" + "DInput 1"
                + "\t" + "Warnings"
                + "\t" + "Errors";
        }

        private static readonly string[] GasColumnOrder = { "H2", "N2", "CH4", "O2", "Ar", "H2 (2)" };

        private static string Row(ProcessSample s)
        {
            StringBuilder row = new StringBuilder();
            row.Append(s.MeasurementTime.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture));

            for (int i = 0; i < s.CoolingTemp.Length; i++)
                row.Append("\t").Append(s.CoolingTemp[i].ToString("F2", CultureInfo.InvariantCulture));
            row.Append("\t").Append(s.SampleTempCh1.ToString("F2", CultureInfo.InvariantCulture));
            row.Append("\t").Append(s.SampleTempCh2.ToString("F2", CultureInfo.InvariantCulture));
            row.Append("\t").Append(s.SampleTempSum.ToString("F2", CultureInfo.InvariantCulture));
            for (int i = 0; i < s.CoolingFlow.Length; i++)
                row.Append("\t").Append(s.CoolingFlow[i].ToString("F2", CultureInfo.InvariantCulture));

            row.Append("\t").Append(s.SetIncPowerW.ToString("F0", CultureInfo.InvariantCulture));
            row.Append("\t").Append(s.IncPowerW.ToString("F0", CultureInfo.InvariantCulture));
            row.Append("\t").Append(s.ReflPowerW.ToString("F0", CultureInfo.InvariantCulture));
            row.Append("\t").Append(s.MWControl.ToString("X2", CultureInfo.InvariantCulture));
            row.Append("\t").Append(s.MWError1.ToString("X2", CultureInfo.InvariantCulture));
            row.Append("\t").Append(s.MWError2.ToString("X2", CultureInfo.InvariantCulture));

            row.Append("\t").Append(s.WaterPressure.ToString("F1", CultureInfo.InvariantCulture));
            row.Append("\t").Append(s.CDAPressure.ToString("F1", CultureInfo.InvariantCulture));

            row.Append("\t").Append(s.SetChamberPressure.ToString("F0", CultureInfo.InvariantCulture));
            row.Append("\t").Append(s.ChamberPressure.ToString("F1", CultureInfo.InvariantCulture));
            row.Append("\t").Append(s.HiVacChamberPressure.ToString("0.###E-0", CultureInfo.InvariantCulture));

            row.Append("\t").Append(s.HiVacPumpEn ? "1" : "0");
            row.Append("\t").Append(s.HiVacPumpSpeed.ToString("F1", CultureInfo.InvariantCulture));

            foreach (string gas in GasColumnOrder)
            {
                int i = Array.IndexOf(PLC210GasFlowClient.GasNames, gas);
                double set = i >= 0 ? s.GasSet[i] : 0;
                double flow = i >= 0 ? s.GasFlow[i] : 0;
                row.Append("\t").Append(set.ToString("F0", CultureInfo.InvariantCulture));
                row.Append("\t").Append(flow.ToString("F1", CultureInfo.InvariantCulture));
            }

            row.Append("\t").Append(EncodeBool(s.GasValves));
            row.Append("\t").Append(EncodeBool(s.VacValves));
            row.Append("\t").Append(EncodeBool(s.Pumps));
            row.Append("\t").Append(EncodeBool(s.DInputs));

            row.Append("\t").Append(EncodeByte(s.WarningsInput)).Append("\t");
            row.Append("\t").Append(EncodeByte(s.ErrorsInput)).Append("\t");

            return row.ToString();
        }

        private static string EncodeBool(bool[] arr)
        {
            string str = "b";
            BitArray bits = new BitArray(arr);
            for (int i = bits.Length; i > 0; i--)
                str += bits[i - 1] ? "1" : "0";
            return str;
        }

        private static string EncodeByte(byte[] arr)
        {
            char[] c = new char[arr.Length * 3];
            for (int i = 0; i < arr.Length; i++)
            {
                string hex = arr[i].ToString("X2");
                c[i * 3] = hex[0];
                c[i * 3 + 1] = hex[1];
                c[i * 3 + 2] = ' ';
            }
            return new string(c);
        }
    }
}
