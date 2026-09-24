using ArdisCVDCore.modules_hw;
using System;
using System.Collections.Generic;

namespace ArdisCVDCore
{
    public enum AlarmLevel
    {
        Alarm = 0,
        Abort = 1
    }

    public sealed class AlarmEvent
    {
        public DateTime At;
        public int Code;
        public AlarmLevel Level;
        public bool Raised;

        public AlarmEvent(DateTime at, int code, AlarmLevel level, bool raised)
        {
            At = at;
            Code = code;
            Level = level;
            Raised = raised;
        }
    }

    public static class AlarmJournal
    {
        public const int MaxEvents = 5000;

        private static readonly object Sync = new object();
        private static readonly List<AlarmEvent> Events = new List<AlarmEvent>();

        private static ulong _previousAlarm;
        private static ulong _previousAbort;
        private static bool _seenAnyPoll;

        public static void Poll(PLC210AlarmClient.State state)
        {
            if (state == null || !state.Connected)
                return;

            ulong abort = state.AbortOrLatched;
            ulong alarm = state.AlarmMask & ~abort;

            lock (Sync)
            {
                if (!_seenAnyPoll)
                {
                    _seenAnyPoll = true;
                    _previousAlarm = 0;
                    _previousAbort = 0;
                }

                DateTime now = DateTime.Now;

                Record(now, abort & ~_previousAbort, AlarmLevel.Abort, true);
                Record(now, _previousAbort & ~abort, AlarmLevel.Abort, false);
                Record(now, alarm & ~_previousAlarm, AlarmLevel.Alarm, true);
                Record(now, _previousAlarm & ~alarm, AlarmLevel.Alarm, false);

                _previousAlarm = alarm;
                _previousAbort = abort;
            }
        }

        public static List<AlarmEvent> Snapshot()
        {
            lock (Sync)
                return new List<AlarmEvent>(Events);
        }

        public static int Count
        {
            get
            {
                lock (Sync)
                    return Events.Count;
            }
        }

        public static void ClearHistory()
        {
            lock (Sync)
                Events.Clear();
        }

        private static void Record(DateTime at, ulong mask, AlarmLevel level, bool raised)
        {
            if (mask == 0)
                return;

            for (int code = 0; code < AlarmCatalog.CodeCount; code++)
            {
                if ((mask & (1UL << code)) == 0)
                    continue;

                Events.Add(new AlarmEvent(at, code, level, raised));
            }

            while (Events.Count > MaxEvents)
                Events.RemoveRange(0, Events.Count - MaxEvents);
        }
    }
}
