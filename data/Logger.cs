using System;

namespace ArdisCVDCore
{
    public static class Logger
    {
        private static readonly NLog.Logger nlogger = NLog.LogManager.GetCurrentClassLogger();

        public static volatile bool Enabled = true;

        public static void WriteError(Exception e)
        {
            if (!Enabled)
                return;

            try
            {
                nlogger.Error(e);
            }
            catch
            {
            }
        }
    }
}
