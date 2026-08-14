using System;

namespace NodeCraft.Execution
{
    internal static class ExecutionErrorFormatter
    {
        internal static string Format(string stage, Exception exception, int maxLength)
        {
            if (exception == null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            if (maxLength < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxLength));
            }

            var prefix = string.IsNullOrWhiteSpace(stage)
                ? "Execution failed"
                : stage.Trim();
            prefix = prefix.TrimEnd('.', ':', ' ');
            var message = GetInnermostMessage(exception);
            var formatted = string.IsNullOrWhiteSpace(message)
                ? prefix
                : prefix + ": " + message;

            if (formatted.Length <= maxLength)
            {
                return formatted;
            }

            if (maxLength == 0)
            {
                return string.Empty;
            }

            if (maxLength == 1)
            {
                return "…";
            }

            return formatted.Substring(0, maxLength - 1) + "…";
        }

        private static string GetInnermostMessage(Exception exception)
        {
            var current = exception;
            while (current.InnerException != null)
            {
                current = current.InnerException;
            }

            return current.Message;
        }
    }
}
