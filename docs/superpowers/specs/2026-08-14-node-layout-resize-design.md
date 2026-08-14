# Node Layout and Resizable Content Design

**Date:** 2026-08-14  
**Branch:** `main`

## Goal

让流程节点接近 ComfyUI 的布局：输入端口保留在左侧，输出端口保留在右侧，两侧端口从节点内容区顶部开始按定义顺序向下排列；节点内部内容不再垂直居中，resize 时可以使用新增的可用空间。图片预览节点需要把图片区域填满可用内容区。

## Scope

本次改动包含：

1. 调整 `NodeView` 默认模板的内容宿主和左右端口对齐方式。
2. 让普通节点内容继续由纵向 `StackPanel` 自己决定内部排列。
3. 让图片预览节点直接使用 `Grid` 作为内容根，并让图片区域使用星号行填充剩余空间。
4. 移除图片预览区域的固定宽高，保留可用的最小尺寸。
5. 增加模板和 STA/WPF 布局回归测试。

不包含端口位置从左右边缘改到上下边缘、不包含端口 slot 语义或连线算法调整，也不改变插件自定义 `ContentFactory` 的接口。

## Design

### 1. NodeView template layout

`NodeCraft.Flow/Themes/Flow.xaml` 中的 `NodeView` 模板继续使用外层 `Grid`：左列放输入端口，中间列放内容，右列放输出端口。输入和输出 `StackPanel` 的 `VerticalAlignment` 改为 `Top`，使端口列表不再在内容区垂直居中。

中间内容宿主使用可伸展的 `Grid`。`NodeView.HorizontalContentAlignment` 和 `VerticalContentAlignment` 默认设为 `Stretch`，内容宿主及其 `ContentPresenter` 显式使用模板绑定的伸展对齐方式。节点 resize 时，中间内容区的实际宽高随节点变化，端口仍仅根据自身列表高度从顶部向下排列。

保留 `ResizeThumb`、节点最小宽高、选中状态、端口生成顺序和现有 socket 坐标计算。这样连线端点会继续读取实际 `Connector` 位置，不需要改变 `FlowCanvas` 的连线逻辑。

### 2. ContentFactory root layout

`DefaultFlowNodeContentFactory.Build` 按节点类型选择内容根布局：

- 普通编辑、运算、文本预览节点继续使用纵向 `StackPanel`，标题、编辑控件和说明文字按自然高度从上到下排列。
- `ImagePreviewNodeModel` 直接返回图片预览 `Grid`，不再把图片预览嵌套在外层纵向 `StackPanel` 中。这样图片区域可以获得 `Grid` 内容宿主分配的剩余高度。

图片预览 `Grid` 的行定义为标题 `Auto`、图片区域 `*`、可选路径信息 `Auto`。预览 `Border` 和内部 `Image` 使用横向及纵向 `Stretch`，只保留最小宽高，不设置固定 `Width=180`、`Height=120`。图片仍使用 `Stretch.UniformToFill`，保持当前裁剪填充行为，避免因节点 resize 产生变形。

图片路径不存在、加载失败或尚未有输入时，仍在图片区域显示现有占位/错误文本；这些文本在图片区域内居中，不影响布局填充。

### 3. Data flow and compatibility

节点模型的 `Width`、`Height` 以及端口 definition 不变。`NodeView` resize 仍即时写回 `NodeModel` 并触发画布布局更新；现有序列化逻辑继续负责保存尺寸。

自定义插件内容仍通过 `ContentFactory` 注入。模板只提供可伸展的宿主，不强制插件根元素必须是 `Grid` 或 `StackPanel`；插件内容选择自身需要的布局类型。

## Testing strategy

测试沿用 `NodeCraft.Tests` 现有的自运行测试和 STA/WPF 辅助方法：

1. 读取 `Flow.xaml`，验证 `NodeView` 默认内容对齐为 `Stretch`，左右 socket panel 的垂直对齐为 `Top`，并确认内容宿主存在显式的 stretch 绑定。
2. 在主题资源加载的 STA 窗口中创建带有可观察子控件的 `NodeView`，比较节点在两组宽高下的内容宿主实际尺寸，确认 resize 增长会传递到内部内容区。
3. 创建 `ImagePreviewNodeModel`，确认内容根为 `Grid`，图片区域行使用 `*`，预览容器没有固定宽高，并保留图片加载失败/占位分支。
4. 创建普通节点内容，确认普通节点仍以纵向 `StackPanel` 呈现，编辑控件和说明文字没有被图片预览布局改变。
5. 保持现有端口 slot、连接和尺寸持久化测试通过。

最终执行 `dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows` 和 `dotnet build NodeCraft.sln`。

## Acceptance criteria

- 输入端口仍在左侧，输出端口仍在右侧。
- 端口从节点内容区顶部开始向下排列，不再整体垂直居中。
- 节点内部普通内容从顶部开始排列，resize 后不会只增加上下空白。
- 图片预览随节点宽高扩大，图片区域填充新增空间，路径信息仍位于底部（存在时）。
- 图片错误和占位提示仍可显示。
- 插件自定义内容、端口 slot、连线端点和节点尺寸持久化行为不回归。
- 新增回归测试和完整现有测试通过。
