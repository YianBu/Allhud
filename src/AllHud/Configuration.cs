using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using AllHud.Data;
using Dalamud.Configuration;

namespace AllHud;

public sealed class Configuration : IPluginConfiguration
{
	private const int CurrentVersion = 77;

	public const string TaskBarComponentTime = "time";

	public const string TaskBarComponentLocalTime = "local_time";

	public const string TaskBarComponentEorzeaTime = "eorzea_time";

	public const string TaskBarComponentFps = "fps";

	public const string TaskBarComponentVolume = "volume";

	public const string TaskBarComponentMainMenu = "main_menu";

	public const string TaskBarComponentPluginList = "plugin_list";

	public const string TaskBarComponentPluginShortcut = "plugin_shortcut";

	public const string TaskBarComponentCustomShortcut = "custom_shortcut";

	public const string TaskBarComponentQuickMenu = "quick_menu";

	public const string TaskBarComponentServerInfo = "server_info";

	public const string TaskBarComponentInventory = "inventory";

	public const string TaskBarComponentSaddlebag = "saddlebag";

	public const string TaskBarComponentTeleport = "teleport";

	public const string TaskBarComponentCoordinates = "coordinates";

	public const string TaskBarComponentGearsetSwitcher = "gearset_switcher";

	public const string TaskBarComponentCurrency = "currency";

	public const string TaskBarComponentWalkingIndicator = "walking_indicator";

	public static readonly string[] DefaultTaskBarComponentOrder = new string[9] { "time", "fps", "volume", "main_menu", "plugin_list", "plugin_shortcut", "server_info", "inventory", "saddlebag" };

	private static readonly string[] KnownTaskBarComponentIds = new string[16]
	{
		"time", "fps", "volume", "main_menu", "plugin_list", "plugin_shortcut", "custom_shortcut", "quick_menu", "server_info", "inventory",
		"saddlebag", "teleport", "coordinates", "gearset_switcher", "currency", "walking_indicator"
	};

	private static readonly HashSet<string> KnownTaskBarComponentIdSet = KnownTaskBarComponentIds.ToHashSet<string>(StringComparer.OrdinalIgnoreCase);

	public static readonly Vector4 DefaultSelfAppliedTimerColor = new Vector4(0.45f, 1f, 0.55f, 1f);

	public static readonly Vector4 DefaultOtherAppliedTimerColor = new Vector4(1f, 1f, 1f, 1f);

	public int Version { get; set; } = 1;

	public AllHudThemeMode ThemeMode { get; set; }

	public float LiquidGlassOpacity { get; set; } = 0.62f;

	public float LiquidGlassBlurStrength { get; set; } = 4f;

	public float LiquidGlassTintStrength { get; set; }

	public Vector4 LiquidGlassTintColor { get; set; } = new Vector4(0.82f, 0.9f, 1f, 1f);

	public float LiquidGlassBrightness { get; set; } = 0.72f;

	public float LiquidGlassNoise { get; set; } = 0.025f;

	public float LiquidGlassRounding { get; set; } = 18f;

	public bool DalamudThemeFollowAllHud { get; set; } = true;

	public float DalamudThemeBackgroundOpacity { get; set; } = 0.482f;

	public float DalamudThemeBlurStrength { get; set; } = 4f;

	public float DalamudThemeTintStrength { get; set; }

	public Vector4 DalamudThemeTintColor { get; set; } = new Vector4(0.82f, 0.9f, 1f, 1f);

	public float DalamudThemeBrightness { get; set; } = 0.72f;

	public float DalamudThemeLuminosityStrength { get; set; } = 0.526f;

	public float DalamudThemeRounding { get; set; } = 18f;

	public string DalamudThemeManagedStyleName { get; set; } = string.Empty;

	public string DalamudThemeManagedStyleFingerprint { get; set; } = string.Empty;

	public bool DalamudThemeRestoreArmed { get; set; }

	public string? DalamudThemeOriginalChosenStyle { get; set; }

	public bool DalamudBlurRestoreArmed { get; set; }

	public float DalamudOriginalBlurStrength { get; set; }

	public float DalamudLastAppliedBlurStrength { get; set; }

	public bool Locked { get; set; }

	public bool TargetInfoLocked { get; set; }

	public bool StatusBarLocked { get; set; }

	public bool ShowStatusOverlay { get; set; }

	public bool ShowCustomTargetInfo { get; set; } = true;

	public Vector2 CustomTargetInfoPosition { get; set; } = new Vector2(560f, 160f);

	public float CustomTargetInfoWidth { get; set; } = 500f;

	public float CustomTargetInfoBackgroundOpacity { get; set; }

	public int CustomTargetInfoStatusRows { get; set; } = 2;

	public bool CustomTargetInfoStatusesAboveHp { get; set; }

	public bool CustomTargetInfoHideHpNumbers { get; set; }

	public bool CustomTargetInfoHideMaxHp { get; set; }

	public bool CustomTargetInfoSplitCastBar { get; set; }

	public bool CustomTargetInfoSplitStatusBar { get; set; }

	public Vector2 CustomTargetInfoCastBarPosition { get; set; } = new Vector2(760f, 220f);

	public Vector2 CustomTargetInfoStatusBarPosition { get; set; } = new Vector2(560f, 220f);

	public float CustomTargetInfoScale { get; set; } = 1f;

	public float CustomTargetInfoCastBarScale { get; set; } = 1f;

	public float CustomTargetInfoStatusBarScale { get; set; } = 1f;

	public int CustomTargetInfoCastBarPlacement { get; set; }

	public bool ShowSelfBuffs { get; set; } = true;

	public bool ShowSelfEnfeeblements { get; set; } = true;

	public bool ShowSelfOtherStatuses { get; set; } = true;

	public bool ShowSelfConditionalEnhancements { get; set; } = true;

	public bool ShowTargetDots { get; set; } = true;

	public int StatusBarLayoutMode { get; set; }

	public bool ShowPartyInfo { get; set; } = true;

	public bool ShowPartyMitigationCooldowns { get; set; } = true;

	public bool ShowPartyFoodCheck { get; set; }

	public bool ShowPartyLimitBreakBar { get; set; }

	public int PartyLimitBreakBarPosition { get; set; }

	public bool ShowTaskBar { get; set; }

	public int TaskBarEdge { get; set; }

	public bool TaskBarStretchToEdges { get; set; }

	public float TaskBarHorizontalOffset { get; set; } = 0.5f;

	public float TaskBarScale { get; set; } = 1f;

	public float TaskBarOpacity { get; set; } = 1f;

	public bool ShowAuxiliaryBar { get; set; }

	public int AuxiliaryBarPositionMode { get; set; }

	public float AuxiliaryBarScale { get; set; } = 1f;

	public float AuxiliaryBarOpacity { get; set; } = 1f;

	public List<AuxiliaryBarDefinition> AuxiliaryBars { get; set; } = new List<AuxiliaryBarDefinition>();

	public bool TaskBarShowLocalTime { get; set; } = true;

	public bool TaskBarShowEorzeaTime { get; set; } = true;

	public bool TaskBarShowJob { get; set; } = true;

	public bool TaskBarShowHpMp { get; set; } = true;

	public bool TaskBarShowTerritory { get; set; } = true;

	public bool TaskBarShowFps { get; set; } = true;

	public bool TaskBarShowMainMenu { get; set; } = true;

	public bool TaskBarShowVolume { get; set; } = true;

	public bool TaskBarShowPluginList { get; set; } = true;

	public bool TaskBarShowPluginShortcut { get; set; }

