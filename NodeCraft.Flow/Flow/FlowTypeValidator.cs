using System;
using System.Collections.Generic;
using System.Linq;

namespace NodeCraft.Flow
{
    public static class FlowTypeValidator
    {
        public static bool ValidateNodeInput(string receivedType, string inputType, bool strict = false)
        {
            receivedType ??= string.Empty;
            inputType ??= string.Empty;

            if (string.Equals(receivedType, inputType, StringComparison.Ordinal))
            {
                return true;
            }

            if (receivedType == FlowDataType.Any.Key || inputType == FlowDataType.Any.Key)
            {
                return true;
            }

            if (receivedType == FlowDataType.MatchType.Key || inputType == FlowDataType.MatchType.Key)
            {
                return true;
            }

            if (receivedType.Length == 0 || inputType.Length == 0)
            {
                return false;
            }

            var receivedTypes = SplitTypes(receivedType);
            var inputTypes = SplitTypes(inputType);

            if (receivedTypes.Contains(FlowDataType.Any.Key) || inputTypes.Contains(FlowDataType.Any.Key))
            {
                return true;
            }

            if (strict)
            {
                return receivedTypes.IsSubsetOf(inputTypes);
            }

            return receivedTypes.Overlaps(inputTypes);
        }

        private static HashSet<string> SplitTypes(string typeString)
        {
            return new HashSet<string>(
                typeString.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0),
                StringComparer.OrdinalIgnoreCase);
        }
    }
}
