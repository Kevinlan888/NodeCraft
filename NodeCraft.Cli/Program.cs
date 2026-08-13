using System;
using System.IO;
using System.Linq;

namespace NodeCraft.Cli
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            return Run(args, Console.In, Console.Out, Directory.GetCurrentDirectory());
        }

        /// <summary>
        /// Runs the CLI with injectable streams for testing.
        /// </summary>
        public static int Run(string[] args, TextReader input, TextWriter output, string workingDirectory)
        {
            if (args == null || args.Length == 0 || Array.IndexOf(args, "--help") >= 0
                || Array.IndexOf(args, "-h") >= 0)
            {
                PrintUsage(output);
                return args == null || args.Length == 0 ? 1 : 0;
            }

            if (args[0] == "new")
            {
                return new NewCommand(
                    new Questionnaire(input, output),
                    output,
                    workingDirectory).Run(args.Skip(1).ToArray());
            }

            output.WriteLine($"Error: unknown command '{args[0]}'.");
            PrintUsage(output);
            return 1;
        }

        private static void PrintUsage(TextWriter output)
        {
            output.WriteLine("Usage:");
            output.WriteLine("  nodecraft-cli new [ProjectName] [--force]");
            output.WriteLine("  nodecraft-cli --help | -h");
            output.WriteLine();
            output.WriteLine("Creates a new NodeCraft plugin project interactively.");
        }
    }
}