	public bool TaskBarShowServerInfoBar { get; set; } = true;

	public bool TaskBarShowTeleport { get; set; }

	public bool TaskBarShowCoordinates { get; set; }

	public bool TaskBarShowCoordinatesTerritory { get; set; } = true;

	public bool TaskBarShowCoordinatesPosition { get; set; } = true;

	public int TaskBarCoordinatesDisplayMode { get; set; }

	public bool TaskBarShowGearsetSwitcher { get; set; }

	public bool TaskBarGearsetShowNumber { get; set; } = true;

	public bool TaskBarGearsetShowName { get; set; }

	public bool TaskBarGearsetShowLevel { get; set; } = true;

	public bool TaskBarGearsetShowItemLevel { get; set; } = true;

	public bool TaskBarGearsetClosePopupOnSwitch { get; set; } = true;

	public bool TaskBarGearsetShowPopupHeader { get; set; } = true;

	public bool TaskBarGearsetShowGroupHeaders { get; set; } = true;

	public bool TaskBarGearsetEnableGroupCollapse { get; set; } = true;

	public bool TaskBarGearsetEnableGroupScrolling { get; set; } = true;

	public int TaskBarGearsetPopupColumns { get; set; } = 3;

	public int TaskBarGearsetMaxVisibleItemsPerGroup { get; set; } = 6;

	public int TaskBarGearsetButtonWidth { get; set; } = 210;

	public int TaskBarGearsetButtonHeight { get; set; } = 36;

	public float TaskBarGearsetPopupScale { get; set; } = 1f;

	public List<string> TaskBarGearsetCollapsedGroups { get; set; } = new List<string>();

	public List<string> TaskBarGearsetHiddenGroups { get; set; } = new List<string>();

	public List<uint> TaskBarGearsetExpandedJobIds { get; set; } = new List<uint>();

	public bool TaskBarShowCurrency { get; set; }

	public uint TaskBarCurrencyItemId { get; set; } = 1u;

	public bool TaskBarCurrencyShowName { get; set; }

	public bool TaskBarCurrencyShowCap { get; set; } = true;

	public bool TaskBarCurrencyShowWeeklyCap { get; set; } = true;

	public bool TaskBarCurrencyUseThresholdColors { get; set; }

	public int TaskBarCurrencyThresholdPercentage { get; set; } = 75;

	public List<uint> TaskBarCurrencyVisibleItemIds { get; set; } = new List<uint>();

	public string TaskBarCurrencyCustomItemIds { get; set; } = string.Empty;

	public bool TaskBarShowWalkingIndicator { get; set; }

	public bool TaskBarWalkingOnlyWhenWalking { get; set; }

	public bool ShowWorldMarkers { get; set; }

	public bool WorldMarkersShowFlag { get; set; } = true;

	public bool WorldMarkersShowWaymarks { get; set; } = true;

	public bool WorldMarkersShowCompass { get; set; } = true;

	public bool WorldMarkersShowLabels { get; set; } = true;

	public bool WorldMarkersShowDistance { get; set; } = true;

	public float WorldMarkersIconScale { get; set; } = 1f;

	public float WorldMarkersFadeDistance { get; set; } = 80f;

	public float WorldMarkersFadeAttenuation { get; set; } = 20f;

	public float WorldMarkersMaxVisibleDistance { get; set; }

	public Vector4 WorldMarkersColor { get; set; } = new Vector4(0.95f, 0.82f, 0.36f, 1f);

	public string TaskBarPluginShortcutInternalName { get; set; } = string.Empty;

	public List<string> PluginListInternalNames { get; set; } = new List<string>();

	public Dictionary<string, string> PluginShortcutInternalNames { get; set; } = new Dictionary<string, string>();

	public Dictionary<string, CustomShortcutDefinition> CustomShortcuts { get; set; } = new Dictionary<string, CustomShortcutDefinition>();

	public Dictionary<string, QuickMenuDefinition> QuickMenus { get; set; } = new Dictionary<string, QuickMenuDefinition>();

	public bool TaskBarShowInventory { get; set; }

	public bool TaskBarShowSaddlebag { get; set; }

	public bool TaskBarDownloadPluginIcons { get; set; }

	public int TaskBarServerInfoBarMode { get; set; }

	public bool ShowTargetMitigationCooldowns { get; set; } = true;

	public bool ShowPersonalMitigationCooldowns { get; set; } = true;

	public bool ShowMitigationCooldowns { get; set; } = true;

	public bool ShowRawStatusIds { get; set; }

	public bool OnlyShowSelfAppliedTargetStatuses { get; set; } = true;

	public bool HideExpiredCooldowns { get; set; }

	public bool ShowSourceJobNames { get; set; }

	public bool ShowStatusPreview { get; set; }

	public bool ShowTargetInfoPreview { get; set; }

	public bool ShowSelfCooldownBar { get; set; }

	public float SelfCooldownBarScale { get; set; } = 1f;

	public float SelfCooldownBarOpacity { get; set; } = 1f;

	public bool SelfCooldownBarLocked { get; set; }

	public bool SelfCooldownBarHideWhenReady { get; set; }

	public bool SelfCooldownBarSelfOnly { get; set; }

	public bool SelfCooldownBarHideSelf { get; set; }

	public int SelfCooldownBarLayoutDirection { get; set; }

	public bool ShowSelfCooldownBarPreview { get; set; }

	public bool ShowPartyInfoPreview { get; set; }

	public Vector2 SelfCooldownBarPosition { get; set; } = new Vector2(760f, 520f);

	public List<string> HiddenImGuiWindowNames { get; set; } = new List<string>();

	public List<string> TaskBarComponentOrder { get; set; } = DefaultTaskBarComponentOrder.ToList();

	public List<string> TaskBarLeftComponentOrder { get; set; } = new List<string>();

	public List<string> TaskBarCenterComponentOrder { get; set; } = new List<string>();

	public List<string> TaskBarRightComponentOrder { get; set; } = new List<string>();

	public float Scale { get; set; } = 1f;

	public float IconSize { get; set; } = 20f;

	public int StatusIconsPerRow { get; set; } = 8;

	public Vector4 SelfAppliedTimerColor { get; set; } = DefaultSelfAppliedTimerColor;

	public Vector4 OtherAppliedTimerColor { get; set; } = DefaultOtherAppliedTimerColor;

	public Vector2 StatusOverlayPosition { get; set; } = new Vector2(760f, 360f);

	public float MaxTargetStatusDurationSeconds { get; set; } = 120f;

	public float ExpiredCooldownGraceSeconds { get; set; } = 5f;

	public uint SelectedJobSkillConfigClassJobId { get; set; } = 19u;

	public bool JobSkillSelectionInitialized { get; set; }

	public List<string> EnabledJobSkillKeys { get; set; } = new List<string>();

	public bool JobActionSelectionInitialized { get; set; }

	public List<string> EnabledJobActionKeys { get; set; } = new List<string>();

	public List<CustomTrackedDefinition> CustomTrackedDefinitions { get; set; } = new List<CustomTrackedDefinition>();

