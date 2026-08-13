using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace NodeCraft.Flow
{
    public class FlowNodeRegistration
    {
        public FlowNodeRegistration(FlowNodeDefinition definition, Func<IFlowNodeExecutor> executorFactory)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            ExecutorFactory = executorFactory ?? throw new ArgumentNullException(nameof(executorFactory));
        }

        public FlowNodeDefinition Definition { get; }

        public Func<IFlowNodeExecutor> ExecutorFactory { get; }

        public Type NodeModelType { get; set; }

        public Func<NodeModel> NodeFactory { get; set; }

        public string PaletteDisplayName { get; set; }

        public string PaletteDescription { get; set; }

        public bool ShowInPalette { get; set; } = true;

        public bool IsPaletteCategoryExpanded { get; set; } = true;

        public Func<FlowCanvas, NodeModel, FrameworkElement> ContentFactory { get; set; }

        public Action<NodeModel, FlowExecutionContext> ExecutionResultHandler { get; set; }
    }

    public class FlowNodeRegistry
    {
        private readonly Dictionary<string, FlowNodeRegistration> _registrations = new Dictionary<string, FlowNodeRegistration>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _typeKeyByNodeTypeName = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<string> _registrationOrder = new List<string>();
        private readonly ConditionalWeakTable<FlowCanvas, DefaultFlowNodeContentFactory> _defaultContentFactories = new ConditionalWeakTable<FlowCanvas, DefaultFlowNodeContentFactory>();

        public void Register(FlowNodeRegistration registration)
        {
            if (registration == null)
            {
                throw new ArgumentNullException(nameof(registration));
            }

            ApplyRegistration(registration);
        }

        public void RegisterPlugin(string pluginId, IReadOnlyList<FlowNodeRegistration> registrations)
        {
            PluginMetadata.ValidateId(pluginId, nameof(pluginId));

            if (registrations == null)
            {
                throw new ArgumentNullException(nameof(registrations));
            }

            var batchTypeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var batchNodeTypeMappings = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var registration in registrations)
            {
                ValidatePluginRegistration(registration, batchTypeKeys, batchNodeTypeMappings);
            }

            foreach (var registration in registrations)
            {
                ApplyRegistration(registration);
            }
        }

        private void ApplyRegistration(FlowNodeRegistration registration)
        {
            EnsureControlInputPort(registration.Definition);

            if (!_registrations.ContainsKey(registration.Definition.TypeKey))
            {
                _registrationOrder.Add(registration.Definition.TypeKey);
            }

            _registrations[registration.Definition.TypeKey] = registration;

            if (registration.NodeModelType != null)
            {
                RegisterNodeTypeMapping(registration.NodeModelType, registration.Definition.TypeKey);
            }
        }

        public void RegisterNode(
            FlowNodeRegistration registration,
            Type nodeModelType,
            Func<NodeModel> nodeFactory,
            string paletteDescription = null,
            bool showInPalette = true,
            bool isPaletteCategoryExpanded = true,
            string paletteDisplayName = null,
            Func<FlowCanvas, NodeModel, FrameworkElement> contentFactory = null,
            Action<NodeModel, FlowExecutionContext> executionResultHandler = null)
        {
            Register(registration);
            ConfigureNodeEditor(
                registration.Definition.TypeKey,
                nodeModelType,
                nodeFactory,
                paletteDescription,
                showInPalette,
                isPaletteCategoryExpanded,
                paletteDisplayName,
                contentFactory,
                executionResultHandler);
        }

        public void ConfigureNodeEditor(
            string typeKey,
            Type nodeModelType,
            Func<NodeModel> nodeFactory,
            string paletteDescription = null,
            bool showInPalette = true,
            bool isPaletteCategoryExpanded = true,
            string paletteDisplayName = null,
            Func<FlowCanvas, NodeModel, FrameworkElement> contentFactory = null,
            Action<NodeModel, FlowExecutionContext> executionResultHandler = null)
        {
            var registration = Resolve(typeKey);
            registration.NodeModelType = nodeModelType ?? throw new ArgumentNullException(nameof(nodeModelType));
            registration.NodeFactory = nodeFactory ?? throw new ArgumentNullException(nameof(nodeFactory));
            registration.PaletteDescription = paletteDescription ?? string.Empty;
            registration.ShowInPalette = showInPalette;
            registration.IsPaletteCategoryExpanded = isPaletteCategoryExpanded;
            registration.PaletteDisplayName = string.IsNullOrWhiteSpace(paletteDisplayName)
                ? registration.Definition.DisplayName
                : paletteDisplayName;
            registration.ContentFactory = contentFactory;
            registration.ExecutionResultHandler = executionResultHandler;

            RegisterNodeTypeMapping(nodeModelType, typeKey);
        }

        public bool Contains(string typeKey)
        {
            return _registrations.ContainsKey(typeKey);
        }

        public bool TryResolve(string typeKey, out FlowNodeRegistration registration)
        {
            return _registrations.TryGetValue(typeKey, out registration);
        }

        public FlowNodeRegistration Resolve(string typeKey)
        {
            if (!_registrations.TryGetValue(typeKey, out var registration))
            {
                throw new KeyNotFoundException($"Flow node type '{typeKey}' is not registered.");
            }

            return registration;
        }

        public bool TryResolveByNodeTypeName(string nodeTypeName, out FlowNodeRegistration registration)
        {
            registration = null;
            if (string.IsNullOrWhiteSpace(nodeTypeName))
            {
                return false;
            }

            if (_typeKeyByNodeTypeName.TryGetValue(nodeTypeName, out var typeKey)
                && _registrations.TryGetValue(typeKey, out registration))
            {
                return true;
            }

            var shortName = nodeTypeName.Split(',')[0].Trim();
            return shortName.Length > 0
                && _typeKeyByNodeTypeName.TryGetValue(shortName, out typeKey)
                && _registrations.TryGetValue(typeKey, out registration);
        }

        public bool TryCreateNode(string nodeTypeName, out NodeModel node)
        {
            node = null;
            if (!TryResolveByNodeTypeName(nodeTypeName, out var registration) || registration.NodeFactory == null)
            {
                return false;
            }

            node = registration.NodeFactory();
            return node != null;
        }

        public bool TryCreateNodeByTypeKey(string typeKey, out NodeModel node)
        {
            node = null;
            if (!TryResolve(typeKey, out var registration) || registration.NodeFactory == null)
            {
                return false;
            }

            node = registration.NodeFactory();
            return node != null;
        }

        public string GetDisplayName(string typeKey)
        {
            return TryResolve(typeKey, out var registration)
                ? registration.PaletteDisplayName ?? registration.Definition.DisplayName ?? typeKey
                : typeKey;
        }

        public object BuildNodeContent(FlowCanvas canvas, NodeModel node)
        {
            if (node == null || canvas == null)
            {
                return node?.Name;
            }

            if (TryResolve(node.ExecutorType, out var registration) && registration.ContentFactory != null)
            {
                return registration.ContentFactory(canvas, node);
            }

            return _defaultContentFactories.GetValue(canvas, key => new DefaultFlowNodeContentFactory(key)).Build(node);
        }

        public IList<FlowNodePaletteCategory> CreatePaletteCategories()
        {
            var categories = new List<FlowNodePaletteCategory>();
            var categoryLookup = new Dictionary<string, FlowNodePaletteCategory>(StringComparer.OrdinalIgnoreCase);

            foreach (var typeKey in _registrationOrder)
            {
                if (!_registrations.TryGetValue(typeKey, out var registration)
                    || !registration.ShowInPalette
                    || registration.NodeModelType == null
                    || registration.NodeFactory == null)
                {
                    continue;
                }

                var categoryName = string.IsNullOrWhiteSpace(registration.Definition.Category)
                    ? "Other"
                    : registration.Definition.Category;

                if (!categoryLookup.TryGetValue(categoryName, out var category))
                {
                    category = new FlowNodePaletteCategory
                    {
                        Title = categoryName,
                        IconKind = ResolveCategoryIconKind(categoryName),
                        IsExpanded = categories.Count == 0,
                    };
                    categoryLookup[categoryName] = category;
                    categories.Add(category);
                }

                category.Items.Add(new FlowNodePaletteItem
                {
                    DisplayName = string.IsNullOrWhiteSpace(registration.PaletteDisplayName)
                        ? registration.Definition.DisplayName
                        : registration.PaletteDisplayName,
                    Description = registration.PaletteDescription ?? string.Empty,
                    IconKind = ResolveNodeIconKind(typeKey, categoryName),
                    TypeKey = typeKey,
                    NodeTypeName = registration.NodeModelType.AssemblyQualifiedName,
                });
            }

            return categories;
        }

        public IList<NodeModel> ApplyExecutionResults(IEnumerable<NodeModel> nodes, FlowExecutionContext context)
        {
            var updatedNodes = new List<NodeModel>();
            if (nodes == null || context == null)
            {
                return updatedNodes;
            }

            foreach (var node in nodes)
            {
                if (node == null)
                {
                    continue;
                }

                if (TryResolve(node.ExecutorType, out var registration) && registration.ExecutionResultHandler != null)
                {
                    registration.ExecutionResultHandler(node, context);
                    updatedNodes.Add(node);
                }
            }

            return updatedNodes;
        }

        private void ValidatePluginRegistration(
            FlowNodeRegistration registration,
            ISet<string> batchTypeKeys,
            IDictionary<string, string> batchNodeTypeMappings)
        {
            if (registration == null)
            {
                throw new ArgumentNullException(nameof(registration));
            }

            if (registration.Definition == null)
            {
                throw new InvalidOperationException("Plugin node registrations must include a definition.");
            }

            if (registration.ExecutorFactory == null)
            {
                throw new InvalidOperationException(
                    $"Plugin node '{registration.Definition.TypeKey}' must include an executor factory.");
            }

            if (string.IsNullOrWhiteSpace(registration.Definition.TypeKey))
            {
                throw new InvalidOperationException("Plugin node registrations must include a non-empty TypeKey.");
            }

            if (!batchTypeKeys.Add(registration.Definition.TypeKey))
            {
                throw new InvalidOperationException(
                    $"Plugin node type '{registration.Definition.TypeKey}' is duplicated within the staged batch.");
            }

            if (_registrations.ContainsKey(registration.Definition.TypeKey))
            {
                throw new InvalidOperationException(
                    $"Flow node type '{registration.Definition.TypeKey}' is already registered.");
            }

            if (registration.ShowInPalette)
            {
                if (registration.NodeModelType == null)
                {
                    throw new InvalidOperationException(
                        $"Plugin node '{registration.Definition.TypeKey}' must provide a node model type when shown in the palette.");
                }

                if (registration.NodeFactory == null)
                {
                    throw new InvalidOperationException(
                        $"Plugin node '{registration.Definition.TypeKey}' must provide a node factory when shown in the palette.");
                }
            }

            ValidatePluginNodeTypeMapping(registration.NodeModelType, registration.Definition.TypeKey, batchNodeTypeMappings);
        }

        private static string ResolveCategoryIconKind(string categoryName)
        {
            switch (categoryName)
            {
                case "Preview": return "ViewDashboardOutline";
                case "Value": return "FormatListNumbered";
                case "Math": return "CalculatorVariant";
                case "Logic": return "SourceBranch";
                default: return "ShapeOutline";
            }
        }

        private static string ResolveNodeIconKind(string typeKey, string categoryName)
        {
            switch (typeKey)
            {
                case "node.string-value": return "FormatText";
                case "node.integer-value":
                case "node.float-value": return "Numeric";
                case "node.boolean-value": return "ToggleSwitchOutline";
                case "node.add-number": return "Plus";
                case "node.subtract-number": return "Minus";
                case "node.multiply-number": return "Close";
                case "node.divide-number": return "DivisionBox";
                case "node.image-preview": return "ImageOutline";
                case "node.text-preview": return "EyeOutline";
                case "node.if": return "SourceBranch";
                default: return ResolveCategoryIconKind(categoryName);
            }
        }

        private void RegisterNodeTypeMapping(Type nodeModelType, string typeKey)
        {
            if (nodeModelType == null || string.IsNullOrWhiteSpace(typeKey))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(nodeModelType.AssemblyQualifiedName))
            {
                _typeKeyByNodeTypeName[nodeModelType.AssemblyQualifiedName] = typeKey;
            }

            if (!string.IsNullOrWhiteSpace(nodeModelType.FullName))
            {
                _typeKeyByNodeTypeName[nodeModelType.FullName] = typeKey;
            }
        }

        private void ValidatePluginNodeTypeMapping(
            Type nodeModelType,
            string typeKey,
            IDictionary<string, string> batchNodeTypeMappings)
        {
            if (nodeModelType == null || string.IsNullOrWhiteSpace(typeKey))
            {
                return;
            }

            ValidatePluginNodeTypeNameMapping(nodeModelType.AssemblyQualifiedName, typeKey, batchNodeTypeMappings);
            ValidatePluginNodeTypeNameMapping(nodeModelType.FullName, typeKey, batchNodeTypeMappings);
        }

        private void ValidatePluginNodeTypeNameMapping(
            string nodeTypeName,
            string typeKey,
            IDictionary<string, string> batchNodeTypeMappings)
        {
            if (string.IsNullOrWhiteSpace(nodeTypeName))
            {
                return;
            }

            if (_typeKeyByNodeTypeName.TryGetValue(nodeTypeName, out var existingTypeKey)
                && !string.Equals(existingTypeKey, typeKey, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Node model type '{nodeTypeName}' is already mapped to flow node type '{existingTypeKey}'.");
            }

            if (batchNodeTypeMappings.TryGetValue(nodeTypeName, out existingTypeKey)
                && !string.Equals(existingTypeKey, typeKey, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Node model type '{nodeTypeName}' is staged for multiple flow node types.");
            }

            batchNodeTypeMappings[nodeTypeName] = typeKey;
        }

        private static void EnsureControlInputPort(FlowNodeDefinition definition)
        {
            if (definition?.InputPorts == null)
            {
                return;
            }

            if (definition.InputPorts.Any(port => string.Equals(port.Id, FlowPorts.FlowIn, StringComparison.Ordinal)))
            {
                return;
            }

            definition.InputPorts.Insert(0, new FlowPortDefinition
            {
                Id = FlowPorts.FlowIn,
                DisplayName = "Flow In",
                IOType = EIOType.Input,
                DataType = FlowDataType.Control,
                PreferredDirection = EPortDirection.Top,
                IsRequired = false,
                AllowMultipleConnections = false,
            });
        }
    }
}
