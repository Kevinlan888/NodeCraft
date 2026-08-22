# 第一批流程节点技术设计

## 1. 目标与边界

本设计覆盖 `NodeCraft.BuiltIn` 中第一批 7 个节点：`To String`、`String Concat`、`!=`、`>=`、`<=`、`Select`、`Merge Flow`。`JSON Parse` 已从本批次排除。

实现沿用现有 BuiltIn 插件、FlowNodeRegistry、GraphExecutor、动态端口解析器、v5 XML 图格式和 XAML ContentFactory，不修改核心类型系统，不新增插件或历史 TypeKey 迁移层。

所有 Definition 都会由注册表自动获得 slot 0 的 `flowIn` 控制输入。业务代码必须按稳定 Port ID 取值，不能把 `InputParameters` 索引当作 Definition slot。

## 2. 稳定注册契约

| 显示名 | TypeKey | 分类 | 分类图标 | 业务输出 |
| --- | --- | --- | --- | --- |
| To String | `nodecraft.builtin.to-string` | Preview | 复用 Preview 风格 | `output: string` |
| String Concat | `nodecraft.builtin.string-concat` | Preview | 复用 Preview 风格 | `output: string` |
| != | `nodecraft.builtin.not-equal` | Logic | 复用 Logic 风格 | `output: boolean` |
| >= | `nodecraft.builtin.greater-than-or-equal` | Logic | 复用 Logic 风格 | `output: boolean` |
| <= | `nodecraft.builtin.less-than-or-equal` | Logic | 复用 Logic 风格 | `output: boolean` |
| Select | `nodecraft.builtin.select` | Logic | 复用 Logic 风格 | `output: object` |
| Merge Flow | `nodecraft.builtin.merge-flow` | Logic | 复用 Logic 风格 | `flowOut: control` |

固定端口使用插件私有常量。比较节点沿用 `inputA`、`inputB`、`output`；普通转换使用 `input`、`output`；Select 使用 `condition`、`trueValue`、`falseValue`、`output`。显示名采用用户确认的节点名和现有 A/B/Result 风格。

## 3. 节点 Definition 与运行时语义

### 3.1 To String

- 输入：必填 `input: object`。
- 输出：`output: string`。
- `null` 输出空字符串；已有字符串原样输出；数值和布尔使用 invariant 格式；其他对象调用自身 `ToString()`，不调用 JSON 序列化。
- 若普通对象的 `ToString()` 返回 null，Executor 将以空字符串兜底，保证 string 输出契约。

### 3.2 String Concat

- 动态输入：`input_1`、`input_2`……，类型均为必填 `string`。
- 动态模板：`PortIdPrefix = input`、`DisplayNamePrefix = Input`、`MinCount = 2`、`InitialCount = 2`、`MaxCount = null`、`IsRequired = true`、`Availability = Iteration`。
- 输出：`output: string`。
- NodeModel 公共配置：`Separator`，默认空字符串；通过 `IWorkflowNodeValueProvider.WriteWorkflowInputs` 写入稳定配置 key `separator`，由 XML 序列化器保存公共属性。
- Executor 按有效 Definition 中动态端口的顺序读取值，用 `Separator` 只连接相邻片段；不枚举输入字典，不在首尾额外添加分隔符。
- 非字符串值不由本节点隐式转换；画布连接前应使用 `To String`。

### 3.3 `!=`

- 输入：必填 `inputA: object`、`inputB: object`。
- 输出：`output: boolean`。
- 复用 `EqualExecutor` 的对象相等基线，输出 `!Equals(left, right)`；缺失输入按现有 Executor 读取习惯处理，null 参与对象比较。

### 3.4 `>=` 与 `<=`

- 输入：必填 `inputA: number`、`inputB: number`。
- 输出：`output: boolean`。
- 分别复用 `GreaterThanExecutor`、`LessThanExecutor` 的 `NodeValueConverter.ToDouble` 转换边界；缺失数值输入按 0，无法转换时让节点失败。
- Definition 保持 number 端口，避免把运行时转换能力误认为画布类型兼容能力。

### 3.5 Select

- 输入：必填 `condition: boolean`、`trueValue: object`、`falseValue: object`。
- 输出：`output: object`。
- 条件为真返回 trueValue，否则返回 falseValue；候选值可以是 null，选中后原样输出。
- 不产生控制输出，不改变两个候选来源节点的执行状态；两个候选来源均需先提供值，当前引擎不做惰性分支执行。
- 不使用 `MATCH_TYPE`，不增加候选值同类型传播或核心类型校验。

### 3.6 Merge Flow

- 自动固定输入：`flowIn: control`，由注册表注入，不计入分支数量。
- 动态分支输入：`branch_1`、`branch_2`……，类型 `control`，`MinCount = 2`、`InitialCount = 2`、`MaxCount = null`、`IsRequired = false`、`Availability = Iteration`。
- 输出：`flowOut: control`。
- `FlowGraphIterationRunner` 会扫描所有 control 输入；任一已连接分支为 `FlowControlSignal.Active` 时，Executor 返回一次 `flowOut = Active`。多路同时 active 仍只有一次输出。
- 没有 active 分支时返回空输出；没有控制连线导致 Runner 进入 Executor 时，也必须返回空输出，不能伪造 Active。
- 下游节点只会在收到 `flowOut` 的 Active 信号时执行；动态分支 ID、顺序和 LinkId 由现有动态端口及 XML v5 机制保存恢复。

## 4. 分层与配置持久化

- NodeModel 负责名称、端口模型、`Separator` 等编辑态配置；动态端口由通用 `FlowDynamicInputResolver` materialize，不能在 Executor 构造函数中创建。
- Executor 只消费 `inputs` 中已解析的当前轮值，并从 `node.Inputs` 读取静态配置；不得把 LinkRef 当作业务值。
- `StringConcatNodeModel` 实现 `IWorkflowNodeValueProvider`，将 null Separator 归一为空字符串后写入 `separator`。
- 所有端口由 Definition 声明，模型端口列表只用于画布状态和持久化；图链接恢复依赖 Port ID 与定义 slot 的解析过程。

## 5. UI 与注册实现

- 每个节点提供独立 XAML ContentFactory 和对应 NodeModel 类型，遵循现有插件隔离规则。
- `String Concat` 视图提供 Separator 文本框，初始化读取 NodeModel.Separator，文本变化更新模型并调用 `NotifyGraphChanged(false)`。
- 无复杂配置的转换、比较、Select、Merge Flow 视图采用现有轻量公式/端口摘要模式；所有颜色和边框使用 `DynamicResource`，不硬编码主题色。
- Preview 节点使用 Preview 分类图标，Logic 节点使用 Logic 分类图标；节点专属图标保持唯一且使用现有可用图标资源。

## 6. 错误与兼容性

- 端口类型校验交给 `FlowDataType.IsCompatibleWith` 与现有 Registry/GraphExecutor，不在节点中复制类型算法。
- 输出交给 `FlowRuntimeValueValidator` 校验；空输出的 Merge Flow 不返回未知 output key。
- 保持既有 18 个节点的 TypeKey、注册顺序契约和图格式兼容；新增节点不改变旧图的迁移策略。
- `To String` 的对象 ToString 异常、比较非法数值异常应保留为节点失败并进入现有日志/ExecutionContext 状态，不静默吞掉异常。