	public bool ApplyMigrations()
	{
		bool changed = false;
		if (Version < 2)
		{
			ShowPartyMitigationCooldowns = true;
			ShowTargetMitigationCooldowns = true;
			ShowPersonalMitigationCooldowns = true;
			changed = true;
		}
		if (Version < 3)
		{
			bool flag = ShowPartyMitigationCooldowns || ShowTargetMitigationCooldowns;
			if (ShowPartyMitigationCooldowns != flag)
			{
				ShowPartyMitigationCooldowns = flag;
				changed = true;
			}
			if (ShowTargetMitigationCooldowns != flag)
			{
				ShowTargetMitigationCooldowns = flag;
				changed = true;
			}
			changed = true;
		}
		if (Version < 4)
		{
			if (StatusIconsPerRow <= 0)
			{
				StatusIconsPerRow = 8;
				changed = true;
			}
			if (SelfAppliedTimerColor == default(Vector4))
			{
				SelfAppliedTimerColor = DefaultSelfAppliedTimerColor;
				changed = true;
			}
			if (OtherAppliedTimerColor == default(Vector4))
			{
				OtherAppliedTimerColor = DefaultOtherAppliedTimerColor;
				changed = true;
			}
			changed = true;
		}
		if (Version < 5)
		{
			if (StatusOverlayPosition == default(Vector2))
			{
				StatusOverlayPosition = new Vector2(760f, 360f);
				changed = true;
			}
			changed = true;
		}
		if (Version < 6)
		{
			if (EnabledJobActionKeys == null)
			{
				List<string> list = (EnabledJobActionKeys = new List<string>());
			}
			AddMissingEnabledJobActionKey("31:2887", ref changed);
			AddMissingEnabledJobActionKey("25:157", ref changed);
			changed = true;
		}
		if (Version < 8)
		{
			if (EnabledJobActionKeys == null)
			{
				List<string> list = (EnabledJobActionKeys = new List<string>());
			}
			AddMissingEnabledJobActionKey("19:36920", ref changed);
			AddMissingEnabledJobActionKey("21:36923", ref changed);
			AddMissingEnabledJobActionKey("21:16464", ref changed);
			AddMissingEnabledJobActionKey("32:36927", ref changed);
			AddMissingEnabledJobActionKey("37:36935", ref changed);
			AddMissingEnabledJobActionKey("24:25861", ref changed);
			AddMissingEnabledJobActionKey("24:7432", ref changed);
			AddMissingEnabledJobActionKey("28:7434", ref changed);
			AddMissingEnabledJobActionKey("28:25867", ref changed);
			AddMissingEnabledJobActionKey("33:16556", ref changed);
			AddMissingEnabledJobActionKey("33:25873", ref changed);
			AddMissingEnabledJobActionKey("40:24303", ref changed);
			AddMissingEnabledJobActionKey("40:24305", ref changed);
			AddMissingEnabledJobActionKey("40:24317", ref changed);
			changed = true;
		}
		if (Version < 9)
		{
			if (EnabledJobActionKeys == null)
			{
				List<string> list = (EnabledJobActionKeys = new List<string>());
			}
			AddMissingEnabledJobActionKey("27:25799", ref changed);
			changed = true;
		}
		if (Version < 15)
		{
			ShowStatusPreview = false;
			changed = true;
		}
		if (Version < 20)
		{
			changed = true;
		}
		if (Version < 21)
		{
			ShowCustomTargetInfo = true;
			CustomTargetInfoPosition = new Vector2(560f, 160f);
			CustomTargetInfoWidth = 320f;
			ShowStatusOverlay = false;
			changed = true;
		}
		if (Version < 22)
		{
			CustomTargetInfoBackgroundOpacity = 0.22f;
			changed = true;
		}
		if (Version < 23)
		{
			CustomTargetInfoWidth = 480f;
			CustomTargetInfoBackgroundOpacity = 0f;
			changed = true;
		}
		if (Version < 24)
		{
			changed = true;
		}
		if (Version < 25)
		{
			CustomTargetInfoWidth = 640f;
			changed = true;
		}
		if (Version < 26)
		{
			CustomTargetInfoWidth = 560f;
			changed = true;
		}
		if (Version < 27)
		{
			CustomTargetInfoWidth = 500f;
			changed = true;
		}
		if (Version < 28)
		{
			CustomTargetInfoStatusRows = 2;
			CustomTargetInfoStatusesAboveHp = false;
			changed = true;
		}
		if (Version < 29)
		{
			CustomTargetInfoSplitCastBar = false;
			CustomTargetInfoSplitStatusBar = false;
			CustomTargetInfoCastBarPosition = new Vector2(760f, 220f);
			CustomTargetInfoStatusBarPosition = new Vector2(560f, 220f);
			CustomTargetInfoScale = 1f;
			CustomTargetInfoCastBarScale = 1f;
			CustomTargetInfoStatusBarScale = 1f;
			changed = true;
		}
		if (Version < 30)
		{
			CustomTargetInfoCastBarPlacement = 0;
			changed = true;
		}
		if (Version < 31)
		{
			ShowSelfEnfeeblements = true;
			ShowSelfOtherStatuses = true;
			ShowSelfConditionalEnhancements = true;
			changed = true;
		}
		if (Version < 32)
		{
			ShowPartyFoodCheck = false;
			changed = true;
		}
		if (Version < 33)
		{
			ShowPartyLimitBreakBar = false;
			PartyLimitBreakBarPosition = 0;
			changed = true;
		}
		if (Version < 34)
		{
			TargetInfoLocked = Locked;
			StatusBarLocked = Locked;
			changed = true;
		}
		if (Version < 35)
		{
			ShowTaskBar = false;
			TaskBarEdge = 0;
			TaskBarScale = 1f;
			TaskBarOpacity = 0.72f;
			TaskBarShowLocalTime = true;
			TaskBarShowEorzeaTime = true;
			TaskBarShowJob = true;
			TaskBarShowHpMp = true;
			TaskBarShowTerritory = true;
			TaskBarShowFps = true;
			TaskBarShowMainMenu = true;
			TaskBarShowVolume = true;
			TaskBarShowPluginList = true;
			TaskBarShowServerInfoBar = true;
			changed = true;
		}
		if (Version < 36)
		{
			int taskBarEdge = TaskBarEdge;
			if ((taskBarEdge < 0 || taskBarEdge > 1) ? true : false)
			{
				TaskBarEdge = 0;
				changed = true;
			}
		}
		if (Version < 37)
		{
			TaskBarDownloadPluginIcons = false;
			changed = true;
		}
		if (Version < 38)
		{
			TaskBarDownloadPluginIcons = true;
			changed = true;
		}
		if (Version < 39)
		{
			TaskBarStretchToEdges = false;
			changed = true;
		}
		if (Version < 40)
		{
			TaskBarServerInfoBarMode = 0;
			changed = true;
		}
		if (Version < 41)
		{
			changed = true;
		}
		if (Version < 43)
		{
			if (HiddenImGuiWindowNames == null)
			{
				List<string> list = (HiddenImGuiWindowNames = new List<string>());
			}
			changed = true;
		}
		if (Version < 44)
		{
			TaskBarComponentOrder = DefaultTaskBarComponentOrder.ToList();
			changed = true;
		}
		if (Version < 45)
		{
			TaskBarComponentOrder = MergeLegacyTimeComponents(TaskBarComponentOrder).ToList();
			TaskBarShowLocalTime = TaskBarShowLocalTime || TaskBarShowEorzeaTime;
			changed = true;
		}
		if (Version < 46)
		{
			ShowAuxiliaryBar = false;
			AuxiliaryBarPositionMode = 0;
			AuxiliaryBarScale = 1f;
			AuxiliaryBarOpacity = 1f;
			changed = true;
		}
		if (Version < 47)
		{
			int taskBarEdge = 1;
			List<AuxiliaryBarDefinition> list6 = new List<AuxiliaryBarDefinition>(taskBarEdge);
			CollectionsMarshal.SetCount(list6, taskBarEdge);
			CollectionsMarshal.AsSpan(list6)[0] = new AuxiliaryBarDefinition
			{
				Enabled = ShowAuxiliaryBar,
				Name = "辅助栏 1",
				PositionMode = AuxiliaryBarPositionMode,
				Scale = AuxiliaryBarScale,
				Opacity = AuxiliaryBarOpacity
			};
			AuxiliaryBars = list6;
			changed = true;
		}
		if (Version < 48)
		{
			if (Math.Abs(TaskBarOpacity - 0.72f) < 0.001f)
			{
				TaskBarOpacity = 1f;
			}
			changed = true;
		}
		if (Version < 49)
		{
			if (AuxiliaryBars == null)
			{
				List<AuxiliaryBarDefinition> list7 = (AuxiliaryBars = new List<AuxiliaryBarDefinition>());
			}
			foreach (AuxiliaryBarDefinition auxiliaryBar in AuxiliaryBars)
			{
				if (IsGeneratedAuxiliaryBarName(auxiliaryBar.Name))
				{
					auxiliaryBar.Name = "辅助栏";
				}
			}
			changed = true;
		}
		if (Version < 50)
		{
			if (Math.Abs(AuxiliaryBarOpacity - 0.72f) < 0.001f)
			{
				AuxiliaryBarOpacity = 1f;
			}
			if (AuxiliaryBars == null)
			{
				List<AuxiliaryBarDefinition> list7 = (AuxiliaryBars = new List<AuxiliaryBarDefinition>());
			}
			foreach (AuxiliaryBarDefinition auxiliaryBar2 in AuxiliaryBars)
			{
				if (Math.Abs(auxiliaryBar2.Opacity - 0.72f) < 0.001f)
				{
					auxiliaryBar2.Opacity = 1f;
				}
			}
			changed = true;
		}
		if (Version < 51)
		{
			AuxiliaryBars = CollapseDuplicateDefaultAuxiliaryBars(AuxiliaryBars);
			changed = true;
		}
		if (Version < 57)
		{
			List<string> list = TaskBarComponentOrder;
			TaskBarCenterComponentOrder = ((list != null && list.Count > 0) ? TaskBarComponentOrder.ToList() : DefaultTaskBarComponentOrder.ToList());
			changed = true;
		}
		if (Version < 58)
		{
			TaskBarComponentOrder = RemoveUnconfiguredPluginShortcutComponents(TaskBarComponentOrder);
			TaskBarLeftComponentOrder = RemoveUnconfiguredPluginShortcutComponents(TaskBarLeftComponentOrder);
			TaskBarCenterComponentOrder = RemoveUnconfiguredPluginShortcutComponents(TaskBarCenterComponentOrder);
			TaskBarRightComponentOrder = RemoveUnconfiguredPluginShortcutComponents(TaskBarRightComponentOrder);
			foreach (AuxiliaryBarDefinition auxiliaryBar3 in AuxiliaryBars)
			{
				auxiliaryBar3.ComponentOrder = RemoveUnconfiguredPluginShortcutComponents(auxiliaryBar3.ComponentOrder);
			}
			changed = true;
		}
		if (Version < 59)
		{
			foreach (AuxiliaryBarDefinition auxiliaryBar4 in AuxiliaryBars)
			{
				if (auxiliaryBar4.SectionCenterComponentOrder.Count == 0 && auxiliaryBar4.ComponentOrder.Count > 0)
				{
					auxiliaryBar4.SectionCenterComponentOrder = auxiliaryBar4.ComponentOrder.ToList();
				}
			}
			changed = true;
		}
		if (Version < 60)
		{
			TaskBarComponentOrder = RemoveBaseQuickMenuComponents(TaskBarComponentOrder);
			TaskBarLeftComponentOrder = RemoveBaseQuickMenuComponents(TaskBarLeftComponentOrder);
			TaskBarCenterComponentOrder = RemoveBaseQuickMenuComponents(TaskBarCenterComponentOrder);
			TaskBarRightComponentOrder = RemoveBaseQuickMenuComponents(TaskBarRightComponentOrder);
			foreach (AuxiliaryBarDefinition auxiliaryBar5 in AuxiliaryBars)
			{
				auxiliaryBar5.ComponentOrder = RemoveBaseQuickMenuComponents(auxiliaryBar5.ComponentOrder);
				auxiliaryBar5.SectionStartComponentOrder = RemoveBaseQuickMenuComponents(auxiliaryBar5.SectionStartComponentOrder);
				auxiliaryBar5.SectionCenterComponentOrder = RemoveBaseQuickMenuComponents(auxiliaryBar5.SectionCenterComponentOrder);
				auxiliaryBar5.SectionEndComponentOrder = RemoveBaseQuickMenuComponents(auxiliaryBar5.SectionEndComponentOrder);
			}
			changed = true;
		}
		if (Version < 61)
		{
			changed = true;
		}
		if (Version < 62)
		{
			TaskBarComponentOrder = RemoveOptionalTaskBarComponents(TaskBarComponentOrder);
			TaskBarLeftComponentOrder = RemoveOptionalTaskBarComponents(TaskBarLeftComponentOrder);
			TaskBarCenterComponentOrder = RemoveOptionalTaskBarComponents(TaskBarCenterComponentOrder);
			TaskBarRightComponentOrder = RemoveOptionalTaskBarComponents(TaskBarRightComponentOrder);
			foreach (AuxiliaryBarDefinition auxiliaryBar6 in AuxiliaryBars)
			{
				auxiliaryBar6.ComponentOrder = RemoveOptionalTaskBarComponents(auxiliaryBar6.ComponentOrder);
				auxiliaryBar6.SectionStartComponentOrder = RemoveOptionalTaskBarComponents(auxiliaryBar6.SectionStartComponentOrder);
				auxiliaryBar6.SectionCenterComponentOrder = RemoveOptionalTaskBarComponents(auxiliaryBar6.SectionCenterComponentOrder);
				auxiliaryBar6.SectionEndComponentOrder = RemoveOptionalTaskBarComponents(auxiliaryBar6.SectionEndComponentOrder);
			}
			TaskBarShowTeleport = false;
			TaskBarShowCoordinates = false;
			TaskBarShowGearsetSwitcher = false;
			TaskBarShowCurrency = false;
			changed = true;
		}
		if (Version < 63)
		{
			if (TaskBarCoordinatesDisplayMode == 1)
			{
				TaskBarShowCoordinatesTerritory = true;
				TaskBarShowCoordinatesPosition = false;
			}
			else if (TaskBarCoordinatesDisplayMode == 2)
			{
				TaskBarShowCoordinatesTerritory = false;
				TaskBarShowCoordinatesPosition = true;
			}
			else
			{
				TaskBarShowCoordinatesTerritory = true;
				TaskBarShowCoordinatesPosition = true;
			}
			changed = true;
		}
		if (Version < 64)
		{
			TaskBarGearsetShowNumber = true;
			TaskBarGearsetShowName = false;
			TaskBarGearsetClosePopupOnSwitch = true;
			changed = true;
		}
		if (Version < 65)
		{
			TaskBarGearsetShowLevel = true;
			changed = true;
		}
		if (Version < 66)
		{
			StatusBarLayoutMode = 0;
			changed = true;
		}
		if (Version < 67)
		{
			MigrateQuickTeleportCustomShortcutIcon();
			changed = true;
		}
		if (Version < 68)
		{
			if (EnabledJobActionKeys == null)
			{
				List<string> list = (EnabledJobActionKeys = new List<string>());
			}
			if (EnabledJobActionKeys.RemoveAll(IsPartyInfoActionKey) > 0)
			{
				changed = true;
			}
			changed = true;
		}
		if (Version < 69)
		{
			if (EnabledJobActionKeys == null)
			{
				List<string> list = (EnabledJobActionKeys = new List<string>());
			}
			if (EnabledJobActionKeys.RemoveAll(IsPartyInfoActionKey) > 0)
			{
				changed = true;
			}
			changed = true;
		}
		if (Version < 70)
		{
			SelfCooldownBarLayoutDirection = Math.Clamp(SelfCooldownBarLayoutDirection, 0, 1);
			changed = true;
		}
		if (Version < 71)
		{
			TaskBarGearsetShowPopupHeader = true;
			TaskBarGearsetShowGroupHeaders = true;
			TaskBarGearsetEnableGroupCollapse = true;
			TaskBarGearsetEnableGroupScrolling = true;
			TaskBarGearsetPopupColumns = 3;
			TaskBarGearsetMaxVisibleItemsPerGroup = 6;
			TaskBarGearsetCollapsedGroups = new List<string>();
			TaskBarGearsetHiddenGroups = new List<string>();
			changed = true;
		}
		if (Version < 72)
		{
			TaskBarGearsetShowItemLevel = true;
			TaskBarGearsetButtonWidth = 210;
			TaskBarGearsetButtonHeight = 36;
			changed = true;
		}
		if (Version < 73)
		{
			ThemeMode = AllHudThemeMode.Pink;
			changed = true;
		}
		if (Version < 74)
		{
			LiquidGlassOpacity = 0.62f;
			LiquidGlassBlurStrength = 4f;
			LiquidGlassTintStrength = 0f;
			LiquidGlassTintColor = new Vector4(0.82f, 0.9f, 1f, 1f);
			LiquidGlassBrightness = 0.72f;
			LiquidGlassNoise = 0.025f;
			LiquidGlassRounding = 18f;
			changed = true;
		}
		if (Version < 75)
		{
			DalamudThemeManagedStyleName = string.Empty;
			DalamudThemeManagedStyleFingerprint = string.Empty;
			DalamudThemeRestoreArmed = false;
			DalamudThemeOriginalChosenStyle = null;
			DalamudBlurRestoreArmed = false;
			DalamudOriginalBlurStrength = 0f;
			DalamudLastAppliedBlurStrength = 0f;
			changed = true;
		}
		if (Version < 76)
		{
			DalamudThemeFollowAllHud = true;
			CopyLiquidGlassToDalamudTheme();
			changed = true;
		}
		if (Version < 77)
		{
			TaskBarCurrencyShowCap = true;
			TaskBarCurrencyShowWeeklyCap = true;
			TaskBarCurrencyUseThresholdColors = false;
			TaskBarCurrencyThresholdPercentage = 75;
			if (TaskBarCurrencyVisibleItemIds == null)
			{
				List<uint> list12 = (TaskBarCurrencyVisibleItemIds = new List<uint>());
			}
			TaskBarCurrencyCustomItemIds = string.Empty;
			TaskBarShowWalkingIndicator = false;
			TaskBarWalkingOnlyWhenWalking = false;
			ShowWorldMarkers = false;
			WorldMarkersShowFlag = true;
			WorldMarkersShowWaymarks = true;
			WorldMarkersShowCompass = true;
			WorldMarkersShowLabels = true;
			WorldMarkersShowDistance = true;
			WorldMarkersIconScale = 1f;
			WorldMarkersFadeDistance = 80f;
			WorldMarkersFadeAttenuation = 20f;
			WorldMarkersMaxVisibleDistance = 0f;
			WorldMarkersColor = new Vector4(0.95f, 0.82f, 0.36f, 1f);
			changed = true;
		}
		StatusIconsPerRow = Math.Clamp(StatusIconsPerRow, 1, 12);
		StatusBarLayoutMode = Math.Clamp(StatusBarLayoutMode, 0, 1);
		IconSize = Math.Clamp(IconSize, 12f, 64f);
		CustomTargetInfoWidth = 500f;
		CustomTargetInfoBackgroundOpacity = Math.Clamp(CustomTargetInfoBackgroundOpacity, 0f, 0.8f);
		CustomTargetInfoStatusRows = Math.Clamp(CustomTargetInfoStatusRows, 1, 2);
		CustomTargetInfoScale = ClampHudScale(CustomTargetInfoScale);
		CustomTargetInfoCastBarScale = ClampHudScale(CustomTargetInfoCastBarScale);
		CustomTargetInfoStatusBarScale = ClampHudScale(CustomTargetInfoStatusBarScale);
		CustomTargetInfoCastBarPlacement = Math.Clamp(CustomTargetInfoCastBarPlacement, 0, 2);
		PartyLimitBreakBarPosition = Math.Clamp(PartyLimitBreakBarPosition, 0, 1);
		SelfCooldownBarLayoutDirection = Math.Clamp(SelfCooldownBarLayoutDirection, 0, 1);
		TaskBarEdge = ((TaskBarEdge == 1) ? 1 : 0);
		TaskBarServerInfoBarMode = Math.Clamp(TaskBarServerInfoBarMode, 0, 1);
		TaskBarScale = ClampHudScale(TaskBarScale);
		TaskBarOpacity = Math.Clamp(TaskBarOpacity, 0.15f, 1f);
		AuxiliaryBarPositionMode = Math.Clamp(AuxiliaryBarPositionMode, 0, 2);
		AuxiliaryBarScale = ClampHudScale(AuxiliaryBarScale);
		AuxiliaryBarOpacity = Math.Clamp(AuxiliaryBarOpacity, 0.15f, 1f);
		AuxiliaryBars = NormalizeAuxiliaryBars(AuxiliaryBars);
		AuxiliaryBarDefinition auxiliaryBarDefinition = AuxiliaryBars.FirstOrDefault();
		ShowAuxiliaryBar = auxiliaryBarDefinition?.Enabled ?? false;
		AuxiliaryBarPositionMode = auxiliaryBarDefinition?.PositionMode ?? 0;
		AuxiliaryBarScale = auxiliaryBarDefinition?.Scale ?? 1f;
		AuxiliaryBarOpacity = auxiliaryBarDefinition?.Opacity ?? 1f;
		HiddenImGuiWindowNames = (from name in HiddenImGuiWindowNames
			where !string.IsNullOrWhiteSpace(name)
			select name.Trim()).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToList();
		TaskBarComponentOrder = NormalizeTaskBarComponentOrder(TaskBarComponentOrder);
		TaskBarLeftComponentOrder = NormalizeTaskBarSectionComponentOrder(TaskBarLeftComponentOrder);
		TaskBarCenterComponentOrder = NormalizeTaskBarSectionComponentOrder(TaskBarCenterComponentOrder);
		TaskBarRightComponentOrder = NormalizeTaskBarSectionComponentOrder(TaskBarRightComponentOrder);
		TaskBarPluginShortcutInternalName = TaskBarPluginShortcutInternalName.Trim();
		TaskBarGearsetShowNumber = TaskBarGearsetShowNumber || (!TaskBarGearsetShowName && !TaskBarGearsetShowLevel && !TaskBarGearsetShowItemLevel);
		TaskBarGearsetPopupColumns = Math.Clamp(TaskBarGearsetPopupColumns, 1, 3);
		TaskBarGearsetMaxVisibleItemsPerGroup = Math.Clamp(TaskBarGearsetMaxVisibleItemsPerGroup, 2, 12);
		TaskBarGearsetButtonWidth = Math.Clamp(TaskBarGearsetButtonWidth, 160, 360);
		TaskBarGearsetButtonHeight = Math.Clamp(TaskBarGearsetButtonHeight, 28, 64);
		TaskBarCurrencyThresholdPercentage = Math.Clamp(TaskBarCurrencyThresholdPercentage, 0, 100);
		if (TaskBarCurrencyVisibleItemIds == null)
		{
			List<uint> list12 = (TaskBarCurrencyVisibleItemIds = new List<uint>());
		}
		TaskBarCurrencyVisibleItemIds = TaskBarCurrencyVisibleItemIds.Where((uint id) => id != 0).Distinct().Take(64)
			.ToList();
		TaskBarCurrencyCustomItemIds = NormalizeCurrencyCustomItemIds(TaskBarCurrencyCustomItemIds);
		WorldMarkersIconScale = Math.Clamp(WorldMarkersIconScale, 0.5f, 2.5f);
		WorldMarkersFadeDistance = Math.Clamp(WorldMarkersFadeDistance, 0f, 1000f);
		WorldMarkersFadeAttenuation = Math.Clamp(WorldMarkersFadeAttenuation, 1f, 1000f);
		WorldMarkersMaxVisibleDistance = Math.Clamp(WorldMarkersMaxVisibleDistance, 0f, 5000f);
		WorldMarkersColor = new Vector4(Math.Clamp(WorldMarkersColor.X, 0f, 1f), Math.Clamp(WorldMarkersColor.Y, 0f, 1f), Math.Clamp(WorldMarkersColor.Z, 0f, 1f), 1f);
		if (!Enum.IsDefined(ThemeMode))
		{
			ThemeMode = AllHudThemeMode.Pink;
		}
		LiquidGlassOpacity = Math.Clamp(LiquidGlassOpacity, 0.1f, 1f);
		LiquidGlassBlurStrength = Math.Clamp(LiquidGlassBlurStrength, 0f, 8f);
		LiquidGlassTintStrength = Math.Clamp(LiquidGlassTintStrength, 0f, 1f);
		LiquidGlassTintColor = new Vector4(Math.Clamp(LiquidGlassTintColor.X, 0f, 1f), Math.Clamp(LiquidGlassTintColor.Y, 0f, 1f), Math.Clamp(LiquidGlassTintColor.Z, 0f, 1f), 1f);
		LiquidGlassBrightness = Math.Clamp(LiquidGlassBrightness, 0.15f, 0.95f);
		LiquidGlassNoise = Math.Clamp(LiquidGlassNoise, 0f, 0.2f);
		LiquidGlassRounding = Math.Clamp(LiquidGlassRounding, 0f, 32f);
		DalamudThemeBackgroundOpacity = Math.Clamp(DalamudThemeBackgroundOpacity, 0f, 1f);
		DalamudThemeBlurStrength = Math.Clamp(DalamudThemeBlurStrength, 0f, 14f);
		DalamudThemeTintStrength = Math.Clamp(DalamudThemeTintStrength, 0f, 1f);
		DalamudThemeTintColor = new Vector4(Math.Clamp(DalamudThemeTintColor.X, 0f, 1f), Math.Clamp(DalamudThemeTintColor.Y, 0f, 1f), Math.Clamp(DalamudThemeTintColor.Z, 0f, 1f), 1f);
		DalamudThemeBrightness = Math.Clamp(DalamudThemeBrightness, 0f, 1f);
		DalamudThemeLuminosityStrength = Math.Clamp(DalamudThemeLuminosityStrength, 0f, 1f);
		DalamudThemeRounding = Math.Clamp(DalamudThemeRounding, 0f, 32f);
		DalamudThemeManagedStyleName = DalamudThemeManagedStyleName?.Trim() ?? string.Empty;
		DalamudThemeManagedStyleFingerprint = DalamudThemeManagedStyleFingerprint?.Trim() ?? string.Empty;
		DalamudThemeOriginalChosenStyle = (string.IsNullOrWhiteSpace(DalamudThemeOriginalChosenStyle) ? null : DalamudThemeOriginalChosenStyle.Trim());
		DalamudOriginalBlurStrength = Math.Clamp(DalamudOriginalBlurStrength, 0f, 1f);
		DalamudLastAppliedBlurStrength = Math.Clamp(DalamudLastAppliedBlurStrength, 0f, 1f);
		TaskBarGearsetCollapsedGroups = NormalizeGearsetGroupNames(TaskBarGearsetCollapsedGroups);
		TaskBarGearsetHiddenGroups = NormalizeGearsetGroupNames(TaskBarGearsetHiddenGroups);
		PluginListInternalNames = NormalizePluginListInternalNames(PluginListInternalNames);
		PluginShortcutInternalNames = NormalizePluginShortcutInternalNames(PluginShortcutInternalNames);
		CustomShortcuts = NormalizeCustomShortcuts(CustomShortcuts);
		QuickMenus = NormalizeQuickMenus(QuickMenus);
		if (changed)
		{
			Version = 77;
		}
		return changed;
	}

