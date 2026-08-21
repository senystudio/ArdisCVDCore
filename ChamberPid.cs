namespace ArdisCVDCore
{
    /// <summary>
    /// The operator-set chamber PID parameters.
    /// </summary>
    /// <remarks>
    /// The redesign split ownership of these across two windows: the pressure
    /// setpoint sits in the main window's Chamber Pressure box, the gains and
    /// limits moved to View -> PID Viewer. Both edit the same controller, so the
    /// values live here rather than in either form, and MainForm's one-second
    /// tick is the only thing that pushes them to the PLC (it is also the only
    /// place that knows the measured pressure to send alongside them).
    ///
    /// Every reader and writer is on the UI thread, so no locking: the worker
    /// threads see these only after MainForm has copied them into a
    /// PLC210PidClient.Channel.
    /// </remarks>
    internal static class ChamberPid
    {
        /// <summary>
        /// False until SET has been pressed at least once. Guards against the
        /// defaults (zero gains, zero limits) being pushed the moment the
        /// application connects, which would collapse the PLC's output to its
        /// lower limit before anyone has entered a real value.
        /// </summary>
        public static bool Committed;

        public static double Setpoint;

        // Defaults carried over from the pre-redesign PID panel. Named constants
        // rather than literals in the initialisers below, because Process
        // Parameters' Reset button puts these same numbers back into its fields
        // and the two must not be able to drift apart.
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

        public static bool DirectMode;
        public static double DirectValue;
    }
}
