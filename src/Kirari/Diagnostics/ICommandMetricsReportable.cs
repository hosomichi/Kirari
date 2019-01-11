namespace Kirari.Diagnostics
{
    public interface ICommandMetricsReportable
    {
        void Report(DbCommandMetrics commandMetrics);
    }
}