	internal DalamudThemeStyleParameters GetEffectiveDalamudThemeParameters()
	{
		if (!DalamudThemeFollowAllHud)
		{
			return new DalamudThemeStyleParameters(DalamudThemeBackgroundOpacity, DalamudThemeBackgroundOpacity, DalamudThemeBlurStrength, DalamudThemeTintStrength, DalamudThemeTintColor, DalamudThemeBrightness, DalamudThemeLuminosityStrength, DalamudThemeRounding);
		}
		return GetLiquidGlassDalamudThemeParameters();
	}

	internal void CopyLiquidGlassToDalamudTheme()
	{
		DalamudThemeStyleParameters liquidGlassDalamudThemeParameters = GetLiquidGlassDalamudThemeParameters();
		DalamudThemeBackgroundOpacity = liquidGlassDalamudThemeParameters.WindowOpacity;
		DalamudThemeBlurStrength = liquidGlassDalamudThemeParameters.BlurStrength;
		DalamudThemeTintStrength = liquidGlassDalamudThemeParameters.TintStrength;
		DalamudThemeTintColor = liquidGlassDalamudThemeParameters.TintColor;
		DalamudThemeBrightness = liquidGlassDalamudThemeParameters.Brightness;
		DalamudThemeLuminosityStrength = liquidGlassDalamudThemeParameters.LuminosityStrength;
		DalamudThemeRounding = liquidGlassDalamudThemeParameters.Rounding;
	}

