# Dalamud 插件库（bya）

FFXIV Dalamud 第三方插件库，使用统一的 `pluginmaster.json` 维护插件清单。后续新增插件会一并出现在同一库中，安装链接保持不变。

## 安装

在游戏内 卫月（Dalamud）→ 插件安装器 → 第三方仓库 中添加以下库链接，即可浏览并安装本库收录的全部插件（含 AllHud）：

```
https://raw.githubusercontent.com/YianBu/Plugins/main/pluginmaster.json
```

插件库：<https://github.com/YianBu/Plugins>

插件项目：<https://github.com/YianBu/Allhud>

## 插件列表

### AllHud（bya 改）

FFXIV Dalamud HUD 定制插件。本插件由 `QiongHHHZZZ（岚玉棠改）` 的 AllHud 反编译源码二次修改而来，仅供个人学习使用；公开分发前请自行确认原作者许可。

#### 主要改动（相对原版）

- 套装切换器：由单层列表改为 **角色 -> 职业 -> 套装** 两级菜单；职业默认折叠，点击职业标题展开该职业的配装，点击套装行直接切换。
- 职业菜单行支持自定义缩放（设置中"职业菜单缩放"，0.5–2.0，只放大职业行与图标字号，不影响展开后的套装行）。
- 套装弹窗固定 3 列：`防护/治疗` ｜ `近战/远程物理/远程魔法` ｜ `采集/生产`（"其他"分组追加在第 3 列底部）。
- 作者署名追加 `（bya改）`。

## 目录结构

```
pluginmaster.json                插件库清单（所有插件共用）
plugins/<插件名>/latest.zip      各插件的发布包
```

