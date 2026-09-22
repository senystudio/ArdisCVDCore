using System;
using System.Globalization;

namespace ArdisCVDCore
{
    public static class AlarmSettings
    {
        public const int ParamCount = 8;
        public const int ParamPctCount = 7;
        public const int WaterCount = 4;
        public const int InputCount = 8;
        public const int BlockWords = 32;

        public const int ParamChamber = 0;
        public const int ParamGasFirst = 1;
        public const int ParamReflected = 7;

        public const ushort BlockMagic = 0xA500;
        public const ushort BlockVersion = 0x0001;

        public const int DefaultSampleTargetC = 400;
        public const int DefaultLidInput = 1;

        private const string IniSection = "Alarms";

        public static readonly bool[] ParamAlarmEnable = new bool[ParamCount];
        public static readonly bool[] ParamAbortEnable = new bool[ParamCount];
        public static readonly int[] ParamAlarmPct = new int[ParamPctCount];
        public static readonly int[] ParamAbortPct = new int[ParamPctCount];

        public static int ReflectedAlarmWatt;
        public static int ReflectedAbortWatt;

        public static bool SampleAlarmEnable;
        public static int SampleTargetC = DefaultSampleTargetC;
        public static int SampleAlarmPct;

        public static readonly bool[] WaterAlarmEnable = new bool[WaterCount];
        public static readonly bool[] WaterAbortEnable = new bool[WaterCount];
        public static readonly int[] WaterTargetC = new int[WaterCount];
        public static readonly int[] WaterAlarmPct = new int[WaterCount];
        public static readonly int[] WaterAbortPct = new int[WaterCount];

        public static readonly bool[] InputEnable = new bool[InputCount];
        public static readonly bool[] InputAborts = new bool[InputCount];

        public static int LidInput = DefaultLidInput;
        public static bool RunActive;

        public static void ResetToDefaults()
        {
            for (int i = 0; i < ParamCount; i++)
            {
                ParamAlarmEnable[i] = false;
                ParamAbortEnable[i] = false;
            }

            for (int i = 0; i < ParamPctCount; i++)
            {
                ParamAlarmPct[i] = 0;
                ParamAbortPct[i] = 0;
            }

            ReflectedAlarmWatt = 0;
            ReflectedAbortWatt = 0;

            SampleAlarmEnable = false;
            SampleTargetC = DefaultSampleTargetC;
            SampleAlarmPct = 0;

            for (int i = 0; i < WaterCount; i++)
            {
                WaterAlarmEnable[i] = false;
                WaterAbortEnable[i] = false;
                WaterTargetC[i] = 0;
                WaterAlarmPct[i] = 0;
                WaterAbortPct[i] = 0;
            }

            for (int i = 0; i < InputCount; i++)
            {
                InputEnable[i] = false;
                InputAborts[i] = false;
            }
        }

        public static bool AnyEnabled()
        {
            for (int i = 0; i < ParamCount; i++)
                if (ParamAlarmEnable[i] || ParamAbortEnable[i])
                    return true;

            if (SampleAlarmEnable)
                return true;

            for (int i = 0; i < WaterCount; i++)
                if (WaterAlarmEnable[i] || WaterAbortEnable[i])
                    return true;

            for (int i = 0; i < InputCount; i++)
                if (InputEnable[i])
                    return true;

            return false;
        }

        public static ushort[] Pack()
        {
            ushort[] block = new ushort[BlockWords];

            block[0] = (ushort)(BlockMagic | BlockVersion);
            block[1] = PackBits(ParamAlarmEnable);
            block[2] = PackBits(ParamAbortEnable);

            for (int i = 0; i < ParamPctCount; i++)
                block[3 + i] = PackBytes(ParamAlarmPct[i], ParamAbortPct[i]);

            block[10] = ClampWord(ReflectedAlarmWatt, 2000);
            block[11] = ClampWord(ReflectedAbortWatt, 2000);

            ushort tempEnable = 0;
            if (SampleAlarmEnable)
                tempEnable |= 0x0001;
            for (int i = 0; i < WaterCount; i++)
            {
                if (WaterAlarmEnable[i])
                    tempEnable |= (ushort)(1 << (i + 1));
                if (WaterAbortEnable[i])
                    tempEnable |= (ushort)(1 << (i + 9));
            }
            block[12] = tempEnable;

            block[13] = ClampWord(SampleTargetC, 1500);
            block[14] = PackBytes(SampleAlarmPct, 0);
            block[15] = PackBytes(WaterTargetC[0], WaterTargetC[1]);
            block[16] = PackBytes(WaterTargetC[2], WaterTargetC[3]);

            for (int i = 0; i < WaterCount; i++)
                block[17 + i] = PackBytes(WaterAlarmPct[i], WaterAbortPct[i]);

            block[21] = PackBits(InputEnable);
            block[22] = PackBits(InputAborts);

            ushort flags = 0;
            if (RunActive)
                flags |= 0x0001;
            flags |= (ushort)((ClampInt(LidInput, 1, 12) & 0xFF) << 8);
            block[23] = flags;

            return block;
        }