	private DalamudThemeStyleParameters GetLiquidGlassDalamudThemeParameters()
	{
		float num = Math.Clamp(LiquidGlassOpacity, 0.1f, 1f);
		float num2 = Math.Clamp(LiquidGlassTintStrength, 0f, 1f);
		Vector4 liquidGlassTintColor = LiquidGlassTintColor;
		float num3 = 0.2126f * liquidGlassTintColor.X + 0.7152f * liquidGlassTintColor.Y + 0.0722f * liquidGlassTintColor.Z;
		float num4 = Math.Max(Math.Clamp((LiquidGlassBrightness - 0.55f) / 0.4f, 0f, 1f), num2 * Math.Clamp((num3 - 0.55f) / 0.45f, 0f, 1f));
		float windowOpacity = Math.Clamp(0.2f + 0.4f * num + 0.08f * num4, 0.24f, 0.64f);
		return new DalamudThemeStyleParameters(num, windowOpacity, Math.Clamp(LiquidGlassBlurStrength, 0f, 14f), num2, liquidGlassTintColor, Math.Clamp(LiquidGlassBrightness, 0f, 1f), Math.Clamp(0.34f + 0.3f * num, 0.34f, 0.64f), Math.Clamp(LiquidGlassRounding, 0f, 32f));
	}

