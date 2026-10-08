namespace Loudify;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, @"Local\Loudify.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("O Loudify já está em execução. Procure o ícone na área de notificação.",
                "Loudify", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }
}
