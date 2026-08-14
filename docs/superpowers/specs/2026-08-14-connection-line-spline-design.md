# Connection Line Spline Design

**Date:** 2026-08-14  
**Branch:** `main`

## Goal

让 NodeCraft 的流程连线接近 ComfyUI：连线使用少量控制点的平滑曲线，不再因网格寻路产生大量折点；节点覆盖在连线之上，使穿过节点的曲线在视觉上被节点遮挡。

## Current context

当前 `FlowCanvas` 对每条连接调用 `OrthogonalRouter.Route`。路由器以 `CellSize = 16` 的网格执行 A* 正交寻路，返回多个折线点；`ConnectionLine` 再对每个中间点应用圆角二次 Bézier。因此，路径仍然包含许多不必要的中间点，移动节点时还会频繁重算网格路径。

ComfyUI 的 LiteGraph 前端把网格和连接绘制在背景画布，把节点绘制在前景画布；普通 spline link 使用一段三次 Bézier 曲线，控制点沿端口方向按距离偏移。它不对整张图执行严格的节点障碍物寻路，节点的前景绘制负责遮挡曲线。这是本次要借鉴的视觉与交互模型，而不是引入 ComfyUI 的前端依赖。

## Options considered

### 1. Direct cubic Bézier (recommended)

每条连接只保留起点和终点，按输出端口 `Right`、输入端口 `Left` 生成两个控制点。连线置于节点下方，节点背景覆盖曲线。

优点是点位最少、视觉最接近 ComfyUI、拖动和重绘开销低。缺点是它不提供数学意义上的全局避障；透明节点或节点外观改变时，遮挡效果可能不同。

### 2. Bézier plus full obstacle routing

先用 A* 计算绕开节点的折线路径，再把每段折线转换为连续曲线。

可提供严格避障，但仍需要多个转向点，曲线在密集节点间可能出现过冲，算法和测试复杂度明显增加；不符合本次“减少点位、先看 ComfyUI 效果”的目标。

### 3. Keep orthogonal routing and simplify points

继续使用 A*，仅增加路径压缩、合并短边和更大的圆角。

风险最低，但无法解决正交路径本身的折点感，视觉上仍与 ComfyUI 差异较大。

## Design

### 1. Geometry API and rendering

`ConnectionLine` 保留现有 `Points` 依赖属性，以避免改变控件调用契约：

- 普通连接和拖拽临时连接都只传入两个点：起点、终点。
- 当点数为两个且两点有效时，`DefiningGeometry` 使用一段三次 Bézier：`start -> control1 -> control2 -> end`。
- `controlDistance = max(30, distance(start, end) * 0.25)`。
- 起点控制点沿 `Right` 偏移，终点控制点沿 `Left` 偏移；这与现有 `NodeView` 中输出端口在右侧、输入端口在左侧的模型一致。
- 当 `Points` 包含多于两个点时保留现有圆角折线作为兼容 fallback；本次 `FlowCanvas` 不再生成这种普通连接路径。
- 箭头方向使用曲线末端切线，而不是简单使用终点前一个折点，确保箭头在曲线末端方向正确。

现有线宽、颜色、圆头、命中测试、右键菜单和连接 ID 保持不变。`CornerRadius` 保留以兼容已有属性使用；它只继续影响 fallback 折线，不参与两点 Bézier 的曲率计算。

### 2. FlowCanvas data flow

将 `FlowCanvas.Route` 的普通连接职责收敛为返回 `[start, end]` 两个点，或在 `RedrawConnections`/连接创建处直接构造两点 `PointCollection`。`OrthogonalRouter` 类和既有路由测试暂时保留，作为后续严格避障方案的独立基础，不在本次删除或重构。

以下路径统一使用两点 Bézier：

1. 已保存连接的 `RedrawConnections`。
2. 新连接创建完成后的即时绘制。
3. 用户拖拽连接时的 `_tempLine`。

节点移动、缩放、加载图和连接删除的生命周期不变；它们仍通过现有 `UpdateCanvas`/`RedrawConnections` 更新线条。

### 3. Visual stacking and interaction

连接线必须在节点视觉层下方，但仍能响应现有线条鼠标事件和右键菜单。实现上优先使用显式的低 `Panel.ZIndex`，并确认节点与连接所在 `Canvas` 的命中测试行为不回归；节点和端口仍应优先接收鼠标事件。拖拽临时线保持 `IsHitTestVisible = false`。

如果当前 WPF `Canvas` 的子元素顺序导致节点仍被连线覆盖，则在连接创建与重绘的统一入口设置线条的低层级，避免只修复某一种创建路径。

### 4. Failure and edge cases

- 空点集、单点或相同起终点返回 `Geometry.Empty` 或只绘制可用的 fallback，不抛出异常。
- 距离很小时控制点仍使用最小距离 30，避免曲线退化为不可控的尖角；相同点仍由现有零向量箭头保护逻辑处理。
- 节点重叠、连线在节点后方经过和连接方向相反时不执行额外的全局寻路；节点绘制层负责视觉遮挡。
- WPF 节点背景为透明时，连线可能可见，这是 ComfyUI 式渲染模型的已知边界，不扩展本次范围为透明区域几何避障。

## Testing strategy

沿用 `NodeCraft.Tests` 的自运行测试与 STA/WPF 辅助方法，增加或调整以下回归覆盖：

1. `ConnectionLine` 两点输入生成 Bézier geometry，并且不依赖额外路由点；多点输入仍走旧的圆角折线 fallback。
2. 端点控制点沿输出右侧、输入左侧偏移，距离变化会改变曲率但不会增加路径点。
3. `FlowCanvas` 的持久连接、即时新建连接和拖拽临时连接都只传递两个端点。
4. 连接线的 `Panel.ZIndex` 低于节点，节点/端口仍保持命中测试优先级。
5. 现有 `OrthogonalRouter`、图模型连接、连接删除和节点拖动测试继续通过，证明本次只替换视觉路由，不改变图数据语义。

验证命令：

```powershell
dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows
dotnet build NodeCraft.sln
git diff --check
```

## Acceptance criteria

- 常规连接线由起点和终点定义，不再出现网格 A* 产生的大量折点。
- 连接线视觉上连续、顺滑，输出端和输入端方向自然。
- 线经过节点区域时由节点覆盖，不显示在节点内容上方。
- 新建连接的拖拽预览与已保存连接使用同一种曲线风格。
- 线条右键菜单、删除、选中/悬停高亮和连接数据不回归。
- 现有测试和完整解决方案构建通过。
- 不承诺透明节点场景下的严格几何避障；若后续确实需要，将单独设计障碍物路由策略。
