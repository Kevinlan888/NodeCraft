# 第一批流程节点

## Goal

补齐 NodeCraft 第一批常用的数据转换、字符串处理、比较和流程控制节点，让用户能在现有流程画布中完成更完整的数据处理与分支汇聚。该任务只覆盖需求规划；实现必须在规划摘要获得用户明确批准后开始。

## Background / Confirmed Facts

- 当前第一批交付物为：`To String`、`String Concat`、`!=`、`>=`、`<=`、`Select`、`Merge Flow`；`JSON Parse` 已暂缓，不进入本批实现。
- 现有 `NodeCraft.BuiltIn` 插件通过 Preview、Value、Math、Logic 四个注册工厂提供 18 个节点；当前已有 `JSON Serialize`、`Append Text`、`Greater Than`、`Less Than`、`Equal` 和 `If`，但没有用户列出的这些目标节点；本批次沿用四类，不新增调色板分类：`To String`/`String Concat` 放入 `Preview`，其余五个放入 `Logic`。
- 每个内置节点都通过稳定的 `nodecraft.builtin.*` TypeKey、NodeModel、Executor、独立 XAML ContentFactory 和注册项进入插件；注册表会自动补充 slot 0 的 `flowIn` 控制输入。新节点的稳定 TypeKey 后缀、显示名和 Port ID 采用已确认命名契约，图连线与 XML 依赖这些 ID。
- 当前流程类型包含 `string`、`number`、`boolean`、`object`、`*`、`MATCH_TYPE` 和 `control`；已有动态输入端口、图执行、XML 序列化以及自运行测试跑棒。
- 现有测试覆盖注册契约、工厂实例隔离、端口顺序/类型、Executor 行为、XAML 资源和图执行；新增节点需要沿用这些可观察的验收路径。
- `Select` 已确认是数据选择节点：布尔条件为真时输出 `trueValue`，否则输出 `falseValue`；它不负责激活或跳过控制流分支；`condition`、`trueValue`、`falseValue` 都是必填输入，候选值可为 `null` 并原样输出；`trueValue`、`falseValue` 和输出统一使用 `FlowDataType.Object` 兼容类型。
- `Merge Flow` 已确认用于 `If` 分支处理完成后的公共流程汇合；它只汇合控制信号，不合并数据。分支输入使用动态 `control` 端口，自动注入的 `flowIn` 不计入分支数量。
- 现有 `If` 负责输出 True/False 控制信号；`Select` 的输出应是数据值，`Merge Flow` 的输出应是控制信号。
- `String Concat` 已确认采用动态输入，并提供可选 `Separator` 配置，默认值为空字符串；分隔符插入到相邻输入之间；每个动态片段严格使用 `FlowDataType.String`，非字符串需先经过 `To String`；动态端口最少 2 个、初始 2 个、最大数量不限制且每个片段必填。
- `To String` 已确认使用 `FlowDataType.Object` 输入、`FlowDataType.String` 输出；`null` 输出空字符串，字符串原样输出，数值/布尔使用 invariant 格式，其他对象调用自身 `ToString()`，不自动 JSON 序列化。
- 比较节点规则已确认：`!=` 对任意对象执行对象不等，`>=`/`<=` 复用现有 `>`/`<` 的数值转换，数值输入缺失按 `0`，无法转换时节点失败；`!=` 使用两个 `object` 输入，`>=`/`<=` 使用两个 `number` 输入，三者输出 `boolean`。
- 现有数值比较节点通过 `NodeValueConverter.ToDouble` 比较，缺失值按 `0` 处理；现有字符串追加节点把缺失或非字符串后缀按空字符串处理。这些是可复用的实现基线。

## Requirements

### R1. 数据转换

- 提供 `To String`，使用 `FlowDataType.Object` 输入和 `FlowDataType.String` 输出，把流程值转换为字符串，并遵循已确认的 null、字符串、invariant 格式和其他对象转换规则。

### R2. 字符串处理

- 提供 `String Concat`，按输入顺序把最少 2 个、可无限增加的 `FlowDataType.String` 片段组合为一个字符串输出；每个动态片段必填；提供可选 `Separator` 配置，默认为空字符串，仅在相邻输入之间插入。

### R3. 比较

- 提供三个独立的逻辑节点：`!=`、`>=`、`<=`。
- `!=` 使用两个 `object` 输入并输出 `boolean`；`>=`/`<=` 使用两个 `number` 输入并输出 `boolean`。节点继续沿用现有数值转换、缺失值和失败行为。