	private static List<string> NormalizeGearsetGroupNames(List<string>? groups)
	{
		return (from @group in groups ?? new List<string>()
			where !string.IsNullOrWhiteSpace(@group)
			select @group.Trim()).Distinct<string>(StringComparer.Ordinal).ToList();
	}

	private void AddMissingEnabledJobActionKey(string key, ref bool changed)
	{
		if (!EnabledJobActionKeys.Any((string existing) => string.Equals(existing, key, StringComparison.Ordinal)))
		{
			EnabledJobActionKeys.Add(key);
			changed = true;
		}
	}

	private static bool IsPartyInfoActionKey(string key)
	{
		int num = key.LastIndexOf(':');
		if (num < 0 || num >= key.Length - 1)
		{
			return false;
		}
		if (uint.TryParse(key.AsSpan(num + 1), out var result))
		{
			return TrackedActionCatalog.PartyMitigationActionIds.Contains(result);
		}
		return false;
	}

	private static void AddMissingComponent(List<string> componentOrder, string componentId, ref bool changed)
	{
		if (!componentOrder.Any((string id) => id.Equals(componentId, StringComparison.OrdinalIgnoreCase)))
		{
			componentOrder.Add(componentId);
			changed = true;
		}
	}

	private static float ClampHudScale(float scale)
	{
		return Math.Clamp((scale <= 0f) ? 1f : scale, 0.6f, 2f);
	}

