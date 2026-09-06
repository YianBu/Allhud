using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using AllHud.Data;
using AllHud.Models;
using AllHud.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace AllHud.Windows;

public sealed class ConfigWindow
{
	private enum ConfigPage
	{
		外观,
		状态栏,
		目标情报,
		队伍信息,
		任务栏,
		技能,
		调试
	}

	private enum TaskBarPage
	{
		任务栏,
		辅助栏,
		插件收纳,
		高级
	}

	private readonly record struct PickedImGuiWindow(string Name, string Key);

	private readonly record struct TaskBarComponentDefinition(string Id, string Icon, string Name, string Description);

	private readonly record struct CustomShortcutPreset(string Name, string DisplayName, uint IconId, string Command, string Description);

	private readonly record struct PendingCustomShortcutCommandRemoval(string RowUiId, bool IsPendingLine);

	private readonly record struct CurrencyDisplayOption(uint ItemId, string Name);

	private readonly record struct JobSkillRoleGroup(string Label, IReadOnlyList<uint> ClassJobIds);

	private const int InstalledPluginSelectionCacheTtlMs = 1000;

	private static readonly string[] GearsetSettingsGroupOrder = new string[8] { "防护职业", "治疗职业", "近战职业", "远程物理职业", "远程魔法职业", "生产职业", "采集职业", "其他" };

	private static readonly uint[] CommonCustomShortcutIconIds = new uint[23]
	{
		1u, 2u, 5u, 7u, 14u, 17u, 20u, 21u, 27u, 40u,
		45u, 60u, 74u, 111u, 112u, 60073u, 60074u, 60411u, 60412u, 60413u,
		60414u, 60415u, 60453u
	};

	private static readonly CustomShortcutPreset[] DailyRoutinesCustomShortcutPresets = new CustomShortcutPreset[15]
	{
		new CustomShortcutPreset("DR 木人：重置仇恨", "木人重置", 60413u, "/pdr resetallsd", "Daily Routines 木人模块指令：重置全部木人仇恨。"),
		new CustomShortcutPreset("DR 传送：快捷传送面板", "快捷传送面板", 60453u, "/pdrtp", "Daily Routines 快捷传送面板。"),
		new CustomShortcutPreset("DR 特殊场景：幻象群岛", "幻象群岛", 60415u, "/pdrfe ocs", "Daily Routines 特殊场景进入指令：幻象群岛。"),
		new CustomShortcutPreset("DR 特殊场景：云冠群岛", "云冠群岛", 60415u, "/pdrfe diadem", "Daily Routines 特殊场景进入指令：云冠群岛。"),
		new CustomShortcutPreset("DR 特殊场景：开拓无人岛", "开拓无人岛", 60415u, "/pdrfe island", "Daily Routines 特殊场景进入指令：开拓无人岛。"),
		new CustomShortcutPreset("DR 特殊场景：博兹雅", "博兹雅", 60415u, "/pdrfe bozja", "Daily Routines 特殊场景进入指令：博兹雅。"),
		new CustomShortcutPreset("DR 特殊场景：扎杜诺尔", "扎杜诺尔", 60415u, "/pdrfe zadnor", "Daily Routines 特殊场景进入指令：扎杜诺尔。"),
		new CustomShortcutPreset("DR 特殊场景：常风之地", "常风之地", 60415u, "/pdrfe anemos", "Daily Routines 特殊场景进入指令：常风之地。"),
		new CustomShortcutPreset("DR 特殊场景：恒冰之地", "恒冰之地", 60415u, "/pdrfe pagos", "Daily Routines 特殊场景进入指令：恒冰之地。"),
		new CustomShortcutPreset("DR 特殊场景：涌火之地", "涌火之地", 60415u, "/pdrfe pyros", "Daily Routines 特殊场景进入指令：涌火之地。"),
		new CustomShortcutPreset("DR 特殊场景：丰水之地", "丰水之地", 60415u, "/pdrfe hydatos", "Daily Routines 特殊场景进入指令：丰水之地。"),
		new CustomShortcutPreset("DR 特殊场景：憧憬湾", "憧憬湾", 60415u, "/pdrfe ardorum", "Daily Routines 特殊场景进入指令：憧憬湾。"),
		new CustomShortcutPreset("DR 特殊场景：法恩娜", "法恩娜", 60415u, "/pdrfe phaenna", "Daily Routines 特殊场景进入指令：法恩娜。"),
		new CustomShortcutPreset("DR 特殊场景：俄匊斯", "俄匊斯", 60415u, "/pdrfe oizys", "Daily Routines 特殊场景进入指令：俄匊斯。"),
		new CustomShortcutPreset("DR 特殊场景：奥克塞西亚", "奥克塞西亚", 60415u, "/pdrfe auxesia", "Daily Routines 特殊场景进入指令：奥克塞西亚。")
	};

	private static readonly TaskBarComponentDefinition[] TaskBarComponentDefinitions = new TaskBarComponentDefinition[16]
	{
		new TaskBarComponentDefinition("time", "◷", "时间", "显示本地时间 / 艾欧泽亚时间"),
		new TaskBarComponentDefinition("fps", "▦", "FPS", "显示当前帧率"),
		new TaskBarComponentDefinition("main_menu", "☰", "主菜单", "打开 AllHud 主菜单"),
		new TaskBarComponentDefinition("volume", "♪", "音量控制", "调整音量并打开音量面板"),
		new TaskBarComponentDefinition("plugin_list", "◇", "插件列表", "自己添加常用插件并快速打开"),
		new TaskBarComponentDefinition("plugin_shortcut", "◇", "插件快捷方式", "选择一个插件，点击图标直接打开"),
		new TaskBarComponentDefinition("custom_shortcut", "✦", "自定义快捷方式", "自定义名称、图标和执行命令"),
		new TaskBarComponentDefinition("quick_menu", "▣", "快捷菜单", "把常用项目放进一个弹出菜单"),
		new TaskBarComponentDefinition("server_info", "◎", "服务器信息栏", "显示服务器信息栏条目"),
		new TaskBarComponentDefinition("inventory", "□", "背包", "显示背包已用 / 总格数"),
		new TaskBarComponentDefinition("saddlebag", "□", "陆行鸟鞍囊", "显示鞍囊已用 / 总格数"),
		new TaskBarComponentDefinition("teleport", "✈", "传送", "快速访问已共鸣的以太之光。"),
		new TaskBarComponentDefinition("coordinates", "⌖", "坐标", "显示你在游戏世界中的当前位置。"),
		new TaskBarComponentDefinition("gearset_switcher", "⚒", "套装切换", "显示当前套装，并可快速切换。"),
		new TaskBarComponentDefinition("currency", "¤", "货币", "显示当前的金币和军票。"),
		new TaskBarComponentDefinition("walking_indicator", "♟", "步行指示器", "显示当前步行/跑步状态，点击可切换。")
	};

