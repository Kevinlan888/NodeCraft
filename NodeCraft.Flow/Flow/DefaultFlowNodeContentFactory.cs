using NodeCraft.Flow.Nodes;
using NodeCraft.Localization;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommonControls.WPF;

namespace NodeCraft.Flow
{
    public class DefaultFlowNodeContentFactory
    {
        private readonly FlowCanvas _canvas;

        public DefaultFlowNodeContentFactory(FlowCanvas canvas)
        {
            _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        }

        public FrameworkElement Build(NodeModel node)
        {
            if (node is ImagePreviewNodeModel imagePreviewNode)
            {
                return BuildImagePreview(imagePreviewNode);
            }

            var container = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };

            container.Children.Add(new TextBlock
            {
                Text = node.Name,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 6),
            });

            if (node is StringValueNodeModel stringValueNode)
            {
                container.Children.Add(BuildInlineEditor("Value", stringValueNode.ValueText, value =>
                {
                    stringValueNode.ValueText = value;
                }));
            }
            else if (node is IntegerValueNodeModel integerValueNode)
            {
                container.Children.Add(BuildNumericEditor("Integer", integerValueNode.IntegerValue, value =>
                {
                    integerValueNode.IntegerValue = (int)Math.Round(value);
                }, decimalPlaces: 0));
            }
            else if (node is FloatValueNodeModel floatValueNode)
            {
                container.Children.Add(BuildNumericEditor("Float", floatValueNode.FloatValue, value =>
                {
                    floatValueNode.FloatValue = value;
                }, decimalPlaces: 3));
            }
            else if (node is BooleanValueNodeModel booleanValueNode)
            {
                container.Children.Add(BuildBooleanEditor("Enabled", booleanValueNode.BooleanValue, value =>
                {
                    booleanValueNode.BooleanValue = value;
                }));
            }
            else if (node is AppendTextNodeModel appendTextNode)
            {
                container.Children.Add(BuildInlineEditor("Suffix", appendTextNode.SuffixText, value =>
                {
                    appendTextNode.SuffixText = value;
                }));
            }
            else if (node is MultiplyNumberNodeModel)
            {
                container.Children.Add(BuildBinaryOperationNode(node, "A * B", "输出两个数字输入的乘积"));
            }
            else if (node is SubtractNumberNodeModel)
            {
                container.Children.Add(BuildBinaryOperationNode(node, "A - B", "输出两个数字输入的差值"));
            }
            else if (node is DivideNumberNodeModel)
            {
                container.Children.Add(BuildBinaryOperationNode(node, "A / B", "输出两个数字输入的商，除数为 0 时返回 0"));
            }
            else if (node is AddNumberNodeModel)
            {
                container.Children.Add(BuildBinaryOperationNode(node, "A + B", "输出两个数字输入的和"));
            }
            else if (node is GreaterThanNodeModel)
            {
                container.Children.Add(BuildBinaryOperationNode(node, "A > B", "比较两个数字并输出布尔值"));
            }
            else if (node is LessThanNodeModel)
            {
                container.Children.Add(BuildBinaryOperationNode(node, "A < B", "比较两个数字并输出布尔值"));
            }
            else if (node is EqualNodeModel)
            {
                container.Children.Add(BuildBinaryOperationNode(node, "A == B", "比较两个输入是否相等"));
            }
            else if (node is BooleanOrNodeModel)
            {
                container.Children.Add(BuildBinaryOperationNode(node, "A || B", "任一布尔输入为真时输出真"));
            }
            else if (node is BooleanAndNodeModel)
            {
                container.Children.Add(BuildBinaryOperationNode(node, "A && B", "两个布尔输入都为真时输出真"));
            }
            else if (node is BooleanNotNodeModel)
            {
                container.Children.Add(BuildUnaryOperationNode(node, "!A", "对布尔输入取反"));
            }
            else if (node is IfNodeModel)
            {
                container.Children.Add(BuildIfNode(node));
            }
            else if (node is TextPreviewNodeModel textPreviewNode)
            {
                container.Children.Add(BuildPreviewValue("Text", textPreviewNode.LastPreviewText, "等待执行后显示文本结果"));
            }
            else
            {
                container.Children.Add(new TextBlock
                {
                    Text = "Output node",
                    Opacity = 0.75,
                    HorizontalAlignment = HorizontalAlignment.Center,
                });
            }

