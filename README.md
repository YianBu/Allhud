# AllHud（bya 改）

FFXIV Dalamud HUD 定制插件。本仓库由 `QiongHHHZZZ（岚玉棠改）` 的 AllHud 反编译源码二次修改而来，仅供个人学习使用；公开分发前请自行确认原作者许可。

## 主要改动（相对原版）

- 套装切换器：由单层列表改为 **角色 -> 职业 -> 套装** 两级菜单；职业默认折叠，点击职业标题展开该职业的配装，点击套装行直接切换。
- 职业菜单行支持自定义缩放（设置中"职业菜单缩放"，0.5–2.0，只放大职业行与图标字号，不影响展开后的套装行）。
- 套装弹窗固定 3 列：`防护/治疗` ｜ `近战/远程物理/远程魔法` ｜ `采集/生产`（"其他"分组追加在第 3 列底部）。
- 作者署名追加 `（bya改）`。

## 目录结构

```
src/                   反编译源码工程（.NET 10，引用卫月 SDK）
AllHud.json            插件清单（模板）
pluginmaster.json      第三方库清单（库链接用）
plugins/AllHud/latest.zip  打包好的插件发布物
```

## 本地构建

```
dotnet build src/AllHud.csproj -c Release
```

产物在 `src/bin/Release/net10.0/AllHud.dll`。csproj 通过环境变量 `APPDATA` 下的 `XIVLauncherCN\addon\Hooks\dev` 引用卫月 SDK；路径不同请自行修改 `AllHud.csproj` 中的 `DalamudLibPath`。

## 本地加载（开发）

1. 完全退出游戏。
2. 将 `AllHud.dll + AllHud.json` 放到卫月可识别的开发插件位置（本机为游戏内导入所记录的路径，或 `%APPDATA%\XIVLauncherCN\devPlugins\AllHud\`）。
3. 重新启动游戏，卫月启动时加载。

## 发布新版本

1. 修改 `pluginmaster.json` 与 `AllHud.json` 中的 `AssemblyVersion`。
2. 重新构建并更新 `plugins/AllHud/latest.zip`（zip 根目录须含 `AllHud.dll` 与 `AllHud.json`）。
3. 提交推送，库链接即：

```
https://raw.githubusercontent.com/USERNAME/REPONAME/main/pluginmaster.json
```

在游戏内 卫月 -> 插件安装器 -> 第三方仓库 中添加该链接即可安装。
