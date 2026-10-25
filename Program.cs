namespace PhotoMark;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length == 2 && args[0] == "--self-test")
        {
            Environment.ExitCode = SelfTest.Run(args[1]);
            return;
        }
        if (args.Length == 2 && args[0] == "--ui-check")
        {
            SettingsStore.TestMode = true;
            using var form = new MainForm([]);
            form.Shown += async (_, _) =>
            {
                try { await form.RunUiChecks(args[1]); }
                catch (Exception ex) { File.WriteAllText(Path.Combine(args[1], "ui-results.txt"), "FAIL: " + ex); Environment.ExitCode = 1; }
                finally { form.Close(); }
            };
            Application.Run(form);
            return;
        }
        Application.ThreadException += (_, e) => MessageBox.Show(e.Exception.Message, "拾印 · 出现问题", MessageBoxButtons.OK, MessageBoxIcon.Error);
        Application.Run(new MainForm(args));
    }
}
