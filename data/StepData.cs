using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Serialization;

namespace ArdisCVDCore
{
    public class StepItemData
    {
        public StepItemData()
        {
            H2 = new MFCStruct();
            N2 = new MFCStruct();
            CH4 = new MFCStruct();
            O2 = new MFCStruct();
            AR = new MFCStruct();
            H2_2 = new MFCStruct();
            TempSample = new Pyrometer();
            RFTuner = new MatchingDevice();
            Stub1 = new Stub();
            Stub2 = new Stub();
            Bubbler = new BubblerClass();
            bDO = new bool[8];
            bDO_1 = new bool[8];
            bDO_2 = new bool[8];
            bDO_3 = new bool[8];
            bDI = new bool[8];
            bDI_1 = new bool[8];
            StepComments = "";
        }
        public String StepName;
        public String StepComments;
        public DateTime StepTimeEnd;
        public DateTime Measurement_Time;
        public bool bStepEnable;
        public bool bStepPause;
        public int StepExitIndex;
        public bool[] bDO;
        public bool[] bDO_1;
        public bool[] bDO_2;
        public bool[] bDO_3;
        public bool[] bDI;
        public bool[] bDI_1;
        public byte[] ErrorsInput = new byte[10];
        public byte[] WarningsInput = new byte[10];

        public object Clone()
        {
            StepItemData deepCopy = (StepItemData)this.MemberwiseClone();
            deepCopy.H2 = (StepItemData.MFCStruct)H2.Clone();
            deepCopy.N2 = (StepItemData.MFCStruct)N2.Clone();
            deepCopy.CH4 = (StepItemData.MFCStruct)CH4.Clone();
            deepCopy.O2 = (StepItemData.MFCStruct)O2.Clone();
            deepCopy.AR = (StepItemData.MFCStruct)AR.Clone();
            deepCopy.H2_2 = (StepItemData.MFCStruct)H2_2.Clone();
            deepCopy.Stub1 = (StepItemData.Stub)Stub1.Clone();
            deepCopy.Stub2 = (StepItemData.Stub)Stub2.Clone();
            deepCopy.bDO = (bool[])bDO.Clone();
            deepCopy.bDO_1 = (bool[])bDO_1.Clone();
            deepCopy.bDO_2 = (bool[])bDO_2.Clone();
            deepCopy.bDO_3 = (bool[])bDO_3.Clone();
            deepCopy.bDI = (bool[])bDI.Clone();
            deepCopy.bDI_1 = (bool[])bDI_1.Clone();
            deepCopy.TempSample = (StepItemData.Pyrometer)TempSample.Clone();
            deepCopy.RFTuner = (StepItemData.MatchingDevice)RFTuner.Clone();
            return deepCopy;
        }
        [Serializable()]
        public struct Power
        {
            public bool Power_Enable;
            public bool Preheat_Enable;
            public double IncidentInitialValue;
            public double IncidentCalculatedValue;
            public double IncidentEndValue;
            public double IncidentMeasuredValue;
            public bool IncidentAlarmEnable;
            public bool IncidentAbortEnable;
            public double IncidentAlarmValue;
            public double IncidentAbortValue;

            public double ReflectedMeasuredValue;
            public bool ReflectedAlarmEnable;
            public bool ReflectedAbortEnable;
            public double ReflectedAlarmValue;
            public double ReflectedAbortValue;

            public byte Control;
            public byte Error_1;
            public byte Error_2;
            public byte Error_3;
            public byte Error_4;
            public byte Warning;
        };
        public Power MWPower;
        public Power RFPower;
        [Serializable()]
        public struct DC
        {
            public bool Power_Enable;
            public double InitialValue;
            public double CalculatedValue;
            public double EndValue;
            public double MeasuredValue;
            public bool AlarmEnable;
            public bool AbortEnable;

            public double AlarmValue;
            public double AbortValue;
            public byte StatusRegister;
            public byte FaultRegister;
        };
        public DC DCVoltage;
        public DC DCCurrent;
        [Serializable()]
        public struct PressureSetStruct
        {
            public double InitialValue;
            public double CalculatedValue;
            public double EndValue;
            public double MeasuredValue;
            public bool AlarmEnable;
            public bool AbortEnable;
            public bool DirectPIDEnable;
            public int DirectPIDValue;
            public int AlarmValue;
            public int AbortValue;

        };

        public PressureSetStruct Chamber;
        public PressureSetStruct HiVacChamber;
        public PressureSetStruct Plenum;
        [Serializable()]
        public class MFCStruct
        {
            public double InitialValue;
            public double CalculatedValue;
            public double EndValue;
            public double MeasuredValue;
            public bool AlarmEnable;
            public bool AbortEnable;
            public double AlarmValue;
            public double AbortValue;
            public object Clone()
            {
                return this.MemberwiseClone();
            }
        };
        public MFCStruct H2;
        public MFCStruct CH4;
        public MFCStruct O2;
        public MFCStruct N2;
        public MFCStruct AR;
        public MFCStruct H2_2;