	private static readonly Dictionary<string, TaskBarComponentDefinition> TaskBarComponentDefinitionLookup = TaskBarComponentDefinitions.ToDictionary<TaskBarComponentDefinition, string>((TaskBarComponentDefinition component) => component.Id, StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, int> pendingCustomShortcutCommandLineCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, List<string>> customShortcutCommandLineUiIds = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, PendingCustomShortcutCommandRemoval> pendingCustomShortcutCommandRemovals = new Dictionary<string, PendingCustomShortcutCommandRemoval>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, string> pendingQuickMenuItemRemovals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	private static readonly CurrencyDisplayOption[] CurrencyDisplayOptions = new CurrencyDisplayOption[18]
	{
		new CurrencyDisplayOption(1u, "金币"),
		new CurrencyDisplayOption(29u, "金碟币"),
		new CurrencyDisplayOption(21072u, "探险币"),
		new CurrencyDisplayOption(28u, "亚拉戈诗学神典石"),
		new CurrencyDisplayOption(25u, "狼印章"),
		new CurrencyDisplayOption(26807u, "双色宝石"),
		new CurrencyDisplayOption(27u, "狩猎徽章"),
		new CurrencyDisplayOption(10307u, "百战徽章"),
		new CurrencyDisplayOption(20u, "黑涡团筹备"),
		new CurrencyDisplayOption(21u, "双蛇党筹备"),
		new CurrencyDisplayOption(22u, "恒辉队筹备"),
		new CurrencyDisplayOption(26533u, "坚果"),
		new CurrencyDisplayOption(36656u, "战利品水晶"),
		new CurrencyDisplayOption(33913u, "制作紫票"),
		new CurrencyDisplayOption(33914u, "采集紫票"),
		new CurrencyDisplayOption(41784u, "制作橙票"),
		new CurrencyDisplayOption(41785u, "采集橙票"),
		new CurrencyDisplayOption(28063u, "天穹街票")
	};

	private readonly Configuration config;

	private readonly CombatStateTracker combatState;

	private readonly IDataManager dataManager;

	private readonly ITextureProvider textureProvider;

	private readonly IDalamudPluginInterface pluginInterface;

	private readonly DalamudThemeBridge dalamudThemeBridge;

	private readonly System.Action saveConfig;

	private readonly List<IExposedPlugin> installedPluginSelectionCache = new List<IExposedPlugin>();

	private readonly Dictionary<string, IExposedPlugin> installedPluginSelectionByInternalName = new Dictionary<string, IExposedPlugin>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, string> pluginListTileTextCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	private readonly HashSet<string> pluginListSelectedInternalNameCache = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private long installedPluginSelectionCacheUpdatedAtMs = long.MinValue;

	private float pluginListTileTextCacheWidth = -1f;

	private float expandedSettingsWidth;

	private bool isPickingHiddenImGuiWindow;

	private string hiddenImGuiWindowPickerStatus = string.Empty;

	private string draggingTaskBarComponentId = string.Empty;

	private string selectedTaskBarComponentSettingsId = string.Empty;

	private string selectedTaskBarComponentSettingsScope = string.Empty;

	private string selectedAuxiliaryComponentSettingsId = string.Empty;

	private string selectedAuxiliaryComponentSettingsScope = string.Empty;

	private int selectedAuxiliaryComponentSettingsBarIndex = -1;

	private ConfigPage selectedPage = ConfigPage.状态栏;

	private TaskBarPage selectedTaskBarPage;

	private int selectedAuxiliaryBarIndex;

	private static readonly JobSkillRoleGroup[] JobSkillRoleGroups = new JobSkillRoleGroup[5]
	{
		new JobSkillRoleGroup("防护职业", new global::_003C_003Ez__ReadOnlyArray<uint>(new uint[4] { 19u, 21u, 32u, 37u })),
		new JobSkillRoleGroup("治疗职业", new global::_003C_003Ez__ReadOnlyArray<uint>(new uint[4] { 24u, 28u, 33u, 40u })),
		new JobSkillRoleGroup("近战职业", new global::_003C_003Ez__ReadOnlyArray<uint>(new uint[6] { 20u, 22u, 30u, 34u, 39u, 41u })),
		new JobSkillRoleGroup("远程物理职业", new global::_003C_003Ez__ReadOnlyArray<uint>(new uint[3] { 23u, 31u, 38u })),
		new JobSkillRoleGroup("远程魔法职业", new global::_003C_003Ez__ReadOnlyArray<uint>(new uint[4] { 25u, 27u, 35u, 42u }))
	};

	private static readonly IReadOnlySet<string> HiddenActionSelectionNames = new HashSet<string>(StringComparer.Ordinal) { "狂暴", "战嚎", "Berserk", "安魂祈祷", "Requiescat", "能量抽取", "Energy Siphon" };

	public bool IsOpen { get; set; }

	private void DrawAppearancePage()
	{
		ImGui.PushStyleColor(ImGuiCol.Text, GetEffectiveTheme().Accent);
		ImGui.TextUnformatted("外观与主题");
		ImGui.PopStyleColor();
		ImGui.TextDisabled("主题会即时应用到设置窗口、任务栏、通用弹窗与套装切换器；液态玻璃会实时模糊背后的游戏画面。");
		ImGui.Dummy(new Vector2(1f, 10f));
		float x = ImGui.GetContentRegionAvail().X;
		float num = 12f;
		float x2 = Math.Max(150f, (x - num * 2f) / 3f);
		int num2 = 0;
		foreach (var (mode, palette) in ThemeCatalog.All)
		{
			if (num2 > 0 && num2 % 3 != 0)
			{
				ImGui.SameLine(0f, num);
			}
			DrawThemeCard(mode, palette, new Vector2(x2, 128f));
			num2++;
		}
		ImGui.Dummy(new Vector2(1f, 14f));
		ThemePalette effectiveTheme = GetEffectiveTheme();
		ImU8String text = new ImU8String(5, 1);
		text.AppendLiteral("当前主题：");
		text.AppendFormatted(effectiveTheme.Name);
		ImGui.TextUnformatted(text);
		ImGui.TextDisabled(effectiveTheme.Description);
		ImGui.Dummy(new Vector2(1f, 8f));
		ImGui.TextWrapped((config.ThemeMode == AllHudThemeMode.LiquidGlass) ? "液态玻璃通过 Dalamud 原生 blur-behind 实时模糊窗口背后的游戏画面，默认使用无色清透遮罩；下面的参数会即时应用。" : "选择液态玻璃主题可使用实时背景模糊、清透半透明层和胶囊控件。");
		if (config.ThemeMode == AllHudThemeMode.LiquidGlass)
		{
			DrawLiquidGlassSettings();
		}
	}

	private void DrawThemeCard(AllHudThemeMode mode, ThemePalette palette, Vector2 size)
	{
		bool flag = config.ThemeMode == mode;
		Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
		Vector2 pMax = cursorScreenPos + size;
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		ImU8String strId = new ImU8String(6, 1);
		strId.AppendLiteral("theme_");
		strId.AppendFormatted(mode);
		ImGui.PushID(strId);
		ImGui.InvisibleButton("##card", size);
		bool num = ImGui.IsItemHovered();
		if (ImGui.IsItemClicked())
		{
			config.ThemeMode = mode;
			saveConfig();
		}
		Vector4 col = (num ? palette.SurfaceAlt : palette.Surface);
		windowDrawList.AddRectFilled(cursorScreenPos, pMax, ImGui.GetColorU32(col), palette.Rounding);
		windowDrawList.AddRect(cursorScreenPos, pMax, ImGui.GetColorU32(flag ? palette.Accent : palette.Border), palette.Rounding, ImDrawFlags.None, flag ? 2f : 1f);
		Vector2 vector = cursorScreenPos + new Vector2(12f, 12f);
		Vector2 vector2 = new Vector2(pMax.X - 12f, cursorScreenPos.Y + 64f);
		windowDrawList.AddRectFilled(vector, vector2, ImGui.GetColorU32(palette.WindowBg), 7f);
		if (mode == AllHudThemeMode.LiquidGlass)
		{
			windowDrawList.AddRectFilledMultiColor(vector + new Vector2(1f), vector2 - new Vector2(1f), ImGui.GetColorU32(new Vector4(0.92f, 0.97f, 1f, 0.2f)), ImGui.GetColorU32(new Vector4(0.74f, 0.88f, 1f, 0.14f)), ImGui.GetColorU32(new Vector4(0.3f, 0.42f, 0.56f, 0.03f)), ImGui.GetColorU32(new Vector4(0.5f, 0.64f, 0.78f, 0.05f)));
			windowDrawList.AddLine(vector + new Vector2(8f, 2f), new Vector2(vector2.X - 8f, vector.Y + 2f), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.6f)), 1f);
		}
		windowDrawList.AddRectFilled(vector + new Vector2(6f), new Vector2(vector.X + 44f, vector2.Y - 6f), ImGui.GetColorU32(palette.NavBg), 5f);
		windowDrawList.AddRectFilled(new Vector2(vector.X + 50f, vector.Y + 6f), vector2 - new Vector2(6f), ImGui.GetColorU32(palette.ContentBg), 5f);
		windowDrawList.AddRectFilled(new Vector2(vector.X + 58f, vector.Y + 14f), new Vector2(vector2.X - 15f, vector.Y + 22f), ImGui.GetColorU32(palette.AccentSoft), 999f);
		windowDrawList.AddRectFilled(new Vector2(vector.X + 58f, vector.Y + 29f), new Vector2(vector2.X - 38f, vector.Y + 36f), ImGui.GetColorU32(palette.TextMuted), 999f);
		windowDrawList.AddText(cursorScreenPos + new Vector2(13f, 75f), ImGui.GetColorU32(palette.Text), palette.Name);
		windowDrawList.AddText(cursorScreenPos + new Vector2(13f, 98f), ImGui.GetColorU32(palette.TextMuted), flag ? "当前使用" : "点击切换");
		if (flag)
		{
			string text = "✓";
			Vector2 vector3 = ImGui.CalcTextSize(text);
			windowDrawList.AddText(new Vector2(pMax.X - vector3.X - 14f, cursorScreenPos.Y + 76f), ImGui.GetColorU32(palette.Accent), text);
		}
		ImGui.PopID();
	}

	private void DrawLiquidGlassSettings()
	{
		ImGui.Dummy(new Vector2(1f, 12f));
		ImGui.TextUnformatted("液态玻璃参数");
		ImGui.Separator();
		ImGui.TextDisabled("玻璃层强度越低越透明；它会联动遮罩、染色、亮度融合和颗粒，但不会把文字与图标变淡。");
		ImGui.Dummy(new Vector2(1f, 6f));
		float value = config.LiquidGlassOpacity;
		if (DrawGlassPercentSlider("玻璃层强度", "LiquidGlassOpacity", ref value, 0.1f, 1f))
		{
			config.LiquidGlassOpacity = value;
		}
		bool flag = ImGui.IsItemDeactivatedAfterEdit();
		float value2 = config.LiquidGlassBlurStrength;
		if (DrawGlassValueSlider("模糊强度", "LiquidGlassBlurStrength", ref value2, 0f, 8f, "%.2f"))
		{
			config.LiquidGlassBlurStrength = value2;
		}
		flag |= ImGui.IsItemDeactivatedAfterEdit();
		float value3 = config.LiquidGlassTintStrength;
		if (DrawGlassPercentSlider("染色强度", "LiquidGlassTintStrength", ref value3, 0f, 1f))
		{
			config.LiquidGlassTintStrength = value3;
		}
		flag |= ImGui.IsItemDeactivatedAfterEdit();
		float value4 = config.LiquidGlassBrightness;
		if (DrawGlassPercentSlider("背景亮度目标", "LiquidGlassBrightness", ref value4, 0.15f, 0.95f))
		{
			config.LiquidGlassBrightness = value4;
		}
		flag |= ImGui.IsItemDeactivatedAfterEdit();
		float value5 = config.LiquidGlassNoise;
		if (DrawGlassPercentSlider("颗粒强度", "LiquidGlassNoise", ref value5, 0f, 0.2f))
		{
			config.LiquidGlassNoise = value5;
		}
		flag |= ImGui.IsItemDeactivatedAfterEdit();
		float value6 = config.LiquidGlassRounding;
		if (DrawGlassValueSlider("圆角", "LiquidGlassRounding", ref value6, 0f, 32f, "%.0f px"))
		{
			config.LiquidGlassRounding = value6;
		}
		flag |= ImGui.IsItemDeactivatedAfterEdit();
		if (config.LiquidGlassTintStrength > 0.001f)
		{
			ImGui.AlignTextToFramePadding();
			ImGui.TextUnformatted("染色色相");
			ImGui.SameLine(0f, 10f);
			ImGui.SetNextItemWidth(Math.Clamp(ImGui.GetContentRegionAvail().X, 140f, 280f));
			Vector4 liquidGlassTintColor = config.LiquidGlassTintColor;
			Vector3 col = new Vector3(liquidGlassTintColor.X, liquidGlassTintColor.Y, liquidGlassTintColor.Z);
			if (ImGui.ColorEdit3("##LiquidGlassTintColor", ref col, ImGuiColorEditFlags.NoAlpha | ImGuiColorEditFlags.PickerHueWheel))
			{
				config.LiquidGlassTintColor = new Vector4(col, 1f);
			}
			flag |= ImGui.IsItemDeactivatedAfterEdit();
		}
		if (flag)
		{
			SaveLiquidGlassSettings();
		}
		ImGui.Dummy(new Vector2(1f, 6f));
		if (ImGui.Button("恢复清透默认值##LiquidGlassReset", new Vector2(154f, 28f)))
		{
			config.LiquidGlassOpacity = 0.62f;
			config.LiquidGlassBlurStrength = 4f;
			config.LiquidGlassTintStrength = 0f;
			config.LiquidGlassTintColor = new Vector4(0.82f, 0.9f, 1f, 1f);
			config.LiquidGlassBrightness = 0.72f;
			config.LiquidGlassNoise = 0.025f;
			config.LiquidGlassRounding = 18f;
			SaveLiquidGlassSettings();
		}
		DrawDalamudThemeSettings();
	}

	private void DrawDalamudThemeSettings()
	{
		ImGui.Dummy(new Vector2(1f, 14f));
		ImGui.TextUnformatted("卫月全局主题（实验性）");
		ImGui.Separator();
		ImGui.TextWrapped("通过反射创建独立的 AllHud Liquid Glass 卫月主题。它会影响卫月内置窗口，以及采用标准 WindowSystem 且允许背景模糊的插件窗口。");
		ImGui.TextDisabled("直接调用 ImGui.Begin、硬编码颜色或关闭背景模糊的插件可能不会跟随。卫月更新后若内部接口变化，本功能会安全停用。 ");
		ImGui.Dummy(new Vector2(1f, 6f));
		bool v = config.DalamudThemeFollowAllHud;
		if (ImGui.Checkbox("跟随 AllHud 液态玻璃参数", ref v))
		{
			if (!v)
			{
				config.CopyLiquidGlassToDalamudTheme();
			}
			config.DalamudThemeFollowAllHud = v;
			SaveAndReapplyActiveDalamudTheme();
		}
		if (ImGui.IsItemHovered())
		{
			DrawStyledTooltip("开启时使用上方 AllHud 液态玻璃参数；关闭后可单独调整卫月窗口，不影响 AllHud 自身外观。");
		}
		if (!config.DalamudThemeFollowAllHud)
		{
			DrawDalamudThemeParameterEditors();
		}
		ImU8String text = new ImU8String(7, 1);
		text.AppendLiteral("当前卫月主题：");
		text.AppendFormatted(string.IsNullOrEmpty(dalamudThemeBridge.CurrentChosenStyle) ? "未知" : dalamudThemeBridge.CurrentChosenStyle);
		ImGui.TextUnformatted(text);
		ImGui.TextDisabled(dalamudThemeBridge.Status);
		if (config.DalamudThemeRestoreArmed)
		{
			ImU8String text2 = new ImU8String(5, 1);
			text2.AppendLiteral("可还原到：");
			text2.AppendFormatted(dalamudThemeBridge.RestoreTarget);
			ImGui.TextDisabled(text2);
		}
		ImGui.Dummy(new Vector2(1f, 5f));
		if (!dalamudThemeBridge.IsAvailable)
		{
			ImGui.BeginDisabled();
		}
		if (ImGui.Button(dalamudThemeBridge.IsManagedStyleActive ? "更新并重新应用##DalamudLiquidGlassApply" : "应用到卫月主题##DalamudLiquidGlassApply", new Vector2(184f, 30f)))
		{
			dalamudThemeBridge.RequestApply();
		}
		if (!dalamudThemeBridge.IsAvailable)
		{
			ImGui.EndDisabled();
		}
		ImGui.SameLine();
		if (!dalamudThemeBridge.CanRestore)
		{
			ImGui.BeginDisabled();
		}
		if (ImGui.Button("还原卫月原主题##DalamudLiquidGlassRestore", new Vector2(154f, 30f)))
		{
			dalamudThemeBridge.RequestRestore();
		}
		if (!dalamudThemeBridge.CanRestore)
		{
			ImGui.EndDisabled();
		}
		if (!dalamudThemeBridge.IsAvailable)
		{
			ImGui.SameLine();
			if (ImGui.Button("重新检测##DalamudLiquidGlassRetry", new Vector2(92f, 30f)))
			{
				dalamudThemeBridge.RefreshState();
			}
		}
	}

	private void DrawDalamudThemeParameterEditors()
	{
		ImGui.Dummy(new Vector2(1f, 6f));
		ImGui.TextDisabled("直接编辑卫月 StyleModelV1；松开滑杆后，当前受管主题会自动更新。背景遮罩越低越清透。");
		bool flag = false;
		float value = config.DalamudThemeBackgroundOpacity;
		if (DrawGlassPercentSlider("背景遮罩", "DalamudThemeBackgroundOpacity", ref value, 0f, 1f))
		{
			config.DalamudThemeBackgroundOpacity = value;
		}
		flag |= ImGui.IsItemDeactivatedAfterEdit();
		float value2 = config.DalamudThemeBlurStrength;
		if (DrawGlassValueSlider("模糊强度", "DalamudThemeBlurStrength", ref value2, 0f, 14f, "%.2f"))
		{
			config.DalamudThemeBlurStrength = value2;
		}
		flag |= ImGui.IsItemDeactivatedAfterEdit();
		float value3 = config.DalamudThemeTintStrength;
		if (DrawGlassPercentSlider("染色强度", "DalamudThemeTintStrength", ref value3, 0f, 1f))
		{
			config.DalamudThemeTintStrength = value3;
		}
		flag |= ImGui.IsItemDeactivatedAfterEdit();
		if (config.DalamudThemeTintStrength > 0.001f)
		{
			ImGui.AlignTextToFramePadding();
			ImGui.TextUnformatted("染色色相");
			ImGui.SameLine(0f, 10f);
			ImGui.SetNextItemWidth(Math.Clamp(ImGui.GetContentRegionAvail().X, 140f, 280f));
			Vector4 dalamudThemeTintColor = config.DalamudThemeTintColor;
			Vector3 col = new Vector3(dalamudThemeTintColor.X, dalamudThemeTintColor.Y, dalamudThemeTintColor.Z);
			if (ImGui.ColorEdit3("##DalamudThemeTintColor", ref col, ImGuiColorEditFlags.NoAlpha | ImGuiColorEditFlags.PickerHueWheel))
			{
				config.DalamudThemeTintColor = new Vector4(col, 1f);
			}
			flag |= ImGui.IsItemDeactivatedAfterEdit();
		}
		float value4 = config.DalamudThemeBrightness;
		if (DrawGlassPercentSlider("亮度目标", "DalamudThemeBrightness", ref value4, 0f, 1f))
		{
			config.DalamudThemeBrightness = value4;
		}
		flag |= ImGui.IsItemDeactivatedAfterEdit();
		float value5 = config.DalamudThemeLuminosityStrength;
		if (DrawGlassPercentSlider("亮度融合", "DalamudThemeLuminosityStrength", ref value5, 0f, 1f))
		{
			config.DalamudThemeLuminosityStrength = value5;
		}
		flag |= ImGui.IsItemDeactivatedAfterEdit();
		float value6 = config.DalamudThemeRounding;
		if (DrawGlassValueSlider("圆角", "DalamudThemeRounding", ref value6, 0f, 32f, "%.0f px"))
		{
			config.DalamudThemeRounding = value6;
		}
		if (flag | ImGui.IsItemDeactivatedAfterEdit())
		{
			SaveAndReapplyActiveDalamudTheme();
		}
		ImGui.Dummy(new Vector2(1f, 4f));
		if (ImGui.Button("从 AllHud 复制参数##DalamudThemeCopy", new Vector2(154f, 28f)))
		{
			config.CopyLiquidGlassToDalamudTheme();
			SaveAndReapplyActiveDalamudTheme();
		}
	}

	private void SaveAndReapplyActiveDalamudTheme()
	{
		saveConfig();
		if (dalamudThemeBridge.IsAvailable && dalamudThemeBridge.IsManagedStyleActive)
		{
			dalamudThemeBridge.RequestApply();
		}
	}

	private void SaveLiquidGlassSettings()
	{
		saveConfig();
		if (config.DalamudThemeFollowAllHud && dalamudThemeBridge.IsAvailable && dalamudThemeBridge.IsManagedStyleActive)
		{
			dalamudThemeBridge.RequestApply();
		}
	}

	private static bool DrawGlassPercentSlider(string label, string id, ref float value, float min, float max)
	{
		float v = value * 100f;
		ImGui.AlignTextToFramePadding();
		ImGui.TextUnformatted(label);
		ImGui.SameLine(0f, 10f);
		ImGui.SetNextItemWidth(Math.Clamp(ImGui.GetContentRegionAvail().X, 140f, 320f));
		ImU8String label2 = new ImU8String(2, 1);
		label2.AppendLiteral("##");
		label2.AppendFormatted(id);
		if (!ImGui.SliderFloat(label2, ref v, min * 100f, max * 100f, "%.0f%%"))
		{
			return false;
		}
		value = Math.Clamp(v / 100f, min, max);
		return true;
	}

	private static bool DrawGlassValueSlider(string label, string id, ref float value, float min, float max, string format)
	{
		ImGui.AlignTextToFramePadding();
		ImGui.TextUnformatted(label);
		ImGui.SameLine(0f, 10f);
		ImGui.SetNextItemWidth(Math.Clamp(ImGui.GetContentRegionAvail().X, 140f, 320f));
		ImU8String label2 = new ImU8String(2, 1);
		label2.AppendLiteral("##");
		label2.AppendFormatted(id);
		return ImGui.SliderFloat(label2, ref value, min, max, format);
	}

	private List<CurrencyDisplayOption> GetCurrencySettingsOptions()
	{
		List<CurrencyDisplayOption> list = new List<CurrencyDisplayOption>(CurrencyDisplayOptions);
		HashSet<uint> hashSet = list.Select((CurrencyDisplayOption option) => option.ItemId).ToHashSet();
		try
		{
			foreach (TomestonesItem item in dataManager.GetExcelSheet<TomestonesItem>())
			{
				uint rowId = item.Tomestones.RowId;
				if (rowId - 2 <= 1)
				{
					Item value = item.Item.Value;
					string text = value.Name.ExtractText().Replace("\u00ad", string.Empty, StringComparison.Ordinal).Trim();
					if (hashSet.Add(value.RowId))
					{
						list.Add(new CurrencyDisplayOption(value.RowId, string.IsNullOrWhiteSpace(text) ? $"神典石 {value.RowId}" : text));
					}
				}
			}
		}
		catch
		{
		}
		string[] array = (config.TaskBarCurrencyCustomItemIds ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries);
		for (int num = 0; num < array.Length; num++)
		{
			if (!uint.TryParse(array[num].Trim(), out var result) || result == 0 || !hashSet.Add(result))
			{
				continue;
			}
			string text2 = string.Empty;
			try
			{
				if (dataManager.GetExcelSheet<Item>().TryGetRow(result, out var row))
				{
					text2 = row.Name.ExtractText().Replace("\u00ad", string.Empty, StringComparison.Ordinal).Trim();
				}
			}
			catch
			{
			}
			list.Add(new CurrencyDisplayOption(result, string.IsNullOrWhiteSpace(text2) ? $"物品 {result}" : text2));
		}
		return list;
	}

	public ConfigWindow(Configuration config, CombatStateTracker combatState, IDataManager dataManager, ITextureProvider textureProvider, IDalamudPluginInterface pluginInterface, DalamudThemeBridge dalamudThemeBridge, System.Action saveConfig)
	{
		this.config = config;
		this.combatState = combatState;
		this.dataManager = dataManager;
		this.textureProvider = textureProvider;
		this.pluginInterface = pluginInterface;
		this.dalamudThemeBridge = dalamudThemeBridge;
		this.saveConfig = saveConfig;
	}

	private void DrawStyledTooltip(string text)
	{
		DrawStyledTooltip(delegate
		{
			ImGui.TextUnformatted(text);
		});
	}

	private void DrawStyledTooltip(System.Action drawContent)
	{
		ThemePalette effectiveTheme = GetEffectiveTheme();
		ImGui.PushStyleColor(ImGuiCol.PopupBg, effectiveTheme.TooltipBg);
		ImGui.PushStyleColor(ImGuiCol.Text, effectiveTheme.TooltipText);
		ImGui.PushStyleColor(ImGuiCol.Border, WithAlpha(effectiveTheme.Border, 0.78f));
		ImGui.PushStyleColor(ImGuiCol.Separator, WithAlpha(effectiveTheme.Border, 0.48f));
		ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(11f, 8f));
		ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 7f);
		ImGui.PushStyleVar(ImGuiStyleVar.PopupBorderSize, 1f);
		ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6f, 4f));
		ImGui.BeginTooltip();
		drawContent();
		ImGui.EndTooltip();
		ImGui.PopStyleVar(4);
		ImGui.PopStyleColor(4);
	}

	public void Draw()
	{
		if (!IsOpen)
		{
			return;
		}
		ThemePalette effectiveTheme = GetEffectiveTheme();
		PushCustomStyle(effectiveTheme, ThemeDrawing.IsLiquidGlass(config.ThemeMode));
		ImGui.SetNextWindowSize(new Vector2(760f, 620f), ImGuiCond.FirstUseEver);
		bool open = IsOpen;
		if (!ImGui.Begin("AllHud 设置", ref open, ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse))
		{
			IsOpen = open;
			PopCustomStyle();
			ImGui.End();
			return;
		}
		IsOpen = open;
		if (ThemeDrawing.IsLiquidGlass(config.ThemeMode))
		{
			ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
			Vector2 windowPos = ImGui.GetWindowPos();
			Vector2 max = windowPos + ImGui.GetWindowSize();
			ThemeDrawing.PrependLiquidGlassBlur(windowDrawList, windowPos, max, ThemeDrawing.GetLiquidGlassRounding(config), config);
		}
		DrawCustomTitleBar();
		float x = 156f;
		ImGui.PushStyleColor(ImGuiCol.ChildBg, effectiveTheme.NavBg);
		ImGui.PushStyleColor(ImGuiCol.Border, effectiveTheme.Border);
		ImGui.BeginChild("config_nav", new Vector2(x, -1f), border: true);
		if (ThemeDrawing.IsLiquidGlass(config.ThemeMode))
		{
			ThemeDrawing.DrawLiquidGlassPanel(ImGui.GetWindowDrawList(), ImGui.GetWindowPos(), ImGui.GetWindowPos() + ImGui.GetWindowSize(), ThemeDrawing.GetLiquidGlassRounding(config), config, 0.72f, drawShadow: false);
		}
		DrawNavSection();
		ImGui.EndChild();
		ImGui.PopStyleColor(2);
		ImGui.SameLine();
		ImGui.PushStyleColor(ImGuiCol.ChildBg, effectiveTheme.ContentBg);
		ImGui.PushStyleColor(ImGuiCol.Border, effectiveTheme.Border);
		ImGui.BeginChild("config_content", new Vector2(0f, -1f), border: true);
		if (ThemeDrawing.IsLiquidGlass(config.ThemeMode))
		{
			ThemeDrawing.DrawLiquidGlassPanel(ImGui.GetWindowDrawList(), ImGui.GetWindowPos(), ImGui.GetWindowPos() + ImGui.GetWindowSize(), ThemeDrawing.GetLiquidGlassRounding(config), config, 0.68f, drawShadow: false);
		}
		ImGui.Dummy(new Vector2(1f, 4f));
		DrawSelectedPage();
		ImGui.EndChild();
		ImGui.PopStyleColor(2);
		ImGui.End();
		PopCustomStyle();
		DrawHiddenImGuiWindowPickerOverlay();
	}

	private void DrawHiddenImGuiWindowPickerOverlay()
	{
		if (!isPickingHiddenImGuiWindow)
		{
			return;
		}
		ImGuiViewportPtr mainViewport = ImGui.GetMainViewport();
		ImGui.SetNextWindowPos(mainViewport.Pos, ImGuiCond.Always);
		ImGui.SetNextWindowSize(mainViewport.Size, ImGuiCond.Always);
		ImGui.SetNextWindowBgAlpha(0f);
		ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings;
		if (!ImGui.Begin("AllHud 隐藏窗口拾取遮罩", flags))
		{
			ImGui.End();
			return;
		}
		PickedImGuiWindow? pickedImGuiWindow = TryGetHoveredImGuiWindow();
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		Vector2 mousePos = ImGui.GetMousePos();
		string text = ((!pickedImGuiWindow.HasValue) ? "移动到要隐藏的悬浮窗上，左键确认，右键或 Esc 取消" : ("左键隐藏: " + pickedImGuiWindow.GetValueOrDefault().Name));
		Vector2 vector = ImGui.CalcTextSize(text) + new Vector2(16f, 10f);
		Vector2 vector2 = mousePos + new Vector2(16f, 18f);
		Vector2 pMax = vector2 + vector;
		ThemePalette effectiveTheme = GetEffectiveTheme();
		windowDrawList.AddRectFilled(vector2, pMax, ImGui.GetColorU32(effectiveTheme.TooltipBg), 6f);
		windowDrawList.AddRect(vector2, pMax, ImGui.GetColorU32(WithAlpha(effectiveTheme.Border, 0.84f)), 6f);
		windowDrawList.AddText(vector2 + new Vector2(8f, 5f), ImGui.GetColorU32(effectiveTheme.TooltipText), text);
		if (ImGui.IsKeyPressed(ImGuiKey.Escape) || ImGui.IsMouseClicked(ImGuiMouseButton.Right))
		{
			isPickingHiddenImGuiWindow = false;
			hiddenImGuiWindowPickerStatus = "已取消添加。";
		}
		else if (pickedImGuiWindow.HasValue && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
		{
			AddHiddenImGuiWindow(pickedImGuiWindow.Value.Name);
		}
		ImGui.End();
	}

	private ThemePalette GetEffectiveTheme()
	{
		ThemePalette themePalette = ThemeCatalog.Get(config.ThemeMode);
		if (!ThemeDrawing.IsLiquidGlass(config.ThemeMode))
		{
			return themePalette;
		}
		return themePalette with
		{
			WindowBg = Glass(themePalette.WindowBg),
			NavBg = Glass(themePalette.NavBg),
			ContentBg = Glass(themePalette.ContentBg),
			Surface = Glass(themePalette.Surface),
			SurfaceAlt = Glass(themePalette.SurfaceAlt),
			FrameBg = Glass(themePalette.FrameBg),
			FrameHovered = Glass(themePalette.FrameHovered),
			FrameActive = Glass(themePalette.FrameActive),
			Button = Glass(themePalette.Button),
			ButtonHovered = Glass(themePalette.ButtonHovered),
			ButtonActive = Glass(themePalette.ButtonActive),
			Header = Glass(themePalette.Header),
			HeaderHovered = Glass(themePalette.HeaderHovered),
			HeaderActive = Glass(themePalette.HeaderActive),
			ScrollbarBg = Glass(themePalette.ScrollbarBg),
			TitleBar = Glass(themePalette.TitleBar),
			TitlePill = Glass(themePalette.TitlePill),
			PopupBg = Glass(themePalette.PopupBg)
		};
		Vector4 Glass(Vector4 color)
		{
			return ThemeDrawing.ApplyLiquidGlassOpacity(config, color);
		}
	}

	private void PushCustomStyle(ThemePalette theme, bool liquidGlass)
	{
		ImGui.PushStyleColor(ImGuiCol.WindowBg, theme.WindowBg);
		ImGui.PushStyleColor(ImGuiCol.ChildBg, theme.Surface);
		ImGui.PushStyleColor(ImGuiCol.FrameBg, theme.FrameBg);
		ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, theme.FrameHovered);
		ImGui.PushStyleColor(ImGuiCol.FrameBgActive, theme.FrameActive);
		ImGui.PushStyleColor(ImGuiCol.PopupBg, theme.PopupBg);
		ImGui.PushStyleColor(ImGuiCol.Button, theme.Button);
		ImGui.PushStyleColor(ImGuiCol.ButtonHovered, theme.ButtonHovered);
		ImGui.PushStyleColor(ImGuiCol.ButtonActive, theme.ButtonActive);
		ImGui.PushStyleColor(ImGuiCol.Header, theme.Header);
		ImGui.PushStyleColor(ImGuiCol.HeaderHovered, theme.HeaderHovered);
		ImGui.PushStyleColor(ImGuiCol.HeaderActive, theme.HeaderActive);
		ImGui.PushStyleColor(ImGuiCol.Tab, theme.Surface);
		ImGui.PushStyleColor(ImGuiCol.TabHovered, theme.HeaderHovered);
		ImGui.PushStyleColor(ImGuiCol.TabActive, theme.Header);
		ImGui.PushStyleColor(ImGuiCol.Separator, theme.Border);
		ImGui.PushStyleColor(ImGuiCol.Border, theme.Border);
		ImGui.PushStyleColor(ImGuiCol.Text, theme.Text);
		ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, theme.ScrollbarBg);
		ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, theme.ScrollbarGrab);
		ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, theme.ScrollbarGrabHovered);
		ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, theme.ScrollbarGrabActive);
		ImGui.PushStyleColor(ImGuiCol.SliderGrab, theme.Accent);
		ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, theme.AccentSoft);
		ImGui.PushStyleColor(ImGuiCol.ResizeGrip, new Vector4(0f, 0f, 0f, 0f));
		ImGui.PushStyleColor(ImGuiCol.ResizeGripHovered, new Vector4(0f, 0f, 0f, 0f));
		ImGui.PushStyleColor(ImGuiCol.ResizeGripActive, new Vector4(0f, 0f, 0f, 0f));
		ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, liquidGlass ? 12f : 4f);
		ImGui.PushStyleVar(ImGuiStyleVar.TabRounding, liquidGlass ? 12f : 6f);
		ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, liquidGlass ? config.LiquidGlassRounding : theme.Rounding);
		ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, 10f);
		ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, liquidGlass ? 999f : 4f);
		ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, liquidGlass ? 999f : 4f);
		ImGui.PushStyleVar(ImGuiStyleVar.GrabMinSize, 10f);
		ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
		ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, liquidGlass ? 16f : 8f);
		ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1f);
	}

	private static void PopCustomStyle()
	{
		ImGui.PopStyleVar(10);
		ImGui.PopStyleColor(27);
	}

	private void DrawCustomTitleBar()
	{
		ThemePalette effectiveTheme = GetEffectiveTheme();
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		float x = ImGui.GetContentRegionAvail().X;
		float num = ImGui.GetTextLineHeight() + 18f;
		Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
		Vector2 vector = cursorScreenPos + new Vector2(x, num);
		float y = cursorScreenPos.Y + 9f;
		windowDrawList.AddRectFilled(cursorScreenPos, vector, ImGui.GetColorU32(effectiveTheme.TitleBar), ThemeDrawing.IsLiquidGlass(config.ThemeMode) ? ThemeDrawing.GetLiquidGlassRounding(config) : effectiveTheme.Rounding);
		windowDrawList.AddRect(cursorScreenPos, vector, ImGui.GetColorU32(effectiveTheme.Border), ThemeDrawing.IsLiquidGlass(config.ThemeMode) ? ThemeDrawing.GetLiquidGlassRounding(config) : effectiveTheme.Rounding, ImDrawFlags.None, 1f);
		windowDrawList.AddLine(new Vector2(cursorScreenPos.X + 10f, vector.Y - 1f), new Vector2(vector.X - 10f, vector.Y - 1f), ImGui.GetColorU32(WithAlpha(effectiveTheme.Accent, 0.26f)), 1f);
		if (ThemeDrawing.IsLiquidGlass(config.ThemeMode))
		{
			ThemeDrawing.DrawLiquidGlassPanel(windowDrawList, cursorScreenPos, vector, ThemeDrawing.GetLiquidGlassRounding(config), config, 0.92f, drawShadow: false);
		}
		InlineArray6<string> buffer = default(InlineArray6<string>);
		buffer[0] = "A";
		buffer[1] = "l";
		buffer[2] = "l";
		buffer[3] = "H";
		buffer[4] = "u";
		buffer[5] = "d";
		ReadOnlySpan<string> readOnlySpan = buffer;
		float num2 = 0f;
		ReadOnlySpan<string> readOnlySpan2 = readOnlySpan;
		for (int i = 0; i < readOnlySpan2.Length; i++)
		{
			string text = readOnlySpan2[i];
			num2 += ImGui.CalcTextSize(text).X;
		}
		string text2 = " 设置";
		float num3 = num2 + ImGui.CalcTextSize(text2).X;
		float num4 = (cursorScreenPos.X + vector.X) * 0.5f;
		Vector2 vector2 = new Vector2(num4 - num3 * 0.5f, y);
		Vector2 vector3 = vector2 - new Vector2(12f, 5f);
		Vector2 vector4 = vector2 + new Vector2(num3 + 12f, ImGui.GetTextLineHeight() + 5f);
		windowDrawList.AddRectFilled(vector3, vector4, ImGui.GetColorU32(effectiveTheme.TitlePill), 999f);
		windowDrawList.AddRectFilled(vector3 + new Vector2(8f, vector4.Y - vector3.Y - 3f), vector4 - new Vector2(8f, 1.5f), ImGui.GetColorU32(WithAlpha(effectiveTheme.Accent, 0.18f)), 999f);
		windowDrawList.AddRect(vector3, vector4, ImGui.GetColorU32(WithAlpha(effectiveTheme.Accent, 0.34f)), 999f, ImDrawFlags.None, 1f);
		float num5 = vector2.X;
		for (int j = 0; j < readOnlySpan.Length; j++)
		{
			string text3 = readOnlySpan[j];
			Vector2 vector5 = new Vector2(num5, vector2.Y);
			windowDrawList.AddText(vector5 + new Vector2(0.8f, 0.8f), ImGui.GetColorU32(effectiveTheme.TextShadow), text3);
			windowDrawList.AddText(vector5, ImGui.GetColorU32(effectiveTheme.Text), text3);
			num5 += ImGui.CalcTextSize(text3).X;
		}
		windowDrawList.AddText(new Vector2(num5, vector2.Y), ImGui.GetColorU32(effectiveTheme.Text), text2);
		Vector2 vector6 = new Vector2(vector.X - 26f - 8f, cursorScreenPos.Y + (num - 26f) * 0.5f);
		Vector2 vector7 = vector6 + new Vector2(26f, 26f);
		Vector2 vector8 = (vector6 + vector7) * 0.5f;
		bool flag = ImGui.IsMouseHoveringRect(vector6, vector7);
		bool flag2 = ThemeDrawing.IsLiquidGlass(config.ThemeMode);
		uint colorU = ImGui.GetColorU32(flag ? effectiveTheme.ButtonHovered : effectiveTheme.Button);
		windowDrawList.AddRectFilled(vector6, vector7, colorU, 6f);
		windowDrawList.AddRect(vector6, vector7, ImGui.GetColorU32(flag ? effectiveTheme.Accent : effectiveTheme.Border), flag2 ? 999f : effectiveTheme.Rounding, ImDrawFlags.None, 1f);
		uint colorU2 = ImGui.GetColorU32(flag ? effectiveTheme.Text : effectiveTheme.TextMuted);
		windowDrawList.AddLine(vector8 - new Vector2(5f, 5f), vector8 + new Vector2(5f, 5f), colorU2, 1.55f);
		windowDrawList.AddLine(vector8 + new Vector2(5f, -5f), vector8 - new Vector2(5f, -5f), colorU2, 1.55f);
		if (flag && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
		{
			IsOpen = false;
		}
		ImGui.SetCursorScreenPos(new Vector2(cursorScreenPos.X, vector.Y));
		ImGui.Dummy(new Vector2(1f, 6f));
	}

	private void DrawNavSection()
	{
		ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X, 8f));
		ImGui.Indent(4f);
		DrawNavButton(ConfigPage.外观, "外观与主题");
		DrawNavButton(ConfigPage.状态栏, "状态栏");
		DrawNavButton(ConfigPage.目标情报, "目标情报");
		DrawNavButton(ConfigPage.队伍信息, "队伍信息");
		DrawNavButton(ConfigPage.任务栏, "任务栏");
		DrawNavButton(ConfigPage.技能, "独立监控");
		DrawNavButton(ConfigPage.调试, "调试");
		ImGui.Unindent(4f);
	}

	private void DrawNavButton(ConfigPage page, string label)
	{
		ThemePalette effectiveTheme = GetEffectiveTheme();
		bool flag = selectedPage == page;
		Vector4 color = (ThemeDrawing.IsLiquidGlass(config.ThemeMode) ? effectiveTheme.Accent : GetPageAccentColor(page));
		float x = ImGui.GetContentRegionAvail().X - 4f;
		Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		Vector2 vector = cursorScreenPos;
		Vector2 vector2 = cursorScreenPos + new Vector2(x, 38f);
		if (flag)
		{
			float rounding = (ThemeDrawing.IsLiquidGlass(config.ThemeMode) ? 14f : 7f);
			windowDrawList.AddRectFilled(vector, vector2, ImGui.GetColorU32(effectiveTheme.SurfaceAlt), rounding);
			windowDrawList.AddRect(vector, vector2, ImGui.GetColorU32(WithAlpha(effectiveTheme.Border, 0.62f)), rounding);
			windowDrawList.AddRectFilled(vector + new Vector2(0f, 8f), vector + new Vector2(3f, 30f), ImGui.GetColorU32(WithAlpha(color, 0.95f)), 1.5f);
		}
		if (ImGui.IsMouseHoveringRect(vector, vector2) && !flag)
		{
			windowDrawList.AddRectFilled(vector, vector2, ImGui.GetColorU32(WithAlpha(effectiveTheme.SurfaceAlt, 0.72f)), ThemeDrawing.IsLiquidGlass(config.ThemeMode) ? 14f : 6f);
		}
		Vector4 col = (flag ? WithAlpha(color, 1f) : effectiveTheme.TextMuted);
		windowDrawList.AddText(vector + new Vector2(14f, (38f - ImGui.GetTextLineHeight()) * 0.5f), ImGui.GetColorU32(col), label);
		ImGui.SetCursorPos(ImGui.GetCursorPos());
		ImU8String strId = new ImU8String(4, 1);
		strId.AppendLiteral("nav_");
		strId.AppendFormatted(page);
		ImGui.InvisibleButton(strId, new Vector2(x, 38f));
		if (ImGui.IsItemClicked())
		{
			selectedPage = page;
		}
		ImGui.Dummy(new Vector2(x, 0f));
	}

	private Vector4 GetPageAccentColor(ConfigPage page)
	{
		return GetEffectiveTheme().Accent;
	}

	private Vector4 GetSectionTitleColor(string title)
	{
		return GetEffectiveTheme().Accent;
	}

	private static Vector4 WithAlpha(Vector4 color, float alpha)
	{
		return new Vector4(color.X, color.Y, color.Z, alpha);
	}

	private void DrawSelectedPage()
	{
		switch (selectedPage)
		{
		case ConfigPage.外观:
			DrawAppearancePage();
			break;
		case ConfigPage.目标情报:
			DrawTargetInfoPage();
			break;
		case ConfigPage.状态栏:
			DrawStatusBarPage();
			break;
		case ConfigPage.队伍信息:
			DrawPartyInfoPage();
			break;
		case ConfigPage.任务栏:
			DrawTaskBarPage();
			break;
		case ConfigPage.技能:
			DrawSkillsPage();
			break;
		case ConfigPage.调试:
			DrawDebugPage();
			break;
		}
	}

	private void DrawLimitBreakPositionSelector()
	{
		int currentValue = Math.Clamp(config.PartyLimitBreakBarPosition, 0, 1);
		DrawSegmentedSelector("LB 位置", "limit_break_position", currentValue, delegate(int value)
		{
			config.PartyLimitBreakBarPosition = value;
		}, ("队伍顶部", 0), ("队伍底部", 1));
	}

	private static byte[] CreateUtf8Buffer(string value, int length)
	{
		byte[] array = new byte[length];
		byte[] bytes = Encoding.UTF8.GetBytes(value);
		Array.Copy(bytes, array, Math.Min(bytes.Length, array.Length - 1));
		return array;
	}

	private static string ReadUtf8Buffer(byte[] buffer)
	{
		int num = Array.IndexOf(buffer, (byte)0);
		if (num < 0)
		{
			num = buffer.Length;
		}
		return Encoding.UTF8.GetString(buffer, 0, num).Trim();
	}

	private void DrawCheckbox(string label, string id, bool value, Action<bool> setter, float? rowHeightOverride = null)
	{
		ThemePalette effectiveTheme = GetEffectiveTheme();
		float frameHeight = ImGui.GetFrameHeight();
		float num = MathF.Round(ImGui.GetFontSize());
		Vector2 vector = ImGui.CalcTextSize(label);
		float num2 = rowHeightOverride ?? MathF.Max(frameHeight, vector.Y);
		Vector2 size = new Vector2(num + 3f + vector.X, num2);
		Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
		Vector2 vector2 = cursorScreenPos + new Vector2(0f, (num2 - num) * 0.5f);
		Vector2 pMax = vector2 + new Vector2(num);
		Vector2 pos = cursorScreenPos + new Vector2(num + 3f, (num2 - vector.Y) * 0.5f + 1f);
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		ImU8String strId = new ImU8String(2, 2);
		strId.AppendFormatted(label);
		strId.AppendLiteral("##");
		strId.AppendFormatted(id);
		ImGui.InvisibleButton(strId, size);
		bool flag = ImGui.IsItemHovered();
		bool flag2 = ImGui.IsItemActive();
		if (ImGui.IsItemClicked())
		{
			setter(!value);
			saveConfig();
		}
		Vector4 col = WithAlpha(flag ? effectiveTheme.Accent : effectiveTheme.Border, flag ? 0.92f : 0.82f);
		Vector4 col2 = (value ? WithAlpha(effectiveTheme.Accent, flag2 ? 0.92f : 0.82f) : (flag ? WithAlpha(effectiveTheme.SurfaceAlt, 0.92f) : WithAlpha(effectiveTheme.Surface, 0.72f)));
		windowDrawList.AddRectFilled(vector2, pMax, ImGui.GetColorU32(col2), 4f);
		windowDrawList.AddRect(vector2, pMax, ImGui.GetColorU32(col), 4f, ImDrawFlags.None, 1.2f);
		if (value)
		{
			uint colorU = ImGui.GetColorU32(effectiveTheme.TooltipText);
			windowDrawList.AddLine(vector2 + new Vector2(4f, 8.5f), vector2 + new Vector2(7f, 11.5f), colorU, 1.8f);
			windowDrawList.AddLine(vector2 + new Vector2(7f, 11.5f), vector2 + new Vector2(12.5f, 5f), colorU, 1.8f);
		}
		windowDrawList.AddText(ImGui.GetFont(), ImGui.GetFontSize(), pos, ImGui.GetColorU32(effectiveTheme.Text), label);
	}

	private void DrawSegmentedSelector(string label, string id, int currentValue, Action<int> setter, params (string Label, int Value)[] options)
	{
		ThemePalette effectiveTheme = GetEffectiveTheme();
		Vector4 selectorAccentColor = GetSelectorAccentColor(label);
		float num = ImGui.CalcTextSize(label).X + 12f;
		float num2 = Math.Clamp(options.Max(((string Label, int Value) option) => ImGui.CalcTextSize(option.Label).X + 24f), 58f, 92f);
		float num3 = 5f;
		float num4 = num2 * (float)options.Length + num3 * (float)Math.Max(0, options.Length - 1);
		float num5 = Math.Max(80f, ImGui.GetContentRegionAvail().X - 28f);
		float num6 = ImGui.GetCursorPosX() + 28f;
		bool flag = num + num4 <= num5;
		ImGui.SetCursorPosX(num6);
		ImGui.AlignTextToFramePadding();
		ImGui.PushStyleColor(ImGuiCol.Text, effectiveTheme.TextMuted);
		ImGui.TextUnformatted(label);
		ImGui.PopStyleColor();
		if (flag)
		{
			ImGui.SameLine(num6 + num);
		}
		else
		{
			ImGui.SetCursorPosX(num6);
		}
		for (int num7 = 0; num7 < options.Length; num7++)
		{
			(string, int) tuple = options[num7];
			bool flag2 = currentValue == tuple.Item2;
			Vector4 col = (flag2 ? effectiveTheme.TooltipText : effectiveTheme.TextMuted);
			Vector4 vector = (flag2 ? WithAlpha(selectorAccentColor, 0.72f) : WithAlpha(effectiveTheme.Surface, 0.88f));
			Vector4 vector2 = (flag2 ? WithAlpha(selectorAccentColor, 0.88f) : WithAlpha(selectorAccentColor, 0.24f));
			Vector4 vector3 = (flag2 ? WithAlpha(selectorAccentColor, 0.95f) : WithAlpha(selectorAccentColor, 0.34f));
			Vector2 vector4 = new Vector2(num2, 24f);
			Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
			Vector2 vector5 = cursorScreenPos + vector4;
			ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
			ImU8String strId = new ImU8String(3, 3);
			strId.AppendFormatted(tuple.Item1);
			strId.AppendLiteral("##");
			strId.AppendFormatted(id);
			strId.AppendLiteral("_");
			strId.AppendFormatted(tuple.Item2);
			ImGui.InvisibleButton(strId, vector4);
			bool flag3 = ImGui.IsItemHovered();
			bool num8 = ImGui.IsItemActive();
			if (ImGui.IsItemClicked() && !flag2)
			{
				setter(tuple.Item2);
				saveConfig();
			}
			Vector4 col2 = (num8 ? vector3 : (flag3 ? vector2 : vector));
			Vector4 col3 = WithAlpha(selectorAccentColor, flag2 ? 0.86f : (flag3 ? 0.56f : 0.34f));
			windowDrawList.AddRectFilled(cursorScreenPos + new Vector2(1f, 1f), vector5 - new Vector2(1f, 1f), ImGui.GetColorU32(col2), 4f);
			windowDrawList.AddRect(cursorScreenPos, vector5, ImGui.GetColorU32(col3), 5f, ImDrawFlags.None, flag2 ? 1.4f : 1f);
			if (flag2)
			{
				windowDrawList.AddRect(cursorScreenPos + new Vector2(2f, 2f), vector5 - new Vector2(2f, 2f), ImGui.GetColorU32(WithAlpha(effectiveTheme.Text, 0.16f)), 3f, ImDrawFlags.None, 1f);
			}
			Vector2 vector6 = ImGui.CalcTextSize(tuple.Item1);
			Vector2 pos = cursorScreenPos + (vector4 - vector6) * 0.5f;
			windowDrawList.AddText(pos, ImGui.GetColorU32(col), tuple.Item1);
			if (num7 < options.Length - 1)
			{
				ImGui.SameLine(0f, num3);
			}
		}
	}

	private void DrawInlineSegmentedSelector(string label, string id, int currentValue, Action<int> setter, params (string Label, int Value)[] options)
	{
		ThemePalette effectiveTheme = GetEffectiveTheme();
		Vector4 selectorAccentColor = GetSelectorAccentColor(label);
		float x = Math.Clamp(options.Max(((string Label, int Value) option) => ImGui.CalcTextSize(option.Label).X + 20f), 50f, 78f);
		if (!string.IsNullOrWhiteSpace(label))
		{
			ImGui.AlignTextToFramePadding();
			ImGui.TextUnformatted(label);
			ImGui.SameLine(0f, 6f);
		}
		for (int num = 0; num < options.Length; num++)
		{
			if (num > 0)
			{
				ImGui.SameLine(0f, 5f);
			}
			(string, int) tuple = options[num];
			bool flag = currentValue == tuple.Item2;
			Vector4 col = (flag ? effectiveTheme.TooltipText : effectiveTheme.TextMuted);
			Vector4 vector = (flag ? WithAlpha(selectorAccentColor, 0.72f) : WithAlpha(effectiveTheme.Surface, 0.88f));
			Vector4 vector2 = (flag ? WithAlpha(selectorAccentColor, 0.88f) : WithAlpha(selectorAccentColor, 0.24f));
			Vector4 vector3 = (flag ? WithAlpha(selectorAccentColor, 0.95f) : WithAlpha(selectorAccentColor, 0.34f));
			Vector2 vector4 = new Vector2(x, 24f);
			Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
			Vector2 vector5 = cursorScreenPos + vector4;
			ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
			ImU8String strId = new ImU8String(3, 3);
			strId.AppendFormatted(tuple.Item1);
			strId.AppendLiteral("##");
			strId.AppendFormatted(id);
			strId.AppendLiteral("_");
			strId.AppendFormatted(tuple.Item2);
			ImGui.InvisibleButton(strId, vector4);
			bool flag2 = ImGui.IsItemHovered();
			bool num2 = ImGui.IsItemActive();
			if (ImGui.IsItemClicked() && !flag)
			{
				setter(tuple.Item2);
				saveConfig();
			}
			Vector4 col2 = (num2 ? vector3 : (flag2 ? vector2 : vector));
			Vector4 col3 = WithAlpha(selectorAccentColor, flag ? 0.86f : (flag2 ? 0.56f : 0.34f));
			windowDrawList.AddRectFilled(cursorScreenPos + new Vector2(1f, 1f), vector5 - new Vector2(1f, 1f), ImGui.GetColorU32(col2), 5f);
			windowDrawList.AddRect(cursorScreenPos, vector5, ImGui.GetColorU32(col3), 5f, ImDrawFlags.None, flag ? 1.4f : 1f);
			Vector2 vector6 = ImGui.CalcTextSize(tuple.Item1);
			windowDrawList.AddText(cursorScreenPos + (vector4 - vector6) * 0.5f, ImGui.GetColorU32(col), tuple.Item1);
		}
	}

	private Vector4 GetSelectorAccentColor(string label)
	{
		return GetEffectiveTheme().Accent;
	}

	private static void DrawTargetInfoSubsection(string title)
	{
		ImGui.Spacing();
		Vector4 vector = ImGui.GetStyle().Colors[19];
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
		(float, float) contentLineBounds = GetContentLineBounds();
		float x = Math.Max(24f, contentLineBounds.Item2 - contentLineBounds.Item1);
		float y = cursorScreenPos.Y + 3f;
		windowDrawList.AddLine(new Vector2(contentLineBounds.Item1, y), new Vector2(contentLineBounds.Item2, y), ImGui.GetColorU32(WithAlpha(vector, 0.32f)), 1f);
		ImGui.Dummy(new Vector2(x, 7f));
		ImGui.PushStyleColor(ImGuiCol.Text, vector);
		ImGui.TextUnformatted(title);
		ImGui.PopStyleColor();
		ImGui.Spacing();
	}

	private static (float Left, float Right) GetContentLineBounds()
	{
		Vector2 windowPos = ImGui.GetWindowPos();
		return (Left: windowPos.X + ImGui.GetWindowContentRegionMin().X, Right: windowPos.X + ImGui.GetWindowContentRegionMax().X);
	}

	private static void DrawFullContentWidthDivider(float yOffset, Vector4 color, float thickness = 1f, float height = 1f)
	{
		Vector4 vector = ImGui.GetStyle().Colors[27];
		color = new Vector4(vector.X, vector.Y, vector.Z, color.W);
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
		(float, float) contentLineBounds = GetContentLineBounds();
		float y = cursorScreenPos.Y + yOffset;
		windowDrawList.AddLine(new Vector2(contentLineBounds.Item1, y), new Vector2(contentLineBounds.Item2, y), ImGui.GetColorU32(color), thickness);
		ImGui.Dummy(new Vector2(1f, height));
	}

	private static (float Left, float Right) GetSectionCardLineBounds()
	{
		(float, float) contentLineBounds = GetContentLineBounds();
		return (Left: contentLineBounds.Item1 + 1f, Right: contentLineBounds.Item2 - 1f);
	}

	private bool DrawAddComponentCard(TaskBarComponentDefinition component)
	{
		ThemePalette effectiveTheme = GetEffectiveTheme();
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
		float x = Math.Max(280f, ImGui.GetContentRegionAvail().X);
		Vector2 pMax = cursorScreenPos + new Vector2(x, 48f);
		ImU8String strId = new ImU8String(14, 1);
		strId.AppendLiteral("add_component_");
		strId.AppendFormatted(component.Id);
		ImGui.InvisibleButton(strId, new Vector2(x, 48f));
		bool flag = ImGui.IsItemHovered();
		bool flag2 = ImGui.IsItemActive();
		bool result = ImGui.IsItemClicked();
		Vector4 col = (flag2 ? WithAlpha(effectiveTheme.HeaderActive, 0.96f) : (flag ? WithAlpha(effectiveTheme.HeaderHovered, 0.96f) : WithAlpha(effectiveTheme.Surface, 0.88f)));
		Vector4 col2 = WithAlpha(flag ? effectiveTheme.Accent : effectiveTheme.Border, flag ? 0.78f : 0.44f);
		windowDrawList.AddRectFilled(cursorScreenPos, pMax, ImGui.GetColorU32(col), 8f);
		windowDrawList.AddRect(cursorScreenPos, pMax, ImGui.GetColorU32(col2), 8f, ImDrawFlags.None, flag2 ? 1.4f : 1f);
		DrawTaskBarComponentIcon(windowDrawList, component.Id, cursorScreenPos + new Vector2(25f, 24f), WithAlpha(flag ? effectiveTheme.Accent : effectiveTheme.TextMuted, flag ? 1f : 0.82f));
		Vector2 pos = cursorScreenPos + new Vector2(50f, 7f);
		Vector2 pos2 = cursorScreenPos + new Vector2(50f, 27f);
		float maxWidth = Math.Max(40f, pMax.X - pos2.X - 12f);
		windowDrawList.AddText(pos, ImGui.GetColorU32(effectiveTheme.Text), component.Name);
		windowDrawList.AddText(pos2, ImGui.GetColorU32(effectiveTheme.TextMuted), TrimTextToWidth(component.Description, maxWidth));
		ImGui.SetCursorScreenPos(cursorScreenPos + new Vector2(0f, 54f));
		return result;
	}

	private static string TrimTextToWidth(string text, float maxWidth)
	{
		if (string.IsNullOrWhiteSpace(text) || ImGui.CalcTextSize(text).X <= maxWidth)
		{
			return text;
		}
		string text2 = text.Trim();
		while (text2.Length > 0 && ImGui.CalcTextSize(text2 + "…").X > maxWidth)
		{
			string text3 = text2;
			text2 = text3.Substring(0, text3.Length - 1);
		}
		if (text2.Length != 0)
		{
			return text2 + "…";
		}
		return "…";
	}

	private void DrawHudScaleCombo(string label, float currentScale, Action<float> setter)
	{
		DrawInlineHudScaleCombo(label, label, currentScale, setter, GetEffectiveTheme().TextMuted);
	}

	private void DrawInlineFrameLabel(string label, Vector4? labelColor = null)
	{
		ImGui.AlignTextToFramePadding();
		if (labelColor.HasValue)
		{
			Vector4 valueOrDefault = labelColor.GetValueOrDefault();
			ImGui.PushStyleColor(ImGuiCol.Text, valueOrDefault);
		}
		ImGui.TextUnformatted(label);
		if (labelColor.HasValue)
		{
			ImGui.PopStyleColor();
		}
	}

	private void DrawInlineHudScaleCombo(string label, string id, float currentScale, Action<float> setter, Vector4? labelColor = null)
	{
		ReadOnlySpan<float> readOnlySpan = new float[8] { 0.6f, 0.8f, 1f, 1.2f, 1.4f, 1.6f, 1.8f, 2f };
		string text = $"{MathF.Round(currentScale * 100f):0}%";
		string text2 = label + " " + text;
		ImGui.SetNextItemWidth(Math.Clamp(ImGui.CalcTextSize(text2).X + 48f, 110f, 220f));
		ImU8String label2 = new ImU8String(2, 1);
		label2.AppendLiteral("##");
		label2.AppendFormatted(id);
		if (!ImGui.BeginCombo(label2, text2))
		{
			return;
		}
		ReadOnlySpan<float> readOnlySpan2 = readOnlySpan;
		for (int i = 0; i < readOnlySpan2.Length; i++)
		{
			float num = readOnlySpan2[i];
			string obj = $"{MathF.Round(num * 100f):0}%";
			bool flag = Math.Abs(currentScale - num) < 0.001f;
			if (ImGui.Selectable(obj, flag))
			{
				setter(num);
				saveConfig();
			}
			if (flag)
			{
				ImGui.SetItemDefaultFocus();
			}
		}
		ImGui.EndCombo();
	}

	private static bool DrawInlineOpacitySlider(string label, string id, ref float value, float minValue = 0.15f, float maxValue = 1f)
	{
		float v = value * 100f;
		ImGui.SetNextItemWidth(Math.Clamp(ImGui.CalcTextSize($"{label} {v:0}%").X + 42f, 82f, 150f));
		ImU8String imU8String = new ImU8String(2, 1);
		imU8String.AppendLiteral("##");
		imU8String.AppendFormatted(id);
		ImU8String label2 = imU8String;
		float vMin = minValue * 100f;
		float vMax = maxValue * 100f;
		ImU8String format = new ImU8String(7, 1);
		format.AppendFormatted(label);
		format.AppendLiteral(" %.0f%%");
		if (!ImGui.SliderFloat(label2, ref v, vMin, vMax, format))
		{
			return false;
		}
		value = Math.Clamp(v / 100f, minValue, maxValue);
		return true;
	}

	private static bool DrawInlinePercentSlider(string label, string id, ref float value, float minValue = 0f, float maxValue = 100f)
	{
		ImGui.SetNextItemWidth(Math.Clamp(ImGui.CalcTextSize($"{label} {value:0}%").X + 42f, 82f, 150f));
		ImU8String imU8String = new ImU8String(2, 1);
		imU8String.AppendLiteral("##");
		imU8String.AppendFormatted(id);
		ImU8String label2 = imU8String;
		ImU8String format = new ImU8String(7, 1);
		format.AppendFormatted(label);
		format.AppendLiteral(" %.0f%%");
		return ImGui.SliderFloat(label2, ref value, minValue, maxValue, format);
	}

	private static bool DrawInlineFloatSlider(string label, string id, ref float value, float minValue, float maxValue, string format)
	{
		ImGui.SetNextItemWidth(Math.Clamp(ImGui.CalcTextSize($"{label} {value:0.0}").X + 42f, 100f, 170f));
		ImU8String imU8String = new ImU8String(2, 1);
		imU8String.AppendLiteral("##");
		imU8String.AppendFormatted(id);
		ImU8String label2 = imU8String;
		ImU8String format2 = new ImU8String(1, 2);
		format2.AppendFormatted(label);
		format2.AppendLiteral(" ");
		format2.AppendFormatted(format);
		return ImGui.SliderFloat(label2, ref value, minValue, maxValue, format2);
	}

	private void DrawCustomShortcutSettings(string componentId)
	{
		CustomShortcutDefinition customShortcut = GetCustomShortcut(componentId);
		float width = GetExpandedSettingsWidth(260f);
		DrawCustomShortcutFormSettings(componentId, customShortcut, width);
	}

	private void DrawCustomShortcutFormSettings(string componentId, CustomShortcutDefinition shortcut, float width)
	{
		float width2 = Math.Min(520f, Math.Max(180f, width - 8f));
		DrawEditableTextButton("点击改名", string.IsNullOrWhiteSpace(shortcut.Name) ? "快捷方式" : shortcut.Name.Trim(), "CustomShortcutName_" + componentId, 92f, delegate(string value)
		{
			shortcut.Name = value.Trim();
		});
		SameLineOrWrap(108f);
		DrawAddCommandLineButton(componentId, shortcut, 92f);
		SameLineOrWrap(134f);
		DrawDailyRoutinesPresetButton(componentId, shortcut, 118f);
		SameLineOrWrap(192f);
		DrawIconPickerButton("更换图标", "CustomShortcutIcon_" + componentId, shortcut.IconId, delegate(uint value)
		{
			shortcut.IconId = value;
		}, "使用名称文字", new Vector2(184f, 34f));
		ImGui.Dummy(new Vector2(1f, 8f));
		DrawCustomShortcutCommandPreview(componentId, shortcut, width2);
	}

	private static void SameLineOrWrap(float nextItemWidth, float spacing = 8f)
	{
		if (ImGui.GetContentRegionAvail().X >= nextItemWidth + spacing)
		{
			ImGui.SameLine(0f, spacing);
		}
		else
		{
			ImGui.Dummy(new Vector2(1f, 6f));
		}
	}

	private void DrawDailyRoutinesPresetButton(string componentId, CustomShortcutDefinition shortcut, float buttonWidth)
	{
		string text = "DailyRoutinesShortcutPreset_" + componentId;
		ImU8String label = new ImU8String(10, 1);
		label.AppendLiteral("添加 DR 预设##");
		label.AppendFormatted(componentId);
		if (ImGui.Button(label, new Vector2(buttonWidth, 26f)))
		{
			ImGui.OpenPopup(text);
		}
		if (!ImGui.BeginPopup(text))
		{
			return;
		}
		ImGui.TextDisabled("Daily Routines 预设");
		ImGui.Separator();
		CustomShortcutPreset[] dailyRoutinesCustomShortcutPresets = DailyRoutinesCustomShortcutPresets;
		for (int i = 0; i < dailyRoutinesCustomShortcutPresets.Length; i++)
		{
			CustomShortcutPreset preset = dailyRoutinesCustomShortcutPresets[i];
			if (ImGui.Selectable(preset.DisplayName))
			{
				ApplyDailyRoutinesPreset(shortcut, preset);
				pendingCustomShortcutCommandLineCounts.Remove(componentId);
				ImGui.CloseCurrentPopup();
			}
			if (ImGui.IsItemHovered())
			{
				DrawStyledTooltip(delegate
				{
					ImGui.TextUnformatted(preset.Description);
					ImGui.Separator();
					ImGui.TextUnformatted(preset.Command);
				});
			}
		}
		ImGui.EndPopup();
	}

	private void ApplyDailyRoutinesPreset(CustomShortcutDefinition shortcut, CustomShortcutPreset preset)
	{
		List<string> customShortcutCommandLines = GetCustomShortcutCommandLines(shortcut);
		if (!customShortcutCommandLines.Any((string command) => command.Equals(preset.Command, StringComparison.OrdinalIgnoreCase)))
		{
			customShortcutCommandLines.Add(preset.Command);
		}
		shortcut.Name = preset.Name;
		shortcut.IconId = preset.IconId;
		shortcut.Command = string.Join('\n', customShortcutCommandLines);
		saveConfig();
	}

	private void DrawAddCommandLineButton(string componentId, CustomShortcutDefinition shortcut, float buttonWidth)
	{
		ImU8String label = new ImU8String(6, 1);
		label.AppendLiteral("添加命令##");
		label.AppendFormatted(componentId);
		if (ImGui.Button(label, new Vector2(buttonWidth, 26f)))
		{
			pendingCustomShortcutCommandLineCounts.TryGetValue(componentId, out var value);
			pendingCustomShortcutCommandLineCounts[componentId] = value + 1;
		}
	}

	private void DrawCustomShortcutCommandPreview(string componentId, CustomShortcutDefinition shortcut, float width)
	{
		ApplyPendingCustomShortcutCommandRemoval(componentId, shortcut);
		List<string> customShortcutCommandLines = GetCustomShortcutCommandLines(shortcut);
		pendingCustomShortcutCommandLineCounts.TryGetValue(componentId, out var value);
		int num = customShortcutCommandLines.Count + value;
		List<string> list = GetCustomShortcutCommandLineUiIds(componentId, num);
		DrawCustomShortcutFieldLabel((num == 0) ? "未设置命令" : "已添加命令");
		float previewWidth = Math.Min(420f, Math.Max(180f, width));
		float rowHeight = ImGui.GetFrameHeight() + 4f;
		if (num == 0)
		{
			ImGui.TextDisabled("点击“添加命令”设置点击后执行的指令。");
			return;
		}
		for (int i = 0; i < num; i++)
		{
			bool isPendingLine = i >= customShortcutCommandLines.Count;
			bool flag = DrawCustomShortcutCommandLine(componentId, shortcut, customShortcutCommandLines, list[i], i, isPendingLine, previewWidth, rowHeight, out var removeRequested);
			if (removeRequested)
			{
				pendingCustomShortcutCommandRemovals[componentId] = new PendingCustomShortcutCommandRemoval(list[i], isPendingLine);
			}
			else if (flag)
			{
				break;
			}
		}
	}

	private void ApplyPendingCustomShortcutCommandRemoval(string componentId, CustomShortcutDefinition shortcut)
	{
		if (pendingCustomShortcutCommandRemovals.Remove(componentId, out var value) && customShortcutCommandLineUiIds.TryGetValue(componentId, out List<string> value2))
		{
			int num = value2.IndexOf(value.RowUiId);
			if (num >= 0)
			{
				List<string> customShortcutCommandLines = GetCustomShortcutCommandLines(shortcut);
				RemoveCustomShortcutCommandLine(componentId, shortcut, customShortcutCommandLines, num, value.IsPendingLine);
			}
		}
	}

	private List<string> GetCustomShortcutCommandLineUiIds(string componentId, int rowCount)
	{
		if (!customShortcutCommandLineUiIds.TryGetValue(componentId, out List<string> value))
		{
			value = new List<string>();
			customShortcutCommandLineUiIds[componentId] = value;
		}
		while (value.Count < rowCount)
		{
			value.Add(Guid.NewGuid().ToString("N"));
		}
		while (value.Count > rowCount)
		{
			value.RemoveAt(value.Count - 1);
		}
		return value;
	}

	private bool DrawCustomShortcutCommandLine(string componentId, CustomShortcutDefinition shortcut, List<string> commands, string rowUiId, int index, bool isPendingLine, float previewWidth, float rowHeight, out bool removeRequested)
	{
		ThemePalette effectiveTheme = GetEffectiveTheme();
		removeRequested = false;
		Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
		if (!isPendingLine && (index < 0 || index >= commands.Count))
		{
			return true;
		}
		string value = (isPendingLine ? string.Empty : commands[index]);
		float nextItemWidth = Math.Max(90f, previewWidth - 26f - 48f - 12f);
		ImGui.SetCursorScreenPos(new Vector2(cursorScreenPos.X + 26f, cursorScreenPos.Y));
		byte[] array = CreateUtf8Buffer(value, 256);
		ImGui.SetNextItemWidth(nextItemWidth);
		bool result = false;
		ImGui.PushStyleColor(ImGuiCol.FrameBg, WithAlpha(effectiveTheme.FrameBg, 0.86f));
		ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, WithAlpha(effectiveTheme.FrameHovered, 0.86f));
		ImGui.PushStyleColor(ImGuiCol.FrameBgActive, WithAlpha(effectiveTheme.FrameActive, 0.86f));
		ImU8String label = new ImU8String(28, 1);
		label.AppendLiteral("##CustomShortcutCommandLine_");
		label.AppendFormatted(rowUiId);
		if (ImGui.InputText(label, array))
		{
			result = UpdateCustomShortcutCommandLine(componentId, shortcut, commands, index, isPendingLine, ReadUtf8Buffer(array));
		}
		ImGui.PopStyleColor(3);
		Vector2 itemRectMin = ImGui.GetItemRectMin();
		Vector2 itemRectMax = ImGui.GetItemRectMax();
		float num = itemRectMax.Y - itemRectMin.Y;
		string text = $"{index + 1}.";
		Vector2 vector = ImGui.CalcTextSize(text);
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		Vector2 pos = new Vector2(itemRectMin.X - 4f - vector.X, itemRectMin.Y + (num - vector.Y) * 0.5f + 1.5f);
		windowDrawList.AddText(pos, ImGui.GetColorU32(WithAlpha(effectiveTheme.TextMuted, 0.76f)), text);
		ImGui.SetCursorScreenPos(new Vector2(itemRectMax.X + 12f, itemRectMin.Y));
		if (DrawCommandLineRemoveButton("CustomShortcutCommandRemove_" + rowUiId, new Vector2(48f, num)))
		{
			removeRequested = true;
		}
		ImGui.SetCursorScreenPos(new Vector2(cursorScreenPos.X, cursorScreenPos.Y + rowHeight));
		return result;
	}

	private bool DrawCommandLineRemoveButton(string id, Vector2 size)
	{
		ThemePalette effectiveTheme = GetEffectiveTheme();
		Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
		ImU8String strId = new ImU8String(2, 1);
		strId.AppendLiteral("##");
		strId.AppendFormatted(id);
		ImGui.InvisibleButton(strId, size);
		bool result = ImGui.IsItemClicked(ImGuiMouseButton.Left);
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		Vector2 pMax = cursorScreenPos + size;
		windowDrawList.AddRectFilled(cursorScreenPos, pMax, ImGui.GetColorU32(WithAlpha(effectiveTheme.Surface, 0.74f)), 5f);
		windowDrawList.AddRect(cursorScreenPos, pMax, ImGui.GetColorU32(WithAlpha(effectiveTheme.Accent, 0.52f)), 5f);
		Vector2 vector = ImGui.CalcTextSize("移除");
		windowDrawList.AddText(cursorScreenPos + (size - vector) * 0.5f, ImGui.GetColorU32(WithAlpha(effectiveTheme.Accent, 0.96f)), "移除");
		return result;
	}

	private void RemoveCustomShortcutCommandLine(string componentId, CustomShortcutDefinition shortcut, List<string> commands, int index, bool isPendingLine)
	{
		if (isPendingLine)
		{
			DecrementPendingCustomShortcutCommandLine(componentId);
			RemoveCustomShortcutCommandLineUiId(componentId, index);
		}
		else if (index >= 0 && index < commands.Count)
		{
			commands.RemoveAt(index);
			RemoveCustomShortcutCommandLineUiId(componentId, index);
			shortcut.Command = string.Join('\n', commands);
			saveConfig();
		}
	}

	private void RemoveCustomShortcutCommandLineUiId(string componentId, int index)
	{
		if (customShortcutCommandLineUiIds.TryGetValue(componentId, out List<string> value) && index >= 0 && index < value.Count)
		{
			value.RemoveAt(index);
		}
	}

	private bool UpdateCustomShortcutCommandLine(string componentId, CustomShortcutDefinition shortcut, List<string> commands, int index, bool isPendingLine, string value)
	{
		if (isPendingLine)
		{
			commands.Add(value);
			DecrementPendingCustomShortcutCommandLine(componentId);
		}
		else
		{
			commands[index] = value;
		}
		shortcut.Command = string.Join('\n', commands.Where((string command) => !string.IsNullOrWhiteSpace(command)));
		saveConfig();
		return isPendingLine;
	}

	private void DecrementPendingCustomShortcutCommandLine(string componentId)
	{
		if (pendingCustomShortcutCommandLineCounts.TryGetValue(componentId, out var value))
		{
			if (value <= 1)
			{
				pendingCustomShortcutCommandLineCounts.Remove(componentId);
			}
			else
			{
				pendingCustomShortcutCommandLineCounts[componentId] = value - 1;
			}
		}
	}

	private static List<string> GetCustomShortcutCommandLines(CustomShortcutDefinition shortcut)
	{
		return (from command in (shortcut.Command ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n')
			where !string.IsNullOrWhiteSpace(command)
			select command).ToList();
	}

	private void DrawEditableTextButton(string actionLabel, string value, string id, float buttonWidth, Action<string> setter)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			value.Trim();
		}
		string text = "EditText_" + id;
		ImU8String label = new ImU8String(9, 2);
		label.AppendFormatted(actionLabel);
		label.AppendLiteral("##");
		label.AppendFormatted(id);
		label.AppendLiteral("_Button");
		if (ImGui.Button(label, new Vector2(buttonWidth, 26f)))
		{
			ImGui.OpenPopup(text);
		}
		ImGui.SetNextWindowSize(new Vector2(320f, 0f), ImGuiCond.Appearing);
		if (ImGui.BeginPopup(text))
		{
			byte[] array = CreateUtf8Buffer(value, 96);
			ImGui.SetNextItemWidth(260f);
			ImU8String label2 = new ImU8String(8, 1);
			label2.AppendLiteral("##");
			label2.AppendFormatted(id);
			label2.AppendLiteral("_Input");
			if (ImGui.InputText(label2, array))
			{
				setter(ReadUtf8Buffer(array));
				saveConfig();
			}
			ImGui.SameLine(0f, 6f);
			ImU8String label3 = new ImU8String(12, 1);
			label3.AppendLiteral("确定##");
			label3.AppendFormatted(id);
			label3.AppendLiteral("_Confirm");
			if (ImGui.Button(label3, new Vector2(52f, 24f)))
			{
				setter(ReadUtf8Buffer(array));
				saveConfig();
				ImGui.CloseCurrentPopup();
			}
			ImGui.EndPopup();
		}
	}

	private void DrawCustomShortcutFieldLabel(string label, string hint = "")
	{
		ImGui.PushStyleColor(ImGuiCol.Text, WithAlpha(GetEffectiveTheme().TextMuted, 0.94f));
		ImGui.TextUnformatted(label);
		ImGui.PopStyleColor();
		if (!string.IsNullOrWhiteSpace(hint))
		{
			ImGui.SameLine();
			ImGui.TextDisabled(hint);
		}
	}

	private void DrawIconPickerButton(string actionLabel, string id, uint iconId, Action<uint> setter, string emptyText = "默认图标", Vector2? size = null)
	{
		ThemePalette effectiveTheme = GetEffectiveTheme();
		Vector2 vector = size ?? new Vector2(184f, 42f);
		Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
		ImU8String strId = new ImU8String(9, 1);
		strId.AppendLiteral("##");
		strId.AppendFormatted(id);
		strId.AppendLiteral("_Button");
		ImGui.InvisibleButton(strId, vector);
		bool flag = ImGui.IsItemHovered();
		bool flag2 = ImGui.IsItemActive();
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		Vector2 pMax = cursorScreenPos + vector;
		windowDrawList.AddRectFilled(cursorScreenPos, pMax, ImGui.GetColorU32(flag2 ? WithAlpha(effectiveTheme.HeaderActive, 0.78f) : (flag ? WithAlpha(effectiveTheme.HeaderHovered, 0.82f) : WithAlpha(effectiveTheme.Surface, 0.78f))), 7f);
		windowDrawList.AddRect(cursorScreenPos, pMax, ImGui.GetColorU32(WithAlpha(effectiveTheme.Border, flag ? 0.74f : 0.42f)), 7f);
		float num = Math.Min(34f, Math.Max(22f, vector.Y - 4f));
		Vector2 vector2 = cursorScreenPos + new Vector2(4f, (vector.Y - num) * 0.5f);
		DrawActionIconImage(iconId, vector2, vector2 + new Vector2(num, num), enabled: true);
		string text = ((iconId == 0) ? (actionLabel + "：" + emptyText) : $"{actionLabel}：{iconId}");
		Vector2 vector3 = ImGui.CalcTextSize(text);
		windowDrawList.AddText(cursorScreenPos + new Vector2(num + 12f, (vector.Y - vector3.Y) * 0.5f), ImGui.GetColorU32(effectiveTheme.Text), text);
		if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
		{
			ImU8String strId2 = new ImU8String(11, 1);
			strId2.AppendLiteral("IconPicker_");
			strId2.AppendFormatted(id);
			ImGui.OpenPopup(strId2);
		}
		DrawIconPickerPopup("IconPicker_" + id, iconId, setter);
	}

	private void DrawIconPickerPopup(string popupId, uint iconId, Action<uint> setter)
	{
		ThemePalette effectiveTheme = GetEffectiveTheme();
		ImGui.SetNextWindowSize(new Vector2(340f, 0f), ImGuiCond.Appearing);
		if (!ImGui.BeginPopup(popupId))
		{
			return;
		}
		Vector2 vector = new Vector2(38f, 38f);
		Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
		Vector2 max = cursorScreenPos + vector;
		ImGui.TextDisabled("当前图标");
		ImU8String strId = new ImU8String(10, 1);
		strId.AppendLiteral("##");
		strId.AppendFormatted(popupId);
		strId.AppendLiteral("_Preview");
		ImGui.InvisibleButton(strId, vector);
		DrawActionIconImage(iconId, cursorScreenPos, max, enabled: true);
		int data = (int)((iconId > int.MaxValue) ? int.MaxValue : iconId);
		float x = 58f;
		ImGui.SameLine(0f, 8f);
		ImGui.SetNextItemWidth(132f);
		ImU8String label = new ImU8String(9, 1);
		label.AppendLiteral("##");
		label.AppendFormatted(popupId);
		label.AppendLiteral("_IconId");
		if (ImGui.InputInt(label, ref data))
		{
			setter((uint)Math.Max(0, data));
			saveConfig();
		}
		ImGui.SameLine(0f, 6f);
		ImU8String label2 = new ImU8String(4, 1);
		label2.AppendLiteral("清除##");
		label2.AppendFormatted(popupId);
		if (ImGui.Button(label2, new Vector2(x, 24f)))
		{
			setter(0u);
			saveConfig();
		}
		ImGui.Spacing();
		ImGui.TextDisabled("常用图标");
		Vector2 vector2 = new Vector2(54f, 54f);
		for (int i = 0; i < CommonCustomShortcutIconIds.Length; i++)
		{
			uint num = CommonCustomShortcutIconIds[i];
			ImU8String strId2 = new ImU8String(19, 1);
			strId2.AppendLiteral("CustomShortcutIcon_");
			strId2.AppendFormatted(num);
			ImGui.PushID(strId2);
			Vector2 cursorScreenPos2 = ImGui.GetCursorScreenPos();
			ImGui.InvisibleButton("##Icon", vector2);
			bool flag = ImGui.IsItemHovered();
			bool flag2 = iconId == num;
			Vector2 vector3 = cursorScreenPos2 + vector2;
			ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
			if (flag2 | flag)
			{
				windowDrawList.AddRectFilled(cursorScreenPos2, vector3, ImGui.GetColorU32(flag2 ? WithAlpha(effectiveTheme.HeaderActive, 0.62f) : WithAlpha(effectiveTheme.HeaderHovered, 0.34f)), 8f);
				windowDrawList.AddRect(cursorScreenPos2, vector3, ImGui.GetColorU32(WithAlpha(effectiveTheme.Accent, flag2 ? 0.92f : 0.58f)), 8f);
			}
			DrawActionIconImage(num, cursorScreenPos2 + new Vector2(6f), vector3 - new Vector2(6f), enabled: true);
			if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
			{
				setter(num);
				saveConfig();
				ImGui.CloseCurrentPopup();
			}
			if (flag)
			{
				DrawStyledTooltip(num.ToString());
			}
			ImGui.PopID();
			if ((i + 1) % 5 != 0)
			{
				ImGui.SameLine();
			}
		}
		ImGui.Spacing();
		ImGui.Separator();
		ImGui.TextDisabled("更多图标可直接填写图标 ID。");
		ImGui.EndPopup();
	}

	private List<TaskBarComponentDefinition> GetActiveTaskBarComponentDefinitions(List<string> componentOrder, bool normalizeAdaptiveOrder)
	{
		if (normalizeAdaptiveOrder)
		{
			config.TaskBarComponentOrder = Configuration.NormalizeTaskBarComponentOrder(componentOrder);
			componentOrder = config.TaskBarComponentOrder;
		}
		else
		{
			componentOrder = Configuration.NormalizeTaskBarSectionComponentOrder(componentOrder);
		}
		List<TaskBarComponentDefinition> list = new List<TaskBarComponentDefinition>(componentOrder.Count);
		foreach (string item in componentOrder)
		{
			if (IsPlacedComponentId(item))
			{
				TaskBarComponentDefinition componentDefinition = GetComponentDefinition(item);
				if (!string.IsNullOrWhiteSpace(componentDefinition.Id) && IsTaskBarComponentEnabled(componentDefinition))
				{
					list.Add(componentDefinition);
				}
			}
		}
		return list;
	}

	private List<TaskBarComponentDefinition> GetOrderedTaskBarComponentDefinitions()
	{
		config.TaskBarComponentOrder = Configuration.NormalizeTaskBarComponentOrder(config.TaskBarComponentOrder);
		return GetOrderedTaskBarComponentDefinitions(config.TaskBarComponentOrder);
	}

	private List<TaskBarComponentDefinition> GetOrderedTaskBarSectionComponentDefinitions(List<string> componentOrder)
	{
		return GetOrderedTaskBarComponentDefinitions(Configuration.NormalizeTaskBarSectionComponentOrder(componentOrder));
	}

	private List<TaskBarComponentDefinition> GetOrderedTaskBarComponentDefinitions(IReadOnlyList<string> componentOrder)
	{
		List<TaskBarComponentDefinition> list = new List<TaskBarComponentDefinition>(componentOrder.Count);
		foreach (string item in componentOrder)
		{
			if (IsPlacedComponentId(item))
			{
				TaskBarComponentDefinition componentDefinition = GetComponentDefinition(item);
				if (!string.IsNullOrWhiteSpace(componentDefinition.Id))
				{
					list.Add(componentDefinition);
				}
			}
		}
		return list;
	}

	private static bool ContainsTaskBarComponent(IEnumerable<string> componentOrder, string componentId)
	{
		return componentOrder.Any((string id) => id.Equals(componentId, StringComparison.OrdinalIgnoreCase));
	}

	private bool IsTaskBarComponentActiveInOrder(IEnumerable<string> componentOrder, string componentId)
	{
		if (ContainsTaskBarComponent(componentOrder, componentId))
		{
			return IsTaskBarComponentEnabled(componentId);
		}
		return false;
	}

	private bool IsTaskBarComponentEnabled(TaskBarComponentDefinition component)
	{
		string id = component.Id;
		switch (id)
		{
		case "time":
			return config.TaskBarShowLocalTime || config.TaskBarShowEorzeaTime;
		case "fps":
			return config.TaskBarShowFps;
		case "volume":
			return config.TaskBarShowVolume;
		case "main_menu":
			return config.TaskBarShowMainMenu;
		case "plugin_list":
			return config.TaskBarShowPluginList;
		case "plugin_shortcut":
			return config.TaskBarShowPluginShortcut && !string.IsNullOrWhiteSpace(config.TaskBarPluginShortcutInternalName);
		default:
			if (Configuration.IsPluginShortcutComponentId(component.Id))
			{
				return true;
			}
			if (Configuration.IsCustomShortcutComponentId(component.Id))
			{
				return true;
			}
			if (!Configuration.IsQuickMenuComponentId(component.Id))
			{
				return id switch
				{
					"server_info" => config.TaskBarShowServerInfoBar, 
					"inventory" => config.TaskBarShowInventory, 
					"saddlebag" => config.TaskBarShowSaddlebag, 
					"teleport" => config.TaskBarShowTeleport, 
					"coordinates" => config.TaskBarShowCoordinates, 
					"gearset_switcher" => config.TaskBarShowGearsetSwitcher, 
					"currency" => config.TaskBarShowCurrency, 
					"walking_indicator" => config.TaskBarShowWalkingIndicator, 
					_ => false, 
				};
			}
			return !component.Id.Equals("quick_menu", StringComparison.OrdinalIgnoreCase);
		}
	}

	private void SetTaskBarComponentEnabled(string componentId, bool enabled)
	{
		if (componentId == null)
		{
			return;
		}
		switch (componentId.Length)
		{
		case 11:
			switch (componentId[0])
			{
			case 'e':
				if (componentId == "eorzea_time")
				{
					config.TaskBarShowEorzeaTime = enabled;
				}
				break;
			case 'p':
				if (componentId == "plugin_list")
				{
					config.TaskBarShowPluginList = enabled;
				}
				break;
			case 's':
				if (componentId == "server_info")
				{
					config.TaskBarShowServerInfoBar = enabled;
				}
				break;
			case 'c':
				if (componentId == "coordinates")
				{
					config.TaskBarShowCoordinates = enabled;
				}
				break;
			}
			break;
		case 9:
			switch (componentId[0])
			{
			case 'm':
				if (componentId == "main_menu")
				{
					config.TaskBarShowMainMenu = enabled;
				}
				break;
			case 'i':
				if (componentId == "inventory")
				{
					config.TaskBarShowInventory = enabled;
				}
				break;
			case 's':
				if (componentId == "saddlebag")
				{
					config.TaskBarShowSaddlebag = enabled;
				}
				break;
			}
			break;
		case 8:
			switch (componentId[0])
			{
			case 't':
				if (componentId == "teleport")
				{
					config.TaskBarShowTeleport = enabled;
				}
				break;
			case 'c':
				if (componentId == "currency")
				{
					config.TaskBarShowCurrency = enabled;
				}
				break;
			}
			break;
		case 4:
			if (componentId == "time")
			{
				config.TaskBarShowLocalTime = enabled;
				config.TaskBarShowEorzeaTime = enabled;
			}
			break;
		case 10:
			if (componentId == "local_time")
			{
				config.TaskBarShowLocalTime = enabled;
			}
			break;
		case 3:
			if (componentId == "fps")
			{
				config.TaskBarShowFps = enabled;
			}
			break;
		case 6:
			if (componentId == "volume")
			{
				config.TaskBarShowVolume = enabled;
			}
			break;
		case 15:
			if (componentId == "plugin_shortcut")
			{
				config.TaskBarShowPluginShortcut = enabled;
			}
			break;
		case 16:
			if (componentId == "gearset_switcher")
			{
				config.TaskBarShowGearsetSwitcher = enabled;
			}
			break;
		case 17:
			if (componentId == "walking_indicator")
			{
				config.TaskBarShowWalkingIndicator = enabled;
			}
			break;
		case 5:
		case 7:
		case 12:
		case 13:
		case 14:
			break;
		}
	}

	private void AddTaskBarComponent(string componentId, List<string>? targetOrder = null)
	{
		componentId = CreateComponentInstanceId(componentId);
		if (Configuration.IsQuickMenuComponentId(componentId))
		{
			GetQuickMenu(componentId);
		}
		if (targetOrder == null)
		{
			targetOrder = config.TaskBarComponentOrder;
		}
		List<string> collection = ((targetOrder == config.TaskBarComponentOrder) ? Configuration.NormalizeTaskBarComponentOrder(targetOrder) : Configuration.NormalizeTaskBarSectionComponentOrder(targetOrder));
		targetOrder.Clear();
		targetOrder.AddRange(collection);
		if (!Configuration.IsRepeatableComponentId(componentId))
		{
			RemoveTaskBarComponentFromAllOrders(componentId);
		}
		int num = targetOrder.FindLastIndex(IsTaskBarComponentEnabled);
		if (num < 0)
		{
			targetOrder.Insert(0, componentId);
		}
		else
		{
			targetOrder.Insert(num + 1, componentId);
		}
		SetTaskBarComponentEnabled(componentId, enabled: true);
		saveConfig();
	}

	private static string CreateComponentInstanceId(string componentId)
	{
		if (Configuration.IsPluginShortcutComponentId(componentId))
		{
			return Configuration.CreatePluginShortcutComponentId();
		}
		if (Configuration.IsCustomShortcutComponentId(componentId))
		{
			return Configuration.CreateCustomShortcutComponentId();
		}
		if (!Configuration.IsQuickMenuComponentId(componentId))
		{
			return componentId;
		}
		return Configuration.CreateQuickMenuComponentId();
	}

	private void RemoveRepeatableComponentData(string componentId)
	{
		config.PluginShortcutInternalNames.Remove(componentId);
		config.CustomShortcuts.Remove(componentId);
		config.QuickMenus.Remove(componentId);
	}

	private void MoveTaskBarComponentRelativeTo(List<string> componentOrder, string componentId, string targetComponentId, bool insertAfter)
	{
		if (componentId.Equals(targetComponentId, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		List<string> collection = ((componentOrder == config.TaskBarComponentOrder) ? Configuration.NormalizeTaskBarComponentOrder(componentOrder) : Configuration.NormalizeTaskBarSectionComponentOrder(componentOrder));
		componentOrder.Clear();
		componentOrder.AddRange(collection);
		int num = componentOrder.FindIndex((string id) => id.Equals(componentId, StringComparison.OrdinalIgnoreCase));
		int num2 = componentOrder.FindIndex((string id) => id.Equals(targetComponentId, StringComparison.OrdinalIgnoreCase));
		if (num >= 0 && num2 >= 0)
		{
			string item = componentOrder[num];
			componentOrder.RemoveAt(num);
			if (num < num2)
			{
				num2--;
			}
			int value = (insertAfter ? (num2 + 1) : num2);
			componentOrder.Insert(Math.Clamp(value, 0, componentOrder.Count), item);
			saveConfig();
		}
	}

	private static string GetTaskBarDragComponentId(string dragId, string scopeKey)
	{
		string text = "task:" + scopeKey + ":";
		if (!dragId.StartsWith(text, StringComparison.OrdinalIgnoreCase))
		{
			return string.Empty;
		}
		return dragId.Substring(text.Length);
	}

	private void RemoveTaskBarComponentFromAllOrders(string componentId)
	{
		config.TaskBarComponentOrder.RemoveAll((string id) => id.Equals(componentId, StringComparison.OrdinalIgnoreCase));
		config.TaskBarLeftComponentOrder.RemoveAll((string id) => id.Equals(componentId, StringComparison.OrdinalIgnoreCase));
		config.TaskBarCenterComponentOrder.RemoveAll((string id) => id.Equals(componentId, StringComparison.OrdinalIgnoreCase));
		config.TaskBarRightComponentOrder.RemoveAll((string id) => id.Equals(componentId, StringComparison.OrdinalIgnoreCase));
	}

	private bool IsTaskBarComponentEnabled(string componentId)
	{
		TaskBarComponentDefinition componentDefinition = GetComponentDefinition(componentId);
		if (!string.IsNullOrWhiteSpace(componentDefinition.Id))
		{
			return IsTaskBarComponentEnabled(componentDefinition);
		}
		return false;
	}

	private void SetTaskBarStretchToEdges(bool stretchToEdges)
	{
		if (config.TaskBarStretchToEdges != stretchToEdges)
		{
			if (stretchToEdges)
			{
				config.TaskBarCenterComponentOrder = Configuration.NormalizeTaskBarSectionComponentOrder(config.TaskBarComponentOrder);
				config.TaskBarLeftComponentOrder.Clear();
				config.TaskBarRightComponentOrder.Clear();
			}
			else
			{
				config.TaskBarComponentOrder = Configuration.NormalizeTaskBarComponentOrder(config.TaskBarLeftComponentOrder.Concat(config.TaskBarCenterComponentOrder).Concat(config.TaskBarRightComponentOrder));
				config.TaskBarLeftComponentOrder.Clear();
				config.TaskBarCenterComponentOrder.Clear();
				config.TaskBarRightComponentOrder.Clear();
			}
			config.TaskBarStretchToEdges = stretchToEdges;
			ClearSelectedTaskBarComponentSettings();
			saveConfig();
		}
	}

	private void DrawTaskBarPluginDockPage()
	{
		DrawTargetInfoSubsection("插件入口隐藏");
		ImGui.TextWrapped("隐藏插件悬浮窗。点击添加按钮后，把鼠标移到目标悬浮窗上，左键确认，右键或 Esc 取消。");
		ImGui.Spacing();
		DrawHiddenImGuiWindowPicker();
		DrawHiddenImGuiWindowList();
	}

	private void DrawHiddenImGuiWindowPicker()
	{
		if (!isPickingHiddenImGuiWindow)
		{
			if (ImGui.Button("添加隐藏窗口"))
			{
				isPickingHiddenImGuiWindow = true;
				hiddenImGuiWindowPickerStatus = "选择中：移动到目标悬浮窗，左键确认，右键或 Esc 取消。";
			}
			if (!string.IsNullOrWhiteSpace(hiddenImGuiWindowPickerStatus))
			{
				ImGui.SameLine();
				ImGui.TextDisabled(hiddenImGuiWindowPickerStatus);
			}
		}
		else
		{
			ImGui.TextDisabled(hiddenImGuiWindowPickerStatus);
		}
	}

	private void DrawHiddenImGuiWindowList()
	{
		if (config.HiddenImGuiWindowNames.Count == 0)
		{
			return;
		}
		ImGui.Spacing();
		DrawTargetInfoSubsection("已添加的隐藏项");
		for (int i = 0; i < config.HiddenImGuiWindowNames.Count; i++)
		{
			string text = config.HiddenImGuiWindowNames[i];
			ImU8String strId = new ImU8String(17, 2);
			strId.AppendLiteral("HiddenImGuiWindow");
			strId.AppendFormatted(i);
			strId.AppendFormatted(text);
			ImGui.PushID(strId);
			ImGui.TextUnformatted(text);
			ImGui.SameLine();
			if (ImGui.SmallButton("移除"))
			{
				config.HiddenImGuiWindowNames.RemoveAt(i);
				saveConfig();
				ImGui.PopID();
				break;
			}
			ImGui.PopID();
		}
	}

	private void AddHiddenImGuiWindow(string windowName)
	{
		string key = NormalizeImGuiWindowNameKey(windowName);
		if (key.Length == 0)
		{
			hiddenImGuiWindowPickerStatus = "未能识别窗口名。";
			return;
		}
		if (config.HiddenImGuiWindowNames.Any((string existing) => NormalizeImGuiWindowNameKey(existing).Equals(key, StringComparison.OrdinalIgnoreCase)))
		{
			hiddenImGuiWindowPickerStatus = "已在列表中: " + windowName;
			return;
		}
		config.HiddenImGuiWindowNames.Add(windowName.Trim());
		saveConfig();
		hiddenImGuiWindowPickerStatus = "已添加: " + windowName + "。可继续选择，右键或 Esc 结束。";
	}

	private unsafe static PickedImGuiWindow? TryGetHoveredImGuiWindow()
	{
		try
		{
			ImGuiContext* currentContext = ImGuiNative.GetCurrentContext();
			if (currentContext == null)
			{
				return null;
			}
			ImGuiContextPtr imGuiContextPtr = new ImGuiContextPtr(currentContext);
			if (imGuiContextPtr.IsNull)
			{
				return null;
			}
			Vector2 mousePos = ImGui.GetMousePos();
			PickedImGuiWindow? result = null;
			float num = float.MaxValue;
			ref ImVector<ImGuiWindowPtr> windows = ref imGuiContextPtr.Windows;
			for (int i = 0; i < windows.Size; i++)
			{
				ImGuiWindowPtr imGuiWindowPtr = windows[i];
				if (imGuiWindowPtr.Handle == null || imGuiWindowPtr.Name == null || imGuiWindowPtr.Hidden)
				{
					continue;
				}
				string text = Marshal.PtrToStringUTF8((nint)imGuiWindowPtr.Name)?.Trim() ?? string.Empty;
				string text2 = NormalizeImGuiWindowNameKey(text);
				if (text2.Length == 0 || IsSelfImGuiWindowName(text2))
				{
					continue;
				}
				Vector2 pos = imGuiWindowPtr.Pos;
				Vector2 size = imGuiWindowPtr.Size;
				if (size.X <= 1f || size.Y <= 1f)
				{
					continue;
				}
				Vector2 vector = pos + size;
				if (!(mousePos.X < pos.X) && !(mousePos.Y < pos.Y) && !(mousePos.X > vector.X) && !(mousePos.Y > vector.Y))
				{
					float num2 = size.X * size.Y;
					if (!(num2 >= num))
					{
						num = num2;
						result = new PickedImGuiWindow(text, text2);
					}
				}
			}
			return result;
		}
		catch
		{
			return null;
		}
	}

	private static bool IsSelfImGuiWindowName(string key)
	{
		if (!key.Contains("AllHud", StringComparison.OrdinalIgnoreCase) && !key.Contains("config_content", StringComparison.OrdinalIgnoreCase))
		{
			return key.Contains("config_nav", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private static string NormalizeImGuiWindowNameKey(string windowName)
	{
		string text = (windowName ?? string.Empty).Trim();
		if (text.Length == 0)
		{
			return string.Empty;
		}
		int num = text.IndexOf("###", StringComparison.Ordinal);
		if (num < 0)
		{
			return text;
		}
		return text.Substring(num);
	}

	private void DrawTaskBarAdvancedPage()
	{
		DrawTargetInfoSubsection("高级");
		DrawCheckbox("下载并缓存插件图标", "TaskBarDownloadPluginIcons", config.TaskBarDownloadPluginIcons, delegate(bool taskBarDownloadPluginIcons)
		{
			config.TaskBarDownloadPluginIcons = taskBarDownloadPluginIcons;
		});
		ImGui.Spacing();
		DrawTargetInfoSubsection("Umbra 世界标记");
		DrawCheckbox("启用世界标记", "ShowWorldMarkers", config.ShowWorldMarkers, delegate(bool showWorldMarkers)
		{
			config.ShowWorldMarkers = showWorldMarkers;
		});
		if (config.ShowWorldMarkers)
		{
			DrawCompactCheckbox("地图旗标", "WorldMarkersShowFlag", config.WorldMarkersShowFlag, delegate(bool worldMarkersShowFlag)
			{
				config.WorldMarkersShowFlag = worldMarkersShowFlag;
			});
			ImGui.SameLine(0f, 10f);
			DrawCompactCheckbox("场地标点", "WorldMarkersShowWaymarks", config.WorldMarkersShowWaymarks, delegate(bool worldMarkersShowWaymarks)
			{
				config.WorldMarkersShowWaymarks = worldMarkersShowWaymarks;
			});
			ImGui.SameLine(0f, 10f);
			DrawCompactCheckbox("屏外指示", "WorldMarkersShowCompass", config.WorldMarkersShowCompass, delegate(bool worldMarkersShowCompass)
			{
				config.WorldMarkersShowCompass = worldMarkersShowCompass;
			});
			DrawCompactCheckbox("显示标签", "WorldMarkersShowLabels", config.WorldMarkersShowLabels, delegate(bool worldMarkersShowLabels)
			{
				config.WorldMarkersShowLabels = worldMarkersShowLabels;
			});
			ImGui.SameLine(0f, 10f);
			DrawCompactCheckbox("显示距离", "WorldMarkersShowDistance", config.WorldMarkersShowDistance, delegate(bool worldMarkersShowDistance)
			{
				config.WorldMarkersShowDistance = worldMarkersShowDistance;
			});
			float value = config.WorldMarkersIconScale;
			if (DrawInlineFloatSlider("图标缩放", "WorldMarkersIconScale", ref value, 0.5f, 2.5f, "%.1fx"))
			{
				config.WorldMarkersIconScale = value;
				saveConfig();
			}
			float value2 = config.WorldMarkersFadeDistance;
			if (DrawInlineFloatSlider("开始淡出", "WorldMarkersFadeDistance", ref value2, 0f, 300f, "%.0fm"))
			{
				config.WorldMarkersFadeDistance = value2;
				saveConfig();
			}
			float value3 = config.WorldMarkersFadeAttenuation;
			if (DrawInlineFloatSlider("淡出距离", "WorldMarkersFadeAttenuation", ref value3, 1f, 300f, "%.0fm"))
			{
				config.WorldMarkersFadeAttenuation = value3;
				saveConfig();
			}
			float value4 = config.WorldMarkersMaxVisibleDistance;
			if (DrawInlineFloatSlider("最大距离", "WorldMarkersMaxVisibleDistance", ref value4, 0f, 1000f, (value4 <= 0f) ? "不限" : "%.0fm"))
			{
				config.WorldMarkersMaxVisibleDistance = value4;
				saveConfig();
			}
		}
	}

	private void DrawTaskBarPageTabs()
	{
		DrawTaskBarPageTab(TaskBarPage.任务栏, "主栏");
		ImGui.SameLine(0f, 6f);
		DrawTaskBarPageTab(TaskBarPage.辅助栏, "辅助栏");
		ImGui.SameLine(0f, 6f);
		DrawTaskBarPageTab(TaskBarPage.插件收纳, "插件隐藏");
		ImGui.SameLine(0f, 6f);
		DrawTaskBarPageTab(TaskBarPage.高级, "高级");
	}

	private void DrawTaskBarPageTab(TaskBarPage page, string label)
	{
		ThemePalette effectiveTheme = GetEffectiveTheme();
		bool flag = selectedTaskBarPage == page;
		Vector4 pageAccentColor = GetPageAccentColor(ConfigPage.任务栏);
		Vector2 vector = ImGui.CalcTextSize(label);
		Vector2 vector2 = new Vector2(vector.X + 24f, 28f);
		Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
		Vector2 pMax = cursorScreenPos + vector2;
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		ImU8String strId = new ImU8String(14, 1);
		strId.AppendLiteral("task_bar_page_");
		strId.AppendFormatted(page);
		ImGui.InvisibleButton(strId, vector2);
		bool flag2 = ImGui.IsItemHovered();
		if (ImGui.IsItemClicked())
		{
			selectedTaskBarPage = page;
		}
		Vector4 col = (flag ? WithAlpha(pageAccentColor, 0.78f) : (flag2 ? WithAlpha(pageAccentColor, 0.18f) : WithAlpha(effectiveTheme.Surface, 0.76f)));
		Vector4 col2 = (flag ? WithAlpha(pageAccentColor, 0.92f) : WithAlpha(pageAccentColor, flag2 ? 0.48f : 0.26f));
		Vector4 col3 = (flag ? effectiveTheme.TooltipText : WithAlpha(effectiveTheme.TextMuted, 0.92f));
		windowDrawList.AddRectFilled(cursorScreenPos, pMax, ImGui.GetColorU32(col), 999f);
		windowDrawList.AddRect(cursorScreenPos, pMax, ImGui.GetColorU32(col2), 999f, ImDrawFlags.None, flag ? 1.3f : 1f);
		windowDrawList.AddText(cursorScreenPos + (vector2 - vector) * 0.5f, ImGui.GetColorU32(col3), label);
	}

	private void DrawTaskBarServerInfoModeSelector()
	{
		int num = Math.Clamp(config.TaskBarServerInfoBarMode, 0, 1);
		if (num != config.TaskBarServerInfoBarMode)
		{
			config.TaskBarServerInfoBarMode = num;
		}
		DrawInlineSegmentedSelector("", "task_bar_server_info_mode", num, delegate(int value)
		{
			config.TaskBarServerInfoBarMode = Math.Clamp(value, 0, 1);
		}, ("独立一行", 0), ("合入主栏", 1));
	}

	private void DrawTaskBarCoordinatesModeSelector()
	{
		DrawComponentSettingGroupSpacing();
		DrawCheckbox("地区", "TaskBarShowCoordinatesTerritory_ComponentPanel", config.TaskBarShowCoordinatesTerritory, delegate(bool showTerritory)
		{
			config.TaskBarShowCoordinatesTerritory = showTerritory || !config.TaskBarShowCoordinatesPosition;
		});
		DrawCheckbox("坐标", "TaskBarShowCoordinatesPosition_ComponentPanel", config.TaskBarShowCoordinatesPosition, delegate(bool showPosition)
		{
			config.TaskBarShowCoordinatesPosition = showPosition || !config.TaskBarShowCoordinatesTerritory;
		});
	}

	private void DrawTaskBarEdgeSelector()
	{
		int num = ((config.TaskBarEdge == 1) ? 1 : 0);
		if (config.TaskBarEdge != num)
		{
			config.TaskBarEdge = num;
		}
		DrawInlineSegmentedSelector("位置", "task_bar_edge", num, delegate(int value)
		{
			config.TaskBarEdge = ((value == 1) ? 1 : 0);
		}, ("顶部", 0), ("底部", 1));
	}

	private void DrawAuxiliaryBarPositionSelector(AuxiliaryBarDefinition bar, int index)
	{
		int num = Math.Clamp(bar.PositionMode, 0, 2);
		if (bar.PositionMode != num)
		{
			bar.PositionMode = num;
		}
		DrawInlineSegmentedSelector("位置", $"auxiliary_bar_position_{index}", num, delegate(int value)
		{
			bar.PositionMode = Math.Clamp(value, 0, 2);
			SyncLegacyAuxiliaryBarSettings();
		}, ("左侧", 0), ("右侧", 1), ("自定义", 2));
	}

	private void DrawTaskBarHorizontalOffsetSlider()
	{
		float value = Math.Clamp(config.TaskBarHorizontalOffset, 0f, 1f) * 100f;
		if (DrawInlinePercentSlider("水平位置", "TaskBarHorizontalOffset", ref value))
		{
			config.TaskBarHorizontalOffset = Math.Clamp(value / 100f, 0f, 1f);
			saveConfig();
		}
	}

	private void DrawAuxiliaryBarVerticalOffsetSlider(AuxiliaryBarDefinition bar, int index)
	{
		float value = Math.Clamp(bar.VerticalOffset, 0f, 1f) * 100f;
		if (DrawInlinePercentSlider("垂直位置", $"AuxiliaryBarVerticalOffset{index}", ref value))
		{
			bar.VerticalOffset = Math.Clamp(value / 100f, 0f, 1f);
			saveConfig();
		}
	}

	private void DrawTaskBarOpacitySlider()
	{
		float value = Math.Clamp(config.TaskBarOpacity, 0.15f, 1f);
		if (DrawInlineOpacitySlider("透明度", "TaskBarOpacity", ref value))
		{
			config.TaskBarOpacity = value;
			saveConfig();
		}
	}

	private void DrawAuxiliaryBarOpacitySlider(AuxiliaryBarDefinition bar, int index)
	{
		float value = Math.Clamp(bar.Opacity, 0.15f, 1f);
		if (DrawInlineOpacitySlider("透明度", $"AuxiliaryBarOpacity{index}", ref value))
		{
			bar.Opacity = value;
			SyncLegacyAuxiliaryBarSettings();
			saveConfig();
		}
	}

	private void SyncLegacyAuxiliaryBarSettings()
	{
		AuxiliaryBarDefinition auxiliaryBarDefinition = config.AuxiliaryBars?.FirstOrDefault();
		if (auxiliaryBarDefinition == null)
		{
			config.ShowAuxiliaryBar = false;
			config.AuxiliaryBarPositionMode = 0;
			config.AuxiliaryBarScale = 1f;
			config.AuxiliaryBarOpacity = 0.72f;
		}
		else
		{
			config.ShowAuxiliaryBar = auxiliaryBarDefinition.Enabled;
			config.AuxiliaryBarPositionMode = Math.Clamp(auxiliaryBarDefinition.PositionMode, 0, 2);
			config.AuxiliaryBarScale = Math.Clamp((auxiliaryBarDefinition.Scale <= 0f) ? 1f : auxiliaryBarDefinition.Scale, 0.6f, 2f);
			config.AuxiliaryBarOpacity = Math.Clamp(auxiliaryBarDefinition.Opacity, 0.15f, 1f);
		}
	}

	private void DrawSkillsPage()
	{
		DrawSectionCard("独立监控", delegate
		{
			DrawSelfCooldownBarSettings();
			DrawJobSkillSelector();
		});
	}

	private void DrawSelfCooldownBarSettings()
	{
		DrawTargetInfoSubsection("显示设置");
		DrawCheckbox("启动独立监控", "ShowSelfCooldownBar", config.ShowSelfCooldownBar, delegate(bool value)
		{
			config.ShowSelfCooldownBar = value;
		});
		if (!config.ShowSelfCooldownBar)
		{
			ImGui.Spacing();
			return;
		}
		ImGui.SameLine(0f, 10f);
		DrawCheckbox("锁定窗口", "SelfCooldownBarLocked", config.SelfCooldownBarLocked, delegate(bool value)
		{
			config.SelfCooldownBarLocked = value;
		});
		DrawSelfCooldownBarSelfVisibilityOptions();
		DrawInlineSegmentedSelector("方向", "self_cooldown_bar_layout_direction", Math.Clamp(config.SelfCooldownBarLayoutDirection, 0, 1), delegate(int value)
		{
			config.SelfCooldownBarLayoutDirection = value;
		}, ("竖向", 0), ("横向", 1));
		DrawSelfCooldownBarScaleOpacityRow();
		DrawCheckbox("隐藏已就绪技能", "SelfCooldownBarHideWhenReady", config.SelfCooldownBarHideWhenReady, delegate(bool value)
		{
			config.SelfCooldownBarHideWhenReady = value;
		});
		ImGui.Spacing();
	}

	private void DrawSelfCooldownBarSelfVisibilityOptions()
	{
		int currentValue = (config.SelfCooldownBarSelfOnly ? 1 : (config.SelfCooldownBarHideSelf ? 2 : 0));
		DrawInlineSegmentedSelector("显示", "self_cooldown_bar_visibility", currentValue, delegate(int value)
		{
			config.SelfCooldownBarSelfOnly = value == 1;
			config.SelfCooldownBarHideSelf = value == 2;
		}, ("全部", 0), ("仅自己", 1), ("隐藏自己", 2));
	}

	private void DrawSelfCooldownBarScaleOpacityRow()
	{
		float value = Math.Clamp(config.SelfCooldownBarOpacity, 0.15f, 1f);
		Vector4 textMuted = GetEffectiveTheme().TextMuted;
		DrawInlineHudScaleCombo("缩放", "SelfCooldownBarScale", config.SelfCooldownBarScale, delegate(float value2)
		{
			config.SelfCooldownBarScale = Math.Clamp(value2, 0.6f, 2f);
		}, textMuted);
		ImGui.SameLine(0f, 12f);
		if (DrawInlineOpacitySlider("透明度", "SelfCooldownBarOpacity", ref value))
		{
			config.SelfCooldownBarOpacity = value;
			saveConfig();
		}
	}

	private void DrawDebugPage()
	{
		DrawSectionCard("预览", delegate
		{
			DrawCheckbox("状态栏预览", "ShowStatusPreview", config.ShowStatusPreview, delegate(bool value)
			{
				config.ShowStatusPreview = value;
			});
			DrawCheckbox("目标情报预览", "ShowTargetInfoPreview", config.ShowTargetInfoPreview, delegate(bool value)
			{
				config.ShowTargetInfoPreview = value;
			});
			DrawCheckbox("独立监控冷却栏预览", "ShowSelfCooldownBarPreview", config.ShowSelfCooldownBarPreview, delegate(bool value)
			{
				config.ShowSelfCooldownBarPreview = value;
			});
			DrawCheckbox("队伍信息预览", "ShowPartyInfoPreview", config.ShowPartyInfoPreview, delegate(bool value)
			{
				config.ShowPartyInfoPreview = value;
			});
			ImGui.TextDisabled("队伍信息贴在原生队伍栏上，预览只显示在当前可见的队伍行（单人时即自己那一行）");
		});
	}

	private void DrawJobSkillSelector()
	{
		TrackedActionCatalog.EnsureActionSelectionInitialized(config);
		Configuration configuration = config;
		if (configuration.EnabledJobActionKeys == null)
		{
			List<string> list = (configuration.EnabledJobActionKeys = new List<string>());
		}
		DrawJobActionSelector();
	}

	private void DrawSectionCard(string title, System.Action content)
	{
		ThemePalette effectiveTheme = GetEffectiveTheme();
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
		float num = Math.Max(120f, ImGui.GetContentRegionAvail().X);
		Vector4 vector = (ThemeDrawing.IsLiquidGlass(config.ThemeMode) ? effectiveTheme.Text : GetSectionTitleColor(title));
		windowDrawList.ChannelsSplit(2);
		windowDrawList.ChannelsSetCurrent(1);
		ImGui.Dummy(new Vector2(1f, 6f));
		ImGui.Indent(10f);
		ImGui.PushStyleColor(ImGuiCol.Text, vector);
		ImGui.TextUnformatted(title);
		ImGui.PopStyleColor();
		ImGui.Spacing();
		content();
		ImGui.Unindent(10f);
		ImGui.Dummy(new Vector2(1f, 4f));
		Vector2 vector2 = new Vector2(cursorScreenPos.X + num, ImGui.GetCursorScreenPos().Y);
		float num2 = vector2.Y - cursorScreenPos.Y;
		if (num2 > 0f)
		{
			windowDrawList.ChannelsSetCurrent(0);
			windowDrawList.AddRectFilled(cursorScreenPos + new Vector2(0f, 5f), vector2 + new Vector2(0f, 8f), ImGui.GetColorU32(WithAlpha(effectiveTheme.TextShadow, 0.22f)), ThemeDrawing.IsLiquidGlass(config.ThemeMode) ? 16f : 7f);
			windowDrawList.AddRectFilled(cursorScreenPos + new Vector2(0f, 2f), vector2 + new Vector2(0f, 4f), ImGui.GetColorU32(WithAlpha(effectiveTheme.TextShadow, 0.1f)), ThemeDrawing.IsLiquidGlass(config.ThemeMode) ? 16f : 7f);
			windowDrawList.AddRectFilled(cursorScreenPos, vector2 + new Vector2(0f, 2f), ImGui.GetColorU32(WithAlpha(effectiveTheme.Surface, 0.92f)), ThemeDrawing.IsLiquidGlass(config.ThemeMode) ? 16f : 7f);
			windowDrawList.AddRect(cursorScreenPos, vector2 + new Vector2(0f, 2f), ImGui.GetColorU32(effectiveTheme.Border), ThemeDrawing.IsLiquidGlass(config.ThemeMode) ? 16f : 7f, ImDrawFlags.None, 1f);
			windowDrawList.AddLine(cursorScreenPos + new Vector2(10f, 1f), new Vector2(vector2.X - 10f, cursorScreenPos.Y + 1f), ImGui.GetColorU32(WithAlpha(effectiveTheme.TaskBarGradientStart, 0.66f)), 1f);
			windowDrawList.AddRectFilled(cursorScreenPos + new Vector2(0f, 12f), cursorScreenPos + new Vector2(3f, Math.Min(num2 - 8f, 40f)), ImGui.GetColorU32(WithAlpha(ThemeDrawing.IsLiquidGlass(config.ThemeMode) ? effectiveTheme.Accent : vector, 0.82f)), 1.5f);
			if (ThemeDrawing.IsLiquidGlass(config.ThemeMode))
			{
				ThemeDrawing.DrawLiquidGlassPanel(windowDrawList, cursorScreenPos, vector2 + new Vector2(0f, 2f), Math.Min(16f, ThemeDrawing.GetLiquidGlassRounding(config)), config, 0.42f, drawShadow: false);
			}
		}
		windowDrawList.ChannelsMerge();
		ImGui.Spacing();
	}

	private void DrawJobActionSelector()
	{
		DrawJobClassSelectorIcons();
		int selectedJobCatalogIndex = GetSelectedJobCatalogIndex();
		JobCatalogEntry jobCatalogEntry = TrackedActionCatalog.Jobs[selectedJobCatalogIndex];
		DrawJobSkillSelectorDivider();
		IReadOnlyList<JobActionCatalogEntry> jobActionCatalog = combatState.GetJobActionCatalog(jobCatalogEntry.ClassJobId);
		if (jobActionCatalog.Count == 0)
		{
			DrawSkillHintText("这个职业暂时没有读取到技能。");
			return;
		}
		IReadOnlySet<string> enabledActionKeySet = GetEnabledActionKeySet();
		List<JobActionCatalogEntry> actions = FilterJobActions(jobActionCatalog, enabledActionKeySet);
		DrawJobActionGroupsByCategory(actions, 42f, "job", enabledActionKeySet);
	}

	private static void DrawJobSkillSelectorDivider()
	{
		Vector4 vector = ImGui.GetStyle().Colors[27];
		DrawFullContentWidthDivider(4f, new Vector4(vector.X, vector.Y, vector.Z, 0.22f), 1f, 9f);
	}

	private int GetSelectedJobCatalogIndex()
	{
		for (int i = 0; i < TrackedActionCatalog.Jobs.Count; i++)
		{
			if (TrackedActionCatalog.Jobs[i].ClassJobId == config.SelectedJobSkillConfigClassJobId)
			{
				return i;
			}
		}
		return 0;
	}

	private void DrawJobClassSelectorIcons()
	{
		DrawTargetInfoSubsection("职业");
		JobSkillRoleGroup selectedGroup = GetSelectedJobSkillRoleGroup();
		DrawJobSkillRoleTabs(selectedGroup);
		ImGui.Spacing();
		Vector2 size = new Vector2(42f, 42f);
		float num = Math.Max(120f, ImGui.GetContentRegionAvail().X);
		int num2 = Math.Max(1, (int)((num + 9f) / 51f));
		List<JobCatalogEntry> list = TrackedActionCatalog.Jobs.Where((JobCatalogEntry job) => selectedGroup.ClassJobIds.Contains(job.ClassJobId)).ToList();
		for (int num3 = 0; num3 < list.Count; num3++)
		{
			if (num3 > 0 && num3 % num2 != 0)
			{
				ImGui.SameLine(0f, 9f);
			}
			JobCatalogEntry jobCatalogEntry = list[num3];
			bool flag = config.SelectedJobSkillConfigClassJobId == jobCatalogEntry.ClassJobId;
			ImU8String strId = new ImU8String(10, 1);
			strId.AppendLiteral("job_class_");
			strId.AppendFormatted(jobCatalogEntry.ClassJobId);
			ImGui.PushID(strId);
			try
			{
				if (ImGui.InvisibleButton("##job_icon", size) && !flag)
				{
					config.SelectedJobSkillConfigClassJobId = jobCatalogEntry.ClassJobId;
					flag = true;
				}
				Vector2 itemRectMin = ImGui.GetItemRectMin();
				Vector2 itemRectMax = ImGui.GetItemRectMax();
				DrawActionIconImage(combatState.GetClassJobIconId(jobCatalogEntry.ClassJobId), itemRectMin, itemRectMax, flag, 0.75f);
				if (flag)
				{
					DrawSelectedJobIconBorder(itemRectMin, itemRectMax);
				}
				if (ImGui.IsItemHovered())
				{
					DrawStyledTooltip(jobCatalogEntry.Name);
				}
			}
			finally
			{
				ImGui.PopID();
			}
		}
	}

	private JobSkillRoleGroup GetSelectedJobSkillRoleGroup()
	{
		uint selectedJobSkillConfigClassJobId = config.SelectedJobSkillConfigClassJobId;
		JobSkillRoleGroup[] jobSkillRoleGroups = JobSkillRoleGroups;
		for (int i = 0; i < jobSkillRoleGroups.Length; i++)
		{
			JobSkillRoleGroup result = jobSkillRoleGroups[i];
			if (result.ClassJobIds.Contains(selectedJobSkillConfigClassJobId))
			{
				return result;
			}
		}
		JobSkillRoleGroup result2 = JobSkillRoleGroups[0];
		config.SelectedJobSkillConfigClassJobId = result2.ClassJobIds[0];
		return result2;
	}

	private void DrawJobSkillRoleTabs(JobSkillRoleGroup selectedGroup)
	{
		ThemePalette effectiveTheme = GetEffectiveTheme();
		Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
		for (int i = 0; i < JobSkillRoleGroups.Length; i++)
		{
			if (i > 0)
			{
				ImGui.SameLine(0f, 2f);
			}
			DrawJobSkillRoleTab(JobSkillRoleGroups[i], selectedGroup);
		}
		float y = cursorScreenPos.Y + 27f;
		(float, float) sectionCardLineBounds = GetSectionCardLineBounds();
		ImGui.GetWindowDrawList().AddLine(new Vector2(sectionCardLineBounds.Item1, y), new Vector2(sectionCardLineBounds.Item2, y), ImGui.GetColorU32(WithAlpha(effectiveTheme.Border, 0.34f)), 1f);
	}

	private void DrawJobSkillRoleTab(JobSkillRoleGroup group, JobSkillRoleGroup selectedGroup)
	{
		ThemePalette effectiveTheme = GetEffectiveTheme();
		bool flag = string.Equals(group.Label, selectedGroup.Label, StringComparison.Ordinal);
		Vector4 pageAccentColor = GetPageAccentColor(ConfigPage.技能);
		Vector2 vector = ImGui.CalcTextSize(group.Label);
		Vector2 vector2 = new Vector2(Math.Clamp(vector.X + 28f, 72f, 138f), 28f);
		Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
		Vector2 pMax = cursorScreenPos + vector2;
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		ImU8String strId = new ImU8String(19, 1);
		strId.AppendLiteral("job_skill_role_tab_");
		strId.AppendFormatted(group.Label);
		ImGui.InvisibleButton(strId, vector2);
		bool flag2 = ImGui.IsItemHovered();
		if (ImGui.IsItemClicked() && !flag)
		{
			config.SelectedJobSkillConfigClassJobId = group.ClassJobIds[0];
			flag = true;
		}
		Vector4 col = (flag ? WithAlpha(effectiveTheme.SurfaceAlt, 0.96f) : (flag2 ? WithAlpha(effectiveTheme.HeaderHovered, 0.82f) : WithAlpha(effectiveTheme.Surface, 0.58f)));
		Vector4 col2 = (flag ? WithAlpha(pageAccentColor, 0.62f) : WithAlpha(pageAccentColor, flag2 ? 0.36f : 0.22f));
		Vector4 col3 = (flag ? WithAlpha(pageAccentColor, 1f) : WithAlpha(effectiveTheme.TextMuted, 0.92f));
		windowDrawList.AddRectFilled(cursorScreenPos, pMax, ImGui.GetColorU32(col), 5f, ImDrawFlags.RoundCornersTop);
		windowDrawList.AddRect(cursorScreenPos, pMax, ImGui.GetColorU32(col2), 5f, ImDrawFlags.RoundCornersTop, flag ? 1.2f : 1f);
		if (flag)
		{
			windowDrawList.AddLine(new Vector2(cursorScreenPos.X + 1f, pMax.Y), new Vector2(pMax.X - 1f, pMax.Y), ImGui.GetColorU32(effectiveTheme.SurfaceAlt), 2f);
		}
		windowDrawList.AddText(cursorScreenPos + (vector2 - vector) * 0.5f, ImGui.GetColorU32(col3), group.Label);
	}

	private static void DrawSelectedJobIconBorder(Vector2 min, Vector2 max)
	{
		Vector4 col = ImGui.GetStyle().Colors[19];
		ImGui.GetWindowDrawList().AddRect(min, max, ImGui.GetColorU32(col), 4f, ImDrawFlags.None, 2f);
	}

	private void DrawActionGroup(string title, IReadOnlyList<JobActionCatalogEntry> actions, string idPrefix, string description)
	{
		DrawTargetInfoSubsection(title);
		DrawSkillHintText(description);
		if (actions.Count == 0)
		{
			DrawSkillHintText("暂无可选技能。");
			return;
		}
		IReadOnlySet<string> enabledActionKeySet = GetEnabledActionKeySet();
		DrawJobActionGroupsByCategory(actions, 42f, idPrefix, enabledActionKeySet);
	}

	private void DrawJobActionGroupsByCategory(IReadOnlyList<JobActionCatalogEntry> actions, float iconSize, string idPrefix, IReadOnlySet<string> enabledActionKeys)
	{
		if (actions.Count == 0)
		{
			DrawSkillHintText("当前筛选条件下没有技能。");
			return;
		}
		List<JobActionCatalogEntry> source = actions.GroupBy<JobActionCatalogEntry, string>(GetActionSelectionIdentity, StringComparer.Ordinal).Select(GetPreferredActionEntry).GroupBy<JobActionCatalogEntry, string>(GetActionDisplayIdentity, StringComparer.Ordinal)
			.Select(GetPreferredActionEntry)
			.ToList();
		foreach (CooldownGroup group in GetActionCategoryOrder())
		{
			List<JobActionCatalogEntry> list = (from action in source
				where GetActionDisplayGroup(action.Group) == @group
				orderby action.Level, action.Name
				select action).ToList();
			if (list.Count != 0)
			{
				DrawJobActionCategoryRow(group, list, iconSize, $"{idPrefix}_{group}", enabledActionKeys);
			}
		}
	}

	private static IReadOnlyList<CooldownGroup> GetActionCategoryOrder()
	{
		return new global::_003C_003Ez__ReadOnlyArray<CooldownGroup>(new CooldownGroup[5]
		{
			CooldownGroup.RaidBuff,
			CooldownGroup.Burst,
			CooldownGroup.PersonalMitigation,
			CooldownGroup.Personal,
			CooldownGroup.Common
		});
	}

	private static void DrawActionCategoryHeader(CooldownGroup group, int count, string? labelOverride = null)
	{
		DrawTargetInfoSubsection($"{labelOverride ?? GetCooldownGroupLabel(group)} ({count})");
	}

	private void DrawJobActionCategoryRow(CooldownGroup group, IReadOnlyList<JobActionCatalogEntry> actions, float iconSize, string idPrefix, IReadOnlySet<string> enabledActionKeys)
	{
		Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
		float num = Math.Max(72f + iconSize, ImGui.GetContentRegionAvail().X);
		float num2 = Math.Max(iconSize, num - 72f - 8f);
		int num3 = Math.Max(1, (int)((num2 + 7f) / (iconSize + 7f)));
		int num4 = (int)Math.Ceiling((float)actions.Count / (float)num3);
		float num5 = (float)num4 * iconSize + (float)Math.Max(0, num4 - 1) * 7f;
		DrawJobActionCategoryMarker(group, cursorScreenPos, new Vector2(72f, iconSize));
		for (int i = 0; i < actions.Count; i++)
		{
			int num6 = i / num3;
			int num7 = i % num3;
			ImGui.SetCursorScreenPos(cursorScreenPos + new Vector2(80f + (float)num7 * (iconSize + 7f), (float)num6 * (iconSize + 7f)));
			DrawJobActionIcon(actions[i], new Vector2(iconSize, iconSize), idPrefix, enabledActionKeys);
		}
		ImGui.SetCursorScreenPos(cursorScreenPos + new Vector2(0f, num5 + 7f));
	}

	private static void DrawJobActionCategoryMarker(CooldownGroup group, Vector2 min, Vector2 size)
	{
		Span<Vector4> colors = ImGui.GetStyle().Colors;
		Vector4 vector = colors[3];
		Vector4 vector2 = colors[0];
		Vector4 vector3 = colors[6];
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		Vector2 vector4 = min + size;
		Vector4 actionCategoryColor = GetActionCategoryColor(group);
		string cooldownGroupLabel = GetCooldownGroupLabel(group);
		Vector2 vector5 = ImGui.CalcTextSize(cooldownGroupLabel);
		Vector2 pos = min + (size - vector5) * 0.5f;
		windowDrawList.AddRectFilled(min + new Vector2(1f, 2f), vector4 + new Vector2(1f, 2f), ImGui.GetColorU32(new Vector4(vector3.X, vector3.Y, vector3.Z, 0.1f)), 7f);
		windowDrawList.AddRectFilled(min, vector4, ImGui.GetColorU32(new Vector4(vector.X, vector.Y, vector.Z, 0.68f)), 7f);
		windowDrawList.AddRect(min, vector4, ImGui.GetColorU32(WithAlpha(actionCategoryColor, 0.58f)), 7f, ImDrawFlags.None, 1.2f);
		windowDrawList.AddRect(min + new Vector2(2f), vector4 - new Vector2(2f), ImGui.GetColorU32(WithAlpha(actionCategoryColor, 0.16f)), 5f, ImDrawFlags.None, 1f);
		windowDrawList.AddText(pos, ImGui.GetColorU32(new Vector4(vector2.X, vector2.Y, vector2.Z, 0.98f)), cooldownGroupLabel);
	}

	private static Vector4 GetActionCategoryColor(CooldownGroup group)
	{
		return GetActionDisplayGroup(group) switch
		{
			CooldownGroup.RaidBuff => new Vector4(0.58f, 0.4f, 0.95f, 1f), 
			CooldownGroup.Burst => new Vector4(0.96f, 0.42f, 0.52f, 1f), 
			CooldownGroup.PersonalMitigation => new Vector4(0.28f, 0.58f, 0.96f, 1f), 
			CooldownGroup.Common => new Vector4(0.42f, 0.68f, 0.52f, 1f), 
			_ => new Vector4(0.82f, 0.5f, 0.62f, 1f), 
		};
	}

	private void DrawJobActionIconGrid(IReadOnlyList<JobActionCatalogEntry> actions, float iconSize, string idPrefix, IReadOnlySet<string> enabledActionKeys)
	{
		if (actions.Count == 0)
		{
			DrawSkillHintText("当前筛选条件下没有技能。");
			return;
		}
		Vector2 cellSize = new Vector2(iconSize, iconSize);
		float num = Math.Max(120f, ImGui.GetContentRegionAvail().X);
		int num2 = Math.Max(1, (int)((num + 8f) / (iconSize + 8f)));
		int num3 = 0;
		foreach (JobActionCatalogEntry action in actions)
		{
			if (num3 > 0 && num3 % num2 != 0)
			{
				ImGui.SameLine(0f, 8f);
			}
			DrawJobActionIcon(action, cellSize, idPrefix, enabledActionKeys);
			num3++;
		}
	}

	private void DrawJobActionIcon(JobActionCatalogEntry action, Vector2 cellSize, string idPrefix, IReadOnlySet<string> enabledActionKeys)
	{
		bool enabled = IsJobActionEnabled(action, enabledActionKeys);
		ImU8String strId = new ImU8String(17, 2);
		strId.AppendLiteral("job_action_icon_");
		strId.AppendFormatted(idPrefix);
		strId.AppendLiteral("_");
		strId.AppendFormatted(action.Key);
		ImGui.PushID(strId);
		try
		{
			if (ImGui.InvisibleButton("##bg", cellSize))
			{
				SetJobActionEnabled(action, !enabled);
				saveConfig();
				enabled = !enabled;
			}
			Vector2 itemRectMin = ImGui.GetItemRectMin();
			Vector2 itemRectMax = ImGui.GetItemRectMax();
			ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
			bool flag = ImGui.IsItemHovered();
			DrawActionIconBackground(windowDrawList, itemRectMin, itemRectMax, enabled, flag);
			DrawActionIconImage(action.IconId, itemRectMin, itemRectMax, enabled);
			DrawActionIconFrame(windowDrawList, itemRectMin, itemRectMax, enabled, flag, cellSize.X);
			if (flag)
			{
				DrawStyledTooltip(delegate
				{
					ImGui.TextUnformatted(action.Name);
					ImGui.Separator();
					ImU8String text = new ImU8String(3, 1);
					text.AppendLiteral("归类：");
					text.AppendFormatted(action.IsSharedAction ? "通用 / 职能" : "职业");
					ImGui.TextUnformatted(text);
					ImU8String text2 = new ImU8String(3, 1);
					text2.AppendLiteral("等级：");
					text2.AppendFormatted((action.Level == 0) ? "-" : action.Level.ToString());
					ImGui.TextUnformatted(text2);
					ImU8String text3 = new ImU8String(3, 1);
					text3.AppendLiteral("类型：");
					text3.AppendFormatted(GetCooldownGroupLabel(action.Group));
					ImGui.TextUnformatted(text3);
					ImU8String text4 = new ImU8String(3, 1);
					text4.AppendLiteral("冷却：");
					text4.AppendFormatted(FormatCooldownSeconds(action.CooldownSeconds));
					ImGui.TextUnformatted(text4);
					ImU8String text5 = new ImU8String(9, 1);
					text5.AppendLiteral("ActionId：");
					text5.AppendFormatted(action.ActionId);
					ImGui.TextUnformatted(text5);
					ImU8String text6 = new ImU8String(5, 1);
					text6.AppendLiteral("状态追踪：");
					text6.AppendFormatted(action.HasKnownStatus ? "可追状态" : "仅 CD");
					ImGui.TextUnformatted(text6);
					ImU8String text7 = new ImU8String(3, 1);
					text7.AppendLiteral("当前：");
					text7.AppendFormatted(enabled ? "已启用" : "未启用");
					ImGui.TextUnformatted(text7);
				});
			}
		}
		finally
		{
			ImGui.PopID();
		}
	}

	private static void DrawActionIconBackground(ImDrawListPtr drawList, Vector2 min, Vector2 max, bool enabled, bool hovered)
	{
		Span<Vector4> colors = ImGui.GetStyle().Colors;
		Vector4 vector = colors[3];
		Vector4 vector2 = colors[25];
		Vector4 col = (enabled ? new Vector4(vector2.X, vector2.Y, vector2.Z, hovered ? 0.88f : 0.72f) : new Vector4(vector.X, vector.Y, vector.Z, hovered ? 0.56f : 0.42f));
		drawList.AddRectFilled(min, max, ImGui.GetColorU32(col), 4f);
	}

	private static void DrawActionIconFrame(ImDrawListPtr drawList, Vector2 min, Vector2 max, bool enabled, bool hovered, float size)
	{
		Span<Vector4> colors = ImGui.GetStyle().Colors;
		Vector4 vector = colors[19];
		Vector4 vector2 = colors[5];
		float num = Math.Max(1f, size * 0.05f);
		float value = num * 0.5f;
		Vector2 pMin = min + new Vector2(value);
		Vector2 pMax = max - new Vector2(value);
		Vector4 col = (enabled ? new Vector4(vector.X, vector.Y, vector.Z, 0.96f) : (hovered ? new Vector4(vector2.X, vector2.Y, vector2.Z, 0.88f) : new Vector4(vector2.X, vector2.Y, vector2.Z, 0.7f)));
		drawList.AddRect(pMin, pMax, ImGui.GetColorU32(new Vector4(vector2.X, vector2.Y, vector2.Z, 0.85f)), 3f, ImDrawFlags.None, num + 1f);
		drawList.AddRect(pMin, pMax, ImGui.GetColorU32(col), 3f, ImDrawFlags.None, num);
	}

	private void DrawActionIconImage(uint iconId, Vector2 min, Vector2 max, bool enabled, float disabledAlpha = 0.45f)
	{
		ThemePalette effectiveTheme = GetEffectiveTheme();
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		float num = 3f;
		Vector2 vector = min + new Vector2(num, num);
		Vector2 vector2 = max - new Vector2(num, num);
		if (iconId == 0)
		{
			windowDrawList.AddRectFilled(vector, vector2, ImGui.GetColorU32(WithAlpha(effectiveTheme.SurfaceAlt, 0.9f)), 3f);
			windowDrawList.AddText(vector + new Vector2(7f, 11f), ImGui.GetColorU32(effectiveTheme.TextMuted), "?");
			return;
		}
		GameIconLookup lookup = new GameIconLookup
		{
			IconId = iconId,
			HiRes = true
		};
		if (!textureProvider.TryGetFromGameIcon(in lookup, out ISharedImmediateTexture texture) || texture == null || !texture.TryGetWrap(out IDalamudTextureWrap texture2, out Exception _) || texture2 == null)
		{
			windowDrawList.AddRectFilled(vector, vector2, ImGui.GetColorU32(WithAlpha(effectiveTheme.SurfaceAlt, 0.9f)), 3f);
			windowDrawList.AddText(vector + new Vector2(5f, 11f), ImGui.GetColorU32(effectiveTheme.TextMuted), "N/A");
			return;
		}
		Vector2 vector3 = vector2 - vector;
		Vector2 aspectFitSize = GetAspectFitSize(texture2.Size, vector3);
		Vector2 vector4 = vector + (vector3 - aspectFitSize) * 0.5f;
		Vector4 col = (enabled ? Vector4.One : new Vector4(1f, 1f, 1f, disabledAlpha));
		windowDrawList.AddImage(texture2.Handle, vector4, vector4 + aspectFitSize, Vector2.Zero, Vector2.One, ImGui.GetColorU32(col));
	}

	private static Vector2 GetAspectFitSize(Vector2 sourceSize, Vector2 bounds)
	{
		if (sourceSize.X <= 0f || sourceSize.Y <= 0f)
		{
			return bounds;
		}
		float num = Math.Min(bounds.X / sourceSize.X, bounds.Y / sourceSize.Y);
		return new Vector2(sourceSize.X * num, sourceSize.Y * num);
	}

	private List<JobActionCatalogEntry> FilterJobActions(IEnumerable<JobActionCatalogEntry> actions, IReadOnlySet<string> enabledActionKeys)
	{
		List<JobActionCatalogEntry> list = new List<JobActionCatalogEntry>();
		foreach (JobActionCatalogEntry action in actions)
		{
			if (!HiddenActionSelectionNames.Contains(action.Name.Trim()) && !TrackedActionCatalog.PartyMitigationActionIds.Contains(action.ActionId) && IsVisibleAction(action))
			{
				list.Add(action);
			}
		}
		return list;
	}

	private static string GetActionSelectionIdentity(JobActionCatalogEntry action)
	{
		uint num = (TrackedActionCatalog.FindKnownSkill(action.ClassJobId, action.ActionId) ?? ((action.ClassJobId == 0) ? TrackedActionCatalog.FindCommonSkill(action.ActionId) : null))?.Definition.ActionIds.FirstOrDefault((uint actionId) => actionId != 0) ?? 0;
		if (num != 0)
		{
			return TrackedActionCatalog.GetActionKey(action.ClassJobId, num);
		}
		return action.Key;
	}

	private static string GetActionDisplayIdentity(JobActionCatalogEntry action)
	{
		if (action.CooldownGroupId == 0 || GetActionDisplayGroup(action.Group) != CooldownGroup.Personal)
		{
			return action.Key;
		}
		return $"{action.ClassJobId}:display:{action.CooldownGroupId}";
	}

	private static JobActionCatalogEntry GetPreferredActionEntry(IGrouping<string, JobActionCatalogEntry> group)
	{
		return (from action in @group
			orderby action.Level descending, string.Equals(action.Key, @group.Key, StringComparison.Ordinal) descending
			select action).First();
	}

	private static bool IsVisibleCooldownGroup(CooldownGroup group)
	{
		CooldownGroup actionDisplayGroup = GetActionDisplayGroup(group);
		if ((uint)(actionDisplayGroup - 1) <= 1u || (uint)(actionDisplayGroup - 5) <= 1u)
		{
			return true;
		}
		return false;
	}

	private static bool IsVisibleAction(JobActionCatalogEntry action)
	{
		if (!IsVisibleCooldownGroup(action.Group))
		{
			if (action.Group == CooldownGroup.Common)
			{
				return TrackedActionCatalog.IndependentMonitorCommonActionIds.Contains(action.ActionId);
			}
			return false;
		}
		return true;
	}

	private bool IsMergedMitigationCooldownsEnabled()
	{
		if (!config.ShowPartyMitigationCooldowns && !config.ShowTargetMitigationCooldowns && !config.ShowPersonalMitigationCooldowns)
		{
			return config.ShowMitigationCooldowns;
		}
		return true;
	}

	private IReadOnlySet<string> GetEnabledActionKeySet()
	{
		return (config.EnabledJobActionKeys ?? new List<string>()).ToHashSet<string>(StringComparer.Ordinal);
	}

	private static bool IsJobActionEnabled(JobActionCatalogEntry action, IReadOnlySet<string> enabledActionKeys)
	{
		return GetSameSkillActionKeys(action).Any(enabledActionKeys.Contains);
	}

	private void SetJobActionEnabled(JobActionCatalogEntry action, bool enabled)
	{
		Configuration configuration = config;
		if (configuration.EnabledJobActionKeys == null)
		{
			List<string> list = (configuration.EnabledJobActionKeys = new List<string>());
		}
		List<string> keys = GetSameSkillActionKeys(action).ToList();
		config.EnabledJobActionKeys.RemoveAll((string existing) => keys.Contains<string>(existing, StringComparer.Ordinal));
		if (enabled)
		{
			config.EnabledJobActionKeys.Add(keys[0]);
		}
	}

	private static IEnumerable<string> GetSameSkillActionKeys(JobActionCatalogEntry action)
	{
		TrackedActionDefinition trackedActionDefinition = TrackedActionCatalog.FindKnownSkill(action.ClassJobId, action.ActionId) ?? ((action.ClassJobId == 0) ? TrackedActionCatalog.FindCommonSkill(action.ActionId) : null);
		if (trackedActionDefinition != null)
		{
			return from actionId in trackedActionDefinition.Definition.ActionIds.Where((uint actionId) => actionId != 0).Distinct()
				select TrackedActionCatalog.GetActionKey(action.ClassJobId, actionId);
		}
		return new global::_003C_003Ez__ReadOnlySingleElementList<string>(action.Key);
	}

	private static string FormatCooldownSeconds(float seconds)
	{
		if (!(seconds <= 0.05f))
		{
			return $"{seconds:0.#}s";
		}
		return "-";
	}

	private void SetJobSkillEnabled(string key, bool enabled)
	{
		Configuration configuration = config;
		if (configuration.EnabledJobSkillKeys == null)
		{
			List<string> list = (configuration.EnabledJobSkillKeys = new List<string>());
		}
		config.EnabledJobSkillKeys.RemoveAll((string existing) => string.Equals(existing, key, StringComparison.Ordinal));
		if (enabled)
		{
			config.EnabledJobSkillKeys.Add(key);
		}
	}

	private static string GetCooldownGroupLabel(CooldownGroup group)
	{
		return NormalizeCooldownGroup(group) switch
		{
			CooldownGroup.Common => "通用", 
			CooldownGroup.Personal => "技能", 
			CooldownGroup.Burst => "爆发", 
			CooldownGroup.PartyMitigation => "团队减伤", 
			CooldownGroup.PersonalMitigation => "单体减伤", 
			CooldownGroup.RaidBuff => "团辅", 
			CooldownGroup.Mitigation => "减伤", 
			_ => "其他", 
		};
	}

	private static CooldownGroup NormalizeCooldownGroup(CooldownGroup group)
	{
		if (group != CooldownGroup.TargetMitigation)
		{
			return group;
		}
		return CooldownGroup.PartyMitigation;
	}

	private static CooldownGroup GetActionDisplayGroup(CooldownGroup group)
	{
		CooldownGroup cooldownGroup = NormalizeCooldownGroup(group);
		switch (cooldownGroup)
		{
		case CooldownGroup.PartyMitigation:
			return CooldownGroup.PartyMitigation;
		case CooldownGroup.PersonalMitigation:
		case CooldownGroup.Mitigation:
			return CooldownGroup.PersonalMitigation;
		default:
			return cooldownGroup;
		}
	}

	private static void DrawSkillHintText(string text)
	{
		Vector4 vector = ImGui.GetStyle().Colors[1];
		ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(vector.X, vector.Y, vector.Z, 0.82f));
		ImGui.TextUnformatted(text);
		ImGui.PopStyleColor();
	}

	private void DrawTargetInfoPage()
	{
		DrawSectionCard("目标情报", delegate
		{
			DrawCheckbox("启动目标情报", "ShowCustomTargetInfo", config.ShowCustomTargetInfo, delegate(bool value)
			{
				config.ShowCustomTargetInfo = value;
			});
			ImGui.SameLine(0f, 10f);
			DrawCheckbox("锁定窗口", "TargetInfoLocked", config.TargetInfoLocked, delegate(bool value)
			{
				config.TargetInfoLocked = value;
			});
			DrawTargetInfoSubsection("生命值");
			DrawHudScaleCombo("整体缩放", config.CustomTargetInfoScale, delegate(float value)
			{
				config.CustomTargetInfoScale = value;
			});
			DrawCheckbox("隐藏血量数字", "CustomTargetInfoHideHpNumbers", config.CustomTargetInfoHideHpNumbers, delegate(bool value)
			{
				config.CustomTargetInfoHideHpNumbers = value;
			});
			if (!config.CustomTargetInfoHideHpNumbers)
			{
				DrawCheckbox("不显示总血量", "CustomTargetInfoHideMaxHp", config.CustomTargetInfoHideMaxHp, delegate(bool value)
				{
					config.CustomTargetInfoHideMaxHp = value;
				});
			}
			DrawTargetInfoSubsection("状态栏");
			DrawCheckbox("拆分状态栏", "CustomTargetInfoSplitStatusBar", config.CustomTargetInfoSplitStatusBar, delegate(bool value)
			{
				config.CustomTargetInfoSplitStatusBar = value;
			});
			if (config.CustomTargetInfoSplitStatusBar)
			{
				DrawHudScaleCombo("状态栏缩放", config.CustomTargetInfoStatusBarScale, delegate(float value)
				{
					config.CustomTargetInfoStatusBarScale = value;
				});
			}
			DrawCheckbox("状态栏在生命值上方", "CustomTargetInfoStatusesAboveHp", config.CustomTargetInfoStatusesAboveHp, delegate(bool value)
			{
				config.CustomTargetInfoStatusesAboveHp = value;
			});
			DrawCheckbox("目标状态仅显示我施加的", "OnlyShowSelfAppliedTargetStatuses", config.OnlyShowSelfAppliedTargetStatuses, delegate(bool value)
			{
				config.OnlyShowSelfAppliedTargetStatuses = value;
			});
			int currentValue = Math.Clamp(config.CustomTargetInfoStatusRows, 1, 2);
			DrawSegmentedSelector("状态排列", "target_status_rows", currentValue, delegate(int value)
			{
				config.CustomTargetInfoStatusRows = value;
			}, ("单排", 1), ("双排", 2));
			DrawTargetInfoSubsection("咏唱栏");
			DrawCheckbox("拆分咏唱栏", "CustomTargetInfoSplitCastBar", config.CustomTargetInfoSplitCastBar, delegate(bool value)
			{
				config.CustomTargetInfoSplitCastBar = value;
			});
			if (config.CustomTargetInfoSplitCastBar)
			{
				DrawHudScaleCombo("咏唱栏缩放", config.CustomTargetInfoCastBarScale, delegate(float value)
				{
					config.CustomTargetInfoCastBarScale = value;
				});
			}
			else
			{
				DrawCastBarPlacementSelector();
			}
		});
	}

	private void DrawStatusBarPage()
	{
		DrawSectionCard("状态栏", delegate
		{
			DrawCheckbox("启动状态栏", "ShowStatusOverlay", config.ShowStatusOverlay, delegate(bool value)
			{
				config.ShowStatusOverlay = value;
			});
			ImGui.SameLine(0f, 10f);
			DrawCheckbox("锁定窗口", "StatusBarLocked", config.StatusBarLocked, delegate(bool value)
			{
				config.StatusBarLocked = value;
			});
			DrawSegmentedSelector("布局模式", "status_bar_layout_mode", Math.Clamp(config.StatusBarLayoutMode, 0, 1), delegate(int value)
			{
				config.StatusBarLayoutMode = value;
			}, ("合并", 0), ("拆分", 1));
			DrawTargetInfoSubsection("内容");
			DrawCheckbox("显示弱化状态", "ShowSelfEnfeeblements", config.ShowSelfEnfeeblements, delegate(bool value)
			{
				config.ShowSelfEnfeeblements = value;
			});
			DrawCheckbox("显示其他状态（食物 / 部队等）", "ShowSelfOtherStatuses", config.ShowSelfOtherStatuses, delegate(bool value)
			{
				config.ShowSelfOtherStatuses = value;
			});
			DrawCheckbox("显示强化状态", "ShowSelfBuffs", config.ShowSelfBuffs, delegate(bool value)
			{
				config.ShowSelfBuffs = value;
			});
			DrawTargetInfoSubsection("来源");
			DrawCheckbox("来源显示职业名", "ShowSourceJobNames", config.ShowSourceJobNames, delegate(bool value)
			{
				config.ShowSourceJobNames = value;
			});
		});
	}

	private void DrawCastBarPlacementSelector()
	{
		int currentValue = Math.Clamp(config.CustomTargetInfoCastBarPlacement, 0, 2);
		DrawSegmentedSelector("咏唱栏位置", "cast_bar_placement", currentValue, delegate(int value)
		{
			config.CustomTargetInfoCastBarPlacement = value;
		}, ("侧边", 0), ("顶部", 1), ("底部", 2));
	}

	private void DrawPartyInfoPage()
	{
		DrawSectionCard("队伍信息", delegate
		{
			DrawCheckbox("启动队伍信息", "ShowPartyInfo", config.ShowPartyInfo, delegate(bool value)
			{
				config.ShowPartyInfo = value;
			});
			ImGui.TextDisabled("队伍信息会贴在原生队伍列表旁显示，仅副本中启用。");
			DrawTargetInfoSubsection("内容");
			DrawCheckbox("显示减伤", "ShowMergedMitigationCooldowns", IsMergedMitigationCooldownsEnabled(), delegate(bool value)
			{
				config.ShowPartyMitigationCooldowns = value;
				config.ShowTargetMitigationCooldowns = value;
				config.ShowPersonalMitigationCooldowns = value;
				config.ShowMitigationCooldowns = value;
			});
			DrawCheckbox("显示食物检查", "ShowPartyFoodCheck", config.ShowPartyFoodCheck, delegate(bool value)
			{
				config.ShowPartyFoodCheck = value;
			});
			DrawCheckbox("显示极限技槽", "ShowPartyLimitBreakBar", config.ShowPartyLimitBreakBar, delegate(bool value)
			{
				config.ShowPartyLimitBreakBar = value;
			});
			if (config.ShowPartyLimitBreakBar)
			{
				DrawLimitBreakPositionSelector();
			}
			DrawCheckbox("隐藏已结束的队伍冷却", "HideExpiredCooldowns", config.HideExpiredCooldowns, delegate(bool value)
			{
				config.HideExpiredCooldowns = value;
			});
		});
	}

	private void DrawTaskBarPage()
	{
		DrawTaskBarPageTabs();
		ImGui.Spacing();
		switch (selectedTaskBarPage)
		{
		case TaskBarPage.任务栏:
			DrawTaskBarBasicsPage();
			break;
		case TaskBarPage.辅助栏:
			DrawAuxiliaryBarPage();
			break;
		case TaskBarPage.插件收纳:
			DrawTaskBarPluginDockPage();
			break;
		case TaskBarPage.高级:
			DrawTaskBarAdvancedPage();
			break;
		}
	}

	private void DrawTaskBarBasicsPage()
	{
		DrawTargetInfoSubsection("主栏设置");
		DrawCheckbox("启动主栏", "ShowTaskBar", config.ShowTaskBar, delegate(bool value)
		{
			config.ShowTaskBar = value;
		});
		if (config.ShowTaskBar)
		{
			DrawTaskBarEdgeSelector();
			DrawInlineSegmentedSelector("宽度", "task_bar_width", config.TaskBarStretchToEdges ? 1 : 0, delegate(int value)
			{
				SetTaskBarStretchToEdges(value == 1);
			}, ("自适应", 0), ("铺满", 1));
			DrawInlineHudScaleCombo("缩放", "TaskBarScale", config.TaskBarScale, delegate(float value)
			{
				config.TaskBarScale = value;
			});
			ImGui.SameLine(0f, 10f);
			DrawTaskBarOpacitySlider();
			if (!config.TaskBarStretchToEdges)
			{
				ImGui.SameLine(0f, 10f);
				DrawTaskBarHorizontalOffsetSlider();
			}
			ImGui.Spacing();
			DrawTaskBarComponentsPage();
		}
	}

	private void DrawAuxiliaryBarPage()
	{
		DrawTargetInfoSubsection("辅助栏设置");
		Configuration configuration = config;
		if (configuration.AuxiliaryBars == null)
		{
			int num = 1;
			List<AuxiliaryBarDefinition> list = new List<AuxiliaryBarDefinition>(num);
			CollectionsMarshal.SetCount(list, num);
			CollectionsMarshal.AsSpan(list)[0] = new AuxiliaryBarDefinition();
			List<AuxiliaryBarDefinition> list2 = list;
			configuration.AuxiliaryBars = list;
		}
		if (config.AuxiliaryBars.Count == 0)
		{
			config.AuxiliaryBars.Add(new AuxiliaryBarDefinition());
		}
		selectedAuxiliaryBarIndex = Math.Clamp(selectedAuxiliaryBarIndex, 0, config.AuxiliaryBars.Count - 1);
		DrawAuxiliaryBarTabs();
		ImGui.Spacing();
		DrawAuxiliaryBarEditor(config.AuxiliaryBars[selectedAuxiliaryBarIndex], selectedAuxiliaryBarIndex);
		ImGui.Spacing();
		DrawTargetInfoSubsection("辅助栏组件");
		DrawAuxiliaryBarComponentsPage(config.AuxiliaryBars[selectedAuxiliaryBarIndex], selectedAuxiliaryBarIndex);
	}

	private void DrawAuxiliaryBarTabs()
	{
		ThemePalette effectiveTheme = GetEffectiveTheme();
		Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
		for (int i = 0; i < config.AuxiliaryBars.Count; i++)
		{
			if (i > 0)
			{
				ImGui.SameLine(0f, 2f);
			}
			DrawAuxiliaryBarTab(config.AuxiliaryBars[i], i);
		}
		ImGui.SameLine(0f, 2f);
		if (DrawAuxiliaryBarAddTab())
		{
			config.AuxiliaryBars.Add(new AuxiliaryBarDefinition
			{
				Name = "辅助栏"
			});
			selectedAuxiliaryBarIndex = config.AuxiliaryBars.Count - 1;
			SyncLegacyAuxiliaryBarSettings();
			saveConfig();
		}
		float y = cursorScreenPos.Y + 27f;
		(float, float) sectionCardLineBounds = GetSectionCardLineBounds();
		ImGui.GetWindowDrawList().AddLine(new Vector2(sectionCardLineBounds.Item1, y), new Vector2(sectionCardLineBounds.Item2, y), ImGui.GetColorU32(WithAlpha(effectiveTheme.Border, 0.34f)), 1f);
	}

	private void DrawAuxiliaryBarTab(AuxiliaryBarDefinition bar, int index)
	{
		ThemePalette effectiveTheme = GetEffectiveTheme();
		bool flag = selectedAuxiliaryBarIndex == index;
		string auxiliaryBarDisplayName = GetAuxiliaryBarDisplayName(bar, index);
		Vector4 pageAccentColor = GetPageAccentColor(ConfigPage.任务栏);
		bool flag2 = config.AuxiliaryBars.Count > 1;
		Vector2 vector = ImGui.CalcTextSize(auxiliaryBarDisplayName);
		Vector2 vector2 = new Vector2(Math.Clamp(vector.X + (flag2 ? 40f : 22f), 72f, 138f), 28f);
		Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
		Vector2 pMax = cursorScreenPos + vector2;
		Vector2 vector3 = new Vector2(16f, 16f);
		Vector2 vector4 = new Vector2(pMax.X - vector3.X - 5f, cursorScreenPos.Y + 6f);
		Vector2 rMax = vector4 + vector3;
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		ImU8String strId = new ImU8String(18, 1);
		strId.AppendLiteral("auxiliary_bar_tab_");
		strId.AppendFormatted(index);
		ImGui.InvisibleButton(strId, vector2);
		bool flag3 = ImGui.IsItemHovered();
		if (ImGui.IsItemClicked())
		{
			if (ImGui.GetIO().KeyCtrl)
			{
				selectedAuxiliaryBarIndex = index;
				ImGui.OpenPopup("RenameAuxiliaryBar");
			}
			else
			{
				selectedAuxiliaryBarIndex = index;
			}
		}
		Vector4 col = (flag ? WithAlpha(effectiveTheme.SurfaceAlt, 0.96f) : (flag3 ? WithAlpha(effectiveTheme.HeaderHovered, 0.82f) : WithAlpha(effectiveTheme.Surface, 0.58f)));
		Vector4 col2 = (flag ? WithAlpha(pageAccentColor, 0.62f) : WithAlpha(pageAccentColor, flag3 ? 0.36f : 0.22f));
		Vector4 col3 = (flag ? WithAlpha(pageAccentColor, 1f) : WithAlpha(effectiveTheme.TextMuted, 0.92f));
		windowDrawList.AddRectFilled(cursorScreenPos, pMax, ImGui.GetColorU32(col), 5f, ImDrawFlags.RoundCornersTop);
		windowDrawList.AddRect(cursorScreenPos, pMax, ImGui.GetColorU32(col2), 5f, ImDrawFlags.RoundCornersTop, flag ? 1.2f : 1f);
		if (flag)
		{
			windowDrawList.AddLine(new Vector2(cursorScreenPos.X + 1f, pMax.Y), new Vector2(pMax.X - 1f, pMax.Y), ImGui.GetColorU32(effectiveTheme.SurfaceAlt), 2f);
		}
		float x = (flag2 ? (cursorScreenPos.X + 11f) : (cursorScreenPos.X + (vector2.X - vector.X) * 0.5f));
		windowDrawList.AddText(new Vector2(x, cursorScreenPos.Y + (vector2.Y - vector.Y) * 0.5f), ImGui.GetColorU32(col3), auxiliaryBarDisplayName);
		if (flag2)
		{
			bool num = ImGui.IsMouseHoveringRect(vector4, rMax);
			windowDrawList.AddText(col: ImGui.GetColorU32(num ? WithAlpha(effectiveTheme.Accent, 1f) : WithAlpha(effectiveTheme.TextMuted, 0.74f)), pos: vector4 + new Vector2(3f, -1f), text: "×");
			if (num && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
			{
				config.AuxiliaryBars.RemoveAt(index);
				selectedAuxiliaryBarIndex = Math.Clamp(selectedAuxiliaryBarIndex, 0, config.AuxiliaryBars.Count - 1);
				SyncLegacyAuxiliaryBarSettings();
				saveConfig();
			}
		}
		if (selectedAuxiliaryBarIndex == index)
		{
			DrawAuxiliaryBarRenamePopup(bar, index);
		}
	}

	private bool DrawAuxiliaryBarAddTab()
	{
		ThemePalette effectiveTheme = GetEffectiveTheme();
		Vector4 pageAccentColor = GetPageAccentColor(ConfigPage.任务栏);
		Vector2 vector = ImGui.CalcTextSize("+");
		Vector2 vector2 = new Vector2(30f, 28f);
		Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
		Vector2 pMax = cursorScreenPos + vector2;
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		ImGui.InvisibleButton("auxiliary_bar_add_tab", vector2);
		bool flag = ImGui.IsItemHovered();
		bool num = ImGui.IsItemActive();
		ImGui.IsItemClicked();
		Vector4 col = (num ? WithAlpha(pageAccentColor, 0.22f) : (flag ? WithAlpha(pageAccentColor, 0.14f) : WithAlpha(effectiveTheme.Surface, 0.48f)));
		windowDrawList.AddRectFilled(cursorScreenPos, pMax, ImGui.GetColorU32(col), 5f, ImDrawFlags.RoundCornersTop);
		windowDrawList.AddRect(cursorScreenPos, pMax, ImGui.GetColorU32(WithAlpha(pageAccentColor, flag ? 0.4f : 0.22f)), 5f, ImDrawFlags.RoundCornersTop);
		windowDrawList.AddText(cursorScreenPos + (vector2 - vector) * 0.5f, ImGui.GetColorU32(WithAlpha(effectiveTheme.Text, 0.94f)), "+");
		return ImGui.IsItemClicked();
	}

	private void DrawAuxiliaryBarEditor(AuxiliaryBarDefinition bar, int index)
	{
		ThemePalette effectiveTheme = GetEffectiveTheme();
		ImU8String strId = new ImU8String(19, 1);
		strId.AppendLiteral("AuxiliaryBarEditor_");
		strId.AppendFormatted(index);
		ImGui.PushID(strId);
		bool flag = bar.Enabled && bar.PositionMode != 2 && !bar.StretchToEdges;
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
		float x = Math.Max(360f, ImGui.GetContentRegionAvail().X - 8f);
		float y = ((!bar.Enabled) ? 42f : (((bar.PositionMode == 2) | flag) ? 154f : 126f));
		Vector2 pMax = cursorScreenPos + new Vector2(x, y);
		windowDrawList.AddRectFilled(cursorScreenPos, pMax, ImGui.GetColorU32(WithAlpha(effectiveTheme.Surface, 0.72f)), 8f);
		windowDrawList.AddRect(cursorScreenPos, pMax, ImGui.GetColorU32(WithAlpha(effectiveTheme.Border, 0.34f)), 8f);
		ImGui.SetCursorScreenPos(cursorScreenPos + new Vector2(12f, 9f));
		DrawCheckbox("启用", "Enabled", bar.Enabled, delegate(bool value)
		{
			bar.Enabled = value;
			SyncLegacyAuxiliaryBarSettings();
		});
		ImGui.SameLine(0f, 12f);
		ImGui.AlignTextToFramePadding();
		ImGui.TextDisabled("Ctrl + 点击标签改名");
		if (bar.Enabled)
		{
			ImGui.SetCursorScreenPos(cursorScreenPos + new Vector2(12f, 40f));
			DrawAuxiliaryBarPositionSelector(bar, index);
			ImGui.SetCursorScreenPos(cursorScreenPos + new Vector2(12f, 68f));
			DrawInlineSegmentedSelector((bar.PositionMode == 2) ? "尺寸" : "高度", $"auxiliary_bar_height_{index}", bar.StretchToEdges ? 1 : 0, delegate(int value)
			{
				bar.StretchToEdges = value == 1;
			}, ("自适应", 0), ("铺满", 1));
			if (bar.PositionMode == 2)
			{
				ImGui.SameLine(0f, 10f);
				DrawInlineSegmentedSelector("方向", $"auxiliary_bar_direction_{index}", bar.LayoutDirection, delegate(int value)
				{
					bar.LayoutDirection = value;
				}, ("竖向", 0), ("横向", 1));
			}
			if (bar.PositionMode == 2)
			{
				ImGui.SetCursorScreenPos(cursorScreenPos + new Vector2(12f, 96f));
				ImGui.TextDisabled("自定义位置：拖动辅助栏调整位置");
			}
			else if (flag)
			{
				ImGui.SetCursorScreenPos(cursorScreenPos + new Vector2(12f, 96f));
				DrawAuxiliaryBarVerticalOffsetSlider(bar, index);
			}
			ImGui.SetCursorScreenPos(cursorScreenPos + new Vector2(12f, ((bar.PositionMode == 2) | flag) ? 124f : 96f));
			DrawInlineHudScaleCombo("缩放", $"AuxiliaryBarScale{index}", bar.Scale, delegate(float value)
			{
				bar.Scale = value;
				SyncLegacyAuxiliaryBarSettings();
			});
			ImGui.SameLine(0f, 10f);
			DrawAuxiliaryBarOpacitySlider(bar, index);
		}
		ImGui.SetCursorScreenPos(new Vector2(cursorScreenPos.X, pMax.Y + 4f));
		ImGui.PopID();
	}

	private void DrawAuxiliaryBarRenamePopup(AuxiliaryBarDefinition bar, int index)
	{
		if (ImGui.BeginPopup("RenameAuxiliaryBar"))
		{
			byte[] array = CreateUtf8Buffer(GetAuxiliaryBarDisplayName(bar, index), 96);
			ImGui.SetNextItemWidth(180f);
			if (ImGui.InputText("##AuxiliaryBarRename", array))
			{
				string text = ReadUtf8Buffer(array);
				bar.Name = (string.IsNullOrWhiteSpace(text) ? "辅助栏" : text);
				SyncLegacyAuxiliaryBarSettings();
				saveConfig();
			}
			if (ImGui.Button("确定"))
			{
				string text2 = ReadUtf8Buffer(array);
				bar.Name = (string.IsNullOrWhiteSpace(text2) ? "辅助栏" : text2);
				SyncLegacyAuxiliaryBarSettings();
				saveConfig();
				ImGui.CloseCurrentPopup();
			}
			ImGui.SameLine();
			if (ImGui.Button("取消"))
			{
				ImGui.CloseCurrentPopup();
			}
			ImGui.EndPopup();
		}
	}

	private static string GetAuxiliaryBarDisplayName(AuxiliaryBarDefinition bar, int index)
	{
		if (!string.IsNullOrWhiteSpace(bar.Name))
		{
			return bar.Name.Trim();
		}
		return "辅助栏";
	}

	private void DrawAuxiliaryBarComponentsPage(AuxiliaryBarDefinition bar, int barIndex)
	{
		List<string> selectedAuxiliaryComponentOrder = GetSelectedAuxiliaryComponentOrder(bar, barIndex);
		if (selectedAuxiliaryComponentOrder == null || !selectedAuxiliaryComponentOrder.Any((string id) => id.Equals(selectedAuxiliaryComponentSettingsId, StringComparison.OrdinalIgnoreCase)))
		{
			ClearSelectedAuxiliaryComponentSettings();
		}
		ImGui.BeginChild("AuxiliaryComponentList", new Vector2(0f, 0f));
		if (bar.StretchToEdges)
		{
			bool flag = IsAuxiliaryBarHorizontal(bar);
			DrawAuxiliaryBarComponentSection(bar, barIndex, flag ? "左侧" : "上方", bar.SectionStartComponentOrder, "start");
			ImGui.Spacing();
			DrawAuxiliaryBarComponentSection(bar, barIndex, "中间", bar.SectionCenterComponentOrder, "center");
			ImGui.Spacing();
			DrawAuxiliaryBarComponentSection(bar, barIndex, flag ? "右侧" : "下方", bar.SectionEndComponentOrder, "end");
			ImGui.EndChild();
			return;
		}
		bar.ComponentOrder = Configuration.NormalizeAuxiliaryComponentOrder(bar.ComponentOrder);
		List<TaskBarComponentDefinition> list = (from component in bar.ComponentOrder.Where(IsPlacedComponentId).Select(GetComponentDefinition)
			where !string.IsNullOrWhiteSpace(component.Id)
			select component).ToList();
		if (list.Count == 0)
		{
			ImGui.TextDisabled("还没有添加组件。点击添加组件开始配置辅助栏。");
		}
		for (int num = 0; num < list.Count; num++)
		{
			DrawAuxiliaryBarComponentRow(bar, barIndex, list[num], bar.ComponentOrder, "main");
		}
		DrawAuxiliaryBarDraggedComponentPreview(list, $"aux:{barIndex}:main:");
		ImGui.Spacing();
		if (ImGui.Button("添加组件"))
		{
			ImU8String strId = new ImU8String(33, 1);
			strId.AppendLiteral("AllHud_AuxiliaryBar_AddComponent_");
			strId.AppendFormatted(barIndex);
			ImGui.OpenPopup(strId);
		}
		DrawAuxiliaryBarAddComponentPopup(bar, barIndex, bar.ComponentOrder, $"AllHud_AuxiliaryBar_AddComponent_{barIndex}", "添加到辅助栏");
		ImGui.EndChild();
	}

	private void DrawAuxiliaryBarComponentSection(AuxiliaryBarDefinition bar, int barIndex, string label, List<string> componentOrder, string sectionKey)
	{
		componentOrder = NormalizeAuxiliarySectionOrderReference(bar, componentOrder, sectionKey);
		DrawTargetInfoSubsection(label);
		List<TaskBarComponentDefinition> list = (from component in componentOrder.Where(IsPlacedComponentId).Select(GetComponentDefinition)
			where !string.IsNullOrWhiteSpace(component.Id)
			select component).ToList();
		if (list.Count == 0)
		{
			ImGui.TextDisabled("还没有添加组件。");
		}
		foreach (TaskBarComponentDefinition item in list)
		{
			DrawAuxiliaryBarComponentRow(bar, barIndex, item, componentOrder, sectionKey);
		}
		DrawAuxiliaryBarDraggedComponentPreview(list, $"aux:{barIndex}:{sectionKey}:");
		ImGui.Spacing();
		string text = $"AllHud_AuxiliaryBar_AddComponent_{barIndex}_{sectionKey}";
		ImU8String label2 = new ImU8String(11, 2);
		label2.AppendLiteral("添加组件##aux_");
		label2.AppendFormatted(barIndex);
		label2.AppendLiteral("_");
		label2.AppendFormatted(sectionKey);
		if (ImGui.Button(label2))
		{
			ImGui.OpenPopup(text);
		}
		DrawAuxiliaryBarAddComponentPopup(bar, barIndex, componentOrder, text, "添加到" + label);
	}

	private static bool IsAuxiliaryBarHorizontal(AuxiliaryBarDefinition bar)
	{
		if (bar.PositionMode == 2)
		{
			return bar.LayoutDirection == 1;
		}
		return false;
	}

	private static List<string> NormalizeAuxiliarySectionOrderReference(AuxiliaryBarDefinition bar, List<string> componentOrder, string sectionKey)
	{
		List<string> list = Configuration.NormalizeAuxiliaryComponentOrder(componentOrder);
		if (!(sectionKey == "start"))
		{
			if (sectionKey == "end")
			{
				bar.SectionEndComponentOrder = list;
				return bar.SectionEndComponentOrder;
			}
			bar.SectionCenterComponentOrder = list;
			return bar.SectionCenterComponentOrder;
		}
		bar.SectionStartComponentOrder = list;
		return bar.SectionStartComponentOrder;
	}

	private void DrawAuxiliaryBarComponentRow(AuxiliaryBarDefinition bar, int barIndex, TaskBarComponentDefinition component, List<string> componentOrder, string sectionKey)
	{
		ThemePalette effectiveTheme = GetEffectiveTheme();
		ImU8String strId = new ImU8String(24, 3);
		strId.AppendLiteral("AuxiliaryBarComponent_");
		strId.AppendFormatted(barIndex);
		strId.AppendLiteral("_");
		strId.AppendFormatted(sectionKey);
		strId.AppendLiteral("_");
		strId.AppendFormatted(component.Id);
		ImGui.PushID(strId);
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
		float num = Math.Max(260f, ImGui.GetContentRegionAvail().X - 8f);
		Vector2 vector = cursorScreenPos + new Vector2(num, 40f);
		bool flag = ImGui.IsMouseHoveringRect(cursorScreenPos, vector);
		string value = $"aux:{barIndex}:{sectionKey}:{component.Id}";
		bool flag2 = draggingTaskBarComponentId.Equals(value, StringComparison.OrdinalIgnoreCase);
		bool num2 = ComponentHasSettings(component.Id);
		bool flag3 = num2 && selectedAuxiliaryComponentSettingsId.Equals(component.Id, StringComparison.OrdinalIgnoreCase) && selectedAuxiliaryComponentSettingsScope.Equals(sectionKey, StringComparison.OrdinalIgnoreCase) && selectedAuxiliaryComponentSettingsBarIndex == barIndex;
		Vector4 col = (flag2 ? WithAlpha(effectiveTheme.HeaderActive, 0.72f) : (flag3 ? WithAlpha(effectiveTheme.HeaderActive, 0.96f) : (flag ? WithAlpha(effectiveTheme.HeaderHovered, 0.94f) : WithAlpha(effectiveTheme.Surface, 0.82f))));
		Vector4 col2 = (flag2 ? WithAlpha(effectiveTheme.Accent, 0.72f) : (flag3 ? WithAlpha(effectiveTheme.Accent, 0.78f) : WithAlpha(effectiveTheme.Border, 0.46f)));
		windowDrawList.ChannelsSplit(2);
		windowDrawList.ChannelsSetCurrent(1);
		ImGui.SetCursorScreenPos(cursorScreenPos);
		bool flag4 = ImGui.InvisibleButton("aux_row_drag_area", new Vector2(Math.Max(48f, num - 146f), 40f));
		bool flag5 = ImGui.IsItemHovered();
		if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
		{
			draggingTaskBarComponentId = value;
		}
		if ((num2 & flag4) && !flag2)
		{
			ToggleSelectedAuxiliaryComponentSettings(component.Id, barIndex, sectionKey);
		}
		if (!ImGui.IsMouseDown(ImGuiMouseButton.Left) & flag2)
		{
			draggingTaskBarComponentId = string.Empty;
		}
		if (((!string.IsNullOrWhiteSpace(draggingTaskBarComponentId) && draggingTaskBarComponentId.StartsWith($"aux:{barIndex}:{sectionKey}:", StringComparison.OrdinalIgnoreCase) && !flag2) & flag) && ImGui.IsMouseDown(ImGuiMouseButton.Left))
		{
			string auxiliaryDragComponentId = GetAuxiliaryDragComponentId(draggingTaskBarComponentId, barIndex, sectionKey);
			bool flag6 = ImGui.GetMousePos().Y > cursorScreenPos.Y + 20f;
			float y = (flag6 ? (vector.Y - 1f) : (cursorScreenPos.Y + 1f));
			windowDrawList.AddLine(new Vector2(cursorScreenPos.X + 10f, y), new Vector2(vector.X - 10f, y), ImGui.GetColorU32(WithAlpha(effectiveTheme.Accent, 0.82f)), 2f);
			MoveAuxiliaryBarComponentRelativeTo(componentOrder, auxiliaryDragComponentId, component.Id, flag6);
		}
		DrawTaskBarComponentIcon(color: WithAlpha((flag5 | flag2) ? effectiveTheme.Accent : effectiveTheme.TextMuted, (flag5 | flag2) ? 0.95f : 0.7f), drawList: windowDrawList, componentId: component.Id, center: cursorScreenPos + new Vector2(25f, 20f));
		Vector2 pos = cursorScreenPos + new Vector2(48f, 11f);
		windowDrawList.AddText(pos, ImGui.GetColorU32(effectiveTheme.Text), component.Name);
		float x = pos.X + ImGui.CalcTextSize(component.Name).X + 14f;
		windowDrawList.AddText(new Vector2(x, pos.Y), ImGui.GetColorU32(effectiveTheme.TextMuted), component.Description);
		Vector2 center = new Vector2(vector.X - 20f, cursorScreenPos.Y + 20f);
		if (DrawHeaderRemoveComponentButton(min: new Vector2(vector.X - 132f, cursorScreenPos.Y + 7f), label: $"移除##AuxRemove_{barIndex}_{sectionKey}_{component.Id}"))
		{
			RemoveAuxiliaryComponent(componentOrder, component.Id);
			flag3 = false;
		}
		if (num2)
		{
			if (flag3)
			{
				DrawChevronDownGlyph(windowDrawList, center, 8f, ImGui.GetColorU32(WithAlpha(effectiveTheme.Accent, 0.95f)));
			}
			else
			{
				DrawSettingsDotGlyph(windowDrawList, center, 7f, ImGui.GetColorU32(WithAlpha(effectiveTheme.TextMuted, flag ? 0.8f : 0.42f)));
			}
		}
		ImGui.SetCursorScreenPos(cursorScreenPos + new Vector2(0f, 46f));
		if (flag3)
		{
			DrawExpandedComponentSettingsContent(component, num);
		}
		Vector2 pMax = (flag3 ? new Vector2(cursorScreenPos.X + num, ImGui.GetCursorScreenPos().Y - 6f) : vector);
		windowDrawList.ChannelsSetCurrent(0);
		windowDrawList.AddRectFilled(cursorScreenPos, pMax, ImGui.GetColorU32(col), 7f);
		windowDrawList.AddRect(cursorScreenPos, pMax, ImGui.GetColorU32(col2), 7f);
		windowDrawList.ChannelsMerge();
		ImGui.PopID();
	}

	private void DrawAuxiliaryBarDraggedComponentPreview(IReadOnlyList<TaskBarComponentDefinition> activeComponents, string dragPrefix = "aux:")
	{
		ThemePalette effectiveTheme = GetEffectiveTheme();
		if (!string.IsNullOrWhiteSpace(draggingTaskBarComponentId) && draggingTaskBarComponentId.StartsWith(dragPrefix, StringComparison.OrdinalIgnoreCase) && ImGui.IsMouseDown(ImGuiMouseButton.Left))
		{
			string componentId = draggingTaskBarComponentId.Substring(dragPrefix.Length);
			TaskBarComponentDefinition taskBarComponentDefinition = activeComponents.FirstOrDefault((TaskBarComponentDefinition item) => item.Id.Equals(componentId, StringComparison.OrdinalIgnoreCase));
			if (!string.IsNullOrWhiteSpace(taskBarComponentDefinition.Id))
			{
				ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
				float x = Math.Max(260f, ImGui.GetContentRegionAvail().X - 8f);
				Vector2 mousePos = ImGui.GetMousePos();
				Vector2 vector = new Vector2(ImGui.GetCursorScreenPos().X, mousePos.Y - 20f);
				Vector2 vector2 = vector + new Vector2(x, 40f);
				windowDrawList.AddRectFilled(vector + new Vector2(0f, 2f), vector2 + new Vector2(0f, 2f), ImGui.GetColorU32(WithAlpha(effectiveTheme.TextShadow, 0.18f)), 7f);
				windowDrawList.AddRectFilled(vector, vector2, ImGui.GetColorU32(WithAlpha(effectiveTheme.SurfaceAlt, 0.92f)), 7f);
				windowDrawList.AddRect(vector, vector2, ImGui.GetColorU32(WithAlpha(effectiveTheme.Accent, 0.88f)), 7f, ImDrawFlags.None, 1.4f);
				DrawTaskBarComponentIcon(windowDrawList, taskBarComponentDefinition.Id, vector + new Vector2(25f, 20f), WithAlpha(effectiveTheme.Accent, 0.95f));
				windowDrawList.AddText(vector + new Vector2(48f, 11f), ImGui.GetColorU32(WithAlpha(effectiveTheme.Text, 0.98f)), taskBarComponentDefinition.Name);
			}
		}
	}

	private void DrawAuxiliaryBarAddComponentPopup(AuxiliaryBarDefinition bar, int barIndex, List<string> componentOrder, string popupId, string title)
	{
		if (!ImGui.BeginPopup(popupId))
		{
			return;
		}
		ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12f, 10f));
		ImGui.TextDisabled(title);
		ImGui.Dummy(new Vector2(1f, 4f));
		float y = Math.Min(440f, Math.Max(220f, ImGui.GetIO().DisplaySize.Y - ImGui.GetCursorScreenPos().Y - 48f));
		ImU8String strId = new ImU8String(16, 1);
		strId.AppendLiteral("##");
		strId.AppendFormatted(popupId);
		strId.AppendLiteral("_ComponentList");
		ImGui.BeginChild(strId, new Vector2(310f, y));
		TaskBarComponentDefinition[] taskBarComponentDefinitions = TaskBarComponentDefinitions;
		for (int i = 0; i < taskBarComponentDefinitions.Length; i++)
		{
			TaskBarComponentDefinition component = taskBarComponentDefinitions[i];
			if ((Configuration.IsRepeatableComponentId(component.Id) || !AuxiliaryBarContainsComponent(bar, component.Id)) && DrawAddComponentCard(component))
			{
				componentOrder.Add(CreateComponentInstanceId(component.Id));
				List<string> collection = Configuration.NormalizeAuxiliaryComponentOrder(componentOrder);
				componentOrder.Clear();
				componentOrder.AddRange(collection);
				saveConfig();
				ImGui.CloseCurrentPopup();
			}
		}
		ImGui.EndChild();
		if (TaskBarComponentDefinitions.Where((TaskBarComponentDefinition taskBarComponentDefinition) => !Configuration.IsRepeatableComponentId(taskBarComponentDefinition.Id)).All((TaskBarComponentDefinition taskBarComponentDefinition) => AuxiliaryBarContainsComponent(bar, taskBarComponentDefinition.Id)))
		{
			ImGui.TextDisabled("所有组件都已添加。可先移除某个组件后再添加。 ");
		}
		ImGui.PopStyleVar();
		ImGui.EndPopup();
	}

	private void MoveAuxiliaryBarComponentRelativeTo(List<string> componentOrder, string componentId, string targetComponentId, bool insertAfter)
	{
		if (componentId.Equals(targetComponentId, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		List<string> collection = Configuration.NormalizeAuxiliaryComponentOrder(componentOrder);
		componentOrder.Clear();
		componentOrder.AddRange(collection);
		int num = componentOrder.FindIndex((string id) => id.Equals(componentId, StringComparison.OrdinalIgnoreCase));
		int num2 = componentOrder.FindIndex((string id) => id.Equals(targetComponentId, StringComparison.OrdinalIgnoreCase));
		if (num >= 0 && num2 >= 0)
		{
			string item = componentOrder[num];
			componentOrder.RemoveAt(num);
			if (num < num2)
			{
				num2--;
			}
			int value = (insertAfter ? (num2 + 1) : num2);
			componentOrder.Insert(Math.Clamp(value, 0, componentOrder.Count), item);
			saveConfig();
		}
	}

	private static bool AuxiliaryBarContainsComponent(AuxiliaryBarDefinition bar, string componentId)
	{
		if (!bar.ComponentOrder.Any((string id) => id.Equals(componentId, StringComparison.OrdinalIgnoreCase)) && !bar.SectionStartComponentOrder.Any((string id) => id.Equals(componentId, StringComparison.OrdinalIgnoreCase)) && !bar.SectionCenterComponentOrder.Any((string id) => id.Equals(componentId, StringComparison.OrdinalIgnoreCase)))
		{
			return bar.SectionEndComponentOrder.Any((string id) => id.Equals(componentId, StringComparison.OrdinalIgnoreCase));
		}
		return true;
	}

	private static string GetAuxiliaryDragComponentId(string dragId, int? barIndex = null, string? sectionKey = null)
	{
		string text = ((!barIndex.HasValue) ? "aux:" : (string.IsNullOrWhiteSpace(sectionKey) ? $"aux:{barIndex.Value}:" : $"aux:{barIndex.Value}:{sectionKey}:"));
		if (!dragId.StartsWith(text, StringComparison.OrdinalIgnoreCase))
		{
			return string.Empty;
		}
		return dragId.Substring(text.Length);
	}

	private void DrawTaskBarComponentsPage()
	{
		DrawTargetInfoSubsection("主栏组件");
		List<string> selectedTaskBarComponentOrder = GetSelectedTaskBarComponentOrder();
		if (selectedTaskBarComponentOrder == null || !selectedTaskBarComponentOrder.Any((string id) => id.Equals(selectedTaskBarComponentSettingsId, StringComparison.OrdinalIgnoreCase)))
		{
			ClearSelectedTaskBarComponentSettings();
		}
		ImGui.BeginChild("TaskBarComponentList", new Vector2(0f, 0f));
		if (config.TaskBarStretchToEdges)
		{
			DrawTaskBarSectionComponents("左侧", config.TaskBarLeftComponentOrder, "left");
			ImGui.Spacing();
			DrawTaskBarSectionComponents("中间", config.TaskBarCenterComponentOrder, "center");
			ImGui.Spacing();
			DrawTaskBarSectionComponents("右侧", config.TaskBarRightComponentOrder, "right");
			ImGui.EndChild();
			return;
		}
		List<TaskBarComponentDefinition> activeTaskBarComponentDefinitions = GetActiveTaskBarComponentDefinitions(config.TaskBarComponentOrder, normalizeAdaptiveOrder: true);
		if (activeTaskBarComponentDefinitions.Count == 0)
		{
			ImGui.TextDisabled("还没有添加组件。点击添加组件开始配置主栏。");
		}
		for (int num = 0; num < activeTaskBarComponentDefinitions.Count; num++)
		{
			DrawTaskBarComponentRow(activeTaskBarComponentDefinitions[num], config.TaskBarComponentOrder, "adaptive");
		}
		DrawTaskBarDraggedComponentPreview(activeTaskBarComponentDefinitions, "task:adaptive:");
		ImGui.Spacing();
		if (ImGui.Button("添加组件"))
		{
			ImGui.OpenPopup("AllHud_TaskBar_AddComponent");
		}
		DrawTaskBarAddComponentPopup();
		ImGui.EndChild();
	}

	private void DrawTaskBarSectionComponents(string label, List<string> componentOrder, string sectionKey)
	{
		componentOrder = NormalizeTaskBarSectionOrderReference(componentOrder, sectionKey);
		DrawTargetInfoSubsection(label);
		List<TaskBarComponentDefinition> activeTaskBarComponentDefinitions = GetActiveTaskBarComponentDefinitions(componentOrder, normalizeAdaptiveOrder: false);
		if (activeTaskBarComponentDefinitions.Count == 0)
		{
			ImGui.TextDisabled("还没有添加组件。");
		}
		foreach (TaskBarComponentDefinition item in activeTaskBarComponentDefinitions)
		{
			DrawTaskBarComponentRow(item, componentOrder, sectionKey);
		}
		DrawTaskBarDraggedComponentPreview(activeTaskBarComponentDefinitions, "task:" + sectionKey + ":");
		ImGui.Spacing();
		string text = "AllHud_TaskBar_AddComponent_" + sectionKey;
		ImU8String label2 = new ImU8String(6, 1);
		label2.AppendLiteral("添加组件##");
		label2.AppendFormatted(sectionKey);
		if (ImGui.Button(label2))
		{
			ImGui.OpenPopup(text);
		}
		DrawTaskBarAddComponentPopup(text, componentOrder, "添加到" + label);
	}

	private List<string> NormalizeTaskBarSectionOrderReference(List<string> componentOrder, string sectionKey)
	{
		List<string> list = Configuration.NormalizeTaskBarSectionComponentOrder(componentOrder);
		if (!(sectionKey == "left"))
		{
			if (sectionKey == "right")
			{
				config.TaskBarRightComponentOrder = list;
				return config.TaskBarRightComponentOrder;
			}
			config.TaskBarCenterComponentOrder = list;
			return config.TaskBarCenterComponentOrder;
		}
		config.TaskBarLeftComponentOrder = list;
		return config.TaskBarLeftComponentOrder;
	}

	private TaskBarComponentDefinition GetComponentDefinition(string componentId)
	{
		string componentBaseId = Configuration.GetComponentBaseId(componentId);
		if (!TaskBarComponentDefinitionLookup.TryGetValue(componentBaseId, out var value))
		{
			return default(TaskBarComponentDefinition);
		}
		if (Configuration.IsPluginShortcutComponentId(componentId) && !componentId.Equals("plugin_shortcut", StringComparison.OrdinalIgnoreCase))
		{
			return value with
			{
				Id = componentId,
				Description = GetPluginShortcutDescription(componentId)
			};
		}
		if (Configuration.IsCustomShortcutComponentId(componentId) && !componentId.Equals("custom_shortcut", StringComparison.OrdinalIgnoreCase))
		{
			CustomShortcutDefinition customShortcut = GetCustomShortcut(componentId);
			string text = (string.IsNullOrWhiteSpace(customShortcut.Name) ? "快捷方式" : customShortcut.Name.Trim());
			int count = GetCustomShortcutCommandLines(customShortcut).Count;
			string text2 = ((count == 0) ? "未设置命令" : $"{count} 条命令");
			return value with
			{
				Id = componentId,
				Description = text + " · " + text2
			};
		}
		if (Configuration.IsQuickMenuComponentId(componentId) && !componentId.Equals("quick_menu", StringComparison.OrdinalIgnoreCase))
		{
			QuickMenuDefinition quickMenu = GetQuickMenu(componentId);
			string value2 = (string.IsNullOrWhiteSpace(quickMenu.Name) ? "快捷菜单" : quickMenu.Name.Trim());
			return value with
			{
				Id = componentId,
				Description = $"{value2} · {quickMenu.ComponentOrder.Count} 项"
			};
		}
		return value;
	}

	private static bool IsPlacedComponentId(string componentId)
	{
		return !componentId.Equals("quick_menu", StringComparison.OrdinalIgnoreCase);
	}

	private void DrawTaskBarComponentRow(TaskBarComponentDefinition component, List<string> componentOrder, string scopeKey)
	{
		ThemePalette effectiveTheme = GetEffectiveTheme();
		ImU8String strId = new ImU8String(18, 2);
		strId.AppendLiteral("TaskBarComponent_");
		strId.AppendFormatted(scopeKey);
		strId.AppendLiteral("_");
		strId.AppendFormatted(component.Id);
		ImGui.PushID(strId);
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
		float num = Math.Max(260f, ImGui.GetContentRegionAvail().X - 8f);
		Vector2 vector = cursorScreenPos + new Vector2(num, 40f);
		bool flag = ImGui.IsMouseHoveringRect(cursorScreenPos, vector);
		string value = "task:" + scopeKey + ":" + component.Id;
		bool flag2 = draggingTaskBarComponentId.Equals(value, StringComparison.OrdinalIgnoreCase);
		bool num2 = ComponentHasSettings(component.Id);
		bool flag3 = num2 && selectedTaskBarComponentSettingsId.Equals(component.Id, StringComparison.OrdinalIgnoreCase) && selectedTaskBarComponentSettingsScope.Equals(scopeKey, StringComparison.OrdinalIgnoreCase);
		Vector4 col = (flag2 ? WithAlpha(effectiveTheme.HeaderActive, 0.72f) : (flag3 ? WithAlpha(effectiveTheme.HeaderActive, 0.96f) : (flag ? WithAlpha(effectiveTheme.HeaderHovered, 0.94f) : WithAlpha(effectiveTheme.Surface, 0.82f))));
		Vector4 col2 = (flag2 ? WithAlpha(effectiveTheme.Accent, 0.72f) : (flag3 ? WithAlpha(effectiveTheme.Accent, 0.78f) : WithAlpha(effectiveTheme.Border, 0.46f)));
		windowDrawList.ChannelsSplit(2);
		windowDrawList.ChannelsSetCurrent(1);
		ImGui.SetCursorScreenPos(cursorScreenPos);
		bool flag4 = ImGui.InvisibleButton("row_drag_area", new Vector2(Math.Max(48f, num - 146f), 40f));
		bool flag5 = ImGui.IsItemHovered();
		if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
		{
			draggingTaskBarComponentId = value;
		}
		if ((num2 & flag4) && !flag2)
		{
			ToggleSelectedTaskBarComponentSettings(component.Id, scopeKey);
		}
		if (!ImGui.IsMouseDown(ImGuiMouseButton.Left) & flag2)
		{
			draggingTaskBarComponentId = string.Empty;
		}
		if (((!string.IsNullOrWhiteSpace(draggingTaskBarComponentId) && !flag2) & flag) && ImGui.IsMouseDown(ImGuiMouseButton.Left))
		{
			bool flag6 = ImGui.GetMousePos().Y > cursorScreenPos.Y + 20f;
			float y = (flag6 ? (vector.Y - 1f) : (cursorScreenPos.Y + 1f));
			windowDrawList.AddLine(new Vector2(cursorScreenPos.X + 10f, y), new Vector2(vector.X - 10f, y), ImGui.GetColorU32(WithAlpha(effectiveTheme.Accent, 0.82f)), 2f);
			string taskBarDragComponentId = GetTaskBarDragComponentId(draggingTaskBarComponentId, scopeKey);
			MoveTaskBarComponentRelativeTo(componentOrder, taskBarDragComponentId, component.Id, flag6);
		}
		DrawTaskBarComponentIcon(color: WithAlpha((flag5 | flag2) ? effectiveTheme.Accent : effectiveTheme.TextMuted, (flag5 | flag2) ? 0.95f : 0.7f), drawList: windowDrawList, componentId: component.Id, center: cursorScreenPos + new Vector2(25f, 20f));
		Vector2 pos = cursorScreenPos + new Vector2(48f, 11f);
		windowDrawList.AddText(pos, ImGui.GetColorU32(effectiveTheme.Text), component.Name);
		float x = pos.X + ImGui.CalcTextSize(component.Name).X + 14f;
		windowDrawList.AddText(new Vector2(x, pos.Y), ImGui.GetColorU32(effectiveTheme.TextMuted), component.Description);
		Vector2 center = new Vector2(vector.X - 20f, cursorScreenPos.Y + 20f);
		if (DrawHeaderRemoveComponentButton(min: new Vector2(vector.X - 132f, cursorScreenPos.Y + 7f), label: "移除##TaskRemove_" + scopeKey + "_" + component.Id))
		{
			RemoveTaskBarComponent(componentOrder, component.Id);
			flag3 = false;
		}
		if (num2)
		{
			if (flag3)
			{
				DrawChevronDownGlyph(windowDrawList, center, 8f, ImGui.GetColorU32(WithAlpha(effectiveTheme.Accent, 0.95f)));
			}
			else
			{
				DrawSettingsDotGlyph(windowDrawList, center, 7f, ImGui.GetColorU32(WithAlpha(effectiveTheme.TextMuted, flag ? 0.8f : 0.42f)));
			}
		}
		ImGui.SetCursorScreenPos(cursorScreenPos + new Vector2(0f, 46f));
		if (flag3)
		{
			DrawExpandedComponentSettingsContent(component, num);
		}
		Vector2 pMax = (flag3 ? new Vector2(cursorScreenPos.X + num, ImGui.GetCursorScreenPos().Y - 6f) : vector);
		windowDrawList.ChannelsSetCurrent(0);
		windowDrawList.AddRectFilled(cursorScreenPos, pMax, ImGui.GetColorU32(col), 7f);
		windowDrawList.AddRect(cursorScreenPos, pMax, ImGui.GetColorU32(col2), 7f);
		windowDrawList.ChannelsMerge();
		ImGui.PopID();
	}

	private void DrawTaskBarDraggedComponentPreview(IReadOnlyList<TaskBarComponentDefinition> activeComponents, string dragPrefix = "")
	{
		ThemePalette effectiveTheme = GetEffectiveTheme();
		if (string.IsNullOrWhiteSpace(draggingTaskBarComponentId) || !ImGui.IsMouseDown(ImGuiMouseButton.Left))
		{
			return;
		}
		string draggedComponentId = (string.IsNullOrWhiteSpace(dragPrefix) ? draggingTaskBarComponentId : (draggingTaskBarComponentId.StartsWith(dragPrefix, StringComparison.OrdinalIgnoreCase) ? draggingTaskBarComponentId.Substring(dragPrefix.Length) : string.Empty));
		if (!string.IsNullOrWhiteSpace(draggedComponentId))
		{
			TaskBarComponentDefinition taskBarComponentDefinition = activeComponents.FirstOrDefault((TaskBarComponentDefinition item) => item.Id.Equals(draggedComponentId, StringComparison.OrdinalIgnoreCase));
			if (!string.IsNullOrWhiteSpace(taskBarComponentDefinition.Id))
			{
				ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
				float x = Math.Max(260f, ImGui.GetContentRegionAvail().X - 8f);
				Vector2 mousePos = ImGui.GetMousePos();
				Vector2 vector = new Vector2(ImGui.GetCursorScreenPos().X, mousePos.Y - 20f);
				Vector2 vector2 = vector + new Vector2(x, 40f);
				windowDrawList.AddRectFilled(vector + new Vector2(0f, 2f), vector2 + new Vector2(0f, 2f), ImGui.GetColorU32(WithAlpha(effectiveTheme.TextShadow, 0.18f)), 7f);
				windowDrawList.AddRectFilled(vector, vector2, ImGui.GetColorU32(WithAlpha(effectiveTheme.SurfaceAlt, 0.92f)), 7f);
				windowDrawList.AddRect(vector, vector2, ImGui.GetColorU32(WithAlpha(effectiveTheme.Accent, 0.88f)), 7f, ImDrawFlags.None, 1.4f);
				DrawTaskBarComponentIcon(windowDrawList, taskBarComponentDefinition.Id, vector + new Vector2(25f, 20f), WithAlpha(effectiveTheme.Accent, 0.95f));
				Vector2 pos = vector + new Vector2(48f, 11f);
				windowDrawList.AddText(pos, ImGui.GetColorU32(WithAlpha(effectiveTheme.Text, 0.98f)), taskBarComponentDefinition.Name);
				float x2 = pos.X + ImGui.CalcTextSize(taskBarComponentDefinition.Name).X + 14f;
				windowDrawList.AddText(new Vector2(x2, pos.Y), ImGui.GetColorU32(WithAlpha(effectiveTheme.TextMuted, 0.7f)), taskBarComponentDefinition.Description);
			}
		}
	}

	private static void DrawTaskBarComponentIcon(ImDrawListPtr drawList, string componentId, Vector2 center, Vector4 color)
	{
		componentId = Configuration.GetComponentBaseId(componentId);
		uint colorU = ImGui.GetColorU32(color);
		ImGui.GetColorU32(WithAlpha(color, 0.34f));
		if (componentId == null)
		{
			return;
		}
		switch (componentId.Length)
		{
		case 9:
			switch (componentId[0])
			{
			case 'm':
				if (componentId == "main_menu")
				{
					DrawMenuPanelGlyph(drawList, center, 20f, colorU);
				}
				break;
			case 'i':
				if (componentId == "inventory")
				{
					drawList.AddRect(center + new Vector2(-8f, -6f), center + new Vector2(8f, 7f), colorU, 2f, ImDrawFlags.None, 1.5f);
					drawList.AddLine(center + new Vector2(-5f, -6f), center + new Vector2(-3f, -9f), colorU, 1.4f);
					drawList.AddLine(center + new Vector2(5f, -6f), center + new Vector2(3f, -9f), colorU, 1.4f);
					drawList.AddLine(center + new Vector2(-3f, -9f), center + new Vector2(3f, -9f), colorU, 1.4f);
				}
				break;
			case 's':
				if (componentId == "saddlebag")
				{
					drawList.AddRect(center + new Vector2(-8f, -5f), center + new Vector2(8f, 7f), colorU, 3f, ImDrawFlags.None, 1.5f);
					drawList.AddBezierCubic(center + new Vector2(-5f, -5f), center + new Vector2(-4f, -10f), center + new Vector2(4f, -10f), center + new Vector2(5f, -5f), colorU, 1.4f);
					drawList.AddCircleFilled(center + new Vector2(-3f, 1f), 1f, colorU, 8);
					drawList.AddCircleFilled(center + new Vector2(3f, 1f), 1f, colorU, 8);
				}
				break;
			}
			break;
		case 11:
			switch (componentId[0])
			{
			default:
				return;
			case 'p':
				break;
			case 's':
				if (componentId == "server_info")
				{
					drawList.AddCircle(center, 8f, colorU, 24, 1.5f);
					drawList.AddLine(center + new Vector2(-6f, 0f), center + new Vector2(6f, 0f), colorU, 1.3f);
					drawList.AddLine(center + new Vector2(0f, -8f), center + new Vector2(0f, 8f), colorU, 1.3f);
					drawList.AddBezierCubic(center + new Vector2(-3f, -7f), center + new Vector2(-6f, -2f), center + new Vector2(-6f, 2f), center + new Vector2(-3f, 7f), colorU, 1.2f);
					drawList.AddBezierCubic(center + new Vector2(3f, -7f), center + new Vector2(6f, -2f), center + new Vector2(6f, 2f), center + new Vector2(3f, 7f), colorU, 1.2f);
				}
				return;
			case 'c':
				if (componentId == "coordinates")
				{
					drawList.AddCircle(center, 7.5f, colorU, 24, 1.5f);
					drawList.AddLine(center + new Vector2(-7f, 0f), center + new Vector2(7f, 0f), colorU, 1.2f);
					drawList.AddLine(center + new Vector2(0f, -7f), center + new Vector2(0f, 7f), colorU, 1.2f);
					drawList.AddCircleFilled(center, 1.9f, colorU, 8);
				}
				return;
			}
			if (!(componentId == "plugin_list"))
			{
				break;
			}
			goto IL_0317;
		case 15:
			switch (componentId[0])
			{
			default:
				return;
			case 'p':
				if (!(componentId == "plugin_shortcut"))
				{
					return;
				}
				break;
			case 'c':
				if (!(componentId == "custom_shortcut"))
				{
					return;
				}
				break;
			}
			goto IL_0317;
		case 8:
			switch (componentId[0])
			{
			case 't':
				if (componentId == "teleport")
				{
					drawList.AddLine(center + new Vector2(-7f, 4f), center + new Vector2(4f, -7f), colorU, 1.7f);
					drawList.AddLine(center + new Vector2(0f, -7f), center + new Vector2(6f, -7f), colorU, 1.7f);
					drawList.AddLine(center + new Vector2(6f, -7f), center + new Vector2(6f, -1f), colorU, 1.7f);
					drawList.AddCircleFilled(center + new Vector2(-4.5f, 1.5f), 2.2f, colorU, 10);
				}
				break;
			case 'c':
				if (componentId == "currency")
				{
					drawList.AddCircle(center, 7.2f, colorU, 24, 1.5f);
					drawList.AddLine(center + new Vector2(-3.5f, -4.2f), center + new Vector2(3f, -4.2f), colorU, 1.3f);
					drawList.AddLine(center + new Vector2(-3.5f, 0f), center + new Vector2(3.5f, 0f), colorU, 1.3f);
					drawList.AddLine(center + new Vector2(-3.5f, 4.2f), center + new Vector2(3f, 4.2f), colorU, 1.3f);
				}
				break;
			}
			break;
		case 4:
			if (componentId == "time")
			{
				drawList.AddCircle(center, 8f, colorU, 24, 1.6f);
				drawList.AddLine(center, center + new Vector2(0f, -5f), colorU, 1.6f);
				drawList.AddLine(center, center + new Vector2(4f, 2.5f), colorU, 1.6f);
			}
			break;
		case 3:
			if (componentId == "fps")
			{
				drawList.AddRect(center - new Vector2(8f, 6f), center + new Vector2(8f, 6f), colorU, 2f, ImDrawFlags.None, 1.5f);
				drawList.AddLine(center + new Vector2(-5f, 8f), center + new Vector2(5f, 8f), colorU, 1.5f);
				drawList.AddLine(center + new Vector2(-2f, 6f), center + new Vector2(2f, 6f), colorU, 1.5f);
			}
			break;
		case 6:
			if (componentId == "volume")
			{
				DrawSpeakerGlyph(drawList, center, 20f, colorU);
			}
			break;
		case 10:
			if (componentId == "quick_menu")
			{
				DrawQuickListGlyph(drawList, center, 20f, colorU);
			}
			break;
		case 16:
			if (componentId == "gearset_switcher")
			{
				drawList.AddCircle(center, 7f, colorU, 24, 1.5f);
				drawList.AddLine(center + new Vector2(-5f, 0f), center + new Vector2(5f, 0f), colorU, 1.3f);
				drawList.AddLine(center + new Vector2(0f, -5f), center + new Vector2(0f, 5f), colorU, 1.3f);
				drawList.AddCircleFilled(center, 2.1f, colorU, 8);
			}
			break;
		case 17:
			if (componentId == "walking_indicator")
			{
				drawList.AddCircle(center + new Vector2(1f, -5.5f), 2f, colorU, 14, 1.4f);
				drawList.AddLine(center + new Vector2(1f, -3f), center + new Vector2(-1f, 2f), colorU, 1.5f);
				drawList.AddLine(center + new Vector2(-1f, 2f), center + new Vector2(-6f, 7f), colorU, 1.5f);
				drawList.AddLine(center + new Vector2(-1f, 2f), center + new Vector2(5f, 7f), colorU, 1.5f);
				drawList.AddLine(center + new Vector2(0f, -2f), center + new Vector2(6f, 0f), colorU, 1.5f);
			}
			break;
		case 5:
		case 7:
		case 12:
		case 13:
		case 14:
			break;
			IL_0317:
			if (componentId.Equals("custom_shortcut", StringComparison.OrdinalIgnoreCase))
			{
				drawList.AddCircle(center, 8f, colorU, 24, 1.5f);
				drawList.AddLine(center + new Vector2(-5f, 0f), center + new Vector2(5f, 0f), colorU, 1.5f);
				drawList.AddLine(center + new Vector2(0f, -5f), center + new Vector2(0f, 5f), colorU, 1.5f);
			}
			else
			{
				DrawPluginTileGlyph(drawList, center, 20f, colorU);
			}
			break;
		}
	}

	private static void DrawMenuPanelGlyph(ImDrawListPtr drawList, Vector2 center, float size, uint color)
	{
		float num = Math.Max(1.6f, size * 0.1025f);
		float num2 = size * 0.2f;
		Vector2[] array = new Vector2[3]
		{
			new Vector2((0f - size) * 0.25f, size * 0.2f),
			new Vector2((0f - size) * 0.31f, size * 0.31f),
			new Vector2((0f - size) * 0.2f, size * 0.25f)
		};
		for (int i = 0; i < array.Length; i++)
		{
			float num3 = center.Y + (float)(i - 1) * num2;
			drawList.AddRectFilled(new Vector2(center.X + array[i].X, num3 - num * 0.5f), new Vector2(center.X + array[i].Y, num3 + num * 0.5f), color, num * 0.5f);
		}
	}

	private static void DrawPluginTileGlyph(ImDrawListPtr drawList, Vector2 center, float size, uint color)
	{
		float thickness = Math.Max(1.35f, size * 0.0825f);
		float num = size * 0.23f;
		float num2 = size * 0.14f;
		float rounding = Math.Max(1.1f, num * 0.3f);
		Vector2 vector = center - new Vector2(num + num2 * 0.5f, num + num2 * 0.5f);
		for (int i = 0; i < 2; i++)
		{
			for (int j = 0; j < 2; j++)
			{
				Vector2 vector2 = vector + new Vector2((float)j * (num + num2), (float)i * (num + num2));
				drawList.AddRect(vector2, vector2 + new Vector2(num, num), color, rounding, ImDrawFlags.None, thickness);
			}
		}
	}

	private static void DrawQuickListGlyph(ImDrawListPtr drawList, Vector2 center, float size, uint color)
	{
		float num = Math.Max(1.6f, size * 0.1f);
		float num2 = size * 0.24f;
		float num3 = Math.Max(2.2f, size * 0.12f);
		float num4 = center.X - size * 0.31f;
		float x = center.X - size * 0.11f;
		float[] array = new float[3]
		{
			size * 0.27f,
			size * 0.34f,
			size * 0.22f
		};
		float num5 = center.Y - num2;
		for (int i = 0; i < 3; i++)
		{
			float num6 = num5 + (float)i * num2;
			drawList.AddRectFilled(new Vector2(num4 - num3 * 0.5f, num6 - num3 * 0.5f), new Vector2(num4 + num3 * 0.5f, num6 + num3 * 0.5f), color, Math.Max(1f, num3 * 0.35f));
			drawList.AddRectFilled(new Vector2(x, num6 - num * 0.5f), new Vector2(center.X + array[i], num6 + num * 0.5f), color, num * 0.5f);
		}
	}

	private static void DrawSpeakerGlyph(ImDrawListPtr drawList, Vector2 center, float size, uint color)
	{
		float thickness = Math.Max(1.5f, size * 0.0925f);
		float x = (0f - size) * 0.36f;
		float num = (0f - size) * 0.18f;
		float num2 = size * 0.16f;
		float num3 = size * 0.21f;
		float x2 = size * 0.08f;
		float num4 = size * 0.34f;
		Vector2 center2 = center + new Vector2(size * 0.07f, 0f);
		drawList.AddRectFilled(center + new Vector2(x, 0f - num2), center + new Vector2(num, num2), color, Math.Max(1f, size * 0.0725f));
		Span<Vector2> span = stackalloc Vector2[4];
		span[0] = center + new Vector2(num - size * 0.015f, 0f - num3);
		span[1] = center + new Vector2(x2, 0f - num4);
		span[2] = center + new Vector2(x2, num4);
		span[3] = center + new Vector2(num - size * 0.015f, num3);
		drawList.AddConvexPolyFilled(ref span[0], span.Length, color);
		drawList.PathArcTo(center2, size * 0.2f, -0.82f, 0.82f, 12);
		drawList.PathStroke(color, ImDrawFlags.None, thickness);
		drawList.PathArcTo(center2, size * 0.34f, -0.75f, 0.75f, 14);
		drawList.PathStroke(color, ImDrawFlags.None, Math.Max(1.2f, size * 0.0775f));
	}

	private static void DrawChevronGlyph(ImDrawListPtr drawList, Vector2 center, float size, uint color)
	{
		float num = 2f;
		float num2 = size * 0.32f;
		float num3 = size * 0.42f;
		Vector2 vector = new Vector2(center.X - num2, center.Y);
		drawList.AddLine(new Vector2(center.X + num2, center.Y - num3), vector, color, num);
		drawList.AddLine(vector, new Vector2(center.X + num2, center.Y + num3), color, num);
		drawList.AddCircleFilled(vector, num * 0.5f, color, 8);
	}

	private static void DrawChevronDownGlyph(ImDrawListPtr drawList, Vector2 center, float size, uint color)
	{
		float num = 2f;
		float num2 = size * 0.42f;
		float num3 = size * 0.3f;
		Vector2 vector = new Vector2(center.X, center.Y + num3);
		drawList.AddLine(new Vector2(center.X - num2, center.Y - num3), vector, color, num);
		drawList.AddLine(vector, new Vector2(center.X + num2, center.Y - num3), color, num);
		drawList.AddCircleFilled(vector, num * 0.5f, color, 8);
	}

	private static void DrawSettingsDotGlyph(ImDrawListPtr drawList, Vector2 center, float size, uint color)
	{
		drawList.AddCircle(center, size * 0.62f, color, 16, 1.8f);
		drawList.AddCircleFilled(center, size * 0.24f, color, 12);
	}

	private void DrawTaskBarTimeSettings()
	{
		DrawCompactCheckbox("本地时间", "TaskBarShowLocalTime_ComponentPanel", config.TaskBarShowLocalTime, delegate(bool showLocal)
		{
			config.TaskBarShowLocalTime = showLocal || !config.TaskBarShowEorzeaTime;
		});
		if (GetExpandedSettingsWidth() >= 260f)
		{
			ImGui.SameLine(0f, 12f);
		}
		DrawCompactCheckbox("艾欧泽亚时间", "TaskBarShowEorzeaTime_ComponentPanel", config.TaskBarShowEorzeaTime, delegate(bool showEt)
		{
			config.TaskBarShowEorzeaTime = showEt || !config.TaskBarShowLocalTime;
		});
	}

	private void DrawTaskBarCoordinatesSettings()
	{
		DrawCompactCheckbox("地区", "TaskBarShowCoordinatesTerritory_ComponentPanel", config.TaskBarShowCoordinatesTerritory, delegate(bool showTerritory)
		{
			config.TaskBarShowCoordinatesTerritory = showTerritory || !config.TaskBarShowCoordinatesPosition;
		});
		if (GetExpandedSettingsWidth() >= 220f)
		{
			ImGui.SameLine(0f, 12f);
		}
		DrawCompactCheckbox("坐标", "TaskBarShowCoordinatesPosition_ComponentPanel", config.TaskBarShowCoordinatesPosition, delegate(bool showPosition)
		{
			config.TaskBarShowCoordinatesPosition = showPosition || !config.TaskBarShowCoordinatesTerritory;
		});
	}

	private void DrawTaskBarCurrencySettings()
	{
		float num = GetExpandedSettingsWidth();
		DrawCompactCheckbox("显示货币名称", "TaskBarCurrencyShowName_ComponentPanel", config.TaskBarCurrencyShowName, delegate(bool value)
		{
			config.TaskBarCurrencyShowName = value;
		});
		ImGui.SameLine(0f, 12f);
		DrawCompactCheckbox("显示上限", "TaskBarCurrencyShowCap_ComponentPanel", config.TaskBarCurrencyShowCap, delegate(bool value)
		{
			config.TaskBarCurrencyShowCap = value;
		});
		ImGui.SameLine(0f, 12f);
		DrawCompactCheckbox("显示周限额", "TaskBarCurrencyShowWeeklyCap_ComponentPanel", config.TaskBarCurrencyShowWeeklyCap, delegate(bool value)
		{
			config.TaskBarCurrencyShowWeeklyCap = value;
		});
		if (num >= 340f)
		{
			ImGui.SameLine(0f, 18f);
		}
		List<CurrencyDisplayOption> currencySettingsOptions = GetCurrencySettingsOptions();
		CurrencyDisplayOption currencyDisplayOption = currencySettingsOptions.FirstOrDefault((CurrencyDisplayOption option) => option.ItemId == config.TaskBarCurrencyItemId);
		string text = ((currencyDisplayOption.ItemId == 0) ? "金币" : currencyDisplayOption.Name);
		ImGui.SetNextItemWidth(Math.Min(180f, Math.Max(140f, num)));
		if (ImGui.BeginCombo("##TaskBarCurrencyItem", text))
		{
			foreach (CurrencyDisplayOption item in currencySettingsOptions)
			{
				bool flag = item.ItemId == config.TaskBarCurrencyItemId;
				if (ImGui.Selectable(item.Name, flag))
				{
					config.TaskBarCurrencyItemId = item.ItemId;
					saveConfig();
				}
				if (flag)
				{
					ImGui.SetItemDefaultFocus();
				}
			}
			ImGui.EndCombo();
		}
		DrawComponentSettingGroupSpacing();
		DrawCompactCheckbox("启用阈值颜色", "TaskBarCurrencyThreshold_ComponentPanel", config.TaskBarCurrencyUseThresholdColors, delegate(bool value)
		{
			config.TaskBarCurrencyUseThresholdColors = value;
		});
		if (config.TaskBarCurrencyUseThresholdColors)
		{
			int v = config.TaskBarCurrencyThresholdPercentage;
			ImGui.SetNextItemWidth(Math.Min(180f, Math.Max(120f, num)));
			if (ImGui.SliderInt("满额提醒阈值", ref v, 0, 100, "%d%%"))
			{
				config.TaskBarCurrencyThresholdPercentage = v;
				saveConfig();
			}
		}
		DrawComponentSettingGroupSpacing();
		ImGui.TextDisabled("可见货币（弹窗）");
		foreach (CurrencyDisplayOption item2 in currencySettingsOptions)
		{
			bool v2 = config.TaskBarCurrencyVisibleItemIds.Count == 0 || config.TaskBarCurrencyVisibleItemIds.Contains(item2.ItemId);
			ImU8String label = new ImU8String(18, 2);
			label.AppendFormatted(item2.Name);
			label.AppendLiteral("##CurrencyVisible_");
			label.AppendFormatted(item2.ItemId);
			if (!ImGui.Checkbox(label, ref v2))
			{
				continue;
			}
			if (config.TaskBarCurrencyVisibleItemIds.Count == 0)
			{
				config.TaskBarCurrencyVisibleItemIds.AddRange(currencySettingsOptions.Select((CurrencyDisplayOption item) => item.ItemId));
			}
			if (v2)
			{
				if (!config.TaskBarCurrencyVisibleItemIds.Contains(item2.ItemId))
				{
					config.TaskBarCurrencyVisibleItemIds.Add(item2.ItemId);
				}
			}
			else
			{
				config.TaskBarCurrencyVisibleItemIds.Remove(item2.ItemId);
			}
			saveConfig();
		}
		byte[] array = CreateUtf8Buffer(config.TaskBarCurrencyCustomItemIds, 256);
		ImGui.SetNextItemWidth(Math.Min(300f, Math.Max(180f, num)));
		if (ImGui.InputTextWithHint("##TaskBarCurrencyCustomIds", "自定义物品 ID，用逗号分隔", array))
		{
			config.TaskBarCurrencyCustomItemIds = ReadUtf8Buffer(array);
			saveConfig();
		}
	}

	private void DrawTaskBarWalkingSettings()
	{
		DrawCompactCheckbox("仅步行时显示", "TaskBarWalkingOnlyWhenWalking_ComponentPanel", config.TaskBarWalkingOnlyWhenWalking, delegate(bool value)
		{
			config.TaskBarWalkingOnlyWhenWalking = value;
		});
		ImGui.TextDisabled("点击任务栏图标可直接切换 IsWalking。图标会随状态变化。");
	}

	private void DrawTaskBarGearsetSettings()
	{
		DrawCompactCheckbox("职业名", "TaskBarGearsetShowName_ComponentPanel", config.TaskBarGearsetShowName, delegate(bool taskBarGearsetShowName)
		{
			config.TaskBarGearsetShowName = taskBarGearsetShowName;
		});
		ImGui.SameLine(0f, 12f);
		DrawCompactCheckbox("等级", "TaskBarGearsetShowLevel_ComponentPanel", config.TaskBarGearsetShowLevel, delegate(bool taskBarGearsetShowLevel)
		{
			config.TaskBarGearsetShowLevel = taskBarGearsetShowLevel;
		});
		ImGui.SameLine(0f, 12f);
		DrawCompactCheckbox("装等", "TaskBarGearsetShowItemLevel_ComponentPanel", config.TaskBarGearsetShowItemLevel, delegate(bool taskBarGearsetShowItemLevel)
		{
			config.TaskBarGearsetShowItemLevel = taskBarGearsetShowItemLevel;
		});
		ImGui.SameLine(0f, 12f);
		DrawCompactCheckbox("套装编号", "TaskBarGearsetShowNumber_ComponentPanel", config.TaskBarGearsetShowNumber, delegate(bool taskBarGearsetShowNumber)
		{
			config.TaskBarGearsetShowNumber = taskBarGearsetShowNumber;
		});
		ImGui.SameLine(0f, 18f);
		DrawCompactCheckbox("切换后关闭列表", "TaskBarGearsetClosePopupOnSwitch_ComponentPanel", config.TaskBarGearsetClosePopupOnSwitch, delegate(bool taskBarGearsetClosePopupOnSwitch)
		{
			config.TaskBarGearsetClosePopupOnSwitch = taskBarGearsetClosePopupOnSwitch;
		});
		DrawComponentSettingGroupSpacing();
		DrawComponentSettingsDivider(0.5f);
		DrawCompactCheckbox("显示当前套装标题", "TaskBarGearsetShowPopupHeader_ComponentPanel", config.TaskBarGearsetShowPopupHeader, delegate(bool taskBarGearsetShowPopupHeader)
		{
			config.TaskBarGearsetShowPopupHeader = taskBarGearsetShowPopupHeader;
		});
		ImGui.SameLine(0f, 14f);
		DrawCompactCheckbox("显示职业分组标题", "TaskBarGearsetShowGroupHeaders_ComponentPanel", config.TaskBarGearsetShowGroupHeaders, delegate(bool taskBarGearsetShowGroupHeaders)
		{
			config.TaskBarGearsetShowGroupHeaders = taskBarGearsetShowGroupHeaders;
		});
		ImGui.SameLine(0f, 14f);
		DrawCompactCheckbox("允许展开/收起", "TaskBarGearsetEnableGroupCollapse_ComponentPanel", config.TaskBarGearsetEnableGroupCollapse, delegate(bool taskBarGearsetEnableGroupCollapse)
		{
			config.TaskBarGearsetEnableGroupCollapse = taskBarGearsetEnableGroupCollapse;
		});
		DrawComponentSettingGroupSpacing();
		DrawCompactCheckbox("分组过长时滚动", "TaskBarGearsetEnableGroupScrolling_ComponentPanel", config.TaskBarGearsetEnableGroupScrolling, delegate(bool taskBarGearsetEnableGroupScrolling)
		{
			config.TaskBarGearsetEnableGroupScrolling = taskBarGearsetEnableGroupScrolling;
		});
		ImGui.SameLine(0f, 18f);
		ImGui.AlignTextToFramePadding();
		ImGui.TextDisabled("固定 3 列：防护/治疗｜近战/远物/远法｜采集/生产");
		if (config.TaskBarGearsetEnableGroupScrolling)
		{
			ImGui.SameLine(0f, 18f);
			ImGui.AlignTextToFramePadding();
			ImGui.TextUnformatted("每组可见");
			ImGui.SameLine(0f, 6f);
			ImGui.SetNextItemWidth(96f);
			int v = config.TaskBarGearsetMaxVisibleItemsPerGroup;
			if (ImGui.SliderInt("##TaskBarGearsetMaxVisibleItemsPerGroup_ComponentPanel", ref v, 2, 12, "%d 个"))
			{
				config.TaskBarGearsetMaxVisibleItemsPerGroup = v;
				saveConfig();
			}
		}
		DrawComponentSettingGroupSpacing();
		ImGui.AlignTextToFramePadding();
		ImGui.TextUnformatted("套装宽度");
		ImGui.SameLine(0f, 6f);
		ImGui.SetNextItemWidth(132f);
		int v2 = config.TaskBarGearsetButtonWidth;
		if (ImGui.SliderInt("##TaskBarGearsetButtonWidth_ComponentPanel", ref v2, 160, 360, "%d px"))
		{
			config.TaskBarGearsetButtonWidth = v2;
			saveConfig();
		}
		ImGui.SameLine(0f, 18f);
		ImGui.AlignTextToFramePadding();
		ImGui.TextUnformatted("套装高度");
		ImGui.SameLine(0f, 6f);
		ImGui.SetNextItemWidth(132f);
		int v3 = config.TaskBarGearsetButtonHeight;
		if (ImGui.SliderInt("##TaskBarGearsetButtonHeight_ComponentPanel", ref v3, 28, 64, "%d px"))
		{
			config.TaskBarGearsetButtonHeight = v3;
			saveConfig();
		}
		DrawComponentSettingGroupSpacing();
		ImGui.AlignTextToFramePadding();
		ImGui.TextUnformatted("职业菜单缩放");
		ImGui.SameLine(0f, 6f);
		ImGui.SetNextItemWidth(132f);
		float popupScale = config.TaskBarGearsetPopupScale;
		if (ImGui.SliderFloat("##TaskBarGearsetPopupScale_ComponentPanel", ref popupScale, 0.5f, 2f, "%.2f"))
		{
			config.TaskBarGearsetPopupScale = popupScale;
			saveConfig();
		}
		ImGui.SameLine(0f, 12f);
		ImGui.TextDisabled("仅放大职业行/字号，套装行不受影响");
		if (config.TaskBarGearsetShowGroupHeaders && config.TaskBarGearsetEnableGroupCollapse)
		{
			DrawComponentSettingGroupSpacing();
			if (ImGui.Button("全部展开##TaskBarGearsetExpandAll_ComponentPanel", new Vector2(88f, 26f)))
			{
				config.TaskBarGearsetCollapsedGroups.Clear();
				saveConfig();
			}
			ImGui.SameLine(0f, 8f);
			if (ImGui.Button("全部收起##TaskBarGearsetCollapseAll_ComponentPanel", new Vector2(88f, 26f)))
			{
				config.TaskBarGearsetCollapsedGroups = GearsetSettingsGroupOrder.ToList();
				config.TaskBarGearsetExpandedJobIds.Clear();
				saveConfig();
			}
		}
		DrawComponentSettingGroupSpacing();
		ImGui.TextDisabled("弹窗中显示的职业分组");
		for (int num2 = 0; num2 < GearsetSettingsGroupOrder.Length; num2++)
		{
			string group = GearsetSettingsGroupOrder[num2];
			bool value = !config.TaskBarGearsetHiddenGroups.Contains<string>(group, StringComparer.Ordinal);
			DrawCompactCheckbox(group, "TaskBarGearsetGroup_" + group + "_ComponentPanel", value, delegate(bool flag)
			{
				config.TaskBarGearsetHiddenGroups.RemoveAll((string entry) => string.Equals(entry, group, StringComparison.Ordinal));
				if (!flag)
				{
					config.TaskBarGearsetHiddenGroups.Add(group);
				}
			});
			if (num2 < GearsetSettingsGroupOrder.Length - 1 && num2 % 4 != 3)
			{
				ImGui.SameLine(0f, 12f);
			}
		}
	}

	private void DrawTaskBarAddComponentPopup()
	{
		DrawTaskBarAddComponentPopup("AllHud_TaskBar_AddComponent", config.TaskBarComponentOrder, "添加到主栏");
	}

	private void DrawTaskBarAddComponentPopup(string popupId, List<string> targetOrder, string title)
	{
		if (!ImGui.BeginPopup(popupId))
		{
			return;
		}
		ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12f, 10f));
		ImGui.TextDisabled(title);
		ImGui.Dummy(new Vector2(1f, 4f));
		float y = Math.Min(440f, Math.Max(220f, ImGui.GetIO().DisplaySize.Y - ImGui.GetCursorScreenPos().Y - 48f));
		ImU8String strId = new ImU8String(16, 1);
		strId.AppendLiteral("##");
		strId.AppendFormatted(popupId);
		strId.AppendLiteral("_ComponentList");
		ImGui.BeginChild(strId, new Vector2(310f, y));
		bool flag = false;
		bool flag2 = false;
		TaskBarComponentDefinition[] taskBarComponentDefinitions = TaskBarComponentDefinitions;
		for (int i = 0; i < taskBarComponentDefinitions.Length; i++)
		{
			TaskBarComponentDefinition component = taskBarComponentDefinitions[i];
			if (Configuration.IsRepeatableComponentId(component.Id) || !IsTaskBarComponentActiveInOrder(targetOrder, component.Id))
			{
				flag2 = true;
				if (DrawAddComponentCard(component))
				{
					AddTaskBarComponent(component.Id, targetOrder);
					ImGui.CloseCurrentPopup();
					flag = true;
				}
			}
		}
		ImGui.EndChild();
		if (!flag2 && !flag)
		{
			ImGui.TextDisabled("所有组件都已添加。可先移除某个组件后再添加。 ");
		}
		ImGui.PopStyleVar();
		ImGui.EndPopup();
	}

	private void ToggleSelectedTaskBarComponentSettings(string componentId, string scopeKey)
	{
		if (!ComponentHasSettings(componentId))
		{
			ClearSelectedTaskBarComponentSettings();
			return;
		}
		if (selectedTaskBarComponentSettingsId.Equals(componentId, StringComparison.OrdinalIgnoreCase) && selectedTaskBarComponentSettingsScope.Equals(scopeKey, StringComparison.OrdinalIgnoreCase))
		{
			ClearSelectedTaskBarComponentSettings();
			return;
		}
		selectedTaskBarComponentSettingsId = componentId;
		selectedTaskBarComponentSettingsScope = scopeKey;
		ClearSelectedAuxiliaryComponentSettings();
	}

	private void ToggleSelectedAuxiliaryComponentSettings(string componentId, int barIndex, string sectionKey)
	{
		if (!ComponentHasSettings(componentId))
		{
			ClearSelectedAuxiliaryComponentSettings();
			return;
		}
		if (selectedAuxiliaryComponentSettingsId.Equals(componentId, StringComparison.OrdinalIgnoreCase) && selectedAuxiliaryComponentSettingsScope.Equals(sectionKey, StringComparison.OrdinalIgnoreCase) && selectedAuxiliaryComponentSettingsBarIndex == barIndex)
		{
			ClearSelectedAuxiliaryComponentSettings();
			return;
		}
		selectedAuxiliaryComponentSettingsId = componentId;
		selectedAuxiliaryComponentSettingsScope = sectionKey;
		selectedAuxiliaryComponentSettingsBarIndex = barIndex;
		ClearSelectedTaskBarComponentSettings();
	}

	private void ClearSelectedTaskBarComponentSettings()
	{
		selectedTaskBarComponentSettingsId = string.Empty;
		selectedTaskBarComponentSettingsScope = string.Empty;
	}

	private void ClearSelectedAuxiliaryComponentSettings()
	{
		selectedAuxiliaryComponentSettingsId = string.Empty;
		selectedAuxiliaryComponentSettingsScope = string.Empty;
		selectedAuxiliaryComponentSettingsBarIndex = -1;
	}

	private List<string>? GetSelectedTaskBarComponentOrder()
	{
		return selectedTaskBarComponentSettingsScope switch
		{
			"adaptive" => config.TaskBarComponentOrder, 
			"left" => config.TaskBarLeftComponentOrder, 
			"center" => config.TaskBarCenterComponentOrder, 
			"right" => config.TaskBarRightComponentOrder, 
			_ => null, 
		};
	}

	private List<string>? GetSelectedAuxiliaryComponentOrder(AuxiliaryBarDefinition bar, int barIndex)
	{
		if (selectedAuxiliaryComponentSettingsBarIndex != barIndex)
		{
			return null;
		}
		return selectedAuxiliaryComponentSettingsScope switch
		{
			"main" => bar.ComponentOrder, 
			"start" => bar.SectionStartComponentOrder, 
			"center" => bar.SectionCenterComponentOrder, 
			"end" => bar.SectionEndComponentOrder, 
			_ => null, 
		};
	}

	private void DrawExpandedComponentSettingsContent(TaskBarComponentDefinition component, float rowWidth)
	{
		if (ComponentHasSettings(component.Id))
		{
			Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
			float num = Math.Max(180f, rowWidth - 68f);
			ImGui.SetCursorScreenPos(cursorScreenPos);
			ImGui.Indent(48f);
			ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8f, 4f));
			ImGui.PushTextWrapPos((cursorScreenPos + new Vector2(48f, 0f)).X + num);
			float num2 = expandedSettingsWidth;
			expandedSettingsWidth = num;
			try
			{
				DrawComponentSettingsContent(component.Id);
			}
			finally
			{
				expandedSettingsWidth = num2;
			}
			ImGui.PopTextWrapPos();
			float y = ImGui.GetCursorScreenPos().Y;
			ImGui.PopStyleVar();
			ImGui.Unindent(48f);
			float num3 = y;
			ImGui.SetCursorScreenPos(new Vector2(cursorScreenPos.X, num3 + 10f));
		}
	}

	private float GetExpandedSettingsWidth(float fallback = 180f)
	{
		if (!(expandedSettingsWidth > 0f))
		{
			return Math.Max(fallback, ImGui.GetContentRegionAvail().X);
		}
		return expandedSettingsWidth;
	}

	private void DrawComponentSettingsDivider(float alpha)
	{
		ThemePalette effectiveTheme = GetEffectiveTheme();
		DrawFullContentWidthDivider(1f, WithAlpha(effectiveTheme.Border, alpha), 1f, 7f);
	}

	private static void DrawComponentSettingGroupSpacing()
	{
		float cursorPosX = ImGui.GetCursorPosX();
		ImGui.Dummy(new Vector2(1f, 2f));
		ImGui.SetCursorPosX(cursorPosX);
	}

	private void DrawCompactCheckbox(string label, string id, bool value, Action<bool> setter)
	{
		DrawCheckbox(label, id, value, setter);
	}

	private bool DrawRemoveComponentButton(string label, bool alignRight = true)
	{
		ThemePalette effectiveTheme = GetEffectiveTheme();
		string text = label.Split("##")[0];
		float num = Math.Max(118f, ImGui.CalcTextSize(text).X + 30f);
		Vector2 vector = new Vector2(num, 28f);
		if (alignRight)
		{
			float x = ImGui.GetContentRegionAvail().X;
			ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0f, x - num));
		}
		Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
		Vector2 pMax = cursorScreenPos + vector;
		ImGui.InvisibleButton(label, vector);
		bool flag = ImGui.IsItemHovered();
		bool num2 = ImGui.IsItemActive();
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		Vector4 col = (num2 ? WithAlpha(effectiveTheme.Accent, 0.22f) : (flag ? WithAlpha(effectiveTheme.Accent, 0.15f) : WithAlpha(effectiveTheme.Surface, 0.56f)));
		Vector4 col2 = WithAlpha(effectiveTheme.Accent, flag ? 0.72f : 0.44f);
		windowDrawList.AddRectFilled(cursorScreenPos, pMax, ImGui.GetColorU32(col), 7f);
		windowDrawList.AddRect(cursorScreenPos, pMax, ImGui.GetColorU32(col2), 7f, ImDrawFlags.None, flag ? 1.4f : 1f);
		Vector2 vector2 = ImGui.CalcTextSize(text);
		windowDrawList.AddText(cursorScreenPos + (vector - vector2) * 0.5f, ImGui.GetColorU32(WithAlpha(effectiveTheme.Accent, 0.96f)), text);
		return ImGui.IsItemClicked();
	}

	private bool DrawHeaderRemoveComponentButton(string label, Vector2 min)
	{
		ThemePalette effectiveTheme = GetEffectiveTheme();
		Vector2 vector = new Vector2(86f, 26f);
		ImGui.SetCursorScreenPos(min);
		ImGui.InvisibleButton(label, vector);
		bool flag = ImGui.IsItemHovered();
		bool num = ImGui.IsItemActive();
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		Vector2 pMax = min + vector;
		Vector4 col = (num ? WithAlpha(effectiveTheme.Accent, 0.2f) : (flag ? WithAlpha(effectiveTheme.Accent, 0.13f) : WithAlpha(effectiveTheme.Surface, 0.42f)));
		Vector4 col2 = WithAlpha(effectiveTheme.Accent, flag ? 0.66f : 0.34f);
		windowDrawList.AddRectFilled(min, pMax, ImGui.GetColorU32(col), 7f);
		windowDrawList.AddRect(min, pMax, ImGui.GetColorU32(col2), 7f);
		string text = label.Split("##")[0];
		Vector2 vector2 = ImGui.CalcTextSize(text);
		windowDrawList.AddText(min + (vector - vector2) * 0.5f, ImGui.GetColorU32(WithAlpha(effectiveTheme.Accent, 0.9f)), text);
		return ImGui.IsItemClicked();
	}

	private void RemoveSelectedTaskBarComponent(List<string> componentOrder)
	{
		string text = selectedTaskBarComponentSettingsId;
		if (!string.IsNullOrWhiteSpace(text))
		{
			RemoveTaskBarComponent(componentOrder, text);
		}
	}

	private void RemoveTaskBarComponent(List<string> componentOrder, string componentId)
	{
		if (!string.IsNullOrWhiteSpace(componentId))
		{
			componentOrder.RemoveAll((string id) => id.Equals(componentId, StringComparison.OrdinalIgnoreCase));
			if (Configuration.IsRepeatableComponentId(componentId))
			{
				RemoveRepeatableComponentData(componentId);
			}
			else
			{
				SetTaskBarComponentEnabled(componentId, enabled: false);
				RemoveTaskBarComponentFromAllOrders(componentId);
			}
			ClearSelectedTaskBarComponentSettings();
			saveConfig();
		}
	}

	private void RemoveSelectedAuxiliaryComponent(List<string> componentOrder)
	{
		string text = selectedAuxiliaryComponentSettingsId;
		if (!string.IsNullOrWhiteSpace(text))
		{
			RemoveAuxiliaryComponent(componentOrder, text);
		}
	}

	private void RemoveAuxiliaryComponent(List<string> componentOrder, string componentId)
	{
		if (!string.IsNullOrWhiteSpace(componentId))
		{
			componentOrder.RemoveAll((string id) => id.Equals(componentId, StringComparison.OrdinalIgnoreCase));
			RemoveRepeatableComponentData(componentId);
			ClearSelectedAuxiliaryComponentSettings();
			saveConfig();
		}
	}

	private static bool ComponentHasSettings(string componentId)
	{
		if (!componentId.Equals("time", StringComparison.OrdinalIgnoreCase) && !componentId.Equals("server_info", StringComparison.OrdinalIgnoreCase) && !componentId.Equals("coordinates", StringComparison.OrdinalIgnoreCase) && !componentId.Equals("gearset_switcher", StringComparison.OrdinalIgnoreCase) && !componentId.Equals("currency", StringComparison.OrdinalIgnoreCase) && !componentId.Equals("walking_indicator", StringComparison.OrdinalIgnoreCase) && !componentId.Equals("plugin_list", StringComparison.OrdinalIgnoreCase) && !Configuration.IsPluginShortcutComponentId(componentId) && !Configuration.IsCustomShortcutComponentId(componentId))
		{
			return Configuration.IsQuickMenuComponentId(componentId);
		}
		return true;
	}

	private void DrawComponentSettingsPopup(string popupId, string componentId)
	{
		if (ImGui.BeginPopup(popupId))
		{
			DrawComponentSettingsContent(componentId);
			ImGui.EndPopup();
		}
	}

	private void DrawComponentSettingsContent(string componentId)
	{
		if (componentId.Equals("time", StringComparison.OrdinalIgnoreCase))
		{
			DrawTaskBarTimeSettings();
		}
		else if (componentId.Equals("server_info", StringComparison.OrdinalIgnoreCase))
		{
			DrawTaskBarServerInfoModeSelector();
		}
		else if (componentId.Equals("coordinates", StringComparison.OrdinalIgnoreCase))
		{
			DrawTaskBarCoordinatesSettings();
		}
		else if (componentId.Equals("gearset_switcher", StringComparison.OrdinalIgnoreCase))
		{
			DrawTaskBarGearsetSettings();
		}
		else if (componentId.Equals("currency", StringComparison.OrdinalIgnoreCase))
		{
			DrawTaskBarCurrencySettings();
		}
		else if (componentId.Equals("walking_indicator", StringComparison.OrdinalIgnoreCase))
		{
			DrawTaskBarWalkingSettings();
		}
		else if (componentId.Equals("plugin_list", StringComparison.OrdinalIgnoreCase))
		{
			DrawPluginListSettings();
		}
		else if (Configuration.IsPluginShortcutComponentId(componentId))
		{
			DrawPluginShortcutSettings(componentId);
		}
		else if (Configuration.IsCustomShortcutComponentId(componentId))
		{
			DrawCustomShortcutSettings(componentId);
		}
		else if (Configuration.IsQuickMenuComponentId(componentId))
		{
			DrawQuickMenuSettings(componentId);
		}
	}

	private void DrawPluginListSettings()
	{
		List<IExposedPlugin> installedPluginsForSelection = GetInstalledPluginsForSelection();
		pluginListSelectedInternalNameCache.Clear();
		foreach (string pluginListInternalName in config.PluginListInternalNames)
		{
			if (!string.IsNullOrWhiteSpace(pluginListInternalName))
			{
				pluginListSelectedInternalNameCache.Add(pluginListInternalName);
			}
		}
		HashSet<string> selectedInternalNames = pluginListSelectedInternalNameCache;
		ImGui.Indent(18f);
		try
		{
			float x = Math.Max(240f, GetExpandedSettingsWidth(240f) - 18f - 8f);
			if (installedPluginsForSelection.Count == 0)
			{
				ImGui.TextDisabled("没有可选择的插件。");
				return;
			}
			ThemePalette effectiveTheme = GetEffectiveTheme();
			ImGui.PushStyleColor(ImGuiCol.ChildBg, WithAlpha(effectiveTheme.Surface, 0.74f));
			ImGui.PushStyleColor(ImGuiCol.Border, WithAlpha(effectiveTheme.Border, 0.3f));
			ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(16f, 10f));
			float y = Math.Clamp((installedPluginsForSelection.Count <= 8) ? 112f : 178f, 112f, 220f);
			ImGui.BeginChild("##PluginListMultiSelect", new Vector2(x, y), border: true);
			float num = 9f;
			float num2 = Math.Max(160f, ImGui.GetContentRegionAvail().X - 6f);
			int num3 = Math.Clamp((int)Math.Floor((num2 + num) / 210f), 1, 4);
			float width = Math.Max(160f, (num2 - num * (float)(num3 - 1)) / (float)num3);
			for (int i = 0; i < installedPluginsForSelection.Count; i++)
			{
				IExposedPlugin plugin = installedPluginsForSelection[i];
				if (i > 0 && i % num3 != 0)
				{
					ImGui.SameLine(0f, num);
				}
				DrawPluginListToggleTile(plugin, width, selectedInternalNames);
			}
			ImGui.EndChild();
			ImGui.PopStyleVar();
			ImGui.PopStyleColor(2);
		}
		finally
		{
			ImGui.Unindent(18f);
		}
	}

	private void DrawPluginListToggleTile(IExposedPlugin plugin, float width, HashSet<string> selectedInternalNames)
	{
		ThemePalette effectiveTheme = GetEffectiveTheme();
		ImU8String strId = new ImU8String(17, 1);
		strId.AppendLiteral("PluginListToggle_");
		strId.AppendFormatted(plugin.InternalName);
		ImGui.PushID(strId);
		bool flag = selectedInternalNames.Contains(plugin.InternalName);
		Vector2 vector = new Vector2(width, 32f);
		Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
		Vector2 pMax = cursorScreenPos + vector;
		ImGui.InvisibleButton("##PluginTile", vector);
		bool flag2 = ImGui.IsItemHovered();
		if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
		{
			if (flag)
			{
				config.PluginListInternalNames.RemoveAll((string name) => name.Equals(plugin.InternalName, StringComparison.OrdinalIgnoreCase));
				selectedInternalNames.Remove(plugin.InternalName);
			}
			else if (!config.PluginListInternalNames.Contains<string>(plugin.InternalName, StringComparer.OrdinalIgnoreCase))
			{
				config.PluginListInternalNames.Add(plugin.InternalName.Trim());
				selectedInternalNames.Add(plugin.InternalName);
			}
			flag = !flag;
			saveConfig();
		}
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		Vector4 accent = effectiveTheme.Accent;
		Vector4 col = (flag ? WithAlpha(effectiveTheme.HeaderActive, flag2 ? 0.96f : 0.84f) : (flag2 ? WithAlpha(effectiveTheme.HeaderHovered, 0.9f) : WithAlpha(effectiveTheme.Surface, 0.62f)));
		Vector4 col2 = (flag ? WithAlpha(accent, 0.7f) : WithAlpha(accent, flag2 ? 0.42f : 0.2f));
		windowDrawList.AddRectFilled(cursorScreenPos, pMax, ImGui.GetColorU32(col), 7f);
		windowDrawList.AddRect(cursorScreenPos, pMax, ImGui.GetColorU32(col2), 7f, ImDrawFlags.None, flag ? 1.25f : 1f);
		Vector2 vector2 = cursorScreenPos + new Vector2(14f, 8f);
		Vector2 pMax2 = vector2 + new Vector2(16f, 16f);
		windowDrawList.AddRect(vector2, pMax2, ImGui.GetColorU32(WithAlpha(accent, flag ? 0.78f : 0.38f)), 4f, ImDrawFlags.None, 1.4f);
		if (flag)
		{
			windowDrawList.AddLine(vector2 + new Vector2(3.5f, 8f), vector2 + new Vector2(7f, 12f), ImGui.GetColorU32(accent), 2f);
			windowDrawList.AddLine(vector2 + new Vector2(7f, 12f), vector2 + new Vector2(13f, 4f), ImGui.GetColorU32(accent), 2f);
		}
		float num = 38f;
		float maxWidth = Math.Max(40f, width - num - 10f);
		string pluginListTileText = GetPluginListTileText(plugin.Name, maxWidth);
		windowDrawList.AddText(cursorScreenPos + new Vector2(num, 8f), ImGui.GetColorU32(WithAlpha(effectiveTheme.Text, 0.96f)), pluginListTileText);
		if (flag2)
		{
			DrawStyledTooltip(delegate
			{
				ImGui.TextUnformatted(plugin.Name);
				if (!string.IsNullOrWhiteSpace(plugin.InternalName))
				{
					ImGui.Separator();
					ImGui.TextUnformatted(plugin.InternalName);
				}
			});
		}
		ImGui.PopID();
	}

	private List<IExposedPlugin> GetInstalledPluginsForSelection()
	{
		long tickCount = Environment.TickCount64;
		if (installedPluginSelectionCacheUpdatedAtMs != long.MinValue && tickCount - installedPluginSelectionCacheUpdatedAtMs < 1000)
		{
			return installedPluginSelectionCache;
		}
		installedPluginSelectionCacheUpdatedAtMs = tickCount;
		installedPluginSelectionCache.Clear();
		installedPluginSelectionByInternalName.Clear();
		Dictionary<string, IExposedPlugin> dictionary = new Dictionary<string, IExposedPlugin>(StringComparer.OrdinalIgnoreCase);
		foreach (IExposedPlugin installedPlugin in pluginInterface.InstalledPlugins)
		{
			if (installedPlugin.IsLoaded && (installedPlugin.HasMainUi || installedPlugin.HasConfigUi) && !string.IsNullOrWhiteSpace(installedPlugin.InternalName) && (!dictionary.TryGetValue(installedPlugin.InternalName, out var value) || IsPreferredPluginCandidate(installedPlugin, value)))
			{
				dictionary[installedPlugin.InternalName] = installedPlugin;
			}
		}
		installedPluginSelectionCache.AddRange(dictionary.Values.OrderBy<IExposedPlugin, string>((IExposedPlugin plugin) => plugin.Name, StringComparer.OrdinalIgnoreCase).ToList());
		foreach (IExposedPlugin item in installedPluginSelectionCache)
		{
			installedPluginSelectionByInternalName[item.InternalName] = item;
		}
		return installedPluginSelectionCache;
	}

	private string GetPluginListTileText(string name, float maxWidth)
	{
		float num = MathF.Round(maxWidth / 8f) * 8f;
		if (Math.Abs(pluginListTileTextCacheWidth - num) > 0.1f)
		{
			pluginListTileTextCache.Clear();
			pluginListTileTextCacheWidth = num;
		}
		if (pluginListTileTextCache.TryGetValue(name, out string value))
		{
			return value;
		}
		string text = TrimTextToWidth(name, maxWidth);
		pluginListTileTextCache[name] = text;
		return text;
	}

	private static bool IsPreferredPluginCandidate(IExposedPlugin candidate, IExposedPlugin current)
	{
		int pluginCandidateScore = GetPluginCandidateScore(candidate);
		int pluginCandidateScore2 = GetPluginCandidateScore(current);
		if (pluginCandidateScore != pluginCandidateScore2)
		{
			return pluginCandidateScore > pluginCandidateScore2;
		}
		return string.Compare(candidate.Name, current.Name, StringComparison.OrdinalIgnoreCase) < 0;
	}

	private static int GetPluginCandidateScore(IExposedPlugin plugin)
	{
		int num = 0;
		if (plugin.IsLoaded)
		{
			num += 100;
		}
		if (plugin.IsDev)
		{
			num += 50;
		}
		if (plugin.HasMainUi)
		{
			num += 10;
		}
		if (plugin.HasConfigUi)
		{
			num += 5;
		}
		return num;
	}

	private void DrawPluginShortcutSettings(string componentId)
	{
		List<IExposedPlugin> installedPluginsForSelection = GetInstalledPluginsForSelection();
		if (installedPluginsForSelection.Count == 0)
		{
			ImGui.TextDisabled("没有可选择的插件。");
			return;
		}
		string selectedInternalName = GetPluginShortcutInternalName(componentId);
		string text = installedPluginsForSelection.FirstOrDefault((IExposedPlugin plugin) => plugin.InternalName.Equals(selectedInternalName, StringComparison.OrdinalIgnoreCase))?.Name ?? "选择插件";
		DrawComponentSettingGroupSpacing();
		ImGui.SetNextItemWidth(Math.Min(320f, GetExpandedSettingsWidth()));
		ImU8String label = new ImU8String(23, 1);
		label.AppendLiteral("##PluginShortcutPlugin_");
		label.AppendFormatted(componentId);
		if (!ImGui.BeginCombo(label, text))
		{
			return;
		}
		foreach (IExposedPlugin item in installedPluginsForSelection)
		{
			bool flag = item.InternalName.Equals(selectedInternalName, StringComparison.OrdinalIgnoreCase);
			if (ImGui.Selectable(item.Name, flag))
			{
				SetPluginShortcutInternalName(componentId, item.InternalName);
				saveConfig();
			}
			if (flag)
			{
				ImGui.SetItemDefaultFocus();
			}
		}
		ImGui.EndCombo();
	}

	private string GetPluginShortcutInternalName(string componentId)
	{
		if (!config.PluginShortcutInternalNames.TryGetValue(componentId, out string value))
		{
			return config.TaskBarPluginShortcutInternalName;
		}
		return value;
	}

	private void SetPluginShortcutInternalName(string componentId, string internalName)
	{
		config.PluginShortcutInternalNames[componentId] = internalName;
		if (componentId.Equals("plugin_shortcut", StringComparison.OrdinalIgnoreCase))
		{
			config.TaskBarPluginShortcutInternalName = internalName;
		}
	}

	private string GetPluginShortcutDescription(string componentId)
	{
		string pluginShortcutInternalName = GetPluginShortcutInternalName(componentId);
		if (string.IsNullOrWhiteSpace(pluginShortcutInternalName))
		{
			return "未选择插件";
		}
		GetInstalledPluginsForSelection();
		installedPluginSelectionByInternalName.TryGetValue(pluginShortcutInternalName, out IExposedPlugin value);
		return value?.Name ?? (pluginShortcutInternalName + "（未安装）");
	}

	private CustomShortcutDefinition GetCustomShortcut(string componentId)
	{
		if (!config.CustomShortcuts.TryGetValue(componentId, out CustomShortcutDefinition value) || value == null)
		{
			value = new CustomShortcutDefinition();
			config.CustomShortcuts[componentId] = value;
		}
		return value;
	}

	private QuickMenuDefinition GetQuickMenu(string componentId)
	{
		if (!config.QuickMenus.TryGetValue(componentId, out QuickMenuDefinition value) || value == null)
		{
			value = new QuickMenuDefinition();
			config.QuickMenus[componentId] = value;
		}
		value.ComponentOrder = value.ComponentOrder.Where((string id) => !Configuration.IsQuickMenuComponentId(id)).ToList();
		return value;
	}

	private void DrawQuickMenuSettings(string componentId)
	{
		QuickMenuDefinition menu = GetQuickMenu(componentId);
		float num = GetExpandedSettingsWidth(260f);
		float width = Math.Min(520f, Math.Max(180f, num - 8f));
		DrawEditableTextButton("点击改名", string.IsNullOrWhiteSpace(menu.Name) ? "快捷菜单" : menu.Name.Trim(), "QuickMenuName_" + componentId, 92f, delegate(string value)
		{
			menu.Name = value.Trim();
		});
		SameLineOrWrap(108f);
		DrawQuickMenuAddItemButton(componentId, menu, 92f);
		SameLineOrWrap(192f);
		DrawIconPickerButton("更换图标", "QuickMenuIcon_" + componentId, menu.IconId, delegate(uint value)
		{
			menu.IconId = value;
		}, "使用默认图标", new Vector2(184f, 34f));
		ImGui.Dummy(new Vector2(1f, 8f));
		DrawQuickMenuItemPreview(componentId, menu, width);
	}

	private void DrawQuickMenuAddItemButton(string componentId, QuickMenuDefinition menu, float buttonWidth)
	{
		string text = "QuickMenuAdd_" + componentId;
		ImU8String label = new ImU8String(6, 1);
		label.AppendLiteral("添加功能##");
		label.AppendFormatted(componentId);
		if (ImGui.Button(label, new Vector2(buttonWidth, 26f)))
		{
			ImGui.OpenPopup(text);
		}
		if (!ImGui.BeginPopup(text))
		{
			return;
		}
		ImGui.TextDisabled("选择要放进菜单的功能");
		ImGui.Separator();
		foreach (TaskBarComponentDefinition item2 in TaskBarComponentDefinitions.Where(IsQuickMenuAllowedComponent))
		{
			if (DrawAddComponentCard(item2))
			{
				string item = CreateComponentInstanceId(item2.Id);
				menu.ComponentOrder.Add(item);
				saveConfig();
				ImGui.CloseCurrentPopup();
			}
		}
		ImGui.EndPopup();
	}

	private void DrawQuickMenuItemPreview(string componentId, QuickMenuDefinition menu, float width)
	{
		ApplyPendingQuickMenuItemRemoval(componentId, menu);
		int count = menu.ComponentOrder.Count;
		DrawCustomShortcutFieldLabel((count == 0) ? "菜单里还没有功能" : "菜单里的功能");
		float previewWidth = Math.Min(420f, Math.Max(180f, width));
		float rowHeight = ImGui.GetFrameHeight() + 4f;
		if (count == 0)
		{
			ImGui.TextDisabled("点击“添加功能”把常用项目放进弹出菜单。");
			return;
		}
		for (int i = 0; i < menu.ComponentOrder.Count; i++)
		{
			string text = menu.ComponentOrder[i];
			TaskBarComponentDefinition componentDefinition = GetComponentDefinition(text);
			if (!string.IsNullOrWhiteSpace(componentDefinition.Id) && DrawQuickMenuItemLine(componentId, componentDefinition, text, i, previewWidth, rowHeight, out var removeRequested) && removeRequested)
			{
				pendingQuickMenuItemRemovals[componentId] = text;
			}
		}
	}

	private bool DrawQuickMenuItemLine(string componentId, TaskBarComponentDefinition definition, string itemId, int index, float previewWidth, float rowHeight, out bool removeRequested)
	{
		ThemePalette effectiveTheme = GetEffectiveTheme();
		removeRequested = false;
		Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
		float frameHeight = ImGui.GetFrameHeight();
		bool flag = ComponentHasSettings(itemId);
		float num = 56f + (flag ? 56f : 0f);
		float x = Math.Max(90f, previewWidth - 26f - num);
		string text = $"{index + 1}.";
		Vector2 vector = ImGui.CalcTextSize(text);
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		windowDrawList.AddText(new Vector2(cursorScreenPos.X + 26f - 4f - vector.X, cursorScreenPos.Y + (frameHeight - vector.Y) * 0.5f + 1.5f), ImGui.GetColorU32(WithAlpha(effectiveTheme.TextMuted, 0.76f)), text);
		Vector2 vector2 = new Vector2(cursorScreenPos.X + 26f, cursorScreenPos.Y);
		Vector2 pMax = vector2 + new Vector2(x, frameHeight);
		windowDrawList.AddRectFilled(vector2, pMax, ImGui.GetColorU32(WithAlpha(effectiveTheme.FrameBg, 0.86f)), 4f);
		windowDrawList.AddRect(vector2, pMax, ImGui.GetColorU32(WithAlpha(effectiveTheme.Border, 0.4f)), 4f);
		Vector2 vector3 = ImGui.CalcTextSize(definition.Name);
		windowDrawList.AddText(new Vector2(vector2.X + 8f, vector2.Y + (frameHeight - vector3.Y) * 0.5f), ImGui.GetColorU32(effectiveTheme.Text), definition.Name);
		ImGui.SetCursorScreenPos(vector2);
		ImU8String strId = new ImU8String(22, 3);
		strId.AppendLiteral("##QuickMenuItemName_");
		strId.AppendFormatted(componentId);
		strId.AppendLiteral("_");
		strId.AppendFormatted(itemId);
		strId.AppendLiteral("_");
		strId.AppendFormatted(index);
		ImGui.InvisibleButton(strId, new Vector2(x, frameHeight));
		if (ImGui.IsItemHovered() && !string.IsNullOrWhiteSpace(definition.Description))
		{
			DrawStyledTooltip(definition.Description);
		}
		float num2 = pMax.X + 8f;
		if (flag)
		{
			ImGui.SetCursorScreenPos(new Vector2(num2, cursorScreenPos.Y));
			if (DrawQuickMenuItemSettingsButton($"QuickMenuItemSettings_{componentId}_{itemId}_{index}", new Vector2(48f, frameHeight)))
			{
				ImU8String strId2 = new ImU8String(22, 1);
				strId2.AppendLiteral("QuickMenuItemSettings_");
				strId2.AppendFormatted(itemId);
				ImGui.OpenPopup(strId2);
			}
			DrawComponentSettingsPopup("QuickMenuItemSettings_" + itemId, itemId);
			num2 += 56f;
		}
		ImGui.SetCursorScreenPos(new Vector2(num2, cursorScreenPos.Y));
		if (DrawCommandLineRemoveButton($"QuickMenuItemRemove_{componentId}_{itemId}_{index}", new Vector2(48f, frameHeight)))
		{
			removeRequested = true;
		}
		ImGui.SetCursorScreenPos(new Vector2(cursorScreenPos.X, cursorScreenPos.Y + rowHeight));
		return removeRequested;
	}

	private bool DrawQuickMenuItemSettingsButton(string id, Vector2 size)
	{
		ThemePalette effectiveTheme = GetEffectiveTheme();
		Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
		ImU8String strId = new ImU8String(2, 1);
		strId.AppendLiteral("##");
		strId.AppendFormatted(id);
		ImGui.InvisibleButton(strId, size);
		bool result = ImGui.IsItemClicked(ImGuiMouseButton.Left);
		bool flag = ImGui.IsItemHovered();
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		Vector2 pMax = cursorScreenPos + size;
		windowDrawList.AddRectFilled(cursorScreenPos, pMax, ImGui.GetColorU32(flag ? WithAlpha(effectiveTheme.HeaderHovered, 0.82f) : WithAlpha(effectiveTheme.Surface, 0.74f)), 5f);
		windowDrawList.AddRect(cursorScreenPos, pMax, ImGui.GetColorU32(WithAlpha(effectiveTheme.Border, flag ? 0.72f : 0.48f)), 5f);
		Vector2 vector = ImGui.CalcTextSize("设置");
		windowDrawList.AddText(cursorScreenPos + (size - vector) * 0.5f, ImGui.GetColorU32(WithAlpha(effectiveTheme.Text, 0.96f)), "设置");
		return result;
	}

	private void ApplyPendingQuickMenuItemRemoval(string componentId, QuickMenuDefinition menu)
	{
		if (pendingQuickMenuItemRemovals.Remove(componentId, out string value))
		{
			int num = menu.ComponentOrder.IndexOf(value);
			if (num >= 0)
			{
				menu.ComponentOrder.RemoveAt(num);
				RemoveRepeatableComponentData(value);
				saveConfig();
			}
		}
	}

	private static bool IsQuickMenuAllowedComponent(TaskBarComponentDefinition component)
	{
		switch (component.Id)
		{
		case "volume":
		case "plugin_list":
		case "plugin_shortcut":
		case "custom_shortcut":
		case "inventory":
		case "saddlebag":
			return true;
		default:
			return false;
		}
	}
}
