using System;
using System.Diagnostics;

namespace EarTrumpet.Diagnosis
{
    /// <summary>
    /// Collects trace output so the Troubleshoot button can show it.
    ///
    /// Nothing is reported anywhere. Upstream this also uploaded crash reports to the
    /// EarTrumpet project through Bugsnag, gated on a "send crash data" setting; both the
    /// client and the setting are gone in this build. Crash data would have gone to the
    /// upstream project's account, and this is a self-compiled fork - keeping a switch that
    /// claims to control that would be worse than not having one. Diagnostics stay local, in
    /// memory, until the user opens them.
    /// </summary>
    class ErrorReporter
    {
        private static ErrorReporter s_instance;
        private readonly CircularBufferTraceListener _listener;

        public ErrorReporter()
        {
            Debug.Assert(s_instance == null);
            s_instance = this;

            _listener = new CircularBufferTraceListener();
            Trace.Listeners.Clear();
            Trace.Listeners.Add(_listener);
        }

        public void DisplayDiagnosticData()
        {
            LocalDataExporter.DumpAndShowData(_listener.GetLogText());
        }

        public static void LogWarning(Exception ex) => s_instance.LogWarningInstance(ex);

        private void LogWarningInstance(Exception ex)
        {
            Trace.WriteLine($"## Warning Notify ##: {ex}");
        }
    }
}