        public static void Load()
        {
            ResetToDefaults();

            for (int i = 0; i < ParamCount; i++)
            {
                ParamAlarmEnable[i] = ReadBool("ParamAlarmEnable" + i, false);
                ParamAbortEnable[i] = ReadBool("ParamAbortEnable" + i, false);
            }

            for (int i = 0; i < ParamPctCount; i++)
            {
                ParamAlarmPct[i] = ReadInt("ParamAlarmPct" + i, 0, 0, 100);
                ParamAbortPct[i] = ReadInt("ParamAbortPct" + i, 0, 0, 100);
            }

            ReflectedAlarmWatt = ReadInt("ReflectedAlarmWatt", 0, 0, 2000);
            ReflectedAbortWatt = ReadInt("ReflectedAbortWatt", 0, 0, 2000);

            SampleAlarmEnable = ReadBool("SampleAlarmEnable", false);
            SampleTargetC = ReadInt("SampleTargetC", DefaultSampleTargetC, 0, 1500);
            SampleAlarmPct = ReadInt("SampleAlarmPct", 0, 0, 100);

            for (int i = 0; i < WaterCount; i++)
            {
                WaterAlarmEnable[i] = ReadBool("WaterAlarmEnable" + i, false);
                WaterAbortEnable[i] = ReadBool("WaterAbortEnable" + i, false);
                WaterTargetC[i] = ReadInt("WaterTargetC" + i, 0, 0, 100);
                WaterAlarmPct[i] = ReadInt("WaterAlarmPct" + i, 0, 0, 100);
                WaterAbortPct[i] = ReadInt("WaterAbortPct" + i, 0, 0, 100);
            }

            for (int i = 0; i < InputCount; i++)
            {
                InputEnable[i] = ReadBool("InputEnable" + i, false);
                InputAborts[i] = ReadBool("InputAborts" + i, false);
            }

            LidInput = ReadInt("LidInput", DefaultLidInput, 1, 12);
        }

        public static void Save()
        {
            for (int i = 0; i < ParamCount; i++)
            {
                WriteBool("ParamAlarmEnable" + i, ParamAlarmEnable[i]);
                WriteBool("ParamAbortEnable" + i, ParamAbortEnable[i]);
            }

            for (int i = 0; i < ParamPctCount; i++)
            {
                WriteInt("ParamAlarmPct" + i, ParamAlarmPct[i]);
                WriteInt("ParamAbortPct" + i, ParamAbortPct[i]);
            }

            WriteInt("ReflectedAlarmWatt", ReflectedAlarmWatt);
            WriteInt("ReflectedAbortWatt", ReflectedAbortWatt);

            WriteBool("SampleAlarmEnable", SampleAlarmEnable);
            WriteInt("SampleTargetC", SampleTargetC);
            WriteInt("SampleAlarmPct", SampleAlarmPct);

            for (int i = 0; i < WaterCount; i++)
            {
                WriteBool("WaterAlarmEnable" + i, WaterAlarmEnable[i]);
                WriteBool("WaterAbortEnable" + i, WaterAbortEnable[i]);
                WriteInt("WaterTargetC" + i, WaterTargetC[i]);
                WriteInt("WaterAlarmPct" + i, WaterAlarmPct[i]);
                WriteInt("WaterAbortPct" + i, WaterAbortPct[i]);
            }

            for (int i = 0; i < InputCount; i++)
            {
                WriteBool("InputEnable" + i, InputEnable[i]);
                WriteBool("InputAborts" + i, InputAborts[i]);
            }

            WriteInt("LidInput", LidInput);
        }

        private static ushort PackBits(bool[] flags)
        {
            ushort value = 0;
            for (int i = 0; i < flags.Length && i < 16; i++)
                if (flags[i])
                    value |= (ushort)(1 << i);
            return value;
        }

        private static ushort PackBytes(int low, int high)
        {
            return (ushort)((ClampInt(low, 0, 255) & 0xFF) | ((ClampInt(high, 0, 255) & 0xFF) << 8));
        }

        private static ushort ClampWord(int value, int max)
        {
            return (ushort)ClampInt(value, 0, max);
        }

        private static int ClampInt(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        private static bool ReadBool(string key, bool fallback)
        {
            if (!IniWriter.INI.KeyExists(key, IniSection))
                return fallback;

            return IniWriter.INI.ReadINI(IniSection, key).Trim() == "1";
        }

        private static int ReadInt(string key, int fallback, int min, int max)
        {
            if (!IniWriter.INI.KeyExists(key, IniSection))
                return fallback;

            int parsed;
            if (!int.TryParse(IniWriter.INI.ReadINI(IniSection, key).Trim(),
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                return fallback;

            return ClampInt(parsed, min, max);
        }

        private static void WriteBool(string key, bool value)
        {
            IniWriter.INI.Write(IniSection, key, value ? "1" : "0");
        }

        private static void WriteInt(string key, int value)
        {
            IniWriter.INI.Write(IniSection, key, value.ToString(CultureInfo.InvariantCulture));
        }
    }
}
