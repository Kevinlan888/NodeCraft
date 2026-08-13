using System;

namespace NodeCraft.Flow.Nodes
{
    internal static class BuiltInNodeRegistration
    {
        private static bool _registered;

        public static void RegisterDefaults()
        {
            if (_registered)
            {
                return;
            }

            NodeExecutorFactory.Registry.Register(new FlowNodeRegistration(
                new FlowNodeDefinition
                {
                    TypeKey = StringValueExecutor.FlowNodeTypeKey,
                    DisplayName = "String Value",
                    Category = "Preview",
                    OutputPorts =
                    {
                        new FlowPortDefinition
                        {
                            Id = BuiltInNodePorts.Output,
                            DisplayName = "Value",
                            IOType = EIOType.Output,
                            DataType = FlowDataType.String,
                            PreferredDirection = EPortDirection.Right,
                        }
                    }
                },
                () => new StringValueExecutor()));

            NodeExecutorFactory.Registry.Register(new FlowNodeRegistration(
                new FlowNodeDefinition
                {
                    TypeKey = AppendTextExecutor.FlowNodeTypeKey,
                    DisplayName = "Append Text",
                    Category = "Preview",
                    InputPorts =
                    {
                        new FlowPortDefinition
                        {
                            Id = BuiltInNodePorts.Input,
                            DisplayName = "Input",
                            IOType = EIOType.Input,
                            DataType = FlowDataType.String,
                            PreferredDirection = EPortDirection.Left,
                            IsRequired = true,
                        }
                    },
                    OutputPorts =
                    {
                        new FlowPortDefinition
                        {
                            Id = BuiltInNodePorts.Output,
                            DisplayName = "Output",
                            IOType = EIOType.Output,
                            DataType = FlowDataType.String,
                            PreferredDirection = EPortDirection.Right,
                        }
                    }
                },
                () => new AppendTextExecutor()));

            NodeExecutorFactory.Registry.Register(new FlowNodeRegistration(
                new FlowNodeDefinition
                {
                    TypeKey = HelloworldNodeModel.FlowNodeTypeKey,
                    DisplayName = "Hello World",
                    Category = "Preview",
                    InputPorts =
                    {
                        new FlowPortDefinition
                        {
                            Id = BuiltInNodePorts.Input,
                            DisplayName = "Input",
                            IOType = EIOType.Input,
                            DataType = FlowDataType.String,
                            PreferredDirection = EPortDirection.Left,
                            IsRequired = false,
                        }
                    },
                    OutputPorts =
                    {
                        new FlowPortDefinition
                        {
                            Id = BuiltInNodePorts.Output,
                            DisplayName = "Output",
                            IOType = EIOType.Output,
                            DataType = FlowDataType.String,
                            PreferredDirection = EPortDirection.Right,
                        }
                    }
                },
                () => new HelloWorldExecutor()));

            NodeExecutorFactory.Registry.Register(new FlowNodeRegistration(
                new FlowNodeDefinition
                {
                    TypeKey = TextPreviewExecutor.FlowNodeTypeKey,
                    DisplayName = "Text Preview",
                    Category = "Preview",
                    InputPorts =
                    {
                        new FlowPortDefinition
                        {
                            Id = BuiltInNodePorts.Input,
                            DisplayName = "Input",
                            IOType = EIOType.Input,
                            DataType = FlowDataType.Object,
                            PreferredDirection = EPortDirection.Left,
                            IsRequired = true,
                        }
                    },
                    OutputPorts =
                    {
                        new FlowPortDefinition
                        {
                            Id = BuiltInNodePorts.Output,
                            DisplayName = "Output",
                            IOType = EIOType.Output,
                            DataType = FlowDataType.Object,
                            PreferredDirection = EPortDirection.Right,
                        }
                    }
                },
                () => new TextPreviewExecutor()));

            NodeExecutorFactory.Registry.Register(new FlowNodeRegistration(
                new FlowNodeDefinition
                {
                    TypeKey = ImagePreviewExecutor.FlowNodeTypeKey,
                    DisplayName = "Image Preview",
                    Category = "Preview",
                    InputPorts =
                    {
                        new FlowPortDefinition
                        {
                            Id = BuiltInNodePorts.Input,
                            DisplayName = "Image Path",
                            IOType = EIOType.Input,
                            DataType = FlowDataType.String,
                            PreferredDirection = EPortDirection.Left,
                            IsRequired = true,
                        }
                    },
                    OutputPorts =
                    {
                        new FlowPortDefinition
                        {
                            Id = BuiltInNodePorts.Output,
                            DisplayName = "Output",
                            IOType = EIOType.Output,
                            DataType = FlowDataType.String,
                            PreferredDirection = EPortDirection.Right,
                        }
                    }
                },
                () => new ImagePreviewExecutor()));

            NodeExecutorFactory.Registry.Register(new FlowNodeRegistration(
                new FlowNodeDefinition
                {
                    TypeKey = IntegerValueExecutor.FlowNodeTypeKey,
                    DisplayName = "Integer Value",
                    Category = "Value",
                    OutputPorts =
                    {
                        new FlowPortDefinition
                        {
                            Id = BuiltInNodePorts.Output,
                            DisplayName = "Value",
                            IOType = EIOType.Output,
                            DataType = FlowDataType.Number,
                            PreferredDirection = EPortDirection.Right,
                        }
                    }
                },
                () => new IntegerValueExecutor()));

            NodeExecutorFactory.Registry.Register(new FlowNodeRegistration(
                new FlowNodeDefinition
                {
                    TypeKey = FloatValueExecutor.FlowNodeTypeKey,
                    DisplayName = "Float Value",
                    Category = "Value",
                    OutputPorts =
                    {
                        new FlowPortDefinition
                        {
                            Id = BuiltInNodePorts.Output,
                            DisplayName = "Value",
                            IOType = EIOType.Output,
                            DataType = FlowDataType.Number,
                            PreferredDirection = EPortDirection.Right,
                        }
                    }
                },
                () => new FloatValueExecutor()));

            NodeExecutorFactory.Registry.Register(new FlowNodeRegistration(
                new FlowNodeDefinition
                {
                    TypeKey = BooleanValueExecutor.FlowNodeTypeKey,
                    DisplayName = "Boolean Value",
                    Category = "Value",
                    OutputPorts =
                    {
                        new FlowPortDefinition
                        {
                            Id = BuiltInNodePorts.Output,
                            DisplayName = "Value",
                            IOType = EIOType.Output,
                            DataType = FlowDataType.Boolean,
                            PreferredDirection = EPortDirection.Right,
                        }
                    }
                },
                () => new BooleanValueExecutor()));

            NodeExecutorFactory.Registry.Register(new FlowNodeRegistration(
                new FlowNodeDefinition
                {
                    TypeKey = AddNumberExecutor.FlowNodeTypeKey,
                    DisplayName = "Add",
                    Category = "Math",
                    InputPorts =
                    {
                        new FlowPortDefinition
                        {
                            Id = BuiltInNodePorts.InputA,
                            DisplayName = "A",
                            IOType = EIOType.Input,
                            DataType = FlowDataType.Number,
                            PreferredDirection = EPortDirection.Left,
                            IsRequired = true,
                        },
                        new FlowPortDefinition
                        {
                            Id = BuiltInNodePorts.InputB,
                            DisplayName = "B",
                            IOType = EIOType.Input,
                            DataType = FlowDataType.Number,
                            PreferredDirection = EPortDirection.Left,
                            IsRequired = true,
                        }
                    },
                    OutputPorts =
                    {
                        new FlowPortDefinition
                        {
                            Id = BuiltInNodePorts.Output,
                            DisplayName = "Sum",
                            IOType = EIOType.Output,
                            DataType = FlowDataType.Number,
                            PreferredDirection = EPortDirection.Right,
                        }
                    }
                },
                () => new AddNumberExecutor()));

            NodeExecutorFactory.Registry.Register(new FlowNodeRegistration(
                new FlowNodeDefinition
                {
                    TypeKey = MultiplyNumberExecutor.FlowNodeTypeKey,
                    DisplayName = "Multiply",
                    Category = "Math",
                    InputPorts =
                    {
                        new FlowPortDefinition
                        {
                            Id = BuiltInNodePorts.InputA,
                            DisplayName = "A",
                            IOType = EIOType.Input,
                            DataType = FlowDataType.Number,
                            PreferredDirection = EPortDirection.Left,
                            IsRequired = true,
                        },
                        new FlowPortDefinition
                        {
                            Id = BuiltInNodePorts.InputB,
                            DisplayName = "B",
                            IOType = EIOType.Input,
                            DataType = FlowDataType.Number,
                            PreferredDirection = EPortDirection.Left,
                            IsRequired = true,
                        }
                    },
                    OutputPorts =
                    {
                        new FlowPortDefinition
                        {
                            Id = BuiltInNodePorts.Output,
                            DisplayName = "Product",
                            IOType = EIOType.Output,
                            DataType = FlowDataType.Number,
                            PreferredDirection = EPortDirection.Right,
                        }
                    }
                },
                () => new MultiplyNumberExecutor()));

            NodeExecutorFactory.Registry.Register(new FlowNodeRegistration(
                new FlowNodeDefinition
                {
                    TypeKey = SubtractNumberExecutor.FlowNodeTypeKey,
                    DisplayName = "Subtract",
                    Category = "Math",
                    InputPorts =
                    {
                        new FlowPortDefinition
                        {
                            Id = BuiltInNodePorts.InputA,
                            DisplayName = "A",
                            IOType = EIOType.Input,
                            DataType = FlowDataType.Number,
                            PreferredDirection = EPortDirection.Left,
                            IsRequired = true,
                        },
                        new FlowPortDefinition
                        {
                            Id = BuiltInNodePorts.InputB,
                            DisplayName = "B",
                            IOType = EIOType.Input,
                            DataType = FlowDataType.Number,
                            PreferredDirection = EPortDirection.Left,
                            IsRequired = true,
                        }
                    },
                    OutputPorts =
                    {
                        new FlowPortDefinition
                        {
                            Id = BuiltInNodePorts.Output,
                            DisplayName = "Difference",
                            IOType = EIOType.Output,
                            DataType = FlowDataType.Number,
                            PreferredDirection = EPortDirection.Right,
                        }
                    }
                },
                () => new SubtractNumberExecutor()));

            NodeExecutorFactory.Registry.Register(new FlowNodeRegistration(
                new FlowNodeDefinition
                {
                    TypeKey = DivideNumberExecutor.FlowNodeTypeKey,
                    DisplayName = "Divide",
                    Category = "Math",
                    InputPorts =
                    {
                        new FlowPortDefinition
                        {
                            Id = BuiltInNodePorts.InputA,
                            DisplayName = "A",
                            IOType = EIOType.Input,
                            DataType = FlowDataType.Number,
                            PreferredDirection = EPortDirection.Left,
                            IsRequired = true,
                        },
                        new FlowPortDefinition
                        {
                            Id = BuiltInNodePorts.InputB,
                            DisplayName = "B",
                            IOType = EIOType.Input,
                            DataType = FlowDataType.Number,
                            PreferredDirection = EPortDirection.Left,
                            IsRequired = true,
                        }
                    },
                    OutputPorts =
                    {
                        new FlowPortDefinition
                        {
                            Id = BuiltInNodePorts.Output,
                            DisplayName = "Quotient",
                            IOType = EIOType.Output,
                            DataType = FlowDataType.Number,
                            PreferredDirection = EPortDirection.Right,
                        }
                    }
                },
                () => new DivideNumberExecutor()));

            NodeExecutorFactory.Registry.Register(new FlowNodeRegistration(
                new FlowNodeDefinition
                {
                    TypeKey = GreaterThanExecutor.FlowNodeTypeKey,
                    DisplayName = "Greater Than",
                    Category = "Logic",
                    InputPorts =
                    {
                        new FlowPortDefinition
                        {
                            Id = BuiltInNodePorts.InputA,
                            DisplayName = "A",
                            IOType = EIOType.Input,
                            DataType = FlowDataType.Number,
                            PreferredDirection = EPortDirection.Left,
                            IsRequired = true,
                        },
                        new FlowPortDefinition
                        {
                            Id = BuiltInNodePorts.InputB,
                            DisplayName = "B",
                            IOType = EIOType.Input,
                            DataType = FlowDataType.Number,
                            PreferredDirection = EPortDirection.Left,
                            IsRequired = true,
                        }
                    },
                    OutputPorts =
                    {
                        new FlowPortDefinition
                        {
                            Id = BuiltInNodePorts.Output,
                            DisplayName = "Result",
                            IOType = EIOType.Output,
                            DataType = FlowDataType.Boolean,
                            PreferredDirection = EPortDirection.Right,
                        }
                    }
                },
                () => new GreaterThanExecutor()));

            NodeExecutorFactory.Registry.Register(new FlowNodeRegistration(
                new FlowNodeDefinition
                {
                    TypeKey = LessThanExecutor.FlowNodeTypeKey,
                    DisplayName = "Less Than",
                    Category = "Logic",
                    InputPorts =
                    {
                        new FlowPortDefinition
                        {
                            Id = BuiltInNodePorts.InputA,
                            DisplayName = "A",
                            IOType = EIOType.Input,
                            DataType = FlowDataType.Number,
                            PreferredDirection = EPortDirection.Left,
                            IsRequired = true,
                        },
                        new FlowPortDefinition
                        {
                            Id = BuiltInNodePorts.InputB,
                            DisplayName = "B",
                            IOType = EIOType.Input,
                            DataType = FlowDataType.Number,
                            PreferredDirection = EPortDirection.Left,
                            IsRequired = true,
                        }
                    },
                    OutputPorts =
                    {
                        new FlowPortDefinition
                        {
                            Id = BuiltInNodePorts.Output,
                            DisplayName = "Result",
                            IOType = EIOType.Output,
                            DataType = FlowDataType.Boolean,
                            PreferredDirection = EPortDirection.Right,
                        }
                    }
                },
                () => new LessThanExecutor()));

            NodeExecutorFactory.Registry.Register(new FlowNodeRegistration(
                new FlowNodeDefinition
                {
                    TypeKey = EqualExecutor.FlowNodeTypeKey,
                    DisplayName = "Equal",
                    Category = "Logic",
                    InputPorts =
                    {
                        new FlowPortDefinition
                        {
                            Id = BuiltInNodePorts.InputA,
                            DisplayName = "A",
                            IOType = EIOType.Input,
                            DataType = FlowDataType.Object,
                            PreferredDirection = EPortDirection.Left,
                            IsRequired = true,
                        },
                        new FlowPortDefinition
                        {
                            Id = BuiltInNodePorts.InputB,
                            DisplayName = "B",
                            IOType = EIOType.Input,
                            DataType = FlowDataType.Object,
                            PreferredDirection = EPortDirection.Left,
                            IsRequired = true,
                        }
                    },
                    OutputPorts =
                    {
                        new FlowPortDefinition
                        {
                            Id = BuiltInNodePorts.Output,
                            DisplayName = "Result",
                            IOType = EIOType.Output,
                            DataType = FlowDataType.Boolean,
                            PreferredDirection = EPortDirection.Right,
                        }
                    }
                },
                () => new EqualExecutor()));

            NodeExecutorFactory.Registry.Register(new FlowNodeRegistration(
                new FlowNodeDefinition
                {
                    TypeKey = BooleanAndExecutor.FlowNodeTypeKey,
                    DisplayName = "Boolean And",
                    Category = "Logic",
                    InputPorts =
                    {
                        new FlowPortDefinition
                        {
                            Id = BuiltInNodePorts.InputA,
                            DisplayName = "A",
                            IOType = EIOType.Input,
                            DataType = FlowDataType.Boolean,
                            PreferredDirection = EPortDirection.Left,
                            IsRequired = true,
                        },
                        new FlowPortDefinition
                        {
                            Id = BuiltInNodePorts.InputB,
                            DisplayName = "B",
                            IOType = EIOType.Input,
                            DataType = FlowDataType.Boolean,
                            PreferredDirection = EPortDirection.Left,
                            IsRequired = true,
                        }
                    },
                    OutputPorts =
                    {
                        new FlowPortDefinition
                        {
                            Id = BuiltInNodePorts.Output,
                            DisplayName = "Result",
                            IOType = EIOType.Output,
                            DataType = FlowDataType.Boolean,
                            PreferredDirection = EPortDirection.Right,
                        }
                    }
                },
                () => new BooleanAndExecutor()));

            NodeExecutorFactory.Registry.Register(new FlowNodeRegistration(
                new FlowNodeDefinition
                {
                    TypeKey = BooleanOrExecutor.FlowNodeTypeKey,
                    DisplayName = "Boolean Or",
                    Category = "Logic",
                    InputPorts =
                    {
                        new FlowPortDefinition
                        {
                            Id = BuiltInNodePorts.InputA,
                            DisplayName = "A",
                            IOType = EIOType.Input,
                            DataType = FlowDataType.Boolean,
                            PreferredDirection = EPortDirection.Left,
                            IsRequired = true,
                        },
                        new FlowPortDefinition
                        {
                            Id = BuiltInNodePorts.InputB,
                            DisplayName = "B",
                            IOType = EIOType.Input,
                            DataType = FlowDataType.Boolean,
                            PreferredDirection = EPortDirection.Left,
                            IsRequired = true,
                        }
                    },
                    OutputPorts =
                    {
                        new FlowPortDefinition
                        {
                            Id = BuiltInNodePorts.Output,
                            DisplayName = "Result",
                            IOType = EIOType.Output,
                            DataType = FlowDataType.Boolean,
                            PreferredDirection = EPortDirection.Right,
                        }
                    }
                },
                () => new BooleanOrExecutor()));

            NodeExecutorFactory.Registry.Register(new FlowNodeRegistration(
                new FlowNodeDefinition
                {
                    TypeKey = BooleanNotExecutor.FlowNodeTypeKey,
                    DisplayName = "Boolean Not",
                    Category = "Logic",
                    InputPorts =
                    {
                        new FlowPortDefinition
                        {
                            Id = BuiltInNodePorts.Input,
                            DisplayName = "Input",
                            IOType = EIOType.Input,
                            DataType = FlowDataType.Boolean,
                            PreferredDirection = EPortDirection.Left,
                            IsRequired = true,
                        }
                    },
                    OutputPorts =
                    {
                        new FlowPortDefinition
                        {
                            Id = BuiltInNodePorts.Output,
                            DisplayName = "Result",
                            IOType = EIOType.Output,
                            DataType = FlowDataType.Boolean,
                            PreferredDirection = EPortDirection.Right,
                        }
                    }
                },
                () => new BooleanNotExecutor()));

            NodeExecutorFactory.Registry.Register(new FlowNodeRegistration(
                new FlowNodeDefinition
                {
                    TypeKey = IfExecutor.FlowNodeTypeKey,
                    DisplayName = "If",
                    Category = "Logic",
                    InputPorts =
                    {
                        new FlowPortDefinition
                        {
                            Id = FlowPorts.Condition,
                            DisplayName = "Condition",
                            IOType = EIOType.Input,
                            DataType = FlowDataType.Boolean,
                            PreferredDirection = EPortDirection.Left,
                            IsRequired = true,
                        }
                    },
                    OutputPorts =
                    {
                        new FlowPortDefinition
                        {
                            Id = FlowPorts.True,
                            DisplayName = "True",
                            IOType = EIOType.Output,
                            DataType = FlowDataType.Control,
                            PreferredDirection = EPortDirection.Right,
                        },
                        new FlowPortDefinition
                        {
                            Id = FlowPorts.False,
                            DisplayName = "False",
                            IOType = EIOType.Output,
                            DataType = FlowDataType.Control,
                            PreferredDirection = EPortDirection.Right,
                        }
                    }
                },
                () => new IfExecutor()));

            ConfigureEditors();

            _registered = true;
        }

