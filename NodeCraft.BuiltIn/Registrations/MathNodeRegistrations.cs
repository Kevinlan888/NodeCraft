using System;
using System.Collections.Generic;
using System.Windows;
using NodeCraft.BuiltIn.Nodes;
using NodeCraft.BuiltIn.Views;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Registrations
{
    internal static class MathNodeRegistrations
    {
        internal static IReadOnlyList<FlowNodeRegistration> CreateAll()
        {
            return new[]
            {
                CreateMathNode(AddNumberNodeModel.FlowNodeTypeKey, "Add", "A + B", "Plus", "Sum", typeof(AddNumberNodeModel), () => new AddNumberNodeModel(), () => new AddNumberExecutor(), AddNumberView.CreateContent),
                CreateMathNode(MultiplyNumberNodeModel.FlowNodeTypeKey, "Multiply", "A * B", "Close", "Product", typeof(MultiplyNumberNodeModel), () => new MultiplyNumberNodeModel(), () => new MultiplyNumberExecutor(), MultiplyNumberView.CreateContent),
                CreateMathNode(SubtractNumberNodeModel.FlowNodeTypeKey, "Subtract", "A - B", "Minus", "Difference", typeof(SubtractNumberNodeModel), () => new SubtractNumberNodeModel(), () => new SubtractNumberExecutor(), SubtractNumberView.CreateContent),
                CreateMathNode(DivideNumberNodeModel.FlowNodeTypeKey, "Divide", "A / B", "DivisionBox", "Quotient", typeof(DivideNumberNodeModel), () => new DivideNumberNodeModel(), () => new DivideNumberExecutor(), DivideNumberView.CreateContent),
            };
        }

        private static FlowNodeRegistration CreateMathNode(
            string typeKey,
            string displayName,
            string description,
            string icon,
            string outputName,
            Type modelType,
            Func<NodeModel> nodeFactory,
            Func<IFlowNodeExecutor> executorFactory,
            Func<FlowCanvas, NodeModel, FrameworkElement> contentFactory)
        {
            return new FlowNodeRegistration(
                new FlowNodeDefinition
                {
                    TypeKey = typeKey,
                    DisplayName = displayName,
                    Category = "Math",
                    InputPorts =
                    {
                        Input(BuiltInPortIds.InputA, "A"),
                        Input(BuiltInPortIds.InputB, "B"),
                    },
                    OutputPorts =
                    {
                        new FlowPortDefinition
                        {
                            Id = BuiltInPortIds.Output,
                            DisplayName = outputName,
                            IOType = EIOType.Output,
                            DataType = FlowDataType.Number,
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
                PaletteCategoryIconKind = "CalculatorVariant",
                PaletteIconKind = icon,
                ContentFactory = contentFactory,
            };
        }

        private static FlowPortDefinition Input(string id, string displayName)
        {
            return new FlowPortDefinition
            {
                Id = id,
                DisplayName = displayName,
                IOType = EIOType.Input,
                DataType = FlowDataType.Number,
                PreferredDirection = EPortDirection.Left,
                IsRequired = true,
            };
        }
    }
}
