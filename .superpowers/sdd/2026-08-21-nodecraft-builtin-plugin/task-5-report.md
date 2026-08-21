# Task 5 Report — Built-In Logic plugin nodes

## Scope and files

Task 5 only. No existing file under `NodeCraft.Flow/Flow/Nodes` was modified or deleted.

Created:

- `NodeCraft.BuiltIn/Nodes/BooleanNodePorts.cs`
- `NodeCraft.BuiltIn/Nodes/{GreaterThan,LessThan,Equal,BooleanAnd,BooleanOr,BooleanNot,If}{NodeModel,Executor}.cs`
- `NodeCraft.BuiltIn/Registrations/LogicNodeRegistrations.cs`
- `NodeCraft.BuiltIn/Views/{GreaterThan,LessThan,Equal,BooleanAnd,BooleanOr,BooleanNot,If}View.xaml`
- `NodeCraft.BuiltIn/Views/{GreaterThan,LessThan,Equal,BooleanAnd,BooleanOr,BooleanNot,If}View.xaml.cs`
- `NodeCraft.Tests/BuiltInLogicNodeTests.cs`
- `.superpowers/sdd/2026-08-21-nodecraft-builtin-plugin/task-5-report.md`

Modified:

- `NodeCraft.BuiltIn/NodeCraft.BuiltIn.csproj`
- `NodeCraft.BuiltIn/Plugin/BuiltInPlugin.cs`
- `NodeCraft.BuiltIn/Views/BuiltInInputViewSupport.cs`
- `NodeCraft.Tests/Program.cs`
- `NodeCraft.Tests/BuiltInMathNodeTests.cs` (approved incremental-decision exception only)

## RED → GREEN

RED was established before production implementation.

1. Added `RunBuiltInLogicNodeTestsAsync()`, the Logic test file, and Program wiring.
2. First compile exposed two test-authoring errors (a partial-class nested record name collision and using `Count` after converting to an array). Both were corrected before accepting RED.
3. Command: `dotnet build NodeCraft.Tests/NodeCraft.Tests.csproj --no-restore`
4. Expected RED: exit 1 with `CS0246`/`CS0103` for the missing seven BuiltIn Logic models, executors, and view types.

GREEN implementation then added the seven models/executors, plugin-owned port helper, registrations, seven embedded XAML resources/code-behind files, plugin registration, and unary binding helper.

Compile command: `dotnet build NodeCraft.Tests/NodeCraft.Tests.csproj --no-restore`

- Result: exit 0, 0 errors.
- The project emits its pre-existing nullable warning set; the two nullable warnings initially introduced in `BuiltInLogicNodeTests.cs` were removed during refactor.

Targeted command (first GREEN and final refactor rerun):

`dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows --no-restore -- --built-in-logic-only`

The narrow Program argument was added so the command executes only the Task 5 group while leaving the default runner path unchanged. Both runs reported these 11 targeted tests as PASS:

1. exact eighteen-node Preview4 → Value3 → Math4 → Logic7 key order
2. seven exact Logic definitions and presentation contracts
3. plugin-local model ports and identities
4. fresh exact models, executors, and XAML views
5. strict factory rejection of every other concrete model, including inherited `BooleanOrNodeModel`
6. binary view atomic injected-slot swap behavior
7. Boolean Not unary source summary
8. If localized branch labels and themed foregrounds
9. seven independent embedded XAML resources and source policy
10. conversion, equality, formula, branch, and cancellation executor behavior
11. representative `>`, `<`, Equal, AND, OR, NOT, and both If-branch workflows using new keys and a local registry

## Execution and control flow

- Comparisons retain invariant numeric conversion and default missing inputs to zero.
- Equal retains `object.Equals` semantics, including distinct strings with equal values and unequal values.
- AND/OR/NOT retain the shared boolean conversion behavior.
- If uses only `BuiltInPortIds.Condition`, `.True`, and `.False` and emits exactly one active control signal.
- Local-registry graph executions prove the selected If downstream succeeds and the unselected downstream is `Skipped` for both conditions.
- All seven executors throw on a pre-cancelled token.

## XAML and STA verification

- Five binary views independently embed formula, description, named `InputAValue`, `InputBValue`, and `SwapInputsButton` elements. Code-behind calls the already-reviewed atomic `BindBinary` helper.
- Boolean Not owns `InputValue` in XAML and code-behind calls `BindUnary`.
- If owns `IF`, `TrueLabel`, and `FalseLabel` in XAML. `Loc FlowPort_true` / `Loc FlowPort_false` and success/danger `DynamicResource` foregrounds are present only in XAML.
- All seven code-behind files only load/validate/find/bind; static policy rejects business control/brush construction.
- STA tests instantiate every view through `FlowNodeRegistry.BuildNodeContent`. A themed visible WPF window verifies If localization produces non-empty True/False text and both themed foregrounds resolve non-null and differently.

## Incremental decision

The approved Task 4 exception was applied minimally in `BuiltInMathNodeTests.cs`: the previous exact 11-item assertion now filters only Preview, Value, and Math registrations before retaining the same exact 11 literal keys, order, fields, and Math behavior assertions. No Math behavior was relaxed.

## Static checks and self-review

Commands/checks used:

- `git diff --check` — no whitespace errors (Git only reported expected LF/CRLF worktree notices).
- `rg` policy checks over `NodeCraft.BuiltIn` and `BuiltInLogicNodeTests.cs` — no `NodeCraft.Flow.Nodes`, `BuiltInNodePorts`, or `FlowPorts.Condition/True/False` dependency.
- code-behind policy search — no `new StackPanel`, `TextBlock`, `TextBox`, `Button`, `Border`, `Grid`, or `SolidColorBrush` in the seven new view code-behind files.
- binding/factory search — five `BindBinary`, one `BindUnary`, and seven exact `GetType()` guards are present.
- status/name review — no `NodeCraft.Flow` production file is changed.

Self-review concerns: none open. The inherited Boolean Or model is deliberately preserved from existing behavior, and exact factory guards prevent it from being accepted by the Boolean And view. The ordinary full runner was not executed after implementation because the updated execution policy reserves repo/full regression for the root after all tasks.

## Baseline/full-run record and commit

Before the updated targeted-only execution policy arrived, the initial baseline command was started:

`dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows`

It completed with the known allowed intermittent failure `TCP connection observes a bounded connect timeout`; all other displayed baseline tests passed. No full runner was started again.

Required commit message: `feat: add built-in Logic plugin nodes with XAML views`

The final commit SHA is reported in the task handoff after commit creation.
