using System;

namespace NodeCraft.Cli.Tests
{
    internal static class Program
    {
        private static int _failures;

        private static int Main()
        {
            Run("test harness runs", () => true);
            ValidatorTests.RunAll();
            TemplateTests.RunAll();
            GeneratorTests.RunAll();
            QuestionnaireTests.RunAll();
            NewCommandTests.RunAll();

            if (_failures > 0)
            {
                Console.WriteLine($"{_failures} test(s) failed.");
                return 1;
            }

            Console.WriteLine("All tests passed.");
            return 0;
        }

        internal static void Run(string name, Func<bool> assertion)
        {
            try
            {
                if (assertion())
                {
                    Console.WriteLine($"PASS {name}");
                }
                else
                {
                    _failures++;
                    Console.WriteLine($"FAIL {name}");
                }
            }
            catch (Exception ex)
            {
                _failures++;
                Console.WriteLine($"FAIL {name} ({ex.GetType().Name}: {ex.Message})");
            }
        }
    }
}