        private static void ConfigureEditors()
        {
            ConfigureBuiltInNode(StringValueExecutor.FlowNodeTypeKey, typeof(StringValueNodeModel), () => new StringValueNodeModel(), "固定字符串输出", true);
            ConfigureBuiltInNode(IntegerValueExecutor.FlowNodeTypeKey, typeof(IntegerValueNodeModel), () => new IntegerValueNodeModel(), "固定整数输出", true);
            ConfigureBuiltInNode(FloatValueExecutor.FlowNodeTypeKey, typeof(FloatValueNodeModel), () => new FloatValueNodeModel(), "固定浮点数输出", true);
            ConfigureBuiltInNode(BooleanValueExecutor.FlowNodeTypeKey, typeof(BooleanValueNodeModel), () => new BooleanValueNodeModel(), "固定布尔输出", true);
            ConfigureBuiltInNode(AddNumberExecutor.FlowNodeTypeKey, typeof(AddNumberNodeModel), () => new AddNumberNodeModel(), "A + B", true);
            ConfigureBuiltInNode(SubtractNumberExecutor.FlowNodeTypeKey, typeof(SubtractNumberNodeModel), () => new SubtractNumberNodeModel(), "A - B", true);
            ConfigureBuiltInNode(MultiplyNumberExecutor.FlowNodeTypeKey, typeof(MultiplyNumberNodeModel), () => new MultiplyNumberNodeModel(), "A * B", true);
            ConfigureBuiltInNode(DivideNumberExecutor.FlowNodeTypeKey, typeof(DivideNumberNodeModel), () => new DivideNumberNodeModel(), "A / B", true);
            ConfigureBuiltInNode(GreaterThanExecutor.FlowNodeTypeKey, typeof(GreaterThanNodeModel), () => new GreaterThanNodeModel(), "A > B", true);
            ConfigureBuiltInNode(LessThanExecutor.FlowNodeTypeKey, typeof(LessThanNodeModel), () => new LessThanNodeModel(), "A < B", true);
            ConfigureBuiltInNode(EqualExecutor.FlowNodeTypeKey, typeof(EqualNodeModel), () => new EqualNodeModel(), "A == B", true);
            ConfigureBuiltInNode(BooleanAndExecutor.FlowNodeTypeKey, typeof(BooleanAndNodeModel), () => new BooleanAndNodeModel(), "A && B", true);
            ConfigureBuiltInNode(BooleanOrExecutor.FlowNodeTypeKey, typeof(BooleanOrNodeModel), () => new BooleanOrNodeModel(), "A || B", true);
            ConfigureBuiltInNode(BooleanNotExecutor.FlowNodeTypeKey, typeof(BooleanNotNodeModel), () => new BooleanNotNodeModel(), "!A", true);
            ConfigureBuiltInNode(IfExecutor.FlowNodeTypeKey, typeof(IfNodeModel), () => new IfNodeModel(), "condition ? true : false", true);
            ConfigureBuiltInNode(AppendTextExecutor.FlowNodeTypeKey, typeof(AppendTextNodeModel), () => new AppendTextNodeModel(), "给字符串追加后缀", false);
            ConfigureBuiltInNode(HelloworldNodeModel.FlowNodeTypeKey, typeof(HelloworldNodeModel), () => new HelloworldNodeModel(), "调试输出节点", false);
            ConfigureBuiltInNode(
                TextPreviewExecutor.FlowNodeTypeKey,
                typeof(TextPreviewNodeModel),
                () => new TextPreviewNodeModel(),
                "显示任意输入的文本结果",
                true,
                executionResultHandler: (node, context) =>
                {
                    if (node is TextPreviewNodeModel previewNode)
                    {
                        previewNode.LastPreviewText = TryReadNodeOutput(context, node, BuiltInNodePorts.Output);
                    }
                });
            ConfigureBuiltInNode(
                ImagePreviewExecutor.FlowNodeTypeKey,
                typeof(ImagePreviewNodeModel),
                () => new ImagePreviewNodeModel(),
                "显示图片路径对应的图片",
                true,
                executionResultHandler: (node, context) =>
                {
                    if (node is ImagePreviewNodeModel previewNode)
                    {
                        previewNode.LastImagePath = TryReadNodeOutput(context, node, BuiltInNodePorts.Output);
                        previewNode.LastImageError = DefaultFlowNodeContentFactory.ResolveImagePreviewError(previewNode.LastImagePath);
                    }
                });
        }

