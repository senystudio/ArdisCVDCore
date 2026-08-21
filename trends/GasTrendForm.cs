using ArdisCVDCore.modules_hw;
using System.Drawing;

namespace ArdisCVDCore.trends
{
    /// <summary>View -> Gas Trend: the six РРГ-20 channels, measured and setpoint.</summary>
    public class GasTrendForm : TrendForm
    {
        // Index order matches PLC210GasFlowClient.GasNames (== PLC channel index,
        // == aCfg[] order in FB_MfcModbusMaster.st): H2, CH4, N2, O2, Ar, H2 (2nd line).
        private static readonly string[] SeriesName = { "H2", "CH4", "N2", "O2", "Ar", "H2_2" };
        private static readonly string[] SeriesLabel = { "H2", "CH4", "N2", "O2", "Ar", "H2 (2)" };
        private static readonly Color[] ChannelColor =
        {
            Color.Blue, Color.Orange, Color.Brown, Color.DeepSkyBlue, Color.Purple, Color.SeaGreen
        };

        public GasTrendForm()
            : base("GasTrend", "Gas Trend", 0, 1000)
        {
            for (int i = 0; i < SeriesName.Length; i++)
            {
                AddSeries(SeriesName[i], SeriesLabel[i] + " (sccm)", ChannelColor[i], 3);
                AddSetPointSeries(SeriesName[i] + "SetPoint", SeriesLabel[i] + " setpoint", ChannelColor[i]);
            }
        }

        protected override void AppendPoints(string xTime)
        {
            PLC210GasFlowClient.State state = PLC210GasFlowClient.GetState();

            for (int i = 0; i < SeriesName.Length; i++)
            {
                PLC210GasFlowClient.ChannelState channel = state.Channels[i];
                Plot(SeriesName[i], xTime, channel.MeasuredSccm);
                Plot(SeriesName[i] + "SetPoint", xTime, channel.SetpointSccm);
            }
        }
    }
}
