using System;
using System.Collections.Generic;
using NodeCraft.BuiltIn.Nodes;
using NodeCraft.BuiltIn.Views;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Registrations
{
    internal static class PreviewNodeRegistrations
    {
        internal static IReadOnlyList<FlowNodeRegistration> CreateAll()
        {
            return new[]
            {
                CreateStringValue(),
                CreateAppendText(),
                CreateTextPreview(),
                CreateJsonSerialize(),
            };
        }

        private static FlowNodeRegistration CreateStringValue()
        {
            return new FlowNodeRegistration(
                new FlowNodeDefinition
                {
                    TypeKey = StringValueNodeModel.FlowNodeTypeKey,
                    DisplayName = "String Value",
                    Category = "Preview",
                    OutputPorts =
                    {
                        Output(BuiltInPortIds.Output, "Value", FlowDataType.String),
                    },
                },
                () => new StringValueExecutor())
            {
                NodeModelType = typeof(StringValueNodeModel),
                NodeFactory = () => new StringValueNodeModel(),
                PaletteDisplayName = "String Value",
                PaletteDescription = "固定字符串输出",
                PaletteCategoryIconKind = "ViewDashboardOutline",
                PaletteIconKind = "FormatText",
                ContentFactory = StringValueEditor.CreateContent,
            };
        }

        private static FlowNodeRegistration CreateAppendText()
        {
            return new FlowNodeRegistration(
                new FlowNodeDefinition
                {
                    TypeKey = AppendTextNodeModel.FlowNodeTypeKey,
                    DisplayName = "Append Text",
                    Category = "Preview",
                    InputPorts =
                    {
                        Input(BuiltInPortIds.Input, "Input", FlowDataType.String),
                    },
                    OutputPorts =
                    {
                        Output(BuiltInPortIds.Output, "Output", FlowDataType.String),
                    },
                },
                () => new AppendTextExecutor())
            {
                NodeModelType = typeof(AppendTextNodeModel),
                NodeFactory = () => new AppendTextNodeModel(),
                PaletteDisplayName = "Append Text",
                PaletteDescription = "给字符串追加后缀",
                PaletteCategoryIconKind = "ViewDashboardOutline",
                PaletteIconKind = "ViewDashboardOutline",
                IsPaletteCategoryExpanded = false,
                ContentFactory = AppendTextEditor.CreateContent,
            };
        }

        private static FlowNodeRegistration CreateTextPreview()
        {
            var definition = new FlowNodeDefinition
            {
                TypeKey = TextPreviewNodeModel.FlowNodeTypeKey,
                DisplayName = "Text Preview",
                Category = "Preview",
                InputPorts =
                {
                    Input(BuiltInPortIds.Input, "Input", FlowDataType.Object),
                },
                OutputPorts =
                {
                    Output(BuiltInPortIds.Output, "Output", FlowDataType.Object),
                },
            };

            return new FlowNodeRegistration(definition, () => new TextPreviewExecutor())
            {
                NodeModelType = typeof(TextPreviewNodeModel),
                NodeFactory = () => new TextPreviewNodeModel(),
                PaletteDisplayName = "Text Preview",
                PaletteDescription = "显示任意输入的文本结果",
                PaletteCategoryIconKind = "ViewDashboardOutline",
                PaletteIconKind = "EyeOutline",
                ContentFactory = TextPreviewView.CreateContent,
                ExecutionResultHandler = (node, executionContext) =>
                {
                    if (node is not TextPreviewNodeModel previewNode)
                    {
                        return;
                    }

                    var outputSlot = definition.OutputPorts.FindIndex(port =>
                        string.Equals(port.Id, BuiltInPortIds.Output, StringComparison.Ordinal));
                    if (outputSlot >= 0
                        && executionContext != null
                        && executionContext.TryGetPortValue(node.Id, outputSlot, out var value))
                    {
                        previewNode.LastPreviewText = value as string
                            ?? value?.ToString()
                            ?? string.Empty;
                    }
                    else
                    {
                        previewNode.LastPreviewText = string.Empty;
                    }
                },
            };
        }

        private static FlowNodeRegistration CreateJsonSerialize()
        {
            return new FlowNodeRegistration(
                new FlowNodeDefinition
                {
                    TypeKey = JsonSerializeNodeModel.FlowNodeTypeKey,
                    DisplayName = "JSON Serialize",
                    Category = "Preview",
                    InputPorts =
                    {
                        Input(BuiltInPortIds.Input, "Input", FlowDataType.Object),
                    },
                    OutputPorts =
                    {
                        Output(BuiltInPortIds.Output, "JSON", FlowDataType.String),
                    },
                },
                () => new JsonSerializeExecutor())
            {
                NodeModelType = typeof(JsonSerializeNodeModel),
                NodeFactory = () => new JsonSerializeNodeModel(),
                PaletteDisplayName = "JSON Serialize",
                PaletteDescription = "将任意输入格式化为多行 JSON",
                PaletteCategoryIconKind = "ViewDashboardOutline",
                PaletteIconKind = "ViewDashboardOutline",
                ContentFactory = JsonSerializeView.CreateContent,
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
