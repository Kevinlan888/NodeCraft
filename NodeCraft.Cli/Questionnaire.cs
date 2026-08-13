using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NodeCraft.Cli
{
    /// <summary>
    /// Interactive prompt helpers over injectable text streams.
    /// </summary>
    public sealed class Questionnaire
    {
        private readonly TextReader _input;
        private readonly TextWriter _output;

        public Questionnaire(TextReader input, TextWriter output)
        {
            _input = input ?? throw new ArgumentNullException(nameof(input));
            _output = output ?? throw new ArgumentNullException(nameof(output));
        }

        /// <summary>
        /// Prompts for a string. An empty line returns <paramref name="defaultValue"/>
        /// when it is non-null; otherwise validation errors re-prompt.
        /// Returns null when the input stream ends (EOF) — the caller treats
        /// that as "abort" and must not loop forever on the returned value.
        /// </summary>
        public string AskString(string prompt, string defaultValue, Func<string, string> validate)
        {
            while (true)
            {
                _output.Write(defaultValue != null ? $"? {prompt} ({defaultValue}): " : $"? {prompt}: ");
                var line = _input.ReadLine();
                if (line == null)
                {
                    return null;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    if (defaultValue != null)
                    {
                        return defaultValue;
                    }

                    line = string.Empty;
                }

                line = line.Trim();
                var error = validate?.Invoke(line);
                if (error == null)
                {
                    return line;
                }

                _output.WriteLine($"Error: {error}");
            }
        }

        /// <summary>
        /// Prompts for a yes/no answer. Accepts y/Y/n/N; empty line returns
        /// <paramref name="defaultValue"/>; end of input returns
        /// <paramref name="defaultValue"/> as well; anything else re-prompts.
        /// </summary>
        public bool AskConfirm(string prompt, bool defaultValue)
        {
            while (true)
            {
                _output.Write($"? {prompt} (y/n, default {(defaultValue ? "y" : "n")}): ");
                var line = _input.ReadLine();
                if (line == null || string.IsNullOrWhiteSpace(line))
                {
                    return defaultValue;
                }

                var answer = line.Trim().ToLowerInvariant();
                if (answer == "y")
                {
                    return true;
                }

                if (answer == "n")
                {
                    return false;
                }

                _output.WriteLine("Error: answer y or n.");
            }
        }

        /// <summary>
        /// Multi-select checkbox prompt. Prints one numbered line per option and
        /// accepts: indices (space-separated) to toggle, "a" select all, "n" none,
        /// empty line to confirm. Returns the selected option indices in order.
        /// End of input returns the current selection; the caller should treat a
        /// selection made under EOF as incomplete.
        /// </summary>
        public IReadOnlyList<int> SelectFeatures(IReadOnlyList<string> options)
        {
            var selected = new HashSet<int>();
            while (true)
            {
                _output.WriteLine();
                for (var i = 0; i < options.Count; i++)
                {
                    var marker = selected.Contains(i) ? "x" : " ";
                    _output.WriteLine($"  [{marker}] {i + 1}. {options[i]}");
                }

                _output.Write("? Select (indices toggle, a=all, n=none, Enter=confirm): ");
                var line = _input.ReadLine();
                if (line == null || string.IsNullOrWhiteSpace(line))
                {
                    return selected.OrderBy(index => index).ToArray();
                }

                var tokens = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var token in tokens)
                {
                    switch (token.ToLowerInvariant())
                    {
                        case "a":
                            for (var i = 0; i < options.Count; i++)
                            {
                                selected.Add(i);
                            }

                            break;
                        case "n":
                            selected.Clear();
                            break;
                        default:
                            if (int.TryParse(token, out var index) && index >= 1 && index <= options.Count)
                            {
                                if (!selected.Remove(index - 1))
                                {
                                    selected.Add(index - 1);
                                }
                            }
                            else
                            {
                                _output.WriteLine($"Error: unknown selection '{token}'.");
                            }

                            break;
                    }
                }
            }
        }
    }
}
