using System;
using System.Collections.Generic;
using System.Windows;
using NodeCraft.BuiltIn.Nodes;
using NodeCraft.BuiltIn.Views;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Registrations
{
    internal static class LogicNodeRegistrations
    {
        internal static IReadOnlyList<FlowNodeRegistration> CreateAll()
        {
            return new[]
            {
                CreateBinary(GreaterThanNodeModel.FlowNodeTypeKey, "Greater Than", "A > B", FlowDataType.Number, typeof(GreaterThanNodeModel), () => new GreaterThanNodeModel(), () => new GreaterThanExecutor(), GreaterThanView.CreateContent),
                CreateBinary(LessThanNodeModel.FlowNodeTypeKey, "Less Than", "A < B", FlowDataType.Number, typeof(LessThanNodeModel), () => new LessThanNodeModel(), () => new LessThanExecutor(), LessThanView.CreateContent),
                CreateBinary(EqualNodeModel.FlowNodeTypeKey, "Equal", "A == B", FlowDataType.Object, typeof(EqualNodeModel), () => new EqualNodeModel(), () => new EqualExecutor(), EqualView.CreateContent),
                CreateBinary(BooleanAndNodeModel.FlowNodeTypeKey, "Boolean And", "A && B", FlowDataType.Boolean, typeof(BooleanAndNodeModel), () => new BooleanAndNodeModel(), () => new BooleanAndExecutor(), BooleanAndView.CreateContent),
                CreateBinary(BooleanOrNodeModel.FlowNodeTypeKey, "Boolean Or", "A || B", FlowDataType.Boolean, typeof(BooleanOrNodeModel), () => new BooleanOrNodeModel(), () => new BooleanOrExecutor(), BooleanOrView.CreateContent),
                CreateBooleanNot(),
                CreateIf(),
                CreateBinary(NotEqualNodeModel.FlowNodeTypeKey, "!=", "A != B", FlowDataType.Object, typeof(NotEqualNodeModel), () => new NotEqualNodeModel(), () => new NotEqualExecutor(), NotEqualView.CreateContent),
                CreateBinary(GreaterThanOrEqualNodeModel.FlowNodeTypeKey, ">=", "A >= B", FlowDataType.Number, typeof(GreaterThanOrEqualNodeModel), () => new GreaterThanOrEqualNodeModel(), () => new GreaterThanOrEqualExecutor(), GreaterThanOrEqualView.CreateContent),
                CreateBinary(LessThanOrEqualNodeModel.FlowNodeTypeKey, "<=", "A <= B", FlowDataType.Number, typeof(LessThanOrEqualNodeModel), () => new LessThanOrEqualNodeModel(), () => new LessThanOrEqualExecutor(), LessThanOrEqualView.CreateContent),
                CreateSelect(),
                CreateMergeFlow(),
            };
        }

        private static FlowNodeRegistration CreateBinary(
            string typeKey,
            string displayName,
            string description,
            FlowDataType inputType,
            Type modelType,
            Func<NodeModel> nodeFactory,
            Func<IFlowNodeExecutor> executorFactory,
            Func<FlowCanvas, NodeModel, FrameworkElement> contentFactory)
        {
            return CreateRegistration(
                new FlowNodeDefinition
                {
                    TypeKey = typeKey,
                    DisplayName = displayName,
                    Category = "Logic",
                    InputPorts =
                    {
                        Input(BuiltInPortIds.InputA, "A", inputType),
                        Input(BuiltInPortIds.InputB, "B", inputType),
                    },
                    OutputPorts =
                    {
                        Output(BuiltInPortIds.Output, "Result", FlowDataType.Boolean),
                    },
                },
                description,
                modelType,
                nodeFactory,
                executorFactory,
                contentFactory);
        }

        private static FlowNodeRegistration CreateBooleanNot()
        {
            return CreateRegistration(
                new FlowNodeDefinition
                {
                    TypeKey = BooleanNotNodeModel.FlowNodeTypeKey,
                    DisplayName = "Boolean Not",
                    Category = "Logic",
                    InputPorts =
                    {
                        Input(BuiltInPortIds.Input, "Input", FlowDataType.Boolean),
                    },
                    OutputPorts =
                    {
                        Output(BuiltInPortIds.Output, "Result", FlowDataType.Boolean),
                    },
                },
                "!A",
                typeof(BooleanNotNodeModel),
                () => new BooleanNotNodeModel(),
                () => new BooleanNotExecutor(),
                BooleanNotView.CreateContent);
        }

        private static FlowNodeRegistration CreateIf()
        {
            return CreateRegistration(
                new FlowNodeDefinition
                {
                    TypeKey = IfNodeModel.FlowNodeTypeKey,
                    DisplayName = "If",
                    Category = "Logic",
                    InputPorts =
                    {
                        Input(BuiltInPortIds.Condition, "Condition", FlowDataType.Boolean),
                    },
                    OutputPorts =
                    {
                        Output(BuiltInPortIds.True, "True", FlowDataType.Control),
                        Output(BuiltInPortIds.False, "False", FlowDataType.Control),
                    },
                },
                "condition ? true : false",
                typeof(IfNodeModel),
                () => new IfNodeModel(),
                () => new IfExecutor(),
                IfView.CreateContent);
        }

        private static FlowNodeRegistration CreateSelect()
        {
            return CreateRegistration(
                new FlowNodeDefinition
                {
                    TypeKey = SelectNodeModel.FlowNodeTypeKey,
                    DisplayName = "Select",
                    Category = "Logic",
                    InputPorts =
                    {
                        Input(BuiltInPortIds.Condition, "Condition", FlowDataType.Boolean),
                        Input(BuiltInPortIds.TrueValue, "True Value", FlowDataType.Object),
                        Input(BuiltInPortIds.FalseValue, "False Value", FlowDataType.Object),
                    },
                    OutputPorts =
                    {
                        Output(BuiltInPortIds.Output, "Result", FlowDataType.Object),
                    },
                },
                "condition ? trueValue : falseValue",
                typeof(SelectNodeModel),
                () => new SelectNodeModel(),
                () => new SelectExecutor(),
                SelectView.CreateContent);
        }

        private static FlowNodeRegistration CreateMergeFlow()
        {
            return CreateRegistration(
                new FlowNodeDefinition
                {
                    TypeKey = MergeFlowNodeModel.FlowNodeTypeKey,
                    DisplayName = "Merge Flow",
                    Category = "Logic",
                    OutputPorts =
                    {
                        Output(BuiltInPortIds.FlowOut, "Flow Out", FlowDataType.Control),
                    },
                    DynamicInputTemplate = new FlowDynamicInputTemplate
                    {
                        PortIdPrefix = "branch",
                        DisplayNamePrefix = "Branch",
                        DataType = FlowDataType.Control,
                        PreferredDirection = EPortDirection.Left,
                        IsRequired = false,
                        Availability = FlowPortAvailability.Iteration,
                        MinCount = 2,
                        InitialCount = 2,
                        MaxCount = null,
                    },
                },
                "merge active control branches",
                typeof(MergeFlowNodeModel),
                () => new MergeFlowNodeModel(),
                () => new MergeFlowExecutor(),
                MergeFlowView.CreateContent);
        }
        private static FlowNodeRegistration CreateRegistration(
            FlowNodeDefinition definition,
            string description,
            Type modelType,
            Func<NodeModel> nodeFactory,
            Func<IFlowNodeExecutor> executorFactory,
            Func<FlowCanvas, NodeModel, FrameworkElement> contentFactory)
        {
            return new FlowNodeRegistration(definition, executorFactory)
            {
                NodeModelType = modelType,
                NodeFactory = nodeFactory,
                PaletteDisplayName = definition.DisplayName,
                PaletteDescription = description,
                PaletteCategoryIconKind = "SourceBranch",
                PaletteIconKind = "SourceBranch",
                ContentFactory = contentFactory,
            };
        }

        private static FlowPortDefinition Input(
            string id,
            string displayName,
            FlowDataType dataType)
        {
            return new FlowPortDefinition
            {
                Id = id,
                DisplayName = displayName,
                IOType = EIOType.Input,
                DataType = dataType,
                PreferredDirection = EPortDirection.Left,
                IsRequired = true,
            };
        }

        private static FlowPortDefinition Output(
            string id,
            string displayName,
            FlowDataType dataType)
        {
            return new FlowPortDefinition
            {
                Id = id,
                DisplayName = displayName,
                IOType = EIOType.Output,
                DataType = dataType,
                PreferredDirection = EPortDirection.Right,
            };
        }
    }
}