	private List<AuxiliaryBarDefinition> NormalizeAuxiliaryBars(IEnumerable<AuxiliaryBarDefinition>? bars)
	{
		List<AuxiliaryBarDefinition> bars2 = (bars ?? Array.Empty<AuxiliaryBarDefinition>()).Where((AuxiliaryBarDefinition bar) => bar != null).Select((AuxiliaryBarDefinition bar, int index) => new AuxiliaryBarDefinition
		{
			Enabled = bar.Enabled,
			Name = ((string.IsNullOrWhiteSpace(bar.Name) || IsGeneratedAuxiliaryBarName(bar.Name)) ? "辅助栏" : bar.Name.Trim()),
			PositionMode = Math.Clamp(bar.PositionMode, 0, 2),
			StretchToEdges = bar.StretchToEdges,
			CustomPosition = ((bar.CustomPosition == default(Vector2)) ? new Vector2(120f, 240f) : bar.CustomPosition),
			Scale = ClampHudScale(bar.Scale),
			Opacity = Math.Clamp((bar.Opacity <= 0f) ? 1f : bar.Opacity, 0.15f, 1f),
			LayoutDirection = Math.Clamp(bar.LayoutDirection, 0, 1),
			ComponentOrder = NormalizeAuxiliaryComponentOrder(RemoveUnconfiguredPluginShortcutComponents(bar.ComponentOrder)),
			SectionStartComponentOrder = NormalizeAuxiliaryComponentOrder(RemoveUnconfiguredPluginShortcutComponents(bar.SectionStartComponentOrder)),
			SectionCenterComponentOrder = NormalizeAuxiliaryComponentOrder(RemoveUnconfiguredPluginShortcutComponents(bar.SectionCenterComponentOrder)),
			SectionEndComponentOrder = NormalizeAuxiliaryComponentOrder(RemoveUnconfiguredPluginShortcutComponents(bar.SectionEndComponentOrder))
		}).ToList();
		bars2 = CollapseDuplicateDefaultAuxiliaryBars(bars2);
		if (bars2.Count == 0)
		{
			bars2.Add(new AuxiliaryBarDefinition());
		}
		return bars2;
	}

	private static List<AuxiliaryBarDefinition> CollapseDuplicateDefaultAuxiliaryBars(IEnumerable<AuxiliaryBarDefinition>? bars)
	{
		List<AuxiliaryBarDefinition> list = new List<AuxiliaryBarDefinition>();
		bool flag = false;
		foreach (AuxiliaryBarDefinition item in bars ?? Array.Empty<AuxiliaryBarDefinition>())
		{
			if (IsDefaultAuxiliaryBar(item))
			{
				if (flag)
				{
					continue;
				}
				flag = true;
			}
			list.Add(item);
		}
		return list;
	}

	private static bool IsDefaultAuxiliaryBar(AuxiliaryBarDefinition bar)
	{
		if (!bar.Enabled && string.Equals(bar.Name?.Trim(), "辅助栏", StringComparison.Ordinal) && bar.PositionMode == 0 && !bar.StretchToEdges && bar.ComponentOrder.Count == 0 && Math.Abs(bar.Scale - 1f) < 0.001f)
		{
			return Math.Abs(bar.Opacity - 1f) < 0.001f;
		}
		return false;
	}