        [Serializable()]
        public class BubblerClass
        {
            public double SetPressureAutomode;
            public double SetTemperatureAutomode;
            public object Clone()
            {
                return this.MemberwiseClone();
            }
        };
        public BubblerClass Bubbler;
        public struct Pump
        {
            public bool Enable;
            public int Speed;
        };
        public Pump ForeVacPump;
        public Pump HiVacPump;
        public Pump Water;

        [Serializable()]
        public class Pyrometer
        {
            public float MeasuredValue_ch1;
            public float MeasuredValue_ch2;
            public float MeasuredValue_ch3;
            public bool ChannelSelector;

            public decimal SetValue;
            public bool AlarmEnable;
            public bool AbortEnable;
            public decimal AlarmValue;
            public decimal AbortValue;
            public object Clone()
            {
                return this.MemberwiseClone();
            }
        };
        public Pyrometer TempSample;
        [Serializable()]
        public class MatchingDevice
        {
            public double IncidentMeasuredValue;
            public double ReflectedMeasuredValue;
            public double UBias;
            public decimal SetValue;
            public bool AlarmEnable;
            public bool AbortEnable;
            public decimal AlarmValue;
            public decimal AbortValue;
            public object Clone()
            {
                return this.MemberwiseClone();
            }
        };
        public MatchingDevice RFTuner;
        public struct CheckStruct
        {
            public double MeasuredValue;
            public decimal SetValue;
            public bool AlarmEnable;
            public bool AbortEnable;
            public decimal AlarmValue;
            public decimal AbortValue;
        };

        public CheckStruct PlasmaDropS1;
        public CheckStruct PlasmaDropS2;
        public CheckStruct PlasmaDropS3;
        public CheckStruct PlasmaDropS4;
        public CheckStruct TempInternalH2O;
        public CheckStruct TempExternalH2O;
        public CheckStruct TempStageH2O;
        public CheckStruct TempRodH2O;
        public CheckStruct TempChamberH2O;
        public CheckStruct TempMWHeadH2O;
        public CheckStruct TempMWPowerH2O;

        public CheckStruct FlowInternalH2O;
        public CheckStruct FlowExternalH2O;
        public CheckStruct FlowStageH2O;
        public CheckStruct FlowRodH2O;
        public CheckStruct FlowChamberH2O;
        public CheckStruct FlowMWHeadH2O;
        public CheckStruct FlowMWPowerH2O;

        public CheckStruct CDAPressure;
        public CheckStruct WaterPressure;
        public struct InputCheckStruct
        {
            public bool Enable;
            public decimal Reaction;
        };
        public InputCheckStruct Input_1;
        public InputCheckStruct Input_2;
        public InputCheckStruct Input_3;
        public InputCheckStruct Input_4;
        public InputCheckStruct Input_5;
        public InputCheckStruct Input_6;
        public InputCheckStruct Input_7;
        public InputCheckStruct Input_8;
        public InputCheckStruct Input_9;
        public InputCheckStruct Input_10;
        public InputCheckStruct Input_11;
        [XmlIgnore]
        public TimeSpan StepDuration;
        [XmlElement("StepDuration")]
        public long TimeSinceLastEventTicks
        {
            get { return StepDuration.Ticks; }
            set { StepDuration = new TimeSpan(value); }
        }
        public struct GCF_Struct
        {
            public double H2;
            public double N2;
            public double CH4;
            public double O2;
            public double AR;
            public double H2_2;
        }
        public GCF_Struct GCF;
        public struct ZPosition
        {
            public double ZCurrent;
            public int Speed;
            public double InitialValue;
            public double EndValue;
            public double CalculatedValue;
        };

        public ZPosition ZPos;
        [Serializable()]
        public class Stub
        {
            public bool Direction;
            public bool UpLimit;
            public bool DownLimit;
            public double CurrentPosition;
            public double LastPosition;
            public double AutomodeStepPosition;
            public bool AutomodeAutotuneOn;
            public DateTime StartTime;
            public object Clone()
            {
                return this.MemberwiseClone();
            }

        };
        public Stub Stub1;
        public Stub Stub2;
    }
    class DataSerializer
    {
        public static IList<StepItemData> Deserialization(string a_fileName)
        {
            XmlSerializer deserializer = new XmlSerializer(typeof(List<StepItemData>));
            TextReader reader = new StreamReader(a_fileName);
            object obj = deserializer.Deserialize(reader);
            reader.Close();
            return (List<StepItemData>)obj;
        }
        public static string DeserializatedRecipeComments(string a_fileName)
        {
            XmlDocument doc;
            doc = new XmlDocument();
            try
            {
                doc.Load(a_fileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error opening configuration file :: "
                                + ex.Message, "Warning!");
            }
            XmlElement Comments = doc.GetElementsByTagName("StepComments")[0] as XmlElement;
            return Comments.InnerText;
        }
        public static void Serialization(IList<StepItemData> a_stations, string a_fileName)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(List<StepItemData>));

            using (XmlTextWriter tw = new XmlTextWriter(a_fileName, Encoding.UTF8))
            {
                tw.Formatting = Formatting.Indented;
                serializer.Serialize(tw, a_stations);
            }
        }
    }
}
