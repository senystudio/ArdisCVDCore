namespace ArdisCVDCore
{
    internal static class ChamberPid
    {
        public static bool Committed;

        public static double Setpoint;

        public const double DefaultKp = 1;
        public const double DefaultKi = 0;
        public const double DefaultKd = 4;
        public const double DefaultUpperLimit = 4000;
        public const double DefaultLowerLimit = 1000;

        public static double Kp = DefaultKp;
        public static double Ki = DefaultKi;
        public static double Kd = DefaultKd;
        public static double UpperLimit = DefaultUpperLimit;
        public static double LowerLimit = DefaultLowerLimit;

        public static bool SmartMode;
        public static double SmartKp = DefaultKp;

        public static bool DirectMode;
        public static double DirectValue;
    }
}