            return container;
        }

        public static string ResolveImagePreviewError(string imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath))
            {
                return "等待输入图片路径";
            }

            if (Uri.TryCreate(imagePath, UriKind.Absolute, out var uri))
            {
                if (uri.IsFile && !File.Exists(uri.LocalPath))
                {
                    return "图片文件不存在";
                }

                return string.Empty;
            }

            return File.Exists(imagePath) ? string.Empty : "图片文件不存在";
        }

        private FrameworkElement BuildInlineEditor(string label, string value, Action<string> onChanged)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Vertical,
            };

            panel.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                Opacity = 0.75,
                Margin = new Thickness(0, 0, 0, 4),
            });

            var editor = new TextBox
            {
                Text = value,
                MinWidth = 140,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            editor.TextChanged += (_, __) => onChanged(editor.Text);
            panel.Children.Add(editor);

            return panel;
        }

        private FrameworkElement BuildNumericEditor(string label, double value, Action<double> onChanged, int decimalPlaces)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Vertical,
            };

            panel.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                Opacity = 0.75,
                Margin = new Thickness(0, 0, 0, 4),
            });

            var editor = new TextBox
            {
                Text = value.ToString($"F{decimalPlaces}", System.Globalization.CultureInfo.InvariantCulture),
                MinWidth = 140,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            editor.TextChanged += (_, __) =>
            {
                if (double.TryParse(editor.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                {
                    onChanged(parsed);
                }
            };
            panel.Children.Add(editor);
            return panel;
        }

        private FrameworkElement BuildBooleanEditor(string label, bool value, Action<bool> onChanged)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Vertical,
            };

            panel.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                Opacity = 0.75,
                Margin = new Thickness(0, 0, 0, 4),
            });

            var checkBox = new CheckBox
            {
                IsChecked = value,
                Content = value ? "True" : "False",
            };
            checkBox.Checked += (_, __) =>
            {
                checkBox.Content = "True";
                onChanged(true);
            };
            checkBox.Unchecked += (_, __) =>
            {
                checkBox.Content = "False";
                onChanged(false);
            };
            panel.Children.Add(checkBox);
            return panel;
        }

        private FrameworkElement BuildOperationSummary(string formula, string description)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                IsHitTestVisible = false,
            };

            panel.Children.Add(new TextBlock
            {
                Text = formula,
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 6),
            });

            panel.Children.Add(new TextBlock
            {
                Text = description,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.75,
                TextAlignment = TextAlignment.Center,
                MaxWidth = 180,
            });

            return panel;
        }

        private FrameworkElement BuildBinaryOperationNode(NodeModel node, string formula, string description)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(0),
            };

            panel.Children.Add(BuildOperationSummary(formula, description));
            panel.Children.Add(BuildInputBindings(node));
            panel.Children.Add(BuildSwapInputsButton(node));
            return panel;
        }

        private FrameworkElement BuildUnaryOperationNode(NodeModel node, string formula, string description)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(0),
            };

            panel.Children.Add(BuildOperationSummary(formula, description));
            panel.Children.Add(BuildInputBindings(node));
            return panel;
        }

        private FrameworkElement BuildInputBindings(NodeModel node)
        {
            if (node == null || !NodeExecutorFactory.Registry.TryResolve(node.ExecutorType, out var registration))
            {
                return new Border { Height = 0, Opacity = 0 };
            }

            var inputPorts = registration.Definition.InputPorts?
                .Where(port => !port.IsControlPort)
                .ToList();

            if (inputPorts == null || inputPorts.Count == 0)
            {
                return new Border { Height = 0, Opacity = 0 };
            }

            var panel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(0, 10, 0, 0),
            };

            panel.Children.Add(new TextBlock
            {
                Text = "Inputs",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Opacity = 0.78,
                Margin = new Thickness(0, 0, 0, 4),
            });

            foreach (var inputPort in inputPorts)
            {
                var linkId = node.InputParameters?
                    .FirstOrDefault(item => string.Equals(item.PortId, inputPort.Id, StringComparison.Ordinal))?
                    .LinkId;

                var sourceName = ResolveConnectedSourceName(FindLink(linkId));

                panel.Children.Add(new Border
                {
                    Margin = new Thickness(0, 0, 0, 4),
                    Padding = new Thickness(8, 5, 8, 5),
                    CornerRadius = new CornerRadius(6),
                    Background = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
                    Child = new Grid
                    {
                        ColumnDefinitions =
                        {
                            new ColumnDefinition { Width = GridLength.Auto },
                            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                        },
                        Children =
                        {
                            CreateInputBindingLabel(inputPort.DisplayName ?? inputPort.Id),
                            CreateInputBindingValue(sourceName),
                        }
                    }
                });
            }

            return panel;
        }

        private static TextBlock CreateInputBindingLabel(string label)
        {
            return new TextBlock
            {
                Text = label,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        private static TextBlock CreateInputBindingValue(string value)
        {
            var text = new TextBlock
            {
                Text = value,
                TextWrapping = TextWrapping.Wrap,
                Opacity = string.Equals(value, "未连接", StringComparison.Ordinal) ? 0.62 : 0.92,
                VerticalAlignment = VerticalAlignment.Center,
            };

            Grid.SetColumn(text, 1);
            return text;
        }

        private string ResolveConnectedSourceName(GraphLink link)
        {
            if (link == null)
            {
                return "未连接";
            }

            var sourceNode = _canvas.GraphModel?.Nodes?
                .FirstOrDefault(item => string.Equals(item.Id, link.OriginNodeId, StringComparison.Ordinal));

            if (sourceNode == null)
            {
                return "已连接";
            }

            if (NodeExecutorFactory.Registry.TryResolve(sourceNode.ExecutorType, out var registration)
                && link.OriginSlot >= 0
                && link.OriginSlot < registration.Definition.OutputPorts.Count)
            {
                var sourcePort = registration.Definition.OutputPorts[link.OriginSlot];
                if (sourcePort != null && !string.IsNullOrWhiteSpace(sourcePort.DisplayName))
                {
                    return $"{sourceNode.Name} · {sourcePort.DisplayName}";
                }
            }

            return sourceNode.Name;
        }

        private FrameworkElement BuildSwapInputsButton(NodeModel node)
        {
            if (node == null || !NodeExecutorFactory.Registry.TryResolve(node.ExecutorType, out var registration))
            {
                return new Border { Height = 0, Opacity = 0 };
            }

            if (registration.Definition.InputPorts == null || registration.Definition.InputPorts.Count != 2)
            {
                return new Border { Height = 0, Opacity = 0 };
            }

            var firstPortId = registration.Definition.InputPorts[0].Id;
            var secondPortId = registration.Definition.InputPorts[1].Id;
            var firstSlot = ResolveDefinitionSlot(node, firstPortId, isInput: true);
            var secondSlot = ResolveDefinitionSlot(node, secondPortId, isInput: true);
            var firstLabel = registration.Definition.InputPorts[0].DisplayName ?? firstPortId;
            var secondLabel = registration.Definition.InputPorts[1].DisplayName ?? secondPortId;

            var firstConnection = FindTargetLink(node.Id, firstSlot);
            var secondConnection = FindTargetLink(node.Id, secondSlot);

            var buttonLabel = BuildSwapButtonLabel(firstLabel, secondLabel, firstConnection, secondConnection);

            var button = new RoundButton
            {
                Content = buttonLabel,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 8, 0, 0),
                Padding = new Thickness(8, 5, 8, 5),
                CornerRadius = new CornerRadius(6),
                IsEnabled = firstConnection != null || secondConnection != null,
            };

            button.Click += (_, __) =>
            {
                var currentFirstConnection = FindTargetLink(node.Id, firstSlot);
                var currentSecondConnection = FindTargetLink(node.Id, secondSlot);

                if (currentFirstConnection == null && currentSecondConnection == null)
                {
                    return;
                }

                SwapTargetSlots(node, currentFirstConnection, currentSecondConnection, firstSlot, secondSlot);
                _canvas.NotifyGraphChanged();
            };

            return button;
        }

        private static string BuildSwapButtonLabel(string firstLabel, string secondLabel, GraphLink firstConnection, GraphLink secondConnection)
        {
            if (firstConnection != null && secondConnection != null)
            {
                return $"Swap {firstLabel}/{secondLabel}";
            }

            if (firstConnection != null)
            {
                return $"Move {firstLabel} -> {secondLabel}";
            }

            if (secondConnection != null)
            {
                return $"Move {secondLabel} -> {firstLabel}";
            }

            return $"Swap {firstLabel}/{secondLabel}";
        }

        private GraphLink FindLink(string linkId)
        {
            if (string.IsNullOrWhiteSpace(linkId))
            {
                return null;
            }

            return _canvas.GraphModel?.Links?
                .FirstOrDefault(link => string.Equals(link.Id, linkId, StringComparison.Ordinal));
        }

        private GraphLink FindTargetLink(string nodeId, int slot)
        {
            return _canvas.GraphModel?.Links?
                .FirstOrDefault(link => string.Equals(link.TargetNodeId, nodeId, StringComparison.Ordinal)
                    && link.TargetSlot == slot);
        }

        private static int ResolveDefinitionSlot(NodeModel node, string portId, bool isInput)
        {
            if (node == null || string.IsNullOrWhiteSpace(node.ExecutorType)
                || !NodeExecutorFactory.Registry.TryResolve(node.ExecutorType, out var registration))
            {
                return -1;
            }

            var ports = isInput ? registration.Definition.InputPorts : registration.Definition.OutputPorts;
            for (int i = 0; i < ports.Count; i++)
            {
                if (string.Equals(ports[i].Id, portId, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private void SwapTargetSlots(NodeModel node, GraphLink first, GraphLink second, int firstSlot, int secondSlot)
        {
            var links = _canvas.GraphModel?.Links;
            if (links == null)
            {
                return;
            }

            foreach (var link in links)
            {
                if (ReferenceEquals(link, first))
                {
                    link.TargetSlot = secondSlot;
                }
                else if (ReferenceEquals(link, second))
                {
                    link.TargetSlot = firstSlot;
                }
            }

            SetPortLinkId(node, firstSlot, second?.Id);
            SetPortLinkId(node, secondSlot, first?.Id);
        }

        private static void SetPortLinkId(NodeModel node, int slot, string linkId)
        {
            if (node?.InputParameters == null || string.IsNullOrWhiteSpace(node.ExecutorType)
                || !NodeExecutorFactory.Registry.TryResolve(node.ExecutorType, out var registration))
            {
                return;
            }

            if (slot < 0 || slot >= registration.Definition.InputPorts.Count)
            {
                return;
            }

            var port = node.InputParameters.FirstOrDefault(item => string.Equals(item.PortId, registration.Definition.InputPorts[slot].Id, StringComparison.Ordinal));
            if (port != null)
            {
                port.LinkId = linkId;
            }
        }

        private FrameworkElement BuildPreviewValue(string label, string value, string placeholder)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                IsHitTestVisible = false,
            };

            panel.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                Opacity = 0.75,
                Margin = new Thickness(0, 0, 0, 4),
            });

            panel.Children.Add(new Border
            {
                Padding = new Thickness(8, 6, 8, 6),
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)),
                Child = new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(value) ? placeholder : value,
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = string.IsNullOrWhiteSpace(value) ? 0.65 : 1,
                    MaxWidth = 180,
                }
            });

            return panel;
        }

        private FrameworkElement BuildIfNode(NodeModel node)
        {
            var panel = new StackPanel { Orientation = Orientation.Vertical };
            panel.Children.Add(new TextBlock
            {
                Text = "IF",
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8),
            });

            var trueLabel = new TextBlock
            {
                Text = LanguageManager.GetString("FlowPort_true"),
                Foreground = (Brush)_canvas.FindResource("colorStatusSuccessForeground1"),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            panel.Children.Add(trueLabel);

            var falseLabel = new TextBlock
            {
                Text = LanguageManager.GetString("FlowPort_false"),
                Foreground = (Brush)_canvas.FindResource("colorStatusDangerForeground1"),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            panel.Children.Add(falseLabel);

            return panel;
        }

        private FrameworkElement BuildImagePreview(ImagePreviewNodeModel node)
        {
            var panel = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                IsHitTestVisible = false,
            };

            panel.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Auto,
            });
            panel.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(1, GridUnitType.Star),
            });
            if (!string.IsNullOrWhiteSpace(node.LastImagePath))
            {
                panel.RowDefinitions.Add(new RowDefinition
                {
                    Height = GridLength.Auto,
                });
            }

            var title = new TextBlock
            {
                Text = "Image",
                FontSize = 11,
                Opacity = 0.75,
                Margin = new Thickness(0, 0, 0, 4),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            Grid.SetRow(title, 0);
            panel.Children.Add(title);

            var border = new Border
            {
                MinWidth = 180,
                MinHeight = 120,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)),
                ClipToBounds = true,
            };

            if (!string.IsNullOrWhiteSpace(node.LastImageError))
            {
                border.Child = new TextBlock
                {
                    Text = node.LastImageError,
                    TextWrapping = TextWrapping.Wrap,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    Opacity = 0.7,
                    Margin = new Thickness(10),
                };
            }
            else if (!string.IsNullOrWhiteSpace(node.LastImagePath))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = CreateImageUri(node.LastImagePath);
                    bitmap.EndInit();
                    bitmap.Freeze();

                    border.Child = new Image
                    {
                        Source = bitmap,
                        Stretch = Stretch.UniformToFill,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Stretch,
                    };
                }
                catch
                {
                    border.Child = new TextBlock
                    {
                        Text = "图片加载失败",
                        TextWrapping = TextWrapping.Wrap,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextAlignment = TextAlignment.Center,
                        Opacity = 0.7,
                        Margin = new Thickness(10),
                    };
                }
            }
            else
            {
                border.Child = new TextBlock
                {
                    Text = "等待执行后显示图片",
                    TextWrapping = TextWrapping.Wrap,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    Opacity = 0.65,
                    Margin = new Thickness(10),
                };
            }

            Grid.SetRow(border, 1);
            panel.Children.Add(border);

            if (!string.IsNullOrWhiteSpace(node.LastImagePath))
            {
                var path = new TextBlock
                {
                    Margin = new Thickness(0, 6, 0, 0),
                    Text = node.LastImagePath,
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.65,
                    FontSize = 10,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };
                Grid.SetRow(path, 2);
                panel.Children.Add(path);
            }

            return panel;
        }

        private static Uri CreateImageUri(string imagePath)
        {
            return Uri.TryCreate(imagePath, UriKind.Absolute, out var absoluteUri)
                ? absoluteUri
                : new Uri(Path.GetFullPath(imagePath), UriKind.Absolute);
        }
    }
}
