# 第一批流程节点实施清单

本清单只在最终规划摘要获得用户明确批准后执行。当前阶段不运行 `task.py start`，不修改产品代码。

## 1. 文件与注册结构

- [ ] 扩展 `NodeCraft.BuiltIn/Nodes/BuiltInPortIds.cs` 或新增插件私有端口常量，加入 `trueValue`、`falseValue`、`flowOut`、`branch` 等稳定 ID。
- [ ] 新增 7 个 NodeModel 与 7 个 Executor；`StringConcatNodeModel` 实现 `IWorkflowNodeValueProvider` 并持有 `Separator`。
- [ ] 新增 Preview/Logic 注册方法，补充 `BuiltInPlugin.Register` 使用的注册工厂；保证 TypeKey、Model、Executor、NodeFactory、ContentFactory 一一对应。
- [ ] 为 String Concat 配置 `FlowDynamicInputTemplate`：`input` 前缀、string 类型、最少/初始 2、无限上限、必填、Iteration。
- [ ] 为 Merge Flow 配置 control 动态模板：`branch` 前缀、最少/初始 2、无限上限、非必填、Iteration；保留自动注入的 `flowIn`。

## 2. Executor 实现

- [ ] 实现 To String 的 null、string、invariant 数值/布尔、普通对象 ToString 规则及取消令牌检查。
- [ ] 实现 String Concat 的有效 Definition 顺序遍历、Separator 配置读取、相邻分隔符规则和缺失输入错误边界。
- [ ] 基于现有 Equal/GreaterThan/LessThan 代码抽取或复用转换逻辑，实现 `!=`、`>=`、`<=`，不改变旧 Executor 行为。
- [ ] 实现 Select 的布尔选择、null 候选原样输出、必填值校验边界，不增加控制输出。
- [ ] 实现 Merge Flow 的任一路 active 单次输出、多路 active 去重和无 active 空输出。

## 3. XAML 与编辑器

- [ ] 为 7 个节点新增独立 XAML/代码后置 ContentFactory，复用现有视图布局与 `DynamicResource` 主题键。
- [ ] String Concat 编辑器提供 Separator 文本框，变更时更新 NodeModel 并通知画布图已修改。
- [ ] 其余节点提供最小可观察的公式、端口标签或状态摘要，不将业务逻辑放入 XAML。
- [ ] 检查动态输入添加/删除按钮、端口顺序、flowIn slot 0 和 control 端口的视觉方向。

## 4. 测试更新与新增

- [ ] 更新 BuiltIn 注册数量、TypeKey 列表和分类契约：现有 18 个加新增 7 个，合计 25 个。
- [ ] 增加各 NodeModel 的稳定 ExecutorType、Name、InputParameters/OutputParameters 测试。
- [ ] 增加各注册项的 Definition、PaletteDisplayName/Description、分类图标、专属图标和 XAML ContentFactory 实例隔离测试。
- [ ] To String：null、string、数值/布尔 invariant、普通对象 ToString、ToString null/异常边界。
- [ ] String Concat：两输入、动态顺序、添加/删除、默认/自定义 Separator、无额外首尾分隔符、XML v5 动态端口往返。
- [ ] 比较节点：对象不等、相等对象、数值边界、缺失数值按 0、非法数值失败、取消令牌。
- [ ] Select：真/假候选、null 候选、必填输入、无控制输出、候选类型为 object。
- [ ] Merge Flow：True/False 汇合、第三路动态分支、无 active、多路同时 active 只输出一次、下游跳过/执行、XML 往返。
- [ ] 构造代表性工作流，验证节点链接按 Port ID 解析，保存/加载后节点和连线恢复。

## 5. 验证命令

在实现完成后按项目约定执行：

1. `dotnet build NodeCraft.sln`
2. `dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows`
3. 按 `trellis-check` 做规格、类型、测试、图数据流、XAML 和插件打包检查。

预期测试输出为 `ALL PASS`。若失败，先修复契约或实现，再重复完整验证；不要只运行新增测试。

## 6. 交付前检查

- [ ] PRD 中的 Open Questions 保持为空，未引入 JSON Parse 或其他未列节点。
- [ ] 旧 18 个内置节点的 TypeKey、分类、注册顺序和行为无回归。
- [ ] 新增节点所有稳定 ID、动态端口元数据和 Separator 配置可持久化恢复。
- [ ] 通过质量检查后运行 `trellis-finish-work`，提醒用户提交变更；在用户批准前不得执行本清单。