### R4. 数据选择

- 提供 `Select` 数据节点，包含布尔条件、`trueValue`、`falseValue` 和一个数据输出。
- `condition` 使用必填 `FlowDataType.Boolean` 输入，`trueValue`/`falseValue` 使用必填 `FlowDataType.Object` 输入；条件为真输出 `trueValue`，条件为假输出 `falseValue`；候选值为 `null` 时原样输出；节点不输出控制信号，也不改变两条数据来源节点的执行状态。
- `trueValue`、`falseValue` 和输出统一使用 `FlowDataType.Object` 兼容类型；本批次不新增同类型传播或核心类型校验；两个候选来源都必须提供值，不做惰性执行。

### R5. 流程汇聚

- 提供 `Merge Flow` 控制节点，把 `If` 的多条控制分支汇聚为一路公共控制输出。
- 分支输入至少 2 个、初始 2 个、最大数量不限制；任一路分支 active 时只输出一次 `flowOut = Active`，多路同时 active 不重复触发；没有分支 active 时不输出、下游不执行。
- 动态分支端口使用稳定的 `branch_1`、`branch_2` 等 ID，并沿用现有动态端口的画布操作与 XML 往返机制。

### R6. 插件与兼容性

- 新节点属于现有 `nodecraft.builtin` 插件，并在现有 `Preview`/`Logic` 调色板分类中可发现、创建和执行；显示名为 `To String`、`String Concat`、`!=`、`>=`、`<=`、`Select`、`Merge Flow`。
- 每个节点使用稳定且唯一的 TypeKey；采用 `nodecraft.builtin.to-string`、`string-concat`、`not-equal`、`greater-than-or-equal`、`less-than-or-equal`、`select`、`merge-flow`；节点模型、端口链接和图文件往返必须保持可恢复。
- 保持现有插件隔离、共享程序集、主题 `DynamicResource`、独立 XAML 视图和错误日志约定；不引入旧 TypeKey 迁移层。

## Acceptance Criteria

- [ ] 7 个目标节点（`To String`、`String Concat`、3 个比较节点、`Select`、`Merge Flow`）的最终端口与边界语义已写入规划并得到用户确认。
- [ ] 所有目标节点能通过 `BuiltInPlugin` 注册，TypeKey 唯一，调色板显示名称/分类/图标/视图契约明确。
- [ ] `Select` 的行为可由测试观察到：条件为真/假分别得到两个候选值，且不会把候选节点错误地标记为控制流分支。
- [ ] `Merge Flow` 的测试覆盖 True/False 分支汇合、无 active 输入以及同一 iteration 多路 active 的结果。
- [ ] `To String` 测试覆盖 null、字符串、数值/布尔 invariant 格式和普通对象 `ToString()`。
- [ ] 比较节点测试覆盖对象不等、数值边界、缺失值和非法数值失败。
- [ ] `String Concat` 测试覆盖动态输入顺序、默认空分隔符和自定义分隔符。
- [ ] 每个目标节点有可观察的 Executor 行为测试；比较、选择和控制流测试覆盖成功、未激活或缺失输入等边界。
- [ ] 目标节点的真实 XAML ContentFactory 可创建新实例，使用主题资源，不依赖核心默认业务控件工厂。
- [ ] 典型工作流可执行，节点链接能按稳定 Port ID 解析，保存后重新加载仍能恢复节点与连线。
- [ ] 现有内置节点、插件加载/打包、图执行和测试跑棒不回归。

## Open Questions

无。产品范围、数据语义、端口类型、动态输入边界、调色板分类和稳定命名均已确认；实现前只需在 design.md / implement.md 固化技术方案和验证清单。

## Out of Scope (暂定)

- 不新增独立插件，不改变现有插件加载协议。
- 不为历史 `node.*` TypeKey 增加自动迁移或兼容层。
- `JSON Parse` 暂缓到后续批次，不在本次实现。
- 不在本批次顺带扩展未列出的算术、集合、循环或异步控制节点。

## Notes

- 这是复杂任务；规划完成后需要补充 `design.md` 和 `implement.md`，再经过最终规划摘要批准，才能执行 `task.py start`。
- `prd.md` 只记录需求、约束和验收；技术边界与执行清单分别写入 `design.md`、`implement.md`。
