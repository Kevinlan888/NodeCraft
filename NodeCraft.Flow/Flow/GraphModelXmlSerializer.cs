using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace NodeCraft.Flow
{
    public sealed class GraphLoadResult
    {
        public GraphModel Graph { get; set; }

        public int FormatVersion { get; set; }
    }

    public static class GraphModelXmlSerializer
    {
        public const int CurrentFormatVersion = 4;

        public static void Save(GraphModel graph, string filePath, ILogger logger = null)
        {
            if (graph == null)
            {
                throw new ArgumentNullException(nameof(graph));
            }

            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("File path is required.", nameof(filePath));
            }

            GraphModelLinkReconciler.Reconcile(graph);

            var document = new XDocument(
                new XElement("Graph",
                    new XAttribute("FormatVersion", CurrentFormatVersion),
                    new XElement("Nodes", graph.Nodes?.Select(SerializeNode) ?? Enumerable.Empty<XElement>()),
                    new XElement("Links", graph.Links?.Select(SerializeLink) ?? Enumerable.Empty<XElement>())));

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? ".");
            document.Save(filePath);

            logger?.LogInformation("Saved graph to '{FilePath}'.", filePath);
        }

        public static GraphModel Load(string filePath, ILogger logger = null)
        {
            return LoadWithReport(filePath, logger).Graph;
        }

        public static GraphLoadResult LoadWithReport(string filePath, ILogger logger = null)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("File path is required.", nameof(filePath));
            }

            try
            {
                var document = XDocument.Load(filePath);
                var root = document.Root ?? throw new InvalidOperationException("Graph XML is missing the root element.");
                var formatVersion = ReadFormatVersion(root);

                if (formatVersion != CurrentFormatVersion)
                {
                    throw new InvalidOperationException(
                        $"Graph format v{formatVersion} is unsupported. Current format is v{CurrentFormatVersion}.");
                }

                if (root.Element("Connections") != null)
                {
                    throw new InvalidOperationException(
                        "Legacy Connections graphs are unsupported. Use a NodeCraft v4 graph.");
                }

                var nodes = root.Element("Nodes")
                    ?? throw new InvalidOperationException("Graph XML is missing the Nodes element.");
                var links = root.Element("Links")
                    ?? throw new InvalidOperationException("Graph XML is missing the Links element.");

                var graph = new GraphModel
                {
                    Nodes = nodes.Elements("Node").Select(DeserializeNode).ToList(),
                    Links = links.Elements("Link").Select(DeserializeLink).ToList(),
                };
                GraphModelLinkReconciler.Reconcile(graph);

                logger?.LogInformation("Loaded graph from '{FilePath}'.", filePath);

                return new GraphLoadResult
                {
                    Graph = graph,
                    FormatVersion = formatVersion,
                };
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to load graph from '{FilePath}'.", filePath);
                throw;
            }
        }

        private static XElement SerializeNode(NodeModel node)
        {
            return new XElement("Node",
                new XAttribute("ModelType", node.GetType().AssemblyQualifiedName ?? node.GetType().FullName),
                new XAttribute("Id", node.Id ?? string.Empty),
                new XAttribute("Name", node.Name ?? string.Empty),
                new XAttribute("X", node.X.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("Y", node.Y.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("Width", node.Width.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("Height", node.Height.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("ExecutorType", node.ExecutorType ?? string.Empty),
                new XElement("InputPorts", node.InputParameters?.Select(SerializePort) ?? Enumerable.Empty<XElement>()),
                new XElement("OutputPorts", node.OutputParameters?.Select(SerializePort) ?? Enumerable.Empty<XElement>()),
                new XElement("Properties", SerializeCustomProperties(node)));
        }

        private static IEnumerable<XElement> SerializeCustomProperties(NodeModel node)
        {
            foreach (var property in GetCustomSerializableProperties(node.GetType()))
            {
                var value = property.GetValue(node);
                if (value == null)
                {
                    continue;
                }

                yield return new XElement("Property",
                    new XAttribute("Name", property.Name),
                    new XAttribute("Type", property.PropertyType.AssemblyQualifiedName ?? property.PropertyType.FullName),
                    new XAttribute("Value", Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty));
            }
        }

        private static XElement SerializePort(PortParameter port)
        {
            return new XElement("Port",
                new XAttribute("PortId", port.PortId ?? string.Empty),
                new XAttribute("Direction", port.PortDirection.ToString()),
                new XAttribute("ParameterType", port.Parameter?.ParameterType ?? string.Empty),
                new XAttribute("Value", Convert.ToString(port.Parameter?.Value, CultureInfo.InvariantCulture) ?? string.Empty),
                new XAttribute("LinkId", port.LinkId ?? string.Empty));
        }

        private static XElement SerializeLink(GraphLink link)
        {
            return new XElement("Link",
                new XAttribute("Id", link.Id ?? string.Empty),
                new XAttribute("OriginNodeId", link.OriginNodeId ?? string.Empty),
                new XAttribute("OriginSlot", link.OriginSlot.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("TargetNodeId", link.TargetNodeId ?? string.Empty),
                new XAttribute("TargetSlot", link.TargetSlot.ToString(CultureInfo.InvariantCulture)));
        }

        private static NodeModel DeserializeNode(XElement element)
        {
            var typeName = (string)element.Attribute("ModelType");
            var executorType = (string)element.Attribute("ExecutorType");
            var node = CreateNode(executorType, typeName);

            node.Id = (string)element.Attribute("Id") ?? Guid.NewGuid().ToString();
            node.Name = (string)element.Attribute("Name") ?? node.Name;
            node.X = ReadDoubleAttribute(element, "X");
            node.Y = ReadDoubleAttribute(element, "Y");
            node.Width = ReadDoubleAttribute(element, "Width");
            node.Height = ReadDoubleAttribute(element, "Height");
            node.ExecutorType = executorType ?? node.ExecutorType;
            node.InputParameters = element.Element("InputPorts")?.Elements("Port").Select(DeserializePort).ToList() ?? new List<PortParameter>();
            node.OutputParameters = element.Element("OutputPorts")?.Elements("Port").Select(DeserializePort).ToList() ?? new List<PortParameter>();

            foreach (var propertyElement in element.Element("Properties")?.Elements("Property") ?? Enumerable.Empty<XElement>())
            {
                ApplyPropertyValue(node, propertyElement);
            }

            return node;
        }

        private static NodeModel CreateNode(string executorType, string modelTypeName)
        {
            if (!string.IsNullOrWhiteSpace(executorType)
                && NodeExecutorFactory.Registry.TryCreateNodeByTypeKey(executorType, out var registeredNode)
                && registeredNode != null)
            {
                return registeredNode;
            }

            var nodeType = ResolveNodeType(modelTypeName);
            return (NodeModel)Activator.CreateInstance(nodeType);
        }

        private static PortParameter DeserializePort(XElement element)
        {
            return new PortParameter
            {
                PortId = (string)element.Attribute("PortId"),
                LinkId = (string)element.Attribute("LinkId"),
                PortDirection = ParseEnum((string)element.Attribute("Direction"), EPortDirection.None),
                Parameter = new Parameter
                {
                    ParameterType = (string)element.Attribute("ParameterType"),
                    Value = (string)element.Attribute("Value")
                }
            };
        }

        private static GraphLink DeserializeLink(XElement element)
        {
            return new GraphLink
            {
                Id = (string)element.Attribute("Id") ?? Guid.NewGuid().ToString(),
                OriginNodeId = (string)element.Attribute("OriginNodeId"),
                OriginSlot = ReadRequiredIntAttribute(element, "OriginSlot"),
                TargetNodeId = (string)element.Attribute("TargetNodeId"),
                TargetSlot = ReadRequiredIntAttribute(element, "TargetSlot"),
            };
        }

        private static Type ResolveNodeType(string typeName)
        {
            var nodeType = !string.IsNullOrWhiteSpace(typeName) ? Type.GetType(typeName, throwOnError: false) : null;

            if (nodeType == null
                && NodeExecutorFactory.Registry.TryCreateNode(typeName, out var registeredNode))
            {
                nodeType = registeredNode.GetType();
            }

            if (nodeType == null || !typeof(NodeModel).IsAssignableFrom(nodeType))
            {
                throw new InvalidOperationException($"Unable to resolve node model type '{typeName}'.");
            }

            return nodeType;
        }

        private static void ApplyPropertyValue(NodeModel node, XElement propertyElement)
        {
            var propertyName = (string)propertyElement.Attribute("Name");
            if (string.IsNullOrWhiteSpace(propertyName))
            {
                return;
            }

            var property = node.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (property == null || !property.CanWrite)
            {
                return;
            }

            var rawValue = (string)propertyElement.Attribute("Value");
            var convertedValue = ConvertStringValue(rawValue, property.PropertyType);
            property.SetValue(node, convertedValue);
        }

        private static IEnumerable<PropertyInfo> GetCustomSerializableProperties(Type nodeType)
        {
            var basePropertyNames = new HashSet<string>(typeof(NodeModel)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Select(property => property.Name), StringComparer.Ordinal);

            return nodeType
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property => property.CanRead && property.CanWrite)
                .Where(property => !basePropertyNames.Contains(property.Name))
                .Where(property => IsSupportedPropertyType(property.PropertyType));
        }

        private static bool IsSupportedPropertyType(Type propertyType)
        {
            var actualType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
            return actualType.IsEnum
                || actualType == typeof(string)
                || actualType == typeof(bool)
                || actualType == typeof(int)
                || actualType == typeof(long)
                || actualType == typeof(double)
                || actualType == typeof(float)
                || actualType == typeof(decimal);
        }

        private static object ConvertStringValue(string rawValue, Type targetType)
        {
            var actualType = Nullable.GetUnderlyingType(targetType) ?? targetType;

            if (actualType == typeof(string))
            {
                return rawValue;
            }

            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return Nullable.GetUnderlyingType(targetType) != null ? null : Activator.CreateInstance(actualType);
            }

            if (actualType.IsEnum)
            {
                return Enum.Parse(actualType, rawValue, ignoreCase: true);
            }

            return Convert.ChangeType(rawValue, actualType, CultureInfo.InvariantCulture);
        }

        private static double ReadDoubleAttribute(XElement element, string attributeName)
        {
            var rawValue = (string)element.Attribute(attributeName);
            if (double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }

            return 0;
        }

        private static int ReadRequiredIntAttribute(XElement element, string attributeName)
        {
            var rawValue = (string)element.Attribute(attributeName);
            if (int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }

            throw new InvalidOperationException(
                $"Link attribute '{attributeName}' must be a valid integer, but was '{rawValue ?? string.Empty}'.");
        }

        private static int ReadFormatVersion(XElement root)
        {
            var rawValue = (string)root.Attribute("FormatVersion");
            if (int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0)
            {
                return value;
            }

            return 1;
        }

        private static TEnum ParseEnum<TEnum>(string rawValue, TEnum fallback)
            where TEnum : struct
        {
            if (Enum.TryParse<TEnum>(rawValue, true, out var value))
            {
                return value;
            }

            return fallback;
        }
    }
}
