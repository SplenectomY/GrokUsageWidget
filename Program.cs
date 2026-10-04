namespace GrokUsageWidget;

internal static class Program
{
    internal const string MutexName = @"Local\GrokUsageWidget.SingleInstance";
    internal const string RevealEventName = @"Local\GrokUsageWidget.Reveal";

    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => CrashLog.Write("UI " + e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            CrashLog.Write("domain " + e.ExceptionObject);

        try
        {
            CrashLog.Write("start pid=" + Environment.ProcessId + " path=" + Startup.ExePath);
            KillOtherCopies();

            using var reveal = new EventWaitHandle(false, EventResetMode.AutoReset, RevealEventName);
            using var mutex = new Mutex(true, MutexName, out var created);
            if (!created)
            {
                CrashLog.Write("mutex busy after kill; waiting");
                try { mutex.WaitOne(5000); }
                catch (AbandonedMutexException) { CrashLog.Write("mutex abandoned; taken"); }
            }

            if (Startup.IsEnabled())
            {
                try { Startup.SetEnabled(true); }
                catch (Exception ex) { CrashLog.Write("startup key " + ex.Message); }
            }

            Application.Run(new MeterForm(reveal));
            GC.KeepAlive(mutex);
            CrashLog.Write("exit");
        }
        catch (Exception ex)
        {
            CrashLog.Write("fatal " + ex);
            MessageBox.Show(ex.Message, "Grok Usage Widget", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void KillOtherCopies()
    {
        var me = Environment.ProcessId;
        foreach (var p in Process.GetProcessesByName("GrokUsageWidget"))
        {
            if (p.Id == me)
                continue;
            try
            {
                CrashLog.Write("kill pid=" + p.Id);
                p.Kill(entireProcessTree: true);
                p.WaitForExit(4000);
            }
            catch (Exception ex)
            {
                CrashLog.Write("kill failed pid=" + p.Id + " " + ex.Message);
                try
                {
                    p.Kill();
                    p.WaitForExit(2000);
                }
                catch { /* last resort */ }
            }
        }
    }
}
