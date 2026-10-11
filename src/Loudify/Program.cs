namespace Loudify;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Started by the previous version after it replaced the exe.
        bool updated = args.Length == 2 && args[0] == Updater.AfterUpdateArgument;
        if (updated && int.TryParse(args[1], out int previousProcessId))
            Updater.FinishUpdate(previousProcessId);
        else
            Updater.CleanUp();

        using var mutex = new Mutex(initiallyOwned: true, @"Local\Loudify.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("O Loudify já está em execução. Procure o ícone na área de notificação.",
                "Loudify", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext(updated));
    }
}
