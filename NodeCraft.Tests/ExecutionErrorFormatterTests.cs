using System;
using NodeCraft.Execution;

internal static partial class Program
{
    private static void RunExecutionErrorFormatterTests()
    {
        Run("execution errors use the innermost message without stack text", () =>
        {
            var exception = new InvalidOperationException(
                "outer",
                new ArgumentException("inner"));
            var text = ExecutionErrorFormatter.Format("Run graph", exception, 512);
            return text == "Run graph: inner"
                && !text.Contains("System.ArgumentException", StringComparison.Ordinal)
                && !text.Contains(" at ", StringComparison.Ordinal);
        });

        Run("execution error text is bounded to the requested length", () =>
        {
            var text = ExecutionErrorFormatter.Format(
                "Run graph",
                new InvalidOperationException(new string('x', 1024)),
                512);
            return text.Length == 512
                && text.StartsWith("Run graph: ", StringComparison.Ordinal)
                && text.EndsWith("…", StringComparison.Ordinal);
        });

        Run("iteration diagnostics use trace logging", () =>
        {
            var source = System.IO.File.ReadAllText(FindRepositoryFile(
                "NodeCraft.Flow",
                "Flow",
                "FlowGraphIterationRunner.cs"));
            return source.Contains("logger.LogTrace(\"Graph execution started", StringComparison.Ordinal)
                && source.Contains("logger.LogTrace(\"Skipping node", StringComparison.Ordinal)
                && source.Contains("logger.LogTrace(\"Executing node", StringComparison.Ordinal)
                && source.Contains("logger.LogTrace(\"Graph iteration finished", StringComparison.Ordinal)
                && source.Contains("logger.LogTrace(\"Graph execution finished", StringComparison.Ordinal)
                && !source.Contains("logger.LogInformation", StringComparison.Ordinal);
        });
    }
}
