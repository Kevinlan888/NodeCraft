using System.Collections.Generic;
using NodeCraft.BuiltIn.Nodes;
using NodeCraft.BuiltIn.Views;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Registrations
{
    internal static class ValueNodeRegistrations
    {
        internal static IReadOnlyList<FlowNodeRegistration> CreateAll()
        {
            return new[]
            {
                CreateIntegerValue(),
                CreateFloatValue(),
                CreateBooleanValue(),
            };
        }

        private static FlowNodeRegistration CreateIntegerValue()
        {
            return CreateValueNode(
                IntegerValueNodeModel.FlowNodeTypeKey,
                "Integer Value",
                "固定整数输出",
                FlowDataType.Number,
                "Numeric",
                typeof(IntegerValueNodeModel),
                () => new IntegerValueNodeModel(),
                () => new IntegerValueExecutor(),
                IntegerValueEditor.CreateContent);
        }

        private static FlowNodeRegistration CreateFloatValue()
        {
            return CreateValueNode(
                FloatValueNodeModel.FlowNodeTypeKey,
                "Float Value",
                "固定浮点数输出",
                FlowDataType.Number,
                "Numeric",
                typeof(FloatValueNodeModel),
                () => new FloatValueNodeModel(),
                () => new FloatValueExecutor(),
                FloatValueEditor.CreateContent);
        }

        private static FlowNodeRegistration CreateBooleanValue()
        {
            return CreateValueNode(
                BooleanValueNodeModel.FlowNodeTypeKey,
                "Boolean Value",
                "固定布尔输出",
                FlowDataType.Boolean,
                "ToggleSwitchOutline",
                typeof(BooleanValueNodeModel),
                () => new BooleanValueNodeModel(),
                () => new BooleanValueExecutor(),
                BooleanValueEditor.CreateContent);
        }

        private static FlowNodeRegistration CreateValueNode(
            string typeKey,
            string displayName,
            string description,
            FlowDataType outputType,
            string icon,
            System.Type modelType,
            System.Func<NodeModel> nodeFactory,
            System.Func<IFlowNodeExecutor> executorFactory,
            System.Func<FlowCanvas, NodeModel, System.Windows.FrameworkElement> contentFactory)
        {
            return new FlowNodeRegistration(
                new FlowNodeDefinition
                {
                    TypeKey = typeKey,
                    DisplayName = displayName,
                    Category = "Value",
                    OutputPorts =
                    {
                        new FlowPortDefinition
                        {
                            Id = BuiltInPortIds.Output,
                            DisplayName = "Value",
                            IOType = EIOType.Output,
                            DataType = outputType,
                            PreferredDirection = EPortDirection.Right,
                        },
                    },
                },
                executorFactory)
            {
                NodeModelType = modelType,
                NodeFactory = nodeFactory,
                PaletteDisplayName = displayName,
                PaletteDescription = description,
                PaletteCategoryIconKind = "FormatListNumbered",
                PaletteIconKind = icon,
                ContentFactory = contentFactory,
            };
        }
    }
}
