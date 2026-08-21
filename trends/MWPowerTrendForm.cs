using ArdisCVDCore.modules_hw;
using System.Drawing;

namespace ArdisCVDCore.trends
{
    /// <summary>View -> MW Power Trend: incident against its setpoint, and reflected.</summary>
    public class MWPowerTrendForm : TrendForm
    {
        public MWPowerTrendForm()
            : base("MWPowerTrend", "MW Power Trend", 0, PLC210MicrowaveClient.MaxSetpointKw)
        {
            AddSeries("Incident", "Incident, kW", Color.Blue, 3);
            AddSetPointSeries("IncidentSetPoint", "Incident set point, kW", Color.Blue);
            AddSeries("Reflected", "Reflected, kW", Color.Red, 2);
        }

        protected override void AppendPoints(string xTime)
        {
            PLC210MicrowaveClient.State state = PLC210MicrowaveClient.GetState();

            Plot("Incident", xTime, state.IncidentKw);
            Plot("IncidentSetPoint", xTime, state.SetpointKw);
            Plot("Reflected", xTime, state.ReflectedKw);
        }
    }
}
