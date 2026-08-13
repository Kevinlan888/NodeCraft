# NodeCraft

基于 WPF 与 CommonControls.WPF 的流程编辑与执行应用。

## 构建

```bash
dotnet build NodeCraft.sln
```

> WPF 项目需在 Windows 上构建（Linux 缺少 WindowsDesktop SDK）。

## 运行

```bash
dotnet run --project NodeCraft/NodeCraft.csproj
```

## 测试

```bash
dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows
# 期望输出 ALL PASS
```

## 依赖

- `CommonControls.WPF` 通过 NuGet 引用，本地包位于 `Packages/`（版本 `1.0.0`）。
- 更新本地包：在 CommonControls.WPF 仓库执行 `dotnet pack` 并将 nupkg 覆盖到 `Packages/`。