        private static void ConfigureBuiltInNode(
            string typeKey,
            Type nodeModelType,
            Func<NodeModel> nodeFactory,
            string paletteDescription,
            bool isExpanded,
            Action<NodeModel, FlowExecutionContext> executionResultHandler = null)
        {
            NodeExecutorFactory.Registry.ConfigureNodeEditor(
                typeKey,
                nodeModelType,
                nodeFactory,
                paletteDescription,
                true,
                isExpanded,
                contentFactory: null,
                executionResultHandler: executionResultHandler);
        }

        private static string TryReadNodeOutput(FlowExecutionContext context, NodeModel node, string portId)
        {
            if (node == null
                || context == null
                || string.IsNullOrWhiteSpace(node.ExecutorType)
                || !NodeExecutorFactory.Registry.TryResolve(node.ExecutorType, out var registration))
            {
                return string.Empty;
            }

            var slot = FindOutputSlot(registration.Definition, portId);
            if (slot < 0 || !context.TryGetPortValue(node.Id, slot, out var value))
            {
                return string.Empty;
            }

            return value as string ?? value?.ToString() ?? string.Empty;
        }

        private static int FindOutputSlot(FlowNodeDefinition definition, string portId)
        {
            for (int i = 0; i < definition.OutputPorts.Count; i++)
            {
                if (string.Equals(definition.OutputPorts[i].Id, portId, System.StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}