using System;
using System.Globalization;

namespace NodeCraft.BuiltIn.Nodes
{
    internal static class NodeValueConverter
    {
        internal static double ToDouble(object value)
        {
            if (value == null)
            {
                return 0d;
            }

            return value switch
            {
                double doubleValue => doubleValue,
                float floatValue => floatValue,
                decimal decimalValue => (double)decimalValue,
                int intValue => intValue,
                long longValue => longValue,
                short shortValue => shortValue,
                byte byteValue => byteValue,
                bool boolValue => boolValue ? 1d : 0d,
                string stringValue when double.TryParse(
                    stringValue,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var parsed) => parsed,
                _ => Convert.ToDouble(value, CultureInfo.InvariantCulture),
            };
        }

        internal static bool ToBoolean(object value)
        {
            if (value == null)
            {
                return false;
            }

            return value switch
            {
                bool boolValue => boolValue,
                string stringValue when bool.TryParse(stringValue, out var parsed) => parsed,
                string stringValue when double.TryParse(
                    stringValue,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var parsed) => Math.Abs(parsed) > double.Epsilon,
                _ => Math.Abs(ToDouble(value)) > double.Epsilon,
            };
        }
    }
}
