namespace ModHearth.Cli
{
    internal static class Program
    {
        public static int Main(string[] args)
        {
            return CliApp.Run(args, Console.Out, Console.Error);
        }
    }
}
