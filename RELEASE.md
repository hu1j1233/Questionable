# 发布流程

本仓库的正式插件发布由 `.github/workflows/release.yml` 负责。`new-main` 对应正式版，`testing` 对应预发布版。

## 1. 准备版本

编辑 `Directory.Build.targets`，同步修改两个版本号：

```xml
<Version>YYYY.M.D.N</Version>
<ReleaseVersion>YYYY.M.D.N</ReleaseVersion>
```

版本使用日期格式。当天第一次发布使用 `.1`，同一天重新发布使用递增的序号。

同时更新 `CHANGELOG.md`，把本次变更写在最前面。

## 2. 本地验证

在仓库根目录执行：

```powershell
$env:DALAMUD_HOME='C:\Users\huiji\AppData\Roaming\XIVLauncherCN\addon\Hooks\dev'
dotnet build Questionable\Questionable.csproj -c Debug --no-restore
git diff --check
```

本地 DLL 位于：

```text
Questionable\dist\Questionable.dll
```

## 3. 提交并触发发布

确认工作区只有预期改动后，提交并推送到对应分支：

```powershell
git status -sb
git add Directory.Build.targets CHANGELOG.md <其他预期文件>
git commit -m "Release YYYY.M.D.N"
git push -u origin new-main
```

推送是触发 GitHub Release 的必要步骤；只在本地 commit 不会触发 Actions。

## 4. 检查 GitHub Actions

```powershell
gh run list --repo hu1j1233/Questionable --workflow release.yml --branch new-main --limit 10
gh release view vYYYY.M.D.N --repo hu1j1233/Questionable
```

正常流程会：

1. 从 `ReleaseVersion` 读取版本；
2. 构建 Release 配置并打包 `latest.zip`；
3. 创建并推送 `vYYYY.M.D.N` 标签；
4. 创建 GitHub Release，并上传 `latest.zip`、`Questionable.json` 和校验文件。

仅修改 `QuestPaths/**` 或 `GatheringPaths/**` 的提交不会触发完整插件发布，而会由 `publish-paths.yml` 发布路径包。

## 5. 常见问题

### 没有出现 Actions 运行记录

先确认：

```powershell
git ls-remote origin refs/heads/new-main
gh workflow list --repo hu1j1233/Questionable
gh run list --repo hu1j1233/Questionable --workflow release.yml --limit 20
```

如果 GitHub 已收到推送但没有 `push` 类型运行记录，可手动触发：

```powershell
gh workflow run release.yml --repo hu1j1233/Questionable --ref new-main
```

### 版本标签已经存在

普通触发会因为 `vYYYY.M.D.N` 已存在而跳过发布。应优先递增版本号；只有确认要重建同一版本时，才使用：

```powershell
gh workflow run release.yml --repo hu1j1233/Questionable --ref new-main -f force_release=true
```

强制发布会删除并重新创建同名标签和 Release，应谨慎使用。

### Release 已创建但插件列表没有更新

当前 `release.yml` 中的 `update-pluginmaster` job 被注释，Release 不会自动更新 `pluginmaster.json`。需要单独维护插件仓库清单或另行执行仓库的插件索引更新流程。