	public static bool IsPluginShortcutComponentId(string componentId)
	{
		if (!componentId.Equals("plugin_shortcut", StringComparison.OrdinalIgnoreCase))
		{
			return componentId.StartsWith("plugin_shortcut:", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	public static bool IsCustomShortcutComponentId(string componentId)
	{
		if (!componentId.Equals("custom_shortcut", StringComparison.OrdinalIgnoreCase))
		{
			return componentId.StartsWith("custom_shortcut:", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	public static bool IsQuickMenuComponentId(string componentId)
	{
		if (!componentId.Equals("quick_menu", StringComparison.OrdinalIgnoreCase))
		{
			return componentId.StartsWith("quick_menu:", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	public static bool IsRepeatableComponentId(string componentId)
	{
		if (!IsPluginShortcutComponentId(componentId) && !IsCustomShortcutComponentId(componentId))
		{
			return IsQuickMenuComponentId(componentId);
		}
		return true;
	}

	public static string GetComponentBaseId(string componentId)
	{
		if (IsPluginShortcutComponentId(componentId))
		{
			return "plugin_shortcut";
		}
		if (IsCustomShortcutComponentId(componentId))
		{
			return "custom_shortcut";
		}
		if (!IsQuickMenuComponentId(componentId))
		{
			return componentId;
		}
		return "quick_menu";
	}

	public static string CreatePluginShortcutComponentId()
	{
		return $"{"plugin_shortcut"}:{Guid.NewGuid():N}";
	}

	public static string CreateCustomShortcutComponentId()
	{
		return $"{"custom_shortcut"}:{Guid.NewGuid():N}";
	}

	public static string CreateQuickMenuComponentId()
	{
		return $"{"quick_menu"}:{Guid.NewGuid():N}";
	}

	public static List<string> NormalizeAuxiliaryComponentOrder(IEnumerable<string>? componentOrder)
	{
		return NormalizeTaskBarSectionComponentOrder(componentOrder);
	}

	public static List<string> NormalizeTaskBarSectionComponentOrder(IEnumerable<string>? componentOrder)
	{
		List<string> list = new List<string>();
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string item in from id in MergeLegacyTimeComponents(componentOrder)
			where !string.IsNullOrWhiteSpace(id)
			select id.Trim() into id
			where !id.Equals("quick_menu", StringComparison.OrdinalIgnoreCase)
			where KnownTaskBarComponentIdSet.Contains(GetComponentBaseId(id))
			select id)
		{
			if (IsRepeatableComponentId(item) || hashSet.Add(item))
			{
				list.Add(item);
			}
		}
		return list;
	}

	private List<string> RemoveUnconfiguredPluginShortcutComponents(IEnumerable<string>? componentOrder)
	{
		return (componentOrder ?? Array.Empty<string>()).Where((string id) => !IsPluginShortcutComponentId(id) || HasConfiguredPluginShortcut(id)).ToList();
	}

	private static List<string> RemoveBaseQuickMenuComponents(IEnumerable<string>? componentOrder)
	{
		return (componentOrder ?? Array.Empty<string>()).Where((string id) => !string.Equals(id?.Trim(), "quick_menu", StringComparison.OrdinalIgnoreCase)).ToList();
	}

	private static List<string> RemoveOptionalTaskBarComponents(IEnumerable<string>? componentOrder)
	{
		return (componentOrder ?? Array.Empty<string>()).Where((string id) => !IsOptionalTaskBarComponentId(id?.Trim() ?? string.Empty)).ToList();
	}

	private static string NormalizeCurrencyCustomItemIds(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return string.Empty;
		}
		return string.Join(',', (from part in value.Split(',', StringSplitOptions.RemoveEmptyEntries)
			select uint.TryParse(part.Trim(), out var result) ? result : 0u into id
			where id != 0
			select id).Distinct());
	}

	private static bool IsOptionalTaskBarComponentId(string componentId)
	{
		if (!componentId.Equals("teleport", StringComparison.OrdinalIgnoreCase) && !componentId.Equals("coordinates", StringComparison.OrdinalIgnoreCase) && !componentId.Equals("gearset_switcher", StringComparison.OrdinalIgnoreCase))
		{
			return componentId.Equals("currency", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private bool HasConfiguredPluginShortcut(string componentId)
	{
		if (componentId.Equals("plugin_shortcut", StringComparison.OrdinalIgnoreCase))
		{
			return !string.IsNullOrWhiteSpace(TaskBarPluginShortcutInternalName);
		}
		if (PluginShortcutInternalNames.TryGetValue(componentId, out string value))
		{
			return !string.IsNullOrWhiteSpace(value);
		}
		return false;
	}

	public static List<string> NormalizeTaskBarComponentOrder(IEnumerable<string>? componentOrder)
	{
		List<string> list = new List<string>();
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string item in from id in MergeLegacyTimeComponents(componentOrder)
			where !string.IsNullOrWhiteSpace(id)
			select id.Trim() into id
			where !id.Equals("quick_menu", StringComparison.OrdinalIgnoreCase)
			where KnownTaskBarComponentIdSet.Contains(GetComponentBaseId(id))
			select id)
		{
			if (IsRepeatableComponentId(item) || hashSet.Add(item))
			{
				list.Add(item);
			}
		}
		string[] defaultTaskBarComponentOrder = DefaultTaskBarComponentOrder;
		foreach (string text in defaultTaskBarComponentOrder)
		{
			if (!list.Contains<string>(text, StringComparer.OrdinalIgnoreCase))
			{
				list.Add(text);
			}
		}
		return list;
	}

	private static Dictionary<string, string> NormalizePluginShortcutInternalNames(Dictionary<string, string>? shortcuts)
	{
		return (shortcuts ?? new Dictionary<string, string>()).Where<KeyValuePair<string, string>>((KeyValuePair<string, string> pair) => IsPluginShortcutComponentId(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value)).ToDictionary<KeyValuePair<string, string>, string, string>((KeyValuePair<string, string> pair) => pair.Key.Trim(), (KeyValuePair<string, string> pair) => pair.Value.Trim(), StringComparer.OrdinalIgnoreCase);
	}

	private static Dictionary<string, CustomShortcutDefinition> NormalizeCustomShortcuts(Dictionary<string, CustomShortcutDefinition>? shortcuts)
	{
		Dictionary<string, CustomShortcutDefinition> dictionary = new Dictionary<string, CustomShortcutDefinition>(StringComparer.OrdinalIgnoreCase);
		foreach (KeyValuePair<string, CustomShortcutDefinition> item in shortcuts ?? new Dictionary<string, CustomShortcutDefinition>())
		{
			string text = item.Key?.Trim() ?? string.Empty;
			if (IsCustomShortcutComponentId(text) && !text.Equals("custom_shortcut", StringComparison.OrdinalIgnoreCase))
			{
				CustomShortcutDefinition customShortcutDefinition = item.Value ?? new CustomShortcutDefinition();
				dictionary[text] = new CustomShortcutDefinition
				{
					Name = (string.IsNullOrWhiteSpace(customShortcutDefinition.Name) ? "快捷方式" : customShortcutDefinition.Name.Trim()),
					IconId = customShortcutDefinition.IconId,
					Command = (customShortcutDefinition.Command ?? string.Empty).Trim()
				};
			}
		}
		return dictionary;
	}

	private void MigrateQuickTeleportCustomShortcutIcon()
	{
		foreach (CustomShortcutDefinition value in CustomShortcuts.Values)
		{
			if (value.IconId == 60314 && string.Equals((value.Command ?? string.Empty).Trim(), "/pdrtp", StringComparison.OrdinalIgnoreCase))
			{
				value.IconId = 60453u;
			}
		}
	}

	private static Dictionary<string, QuickMenuDefinition> NormalizeQuickMenus(Dictionary<string, QuickMenuDefinition>? menus)
	{
		Dictionary<string, QuickMenuDefinition> dictionary = new Dictionary<string, QuickMenuDefinition>(StringComparer.OrdinalIgnoreCase);
		foreach (KeyValuePair<string, QuickMenuDefinition> item in menus ?? new Dictionary<string, QuickMenuDefinition>())
		{
			string text = item.Key?.Trim() ?? string.Empty;
			if (IsQuickMenuComponentId(text) && !text.Equals("quick_menu", StringComparison.OrdinalIgnoreCase))
			{
				QuickMenuDefinition quickMenuDefinition = item.Value ?? new QuickMenuDefinition();
				dictionary[text] = new QuickMenuDefinition
				{
					Name = (string.IsNullOrWhiteSpace(quickMenuDefinition.Name) ? "快捷菜单" : quickMenuDefinition.Name.Trim()),
					IconId = quickMenuDefinition.IconId,
					ComponentOrder = (from id in NormalizeTaskBarSectionComponentOrder(quickMenuDefinition.ComponentOrder)
						where !IsQuickMenuComponentId(id)
						select id).ToList()
				};
			}
		}
		return dictionary;
	}

	private static List<string> NormalizePluginListInternalNames(IEnumerable<string>? internalNames)
	{
		List<string> list = new List<string>();
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string item in internalNames ?? Array.Empty<string>())
		{
			string text = item?.Trim() ?? string.Empty;
			if (text.Length > 0 && hashSet.Add(text))
			{
				list.Add(text);
			}
		}
		return list;
	}

	private static IEnumerable<string> MergeLegacyTimeComponents(IEnumerable<string>? componentOrder)
	{
		bool emittedTime = false;
		foreach (string item in componentOrder ?? Array.Empty<string>())
		{
			string text = item?.Trim() ?? string.Empty;
			if (text.Equals("local_time", StringComparison.OrdinalIgnoreCase) || text.Equals("eorzea_time", StringComparison.OrdinalIgnoreCase))
			{
				if (!emittedTime)
				{
					emittedTime = true;
					yield return "time";
				}
			}
			else
			{
				yield return text;
			}
		}
	}

	private static bool IsGeneratedAuxiliaryBarName(string? name)
	{
		int result;
		if (!string.IsNullOrWhiteSpace(name) && name.Trim().StartsWith("辅助栏 ", StringComparison.Ordinal))
		{
			return int.TryParse(name.Trim().Substring("辅助栏 ".Length), out result);
		}
		return false;
	}
}
