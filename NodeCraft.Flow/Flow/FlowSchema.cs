using System;
using System.Collections.Generic;
using System.Linq;

namespace NodeCraft.Flow
{
    public enum FlowPortAvailability
    {
        Iteration,
        Session,
    }

    public class FlowDataType : IEquatable<FlowDataType>
    {
        public static readonly FlowDataType String = new FlowDataType("string", typeof(string));
        public static readonly FlowDataType Number = new FlowDataType("number", typeof(double));
        public static readonly FlowDataType Boolean = new FlowDataType("boolean", typeof(bool));
        public static readonly FlowDataType Object = new FlowDataType("object", typeof(object));
        public static readonly FlowDataType Any = new FlowDataType("*", typeof(object));
        public static readonly FlowDataType MatchType = new FlowDataType("MATCH_TYPE", typeof(object));
        public static readonly FlowDataType Control = new FlowDataType("control", typeof(FlowControlSignal));
        public static readonly FlowDataType Image = new FlowDataType("image", typeof(FlowImage));
        public static readonly FlowDataType CameraCalibration = new FlowDataType(
            "camera-calibration",
            typeof(NodeCraft.Flow.CameraCalibration));

        public FlowDataType(string key, Type clrType = null)
        {
            Key = string.IsNullOrWhiteSpace(key) ? Object.Key : key.Trim();
            ClrType = clrType ?? typeof(object);
        }

        public string Key { get; }

        public Type ClrType { get; }

        public bool IsCompatibleWith(FlowDataType other)
        {
            if (other == null)
            {
                return false;
            }

            if (IsControlType(this) || IsControlType(other))
            {
                return IsControlType(this) && IsControlType(other);
            }

            // 保留旧 object 通配语义：TextPreview 等节点用 object 接受任意数据。
            if (Equals(this, Object) || Equals(other, Object))
            {
                return true;
            }

            return FlowTypeValidator.ValidateNodeInput(Key, other.Key, strict: false);
        }

        public bool AcceptsValue(object value)
        {
            if (value == null)
            {
                return true;
            }

            if (string.Equals(Key, Number.Key, StringComparison.OrdinalIgnoreCase))
            {
                return value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal;
            }

            if (string.Equals(Key, Boolean.Key, StringComparison.OrdinalIgnoreCase))
            {
                return value is bool;
            }

            if (string.Equals(Key, Control.Key, StringComparison.OrdinalIgnoreCase))
            {
                return value is FlowControlSignal;
            }

            return ClrType == typeof(object) || ClrType.IsInstanceOfType(value);
        }

        public bool Equals(FlowDataType other)
        {
            if (ReferenceEquals(null, other))
            {
                return false;
            }

            return string.Equals(Key, other.Key, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as FlowDataType);
        }

        public override int GetHashCode()
        {
            return StringComparer.OrdinalIgnoreCase.GetHashCode(Key);
        }

        public override string ToString()
        {
            return Key;
        }

        public static FlowDataType FromLegacyTypeName(string typeName)
        {
            if (string.Equals(typeName, nameof(String), StringComparison.OrdinalIgnoreCase)
                || string.Equals(typeName, String.Key, StringComparison.OrdinalIgnoreCase))
            {
                return String;
            }

            if (string.Equals(typeName, nameof(Double), StringComparison.OrdinalIgnoreCase)
                || string.Equals(typeName, nameof(Single), StringComparison.OrdinalIgnoreCase)
                || string.Equals(typeName, nameof(Int32), StringComparison.OrdinalIgnoreCase)
                || string.Equals(typeName, nameof(Int64), StringComparison.OrdinalIgnoreCase)
                || string.Equals(typeName, Number.Key, StringComparison.OrdinalIgnoreCase))
            {
                return Number;
            }

            if (string.Equals(typeName, nameof(Boolean), StringComparison.OrdinalIgnoreCase)
                || string.Equals(typeName, Boolean.Key, StringComparison.OrdinalIgnoreCase)
                || string.Equals(typeName, "bool", StringComparison.OrdinalIgnoreCase))
            {
                return Boolean;
            }

            if (string.Equals(typeName, Control.Key, StringComparison.OrdinalIgnoreCase))
            {
                return Control;
            }

            if (string.Equals(typeName, Image.Key, StringComparison.OrdinalIgnoreCase))
            {
                return Image;
            }

            if (string.Equals(typeName, CameraCalibration.Key, StringComparison.OrdinalIgnoreCase))
            {
                return CameraCalibration;
            }

            return new FlowDataType(typeName);
        }

        private static bool IsControlType(FlowDataType dataType)
        {
            return dataType != null && string.Equals(dataType.Key, Control.Key, StringComparison.OrdinalIgnoreCase);
        }
    }

    public class FlowPortDefinition
    {
        public string Id { get; set; }

        public string DisplayName { get; set; }

        public EIOType IOType { get; set; }

        public FlowDataType DataType { get; set; } = FlowDataType.Object;

        public EPortDirection PreferredDirection { get; set; } = EPortDirection.None;

        public bool IsRequired { get; set; }

        public bool AllowMultipleConnections { get; set; }

        public bool IsDynamic { get; set; }

        public object DefaultValue { get; set; }

        public FlowPortAvailability Availability { get; set; }
            = FlowPortAvailability.Iteration;

        public bool IsControlPort => DataType != null && DataType.Equals(FlowDataType.Control);
    }

    public class FlowDynamicInputTemplate
    {
        public string PortIdPrefix { get; set; } = "input";

        public string DisplayNamePrefix { get; set; } = "Input";

        public FlowDataType DataType { get; set; } = FlowDataType.Object;

        public EPortDirection PreferredDirection { get; set; } = EPortDirection.Left;

        public bool IsRequired { get; set; }

        public object DefaultValue { get; set; }

        public FlowPortAvailability Availability { get; set; }
            = FlowPortAvailability.Iteration;

        public int MinCount { get; set; }

        public int InitialCount { get; set; }

        public int? MaxCount { get; set; }
    }

    public class FlowNodeDefinition
    {
        public FlowNodeDefinition()
        {
            InputPorts = new List<FlowPortDefinition>();
            OutputPorts = new List<FlowPortDefinition>();
        }

        public string TypeKey { get; set; }

        public string DisplayName { get; set; }

        public string Category { get; set; }

        public int Version { get; set; } = 1;

        public List<FlowPortDefinition> InputPorts { get; set; }

        public List<FlowPortDefinition> OutputPorts { get; set; }

        public FlowDynamicInputTemplate DynamicInputTemplate { get; set; }

        public FlowPortDefinition GetInputPort(string portId)
        {
            return InputPorts.FirstOrDefault(port => string.Equals(port.Id, portId, StringComparison.Ordinal));
        }

        public FlowPortDefinition GetOutputPort(string portId)
        {
            return OutputPorts.FirstOrDefault(port => string.Equals(port.Id, portId, StringComparison.Ordinal));
        }
    }
}
