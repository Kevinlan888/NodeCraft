# 本地 NuGet 源

- `CommonControls.WPF.1.0.0.nupkg` 为控件库本地过渡包。
- 更新方式：在 CommonControls.WPF 仓库 `dotnet pack`，将新 nupkg 覆盖到本目录并更新 `nuget.config` 对应版本。
- 后续可迁移到私有 feed 或 nuget.org。
