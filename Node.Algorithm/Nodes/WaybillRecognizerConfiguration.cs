using System;
using System.Collections.Generic;
using System.Globalization;
using Node.Algorithm.Interop;
using NodeCraft.Flow;

namespace Node.Algorithm.Nodes
{
    internal sealed class WaybillRecognizerConfiguration
    {
        internal const string DefaultModelPath = "models/baseline-2-960.onnx";
        internal const float DefaultConfidence = 0.35f;
        internal const float DefaultIou = 0.50f;
        internal const float DefaultMinMaskAreaRatio = 0.0001f;
        internal const int DefaultMaxDetections = 100;
        internal const int DefaultNumThreads = 0;

        private WaybillRecognizerConfiguration(
            string modelPath,
            WaybillInferenceOptions options)
        {
            ModelPath = modelPath;
            Options = options;
        }

        internal string ModelPath { get; }

        internal WaybillInferenceOptions Options { get; }

        internal static WaybillRecognizerConfiguration Read(WorkflowNode node)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            var inputs = node.Inputs ?? new Dictionary<string, object>();
            var modelPath = ReadModelPath(inputs);
            var confidence = ReadSingle(inputs, "confidence", DefaultConfidence);
            var iou = ReadSingle(inputs, "iou", DefaultIou);
            var minMaskAreaRatio = ReadSingle(
                inputs,
                "minMaskAreaRatio",
                DefaultMinMaskAreaRatio);
            var maxDetections = ReadInteger(
                inputs,
                "maxDetections",
                DefaultMaxDetections);
            var numThreads = ReadInteger(inputs, "numThreads", DefaultNumThreads);

            ValidateUnitRange(confidence, "confidence");
            ValidateUnitRange(iou, "iou");
            ValidateUnitRange(minMaskAreaRatio, "minMaskAreaRatio");
            if (maxDetections <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    "maxDetections",
                    maxDetections,
                    "MaxDetections must be greater than zero.");
            }

            if (numThreads < 0)
            {
                throw new ArgumentOutOfRangeException(
                    "numThreads",
                    numThreads,
                    "NumThreads cannot be negative.");
            }

            return new WaybillRecognizerConfiguration(
                modelPath,
                new WaybillInferenceOptions
                {
                    Confidence = confidence,
                    Iou = iou,
                    MinMaskAreaRatio = minMaskAreaRatio,
                    MaxDetections = maxDetections,
                    NumThreads = numThreads,
                });
        }

        private static string ReadModelPath(IReadOnlyDictionary<string, object> inputs)
        {
            if (!inputs.TryGetValue("modelPath", out var value))
            {
                return DefaultModelPath;
            }

            return value == null ? string.Empty : value as string ?? value.ToString();
        }

        private static float ReadSingle(
            IReadOnlyDictionary<string, object> inputs,
            string key,
            float defaultValue)
        {
            if (!inputs.TryGetValue(key, out var value) || value == null)
            {
                return defaultValue;
            }

            float converted;
            if (value is float single)
            {
                converted = single;
            }
            else if (value is double doubleValue)
            {
                converted = (float)doubleValue;
            }
            else if (value is decimal decimalValue)
            {
                converted = (float)decimalValue;
            }
            else if (value is string text
                && float.TryParse(
                    text,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var parsed))
            {
                converted = parsed;
            }
            else
            {
                throw new ArgumentException(
                    $"Waybill setting '{key}' must be a floating-point number.",
                    key);
            }

            return converted;
        }

        private static int ReadInteger(
            IReadOnlyDictionary<string, object> inputs,
            string key,
            int defaultValue)
        {
            if (!inputs.TryGetValue(key, out var value) || value == null)
            {
                return defaultValue;
            }

            if (value is int integer)
            {
                return integer;
            }

            if (value is long longValue
                && longValue >= int.MinValue
                && longValue <= int.MaxValue)
            {
                return (int)longValue;
            }

            if (value is double doubleValue
                && doubleValue >= int.MinValue
                && doubleValue <= int.MaxValue
                && Math.Truncate(doubleValue) == doubleValue)
            {
                return (int)doubleValue;
            }

            if (value is string text
                && int.TryParse(
                    text,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var parsed))
            {
                return parsed;
            }

            throw new ArgumentException(
                $"Waybill setting '{key}' must be an integer.",
                key);
        }

        private static void ValidateUnitRange(float value, string key)
        {
            if (float.IsNaN(value)
                || float.IsInfinity(value)
                || value < 0
                || value > 1)
            {
                throw new ArgumentOutOfRangeException(
                    key,
                    value,
                    $"Waybill setting '{key}' must be finite and between zero and one.");
            }
        }
    }
}
