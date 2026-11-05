namespace ZLByLHL;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        bool startMinimized = false;
        foreach (var arg in args)
        {
            if (arg.Equals("--startup", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--minimized", StringComparison.OrdinalIgnoreCase))
            {
                startMinimized = true;
                break;
            }
        }
        Application.Run(new Form1(startMinimized));
    }    
}
