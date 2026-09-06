using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using AllHud.Data;
using AllHud.Models;
using AllHud.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Addon.Events;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Config;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Inventory;
using Dalamud.Game.NativeWrapper;
using Dalamud.Interface;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Arrays;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;

namespace AllHud.Windows;

public sealed class OverlayRenderer
{
	private readonly record struct NativePartyMemberAnchor(Vector2 RowMin, float RowRight, float RowH, bool HasJobIconAnchor);

	private sealed record PartyInfoLookupMaps(IReadOnlyDictionary<uint, PartyCooldownGroupEntry> GroupsByEntityId, IReadOnlyDictionary<ulong, PartyCooldownGroupEntry> GroupsByObjectId, IReadOnlyDictionary<int, PartyCooldownGroupEntry> GroupsByPartySlot, IReadOnlyDictionary<uint, PartyFoodStatusEntry> FoodByEntityId, IReadOnlyDictionary<ulong, PartyFoodStatusEntry> FoodByObjectId, IReadOnlyDictionary<int, PartyFoodStatusEntry> FoodByPartySlot);

	private sealed record PartyInfoRenderSnapshot(bool ShowMitigations, bool ShowFoodCheck, bool ShowLimitBreak, IReadOnlyList<PartyCooldownGroupEntry> Groups, IReadOnlyList<PartyFoodStatusEntry> FoodStatuses, PartyInfoLookupMaps Lookups);

	private sealed record NativePartyAnchorSnapshot(NativePartyMemberAnchor?[] Anchors, float? FallbackJobIconLeft, float FallbackJobIconHeight);

	private readonly record struct CachedHiddenImGuiWindow(string Key, ImGuiWindowPtr Window);

	private readonly record struct StatusIconSection(string Label, Vector4 Color, IReadOnlyList<StatusEntry> Statuses);

	private readonly record struct TargetStatusIconLayout(StatusEntry Status, Vector2 Offset, float Size, int Index);

	private readonly record struct LimitBreakGaugeSnapshot(float Current, float Max, int Segments);

	private readonly record struct PluginListEntry(string InternalName, IExposedPlugin? Plugin);

	private readonly record struct PluginIconPathCacheEntry(string? Path, DateTime RetryAt);

	private readonly record struct DtrTaskBarSnapshot(string Title, string Text, string Tooltip, bool HasClickAction, Action<DtrInteractionEvent>? OnClick);

	private readonly record struct GearsetPopupEntry(byte Id, string JobName, uint ClassJobId, short JobLevel, short ItemLevel, string Group, int GroupSort, int JobSort, uint IconId, bool Selected);

	private readonly record struct GearsetPopupJobGroup(uint ClassJobId, string JobName, int JobSort, uint IconId, List<GearsetPopupEntry> Entries);

	private readonly record struct GearsetPopupGroup(string Group, List<GearsetPopupJobGroup> Jobs);

	private readonly record struct GearsetPopupCache(int Fingerprint, int CurrentGearsetIndex, DateTime RefreshAt, List<GearsetPopupEntry> Entries, List<GearsetPopupGroup> Groups);

	private const float GearsetJobHeaderScale = 26f;

	private const float GearsetJobGapScale = 8f;

	private const float GearsetJobRowPadScale = 4f;

	private readonly record struct GearsetJobMetadata(string Name, int ExpArrayIndex, string Group, int GroupSort, int JobSort, uint IconId);

	private readonly record struct GearsetTaskBarCache(int GearsetId, uint ClassJobId, short JobLevel, short ItemLevel, bool ShowNumber, bool ShowName, bool ShowLevel, bool ShowItemLevel, string Text, string MeasureText, uint IconId);

	private readonly record struct StatusSectionLayout(StatusIconSection Section, int Columns, Vector2 GridSize, Vector2 Size);

	private readonly record struct GameIconCacheKey(uint IconId, bool HiRes);

	private enum TaskBarDrawIcon
	{
		None,
		MainMenu,
		Volume,
		PluginList,
		QuickMenu,
		Walking
	}

	private readonly record struct TaskBarItem(string Text, string Tooltip, Action<DtrInteractionEvent>? OnClick, Action<DtrInteractionEvent>? OnRightClick = null, string PopupId = "", bool AdjustVolumeOnWheel = false, bool IsIcon = false, string MeasureText = "", bool IsDalamudIcon = false, uint GameIconId = 0u, float TextScale = 1f, string PluginShortcutInternalName = "", IExposedPlugin? PluginShortcutPlugin = null, TaskBarDrawIcon DrawIcon = TaskBarDrawIcon.None, string QuickMenuComponentId = "", Vector4? TextColor = null);

	private readonly record struct GearsetDisplayInfo(byte GearsetId, string Name, uint ClassJobId, short ItemLevel);

	private readonly record struct TaskBarPopupAnchor(Vector2 Min, Vector2 Max);

	private readonly record struct TaskBarRowMetrics(float Width, float Height);

	private readonly record struct TaskBarMainMenuCategory(uint RowId, string Name, uint IconId, List<TaskBarMainMenuEntry> Entries);

	private readonly record struct TaskBarMainMenuEntry(string Name, uint? CommandId, string ChatCommand, uint IconId, bool IsSeparator = false);

	private sealed class EmptyDisposable : IDisposable
	{
		public static readonly EmptyDisposable Instance = new EmptyDisposable();

		public void Dispose()
		{
		}
	}

	private sealed class StyleScope : IDisposable
	{
		private readonly int colorCount;

		private readonly int varCount;

		public StyleScope(int colorCount, int varCount = 0)
		{
			this.colorCount = colorCount;
			this.varCount = varCount;
		}

		public void Dispose()
		{
			if (varCount > 0)
			{
				ImGui.PopStyleVar(varCount);
			}
			if (colorCount > 0)
			{
				ImGui.PopStyleColor(colorCount);
			}
		}
	}

	private readonly record struct SelfCooldownVisibleGroup(PartyCooldownGroupEntry Entry, IReadOnlyList<CooldownEntry> Cooldowns);

	private sealed record SelfCooldownLayoutCacheKey(IReadOnlyList<PartyCooldownGroupEntry> Groups, bool HideWhenReady, bool Preview, int LayoutDirection, float IconSize, float Spacing, float RowGap, float Pad, float ViewportWidth);

	private sealed record SelfCooldownLayoutCache(IReadOnlyList<SelfCooldownVisibleGroup> VisibleGroups, IReadOnlyList<SelfCooldownHorizontalRow> HorizontalRows);

	private readonly record struct SelfCooldownHorizontalRow(IReadOnlyList<SelfCooldownVisibleGroup> Groups, float Width);

	private readonly record struct QuickMenuRuntimeItem(string Label, TaskBarItem Item);

	private sealed record CurrencyDisplayInfo(uint ItemId, string Name, uint IconId);

	private readonly record struct WorldMarkerRenderInfo(string Key, string Label, Vector3 Position, uint IconId, bool ShowOnCompass);

	private const float MinimumIconSize = 12f;

	private const float MaximumIconSize = 64f;

	private const float NativeStatusIconSize = 38f;

	private const float LongStatusThresholdSeconds = 300f;

	private const float TaskBarPluginShortcutIconSize = 36f;

	private const float TaskBarGameOnlyIconSize = 32f;

	private const uint FoodStatusId = 48u;

	private const int MaxLiveStatusIcons = 24;

	private const int MaxPreviewStatusIconsPerSection = 8;

	private const int MaxSelfStatusEnhancements = 20;

	private const int MaxSelfStatusEnfeeblements = 20;

	private const int MaxSelfStatusOthers = 20;

	private const int NativeStatusIconsPerRow = 10;

	private const ulong PreviewSourceObjectIdBase = 13332480uL;

	private const float TaskBarBaseFontSize = 20f;

	private const float MaxCooldownIconTimeSeconds = 1800f;

	private const string CoordinatesPopupId = "AllHud 坐标";

	private const string GearsetPopupId = "AllHud 套装";

	private const string CurrencyPopupId = "AllHud 货币";

	private const uint CurrencyItemGil = 1u;

	private const uint CurrencyItemMgp = 29u;

	private const uint CurrencyItemVenture = 21072u;

	private const uint CurrencyItemWolfMarks = 25u;

	private const uint CurrencyItemPoetics = 28u;

	private const uint CurrencyItemBiColorGemstones = 26807u;

	private const uint CurrencyItemAlliedSeals = 27u;

	private const uint CurrencyItemCenturioSeals = 10307u;

	private const uint CurrencyItemMaelstromSeals = 20u;

	private const uint CurrencyItemTwinAdderSeals = 21u;

	private const uint CurrencyItemImmortalFlamesSeals = 22u;

	private const uint CurrencyItemSackOfNuts = 26533u;

	private const uint CurrencyItemTrophyCrystals = 36656u;

	private const uint CurrencyItemPurpleCrafterScrips = 33913u;

	private const uint CurrencyItemPurpleGathererScrips = 33914u;

	private const uint CurrencyItemOrangeCrafterScrips = 41784u;

	private const uint CurrencyItemOrangeGathererScrips = 41785u;

	private const uint CurrencyItemSkybuildersScrips = 28063u;

	private const uint BlueMageClassJobId = 36u;

	private const string EmbeddedDalamudIconPngBase64 = "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAQsSURBVFhHxZbfaxNZFMezLkXdZRUqUkV3NTLVJkMSrIUkahIZ6kNbC6KyXasi9kGIixalv2wSFbfrg/+A4IM+iA+++OSD4pMiqOCDCCIi6kxSY42m02lB2W3JLJ+bpkyvWWstjQcCmTP33HPu9/s9Z67LVWH71OxaaGqe3bJ/3uxTs+tHEi7x2D9YUSWW2+3rltfMm1la3VYrqjSMRX7+5dXxmvFcm3JGXjMvZm1b8durnpqCtXmt24rVanrXylw2rlzj3WioapEZWV0tx8zJFt8sLBjdXrMUfgfjyvVsXLly/491VUbnytfDjWoHp4eCUa16uRlS/HL8nMwMBTaaMXWnOGnPmsLIFk8TkOs9a8aHG9WDIEGBw0H3BtbK8d9sJDGSXsvU/Pv1/vU2/ykCeD/s8p6YLGic5GhhJKqGcm3ePnmfWRtQomoSGinfRPbI5qt6yl/I7/AcgfPcvvozJMx2rL0g1sZqd5AcOnh27kVxzuf/NU4Lp4hLT0XsTHf43tt4/UUKMBIBe6ij4Xym0/0Ajmk7hEYcFIhi4so14p17mjF1Lyg5fZ8ZLfTuQMMAHAOrntTecFLjVNAmsZEMp9+3eftJxDM8p7u8zymU9dBDLAdw7sv6GYfRh13eY4Lfsy02P6DVz7aMcaJ0IvpUJD0VtM2wqpBEFJQI2NAx1L7u3OCf7hsoHlqc0Jsx9fd8s+8wiMiUCBN9Ggps1Af22Jx+tGnZTxQA7HBGMLxChQwra9Pd9Y/hn+TATEtCC7E884MqEHLGFjcIVS3S/2639YE9/5Z4JJhirMiqVYiJ0xvJcB6/M5b3xknfR7QhEEn5JugOvc+dh2s6gDaFVg5Isc744iaxWo0+lv34JqkYA3L5PcMGOrLx0GWRPOm1QErE9a+3M0eVW2JAHfr1EgcrKz6EQ3Wy/0tWgjLdG3zBL3MscAehkoAPDyihi3Sn+4l+srZAUcyGz7gXLabVbZ3mnMHMsLqCk2e6wg/RAx2hpyITiBAqSIKQRTfE1L0gIdRfbhSnE82PZN+XDBjRAqcd7AzcBmrRMcyIrvDDkaCnjnUUg25QPu1ZpGP13embadXLZTXPZPrp1mH9dOu7Un9DH8KkAFBBkMIfVRpoQwoGDeig0GkCnu0HgmFDVzg34RCvB5oEx0zF6REul9HreWkkAv+IEZ7yTbDH1MuvnseTZiRanplb3KrTB8cMKoqQBSYGV2/wBaiANLSw3rlmVsac4NtfeiaB/lfrR5LLswHoxcTs2/S+5Ct9P+RCv9ooANFyIniFDp7LIYn4RjT/digq+WhR8b2YZctPGaIS07I4MW0EKK8pmbiQStcvihFinOu17Fsh5NpGAbK/Yla8pvv3l6OsIkYn0K7frYDiXTKcl/0VM74Fzq6ouM14FZtvk6doRY0J+t3Eh5W7hv0Hk1mQnvHVoeMAAAAASUVORK5CYII=";

	private static readonly int[] HudFontSizes = new int[19]
	{
		8, 9, 10, 11, 12, 13, 14, 15, 16, 17,
		18, 19, 20, 22, 24, 26, 28, 30, 32
	};

	private static readonly TimeSpan OverlayPositionSaveDelay = TimeSpan.FromSeconds(1L);

	private static readonly TimeSpan DtrTaskBarCacheDuration = TimeSpan.FromMilliseconds(250L);

	private static readonly TimeSpan InventoryUsageCacheDuration = TimeSpan.FromMilliseconds(500L);

	private static readonly TimeSpan GearsetPopupCacheDuration = TimeSpan.FromMilliseconds(250L);

	private static readonly TimeSpan CoordinatesTextCacheDuration = TimeSpan.FromMilliseconds(200L);

	private static readonly TimeSpan CurrencyCountCacheDuration = TimeSpan.FromMilliseconds(500L);

	private static readonly TimeSpan LimitedTomestoneLookupCacheDuration = TimeSpan.FromSeconds(10L);

	private static readonly TimeSpan GearsetDisplayInfoCacheDuration = TimeSpan.FromMilliseconds(250L);

	private static readonly TimeSpan PluginLookupCacheDuration = TimeSpan.FromSeconds(5L);

	private static readonly TimeSpan PluginIconPathMissRetryDelay = TimeSpan.FromSeconds(10L);

	private static readonly TimeSpan PluginIconTextureRetryDelay = TimeSpan.FromSeconds(2L);

	private static readonly TimeSpan RemotePluginIconRetryDelay = TimeSpan.FromSeconds(30L);

	private static readonly TimeSpan PartyInfoCacheDuration = TimeSpan.FromMilliseconds(150L);

	private static readonly TimeSpan NativePartyAnchorCacheDuration = TimeSpan.FromMilliseconds(100L);

	private static readonly TimeSpan HiddenWindowResolvedDiscoveryInterval = TimeSpan.FromSeconds(2L);

	private static readonly TimeSpan HiddenWindowUnresolvedDiscoveryInterval = TimeSpan.FromMilliseconds(75L);

	private static readonly TimeSpan MissingGameIconRetryDelay = TimeSpan.FromMilliseconds(150L);

	private static readonly Vector4 MitigationColor = new Vector4(0.45f, 0.9f, 1f, 1f);

	private static readonly Vector4 RaidBuffColor = new Vector4(0.86f, 0.62f, 1f, 1f);

	private static readonly Vector4 SelfAppliedBuffColor = new Vector4(0.55f, 1f, 0.55f, 1f);

	private static readonly Vector4 SelfBuffColor = new Vector4(0.65f, 0.9f, 1f, 1f);

	private static readonly Vector4 SelfDebuffColor = new Vector4(1f, 0.75f, 0.35f, 1f);

	private static readonly Vector4 OtherDebuffColor = new Vector4(1f, 0.45f, 0.45f, 1f);

	private static readonly Vector4 HeaderColor = new Vector4(1f, 1f, 1f, 1f);

	private static readonly Vector4 PersonalSkillColor = new Vector4(0.95f, 0.86f, 0.45f, 1f);

	private readonly Configuration config;

	private readonly CombatStateTracker combatState;

	private readonly IDataManager dataManager;

	private readonly ITextureProvider textureProvider;

	private readonly IGameGui gameGui;

	private readonly IAddonEventManager addonEventManager;

	private readonly ICommandManager commandManager;

	private readonly IGameConfig gameConfig;

	private readonly IGameInventory gameInventory;

	private readonly IClientState clientState;

	private readonly IObjectTable objectTable;

	private readonly IDtrBar dtrBar;

	private readonly IDalamudPluginInterface pluginInterface;

	private readonly System.Action saveConfig;

	private readonly string pluginIconCacheDirectory;

	private readonly Dictionary<int, IFontHandle> hudFonts = new Dictionary<int, IFontHandle>();

	private readonly Dictionary<int, IFontHandle> iconFonts = new Dictionary<int, IFontHandle>();

	private readonly HttpClient httpClient = new HttpClient
	{
		Timeout = TimeSpan.FromSeconds(8L)
	};

	private readonly Dictionary<string, PluginIconPathCacheEntry> pluginIconPathCache = new Dictionary<string, PluginIconPathCacheEntry>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, ISharedImmediateTexture> pluginIconTextureCache = new Dictionary<string, ISharedImmediateTexture>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, DateTime> pluginIconTextureRetryAt = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, string> remotePluginIconCachePathCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, DateTime> missingCachedRemotePluginIconRetryAt = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, DateTime> remotePluginIconRetryAt = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<uint, IDalamudTextureWrap?> frameGameIconWrapCache = new Dictionary<uint, IDalamudTextureWrap>();

	private readonly Dictionary<GameIconCacheKey, ISharedImmediateTexture> gameIconTextureCache = new Dictionary<GameIconCacheKey, ISharedImmediateTexture>();

	private readonly Dictionary<uint, DateTime> missingGameIconRetryAt = new Dictionary<uint, DateTime>();

	private readonly Dictionary<uint, GearsetJobMetadata> gearsetJobMetadataCache = new Dictionary<uint, GearsetJobMetadata>();

	private GearsetTaskBarCache? gearsetTaskBarCache;

	private GearsetPopupCache? gearsetPopupCache;

	private readonly Dictionary<int, string> cooldownIconTimeTextCache = new Dictionary<int, string>();

	private readonly Dictionary<int, string> targetStatusTimeTextCache = new Dictionary<int, string>();

	private readonly Dictionary<int, Vector2> lastSavedAuxiliaryBarPositions = new Dictionary<int, Vector2>();

	private readonly Dictionary<int, DateTime> auxiliaryBarPositionSaveDueAt = new Dictionary<int, DateTime>();

	private readonly Dictionary<string, IExposedPlugin> installedPluginLookupCache = new Dictionary<string, IExposedPlugin>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<IReadOnlyList<CooldownEntry>, IReadOnlyList<CooldownEntry>> nativeMitigationCooldownCache = new Dictionary<IReadOnlyList<CooldownEntry>, IReadOnlyList<CooldownEntry>>();

	private readonly HashSet<string> hiddenImGuiWindowKeyCache = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, List<CachedHiddenImGuiWindow>> cachedHiddenImGuiWindows = new Dictionary<string, List<CachedHiddenImGuiWindow>>(StringComparer.OrdinalIgnoreCase);

	private readonly ConcurrentDictionary<string, IDalamudTextureWrap?> pluginRemoteIconCache = new ConcurrentDictionary<string, IDalamudTextureWrap>(StringComparer.OrdinalIgnoreCase);

	private readonly ConcurrentDictionary<string, Task<IDalamudTextureWrap?>> pluginRemoteIconTasks = new ConcurrentDictionary<string, Task<IDalamudTextureWrap>>(StringComparer.OrdinalIgnoreCase);

	private readonly List<TaskBarMainMenuCategory> mainMenuCategories = new List<TaskBarMainMenuCategory>();

	private IDalamudTextureWrap? embeddedDalamudIconTexture;

	private Task<IDalamudTextureWrap?>? embeddedDalamudIconTextureTask;

	private string pluginListFilter = string.Empty;

	private string taskBarFpsText = "FPS 000";

	private List<TaskBarItem> cachedDtrTaskBarItems = new List<TaskBarItem>();

	private List<DtrTaskBarSnapshot> cachedDtrTaskBarSnapshots = new List<DtrTaskBarSnapshot>();

	private readonly List<DtrTaskBarSnapshot> dtrTaskBarSnapshotBuffer = new List<DtrTaskBarSnapshot>();

	private (int Used, int Total) cachedInventoryUsage;

	private (int Used, int Total) cachedSaddlebagUsage;

	private PartyInfoRenderSnapshot? cachedPartyInfoSnapshot;

	private NativePartyAnchorSnapshot? cachedNativePartyAnchorSnapshot;

	private IReadOnlyList<StatusEntry>? cachedSelfStatusSectionSource;

	private int cachedSelfStatusSectionToggles = -1;

	private IReadOnlyList<StatusIconSection>? cachedSelfStatusSections;

	private IReadOnlyList<StatusEntry>? cachedTargetStatusSplitSource;

	private IReadOnlyList<StatusEntry>? cachedTargetOrderedStatuses;

	private int hiddenImGuiWindowKeyCacheSourceCount = -1;

	private ulong hiddenImGuiWindowKeyCacheFingerprint;

	private nint hiddenImGuiContextPtr;

	private int hiddenImGuiWindowCount = -1;

	private int selectedMainMenuCategoryIndex;

	private bool mainMenuLoaded;

	private uint? saddlebagMainCommandId;

	private uint? mapMainCommandId;

	private TaskBarPopupAnchor mainMenuPopupAnchor;

	private TaskBarPopupAnchor volumePopupAnchor;

	private TaskBarPopupAnchor pluginPopupAnchor;

	private TaskBarPopupAnchor quickMenuPopupAnchor;

	private TaskBarPopupAnchor simplePopupAnchor;

	private string activeQuickMenuComponentId = string.Empty;

	private int pendingPluginListPopupOpenFrames;

	private Vector2 lastSavedStatusOverlayPosition;

	private Vector2 lastSavedSelfCooldownBarPosition;

	private Vector2 lastSavedTargetInfoPosition;

	private Vector2 lastSavedTargetInfoCastBarPosition;

	private Vector2 lastSavedTargetInfoStatusBarPosition;

	private DateTime? statusOverlayPositionSaveDueAt;

	private DateTime? selfCooldownBarPositionSaveDueAt;

	private DateTime? targetInfoPositionSaveDueAt;

	private DateTime? targetInfoCastBarPositionSaveDueAt;

	private DateTime? targetInfoStatusBarPositionSaveDueAt;

	private bool nativeCursorForced;

	private bool nativeCursorRequestedThisFrame;

	private bool pluginListPopupDrawnThisFrame;

	private DateTime nextTaskBarFpsUpdateAt;

	private DateTime nextDtrTaskBarRefreshAt;

	private DateTime nextInventoryUsageRefreshAt;

	private DateTime nextPluginLookupRefreshAt;

	private DateTime nextPartyInfoRefreshAt;

	private DateTime nextNativePartyAnchorRefreshAt;

	private DateTime nextHiddenWindowDiscoveryAt;

	private uint cachedTerritoryNameId = uint.MaxValue;

	private string cachedTerritoryName = string.Empty;

	private string? cachedCoordinatesText;

	private DateTime nextCoordinatesTextRefreshAt;

	private long cachedCurrencyCount = -1L;

	private uint cachedCurrencyCountItemId;

	private DateTime nextCurrencyCountRefreshAt;

	private uint cachedLimitedTomestoneItemId;

	private DateTime nextLimitedTomestoneLookupAt;

	private IReadOnlyList<CurrencyDisplayInfo>? cachedCurrencyDisplayOptions;

	private string cachedCurrencyDisplayOptionsCustomIds = string.Empty;

	private DateTime nextCurrencyDisplayOptionsRefreshAt;

	private GearsetDisplayInfo? cachedGearsetDisplayInfo;

	private DateTime nextGearsetDisplayInfoRefreshAt;

	private int overlayStartupWarmupFrames;

	private static readonly string[] GearsetPopupGroupOrder = new string[8] { "防护职业", "治疗职业", "近战职业", "远程物理职业", "远程魔法职业", "生产职业", "采集职业", "其他" };

	private static readonly string[][] GearsetPopupFixedColumns = new string[3][]
	{
		new string[2] { "防护职业", "治疗职业" },
		new string[3] { "近战职业", "远程物理职业", "远程魔法职业" },
		new string[2] { "采集职业", "生产职业" }
	};

	private SelfCooldownLayoutCacheKey? cachedSelfCooldownLayoutKey;

	private SelfCooldownLayoutCache? cachedSelfCooldownLayout;

	private static readonly CurrencyDisplayInfo[] CurrencyDisplayOptions = new CurrencyDisplayInfo[18]
	{
		new CurrencyDisplayInfo(1u, "金币", 0u),
		new CurrencyDisplayInfo(29u, "金碟币", 0u),
		new CurrencyDisplayInfo(21072u, "探险币", 0u),
		new CurrencyDisplayInfo(28u, "亚拉戈诗学神典石", 0u),
		new CurrencyDisplayInfo(25u, "狼印章", 0u),
		new CurrencyDisplayInfo(26807u, "双色宝石", 0u),
		new CurrencyDisplayInfo(27u, "狩猎徽章", 0u),
		new CurrencyDisplayInfo(10307u, "百战徽章", 0u),
		new CurrencyDisplayInfo(20u, "黑涡团筹备", 0u),
		new CurrencyDisplayInfo(21u, "双蛇党筹备", 0u),
		new CurrencyDisplayInfo(22u, "恒辉队筹备", 0u),
		new CurrencyDisplayInfo(26533u, "坚果", 0u),
		new CurrencyDisplayInfo(36656u, "战利品水晶", 0u),
		new CurrencyDisplayInfo(33913u, "制作紫票", 0u),
		new CurrencyDisplayInfo(33914u, "采集紫票", 0u),
		new CurrencyDisplayInfo(41784u, "制作橙票", 0u),
		new CurrencyDisplayInfo(41785u, "采集橙票", 0u),
		new CurrencyDisplayInfo(28063u, "天穹街票", 0u)
	};

	private const uint WorldMarkerFlagFallbackIcon = 60561u;

	private static readonly uint[] WorldMarkerWaymarkIcons = new uint[8] { 61341u, 61342u, 61343u, 61347u, 61344u, 61345u, 61346u, 61348u };

	private DateTime nextWorldMarkerRefreshAt;

	private uint cachedWorldMarkerTerritory;

	private uint cachedWorldMarkerMap;

	private List<WorldMarkerRenderInfo> cachedWorldMarkers = new List<WorldMarkerRenderInfo>();

	private void DrawAuxiliaryBars(ImGuiWindowFlags flags)
	{
		if (config.AuxiliaryBars.Count == 0)
		{
			return;
		}
		ImGuiViewportPtr mainViewport = ImGui.GetMainViewport();
		float num = 0f;
		float num2 = 0f;
		for (int i = 0; i < config.AuxiliaryBars.Count; i++)
		{
			AuxiliaryBarDefinition auxiliaryBarDefinition = config.AuxiliaryBars[i];
			if (!auxiliaryBarDefinition.Enabled)
			{
				continue;
			}
			float num3 = Math.Clamp(auxiliaryBarDefinition.Scale, 0.6f, 2f);
			float opacity = Math.Clamp(auxiliaryBarDefinition.Opacity, 0.15f, 1f);
			bool stretchToEdges = auxiliaryBarDefinition.StretchToEdges;
			bool flag = IsAuxiliaryBarHorizontal(auxiliaryBarDefinition);
			List<TaskBarItem> list = (stretchToEdges ? BuildAuxiliaryBarItemsForOrder(auxiliaryBarDefinition.SectionStartComponentOrder) : new List<TaskBarItem>());
			List<TaskBarItem> list2 = (stretchToEdges ? BuildAuxiliaryBarItemsForOrder(auxiliaryBarDefinition.SectionCenterComponentOrder) : new List<TaskBarItem>());
			List<TaskBarItem> list3 = (stretchToEdges ? BuildAuxiliaryBarItemsForOrder(auxiliaryBarDefinition.SectionEndComponentOrder) : new List<TaskBarItem>());
			List<TaskBarItem> list4 = (stretchToEdges ? new List<TaskBarItem>() : BuildAuxiliaryBarItems(auxiliaryBarDefinition));
			int num4 = (stretchToEdges ? (list.Count + list2.Count + list3.Count) : list4.Count);
			if (num4 == 0)
			{
				continue;
			}
			float num5 = 20f * num3;
			if (!IsHudFontReady(num5) || !AreAuxiliaryItemFontsReady(stretchToEdges, list, list2, list3, list4, num5))
			{
				continue;
			}
			using (PushTaskBarFont(num3))
			{
				float num6 = MathF.Round(Math.Max(44f * num3, ImGui.GetTextLineHeight() + 18f * num3));
				float num7 = MathF.Round(4f * num3);
				Vector2 padding = new Vector2(7f * num3, 3f * num3);
				float num8 = MathF.Round(18f * num3);
				float x = MathF.Round(50f * num3);
				float num9 = (flag ? MathF.Round(auxiliaryBarDefinition.StretchToEdges ? mainViewport.Size.X : CalculateAuxiliaryHorizontalContentWidth(stretchToEdges, list, list2, list3, list4, padding, num8, num3)) : MathF.Max(x, MathF.Round(CalculateAuxiliaryVerticalContentWidth(stretchToEdges, list, list2, list3, list4, num3) + padding.X * 2f)));
				float num10 = (flag ? MathF.Max(x, MathF.Round(num6 + padding.Y * 2f)) : ((float)num4 * num6 + (float)Math.Max(0, num4 - 1) * num7 + padding.Y * 2f));
				float num11 = MathF.Round(flag ? num10 : (auxiliaryBarDefinition.StretchToEdges ? mainViewport.Size.Y : num10));
				bool flag2 = auxiliaryBarDefinition.PositionMode == 2;
				Vector2 displaySize = ImGui.GetIO().DisplaySize;
				Vector2 zero = Vector2.Zero;
				Vector2 vector = displaySize;
				float x2 = (((flag2 & flag) && auxiliaryBarDefinition.StretchToEdges) ? mainViewport.Pos.X : (flag2 ? auxiliaryBarDefinition.CustomPosition.X : ((auxiliaryBarDefinition.PositionMode == 1) ? (vector.X - num9 - num2) : (zero.X + num))));
				float y = ((flag2 && !flag && auxiliaryBarDefinition.StretchToEdges) ? mainViewport.Pos.Y : (flag2 ? auxiliaryBarDefinition.CustomPosition.Y : (auxiliaryBarDefinition.StretchToEdges ? zero.Y : (zero.Y + Math.Max(0f, (vector.Y - num11) * 0.5f) + GetAuxiliaryBarVerticalOffsetPixels(auxiliaryBarDefinition, vector.Y - zero.Y, num11)))));
				if (!flag2)
				{
					if (auxiliaryBarDefinition.PositionMode == 1)
					{
						num2 += num9 + 4f;
					}
					else
					{
						num += num9 + 4f;
					}
				}
				if (stretchToEdges)
				{
					DrawAuxiliaryBarSectionWindow(i, auxiliaryBarDefinition, list, list2, list3, flag, new Vector2(x2, y), new Vector2(num9, num11), padding, num6, num7, num8, opacity, num3, flags, flag2);
				}
				else if (flag)
				{
					DrawAuxiliaryBarSectionWindow(i, auxiliaryBarDefinition, list4, Array.Empty<TaskBarItem>(), Array.Empty<TaskBarItem>(), horizontal: true, new Vector2(x2, y), new Vector2(num9, num11), padding, num6, num7, num8, opacity, num3, flags, flag2);
				}
				else
				{
					DrawAuxiliaryBarWindow(i, auxiliaryBarDefinition, list4, new Vector2(x2, y), new Vector2(num9, num11), padding, num6, num7, opacity, num3, flags, flag2);
				}
			}
		}
	}

	private static bool IsAuxiliaryBarHorizontal(AuxiliaryBarDefinition bar)
	{
		if (bar.PositionMode == 2)
		{
			return bar.LayoutDirection == 1;
		}
		return false;
	}

	private static float GetAuxiliaryBarVerticalOffsetPixels(AuxiliaryBarDefinition bar, float availableHeight, float height)
	{
		float num = Math.Max(0f, availableHeight - height);
		return (Math.Clamp(bar.VerticalOffset, 0f, 1f) - 0.5f) * num;
	}

	private static ImDrawFlags GetAuxiliaryPanelRoundingFlags(AuxiliaryBarDefinition bar, bool customPosition, bool horizontal)
	{
		if (customPosition | horizontal)
		{
			return ImDrawFlags.None;
		}
		if (bar.PositionMode != 1)
		{
			return ImDrawFlags.RoundCornersRight;
		}
		return ImDrawFlags.RoundCornersLeft;
	}

	private bool AreAuxiliaryItemFontsReady(bool stretchSections, IReadOnlyList<TaskBarItem> startItems, IReadOnlyList<TaskBarItem> centerItems, IReadOnlyList<TaskBarItem> endItems, IReadOnlyList<TaskBarItem> items, float taskBarFontSize)
	{
		if (!stretchSections)
		{
			return AreTaskBarItemFontsReady(items, taskBarFontSize);
		}
		if (AreTaskBarItemFontsReady(startItems, taskBarFontSize) && AreTaskBarItemFontsReady(centerItems, taskBarFontSize))
		{
			return AreTaskBarItemFontsReady(endItems, taskBarFontSize);
		}
		return false;
	}

	private bool AreTaskBarItemFontsReady(IReadOnlyList<TaskBarItem> items, float taskBarFontSize)
	{
		foreach (TaskBarItem item in items)
		{
			if (!item.IsIcon && Math.Abs(item.TextScale - 1f) > 0.001f && !IsHudFontReady(taskBarFontSize * item.TextScale))
			{
				return false;
			}
			if (item.IsIcon && item.DrawIcon == TaskBarDrawIcon.None && !IsIconFontReady(taskBarFontSize))
			{
				return false;
			}
		}
		return true;
	}

	private float CalculateAuxiliaryHorizontalContentWidth(bool stretchSections, IReadOnlyList<TaskBarItem> startItems, IReadOnlyList<TaskBarItem> centerItems, IReadOnlyList<TaskBarItem> endItems, IReadOnlyList<TaskBarItem> items, Vector2 padding, float spacing, float scale)
	{
		if (!stretchSections)
		{
			return CalculateAuxiliaryHorizontalSectionWidth(items, padding, spacing, scale);
		}
		return Math.Max(CalculateAuxiliaryHorizontalSectionWidth(startItems, padding, spacing, scale), Math.Max(CalculateAuxiliaryHorizontalSectionWidth(centerItems, padding, spacing, scale), CalculateAuxiliaryHorizontalSectionWidth(endItems, padding, spacing, scale)));
	}

	private float CalculateAuxiliaryVerticalContentWidth(bool stretchSections, IReadOnlyList<TaskBarItem> startItems, IReadOnlyList<TaskBarItem> centerItems, IReadOnlyList<TaskBarItem> endItems, IReadOnlyList<TaskBarItem> items, float scale)
	{
		if (!stretchSections)
		{
			return CalculateAuxiliaryVerticalContentWidth(items, scale);
		}
		return Math.Max(CalculateAuxiliaryVerticalContentWidth(startItems, scale), Math.Max(CalculateAuxiliaryVerticalContentWidth(centerItems, scale), CalculateAuxiliaryVerticalContentWidth(endItems, scale)));
	}

	private float CalculateAuxiliaryVerticalContentWidth(IReadOnlyList<TaskBarItem> items, float scale)
	{
		float num = 0f;
		foreach (TaskBarItem item in items)
		{
			num = Math.Max(num, CalcTaskBarItemLayoutSize(item, scale).X);
		}
		return num;
	}

	private float CalculateAuxiliaryHorizontalSectionWidth(IReadOnlyList<TaskBarItem> items, Vector2 padding, float spacing, float scale)
	{
		if (items.Count == 0)
		{
			return 0f;
		}
		float num = 0f;
		foreach (TaskBarItem item in items)
		{
			num += CalcTaskBarItemLayoutSize(item, scale).X;
		}
		num += spacing * (float)Math.Max(0, items.Count - 1);
		return num + padding.X * 2f;
	}

	private List<TaskBarItem> BuildAuxiliaryBarItems(AuxiliaryBarDefinition bar)
	{
		return BuildAuxiliaryBarItemsForOrder(bar.ComponentOrder);
	}

	private List<TaskBarItem> BuildAuxiliaryBarItemsForOrder(IEnumerable<string> componentOrder)
	{
		List<TaskBarItem> list = new List<TaskBarItem>();
		IReadOnlyList<TaskBarItem> readOnlyList = null;
		foreach (string item4 in componentOrder)
		{
			switch (item4)
			{
			case "time":
			{
				string taskBarTimeText = GetTaskBarTimeText();
				if (!string.IsNullOrWhiteSpace(taskBarTimeText))
				{
					list.Add(new TaskBarItem(taskBarTimeText, taskBarTimeText, null, null, "", AdjustVolumeOnWheel: false, IsIcon: false, GetTaskBarTimeMeasureText(), IsDalamudIcon: false, 0u, GetTaskBarTimeTextScale()));
				}
				continue;
			}
			case "fps":
				UpdateTaskBarFpsText();
				list.Add(new TaskBarItem(taskBarFpsText, taskBarFpsText, null, null, "", AdjustVolumeOnWheel: false, IsIcon: false, "FPS 000"));
				continue;
			case "volume":
				list.Add(new TaskBarItem(string.Empty, GetVolumeTaskBarTooltip(), delegate
				{
				}, HandleVolumeTaskBarClick, "AllHud 音量控制", AdjustVolumeOnWheel: true, IsIcon: false, "", IsDalamudIcon: false, 0u, 1f, "", null, TaskBarDrawIcon.Volume));
				continue;
			case "main_menu":
				list.Add(new TaskBarItem(string.Empty, string.Empty, delegate
				{
				}, null, "AllHud 主菜单", AdjustVolumeOnWheel: false, IsIcon: false, "", IsDalamudIcon: false, 0u, 1f, "", null, TaskBarDrawIcon.MainMenu));
				continue;
			case "plugin_list":
				list.Add(new TaskBarItem(string.Empty, "左键查看已添加插件，右键打开 Dalamud 插件管理器", delegate
				{
				}, delegate
				{
					commandManager.ProcessCommand("/xlplugins");
				}, "AllHud 插件列表", AdjustVolumeOnWheel: false, IsIcon: false, "", IsDalamudIcon: false, 0u, 1f, "", null, TaskBarDrawIcon.PluginList));
				continue;
			}
			if (Configuration.IsPluginShortcutComponentId(item4))
			{
				if (TryCreatePluginShortcutTaskBarItem(item4, out var item))
				{
					list.Add(item);
				}
				continue;
			}
			string componentId = item4;
			if (Configuration.IsCustomShortcutComponentId(componentId))
			{
				if (TryCreateCustomShortcutTaskBarItem(componentId, out var item2))
				{
					list.Add(item2);
				}
				continue;
			}
			string componentId2 = item4;
			TaskBarItem item3;
			if (!Configuration.IsQuickMenuComponentId(componentId2))
			{
				switch (item4)
				{
				case "server_info":
					if (readOnlyList == null)
					{
						readOnlyList = BuildDtrTaskBarItems();
					}
					list.AddRange(readOnlyList);
					break;
				case "inventory":
					list.Add(CreateInventoryTaskBarItem(saddlebag: false));
					break;
				case "saddlebag":
					list.Add(CreateInventoryTaskBarItem(saddlebag: true));
					break;
				case "teleport":
					list.Add(CreateTeleportTaskBarItem());
					break;
				case "coordinates":
					list.Add(CreateCoordinatesTaskBarItem());
					break;
				case "gearset_switcher":
					list.Add(CreateGearsetSwitcherTaskBarItem());
					break;
				case "currency":
					list.Add(CreateCurrencyTaskBarItem());
					break;
				case "walking_indicator":
					if (config.TaskBarShowWalkingIndicator && (!config.TaskBarWalkingOnlyWhenWalking || IsPlayerWalking()))
					{
						bool flag = IsPlayerWalking();
						list.Add(new TaskBarItem(string.Empty, flag ? "步行中（点击切换跑步）" : "跑步中（点击切换步行）", delegate
						{
							TogglePlayerWalking();
						}, null, "", AdjustVolumeOnWheel: false, IsIcon: false, "", IsDalamudIcon: false, 0u, 1f, "", null, TaskBarDrawIcon.Walking));
					}
					break;
				}
			}
			else if (TryCreateQuickMenuTaskBarItem(componentId2, out item3))
			{
				list.Add(item3);
			}
		}
		return list;
	}

	private void DrawAuxiliaryBarWindow(int index, AuxiliaryBarDefinition bar, IReadOnlyList<TaskBarItem> items, Vector2 pos, Vector2 size, Vector2 padding, float itemHeight, float itemSpacing, float opacity, float scale, ImGuiWindowFlags flags, bool customPosition)
	{
		ImGui.SetNextWindowPos(SnapToPixel(pos), (!customPosition) ? ImGuiCond.Always : ImGuiCond.Once);
		ImGui.SetNextWindowSize(SnapToPixel(size), ImGuiCond.Always);
		ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
		ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
		flags |= ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground;
		if (!customPosition)
		{
			flags |= ImGuiWindowFlags.NoMove;
		}
		ImU8String name = new ImU8String(11, 1);
		name.AppendLiteral("AllHud 辅助栏 ");
		name.AppendFormatted(index);
		if (!ImGui.Begin(name, flags))
		{
			ImGui.End();
			ImGui.PopStyleVar(2);
			return;
		}
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		Vector2 windowPos = ImGui.GetWindowPos();
		if (customPosition)
		{
			TrackAuxiliaryBarPosition(index, bar, windowPos);
		}
		Vector2 vector = windowPos + size;
		float rounding = (ThemeDrawing.IsLiquidGlass(config.ThemeMode) ? ThemeDrawing.GetLiquidGlassRounding(config, scale) : (6f * scale));
		ImDrawFlags auxiliaryPanelRoundingFlags = GetAuxiliaryPanelRoundingFlags(bar, customPosition, horizontal: false);
		ThemePalette effectiveTheme = GetEffectiveTheme();
		if (ThemeDrawing.IsLiquidGlass(config.ThemeMode))
		{
			ThemeDrawing.PrependLiquidGlassBlur(windowDrawList, windowPos, vector, rounding, config, opacity);
		}
		windowDrawList.AddRectFilled(windowPos + new Vector2(0f, 2f * scale), vector + new Vector2(0f, 2f * scale), ImGui.GetColorU32(WithOpacity(effectiveTheme.TextShadow, opacity * 0.24f)), rounding, auxiliaryPanelRoundingFlags);
		windowDrawList.AddRectFilled(windowPos, vector, ImGui.GetColorU32(WithOpacity(effectiveTheme.TaskBarBackground, opacity)), rounding, auxiliaryPanelRoundingFlags);
		windowDrawList.AddRectFilledMultiColor(windowPos + new Vector2(1f * scale, 1f * scale), vector - new Vector2(1f * scale, Math.Max(1f, size.Y * 0.46f)), ImGui.GetColorU32(WithOpacity(effectiveTheme.TaskBarGradientStart, opacity)), ImGui.GetColorU32(WithOpacity(effectiveTheme.TaskBarGradientEnd, opacity)), ImGui.GetColorU32(WithOpacity(effectiveTheme.TaskBarGradientEnd, opacity * 0.12f)), ImGui.GetColorU32(WithOpacity(effectiveTheme.TaskBarGradientStart, opacity * 0.16f)));
		windowDrawList.AddRect(windowPos, vector, ImGui.GetColorU32(WithOpacity(effectiveTheme.Border, opacity)), rounding, auxiliaryPanelRoundingFlags, 1f * scale);
		if (ThemeDrawing.IsLiquidGlass(config.ThemeMode))
		{
			ThemeDrawing.DrawLiquidGlassPanel(windowDrawList, windowPos, vector, rounding, config, opacity * 0.78f, drawShadow: false);
		}
		Vector2 vector2 = windowPos + padding;
		for (int i = 0; i < items.Count; i++)
		{
			TaskBarItem item = items[i];
			Vector2 vector3 = CalcTaskBarItemLayoutSize(item, scale);
			Vector2 vector4 = CalcTaskBarItemDrawTextSize(item, scale);
			Vector2 vector5 = SnapToPixel(new Vector2(vector2.X, vector2.Y));
			Vector2 vector6 = SnapToPixel(new Vector2(Math.Max(vector3.X, size.X - padding.X * 2f), itemHeight));
			bool flag = item.DrawIcon != TaskBarDrawIcon.None || item.IsDalamudIcon || !string.IsNullOrWhiteSpace(item.PluginShortcutInternalName) || (item.GameIconId != 0 && string.IsNullOrWhiteSpace(item.Text));
			float num = (flag ? Math.Min(vector6.X, itemHeight - 8f * scale) : (vector6.X + 8f * scale));
			Vector2 vector7 = SnapToPixel(new Vector2(vector5.X + Math.Max(0f, (vector6.X - num) * 0.5f), vector5.Y + 4f * scale));
			Vector2 vector8 = SnapToPixel(new Vector2(num, itemHeight - 8f * scale));
			ImGui.SetCursorScreenPos(vector7);
			ImU8String strId = new ImU8String(12, 2);
			strId.AppendLiteral("##aux_item_");
			strId.AppendFormatted(index);
			strId.AppendLiteral("_");
			strId.AppendFormatted(i);
			ImGui.InvisibleButton(strId, vector8);
			bool flag2 = ImGui.IsItemHovered();
			bool active = ImGui.IsItemActive();
			DrawTaskBarItemCard(windowDrawList, vector7, vector8, i, flag2, active, item.OnClick != null, flag, opacity, scale);
			if (flag2 && item.OnClick != null && !string.IsNullOrWhiteSpace(item.Tooltip))
			{
				DrawTaskBarTooltip(item.Tooltip);
			}
			if (item.OnClick != null && ImGui.IsItemClicked(ImGuiMouseButton.Left))
			{
				item.OnClick(CreateDtrInteractionEvent(MouseClickType.Left));
				OpenTaskBarItemPopup(item);
			}
			if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
			{
				(item.OnRightClick ?? item.OnClick)?.Invoke(CreateDtrInteractionEvent(MouseClickType.Right));
			}
			Vector2 pos2 = SnapToPixel(new Vector2(vector5.X + Math.Max(0f, (vector6.X - vector4.X) * 0.5f), vector5.Y + Math.Max(0f, (itemHeight - vector4.Y) * 0.5f)));
			uint colorU = ImGui.GetColorU32(WithOpacity(item.TextColor ?? effectiveTheme.TaskBarText, opacity));
			if (item.DrawIcon != TaskBarDrawIcon.None)
			{
				DrawTaskBarCustomIcon(windowDrawList, pos2, vector4.X, item.DrawIcon, opacity, scale);
			}
			else if (item.IsDalamudIcon)
			{
				DrawTaskBarDalamudIcon(windowDrawList, pos2, vector4.X, opacity, scale);
			}
			else if (!string.IsNullOrWhiteSpace(item.PluginShortcutInternalName))
			{
				DrawPluginShortcutIcon(item, pos2, vector4.X, scale);
			}
			else if (item.GameIconId != 0)
			{
				DrawTaskBarGameIconItem(windowDrawList, pos2, item, colorU, opacity, scale);
			}
			else
			{
				using (PushTaskBarItemFont(item, scale))
				{
					DrawTaskBarText(windowDrawList, pos2, colorU, item.Text, opacity, scale);
				}
			}
			vector2.Y += itemHeight + itemSpacing;
		}
		DrawMainMenuPopup(opacity, scale);
		DrawQuickMenuPopup(opacity, scale);
		DrawVolumePopup(opacity, scale);
		DrawPluginListPopup(opacity, scale);
		DrawCoordinatesPopup(opacity, scale);
		DrawGearsetSwitcherPopup(opacity, scale);
		DrawCurrencyPopup(opacity, scale);
		ImGui.End();
		ImGui.PopStyleVar(2);
	}

	private void DrawAuxiliaryBarSectionWindow(int index, AuxiliaryBarDefinition bar, IReadOnlyList<TaskBarItem> startItems, IReadOnlyList<TaskBarItem> centerItems, IReadOnlyList<TaskBarItem> endItems, bool horizontal, Vector2 pos, Vector2 size, Vector2 padding, float itemHeight, float itemSpacing, float rowSpacing, float opacity, float scale, ImGuiWindowFlags flags, bool customPosition)
	{
		ImGui.SetNextWindowPos(SnapToPixel(pos), (!customPosition) ? ImGuiCond.Always : ImGuiCond.Once);
		ImGui.SetNextWindowSize(SnapToPixel(size), ImGuiCond.Always);
		ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
		ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
		flags |= ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground;
		if (!customPosition)
		{
			flags |= ImGuiWindowFlags.NoMove;
		}
		ImU8String name = new ImU8String(11, 1);
		name.AppendLiteral("AllHud 辅助栏 ");
		name.AppendFormatted(index);
		if (!ImGui.Begin(name, flags))
		{
			ImGui.End();
			ImGui.PopStyleVar(2);
			return;
		}
		ImDrawListPtr drawList = ImGui.GetWindowDrawList();
		Vector2 windowMin = ImGui.GetWindowPos();
		if (customPosition)
		{
			TrackAuxiliaryBarPosition(index, bar, windowMin);
		}
		Vector2 vector = windowMin + size;
		float rounding = (ThemeDrawing.IsLiquidGlass(config.ThemeMode) ? ThemeDrawing.GetLiquidGlassRounding(config, scale) : (6f * scale));
		ImDrawFlags auxiliaryPanelRoundingFlags = GetAuxiliaryPanelRoundingFlags(bar, customPosition, horizontal);
		ThemePalette theme = GetEffectiveTheme();
		if (ThemeDrawing.IsLiquidGlass(config.ThemeMode))
		{
			ThemeDrawing.PrependLiquidGlassBlur(drawList, windowMin, vector, rounding, config, opacity);
		}
		drawList.AddRectFilled(windowMin + new Vector2(0f, 2f * scale), vector + new Vector2(0f, 2f * scale), ImGui.GetColorU32(WithOpacity(theme.TextShadow, opacity * 0.24f)), rounding, auxiliaryPanelRoundingFlags);
		drawList.AddRectFilled(windowMin, vector, ImGui.GetColorU32(WithOpacity(theme.TaskBarBackground, opacity)), rounding, auxiliaryPanelRoundingFlags);
		drawList.AddRectFilledMultiColor(windowMin + new Vector2(1f * scale, 1f * scale), vector - new Vector2(1f * scale, Math.Max(1f, size.Y * 0.46f)), ImGui.GetColorU32(WithOpacity(theme.TaskBarGradientStart, opacity)), ImGui.GetColorU32(WithOpacity(theme.TaskBarGradientEnd, opacity)), ImGui.GetColorU32(WithOpacity(theme.TaskBarGradientEnd, opacity * 0.12f)), ImGui.GetColorU32(WithOpacity(theme.TaskBarGradientStart, opacity * 0.16f)));
		drawList.AddRect(windowMin, vector, ImGui.GetColorU32(WithOpacity(theme.Border, opacity)), rounding, auxiliaryPanelRoundingFlags, 1f * scale);
		if (ThemeDrawing.IsLiquidGlass(config.ThemeMode))
		{
			ThemeDrawing.DrawLiquidGlassPanel(drawList, windowMin, vector, rounding, config, opacity * 0.78f, drawShadow: false);
		}
		if (horizontal)
		{
			HorizontalGroupWidth(startItems);
			float num = HorizontalGroupWidth(centerItems);
			float num2 = HorizontalGroupWidth(endItems);
			DrawHorizontalGroup(startItems, windowMin.X + padding.X, "start");
			DrawHorizontalGroup(centerItems, windowMin.X + Math.Max(padding.X, (size.X - num) * 0.5f), "center");
			DrawHorizontalGroup(endItems, windowMin.X + Math.Max(padding.X, size.X - padding.X - num2), "end");
		}
		else
		{
			VerticalGroupHeight(startItems);
			float num3 = VerticalGroupHeight(centerItems);
			float num4 = VerticalGroupHeight(endItems);
			DrawVerticalGroup(startItems, windowMin.Y + padding.Y, "start");
			DrawVerticalGroup(centerItems, windowMin.Y + Math.Max(padding.Y, (size.Y - num3) * 0.5f), "center");
			DrawVerticalGroup(endItems, windowMin.Y + Math.Max(padding.Y, size.Y - padding.Y - num4), "end");
		}
		DrawMainMenuPopup(opacity, scale);
		DrawQuickMenuPopup(opacity, scale);
		DrawVolumePopup(opacity, scale);
		DrawPluginListPopup(opacity, scale);
		DrawCoordinatesPopup(opacity, scale);
		DrawGearsetSwitcherPopup(opacity, scale);
		DrawCurrencyPopup(opacity, scale);
		ImGui.End();
		ImGui.PopStyleVar(2);
		void DrawHorizontalGroup(IReadOnlyList<TaskBarItem> group, float x, string section)
		{
			Vector2 slotMin = SnapToPixel(new Vector2(x, windowMin.Y + padding.Y));
			for (int i = 0; i < group.Count; i++)
			{
				TaskBarItem item = group[i];
				float x2 = CalcTaskBarItemLayoutSize(item, scale).X;
				DrawItem(item, slotMin, new Vector2(x2, itemHeight), $"##aux_section_item_{index}_{section}_{i}", i);
				slotMin.X += x2 + rowSpacing;
			}
		}
		void DrawItem(TaskBarItem item, Vector2 slotMin, Vector2 slotSize, string id, int visualIndex)
		{
			CalcTaskBarItemLayoutSize(item, scale);
			Vector2 vector2 = CalcTaskBarItemDrawTextSize(item, scale);
			bool flag = item.DrawIcon != TaskBarDrawIcon.None || item.IsDalamudIcon || !string.IsNullOrWhiteSpace(item.PluginShortcutInternalName) || (item.GameIconId != 0 && string.IsNullOrWhiteSpace(item.Text));
			float num5 = (flag ? Math.Min(slotSize.X, itemHeight - 8f * scale) : (slotSize.X + 8f * scale));
			Vector2 vector3 = SnapToPixel(new Vector2(slotMin.X + Math.Max(0f, (slotSize.X - num5) * 0.5f), slotMin.Y + 4f * scale));
			Vector2 vector4 = SnapToPixel(new Vector2(num5, itemHeight - 8f * scale));
			ImGui.SetCursorScreenPos(vector3);
			ImGui.InvisibleButton(id, vector4);
			bool flag2 = ImGui.IsItemHovered();
			bool active = ImGui.IsItemActive();
			DrawTaskBarItemCard(drawList, vector3, vector4, visualIndex, flag2, active, item.OnClick != null, flag, opacity, scale);
			if (flag2 && item.OnClick != null && !string.IsNullOrWhiteSpace(item.Tooltip))
			{
				DrawTaskBarTooltip(item.Tooltip);
			}
			if (item.OnClick != null && ImGui.IsItemClicked(ImGuiMouseButton.Left))
			{
				item.OnClick(CreateDtrInteractionEvent(MouseClickType.Left));
				OpenTaskBarItemPopup(item);
			}
			if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
			{
				(item.OnRightClick ?? item.OnClick)?.Invoke(CreateDtrInteractionEvent(MouseClickType.Right));
			}
			Vector2 pos2 = SnapToPixel(new Vector2(slotMin.X + Math.Max(0f, (slotSize.X - vector2.X) * 0.5f), slotMin.Y + Math.Max(0f, (itemHeight - vector2.Y) * 0.5f)));
			uint colorU = ImGui.GetColorU32(WithOpacity(item.TextColor ?? theme.TaskBarText, opacity));
			if (item.DrawIcon != TaskBarDrawIcon.None)
			{
				DrawTaskBarCustomIcon(drawList, pos2, vector2.X, item.DrawIcon, opacity, scale);
			}
			else if (item.IsDalamudIcon)
			{
				DrawTaskBarDalamudIcon(drawList, pos2, vector2.X, opacity, scale);
			}
			else if (!string.IsNullOrWhiteSpace(item.PluginShortcutInternalName))
			{
				DrawPluginShortcutIcon(item, pos2, vector2.X, scale);
			}
			else
			{
				if (item.GameIconId == 0)
				{
					using (PushTaskBarItemFont(item, scale))
					{
						DrawTaskBarText(drawList, pos2, colorU, item.Text, opacity, scale);
						return;
					}
				}
				DrawTaskBarGameIconItem(drawList, pos2, item, colorU, opacity, scale);
			}
		}
		void DrawVerticalGroup(IReadOnlyList<TaskBarItem> group, float y, string section)
		{
			Vector2 slotMin = SnapToPixel(new Vector2(windowMin.X + padding.X, y));
			float x = Math.Max(0f, size.X - padding.X * 2f);
			for (int i = 0; i < group.Count; i++)
			{
				DrawItem(group[i], slotMin, new Vector2(x, itemHeight), $"##aux_section_item_{index}_{section}_{i}", i);
				slotMin.Y += itemHeight + itemSpacing;
			}
		}
		float HorizontalGroupWidth(IReadOnlyList<TaskBarItem> group)
		{
			if (group.Count != 0)
			{
				return group.Sum((TaskBarItem item) => CalcTaskBarItemLayoutSize(item, scale).X) + (float)Math.Max(0, group.Count - 1) * rowSpacing;
			}
			return 0f;
		}
		float VerticalGroupHeight(IReadOnlyList<TaskBarItem> group)
		{
			if (group.Count != 0)
			{
				return (float)group.Count * itemHeight + (float)Math.Max(0, group.Count - 1) * itemSpacing;
			}
			return 0f;
		}
	}

	private void TrackAuxiliaryBarPosition(int index, AuxiliaryBarDefinition bar, Vector2 currentPosition)
	{
		if (!lastSavedAuxiliaryBarPositions.ContainsKey(index))
		{
			lastSavedAuxiliaryBarPositions[index] = bar.CustomPosition;
		}
		DateTime utcNow = DateTime.UtcNow;
		if (Vector2.DistanceSquared(currentPosition, bar.CustomPosition) > 1f)
		{
			bar.CustomPosition = currentPosition;
			auxiliaryBarPositionSaveDueAt[index] = utcNow.Add(OverlayPositionSaveDelay);
		}
		if (auxiliaryBarPositionSaveDueAt.TryGetValue(index, out var value) && !(utcNow < value))
		{
			if (Vector2.DistanceSquared(bar.CustomPosition, lastSavedAuxiliaryBarPositions[index]) > 1f)
			{
				saveConfig();
				lastSavedAuxiliaryBarPositions[index] = bar.CustomPosition;
			}
			auxiliaryBarPositionSaveDueAt.Remove(index);
		}
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
			TitleBar = Glass(themePalette.TitleBar),
			TitlePill = Glass(themePalette.TitlePill),
			PopupBg = Glass(themePalette.PopupBg),
			TaskBarBackground = Glass(themePalette.TaskBarBackground),
			TaskBarGradientStart = Glass(themePalette.TaskBarGradientStart),
			TaskBarGradientEnd = Glass(themePalette.TaskBarGradientEnd),
			TaskBarCard = Glass(themePalette.TaskBarCard),
			TaskBarCardAlt = Glass(themePalette.TaskBarCardAlt),
			TaskBarCardHovered = Glass(themePalette.TaskBarCardHovered),
			TaskBarCardActive = Glass(themePalette.TaskBarCardActive)
		};
		Vector4 Glass(Vector4 color)
		{
			return ThemeDrawing.ApplyLiquidGlassOpacity(config, color);
		}
	}

	public OverlayRenderer(Configuration config, CombatStateTracker combatState, IDataManager dataManager, ITextureProvider textureProvider, IGameGui gameGui, IAddonEventManager addonEventManager, ICommandManager commandManager, IGameConfig gameConfig, IGameInventory gameInventory, IClientState clientState, IObjectTable objectTable, IDtrBar dtrBar, IDalamudPluginInterface pluginInterface, System.Action saveConfig)
	{
		this.config = config;
		this.combatState = combatState;
		this.dataManager = dataManager;
		this.textureProvider = textureProvider;
		this.gameGui = gameGui;
		this.addonEventManager = addonEventManager;
		this.commandManager = commandManager;
		this.gameConfig = gameConfig;
		this.gameInventory = gameInventory;
		this.clientState = clientState;
		this.objectTable = objectTable;
		this.dtrBar = dtrBar;
		this.pluginInterface = pluginInterface;
		this.saveConfig = saveConfig;
		pluginIconCacheDirectory = Path.Combine(this.pluginInterface.ConfigDirectory.FullName, "plugin-icon-cache");
		lastSavedStatusOverlayPosition = config.StatusOverlayPosition;
		lastSavedSelfCooldownBarPosition = config.SelfCooldownBarPosition;
		lastSavedTargetInfoPosition = config.CustomTargetInfoPosition;
		lastSavedTargetInfoCastBarPosition = config.CustomTargetInfoCastBarPosition;
		lastSavedTargetInfoStatusBarPosition = config.CustomTargetInfoStatusBarPosition;
		overlayStartupWarmupFrames = 2;
	}

	public void Dispose()
	{
		ResetNativeCursorIfNeeded();
		foreach (IFontHandle value in hudFonts.Values)
		{
			value.Dispose();
		}
		foreach (IFontHandle value2 in iconFonts.Values)
		{
			value2.Dispose();
		}
		foreach (IDalamudTextureWrap value3 in pluginRemoteIconCache.Values)
		{
			value3?.Dispose();
		}
		embeddedDalamudIconTexture?.Dispose();
		httpClient.Dispose();
	}

	public void Draw()
	{
		nativeCursorRequestedThisFrame = false;
		pluginListPopupDrawnThisFrame = false;
		frameGameIconWrapCache.Clear();
		ApplyKnownFloatingShortcutWindowHides();
		ImGuiWindowFlags imGuiWindowFlags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing;
		DrawCustomTargetInfoWindow(imGuiWindowFlags | ImGuiWindowFlags.AlwaysAutoResize);
		if (overlayStartupWarmupFrames <= 0)
		{
			DrawTaskBarWindow(imGuiWindowFlags);
			DrawAuxiliaryBars(imGuiWindowFlags);
		}
		else
		{
			overlayStartupWarmupFrames--;
		}
		DrawSkillsWindow();
		DrawSelfCooldownBarWindow(imGuiWindowFlags | ImGuiWindowFlags.AlwaysAutoResize);
		DrawWorldMarkers();
		if (config.ShowStatusOverlay)
		{
			DrawStatusWindow(imGuiWindowFlags | ImGuiWindowFlags.AlwaysAutoResize);
		}
		if (!nativeCursorRequestedThisFrame)
		{
			ResetNativeCursorIfNeeded();
		}
	}

	private void ApplyKnownFloatingShortcutWindowHides()
	{
		if (config.HiddenImGuiWindowNames.Count == 0)
		{
			ClearHiddenImGuiWindowDiscoveryCache();
		}
		else
		{
			TryApplyKnownFloatingShortcutWindowHides();
		}
	}

	private unsafe void TryApplyKnownFloatingShortcutWindowHides()
	{
		try
		{
			ImGuiContext* currentContext = ImGuiNative.GetCurrentContext();
			if (currentContext == null)
			{
				ClearHiddenImGuiWindowDiscoveryCache();
				return;
			}
			ImGuiContextPtr imGuiContextPtr = new ImGuiContextPtr(currentContext);
			if (imGuiContextPtr.IsNull)
			{
				ClearHiddenImGuiWindowDiscoveryCache();
				return;
			}
			nint num = (nint)currentContext;
			if (hiddenImGuiContextPtr != num)
			{
				ClearHiddenImGuiWindowDiscoveryCache();
				hiddenImGuiContextPtr = num;
				nextHiddenWindowDiscoveryAt = DateTime.MinValue;
			}
			HashSet<string> hashSet = GetHiddenImGuiWindowKeyCache();
			if (hashSet.Count == 0)
			{
				ClearHiddenImGuiWindowDiscoveryCache();
				return;
			}
			ref ImVector<ImGuiWindowPtr> windows = ref imGuiContextPtr.Windows;
			DateTime utcNow = DateTime.UtcNow;
			if (hiddenImGuiWindowCount != windows.Size || utcNow >= nextHiddenWindowDiscoveryAt)
			{
				DiscoverHiddenImGuiWindows(windows, hashSet);
				hiddenImGuiWindowCount = windows.Size;
				nextHiddenWindowDiscoveryAt = utcNow + (HasUnresolvedHiddenImGuiWindows(hashSet) ? HiddenWindowUnresolvedDiscoveryInterval : HiddenWindowResolvedDiscoveryInterval);
			}
			ApplyCachedHiddenImGuiWindows();
		}
		catch
		{
		}
	}

	private void ClearHiddenImGuiWindowDiscoveryCache()
	{
		if (cachedHiddenImGuiWindows.Count != 0 || hiddenImGuiContextPtr != 0 || hiddenImGuiWindowCount != -1 || !(nextHiddenWindowDiscoveryAt == DateTime.MinValue))
		{
			cachedHiddenImGuiWindows.Clear();
			hiddenImGuiContextPtr = 0;
			hiddenImGuiWindowCount = -1;
			nextHiddenWindowDiscoveryAt = DateTime.MinValue;
		}
	}

	private HashSet<string> GetHiddenImGuiWindowKeyCache()
	{
		ulong hiddenImGuiWindowNameFingerprint = GetHiddenImGuiWindowNameFingerprint();
		if (hiddenImGuiWindowKeyCacheSourceCount == config.HiddenImGuiWindowNames.Count && hiddenImGuiWindowKeyCacheFingerprint == hiddenImGuiWindowNameFingerprint)
		{
			return hiddenImGuiWindowKeyCache;
		}
		hiddenImGuiWindowKeyCache.Clear();
		foreach (string hiddenImGuiWindowName in config.HiddenImGuiWindowNames)
		{
			string text = NormalizeImGuiWindowNameKey(hiddenImGuiWindowName);
			if (text.Length > 0)
			{
				hiddenImGuiWindowKeyCache.Add(text);
			}
		}
		hiddenImGuiWindowKeyCacheSourceCount = config.HiddenImGuiWindowNames.Count;
		hiddenImGuiWindowKeyCacheFingerprint = hiddenImGuiWindowNameFingerprint;
		cachedHiddenImGuiWindows.Clear();
		hiddenImGuiWindowCount = -1;
		nextHiddenWindowDiscoveryAt = DateTime.MinValue;
		return hiddenImGuiWindowKeyCache;
	}

	private ulong GetHiddenImGuiWindowNameFingerprint()
	{
		ulong hash = 14695981039346656037uL;
		foreach (string hiddenImGuiWindowName in config.HiddenImGuiWindowNames)
		{
			AppendTrimmedStringHash(hiddenImGuiWindowName, ref hash, 1099511628211uL);
			hash ^= 0x1F;
			hash *= 1099511628211L;
		}
		return hash;
	}

	private static void AppendTrimmedStringHash(string? value, ref ulong hash, ulong prime)
	{
		if (!string.IsNullOrEmpty(value))
		{
			int i = 0;
			int num;
			for (num = value.Length - 1; i <= num && char.IsWhiteSpace(value[i]); i++)
			{
			}
			while (num >= i && char.IsWhiteSpace(value[num]))
			{
				num--;
			}
			for (int j = i; j <= num; j++)
			{
				hash ^= value[j];
				hash *= prime;
			}
		}
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

	private unsafe void DiscoverHiddenImGuiWindows(ImVector<ImGuiWindowPtr> windows, IReadOnlySet<string> keysToHide)
	{
		cachedHiddenImGuiWindows.Clear();
		for (int i = 0; i < windows.Size; i++)
		{
			ImGuiWindowPtr window = windows[i];
			if (window.Handle == null || window.Name == null)
			{
				continue;
			}
			string text = NormalizeImGuiWindowNameKey(Marshal.PtrToStringUTF8((nint)window.Name) ?? string.Empty);
			if (text.Length != 0 && keysToHide.Contains(text))
			{
				if (!cachedHiddenImGuiWindows.TryGetValue(text, out List<CachedHiddenImGuiWindow> value))
				{
					value = new List<CachedHiddenImGuiWindow>();
					cachedHiddenImGuiWindows[text] = value;
				}
				value.Add(new CachedHiddenImGuiWindow(text, window));
			}
		}
	}

	private bool HasUnresolvedHiddenImGuiWindows(IReadOnlySet<string> keysToHide)
	{
		foreach (string item in keysToHide)
		{
			if (!cachedHiddenImGuiWindows.TryGetValue(item, out List<CachedHiddenImGuiWindow> value) || value.Count == 0)
			{
				return true;
			}
		}
		return false;
	}

	private void ApplyCachedHiddenImGuiWindows()
	{
		foreach (List<CachedHiddenImGuiWindow> value in cachedHiddenImGuiWindows.Values)
		{
			foreach (CachedHiddenImGuiWindow item in value)
			{
				ApplyHardHideToImGuiWindow(item.Window);
			}
		}
	}

	private unsafe static void ApplyHardHideToImGuiWindow(ImGuiWindowPtr window)
	{
		if (window.Handle == null)
		{
			return;
		}
		window.Hidden = true;
		window.SkipItems = true;
		window.HiddenFramesCanSkipItems = 2;
		window.HiddenFramesCannotSkipItems = 2;
		window.HiddenFramesForRenderOnly = 2;
		try
		{
			ref ImDrawListPtr drawList = ref window.DrawList;
			drawList.CmdBuffer.Clear();
			drawList.IdxBuffer.Clear();
			drawList.VtxBuffer.Clear();
		}
		catch
		{
		}
	}

	private IDisposable PushTaskBarFont(float scale)
	{
		return PushHudFont(20f * Math.Clamp(scale, 0.6f, 2f));
	}

	private IDisposable PushTaskBarIconFont(float scale)
	{
		return PushIconFont(20f * Math.Clamp(scale, 0.6f, 2f));
	}

	private IDisposable PushHudFont(float fontSize)
	{
		IFontHandle hudFont = GetHudFont(GetNearestHudFontSize(fontSize));
		if (!hudFont.Available)
		{
			return EmptyDisposable.Instance;
		}
		return hudFont.Push();
	}

	private IDisposable PushIconFont(float fontSize)
	{
		IFontHandle iconFont = GetIconFont(GetNearestHudFontSize(fontSize));
		if (!iconFont.Available)
		{
			return EmptyDisposable.Instance;
		}
		return iconFont.Push();
	}

	private IDisposable PushTaskBarItemFont(TaskBarItem item, float scale)
	{
		if (item.IsIcon)
		{
			return PushTaskBarIconFont(scale);
		}
		if (!(Math.Abs(item.TextScale - 1f) > 0.001f))
		{
			return EmptyDisposable.Instance;
		}
		return PushTaskBarFont(scale * item.TextScale);
	}

	private Vector2 CalcTaskBarItemTextSize(TaskBarItem item, float scale)
	{
		if (item.DrawIcon != TaskBarDrawIcon.None)
		{
			float num = MathF.Round(36f * scale);
			return new Vector2(num, num);
		}
		if (!string.IsNullOrWhiteSpace(item.PluginShortcutInternalName))
		{
			float num2 = MathF.Round(36f * scale);
			return new Vector2(num2, num2);
		}
		if (item.IsDalamudIcon)
		{
			float num3 = MathF.Round(36f * scale);
			return new Vector2(num3, num3);
		}
		if (item.GameIconId != 0)
		{
			float num4 = MathF.Round(GetTaskBarGameIconSize(item, scale));
			using (PushTaskBarItemFont(item, scale))
			{
				Vector2 vector = CalcMultilineTextSize(string.IsNullOrEmpty(item.MeasureText) ? item.Text : item.MeasureText);
				return (string.IsNullOrWhiteSpace(item.Text) && string.IsNullOrWhiteSpace(item.MeasureText)) ? new Vector2(num4, num4) : new Vector2(num4 + 6f * scale + vector.X, Math.Max(num4, vector.Y));
			}
		}
		using (PushTaskBarItemFont(item, scale))
		{
			return CalcMultilineTextSize(string.IsNullOrEmpty(item.MeasureText) ? item.Text : item.MeasureText);
		}
	}

	private Vector2 CalcTaskBarItemDrawTextSize(TaskBarItem item, float scale)
	{
		if (item.DrawIcon != TaskBarDrawIcon.None)
		{
			float num = MathF.Round(36f * scale);
			return new Vector2(num, num);
		}
		if (!string.IsNullOrWhiteSpace(item.PluginShortcutInternalName))
		{
			float num2 = MathF.Round(36f * scale);
			return new Vector2(num2, num2);
		}
		if (item.IsDalamudIcon)
		{
			float num3 = MathF.Round(36f * scale);
			return new Vector2(num3, num3);
		}
		if (item.GameIconId != 0)
		{
			float num4 = MathF.Round(GetTaskBarGameIconSize(item, scale));
			using (PushTaskBarItemFont(item, scale))
			{
				Vector2 vector = CalcMultilineTextSize(item.Text);
				return string.IsNullOrWhiteSpace(item.Text) ? new Vector2(num4, num4) : new Vector2(num4 + 6f * scale + vector.X, Math.Max(num4, vector.Y));
			}
		}
		using (PushTaskBarItemFont(item, scale))
		{
			return CalcMultilineTextSize(item.Text);
		}
	}

	private static bool IsTaskBarIconLikeItem(TaskBarItem item)
	{
		if (item.DrawIcon == TaskBarDrawIcon.None && !item.IsDalamudIcon && string.IsNullOrWhiteSpace(item.PluginShortcutInternalName))
		{
			if (item.GameIconId != 0)
			{
				return string.IsNullOrWhiteSpace(item.Text);
			}
			return false;
		}
		return true;
	}

	private Vector2 CalcTaskBarItemLayoutSize(TaskBarItem item, float scale)
	{
		Vector2 result = CalcTaskBarItemTextSize(item, scale);
		if (!IsTaskBarIconLikeItem(item))
		{
			return result;
		}
		float num = MathF.Round(Math.Max(36f * scale, Math.Max(result.X, result.Y)));
		return new Vector2(num, num);
	}

	private static Vector2 CalcMultilineTextSize(string text)
	{
		float textLineHeight = ImGui.GetTextLineHeight();
		if (text.IndexOf('\n', StringComparison.Ordinal) < 0)
		{
			return new Vector2(ImGui.CalcTextSize(text).X, textLineHeight);
		}
		float num = 0f;
		int num2 = 0;
		int num3 = 0;
		while (num3 <= text.Length)
		{
			int num4 = text.IndexOf('\n', num3);
			int num5 = ((num4 < 0) ? text.Length : num4);
			int num6 = num3;
			string text2 = text.Substring(num6, num5 - num6);
			num = Math.Max(num, ImGui.CalcTextSize(text2).X);
			num2++;
			if (num4 < 0)
			{
				break;
			}
			num3 = num4 + 1;
		}
		return new Vector2(num, textLineHeight * (float)num2);
	}

	private static float GetTaskBarGameIconSize(TaskBarItem item, float scale)
	{
		return ((string.IsNullOrWhiteSpace(item.Text) && string.IsNullOrWhiteSpace(item.MeasureText)) ? 32f : 22f) * scale;
	}

	private IFontHandle GetHudFont(int fontSize)
	{
		if (hudFonts.TryGetValue(fontSize, out IFontHandle value))
		{
			return value;
		}
		value = pluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(delegate(IFontAtlasBuildToolkit e)
		{
			e.OnPreBuild(delegate(IFontAtlasBuildToolkitPreBuild tk)
			{
				tk.AddDalamudDefaultFont(fontSize);
			});
		});
		hudFonts.Add(fontSize, value);
		return value;
	}

	private IFontHandle GetIconFont(int fontSize)
	{
		if (iconFonts.TryGetValue(fontSize, out IFontHandle value))
		{
			return value;
		}
		value = pluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(delegate(IFontAtlasBuildToolkit e)
		{
			e.OnPreBuild(delegate(IFontAtlasBuildToolkitPreBuild tk)
			{
				SafeFontConfig fontConfig = new SafeFontConfig
				{
					SizePx = fontSize
				};
				tk.AddFontAwesomeIconFont(in fontConfig);
			});
		});
		iconFonts.Add(fontSize, value);
		return value;
	}

	private static int GetNearestHudFontSize(float fontSize)
	{
		int targetSize = Math.Clamp((int)MathF.Round(fontSize), HudFontSizes[0], HudFontSizes[^1]);
		return HudFontSizes.MinBy((int size) => Math.Abs(size - targetSize));
	}

	private bool IsHudFontReady(float fontSize)
	{
		return GetHudFont(GetNearestHudFontSize(fontSize)).Available;
	}

	private bool IsIconFontReady(float fontSize)
	{
		return GetIconFont(GetNearestHudFontSize(fontSize)).Available;
	}

	private static Vector2 SnapToPixel(Vector2 value)
	{
		return new Vector2(MathF.Round(value.X), MathF.Round(value.Y));
	}

	private static float SnapToPixel(float value)
	{
		return MathF.Round(value);
	}

	private static string FormatNumber(uint value)
	{
		return value.ToString("N0");
	}

	private void DrawSkillsWindow()
	{
		if (!config.ShowPartyInfo || !ShouldDrawPartyInfoNow())
		{
			return;
		}
		PartyInfoRenderSnapshot partyInfoRenderSnapshot = GetPartyInfoRenderSnapshot();
		if (partyInfoRenderSnapshot.ShowMitigations || partyInfoRenderSnapshot.ShowFoodCheck || partyInfoRenderSnapshot.ShowLimitBreak)
		{
			if (config.ShowPartyInfoPreview)
			{
				DrawPartyInfoPreview(partyInfoRenderSnapshot);
			}
			else
			{
				DrawNativeAttachedPartyInfo(partyInfoRenderSnapshot.Groups, partyInfoRenderSnapshot.FoodStatuses, partyInfoRenderSnapshot.Lookups, partyInfoRenderSnapshot.ShowLimitBreak);
			}
		}
	}

	private bool ShouldDrawPartyInfoNow()
	{
		if (!config.ShowPartyInfoPreview)
		{
			return combatState.IsInDutyActive;
		}
		return true;
	}

	private void DrawPartyInfoPreview(PartyInfoRenderSnapshot snapshot)
	{
		ImDrawListPtr backgroundDrawList = ImGui.GetBackgroundDrawList();
		ThemePalette effectiveTheme = GetEffectiveTheme();
		ImGuiViewportPtr mainViewport = ImGui.GetMainViewport();
		int num = Math.Max(snapshot.Groups.Count, snapshot.FoodStatuses.Count);
		if (num > 0 || snapshot.ShowLimitBreak)
		{
			int num2 = Math.Max(1, (int)MathF.Ceiling((float)num / 2f));
			float num3 = Math.Min(760f, Math.Max(520f, mainViewport.WorkSize.X - 48f));
			float num4 = (num3 - 32f - 12f) / 2f;
			float num5 = 38f + (snapshot.ShowLimitBreak ? 26f : 0f) + (float)num2 * 46f + (float)Math.Max(0, num2 - 1) * 5f + 16f;
			Vector2 vector = SnapToPixel(new Vector2(mainViewport.WorkPos.X + mainViewport.WorkSize.X - num3 - 24f, mainViewport.WorkPos.Y + Math.Max(24f, (mainViewport.WorkSize.Y - num5) * 0.5f)));
			Vector2 pMax = vector + new Vector2(num3, num5);
			backgroundDrawList.AddRectFilled(vector, pMax, ImGui.GetColorU32(WithOpacity(effectiveTheme.Surface, 0.82f)), 10f);
			backgroundDrawList.AddRect(vector, pMax, ImGui.GetColorU32(WithOpacity(effectiveTheme.Border, 0.78f)), 10f, ImDrawFlags.None, 1.4f);
			backgroundDrawList.AddText(vector + new Vector2(16f, 10f), ImGui.GetColorU32(effectiveTheme.Text), "队伍信息预览（全职业）");
			float num6 = vector.Y + 16f + 22f;
			if (snapshot.ShowLimitBreak)
			{
				DrawLimitBreakGauge(backgroundDrawList, vector + new Vector2(16f, num6 - vector.Y), num3 - 32f, 18f, new LimitBreakGaugeSnapshot(2f, 3f, 3));
				num6 += 26f;
			}
			for (int i = 0; i < num; i++)
			{
				int num7 = i / num2;
				int num8 = i % num2;
				Vector2 rowMin = new Vector2(vector.X + 16f + (float)num7 * (num4 + 12f), num6 + (float)num8 * 51f);
				PartyCooldownGroupEntry partyCooldownGroupEntry = ((i < snapshot.Groups.Count) ? snapshot.Groups[i] : null);
				PartyFoodStatusEntry foodStatus = (snapshot.ShowFoodCheck ? FindPartyInfoPreviewFoodStatus(snapshot.FoodStatuses, snapshot.Lookups, partyCooldownGroupEntry, i) : null);
				DrawPartyInfoPreviewRow(backgroundDrawList, rowMin, num4, 46f, partyCooldownGroupEntry, foodStatus);
			}
		}
	}

	private static PartyFoodStatusEntry? FindPartyInfoPreviewFoodStatus(IReadOnlyList<PartyFoodStatusEntry> foodStatuses, PartyInfoLookupMaps lookups, PartyCooldownGroupEntry? group, int index)
	{
		if ((object)group != null)
		{
			if (group.SourceEntityId != 0 && lookups.FoodByEntityId.TryGetValue(group.SourceEntityId, out PartyFoodStatusEntry value))
			{
				return value;
			}
			if (group.SourceObjectId != 0L && lookups.FoodByObjectId.TryGetValue(group.SourceObjectId, out value))
			{
				return value;
			}
			if (group.PartySlot >= 0 && lookups.FoodByPartySlot.TryGetValue(group.PartySlot, out value))
			{
				return value;
			}
			return null;
		}
		if (index >= foodStatuses.Count)
		{
			return null;
		}
		return foodStatuses[index];
	}

	private void DrawPartyInfoPreviewRow(ImDrawListPtr drawList, Vector2 rowMin, float rowWidth, float rowHeight, PartyCooldownGroupEntry? group, PartyFoodStatusEntry? foodStatus)
	{
		ThemePalette effectiveTheme = GetEffectiveTheme();
		Vector2 pMax = rowMin + new Vector2(rowWidth, rowHeight);
		drawList.AddRectFilled(rowMin, pMax, ImGui.GetColorU32(WithOpacity(effectiveTheme.SurfaceAlt, 0.78f)), 8f);
		drawList.AddRect(rowMin, pMax, ImGui.GetColorU32(WithOpacity(effectiveTheme.Border, 0.72f)), 8f, ImDrawFlags.None, 1f);
		uint iconId = group?.SourceJobIconId ?? 0;
		string text = group?.SourceName ?? foodStatus?.SourceName ?? "预览队员";
		string text2 = group?.SourceJobName ?? foodStatus?.SourceJobName ?? string.Empty;
		IReadOnlyList<CooldownEntry> readOnlyList2;
		if ((object)group == null)
		{
			IReadOnlyList<CooldownEntry> readOnlyList = Array.Empty<CooldownEntry>();
			readOnlyList2 = readOnlyList;
		}
		else
		{
			readOnlyList2 = GetNativeAttachedMitigationCooldowns(group.Cooldowns);
		}
		IReadOnlyList<CooldownEntry> readOnlyList3 = readOnlyList2;
		Vector2 vector = rowMin + new Vector2(6f, 6f);
		Vector2 max = vector + new Vector2(34f, 34f);
		DrawGameIconImage(drawList, iconId, vector, max, fillBounds: true, bypassMissingRetry: true);
		float num = max.X + 8f;
		drawList.AddText(new Vector2(num, rowMin.Y + 6f), ImGui.GetColorU32(effectiveTheme.Text), text);
		drawList.AddText(new Vector2(num, rowMin.Y + 23f), ImGui.GetColorU32(effectiveTheme.TextMuted), text2);
		int num2 = readOnlyList3.Count + (((object)foodStatus != null) ? 1 : 0);
		if (num2 > 0)
		{
			float num3 = (float)num2 * 32f + (float)Math.Max(0, num2 - 1) * 4f;
			float num4 = Math.Max(num + 72f, pMax.X - num3 - 8f);
			float y = rowMin.Y + (rowHeight - 32f) * 0.5f;
			for (int i = 0; i < readOnlyList3.Count; i++)
			{
				DrawNativeAttachedCooldownIcon(drawList, new Vector2(num4 + (float)i * 36f, y), readOnlyList3[i], 32f);
			}
			if ((object)foodStatus != null)
			{
				DrawNativeAttachedFoodIcon(drawList, new Vector2(num4 + (float)readOnlyList3.Count * 36f, y), foodStatus, 32f);
			}
		}
	}

	private PartyInfoRenderSnapshot GetPartyInfoRenderSnapshot()
	{
		DateTime utcNow = DateTime.UtcNow;
		if ((object)cachedPartyInfoSnapshot != null && utcNow < nextPartyInfoRefreshAt)
		{
			return cachedPartyInfoSnapshot;
		}
		if (config.ShowPartyInfoPreview)
		{
			IReadOnlyList<PartyCooldownGroupEntry> partyCooldownGroupsPreview = combatState.GetPartyCooldownGroupsPreview();
			IReadOnlyList<PartyFoodStatusEntry> readOnlyList2;
			if (!config.ShowPartyFoodCheck)
			{
				IReadOnlyList<PartyFoodStatusEntry> readOnlyList = Array.Empty<PartyFoodStatusEntry>();
				readOnlyList2 = readOnlyList;
			}
			else
			{
				readOnlyList2 = combatState.GetPartyFoodStatusesPreview();
			}
			IReadOnlyList<PartyFoodStatusEntry> foodStatuses = readOnlyList2;
			PartyInfoLookupMaps lookups = CreatePartyInfoLookupMaps(partyCooldownGroupsPreview, foodStatuses);
			nativeMitigationCooldownCache.Clear();
			cachedPartyInfoSnapshot = new PartyInfoRenderSnapshot(ShowMitigations: true, config.ShowPartyFoodCheck, config.ShowPartyLimitBreakBar, partyCooldownGroupsPreview, foodStatuses, lookups);
			nextPartyInfoRefreshAt = utcNow + PartyInfoCacheDuration;
			return cachedPartyInfoSnapshot;
		}
		bool flag = IsMitigationCooldownsVisible() && combatState.IsPartyCooldownTrackingActive;
		bool flag2 = config.ShowPartyFoodCheck && combatState.IsPartyStatusTrackingActive;
		bool showPartyLimitBreakBar = config.ShowPartyLimitBreakBar;
		IReadOnlyList<PartyCooldownGroupEntry> readOnlyList4;
		if (!flag)
		{
			IReadOnlyList<PartyCooldownGroupEntry> readOnlyList3 = Array.Empty<PartyCooldownGroupEntry>();
			readOnlyList4 = readOnlyList3;
		}
		else
		{
			readOnlyList4 = combatState.GetPartyCooldownGroups(config);
		}
		IReadOnlyList<PartyCooldownGroupEntry> groups = readOnlyList4;
		IReadOnlyList<PartyFoodStatusEntry> readOnlyList5;
		if (!flag2)
		{
			IReadOnlyList<PartyFoodStatusEntry> readOnlyList = Array.Empty<PartyFoodStatusEntry>();
			readOnlyList5 = readOnlyList;
		}
		else
		{
			readOnlyList5 = combatState.GetPartyFoodStatuses(config);
		}
		IReadOnlyList<PartyFoodStatusEntry> foodStatuses2 = readOnlyList5;
		PartyInfoLookupMaps lookups2 = CreatePartyInfoLookupMaps(groups, foodStatuses2);
		nativeMitigationCooldownCache.Clear();
		cachedPartyInfoSnapshot = new PartyInfoRenderSnapshot(flag, flag2, showPartyLimitBreakBar, groups, foodStatuses2, lookups2);
		nextPartyInfoRefreshAt = utcNow + PartyInfoCacheDuration;
		return cachedPartyInfoSnapshot;
	}

	private static PartyInfoLookupMaps CreatePartyInfoLookupMaps(IReadOnlyList<PartyCooldownGroupEntry> groups, IReadOnlyList<PartyFoodStatusEntry> foodStatuses)
	{
		Dictionary<uint, PartyCooldownGroupEntry> dictionary = new Dictionary<uint, PartyCooldownGroupEntry>();
		Dictionary<ulong, PartyCooldownGroupEntry> dictionary2 = new Dictionary<ulong, PartyCooldownGroupEntry>();
		Dictionary<int, PartyCooldownGroupEntry> dictionary3 = new Dictionary<int, PartyCooldownGroupEntry>();
		foreach (PartyCooldownGroupEntry group in groups)
		{
			if (group.SourceEntityId != 0)
			{
				dictionary.TryAdd(group.SourceEntityId, group);
			}
			if (group.SourceObjectId != 0L)
			{
				dictionary2.TryAdd(group.SourceObjectId, group);
			}
			if (group.PartySlot >= 0)
			{
				dictionary3.TryAdd(group.PartySlot, group);
			}
		}
		Dictionary<uint, PartyFoodStatusEntry> dictionary4 = new Dictionary<uint, PartyFoodStatusEntry>();
		Dictionary<ulong, PartyFoodStatusEntry> dictionary5 = new Dictionary<ulong, PartyFoodStatusEntry>();
		Dictionary<int, PartyFoodStatusEntry> dictionary6 = new Dictionary<int, PartyFoodStatusEntry>();
		foreach (PartyFoodStatusEntry foodStatus in foodStatuses)
		{
			if (foodStatus.SourceEntityId != 0)
			{
				dictionary4.TryAdd(foodStatus.SourceEntityId, foodStatus);
			}
			if (foodStatus.SourceObjectId != 0L)
			{
				dictionary5.TryAdd(foodStatus.SourceObjectId, foodStatus);
			}
			if (foodStatus.PartySlot >= 0)
			{
				dictionary6.TryAdd(foodStatus.PartySlot, foodStatus);
			}
		}
		return new PartyInfoLookupMaps(dictionary, dictionary2, dictionary3, dictionary4, dictionary5, dictionary6);
	}

	private unsafe void DrawNativeAttachedPartyInfo(IReadOnlyList<PartyCooldownGroupEntry> groups, IReadOnlyList<PartyFoodStatusEntry> foodStatuses, PartyInfoLookupMaps lookups, bool showLimitBreak)
	{
		if (groups.Count == 0 && foodStatuses.Count == 0 && !showLimitBreak)
		{
			return;
		}
		AtkUnitBasePtr addonByName = gameGui.GetAddonByName("_PartyList");
		if (addonByName.IsNull)
		{
			return;
		}
		AddonPartyList* address = (AddonPartyList*)addonByName.Address;
		if (!IsNativePartyListVisible(address))
		{
			return;
		}
		int num = Math.Clamp(address->MemberCount, 0, 8);
		if (num <= 0)
		{
			return;
		}
		PartyListNumberArray* ptr = PartyListNumberArray.Instance();
		if (ptr == null)
		{
			return;
		}
		ImDrawListPtr backgroundDrawList = ImGui.GetBackgroundDrawList();
		NativePartyAnchorSnapshot nativePartyAnchorSnapshot = GetNativePartyAnchorSnapshot(address, num, 36f);
		NativePartyMemberAnchor?[] anchors = nativePartyAnchorSnapshot.Anchors;
		float? fallbackJobIconLeft = nativePartyAnchorSnapshot.FallbackJobIconLeft;
		float fallbackJobIconHeight = nativePartyAnchorSnapshot.FallbackJobIconHeight;
		if (showLimitBreak)
		{
			DrawNativeAttachedLimitBreakBar(backgroundDrawList, (from anchor in anchors
				where anchor.HasValue
				select anchor.Value).ToList());
		}
		for (int num2 = 0; num2 < num; num2++)
		{
			_ = ref address->PartyMembers[num2];
			ref PartyListNumberArray.PartyListMemberNumberArray reference = ref ptr->PartyMembers[num2];
			if (reference.MaxHealth <= 0)
			{
				continue;
			}
			uint num3 = (uint)reference.Data[40];
			bool flag = lookups.GroupsByEntityId.TryGetValue(num3, out PartyCooldownGroupEntry value) || lookups.GroupsByObjectId.TryGetValue(num3, out value) || lookups.GroupsByPartySlot.TryGetValue(num2, out value);
			bool flag2 = lookups.FoodByEntityId.TryGetValue(num3, out PartyFoodStatusEntry value2) || lookups.FoodByObjectId.TryGetValue(num3, out value2) || lookups.FoodByPartySlot.TryGetValue(num2, out value2);
			if (!flag && !flag2)
			{
				continue;
			}
			NativePartyMemberAnchor? nativePartyMemberAnchor = anchors[num2];
			if (!nativePartyMemberAnchor.HasValue)
			{
				continue;
			}
			NativePartyMemberAnchor valueOrDefault = nativePartyMemberAnchor.GetValueOrDefault();
			Vector2 vector = valueOrDefault.RowMin;
			float num4 = valueOrDefault.RowH;
			if (!valueOrDefault.HasJobIconAnchor && fallbackJobIconLeft.HasValue)
			{
				float valueOrDefault2 = fallbackJobIconLeft.GetValueOrDefault();
				float num5 = vector.Y + num4 * 0.5f;
				vector = new Vector2(valueOrDefault2, num5 - fallbackJobIconHeight * 0.5f);
				num4 = fallbackJobIconHeight;
			}
			IReadOnlyList<CooldownEntry> readOnlyList2;
			if (!flag || (object)value == null)
			{
				IReadOnlyList<CooldownEntry> readOnlyList = Array.Empty<CooldownEntry>();
				readOnlyList2 = readOnlyList;
			}
			else
			{
				readOnlyList2 = GetNativeAttachedMitigationCooldowns(value.Cooldowns);
			}
			IReadOnlyList<CooldownEntry> readOnlyList3 = readOnlyList2;
			int num6 = readOnlyList3.Count + ((flag2 && (object)value2 != null) ? 1 : 0);
			if (num6 != 0)
			{
				float num7 = (float)num6 * 36f + (float)Math.Max(0, num6 - 1) * 5f;
				float num8 = vector.X - 16f - num7;
				float y = vector.Y + (num4 - 36f) * 0.5f;
				for (int num9 = 0; num9 < readOnlyList3.Count; num9++)
				{
					DrawNativeAttachedCooldownIcon(backgroundDrawList, new Vector2(num8 + (float)num9 * 41f, y), readOnlyList3[num9], 36f);
				}
				if (flag2 && (object)value2 != null)
				{
					DrawNativeAttachedFoodIcon(backgroundDrawList, new Vector2(num8 + (float)readOnlyList3.Count * 41f, y), value2, 36f);
				}
			}
		}
	}

	private unsafe NativePartyAnchorSnapshot GetNativePartyAnchorSnapshot(AddonPartyList* addon, int memberCount, float fallbackIconSize)
	{
		DateTime utcNow = DateTime.UtcNow;
		if ((object)cachedNativePartyAnchorSnapshot != null && utcNow < nextNativePartyAnchorRefreshAt && cachedNativePartyAnchorSnapshot.Anchors.Length == memberCount)
		{
			return cachedNativePartyAnchorSnapshot;
		}
		NativePartyMemberAnchor?[] array = new NativePartyMemberAnchor?[memberCount];
		List<float> list = new List<float>(memberCount);
		float num = 0f;
		int num2 = 0;
		for (int i = 0; i < memberCount; i++)
		{
			if (TryGetNativePartyMemberAnchor(addon->PartyMembers[i], out var rowMin, out var rowRight, out var rowH, out var hasJobIconAnchor))
			{
				NativePartyMemberAnchor value = new NativePartyMemberAnchor(rowMin, rowRight, rowH, hasJobIconAnchor);
				array[i] = value;
				if (hasJobIconAnchor)
				{
					list.Add(value.RowMin.X);
					num += value.RowH;
					num2++;
				}
			}
		}
		float? fallbackJobIconLeft = null;
		float fallbackJobIconHeight = fallbackIconSize;
		if (num2 > 0)
		{
			list.Sort();
			fallbackJobIconLeft = list[list.Count / 2];
			fallbackJobIconHeight = num / (float)num2;
		}
		cachedNativePartyAnchorSnapshot = new NativePartyAnchorSnapshot(array, fallbackJobIconLeft, fallbackJobIconHeight);
		nextNativePartyAnchorRefreshAt = utcNow + NativePartyAnchorCacheDuration;
		return cachedNativePartyAnchorSnapshot;
	}

	private unsafe static bool IsNativePartyListVisible(AddonPartyList* addon)
	{
		if (addon == null || !addon->IsVisible || addon->Alpha == 0)
		{
			return false;
		}
		if (addon->RootNode != null)
		{
			return addon->RootNode->IsVisible();
		}
		return false;
	}

	private void DrawNativeAttachedLimitBreakBar(ImDrawListPtr drawList, IReadOnlyList<NativePartyMemberAnchor> anchors)
	{
		if (anchors.Count != 0 && TryReadLimitBreakGauge(out var gauge))
		{
			float num = anchors.Min((NativePartyMemberAnchor anchor) => anchor.RowMin.X);
			float num2 = anchors.Max((NativePartyMemberAnchor anchor) => anchor.RowRight);
			float num3 = anchors.Min((NativePartyMemberAnchor anchor) => anchor.RowMin.Y);
			float num4 = anchors.Max((NativePartyMemberAnchor anchor) => anchor.RowMin.Y + anchor.RowH);
			float num5 = Math.Max(1f, num2 - num);
			float num6 = Math.Clamp(num5, 250f, 400f);
			float x = num + (num5 - num6) * 0.5f + 6f;
			float y = ((config.PartyLimitBreakBarPosition == 0) ? (num3 - 18f - 20f) : (num4 + 20f));
			DrawLimitBreakGauge(drawList, new Vector2(x, y), num6, 18f, gauge);
		}
	}

	private unsafe bool TryReadLimitBreakGauge(out LimitBreakGaugeSnapshot gauge)
	{
		gauge = default(LimitBreakGaugeSnapshot);
		LimitBreakController* ptr = LimitBreakController.Instance();
		if (ptr == null)
		{
			return false;
		}
		int num = Math.Clamp((int)ptr->BarCount, 1, 3);
		ushort barUnits = ptr->BarUnits;
		int num2 = barUnits * num;
		if (barUnits <= 0 || num2 <= 0)
		{
			return false;
		}
		float current = Math.Clamp((int)ptr->CurrentUnits, 0f, num2);
		gauge = new LimitBreakGaugeSnapshot(current, num2, num);
		return true;
	}

	private unsafe static float ReadGaugeBarVisualValue(AtkComponentGaugeBar* gaugeBar, int segmentMax)
	{
		if (gaugeBar == null || segmentMax <= 0)
		{
			return 0f;
		}
		AtkNineGridNode* mainFillNode = gaugeBar->PrimaryFill.MainFillNode;
		if (mainFillNode == null || !mainFillNode->IsVisible())
		{
			return 0f;
		}
		float num = Math.Max(0f, (float)(int)mainFillNode->Width * Math.Max(0f, mainFillNode->ScaleX));
		float num2 = Math.Max(0f, gaugeBar->MaxFillPositionX);
		if (num2 <= 1f && gaugeBar->BackdropImageNode != null)
		{
			num2 = Math.Max(0f, (float)(int)gaugeBar->BackdropImageNode->Width * Math.Max(0f, gaugeBar->BackdropImageNode->ScaleX));
		}
		if (num2 <= 1f)
		{
			return 0f;
		}
		return (float)segmentMax * Math.Clamp(num / num2, 0f, 1f);
	}

	private void DrawLimitBreakGauge(ImDrawListPtr drawList, Vector2 pos, float width, float height, LimitBreakGaugeSnapshot gauge)
	{
		Vector2 vector = pos;
		Vector2 vector2 = pos + new Vector2(width, height);
		float num = height * 0.48f;
		float num2 = ((gauge.Max > 0f) ? Math.Clamp(gauge.Current / gauge.Max, 0f, 1f) : 0f);
		bool flag = num2 >= 0.995f;
		int num3 = Math.Clamp(gauge.Segments, 1, 3);
		ThemePalette effectiveTheme = GetEffectiveTheme();
		uint colorU = ImGui.GetColorU32(flag ? new Vector4(1f, 0.86f, 0.34f, 1f) : new Vector4(0.86f, 0.64f, 0.28f, 0.96f));
		uint colorU2 = ImGui.GetColorU32(WithOpacity(effectiveTheme.Border, 0.78f));
		uint colorU3 = ImGui.GetColorU32(WithOpacity(effectiveTheme.Surface, 0.58f));
		uint colorU4 = ImGui.GetColorU32(WithOpacity(effectiveTheme.SurfaceAlt, 0.42f));
		Vector4 col = (flag ? new Vector4(1f, 0.58f, 0.14f, 0.96f) : new Vector4(0.25f, 0.55f, 1f, 0.94f));
		Vector4 col2 = (flag ? new Vector4(1f, 0.86f, 0.34f, 0.74f) : new Vector4(0.52f, 0.92f, 1f, 0.7f));
		Vector4 col3 = new Vector4(1f, 0.58f, 0.14f, 0.96f);
		Vector4 col4 = new Vector4(1f, 0.86f, 0.34f, 0.74f);
		uint colorU5 = ImGui.GetColorU32(col);
		uint colorU6 = ImGui.GetColorU32(col2);
		uint colorU7 = ImGui.GetColorU32(col3);
		uint colorU8 = ImGui.GetColorU32(col4);
		Vector2 vector3 = vector + new Vector2(3f, 3f);
		Vector2 vector4 = vector2 - new Vector2(3f, 3f);
		float num4 = Math.Max(1f, vector4.X - vector3.X);
		float num5 = Math.Max(1f, vector4.Y - vector3.Y);
		float num6 = Math.Max(1f, num - 3f);
		float num7 = vector3.X + num4 * num2;
		float num8 = gauge.Max / (float)num3;
		int num9 = ((num8 > 0f) ? Math.Clamp((int)MathF.Floor(gauge.Current / num8 + 0.0001f), 0, num3) : 0);
		float num10 = vector3.X + num4 * (float)num9 / (float)num3;
		drawList.AddRectFilled(vector + new Vector2(0f, 2f), vector2 + new Vector2(0f, 3f), ImGui.GetColorU32(WithOpacity(effectiveTheme.TextShadow, 0.24f)), num);
		drawList.AddRectFilled(vector, vector2, ImGui.GetColorU32(WithOpacity(effectiveTheme.SurfaceAlt, 0.68f)), num);
		drawList.AddRect(vector + new Vector2(1f, 1f), vector2 - new Vector2(1f, 1f), colorU2, num - 1f, ImDrawFlags.None, 1f);
		drawList.AddRectFilled(vector3, vector4, colorU3, num6);
		drawList.AddRectFilled(vector3 + new Vector2(0f, 1f), vector4 - new Vector2(0f, num5 * 0.45f), colorU4, num6);
		if (num2 > 0.001f)
		{
			Vector2 vector5 = new Vector2(num7, vector4.Y);
			float rounding = num6;
			drawList.AddRectFilled(vector3, vector5, colorU5, rounding, ImDrawFlags.RoundCornersAll);
			drawList.AddRectFilled(vector3 + new Vector2(1f, 2f), vector5 - new Vector2(1f, num5 * 0.46f), colorU6, Math.Max(1f, num6 - 1f), ImDrawFlags.RoundCornersAll);
			if (num10 > vector3.X + 1f)
			{
				Vector2 vector6 = new Vector2(Math.Min(num10, num7), vector4.Y);
				ImDrawFlags flags = ((vector6.X >= num7 - 0.5f) ? ImDrawFlags.RoundCornersAll : ImDrawFlags.RoundCornersLeft);
				drawList.AddRectFilled(vector3, vector6, colorU7, rounding, flags);
				drawList.AddRectFilled(vector3 + new Vector2(1f, 2f), vector6 - new Vector2(1f, num5 * 0.46f), colorU8, Math.Max(1f, num6 - 1f), flags);
				if (!flag && num7 > num10 + 1f)
				{
					float num11 = Math.Min(22f, num7 - num10);
					Vector2 vector7 = new Vector2(Math.Max(vector3.X, num10 - 6f), vector3.Y);
					Vector2 vector8 = new Vector2(Math.Min(num7, num10 + num11), vector4.Y);
					Vector2 pMin = vector7 + new Vector2(0f, 2f);
					Vector2 pMax = vector8 - new Vector2(0f, num5 * 0.46f);
					drawList.AddRectFilledMultiColor(vector7, vector8, colorU7, colorU5, colorU5, colorU7);
					drawList.AddRectFilledMultiColor(pMin, pMax, colorU8, colorU6, colorU6, colorU8);
				}
			}
			drawList.AddLine(new Vector2(vector3.X + 4f, vector3.Y + 2f), new Vector2(Math.Max(vector3.X + 4f, num7 - 4f), vector3.Y + 2f), ImGui.GetColorU32(new Vector4(1f, 0.95f, 0.72f, 0.42f)), 1f);
		}
		for (int i = 1; i < num3; i++)
		{
			float num12 = MathF.Round(vector3.X + num4 * (float)i / (float)num3);
			float num13 = vector3.Y + 1f;
			float num14 = vector4.Y - 1f;
			float num15 = vector.Y + height * 0.5f;
			uint colorU9 = ImGui.GetColorU32(WithOpacity(effectiveTheme.TextShadow, 0.58f));
			uint colorU10 = ImGui.GetColorU32(new Vector4(1f, 0.86f, 0.34f, 0.72f));
			uint colorU11 = ImGui.GetColorU32(WithOpacity(effectiveTheme.Text, 0.8f));
			drawList.AddLine(new Vector2(num12 - 1f, num13), new Vector2(num12 - 1f, num14), colorU9, 1f);
			drawList.AddLine(new Vector2(num12, num13), new Vector2(num12, num14), colorU10, 1.5f);
			drawList.AddLine(new Vector2(num12 + 1f, num13 + 2f), new Vector2(num12 + 1f, num14 - 2f), colorU11, 1f);
			float num16 = Math.Max(2f, height * 0.14f);
			drawList.AddQuadFilled(new Vector2(num12, num15 - num16), new Vector2(num12 + num16, num15), new Vector2(num12, num15 + num16), new Vector2(num12 - num16, num15), ImGui.GetColorU32(new Vector4(1f, 0.78f, 0.22f, 0.62f)));
		}
		drawList.AddRect(vector3, vector4, ImGui.GetColorU32(WithOpacity(effectiveTheme.Border, 0.32f)), num6, ImDrawFlags.None, 1f);
		drawList.AddRect(vector, vector2, colorU, num, ImDrawFlags.None, flag ? 2f : 1.45f);
		if (flag)
		{
			drawList.AddRect(vector - new Vector2(2f, 2f), vector2 + new Vector2(2f, 2f), ImGui.GetColorU32(new Vector4(1f, 0.66f, 0.16f, 0.26f)), num + 2f, ImDrawFlags.None, 3f);
		}
	}

	private unsafe static bool TryGetNativePartyMemberAnchor(AddonPartyList.PartyListMemberStruct nativeMember, out Vector2 rowMin, out float rowRight, out float rowH, out bool hasJobIconAnchor)
	{
		rowMin = Vector2.Zero;
		rowRight = 0f;
		rowH = 0f;
		hasJobIconAnchor = false;
		bool hasBounds = false;
		Vector2 boundsMin = Vector2.Zero;
		float boundsRight = 0f;
		float boundsH = 0f;
		float rowVisualCenterY = 0f;
		int rowVisualCenterWeight = 0;
		if (nativeMember.PartyMemberComponent != null && nativeMember.PartyMemberComponent->AtkResNode != null && nativeMember.PartyMemberComponent->AtkResNode->IsVisible())
		{
			AtkResNode* atkResNode = nativeMember.PartyMemberComponent->AtkResNode;
			IncludeNode(atkResNode->ScreenX, atkResNode->ScreenY, (int)atkResNode->Width, (int)atkResNode->Height, atkResNode->ScaleX, atkResNode->ScaleY);
		}
		float x = 0f;
		float num = 0f;
		float num2 = 0f;
		if (nativeMember.ClassJobIcon != null && nativeMember.ClassJobIcon->IsVisible())
		{
			AtkImageNode* classJobIcon = nativeMember.ClassJobIcon;
			x = classJobIcon->ScreenX;
			num = classJobIcon->ScreenY;
			num2 = Math.Max(1f, (float)(int)classJobIcon->Height * Math.Max(0.1f, classJobIcon->ScaleY));
			hasJobIconAnchor = true;
			IncludeNode(classJobIcon->ScreenX, classJobIcon->ScreenY, (int)classJobIcon->Width, (int)classJobIcon->Height, classJobIcon->ScaleX, classJobIcon->ScaleY);
		}
		if (nativeMember.Name != null && nativeMember.Name->IsVisible())
		{
			AtkTextNode* name = nativeMember.Name;
			IncludeNode(name->ScreenX, name->ScreenY, (int)name->Width, (int)name->Height, name->ScaleX, name->ScaleY);
		}
		if (nativeMember.HPGaugeBar != null && nativeMember.HPGaugeBar->AtkResNode != null && nativeMember.HPGaugeBar->AtkResNode->IsVisible())
		{
			AtkResNode* atkResNode2 = nativeMember.HPGaugeBar->AtkResNode;
			float height = Math.Max(1f, (float)(int)atkResNode2->Height * Math.Max(0.1f, atkResNode2->ScaleY));
			IncludeNode(atkResNode2->ScreenX, atkResNode2->ScreenY, (int)atkResNode2->Width, (int)atkResNode2->Height, atkResNode2->ScaleX, atkResNode2->ScaleY);
			IncludeRowCenter(atkResNode2->ScreenY, height);
		}
		if (nativeMember.MPGaugeBar != null && nativeMember.MPGaugeBar->AtkResNode != null && nativeMember.MPGaugeBar->AtkResNode->IsVisible())
		{
			AtkResNode* atkResNode3 = nativeMember.MPGaugeBar->AtkResNode;
			float height2 = Math.Max(1f, (float)(int)atkResNode3->Height * Math.Max(0.1f, atkResNode3->ScaleY));
			IncludeNode(atkResNode3->ScreenX, atkResNode3->ScreenY, (int)atkResNode3->Width, (int)atkResNode3->Height, atkResNode3->ScaleX, atkResNode3->ScaleY);
			IncludeRowCenter(atkResNode3->ScreenY, height2);
		}
		if (!hasBounds || boundsRight <= boundsMin.X || boundsH <= 1f)
		{
			return false;
		}
		float num3 = ((rowVisualCenterWeight > 0) ? (rowVisualCenterY / (float)rowVisualCenterWeight) : (hasJobIconAnchor ? (num + num2 * 0.5f) : (boundsMin.Y + boundsH * 0.5f)));
		rowMin = (hasJobIconAnchor ? new Vector2(x, num3 - num2 * 0.5f) : boundsMin);
		rowRight = boundsRight;
		rowH = (hasJobIconAnchor ? num2 : boundsH);
		return true;
		void IncludeNode(float num7, float y, float width, float num6, float scaleX, float scaleY)
		{
			float num4 = Math.Max(1f, width * Math.Max(0.1f, scaleX));
			float num5 = Math.Max(1f, num6 * Math.Max(0.1f, scaleY));
			if (!hasBounds)
			{
				boundsMin = new Vector2(num7, y);
				boundsRight = num7 + num4;
				boundsH = num5;
				hasBounds = true;
			}
			else
			{
				boundsMin = new Vector2(Math.Min(boundsMin.X, num7), Math.Min(boundsMin.Y, y));
				boundsRight = Math.Max(boundsRight, num7 + num4);
				boundsH = Math.Max(boundsH, y + num5 - boundsMin.Y);
			}
		}
		void IncludeRowCenter(float y, float num4)
		{
			rowVisualCenterY += y + num4 * 0.5f;
			rowVisualCenterWeight++;
		}
	}

	private IReadOnlyList<CooldownEntry> GetNativeAttachedMitigationCooldowns(IReadOnlyList<CooldownEntry> cooldowns)
	{
		if (nativeMitigationCooldownCache.TryGetValue(cooldowns, out IReadOnlyList<CooldownEntry> value))
		{
			return value;
		}
		value = (from cooldown in cooldowns.Where(delegate(CooldownEntry cooldown)
			{
				CooldownGroup cooldownGroup = NormalizeCooldownGroup(cooldown.Group);
				return ((cooldownGroup == CooldownGroup.PartyMitigation || cooldownGroup == CooldownGroup.Mitigation) ? true : false) || TrackedActionCatalog.PartyInfoExtraMitigationActionIds.Contains(cooldown.ActionId);
			})
			orderby cooldown.IsReady, cooldown.RemainingCooldownSeconds
			select cooldown).ThenBy<CooldownEntry, string>((CooldownEntry cooldown) => cooldown.Name, StringComparer.Ordinal).ToList();
		nativeMitigationCooldownCache[cooldowns] = value;
		return value;
	}

	private void DrawNativeAttachedCooldownIcon(ImDrawListPtr drawList, Vector2 pos, CooldownEntry cooldown, float size)
	{
		ThemePalette effectiveTheme = GetEffectiveTheme();
		Vector2 vector = pos + new Vector2(size, size);
		DrawGameIconImage(drawList, cooldown.IconId, pos, vector);
		if (!cooldown.IsReady)
		{
			drawList.AddRectFilled(pos, vector, ImGui.GetColorU32(WithOpacity(effectiveTheme.TextShadow, 0.34f)), 3f);
			float remainingCooldownSeconds = cooldown.RemainingCooldownSeconds;
			if (ShouldDrawCooldownIconTime(remainingCooldownSeconds, cooldown.CooldownSeconds))
			{
				DrawCenteredIconText(drawList, FormatCooldownIconTime(remainingCooldownSeconds), pos, vector, ImGui.GetColorU32(WithOpacity(effectiveTheme.Text, 0.96f)), size, bold: true);
			}
		}
		float num = Math.Max(1f, size * 0.05f);
		float value = num * 0.5f;
		drawList.AddRect(pos + new Vector2(value), vector - new Vector2(value), ImGui.GetColorU32(WithOpacity(effectiveTheme.TextShadow, 0.85f)), 3f, ImDrawFlags.None, num + 1f);
		drawList.AddRect(pos + new Vector2(value), vector - new Vector2(value), ImGui.GetColorU32(WithOpacity(effectiveTheme.Border, 0.95f)), 3f, ImDrawFlags.None, num);
	}

	private static bool ShouldDrawCooldownIconTime(float seconds, float expectedMaxSeconds)
	{
		if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds <= 0.05f)
		{
			return false;
		}
		float num = Math.Clamp(expectedMaxSeconds + 30f, 30f, 1800f);
		return seconds <= num;
	}

	private void DrawNativeAttachedFoodIcon(ImDrawListPtr drawList, Vector2 pos, PartyFoodStatusEntry foodStatus, float size)
	{
		ThemePalette effectiveTheme = GetEffectiveTheme();
		Vector2 vector = pos + new Vector2(size, size);
		float rounding = 4f;
		DrawGameIconImage(drawList, foodStatus.IconId, pos, vector, fillBounds: true);
		if (!foodStatus.HasFood)
		{
			drawList.AddRectFilled(pos, vector, ImGui.GetColorU32(WithOpacity(effectiveTheme.Accent, 0.24f)), rounding);
			drawList.AddRect(pos + new Vector2(0.5f), vector - new Vector2(0.5f), ImGui.GetColorU32(WithOpacity(effectiveTheme.Accent, 0.72f)), rounding, ImDrawFlags.None, 1f);
			DrawCenteredIconText(drawList, "!", pos, vector, ImGui.GetColorU32(effectiveTheme.Text), size * 1.08f);
		}
		else if (foodStatus.RemainingSeconds > 0.05f)
		{
			string text = FormatCooldownIconTime(foodStatus.RemainingSeconds);
			DrawBottomIconTextPill(drawList, text, pos, vector, ImGui.GetColorU32(new Vector4(0.82f, 1f, 0.76f, 0.98f)), size);
		}
	}

	private void DrawBottomIconTextPill(ImDrawListPtr drawList, string text, Vector2 min, Vector2 max, uint color, float iconSize)
	{
		ThemePalette effectiveTheme = GetEffectiveTheme();
		if (string.IsNullOrWhiteSpace(text))
		{
			return;
		}
		float fontSize = ImGui.GetFontSize();
		float fontSize2 = Math.Clamp(iconSize * 0.3f, fontSize * 0.64f, fontSize * 0.86f);
		IFontHandle hudFont = GetHudFont(GetNearestHudFontSize(fontSize2));
		if (!hudFont.Available)
		{
			return;
		}
		using (hudFont.Push())
		{
			Vector2 vector = ImGui.CalcTextSize(text);
			Vector2 vector2 = new Vector2(3f, 1f);
			Vector2 vector3 = vector + vector2 * 2f;
			Vector2 vector4 = SnapToPixel(new Vector2(min.X + Math.Max(1f, (max.X - min.X - vector3.X) * 0.5f), max.Y - vector3.Y - 1f));
			Vector2 pMax = SnapToPixel(vector4 + vector3);
			drawList.AddRectFilled(vector4, pMax, ImGui.GetColorU32(WithOpacity(effectiveTheme.TooltipBg, 0.62f)), Math.Max(2f, iconSize * 0.11f));
			drawList.AddText(SnapToPixel(vector4 + vector2 + new Vector2(1f, 1f)), ImGui.GetColorU32(WithOpacity(effectiveTheme.TextShadow, 0.82f)), text);
			drawList.AddText(SnapToPixel(vector4 + vector2), color, text);
		}
	}

	private unsafe void DrawGearsetSwitcherPopup(float opacity, float scale)
	{
		int columns = 3;
		float num = (float)config.TaskBarGearsetButtonWidth * scale;
		float num2 = 18f * scale;
		float num3 = 8f * scale;
		float x = (float)columns * (num + num2) + (float)(columns - 1) * num3 + 20f * scale;
		RaptureGearsetModule* module = UIModule.Instance()->GetRaptureGearsetModule();
		GearsetPopupCache? popupCache = null;
		List<GearsetPopupGroup> visibleGroups = new List<GearsetPopupGroup>();
		if (module != null)
		{
			popupCache = GetGearsetPopupCache(module, module->CurrentGearsetIndex);
			visibleGroups = popupCache.Value.Groups.Where((GearsetPopupGroup group) => !config.TaskBarGearsetHiddenGroups.Contains<string>(group.Group, StringComparer.Ordinal)).ToList();
		}
		bool hasHeader = config.TaskBarGearsetShowPopupHeader && popupCache.HasValue && popupCache.GetValueOrDefault().Entries.Any((GearsetPopupEntry entry) => entry.Selected);
		float y = CalculateGearsetPopupHeight(visibleGroups, columns, hasHeader, scale);
		DrawSimpleTaskBarPopup("AllHud 套装", new Vector2(x, y), opacity, scale, delegate
		{
			if (module == null)
			{
				ImGui.TextDisabled("当前无法读取套装切换模块。请稍后重试。");
			}
			else
			{
				GearsetPopupCache gearsetPopupCache = popupCache ?? GetGearsetPopupCache(module, module->CurrentGearsetIndex);
				if (gearsetPopupCache.Entries.Count == 0)
				{
					ImGui.TextDisabled("没有可用的套装。请先在游戏里创建套装。");
				}
				else
				{
					GearsetPopupEntry gearsetPopupEntry = gearsetPopupCache.Entries.FirstOrDefault((GearsetPopupEntry entry) => entry.Selected);
					if (config.TaskBarGearsetShowPopupHeader && gearsetPopupEntry != default(GearsetPopupEntry))
					{
						DrawGearsetPopupHeader(GetGearsetJobName(gearsetPopupEntry.ClassJobId, gearsetPopupEntry.JobName), (config.TaskBarGearsetShowItemLevel && gearsetPopupEntry.ItemLevel > 0) ? $"套装 {gearsetPopupEntry.Id + 1:00} · {gearsetPopupEntry.JobName} · 装等 {gearsetPopupEntry.ItemLevel}" : $"套装 {gearsetPopupEntry.Id + 1:00} · {gearsetPopupEntry.JobName}", gearsetPopupEntry.IconId, scale, opacity);
					}
					List<GearsetPopupGroup> list = visibleGroups;
					if (list.Count != 0)
					{
						using (PushTaskBarPopupScrollbarStyle(scale))
						{
							ImGui.PushStyleColor(ImGuiCol.ChildBg, WithOpacity(GetEffectiveTheme().Surface, opacity * 0.66f));
							if (ImGui.BeginChild("##AllHudGearsetList", new Vector2(0f, 0f), border: true))
							{
								DrawGearsetPopupGroupedList(list, columns, scale, opacity);
							}
							ImGui.EndChild();
							ImGui.PopStyleColor();
							return;
						}
					}
					ImGui.TextDisabled("所有职业分组都已隐藏，请在套装切换器设置中重新启用。");
				}
			}
		});
	}

	private float GearsetPopupMenuScale()
	{
		return Math.Clamp(config.TaskBarGearsetPopupScale, 0.5f, 3f);
	}

	private float GearsetPopupJobHeaderScaled(float scale)
	{
		return GearsetJobHeaderScale * scale * GearsetPopupMenuScale();
	}

	private float GearsetPopupJobGapScaled(float scale)
	{
		return GearsetJobGapScale * scale * GearsetPopupMenuScale();
	}

	private float CalculateGearsetPopupHeight(IReadOnlyList<GearsetPopupGroup> groups, int columns, bool hasHeader, float scale)
	{
		float num = 18f * scale;
		float num2 = (hasHeader ? (64f * scale) : 0f);
		if (groups.Count == 0)
		{
			return Math.Min(96f * scale, ImGui.GetMainViewport().WorkSize.Y);
		}
		List<GearsetPopupGroup>[] array = BuildGearsetPopupColumns(groups);
		float[] array2 = new float[columns];
		float num3 = 7f * scale + ImGui.GetStyle().ItemSpacing.Y * 2f;
		for (int i = 0; i < columns; i++)
		{
			foreach (GearsetPopupGroup group in array[i])
			{
				array2[i] += CalculateGearsetPopupGroupHeight(group, scale) + num3;
			}
		}
		float num4 = 2f + ImGui.GetStyle().WindowPadding.Y * 2f;
		float value = num + num2 + array2.Max() + num4;
		float max = Math.Max(120f * scale, ImGui.GetMainViewport().WorkSize.Y - 24f * scale);
		return Math.Clamp(value, 96f * scale, max);
	}

	private float CalculateGearsetPopupGroupHeight(GearsetPopupGroup group, float scale)
	{
		float num = (config.TaskBarGearsetShowGroupHeaders ? (31f * scale) : 0f);
		if (config.TaskBarGearsetShowGroupHeaders && config.TaskBarGearsetEnableGroupCollapse && config.TaskBarGearsetCollapsedGroups.Contains<string>(group.Group, StringComparer.Ordinal))
		{
			return num;
		}
		float num5 = (config.TaskBarGearsetShowGroupHeaders ? ImGui.GetStyle().ItemSpacing.Y : 0f);
		return num + num5 + CalculateGearsetPopupChildHeight(group.Jobs, scale);
	}

	private float CalculateGearsetPopupChildHeight(IReadOnlyList<GearsetPopupJobGroup> jobs, float scale)
	{
		float rowH = ((float)config.TaskBarGearsetButtonHeight + 4f) * scale;
		float num = (float)CountDisplayedJobRows(jobs) * rowH;
		float num2 = 0f;
		foreach (GearsetPopupJobGroup job in jobs)
		{
			num2 += GearsetPopupJobHeaderScaled(scale);
			if (IsGearsetJobExpanded(job.ClassJobId))
			{
				num2 += GearsetJobRowPadScale * scale;
			}
			num2 += GearsetPopupJobGapScaled(scale);
		}
		return num2 + num + ImGui.GetStyle().WindowPadding.Y * 2f;
	}

	private bool IsGearsetJobExpanded(uint classJobId)
	{
		if (!config.TaskBarGearsetEnableGroupCollapse)
		{
			return true;
		}
		return config.TaskBarGearsetExpandedJobIds.Contains(classJobId);
	}

	private void SetGearsetJobExpanded(uint classJobId, bool expanded)
	{
		config.TaskBarGearsetExpandedJobIds.Remove(classJobId);
		if (expanded)
		{
			config.TaskBarGearsetExpandedJobIds.Add(classJobId);
		}
		saveConfig();
	}

	private int CountVisibleJobRows(IReadOnlyList<GearsetPopupJobGroup> jobs)
	{
		int num = 0;
		foreach (GearsetPopupJobGroup job in jobs)
		{
			if (IsGearsetJobExpanded(job.ClassJobId))
			{
				num += job.Entries.Count;
			}
		}
		return num;
	}

	private int CountDisplayedJobRows(IReadOnlyList<GearsetPopupJobGroup> jobs)
	{
		int num = CountVisibleJobRows(jobs);
		if (config.TaskBarGearsetEnableGroupScrolling)
		{
			num = Math.Min(num, config.TaskBarGearsetMaxVisibleItemsPerGroup);
		}
		return num;
	}

	private unsafe GearsetPopupCache GetGearsetPopupCache(RaptureGearsetModule* module, int currentGearsetIndex)
	{
		DateTime utcNow = DateTime.UtcNow;
		GearsetPopupCache? gearsetPopupCache = this.gearsetPopupCache;
		if (gearsetPopupCache.HasValue)
		{
			GearsetPopupCache valueOrDefault = gearsetPopupCache.GetValueOrDefault();
			if (valueOrDefault.CurrentGearsetIndex == currentGearsetIndex && utcNow < valueOrDefault.RefreshAt)
			{
				return valueOrDefault;
			}
		}
		int gearsetPopupFingerprint = GetGearsetPopupFingerprint(module, currentGearsetIndex);
		gearsetPopupCache = this.gearsetPopupCache;
		if (gearsetPopupCache.HasValue)
		{
			GearsetPopupCache valueOrDefault2 = gearsetPopupCache.GetValueOrDefault();
			if (valueOrDefault2.Fingerprint == gearsetPopupFingerprint)
			{
				valueOrDefault2 = valueOrDefault2 with
				{
					CurrentGearsetIndex = currentGearsetIndex,
					RefreshAt = utcNow.Add(GearsetPopupCacheDuration)
				};
				this.gearsetPopupCache = valueOrDefault2;
				return valueOrDefault2;
			}
		}
		List<GearsetPopupEntry> entries = BuildGearsetPopupEntries(module, currentGearsetIndex);
		GearsetPopupCache gearsetPopupCache2 = new GearsetPopupCache(gearsetPopupFingerprint, currentGearsetIndex, utcNow.Add(GearsetPopupCacheDuration), entries, BuildGearsetPopupGroups(entries, GearsetPopupGroupOrder));
		this.gearsetPopupCache = gearsetPopupCache2;
		return gearsetPopupCache2;
	}

	private unsafe int GetGearsetPopupFingerprint(RaptureGearsetModule* module, int currentGearsetIndex)
	{
		HashCode hashCode = default(HashCode);
		hashCode.Add(currentGearsetIndex);
		for (byte b = 0; b < 100; b++)
		{
			bool flag = module->IsValidGearset(b);
			hashCode.Add(flag);
			if (flag)
			{
				RaptureGearsetModule.GearsetEntry gearsetEntry = module->Entries[b];
				hashCode.Add(b);
				hashCode.Add(gearsetEntry.ClassJob);
				hashCode.Add(gearsetEntry.ItemLevel);
				hashCode.Add<string>(gearsetEntry.NameString, StringComparer.Ordinal);
				GearsetJobMetadata gearsetJobMetadata = GetGearsetJobMetadata(gearsetEntry.ClassJob);
				hashCode.Add(GetGearsetJobLevel(gearsetJobMetadata));
			}
		}
		return hashCode.ToHashCode();
	}

	private unsafe List<GearsetPopupEntry> BuildGearsetPopupEntries(RaptureGearsetModule* module, int currentGearsetIndex)
	{
		List<GearsetPopupEntry> list = new List<GearsetPopupEntry>();
		for (byte b = 0; b < 100; b++)
		{
			if (module->IsValidGearset(b))
			{
				RaptureGearsetModule.GearsetEntry gearsetEntry = module->Entries[b];
				GearsetJobMetadata gearsetJobMetadata = GetGearsetJobMetadata(gearsetEntry.ClassJob);
				string text = gearsetEntry.NameString;
				if (string.IsNullOrWhiteSpace(text))
				{
					text = (string.IsNullOrWhiteSpace(gearsetJobMetadata.Name) ? $"套装 {b + 1}" : gearsetJobMetadata.Name);
				}
				list.Add(new GearsetPopupEntry(b, text, gearsetEntry.ClassJob, GetGearsetJobLevel(gearsetJobMetadata), gearsetEntry.ItemLevel, gearsetJobMetadata.Group, gearsetJobMetadata.GroupSort, gearsetJobMetadata.JobSort, gearsetJobMetadata.IconId, currentGearsetIndex == b));
			}
		}
		return (from entry in list
			orderby entry.GroupSort, entry.JobSort, entry.Id
			select entry).ToList();
	}

	private List<GearsetPopupGroup>[] BuildGearsetPopupColumns(IReadOnlyList<GearsetPopupGroup> groups)
	{
		List<GearsetPopupGroup>[] array = new List<GearsetPopupGroup>[GearsetPopupFixedColumns.Length];
		for (int i = 0; i < array.Length; i++)
		{
			array[i] = new List<GearsetPopupGroup>();
		}
		List<GearsetPopupGroup> list = groups.ToList();
		for (int j = 0; j < GearsetPopupFixedColumns.Length; j++)
		{
			string[] array2 = GearsetPopupFixedColumns[j];
			foreach (string item in array2)
			{
				int num = list.FindIndex((GearsetPopupGroup g) => string.Equals(g.Group, item, StringComparison.Ordinal));
				if (num >= 0)
				{
					array[j].Add(list[num]);
					list.RemoveAt(num);
				}
			}
		}
		foreach (GearsetPopupGroup item2 in list)
		{
			array[array.Length - 1].Add(item2);
		}
		return array;
	}

	private void DrawGearsetPopupGroupedList(IReadOnlyList<GearsetPopupGroup> groups, int columns, float scale, float opacity)
	{
		float x = ImGui.GetContentRegionAvail().X;
		float y = ImGui.GetContentRegionAvail().Y;
		float num = 8f * scale;
		float num2 = MathF.Floor((x - num * (float)(columns - 1)) / (float)columns);
		Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
		List<GearsetPopupGroup>[] array = BuildGearsetPopupColumns(groups);
		for (int num4 = 0; num4 < columns; num4++)
		{
			ImGui.SetCursorScreenPos(cursorScreenPos + new Vector2((float)num4 * (num2 + num), 0f));
			ImU8String strId = new ImU8String(21, 1);
			strId.AppendLiteral("##AllHudGearsetColumn");
			strId.AppendFormatted(num4);
			if (ImGui.BeginChild(strId, new Vector2(num2, y)))
			{
				foreach (GearsetPopupGroup item in array[num4])
				{
					DrawGearsetPopupGroup(item, ImGui.GetContentRegionAvail().X, scale, opacity);
					ImGui.Dummy(new Vector2(1f, 7f * scale));
				}
			}
			ImGui.EndChild();
		}
		ImGui.SetCursorScreenPos(new Vector2(cursorScreenPos.X, cursorScreenPos.Y + y));
	}

	private List<GearsetPopupGroup> BuildGearsetPopupGroups(IReadOnlyList<GearsetPopupEntry> entries, IReadOnlyList<string> desiredGroupOrder)
	{
		Dictionary<string, Dictionary<uint, List<GearsetPopupEntry>>> dictionary = new Dictionary<string, Dictionary<uint, List<GearsetPopupEntry>>>(StringComparer.Ordinal);
		foreach (GearsetPopupEntry entry in entries)
		{
			if (!dictionary.TryGetValue(entry.Group, out var value))
			{
				value = new Dictionary<uint, List<GearsetPopupEntry>>();
				dictionary[entry.Group] = value;
			}
			if (!value.TryGetValue(entry.ClassJobId, out var value2))
			{
				value2 = new List<GearsetPopupEntry>();
				value[entry.ClassJobId] = value2;
			}
			value2.Add(entry);
		}
		List<GearsetPopupGroup> list = new List<GearsetPopupGroup>();
		foreach (string item in desiredGroupOrder)
		{
			if (!dictionary.TryGetValue(item, out var value3) || value3.Count == 0)
			{
				continue;
			}
			List<GearsetPopupJobGroup> list2 = new List<GearsetPopupJobGroup>();
			foreach (KeyValuePair<uint, List<GearsetPopupEntry>> item2 in (from pair in value3
				orderby pair.Value[0].JobSort, pair.Key
				select pair).ToList())
			{
				List<GearsetPopupEntry> list3 = item2.Value;
				string text = GetGearsetJobName(item2.Key, list3[0].JobName);
				list2.Add(new GearsetPopupJobGroup(item2.Key, text, list3[0].JobSort, list3[0].IconId, list3));
			}
			list.Add(new GearsetPopupGroup(item, list2));
		}
		return list;
	}

	private void DrawGearsetPopupGroup(GearsetPopupGroup group, float width, float scale, float opacity)
	{
		bool flag = config.TaskBarGearsetEnableGroupCollapse && config.TaskBarGearsetCollapsedGroups.Contains<string>(group.Group, StringComparer.Ordinal);
		if (config.TaskBarGearsetShowGroupHeaders)
		{
			int num = group.Jobs.Sum((GearsetPopupJobGroup job) => job.Entries.Count);
			if (DrawGearsetPopupGroupHeader(group.Group, num, flag, width, scale, opacity) && config.TaskBarGearsetEnableGroupCollapse)
			{
				SetGearsetGroupCollapsed(group.Group, !flag);
				flag = !flag;
			}
		}
		else
		{
			flag = false;
		}
		if (flag)
		{
			return;
		}
		float num2 = CalculateGearsetPopupChildHeight(group.Jobs, scale);
		bool flag2 = config.TaskBarGearsetEnableGroupScrolling && CountVisibleJobRows(group.Jobs) > config.TaskBarGearsetMaxVisibleItemsPerGroup;
		ImGuiWindowFlags flags = (flag2 ? ImGuiWindowFlags.None : (ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse));
		ImGui.PushStyleColor(ImGuiCol.ChildBg, GetGearsetGroupColor(group.Group, opacity * 0.09f));
		ImU8String strId = new ImU8String(21, 1);
		strId.AppendLiteral("##AllHudGearsetGroup_");
		strId.AppendFormatted(group.Group);
		if (ImGui.BeginChild(strId, new Vector2(width, num2), border: true, flags))
		{
			float num3 = ImGui.GetCursorScreenPos().X;
			float num4 = ImGui.GetContentRegionAvail().X;
			float num5 = ImGui.GetCursorScreenPos().Y;
			float num6 = ((float)config.TaskBarGearsetButtonHeight + 4f) * scale;
			foreach (GearsetPopupJobGroup job in group.Jobs)
			{
				ImGui.SetCursorScreenPos(new Vector2(num3, num5));
				if (DrawGearsetPopupJobHeader(job, num4, scale, opacity) && config.TaskBarGearsetEnableGroupCollapse)
				{
					SetGearsetJobExpanded(job.ClassJobId, !IsGearsetJobExpanded(job.ClassJobId));
				}
				num5 += GearsetPopupJobHeaderScaled(scale);
				if (IsGearsetJobExpanded(job.ClassJobId))
				{
					num5 += GearsetJobRowPadScale * scale;
					float num7 = (float)config.TaskBarGearsetButtonHeight * scale;
					foreach (GearsetPopupEntry entry in job.Entries)
					{
						Vector2 cursorScreenPos = new Vector2(num3, num5);
						float width2 = Math.Min((float)config.TaskBarGearsetButtonWidth * scale, num4);
						if (DrawGearsetPopupJobRow(entry, cursorScreenPos, width2, num7, scale, opacity))
						{
							EquipGearset(entry.Id);
							gearsetPopupCache = null;
							if (config.TaskBarGearsetClosePopupOnSwitch)
							{
								ImGui.CloseCurrentPopup();
							}
						}
						num5 += num6;
					}
				}
				num5 += GearsetPopupJobGapScaled(scale);
			}
		}
		ImGui.EndChild();
		ImGui.PopStyleColor();
	}

	private bool DrawGearsetPopupJobHeader(GearsetPopupJobGroup job, float width, float scale, float opacity)
	{
		IFontHandle fontHandle = GetHudFont(GetNearestHudFontSize(ImGui.GetFontSize() * GearsetPopupMenuScale()));
		IDisposable fontScope = fontHandle.Available ? fontHandle.Push() : EmptyDisposable.Instance;
		using (fontScope)
		{
			return DrawGearsetPopupJobHeaderCore(job, width, scale, opacity);
		}
	}

	private bool DrawGearsetPopupJobHeaderCore(GearsetPopupJobGroup job, float width, float scale, float opacity)
	{
		ThemePalette effectiveTheme = GetEffectiveTheme();
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
		float num = GearsetPopupJobHeaderScaled(scale);
		bool flag = IsGearsetJobExpanded(job.ClassJobId);
		ImU8String strId = new ImU8String(13, 1);
		strId.AppendLiteral("gearset_jobgrp_");
		strId.AppendFormatted(job.ClassJobId);
		ImGui.PushID(strId);
		ImGui.InvisibleButton("##header", new Vector2(width, num));
		bool flag2 = ImGui.IsItemHovered();
		bool flag3 = ImGui.IsItemActive();
		bool result = ImGui.IsItemClicked(ImGuiMouseButton.Left);
		Vector4 value = (flag3 ? WithOpacity(effectiveTheme.HeaderActive, opacity * 0.55f) : (flag2 ? WithOpacity(effectiveTheme.SurfaceAlt, opacity * 0.7f) : WithOpacity(effectiveTheme.SurfaceAlt, opacity * 0.35f)));
		windowDrawList.AddRectFilled(cursorScreenPos, cursorScreenPos + new Vector2(width, num), ImGui.GetColorU32(value), 4f * scale);
		if (flag)
		{
			windowDrawList.AddRectFilled(cursorScreenPos, cursorScreenPos + new Vector2(2f * scale, num), ImGui.GetColorU32(WithOpacity(effectiveTheme.Accent, opacity * 0.9f)));
		}
		float num2 = (num - ImGui.GetTextLineHeight()) * 0.5f;
		string text = (flag ? "▾" : "▸");
		Vector2 textSize = ImGui.CalcTextSize(text);
		windowDrawList.AddText(new Vector2(cursorScreenPos.X + width - textSize.X - 7f * scale, cursorScreenPos.Y + num2), ImGui.GetColorU32(WithOpacity(flag ? effectiveTheme.Accent : effectiveTheme.TextMuted, opacity)), text);
		string text2 = $"{job.Entries.Count} 套";
		Vector2 textSize2 = ImGui.CalcTextSize(text2);
		float rightX = cursorScreenPos.X + width - textSize.X - 8f * scale - textSize2.X - 6f * scale;
		windowDrawList.AddText(new Vector2(rightX, cursorScreenPos.Y + num2), ImGui.GetColorU32(WithOpacity(effectiveTheme.TextMuted, opacity)), text2);
		float iconSize = Math.Clamp(num - 6f * scale, 16f * scale, 46f * scale);
		Vector2 iconMin = cursorScreenPos + new Vector2(7f * scale, (num - iconSize) * 0.5f);
		Vector2 iconMax = iconMin + new Vector2(iconSize);
		if (!DrawGameIconImage(windowDrawList, job.IconId, iconMin, iconMax, fillBounds: true, bypassMissingRetry: true))
		{
			windowDrawList.AddText(iconMin + new Vector2(2f * scale, (iconSize - ImGui.GetTextLineHeight()) * 0.5f), ImGui.GetColorU32(WithOpacity(effectiveTheme.TextMuted, opacity)), (job.JobName.Length > 0) ? job.JobName.Substring(0, 1) : "?");
		}
		float num3 = iconMax.X + 7f * scale;
		string text3 = TruncateTextToWidth(maxWidth: Math.Max(12f * scale, rightX - num3 - 8f * scale), text: job.JobName);
		windowDrawList.AddText(new Vector2(num3, cursorScreenPos.Y + num2), ImGui.GetColorU32(WithOpacity(effectiveTheme.Text, opacity)), text3);
		ImGui.PopID();
		return result;
	}

	private bool DrawGearsetPopupGroupHeader(string label, int count, bool collapsed, float width, float scale, float opacity)
	{
		ThemePalette effectiveTheme = GetEffectiveTheme();
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
		float y = 31f * scale;
		ImU8String strId = new ImU8String(14, 1);
		strId.AppendLiteral("gearset_group_");
		strId.AppendFormatted(label);
		ImGui.PushID(strId);
		ImGui.InvisibleButton("##header", new Vector2(width, y));
		bool flag = ImGui.IsItemHovered();
		bool result = ImGui.IsItemClicked(ImGuiMouseButton.Left);
		windowDrawList.AddRectFilled(col: ImGui.GetColorU32(GetGearsetGroupColor(label, opacity * (flag ? 0.34f : 0.24f))), pMin: cursorScreenPos, pMax: cursorScreenPos + new Vector2(width, y), rounding: 6f * scale);
		windowDrawList.AddRect(cursorScreenPos, cursorScreenPos + new Vector2(width, y), ImGui.GetColorU32(GetGearsetGroupColor(label, opacity * 0.62f)), 6f * scale);
		string text = (collapsed ? "▸" : "▾");
		windowDrawList.AddText(cursorScreenPos + new Vector2(8f * scale, 5f * scale), ImGui.GetColorU32(WithOpacity(effectiveTheme.Text, opacity)), text);
		windowDrawList.AddText(cursorScreenPos + new Vector2(26f * scale, 5f * scale), ImGui.GetColorU32(WithOpacity(effectiveTheme.Text, opacity)), label);
		string text2 = count.ToString();
		windowDrawList.AddText(cursorScreenPos + new Vector2(width - ImGui.CalcTextSize(text2).X - 9f * scale, 5f * scale), ImGui.GetColorU32(WithOpacity(effectiveTheme.TextMuted, opacity)), text2);
		ImGui.PopID();
		return result;
	}

	private void SetGearsetGroupCollapsed(string group, bool collapsed)
	{
		config.TaskBarGearsetCollapsedGroups.RemoveAll((string value) => string.Equals(value, group, StringComparison.Ordinal));
		if (collapsed)
		{
			config.TaskBarGearsetCollapsedGroups.Add(group);
		}
		saveConfig();
	}

	private static Vector4 GetGearsetGroupColor(string group, float alpha)
	{
		if (group == null)
		{
			goto IL_01aa;
		}
		int length = group.Length;
		Vector3 value;
		if (length != 4)
		{
			if (length != 6)
			{
				goto IL_01aa;
			}
			char c = group[2];
			if (c != '物')
			{
				if (c != '魔' || !(group == "远程魔法职业"))
				{
					goto IL_01aa;
				}
				value = new Vector3(0.68f, 0.42f, 0.9f);
			}
			else
			{
				if (!(group == "远程物理职业"))
				{
					goto IL_01aa;
				}
				value = new Vector3(0.84f, 0.52f, 0.3f);
			}
		}
		else
		{
			char c = group[0];
			if ((uint)c <= 29983u)
			{
				if (c != '治')
				{
					if (c != '生' || !(group == "生产职业"))
					{
						goto IL_01aa;
					}
					value = new Vector3(0.72f, 0.56f, 0.34f);
				}
				else
				{
					if (!(group == "治疗职业"))
					{
						goto IL_01aa;
					}
					value = new Vector3(0.38f, 0.78f, 0.48f);
				}
			}
			else if (c != '近')
			{
				if (c != '采')
				{
					if (c != '防' || !(group == "防护职业"))
					{
						goto IL_01aa;
					}
					value = new Vector3(0.34f, 0.52f, 0.95f);
				}
				else
				{
					if (!(group == "采集职业"))
					{
						goto IL_01aa;
					}
					value = new Vector3(0.36f, 0.66f, 0.58f);
				}
			}
			else
			{
				if (!(group == "近战职业"))
				{
					goto IL_01aa;
				}
				value = new Vector3(0.92f, 0.38f, 0.38f);
			}
		}
		goto IL_01bf;
		IL_01aa:
		value = new Vector3(0.62f, 0.52f, 0.58f);
		goto IL_01bf;
		IL_01bf:
		return new Vector4(value, alpha);
	}

	private bool DrawGearsetPopupJobRow(GearsetPopupEntry entry, Vector2 pos, float width, float rowHeight, float scale, float opacity)
	{
		ThemePalette effectiveTheme = GetEffectiveTheme();
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		ImGui.SetCursorScreenPos(pos);
		ImU8String strId = new ImU8String(12, 1);
		strId.AppendLiteral("gearset_job_");
		strId.AppendFormatted(entry.Id);
		ImGui.PushID(strId);
		ImGui.InvisibleButton("##row", new Vector2(width, rowHeight));
		bool flag = ImGui.IsItemHovered();
		bool flag2 = ImGui.IsItemActive();
		bool result = ImGui.IsItemClicked(ImGuiMouseButton.Left);
		Vector4 gearsetGroupColor = GetGearsetGroupColor(entry.Group, opacity * (entry.Selected ? 0.42f : (flag ? 0.25f : 0.08f)));
		if (entry.Selected | flag | flag2)
		{
			windowDrawList.AddRectFilled(col: ImGui.GetColorU32(entry.Selected ? gearsetGroupColor : (flag2 ? GetGearsetGroupColor(entry.Group, opacity * 0.34f) : gearsetGroupColor)), pMin: pos, pMax: pos + new Vector2(width, rowHeight), rounding: 4f * scale);
		}
		float num = Math.Clamp(rowHeight - 6f * scale, 20f * scale, 48f * scale);
		Vector2 vector = pos + new Vector2(3f * scale, (rowHeight - num) * 0.5f);
		Vector2 max = vector + new Vector2(num);
		DrawGameIconImage(windowDrawList, entry.IconId, vector, max, fillBounds: true, bypassMissingRetry: true);
		uint colorU = ImGui.GetColorU32(WithOpacity((entry.JobLevel > 0) ? effectiveTheme.Accent : effectiveTheme.TextMuted, opacity));
		uint colorU2 = ImGui.GetColorU32(WithOpacity(effectiveTheme.Text, opacity));
		float num2 = max.X + 7f * scale;
		float y = pos.Y + Math.Max(2f * scale, (rowHeight - ImGui.GetTextLineHeight()) * 0.5f);
		string text = $"{entry.Id + 1:00}";
		windowDrawList.AddText(new Vector2(num2, y), colorU, text);
		float x = ImGui.CalcTextSize(text).X;
		string text2 = BuildGearsetPopupInfoText(entry);
		Vector2 vector2 = (string.IsNullOrEmpty(text2) ? Vector2.Zero : ImGui.CalcTextSize(text2));
		float num3 = num2 + x + 7f * scale;
		windowDrawList.AddText(text: TruncateTextToWidth(maxWidth: Math.Max(12f * scale, pos.X + width - num3 - vector2.X - (string.IsNullOrEmpty(text2) ? 8f : 18f) * scale), text: entry.JobName), pos: new Vector2(num3, y), col: colorU2);
		if (!string.IsNullOrEmpty(text2))
		{
			windowDrawList.AddText(col: ImGui.GetColorU32(WithOpacity(effectiveTheme.TextMuted, opacity)), pos: new Vector2(pos.X + width - vector2.X - 8f * scale, y), text: text2);
		}
		Vector2 p = new Vector2(num2 + x + 7f * scale, pos.Y + rowHeight - 4f * scale);
		Vector2 p2 = new Vector2(pos.X + width - 8f * scale, p.Y);
		windowDrawList.AddLine(p, p2, ImGui.GetColorU32(GetGearsetGroupColor(entry.Group, opacity * 0.36f)), 2f * scale);
		if (entry.Selected)
		{
			windowDrawList.AddLine(p, new Vector2(p2.X, p.Y), ImGui.GetColorU32(GetGearsetGroupColor(entry.Group, opacity * 0.9f)), 2f * scale);
		}
		ImGui.PopID();
		return result;
	}

	private string BuildGearsetPopupInfoText(GearsetPopupEntry entry)
	{
		string text = ((config.TaskBarGearsetShowLevel && entry.JobLevel > 0) ? $"Lv {entry.JobLevel}" : string.Empty);
		string text2 = ((config.TaskBarGearsetShowItemLevel && entry.ItemLevel > 0) ? $"装等 {entry.ItemLevel}" : string.Empty);
		return string.Join(" · ", new string[2] { text, text2 }.Where((string value) => !string.IsNullOrEmpty(value)));
	}

	private uint GetGearsetIconId(uint classJobId)
	{
		if (classJobId != 0)
		{
			return 62100 + classJobId;
		}
		return 0u;
	}

	private GearsetJobMetadata GetGearsetJobMetadata(uint classJobId)
	{
		if (classJobId == 0)
		{
			return new GearsetJobMetadata(string.Empty, -1, "其他", 7, 100, 0u);
		}
		if (gearsetJobMetadataCache.TryGetValue(classJobId, out var value))
		{
			return value;
		}
		if (!dataManager.GetExcelSheet<ClassJob>().TryGetRow(classJobId, out var row))
		{
			GearsetJobMetadata gearsetJobMetadata = new GearsetJobMetadata(string.Empty, -1, "其他", 7, (int)(100 + classJobId), GetGearsetIconId(classJobId));
			gearsetJobMetadataCache[classJobId] = gearsetJobMetadata;
			return gearsetJobMetadata;
		}
		string text = row.Name.ToString();
		sbyte expArrayIndex = row.ExpArrayIndex;
		string gearsetJobGroup = GetGearsetJobGroup(classJobId);
		GearsetJobMetadata gearsetJobMetadata2 = new GearsetJobMetadata(string.IsNullOrWhiteSpace(text) ? string.Empty : text, expArrayIndex, gearsetJobGroup, GetGearsetGroupSort(gearsetJobGroup), GetGearsetJobSort(classJobId), GetGearsetIconId(classJobId));
		gearsetJobMetadataCache[classJobId] = gearsetJobMetadata2;
		return gearsetJobMetadata2;
	}

	private short GetGearsetJobLevel(uint classJobId)
	{
		return GetGearsetJobLevel(GetGearsetJobMetadata(classJobId));
	}

	private unsafe short GetGearsetJobLevel(GearsetJobMetadata metadata)
	{
		if (metadata.ExpArrayIndex < 0)
		{
			return 0;
		}
		try
		{
			return PlayerState.Instance()->ClassJobLevels[metadata.ExpArrayIndex];
		}
		catch
		{
			return 0;
		}
	}

	private string GetGearsetJobName(uint classJobId, string fallback)
	{
		if (classJobId != 0)
		{
			GearsetJobMetadata gearsetJobMetadata = GetGearsetJobMetadata(classJobId);
			if (!string.IsNullOrWhiteSpace(gearsetJobMetadata.Name))
			{
				return gearsetJobMetadata.Name;
			}
		}
		return fallback;
	}

	private static string GetGearsetJobGroup(uint classJobId)
	{
		switch (classJobId)
		{
		case 19u:
		case 21u:
		case 32u:
		case 37u:
			return "防护职业";
		case 24u:
		case 28u:
		case 33u:
		case 40u:
			return "治疗职业";
		case 20u:
		case 22u:
		case 30u:
		case 34u:
		case 39u:
		case 41u:
			return "近战职业";
		case 23u:
		case 31u:
		case 38u:
			return "远程物理职业";
		case 25u:
		case 27u:
		case 35u:
		case 36u:
		case 42u:
			return "远程魔法职业";
		case 8u:
		case 9u:
		case 10u:
		case 11u:
		case 12u:
		case 13u:
		case 14u:
		case 15u:
			return "生产职业";
		case 16u:
		case 17u:
		case 18u:
			return "采集职业";
		default:
			return "其他";
		}
	}

	private static int GetGearsetGroupSort(string group)
	{
		return group switch
		{
			"防护职业" => 0, 
			"治疗职业" => 1, 
			"近战职业" => 2, 
			"远程物理职业" => 3, 
			"远程魔法职业" => 4, 
			"生产职业" => 5, 
			"采集职业" => 6, 
			_ => 7, 
		};
	}

	private static int GetGearsetJobSort(uint classJobId)
	{
		return classJobId switch
		{
			19u => 0, 
			21u => 1, 
			32u => 2, 
			37u => 3, 
			24u => 10, 
			28u => 11, 
			33u => 12, 
			40u => 13, 
			20u => 20, 
			22u => 21, 
			30u => 22, 
			34u => 23, 
			39u => 24, 
			41u => 25, 
			23u => 30, 
			31u => 31, 
			38u => 32, 
			25u => 40, 
			27u => 41, 
			35u => 42, 
			42u => 43, 
			36u => 44, 
			_ => (int)(100 + classJobId), 
		};
	}

	private void DrawGearsetPopupHeader(string jobName, string setLabel, uint iconId, float scale, float opacity)
	{
		ThemePalette effectiveTheme = GetEffectiveTheme();
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
		float x = ImGui.GetContentRegionAvail().X;
		float y = 56f * scale;
		Vector2 vector = cursorScreenPos + new Vector2(x, y);
		float rounding = 10f * scale;
		windowDrawList.AddRectFilled(cursorScreenPos, vector, ImGui.GetColorU32(WithOpacity(effectiveTheme.SurfaceAlt, opacity)), rounding);
		windowDrawList.AddRect(cursorScreenPos, vector, ImGui.GetColorU32(WithOpacity(effectiveTheme.Border, opacity)), rounding, ImDrawFlags.None, 1f * scale);
		if (ThemeDrawing.IsLiquidGlass(config.ThemeMode))
		{
			ThemeDrawing.DrawLiquidGlassPanel(windowDrawList, cursorScreenPos, vector, rounding, config, opacity * 0.66f, drawShadow: false);
		}
		Vector2 vector2 = cursorScreenPos + new Vector2(10f * scale, 8f * scale);
		Vector2 vector3 = vector2 + new Vector2(40f * scale, 40f * scale);
		windowDrawList.AddRectFilled(vector2, vector3, ImGui.GetColorU32(WithOpacity(effectiveTheme.Surface, opacity)), 9f * scale);
		windowDrawList.AddRect(vector2, vector3, ImGui.GetColorU32(WithOpacity(effectiveTheme.Accent, opacity * 0.8f)), 9f * scale, ImDrawFlags.None, 1f * scale);
		if (!DrawGameIconImage(windowDrawList, iconId, vector2 + new Vector2(4f * scale, 4f * scale), vector3 - new Vector2(4f * scale, 4f * scale), fillBounds: true, bypassMissingRetry: true))
		{
			windowDrawList.AddText(vector2 + new Vector2(10f * scale, 6f * scale), ImGui.GetColorU32(WithOpacity(effectiveTheme.Accent, opacity)), "⚒");
		}
		Vector2 pos = cursorScreenPos + new Vector2(62f * scale, 9f * scale);
		Vector2 pos2 = cursorScreenPos + new Vector2(62f * scale, 29f * scale);
		windowDrawList.AddText(pos, ImGui.GetColorU32(WithOpacity(effectiveTheme.Text, opacity)), jobName);
		windowDrawList.AddText(pos2, ImGui.GetColorU32(WithOpacity(effectiveTheme.TextMuted, opacity)), setLabel);
		ImGui.SetCursorScreenPos(new Vector2(cursorScreenPos.X, vector.Y + 8f * scale));
	}

	private bool DrawGearsetPopupCard(byte id, string name, uint iconId, bool selected, Vector2 size, float scale, float opacity)
	{
		ThemePalette effectiveTheme = GetEffectiveTheme();
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
		Vector2 pMax = cursorScreenPos + size;
		ImU8String strId = new ImU8String(8, 1);
		strId.AppendLiteral("gearset_");
		strId.AppendFormatted(id);
		ImGui.PushID(strId);
		ImGui.InvisibleButton("##card", size);
		bool flag = ImGui.IsItemHovered();
		bool num = ImGui.IsItemActive();
		bool result = ImGui.IsItemClicked(ImGuiMouseButton.Left);
		Vector4 col = (selected ? WithOpacity(effectiveTheme.TaskBarCardActive, opacity * 0.88f) : (flag ? WithOpacity(effectiveTheme.TaskBarCardHovered, opacity * 0.92f) : WithOpacity(effectiveTheme.TaskBarCard, opacity * 0.76f)));
		if (num)
		{
			col = WithOpacity(effectiveTheme.HeaderActive, opacity * 0.92f);
		}
		Vector4 col2 = (selected ? WithOpacity(effectiveTheme.Accent, opacity * 0.96f) : WithOpacity(effectiveTheme.Border, opacity * 0.62f));
		float rounding = 7f * scale;
		windowDrawList.AddRectFilled(cursorScreenPos, pMax, ImGui.GetColorU32(col), rounding);
		windowDrawList.AddRect(cursorScreenPos, pMax, ImGui.GetColorU32(col2), rounding, ImDrawFlags.None, 1f * scale);
		string text = $"{id + 1:00}";
		uint colorU = ImGui.GetColorU32(WithOpacity(selected ? effectiveTheme.Accent : effectiveTheme.TextMuted, opacity));
		uint colorU2 = ImGui.GetColorU32(WithOpacity(selected ? effectiveTheme.Text : effectiveTheme.TextMuted, opacity));
		Vector2 vector = cursorScreenPos + new Vector2(7f * scale, 7f * scale);
		Vector2 max = vector + new Vector2(24f * scale, 24f * scale);
		if (!DrawGameIconImage(windowDrawList, iconId, vector, max, fillBounds: true, bypassMissingRetry: true))
		{
			windowDrawList.AddText(cursorScreenPos + new Vector2(10f * scale, 8f * scale), colorU, text);
		}
		windowDrawList.AddText(cursorScreenPos + new Vector2(38f * scale, 10f * scale), colorU, text);
		windowDrawList.AddText(cursorScreenPos + new Vector2(68f * scale, 10f * scale), colorU2, name);
		ImGui.PopID();
		return result;
	}

	private void DrawCurrencyPopup(float opacity, float scale)
	{
		DrawSimpleTaskBarPopup("AllHud 货币", new Vector2(320f * scale, 0f), opacity, scale, delegate
		{
			ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
			ThemePalette effectiveTheme = GetEffectiveTheme();
			foreach (CurrencyDisplayInfo item in GetCurrencyDisplayOptions().Where(IsCurrencyVisibleInPopup))
			{
				long currencyCount = GetCurrencyCount(item.ItemId);
				long currencyCapacity = GetCurrencyCapacity(item.ItemId);
				string value = ((currencyCount >= 0) ? $"{currencyCount:N0}" : "--");
				Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
				float num = MathF.Round(26f * scale);
				Vector2 vector = new Vector2(Math.Max(180f * scale, ImGui.GetContentRegionAvail().X), num);
				Vector2 pMax = cursorScreenPos + vector;
				bool flag = item.ItemId == config.TaskBarCurrencyItemId;
				ImGui.SetCursorScreenPos(cursorScreenPos);
				ImU8String strId = new ImU8String(20, 1);
				strId.AppendLiteral("##AllHudCurrencyRow_");
				strId.AppendFormatted(item.ItemId);
				ImGui.InvisibleButton(strId, vector);
				bool flag2 = ImGui.IsItemHovered();
				if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
				{
					config.TaskBarCurrencyItemId = item.ItemId;
					saveConfig();
					flag = true;
				}
				Vector4 col = (flag ? effectiveTheme.HeaderActive : (flag2 ? effectiveTheme.HeaderHovered : effectiveTheme.Surface));
				windowDrawList.AddRectFilled(cursorScreenPos, pMax, ImGui.GetColorU32(col), 6f * scale);
				if (flag2)
				{
					windowDrawList.AddRect(cursorScreenPos, pMax, ImGui.GetColorU32(WithOpacity(effectiveTheme.Border, opacity)), 6f * scale, ImDrawFlags.None, Math.Max(1f, scale));
				}
				if (item.IconId != 0)
				{
					DrawGameIconImage(windowDrawList, item.IconId, cursorScreenPos + new Vector2(4f * scale, 3f * scale), cursorScreenPos + new Vector2(24f * scale, 23f * scale), fillBounds: true, bypassMissingRetry: true);
				}
				ImGui.SetCursorScreenPos(cursorScreenPos + new Vector2(30f * scale, Math.Max(0f, (num - ImGui.GetTextLineHeight()) * 0.5f)));
				string value2 = ((currencyCapacity > 0 && config.TaskBarCurrencyShowCap) ? $" / {currencyCapacity:N0}" : string.Empty);
				string value3 = (config.TaskBarCurrencyShowWeeklyCap ? GetCurrencyWeeklyText(item.ItemId) : string.Empty);
				ImGui.PushStyleColor(ImGuiCol.Text, WithOpacity(GetCurrencyValueColor(item.ItemId, currencyCount, currencyCapacity) ?? effectiveTheme.Text, opacity));
				ImU8String text = new ImU8String(1, 4);
				text.AppendFormatted(item.Name);
				text.AppendLiteral("：");
				text.AppendFormatted(value);
				text.AppendFormatted(value2);
				text.AppendFormatted(value3);
				ImGui.TextUnformatted(text);
				ImGui.PopStyleColor();
				ImGui.SetCursorScreenPos(new Vector2(cursorScreenPos.X, pMax.Y + 3f * scale));
			}
		});
	}

	private void DrawSimpleTaskBarPopup(string popupId, Vector2 popupSize, float opacity, float scale, System.Action drawContent)
	{
		if (!ImGui.IsPopupOpen(popupId))
		{
			return;
		}
		if (simplePopupAnchor == default(TaskBarPopupAnchor))
		{
			simplePopupAnchor = new TaskBarPopupAnchor(ImGui.GetMainViewport().WorkPos, ImGui.GetMainViewport().WorkPos);
		}
		Vector2 popupSize2 = ((popupSize.Y > 0f) ? popupSize : new Vector2(popupSize.X, 1f));
		ImGui.SetNextWindowPos(GetTaskBarPopupPosition(simplePopupAnchor, popupSize2, scale, popupSize2.Y), ImGuiCond.Always);
		ImGui.SetNextWindowSize(popupSize, ImGuiCond.Always);
		ThemePalette effectiveTheme = GetEffectiveTheme();
		ImGui.PushStyleColor(ImGuiCol.PopupBg, WithOpacity(effectiveTheme.PopupBg, opacity));
		ImGui.PushStyleColor(ImGuiCol.Text, effectiveTheme.Text);
		ImGui.PushStyleColor(ImGuiCol.Border, WithOpacity(effectiveTheme.Border, opacity));
		ImGui.PushStyleColor(ImGuiCol.Header, effectiveTheme.Header);
		ImGui.PushStyleColor(ImGuiCol.HeaderHovered, effectiveTheme.HeaderHovered);
		ImGui.PushStyleColor(ImGuiCol.HeaderActive, effectiveTheme.HeaderActive);
		ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10f * scale, 9f * scale));
		ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, ThemeDrawing.IsLiquidGlass(config.ThemeMode) ? ThemeDrawing.GetLiquidGlassRounding(config, scale) : (9f * scale));
		ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6f * scale, 5f * scale));
		try
		{
			if (ImGui.BeginPopup(popupId))
			{
				DrawLiquidGlassPopupSurface(opacity, scale);
				drawContent();
				ImGui.EndPopup();
			}
		}
		finally
		{
			ImGui.PopStyleVar(3);
			ImGui.PopStyleColor(6);
		}
	}

	private void DrawLiquidGlassPopupSurface(float opacity, float scale)
	{
		if (ThemeDrawing.IsLiquidGlass(config.ThemeMode))
		{
			ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
			Vector2 windowPos = ImGui.GetWindowPos();
			Vector2 max = windowPos + ImGui.GetWindowSize();
			float liquidGlassRounding = ThemeDrawing.GetLiquidGlassRounding(config, scale);
			ThemeDrawing.PrependLiquidGlassBlur(windowDrawList, windowPos, max, liquidGlassRounding, config, opacity);
			ThemeDrawing.DrawLiquidGlassPanel(windowDrawList, windowPos, max, liquidGlassRounding, config, opacity * 0.92f, drawShadow: false);
		}
	}

	private static Vector4 WithOpacity(Vector4 color, float opacity)
	{
		return new Vector4(color.X, color.Y, color.Z, color.W * Math.Clamp(opacity, 0f, 1f));
	}

	private void DrawVolumeChannelControl(string label, SystemConfigOption volumeOption, SystemConfigOption muteOption, float scale)
	{
		uint volume = GetVolume(volumeOption);
		bool flag = IsVolumeMuted(muteOption);
		ImGui.PushID(label);
		Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
		float num = 30f * scale;
		float textLineHeight = ImGui.GetTextLineHeight();
		float num2 = Math.Max(textLineHeight, 28f * scale);
		float nextItemWidth = 230f * scale;
		float num3 = 0f;
		float num4 = 4f * scale;
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		Vector2 vector = cursorScreenPos;
		Vector2 size = new Vector2(num, num2);
		ImGui.SetCursorScreenPos(vector);
		ImGui.InvisibleButton("##mute", size);
		bool flag2 = ImGui.IsItemHovered();
		bool num5 = ImGui.IsItemActive();
		if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
		{
			ToggleVolumeMute(muteOption);
		}
		if (flag2)
		{
			DrawTaskBarTooltip(flag ? "开启声音" : "静音");
		}
		ThemePalette effectiveTheme = GetEffectiveTheme();
		Vector4 col = (num5 ? effectiveTheme.Accent : (flag2 ? effectiveTheme.Text : effectiveTheme.TextMuted));
		DrawVolumeGlyph(size: MathF.Round(28f * scale), drawList: windowDrawList, center: SnapToPixel(vector + new Vector2(num * 0.5f, num2 * 0.5f)), color: ImGui.GetColorU32(col), scale: scale, volume: volume, muted: flag);
		ImGui.SetCursorScreenPos(new Vector2(cursorScreenPos.X + num + 2f * scale, cursorScreenPos.Y + Math.Max(0f, (num2 - textLineHeight) * 0.5f)));
		ImGui.TextUnformatted(flag ? (label + "：静音") : label);
		if (ImGui.IsItemHovered())
		{
			DrawTaskBarTooltip("Ctrl+点击滑条可手动输入百分比");
		}
		ImGui.SetCursorScreenPos(new Vector2(cursorScreenPos.X, cursorScreenPos.Y + num2 + num3));
		ImGui.SetNextItemWidth(nextItemWidth);
		int v = (int)volume;
		if (ImGui.SliderInt("##volume", ref v, 0, 100, "%d%%"))
		{
			SetVolume(volumeOption, muteOption, (uint)v);
		}
		if (ImGui.IsItemHovered())
		{
			DrawTaskBarTooltip("拖动调整，Ctrl+点击输入百分比");
		}
		ImGui.SetCursorScreenPos(new Vector2(cursorScreenPos.X, ImGui.GetItemRectMax().Y + num4));
		ImGui.PopID();
	}

	private static FontAwesomeIcon GetVolumeIcon(uint volume, bool muted)
	{
		if (muted)
		{
			return FontAwesomeIcon.VolumeMute;
		}
		switch (volume)
		{
		case 0u:
			return FontAwesomeIcon.VolumeOff;
		default:
			return FontAwesomeIcon.VolumeUp;
		case 1u:
		case 2u:
		case 3u:
		case 4u:
		case 5u:
		case 6u:
		case 7u:
		case 8u:
		case 9u:
		case 10u:
		case 11u:
		case 12u:
		case 13u:
		case 14u:
		case 15u:
		case 16u:
		case 17u:
		case 18u:
		case 19u:
		case 20u:
		case 21u:
		case 22u:
		case 23u:
		case 24u:
		case 25u:
		case 26u:
		case 27u:
		case 28u:
		case 29u:
		case 30u:
		case 31u:
		case 32u:
		case 33u:
		case 34u:
		case 35u:
		case 36u:
		case 37u:
		case 38u:
		case 39u:
		case 40u:
		case 41u:
		case 42u:
		case 43u:
		case 44u:
		case 45u:
		case 46u:
		case 47u:
		case 48u:
		case 49u:
			return FontAwesomeIcon.VolumeDown;
		}
	}

	private void DrawPluginListPopup(float opacity, float scale)
	{
		if (pluginListPopupDrawnThisFrame)
		{
			return;
		}
		pluginListPopupDrawnThisFrame = true;
		if (pendingPluginListPopupOpenFrames > 0)
		{
			pendingPluginListPopupOpenFrames--;
			if (pendingPluginListPopupOpenFrames == 0)
			{
				ImGui.OpenPopup("AllHud 插件列表");
			}
		}
		if (pendingPluginListPopupOpenFrames <= 0 && !ImGui.IsPopupOpen("AllHud 插件列表"))
		{
			return;
		}
		Vector2 vector = new Vector2(330f * scale, 420f * scale);
		ImGui.SetNextWindowPos(GetTaskBarPopupPosition(pluginPopupAnchor, vector, scale), ImGuiCond.Always);
		ImGui.SetNextWindowSize(SnapToPixel(vector), ImGuiCond.Always);
		ThemePalette effectiveTheme = GetEffectiveTheme();
		ImGui.PushStyleColor(ImGuiCol.PopupBg, WithOpacity(effectiveTheme.PopupBg, opacity));
		ImGui.PushStyleColor(ImGuiCol.Text, effectiveTheme.Text);
		ImGui.PushStyleColor(ImGuiCol.Border, WithOpacity(effectiveTheme.Border, opacity));
		ImGui.PushStyleColor(ImGuiCol.FrameBg, effectiveTheme.FrameBg);
		ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, effectiveTheme.FrameHovered);
		ImGui.PushStyleColor(ImGuiCol.ChildBg, WithOpacity(effectiveTheme.Surface, opacity));
		ImGui.PushStyleColor(ImGuiCol.Header, effectiveTheme.Header);
		ImGui.PushStyleColor(ImGuiCol.HeaderHovered, effectiveTheme.HeaderHovered);
		ImGui.PushStyleColor(ImGuiCol.HeaderActive, effectiveTheme.HeaderActive);
		ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, effectiveTheme.ScrollbarBg);
		ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, effectiveTheme.ScrollbarGrab);
		ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, effectiveTheme.ScrollbarGrabHovered);
		ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, effectiveTheme.ScrollbarGrabActive);
		ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10f * scale, 9f * scale));
		ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, ThemeDrawing.IsLiquidGlass(config.ThemeMode) ? ThemeDrawing.GetLiquidGlassRounding(config, scale) : (9f * scale));
		ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5f * scale);
		ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6f * scale, 5f * scale));
		try
		{
			if (!ImGui.BeginPopup("AllHud 插件列表"))
			{
				return;
			}
			DrawLiquidGlassPopupSurface(opacity, scale);
			DrawPluginListHeader(scale);
			DrawPluginListSearchRow(scale);
			List<PluginListEntry> list = GetConfiguredPluginListEntries().Where(delegate(PluginListEntry entry)
			{
				if (!string.IsNullOrWhiteSpace(pluginListFilter))
				{
					IExposedPlugin? plugin = entry.Plugin;
					if (plugin == null || !plugin.Name.Contains(pluginListFilter, StringComparison.OrdinalIgnoreCase))
					{
						return entry.InternalName.Contains(pluginListFilter, StringComparison.OrdinalIgnoreCase);
					}
				}
				return true;
			}).ToList();
			float y = Math.Max(220f * scale, ImGui.GetTextLineHeightWithSpacing() * 14f);
			using (PushTaskBarPopupScrollbarStyle(scale))
			{
				if (ImGui.BeginChild("##AllHudPluginListBody", new Vector2(0f, y), border: true))
				{
					if (config.PluginListInternalNames.Count == 0)
					{
						ImGui.TextDisabled("还没有添加插件。");
						ImGui.TextDisabled("在 AllHud 设置里，打开“插件列表”组件的“设置”添加插件。");
					}
					else if (list.Count == 0)
					{
						ImGui.TextDisabled("没有匹配的插件。");
					}
					else
					{
						DrawPluginListSection("已添加", list, scale);
					}
				}
				ImGui.EndChild();
			}
			ImGui.EndPopup();
		}
		finally
		{
			ImGui.PopStyleVar(4);
			ImGui.PopStyleColor(13);
		}
	}

	private void DrawPluginListHeader(float scale)
	{
		Vector2 vector = SnapToPixel(ImGui.GetCursorScreenPos());
		float num = MathF.Round(22f * scale);
		ImGui.SetCursorScreenPos(vector + new Vector2(0f, Math.Max(0f, (num - ImGui.GetTextLineHeight()) * 0.5f)));
		ImGui.TextUnformatted("插件列表");
		ImGui.SetCursorScreenPos(new Vector2(vector.X, vector.Y + num));
	}

	private void DrawPluginListSearchRow(float scale)
	{
		float num = MathF.Round(28f * scale);
		float num2 = 6f * scale;
		float num3 = 4f * scale;
		Vector2 cursorScreenPos = SnapToPixel(ImGui.GetCursorScreenPos());
		float x = ImGui.GetContentRegionAvail().X;
		float nextItemWidth = Math.Max(120f * scale, x - num - num2 - num3);
		ImGui.SetCursorScreenPos(cursorScreenPos);
		ImGui.SetNextItemWidth(nextItemWidth);
		ImGui.InputTextWithHint("##AllHudPluginFilter", "搜索插件...", ref pluginListFilter, 80);
		ImGui.SameLine(0f, num2);
		ThemePalette effectiveTheme = GetEffectiveTheme();
		ImGui.PushStyleColor(ImGuiCol.Button, effectiveTheme.Button);
		ImGui.PushStyleColor(ImGuiCol.ButtonHovered, effectiveTheme.ButtonHovered);
		ImGui.PushStyleColor(ImGuiCol.ButtonActive, effectiveTheme.ButtonActive);
		if (ImGui.Button("##AllHudDalamudSettings", new Vector2(num, num)))
		{
			commandManager.ProcessCommand("/xlsettings");
			ImGui.CloseCurrentPopup();
		}
		DrawSmallGearIcon(ImGui.GetWindowDrawList(), ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), scale, ImGui.IsItemHovered() || ImGui.IsItemActive(), effectiveTheme);
		ImGui.PopStyleColor(3);
		if (ImGui.IsItemHovered())
		{
			DrawTaskBarTooltip("Dalamud 设置");
		}
	}

	private static void DrawSmallGearIcon(ImDrawListPtr drawList, Vector2 min, Vector2 max, float scale, bool active, ThemePalette theme)
	{
		Vector2 vector = SnapToPixel((min + max) * 0.5f);
		float num = MathF.Min(max.X - min.X, max.Y - min.Y) * 0.23f;
		float num2 = num + 1.6f * scale;
		float num3 = num + 4f * scale;
		uint colorU = ImGui.GetColorU32(active ? theme.Accent : theme.Text);
		uint colorU2 = ImGui.GetColorU32(active ? theme.AccentSoft : WithOpacity(theme.Text, 0.16f));
		drawList.AddCircleFilled(vector, num + 2.2f * scale, colorU2, 24);
		for (int i = 0; i < 8; i++)
		{
			float x = (float)Math.PI * 2f * (float)i / 8f;
			Vector2 vector2 = new Vector2(MathF.Cos(x), MathF.Sin(x));
			drawList.AddLine(vector + vector2 * num2, vector + vector2 * num3, colorU, 2f * scale);
		}
		drawList.AddCircle(vector, num, colorU, 24, 2f * scale);
		drawList.AddCircle(vector, MathF.Max(2f * scale, num * 0.38f), colorU, 18, 1.8f * scale);
	}

	private List<PluginListEntry> GetConfiguredPluginListEntries()
	{
		List<PluginListEntry> list = new List<PluginListEntry>(config.PluginListInternalNames.Count);
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string pluginListInternalName in config.PluginListInternalNames)
		{
			string text = pluginListInternalName.Trim();
			if (text.Length != 0 && hashSet.Add(text))
			{
				IExposedPlugin exposedPlugin = FindInstalledPlugin(text);
				if (exposedPlugin != null && exposedPlugin.IsLoaded && (exposedPlugin.HasMainUi || exposedPlugin.HasConfigUi))
				{
					list.Add(new PluginListEntry(text, exposedPlugin));
				}
			}
		}
		return list;
	}

	private void DrawPluginListSection(string label, IEnumerable<PluginListEntry> plugins, float scale)
	{
		List<PluginListEntry> list = plugins.ToList();
		if (list.Count == 0)
		{
			return;
		}
		ImGui.TextDisabled(label);
		foreach (PluginListEntry item in list)
		{
			ThemePalette effectiveTheme = GetEffectiveTheme();
			IExposedPlugin plugin = item.Plugin;
			string text = plugin?.Name ?? item.InternalName;
			bool flag = plugin?.IsLoaded ?? false;
			string text2 = ((plugin == null) ? "（未安装）" : ((!plugin.IsLoaded) ? "（未加载）" : string.Empty));
			float num = MathF.Round(26f * scale);
			Vector2 vector = SnapToPixel(ImGui.GetCursorScreenPos());
			float num2 = Math.Max(120f * scale, ImGui.GetContentRegionAvail().X);
			Vector2 vector2 = vector;
			Vector2 vector3 = SnapToPixel(vector + new Vector2(num2, num));
			Vector2 pMin = SnapToPixel(vector + new Vector2(2f * scale, 2f * scale));
			Vector2 pMax = SnapToPixel(vector + new Vector2(num2 - 2f * scale, num - 2f * scale));
			Vector2 mousePos = ImGui.GetMousePos();
			int num3;
			int num4;
			if (ImGui.IsWindowHovered() && mousePos.X >= vector2.X && mousePos.X < vector3.X && mousePos.Y >= vector2.Y)
			{
				num3 = ((mousePos.Y < vector3.Y) ? 1 : 0);
				if (num3 != 0)
				{
					num4 = (ImGui.IsMouseDown(ImGuiMouseButton.Left) ? 1 : 0);
					goto IL_017e;
				}
			}
			else
			{
				num3 = 0;
			}
			num4 = 0;
			goto IL_017e;
			IL_017e:
			bool flag2 = (byte)num4 != 0;
			if (((uint)num3 | (flag2 ? 1u : 0u)) != 0)
			{
				Vector4 col = (flag2 ? effectiveTheme.HeaderActive : effectiveTheme.HeaderHovered);
				Vector4 col2 = (flag2 ? effectiveTheme.Accent : effectiveTheme.Border);
				ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
				windowDrawList.AddRectFilled(pMin, pMax, ImGui.GetColorU32(col), 5f * scale);
				windowDrawList.AddRect(pMin, pMax, ImGui.GetColorU32(col2), 5f * scale, ImDrawFlags.None, 1f * scale);
			}
			if (num3 != 0 && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
			{
				OpenPluginListEntry(plugin);
				ImGui.CloseCurrentPopup();
			}
			if (num3 != 0 && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
			{
				commandManager.ProcessCommand("/xlplugins");
				ImGui.CloseCurrentPopup();
			}
			if (plugin != null)
			{
				DrawPluginListIconImage(plugin, vector, num, scale);
			}
			else
			{
				DrawPluginListIconFallback(ImGui.GetWindowDrawList(), text, isLoaded: false, vector, num, scale);
			}
			Vector2 pos = SnapToPixel(new Vector2(vector.X + 32f * scale, vector.Y + Math.Max(0f, (num - ImGui.GetTextLineHeight()) * 0.5f)));
			ImGui.GetWindowDrawList().AddText(pos, ImGui.GetColorU32(flag ? effectiveTheme.Text : effectiveTheme.TextMuted), text + text2);
			ImGui.SetCursorScreenPos(new Vector2(vector.X, vector.Y + num));
		}
	}

	private void OpenPluginListEntry(IExposedPlugin? plugin)
	{
		if (plugin == null)
		{
			commandManager.ProcessCommand("/xlplugins");
			return;
		}
		ImGuiIOPtr iO = ImGui.GetIO();
		if ((iO.KeyShift || iO.KeyCtrl) && plugin.HasConfigUi)
		{
			plugin.OpenConfigUi();
		}
		else
		{
			OpenPluginShortcut(plugin);
		}
	}

	private void DrawPluginListIcon(IExposedPlugin plugin, float scale)
	{
		float num = MathF.Round(26f * scale);
		DrawPluginListIconImage(plugin, ImGui.GetCursorScreenPos(), num, scale);
		ImGui.Dummy(new Vector2(MathF.Round(24f * scale), num));
	}

	private void DrawPluginListIconImage(IExposedPlugin plugin, Vector2 rowStart, float rowHeight, float scale, float? overrideSize = null)
	{
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		float num = overrideSize ?? MathF.Round(24f * scale);
		float x = (overrideSize.HasValue ? Math.Max(0f, (rowHeight - num) * 0.5f) : (2f * scale));
		Vector2 vector = SnapToPixel(rowStart + new Vector2(x, Math.Max(0f, (rowHeight - num) * 0.5f)));
		Vector2 vector2 = vector + new Vector2(num, num);
		if (TryGetPluginIconTexture(plugin, out IDalamudTextureWrap texture) && texture != null && TryGetPluginIconTextureHandle(plugin, texture, out var handle))
		{
			windowDrawList.AddImage(handle, vector, vector2, new Vector2(0.035f), new Vector2(0.965f), uint.MaxValue);
		}
		else
		{
			DrawPluginIconFallback(windowDrawList, plugin.Name, plugin.IsLoaded, vector, vector2, scale);
		}
	}

	private void DrawPluginListIconFallback(ImDrawListPtr drawList, string name, bool isLoaded, Vector2 rowStart, float rowHeight, float scale)
	{
		float num = MathF.Round(24f * scale);
		Vector2 vector = SnapToPixel(rowStart + new Vector2(2f * scale, Math.Max(0f, (rowHeight - num) * 0.5f)));
		DrawPluginIconFallback(drawList, name, isLoaded, vector, vector + new Vector2(num, num), scale);
	}

	private void DrawPluginIconFallback(ImDrawListPtr drawList, string name, bool isLoaded, Vector2 iconMin, Vector2 iconMax, float scale)
	{
		ThemePalette effectiveTheme = GetEffectiveTheme();
		Vector4 accent = effectiveTheme.Accent;
		Vector4 surface = effectiveTheme.Surface;
		Vector4 text = effectiveTheme.Text;
		Vector4 textMuted = effectiveTheme.TextMuted;
		float num = iconMax.X - iconMin.X;
		Vector2 vector = iconMin + new Vector2(num * 0.5f);
		Vector4 col = (isLoaded ? accent : surface);
		Vector4 col2 = (isLoaded ? accent : textMuted);
		drawList.AddRectFilled(iconMin, iconMax, ImGui.GetColorU32(col), 4f * scale);
		drawList.AddRect(iconMin, iconMax, ImGui.GetColorU32(col2), 4f * scale, ImDrawFlags.None, 1f * scale);
		string text2 = (string.IsNullOrWhiteSpace(name) ? "?" : name.Substring(0, 1).ToUpperInvariant());
		Vector2 vector2 = ImGui.CalcTextSize(text2);
		uint col3 = (isLoaded ? ImGui.GetColorU32(text) : ImGui.GetColorU32(textMuted));
		drawList.AddText(SnapToPixel(vector - vector2 * 0.5f), col3, text2);
	}

	private bool TryGetPluginIconTextureHandle(IExposedPlugin plugin, IDalamudTextureWrap texture, out ImTextureID handle)
	{
		try
		{
			handle = texture.Handle;
			return true;
		}
		catch (ObjectDisposedException)
		{
			ClearPluginIconTextureCache(plugin);
		}
		catch
		{
		}
		handle = default(ImTextureID);
		return false;
	}

	private void ClearPluginIconTextureCache(IExposedPlugin plugin)
	{
		if (!string.IsNullOrWhiteSpace(plugin.Manifest.IconUrl))
		{
			pluginRemoteIconCache.TryRemove(plugin.Manifest.IconUrl, out IDalamudTextureWrap _);
			pluginRemoteIconTasks.TryRemove(plugin.Manifest.IconUrl, out Task<IDalamudTextureWrap> _);
			string remotePluginIconCachePath = GetRemotePluginIconCachePath(plugin.Manifest.IconUrl);
			pluginIconTextureCache.Remove(remotePluginIconCachePath);
			pluginIconTextureRetryAt.Remove(remotePluginIconCachePath);
			remotePluginIconRetryAt.Remove(plugin.Manifest.IconUrl);
		}
		string localPluginIconPath = GetLocalPluginIconPath(plugin);
		if (!string.IsNullOrWhiteSpace(localPluginIconPath))
		{
			pluginIconTextureCache.Remove(localPluginIconPath);
			pluginIconTextureRetryAt.Remove(localPluginIconPath);
		}
	}

	private bool TryGetPluginIconTexture(IExposedPlugin plugin, out IDalamudTextureWrap? texture)
	{
		texture = null;
		string localPluginIconPath = GetLocalPluginIconPath(plugin);
		if (!string.IsNullOrWhiteSpace(localPluginIconPath))
		{
			if (pluginIconTextureRetryAt.TryGetValue(localPluginIconPath, out var value) && DateTime.UtcNow < value)
			{
				return TryGetRemotePluginIconTexture(plugin, out texture, config.TaskBarDownloadPluginIcons);
			}
			if (!pluginIconTextureCache.TryGetValue(localPluginIconPath, out ISharedImmediateTexture value2))
			{
				try
				{
					value2 = textureProvider.GetFromFile(localPluginIconPath);
					pluginIconTextureCache[localPluginIconPath] = value2;
					pluginIconTextureRetryAt.Remove(localPluginIconPath);
				}
				catch
				{
					pluginIconTextureRetryAt[localPluginIconPath] = DateTime.UtcNow + PluginIconTextureRetryDelay;
					return TryGetRemotePluginIconTexture(plugin, out texture, config.TaskBarDownloadPluginIcons);
				}
			}
			try
			{
				if (value2.TryGetWrap(out texture, out Exception _) && texture != null)
				{
					pluginIconTextureRetryAt.Remove(localPluginIconPath);
					return true;
				}
			}
			catch (ObjectDisposedException)
			{
				pluginIconTextureCache.Remove(localPluginIconPath);
			}
			catch
			{
			}
			pluginIconTextureRetryAt[localPluginIconPath] = DateTime.UtcNow + PluginIconTextureRetryDelay;
		}
		if (TryGetRemotePluginIconTexture(plugin, out texture, config.TaskBarDownloadPluginIcons))
		{
			return true;
		}
		return false;
	}

	private bool TryGetRemotePluginIconTexture(IExposedPlugin plugin, out IDalamudTextureWrap? texture, bool allowDownload)
	{
		texture = null;
		string iconUrl = plugin.Manifest.IconUrl;
		if (string.IsNullOrWhiteSpace(iconUrl))
		{
			return false;
		}
		if (pluginRemoteIconCache.TryGetValue(iconUrl, out IDalamudTextureWrap value))
		{
			texture = value;
			return texture != null;
		}
		if (TryGetCachedRemotePluginIconTexture(iconUrl, out texture))
		{
			return true;
		}
		if (!allowDownload)
		{
			return false;
		}
		if (remotePluginIconRetryAt.TryGetValue(iconUrl, out var value2) && DateTime.UtcNow < value2)
		{
			return false;
		}
		remotePluginIconRetryAt[iconUrl] = DateTime.UtcNow + RemotePluginIconRetryDelay;
		pluginRemoteIconTasks.GetOrAdd(iconUrl, LoadRemotePluginIconAsync);
		return false;
	}

	private bool TryGetCachedRemotePluginIconTexture(string iconUrl, out IDalamudTextureWrap? texture)
	{
		texture = null;
		DateTime utcNow = DateTime.UtcNow;
		if (missingCachedRemotePluginIconRetryAt.TryGetValue(iconUrl, out var value) && utcNow < value)
		{
			return false;
		}
		string remotePluginIconCachePath = GetRemotePluginIconCachePath(iconUrl);
		if (!File.Exists(remotePluginIconCachePath))
		{
			missingCachedRemotePluginIconRetryAt[iconUrl] = utcNow + RemotePluginIconRetryDelay;
			return false;
		}
		missingCachedRemotePluginIconRetryAt.Remove(iconUrl);
		try
		{
			if (!pluginIconTextureCache.TryGetValue(remotePluginIconCachePath, out ISharedImmediateTexture value2))
			{
				value2 = textureProvider.GetFromFile(remotePluginIconCachePath);
				pluginIconTextureCache[remotePluginIconCachePath] = value2;
			}
			if (value2.TryGetWrap(out texture, out Exception _) && texture != null)
			{
				pluginRemoteIconCache[iconUrl] = texture;
				remotePluginIconRetryAt.Remove(iconUrl);
				return true;
			}
		}
		catch
		{
			pluginIconTextureCache.Remove(remotePluginIconCachePath);
			pluginIconTextureRetryAt[remotePluginIconCachePath] = utcNow + PluginIconTextureRetryDelay;
		}
		return false;
	}

	private async Task<IDalamudTextureWrap?> LoadRemotePluginIconAsync(string iconUrl)
	{
		_ = 1;
		try
		{
			byte[] array = await httpClient.GetByteArrayAsync(iconUrl).ConfigureAwait(continueOnCapturedContext: false);
			TryWriteRemotePluginIconCache(iconUrl, array);
			IDalamudTextureWrap dalamudTextureWrap = await textureProvider.CreateFromImageAsync(array, "AllHud plugin icon " + iconUrl).ConfigureAwait(continueOnCapturedContext: false);
			pluginRemoteIconCache[iconUrl] = dalamudTextureWrap;
			remotePluginIconRetryAt.Remove(iconUrl);
			return dalamudTextureWrap;
		}
		catch
		{
			remotePluginIconRetryAt[iconUrl] = DateTime.UtcNow + RemotePluginIconRetryDelay;
			return null;
		}
		finally
		{
			pluginRemoteIconTasks.TryRemove(iconUrl, out Task<IDalamudTextureWrap> _);
		}
	}

	private void TryWriteRemotePluginIconCache(string iconUrl, byte[] bytes)
	{
		try
		{
			Directory.CreateDirectory(pluginIconCacheDirectory);
			File.WriteAllBytes(GetRemotePluginIconCachePath(iconUrl), bytes);
			missingCachedRemotePluginIconRetryAt.Remove(iconUrl);
		}
		catch
		{
		}
	}

	private string GetRemotePluginIconCachePath(string iconUrl)
	{
		if (remotePluginIconCachePathCache.TryGetValue(iconUrl, out string value))
		{
			return value;
		}
		string text = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(iconUrl))).ToLowerInvariant();
		string text2 = Path.Combine(pluginIconCacheDirectory, text + GetRemotePluginIconCacheExtension(iconUrl));
		remotePluginIconCachePathCache[iconUrl] = text2;
		return text2;
	}

	private static string GetRemotePluginIconCacheExtension(string iconUrl)
	{
		try
		{
			string extension = Path.GetExtension(new Uri(iconUrl).AbsolutePath);
			string result;
			switch (extension.ToLowerInvariant())
			{
			case ".png":
			case ".jpg":
			case ".jpeg":
			case ".webp":
				result = extension.ToLowerInvariant();
				break;
			default:
				result = ".png";
				break;
			}
			return result;
		}
		catch
		{
			return ".png";
		}
	}

	private string? GetLocalPluginIconPath(IExposedPlugin plugin)
	{
		DateTime utcNow = DateTime.UtcNow;
		if (pluginIconPathCache.TryGetValue(plugin.InternalName, out var value) && utcNow < value.RetryAt)
		{
			return value.Path;
		}
		string text = FindLocalPluginIconPath(plugin);
		pluginIconPathCache[plugin.InternalName] = new PluginIconPathCacheEntry(string.IsNullOrWhiteSpace(text) ? null : text, string.IsNullOrWhiteSpace(text) ? (utcNow + PluginIconPathMissRetryDelay) : DateTime.MaxValue);
		return text;
	}

	private string? FindLocalPluginIconPath(IExposedPlugin plugin)
	{
		try
		{
			if (plugin.InternalName.Equals("AllHud", StringComparison.OrdinalIgnoreCase))
			{
				string text = FindPluginIconInDirectory(Path.GetDirectoryName(pluginInterface.AssemblyLocation.FullName) ?? string.Empty, plugin);
				if (!string.IsNullOrWhiteSpace(text))
				{
					return text;
				}
			}
			string text2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XIVLauncherCN", "installedPlugins", plugin.InternalName);
			if (!Directory.Exists(text2))
			{
				return null;
			}
			string text3 = FindDirectPluginIconInDirectory(text2);
			if (!string.IsNullOrWhiteSpace(text3))
			{
				return text3;
			}
			string text4 = (from directory in Directory.EnumerateDirectories(text2)
				select (!Version.TryParse(Path.GetFileName(directory), out Version result)) ? (Path: directory, Version: null) : (Path: directory, Version: result) into entry
				where (object)entry.Version != null
				orderby entry.Version descending
				select entry.Path).FirstOrDefault();
			if (string.IsNullOrWhiteSpace(text4) || !Directory.Exists(text4))
			{
				return null;
			}
			return FindPluginIconInDirectory(text4, plugin);
		}
		catch
		{
			return null;
		}
	}

	private static string? FindPluginIconInDirectory(string directory, IExposedPlugin plugin)
	{
		if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
		{
			return null;
		}
		string text = FindDirectPluginIconInDirectory(directory);
		if (!string.IsNullOrWhiteSpace(text))
		{
			return text;
		}
		return (from path in Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories).Where(IsSupportedPluginIconFile)
			select new
			{
				Path = path,
				Score = GetPluginIconCandidateScore(path, plugin)
			} into candidate
			where candidate.Score > 0
			orderby candidate.Score descending, candidate.Path.Length
			select candidate.Path).FirstOrDefault();
	}

	private static string? FindDirectPluginIconInDirectory(string directory)
	{
		if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
		{
			return null;
		}
		string[] array = new string[4] { "icon.png", "icon.jpg", "icon.jpeg", "icon.webp" };
		foreach (string text in array)
		{
			string text2 = Path.Combine(directory, text);
			if (File.Exists(text2))
			{
				return text2;
			}
			string text3 = Path.Combine(directory, "images", text);
			if (File.Exists(text3))
			{
				return text3;
			}
			string text4 = Path.Combine(directory, "Assets", text);
			if (File.Exists(text4))
			{
				return text4;
			}
		}
		return null;
	}

	private static bool IsSupportedPluginIconFile(string path)
	{
		string extension = Path.GetExtension(path);
		if (!extension.Equals(".png", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
		{
			return extension.Equals(".webp", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private static int GetPluginIconCandidateScore(string path, IExposedPlugin plugin)
	{
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(path);
		string fileName = Path.GetFileName(Path.GetDirectoryName(path) ?? string.Empty);
		string text = path.Replace('\\', '/');
		int num = 0;
		if (fileNameWithoutExtension.Equals("icon", StringComparison.OrdinalIgnoreCase))
		{
			num += 100;
		}
		if (fileNameWithoutExtension.Contains("icon", StringComparison.OrdinalIgnoreCase))
		{
			num += 80;
		}
		if (fileNameWithoutExtension.Contains("logo", StringComparison.OrdinalIgnoreCase))
		{
			num += 65;
		}
		if (fileNameWithoutExtension.Contains("shortcut", StringComparison.OrdinalIgnoreCase) || fileNameWithoutExtension.Contains("title", StringComparison.OrdinalIgnoreCase))
		{
			num += 45;
		}
		if (fileNameWithoutExtension.Contains(plugin.InternalName, StringComparison.OrdinalIgnoreCase) || fileNameWithoutExtension.Contains(plugin.Name, StringComparison.OrdinalIgnoreCase))
		{
			num += 70;
		}
		if (fileName.Equals("Assets", StringComparison.OrdinalIgnoreCase) || fileName.Equals("Images", StringComparison.OrdinalIgnoreCase) || fileName.Equals("Resources", StringComparison.OrdinalIgnoreCase))
		{
			num += 12;
		}
		if (text.Contains("/Lang", StringComparison.OrdinalIgnoreCase) || text.Contains("/Localization", StringComparison.OrdinalIgnoreCase))
		{
			num -= 40;
		}
		return num;
	}

	private void DrawSelfCooldownBarWindow(ImGuiWindowFlags flags)
	{
		if (!config.ShowSelfCooldownBar)
		{
			return;
		}
		IReadOnlyList<PartyCooldownGroupEntry> readOnlyList = (config.ShowSelfCooldownBarPreview ? combatState.GetPartyCooldownTrackingPreview(config) : combatState.GetPartyCooldownTracking(config));
		if (readOnlyList.Count == 0)
		{
			return;
		}
		if (config.SelfCooldownBarLocked)
		{
			flags |= ImGuiWindowFlags.NoMove;
		}
		float num = Math.Clamp(config.SelfCooldownBarScale, 0.6f, 2f);
		float num2 = Math.Clamp(config.SelfCooldownBarOpacity, 0.15f, 1f);
		float iconSize = Math.Clamp(36f * num, 12f, 64f);
		float spacing = MathF.Round(5f * num);
		float num3 = MathF.Round(5f * num);
		float headerGap = MathF.Round(7f * num);
		float num4 = MathF.Round(7f * num);
		bool flag = Math.Clamp(config.SelfCooldownBarLayoutDirection, 0, 1) == 1;
		SelfCooldownLayoutCache selfCooldownLayoutCache = GetSelfCooldownLayoutCache(readOnlyList, flag, iconSize, spacing, num3, num4);
		IReadOnlyList<SelfCooldownVisibleGroup> visibleGroups = selfCooldownLayoutCache.VisibleGroups;
		IReadOnlyList<SelfCooldownHorizontalRow> horizontalRows = selfCooldownLayoutCache.HorizontalRows;
		if (visibleGroups.Count == 0)
		{
			return;
		}
		float num5 = ((!flag) ? visibleGroups.Max((SelfCooldownVisibleGroup entry) => iconSize + headerGap + (float)entry.Cooldowns.Count * iconSize + (float)Math.Max(0, entry.Cooldowns.Count - 1) * spacing) : ((horizontalRows.Count > 0) ? horizontalRows.Max((SelfCooldownHorizontalRow row) => row.Width) : 0f));
		float num6 = (flag ? ((float)horizontalRows.Count * (iconSize * 2f + headerGap) + (float)Math.Max(0, horizontalRows.Count - 1) * num3) : ((float)visibleGroups.Count * iconSize + (float)Math.Max(0, visibleGroups.Count - 1) * num3));
		Vector2 vector = SnapToPixel(new Vector2(num5 + num4 * 2f, num6 + num4 * 2f));
		Vector2 clampedStatusOverlayPosition = GetClampedStatusOverlayPosition(config.SelfCooldownBarPosition, vector);
		ImGui.SetNextWindowPos(clampedStatusOverlayPosition, (clampedStatusOverlayPosition != config.SelfCooldownBarPosition) ? ImGuiCond.Always : ImGuiCond.FirstUseEver);
		ImGui.SetNextWindowSize(vector);
		flags |= ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground;
		ImGui.PushStyleVar(ImGuiStyleVar.Alpha, num2);
		if (!ImGui.Begin("AllHud 队伍冷却", flags))
		{
			ImGui.End();
			ImGui.PopStyleVar();
			return;
		}
		Vector2 windowPos = ImGui.GetWindowPos();
		Vector2 clampedStatusOverlayPosition2 = GetClampedStatusOverlayPosition(windowPos, vector);
		if (clampedStatusOverlayPosition2 != windowPos)
		{
			ImGui.SetWindowPos(clampedStatusOverlayPosition2, ImGuiCond.Always);
		}
		TrackSelfCooldownBarPosition();
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		Vector2 windowPos2 = ImGui.GetWindowPos();
		DrawSelfCooldownBarCard(windowDrawList, windowPos2, windowPos2 + vector, num, num2);
		if (flag)
		{
			DrawSelfCooldownBarHorizontalLayout(windowDrawList, horizontalRows, num4, iconSize, spacing, num3, headerGap);
		}
		else
		{
			DrawSelfCooldownBarVerticalLayout(windowDrawList, visibleGroups, num4, iconSize, spacing, num3, headerGap);
		}
		ImGui.End();
		ImGui.PopStyleVar();
	}

	private SelfCooldownLayoutCache GetSelfCooldownLayoutCache(IReadOnlyList<PartyCooldownGroupEntry> groups, bool horizontalLayout, float iconSize, float spacing, float rowGap, float pad)
	{
		float x = ImGui.GetMainViewport().WorkSize.X;
		SelfCooldownLayoutCacheKey selfCooldownLayoutCacheKey = new SelfCooldownLayoutCacheKey(groups, config.SelfCooldownBarHideWhenReady, config.ShowSelfCooldownBarPreview, horizontalLayout ? 1 : 0, iconSize, spacing, rowGap, pad, MathF.Round(x));
		if (cachedSelfCooldownLayoutKey == selfCooldownLayoutCacheKey && (object)cachedSelfCooldownLayout != null)
		{
			return cachedSelfCooldownLayout;
		}
		List<SelfCooldownVisibleGroup> list = new List<SelfCooldownVisibleGroup>(groups.Count);
		foreach (PartyCooldownGroupEntry group in groups)
		{
			if (config.SelfCooldownBarHideWhenReady)
			{
				List<CooldownEntry> list2 = null;
				foreach (CooldownEntry cooldown in group.Cooldowns)
				{
					if (!cooldown.IsReady)
					{
						if (list2 == null)
						{
							list2 = new List<CooldownEntry>(group.Cooldowns.Count);
						}
						list2.Add(cooldown);
					}
				}
				if (list2 != null && list2.Count > 0)
				{
					list.Add(new SelfCooldownVisibleGroup(group, list2));
				}
			}
			else if (group.Cooldowns.Count > 0)
			{
				list.Add(new SelfCooldownVisibleGroup(group, group.Cooldowns));
			}
		}
		if (!config.ShowSelfCooldownBarPreview)
		{
			SortSelfCooldownGroupsByNativePartyList(list);
		}
		IReadOnlyList<SelfCooldownHorizontalRow> readOnlyList2;
		if (!horizontalLayout)
		{
			IReadOnlyList<SelfCooldownHorizontalRow> readOnlyList = Array.Empty<SelfCooldownHorizontalRow>();
			readOnlyList2 = readOnlyList;
		}
		else
		{
			readOnlyList2 = BuildSelfCooldownHorizontalRows(list, iconSize, spacing, rowGap, pad);
		}
		IReadOnlyList<SelfCooldownHorizontalRow> horizontalRows = readOnlyList2;
		cachedSelfCooldownLayoutKey = selfCooldownLayoutCacheKey;
		cachedSelfCooldownLayout = new SelfCooldownLayoutCache(list, horizontalRows);
		return cachedSelfCooldownLayout;
	}

	private static float GetSelfCooldownHorizontalGroupWidth(SelfCooldownVisibleGroup group, float iconSize, float spacing)
	{
		return Math.Max(iconSize, (float)group.Cooldowns.Count * iconSize + (float)Math.Max(0, group.Cooldowns.Count - 1) * spacing);
	}

	private static IReadOnlyList<SelfCooldownHorizontalRow> BuildSelfCooldownHorizontalRows(IReadOnlyList<SelfCooldownVisibleGroup> visibleGroups, float iconSize, float spacing, float groupGap, float pad)
	{
		float num = Math.Max(iconSize, ImGui.GetMainViewport().WorkSize.X - pad * 2f - 32f);
		List<SelfCooldownHorizontalRow> list = new List<SelfCooldownHorizontalRow>();
		List<SelfCooldownVisibleGroup> list2 = new List<SelfCooldownVisibleGroup>();
		float num2 = 0f;
		foreach (SelfCooldownVisibleGroup visibleGroup in visibleGroups)
		{
			float selfCooldownHorizontalGroupWidth = GetSelfCooldownHorizontalGroupWidth(visibleGroup, iconSize, spacing);
			float num3 = ((list2.Count == 0) ? selfCooldownHorizontalGroupWidth : (num2 + groupGap + selfCooldownHorizontalGroupWidth));
			if (list2.Count > 0 && num3 > num)
			{
				list.Add(new SelfCooldownHorizontalRow(list2, num2));
				list2 = new List<SelfCooldownVisibleGroup>();
				num2 = 0f;
				num3 = selfCooldownHorizontalGroupWidth;
			}
			list2.Add(visibleGroup);
			num2 = num3;
		}
		if (list2.Count > 0)
		{
			list.Add(new SelfCooldownHorizontalRow(list2, num2));
		}
		return list;
	}

	private void DrawSelfCooldownBarVerticalLayout(ImDrawListPtr drawList, IReadOnlyList<SelfCooldownVisibleGroup> visibleGroups, float pad, float iconSize, float spacing, float rowGap, float headerGap)
	{
		for (int i = 0; i < visibleGroups.Count; i++)
		{
			SelfCooldownVisibleGroup selfCooldownVisibleGroup = visibleGroups[i];
			float y = pad + (float)i * (iconSize + rowGap);
			ImGui.SetCursorPos(new Vector2(pad, y));
			DrawSelfCooldownBarJobHeader(drawList, ImGui.GetCursorScreenPos(), iconSize, selfCooldownVisibleGroup.Entry.SourceJobIconId);
			float num = pad + iconSize + headerGap;
			for (int j = 0; j < selfCooldownVisibleGroup.Cooldowns.Count; j++)
			{
				ImGui.SetCursorPos(new Vector2(num + (float)j * (iconSize + spacing), y));
				DrawPartyCooldownBarIcon(selfCooldownVisibleGroup.Entry, selfCooldownVisibleGroup.Cooldowns[j], iconSize, j);
			}
		}
	}

	private void DrawSelfCooldownBarHorizontalLayout(ImDrawListPtr drawList, IReadOnlyList<SelfCooldownHorizontalRow> rows, float pad, float iconSize, float spacing, float groupGap, float headerGap)
	{
		float num = iconSize * 2f + headerGap + groupGap;
		for (int i = 0; i < rows.Count; i++)
		{
			SelfCooldownHorizontalRow selfCooldownHorizontalRow = rows[i];
			float num2 = pad;
			float num3 = pad + (float)i * num;
			float y = num3 + iconSize + headerGap;
			foreach (SelfCooldownVisibleGroup group in selfCooldownHorizontalRow.Groups)
			{
				float selfCooldownHorizontalGroupWidth = GetSelfCooldownHorizontalGroupWidth(group, iconSize, spacing);
				ImGui.SetCursorPos(new Vector2(num2 + Math.Max(0f, (selfCooldownHorizontalGroupWidth - iconSize) * 0.5f), num3));
				DrawSelfCooldownBarJobHeader(drawList, ImGui.GetCursorScreenPos(), iconSize, group.Entry.SourceJobIconId);
				for (int j = 0; j < group.Cooldowns.Count; j++)
				{
					ImGui.SetCursorPos(new Vector2(num2 + (float)j * (iconSize + spacing), y));
					DrawPartyCooldownBarIcon(group.Entry, group.Cooldowns[j], iconSize, j);
				}
				num2 += selfCooldownHorizontalGroupWidth + groupGap;
			}
		}
	}

	private void SortSelfCooldownGroupsByNativePartyList(List<SelfCooldownVisibleGroup> groups)
	{
		IReadOnlyList<uint> nativePartyMemberOrder = GetNativePartyMemberOrder();
		if (nativePartyMemberOrder.Count != 0)
		{
			Dictionary<uint, int> indexByEntityId = new Dictionary<uint, int>(nativePartyMemberOrder.Count);
			for (int i = 0; i < nativePartyMemberOrder.Count; i++)
			{
				indexByEntityId[nativePartyMemberOrder[i]] = i;
			}
			groups.Sort(delegate(SelfCooldownVisibleGroup left, SelfCooldownVisibleGroup right)
			{
				int num = GetNativePartyMemberOrderIndex(left.Entry, indexByEntityId).CompareTo(GetNativePartyMemberOrderIndex(right.Entry, indexByEntityId));
				return (num == 0) ? left.Entry.PartySlot.CompareTo(right.Entry.PartySlot) : num;
			});
		}
	}

	private static int GetNativePartyMemberOrderIndex(PartyCooldownGroupEntry entry, IReadOnlyDictionary<uint, int> indexByEntityId)
	{
		if (entry.SourceEntityId != 0 && indexByEntityId.TryGetValue(entry.SourceEntityId, out var value))
		{
			return value;
		}
		if (entry.SourceObjectId <= uint.MaxValue && indexByEntityId.TryGetValue((uint)entry.SourceObjectId, out var value2))
		{
			return value2;
		}
		return int.MaxValue;
	}

	private unsafe IReadOnlyList<uint> GetNativePartyMemberOrder()
	{
		AtkUnitBasePtr addonByName = gameGui.GetAddonByName("_PartyList");
		if (addonByName.IsNull)
		{
			return Array.Empty<uint>();
		}
		AddonPartyList* address = (AddonPartyList*)addonByName.Address;
		if (!IsNativePartyListVisible(address))
		{
			return Array.Empty<uint>();
		}
		int num = Math.Clamp(address->MemberCount, 0, 8);
		if (num <= 0)
		{
			return Array.Empty<uint>();
		}
		PartyListNumberArray* ptr = PartyListNumberArray.Instance();
		if (ptr == null)
		{
			return Array.Empty<uint>();
		}
		List<uint> list = new List<uint>(num);
		for (int i = 0; i < num; i++)
		{
			ref PartyListNumberArray.PartyListMemberNumberArray reference = ref ptr->PartyMembers[i];
			uint num2 = (uint)reference.Data[40];
			if (reference.MaxHealth > 0 && num2 != 0)
			{
				list.Add(num2);
			}
		}
		return list;
	}

	private void DrawPartyCooldownBarIcon(PartyCooldownGroupEntry entry, CooldownEntry cooldown, float size, int index)
	{
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
		_ = cursorScreenPos + new Vector2(size, size);
		ImU8String strId = new ImU8String(14, 4);
		strId.AppendLiteral("##party_cd_");
		strId.AppendFormatted(entry.SourceObjectId);
		strId.AppendLiteral("_");
		strId.AppendFormatted(cooldown.IconId);
		strId.AppendLiteral("_");
		strId.AppendFormatted(cooldown.Name);
		strId.AppendLiteral("_");
		strId.AppendFormatted(index);
		ImGui.InvisibleButton(strId, new Vector2(size, size));
		DrawNativeAttachedCooldownIcon(windowDrawList, cursorScreenPos, cooldown, size);
		if (ImGui.IsItemHovered())
		{
			DrawPartyCooldownTooltip(entry, cooldown);
		}
	}

	private void DrawSelfCooldownBarJobHeader(ImDrawListPtr drawList, Vector2 min, float size, uint jobIconId)
	{
		ThemePalette effectiveTheme = GetEffectiveTheme();
		Vector2 vector = min + new Vector2(size, size);
		float rounding = Math.Max(3f, size * 0.12f);
		float value = MathF.Max(2f, MathF.Round(size * 0.045f));
		drawList.AddRectFilled(min - new Vector2(1f), vector + new Vector2(1f), ImGui.GetColorU32(WithOpacity(effectiveTheme.TextShadow, 0.96f)), rounding);
		drawList.AddRectFilled(min, vector, ImGui.GetColorU32(WithOpacity(effectiveTheme.SurfaceAlt, 0.88f)), rounding);
		if (jobIconId != 0)
		{
			DrawGameIconImage(drawList, jobIconId, min + new Vector2(value), vector - new Vector2(value), fillBounds: true);
		}
	}

	private void DrawSelfCooldownBarCard(ImDrawListPtr drawList, Vector2 min, Vector2 max, float scale, float opacity)
	{
		ThemePalette effectiveTheme = GetEffectiveTheme();
		float rounding = MathF.Round(8f * scale);
		drawList.AddRectFilled(min + new Vector2(1f, 2f), max + new Vector2(1f, 2f), ImGui.GetColorU32(WithOpacity(effectiveTheme.TextShadow, 0.1f * opacity)), rounding);
		drawList.AddRectFilled(min, max, ImGui.GetColorU32(WithOpacity(effectiveTheme.Surface, 0.28f * opacity)), rounding);
		drawList.AddRect(min, max, ImGui.GetColorU32(WithOpacity(effectiveTheme.Border, 0.36f * opacity)), rounding, ImDrawFlags.None, Math.Max(1f, 1f * scale));
	}

	private void DrawPartyCooldownTooltip(PartyCooldownGroupEntry entry, CooldownEntry cooldown)
	{
		DrawStyledTooltip(delegate
		{
			ImGui.TextUnformatted(string.IsNullOrWhiteSpace(entry.SourceJobName) ? entry.SourceName : (entry.SourceName + " · " + entry.SourceJobName));
			ImGui.TextUnformatted(cooldown.Name);
			ImGui.Separator();
			if (cooldown.IsReady)
			{
				ImGui.TextUnformatted("冷却：就绪");
			}
			else
			{
				ImU8String text = new ImU8String(5, 1);
				text.AppendLiteral("冷却剩余：");
				text.AppendFormatted(FormatCooldownIconTime(cooldown.RemainingCooldownSeconds));
				ImGui.TextUnformatted(text);
			}
		});
	}

	private void TrackSelfCooldownBarPosition()
	{
		if (config.SelfCooldownBarLocked)
		{
			return;
		}
		Vector2 windowPos = ImGui.GetWindowPos();
		if (windowPos != config.SelfCooldownBarPosition)
		{
			config.SelfCooldownBarPosition = windowPos;
			selfCooldownBarPositionSaveDueAt = DateTime.UtcNow.Add(OverlayPositionSaveDelay);
		}
		DateTime? dateTime = selfCooldownBarPositionSaveDueAt;
		if (dateTime.HasValue)
		{
			DateTime valueOrDefault = dateTime.GetValueOrDefault();
			if (!(DateTime.UtcNow < valueOrDefault) && !(config.SelfCooldownBarPosition == lastSavedSelfCooldownBarPosition))
			{
				saveConfig();
				lastSavedSelfCooldownBarPosition = config.SelfCooldownBarPosition;
				selfCooldownBarPositionSaveDueAt = null;
			}
		}
	}

	private void DrawStatusWindow(ImGuiWindowFlags flags)
	{
		if (config.StatusBarLocked)
		{
			flags |= ImGuiWindowFlags.NoMove;
		}
		IReadOnlyList<StatusEntry> readOnlyList = (config.ShowStatusPreview ? CreatePreviewSelfHudStatuses() : combatState.GetSelfHudStatuses(config));
		if (readOnlyList.Count == 0)
		{
			return;
		}
		IReadOnlyList<StatusIconSection> readOnlyList2 = BuildSelfStatusIconSections(readOnlyList, config);
		if (readOnlyList2.Count == 0)
		{
			return;
		}
		float num = Math.Clamp(config.Scale, 0.5f, 2.5f);
		float nativeStatusOverlayIconSize = GetNativeStatusOverlayIconSize(num);
		float statusIconSpacing = GetStatusIconSpacing(num);
		float nativeStatusTimerHeight = GetNativeStatusTimerHeight(nativeStatusOverlayIconSize);
		float num2 = MathF.Round(8f * num);
		float num3 = MathF.Round(6f * num);
		float num4 = MathF.Round(((config.StatusBarLayoutMode == 1) ? 8f : 4f) * num);
		float statusSectionLabelWidth = GetStatusSectionLabelWidth(num);
		float statusSectionCardPadding = GetStatusSectionCardPadding(num);
		List<StatusSectionLayout> list = new List<StatusSectionLayout>(readOnlyList2.Count);
		float num5 = nativeStatusOverlayIconSize;
		float num6 = (float)Math.Max(0, readOnlyList2.Count - 1) * num4;
		float num7 = nativeStatusOverlayIconSize + statusSectionLabelWidth + statusSectionCardPadding * 2f + 18f * num;
		for (int i = 0; i < readOnlyList2.Count; i++)
		{
			StatusIconSection section = readOnlyList2[i];
			int columns = Math.Max(1, Math.Min(10, section.Statuses.Count));
			Vector2 statusIconGridSize = GetStatusIconGridSize(section.Statuses.Count, columns, nativeStatusOverlayIconSize, statusIconSpacing, nativeStatusTimerHeight);
			Vector2 size = new Vector2(statusIconGridSize.X + statusSectionLabelWidth + statusSectionCardPadding * 2f + 18f * num, statusIconGridSize.Y + statusSectionCardPadding * 2f);
			list.Add(new StatusSectionLayout(section, columns, statusIconGridSize, size));
			num5 = Math.Max(num5, statusIconGridSize.X);
			num6 += size.Y;
			num7 = Math.Max(num7, size.X);
		}
		float x = ((config.StatusBarLayoutMode == 1) ? (num7 + num2 * 2f) : (num5 + statusSectionLabelWidth + statusSectionCardPadding * 2f + num2 * 2f + 18f * num));
		Vector2 vector = new Vector2(x, num6 + num2 * 2f + num3);
		Vector2 clampedStatusOverlayPosition = GetClampedStatusOverlayPosition(config.StatusOverlayPosition, vector);
		ImGui.SetNextWindowPos(clampedStatusOverlayPosition, (clampedStatusOverlayPosition != config.StatusOverlayPosition) ? ImGuiCond.Always : ImGuiCond.FirstUseEver);
		ImGui.SetNextWindowSize(vector);
		flags |= ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground;
		if (!ImGui.Begin("AllHud 状态", flags))
		{
			ImGui.End();
			return;
		}
		Vector2 clampedStatusOverlayPosition2 = GetClampedStatusOverlayPosition(ImGui.GetWindowPos(), vector);
		if (clampedStatusOverlayPosition2 != ImGui.GetWindowPos())
		{
			ImGui.SetWindowPos(clampedStatusOverlayPosition2, ImGuiCond.Always);
		}
		TrackStatusOverlayPosition();
		ImGui.SetCursorPos(new Vector2(num2, num2));
		if (config.StatusBarLayoutMode == 1)
		{
			DrawSplitStatusIconSections(list, num4, statusSectionLabelWidth, nativeStatusOverlayIconSize, nativeStatusTimerHeight, statusIconSpacing, num);
		}
		else
		{
			DrawMergedStatusIconSections(list, num4, statusSectionLabelWidth, num5, nativeStatusOverlayIconSize, nativeStatusTimerHeight, statusIconSpacing, num);
		}
		ImGui.Dummy(new Vector2(1f, num3));
		ImGui.End();
	}

	private IReadOnlyList<StatusIconSection> BuildSelfStatusIconSections(IReadOnlyList<StatusEntry> selfStatuses, Configuration config)
	{
		int num = (int)((config.ShowSelfEnfeeblements ? 1u : 0u) | (uint)(config.ShowSelfOtherStatuses ? 2 : 0)) | (config.ShowSelfBuffs ? 4 : 0);
		if (cachedSelfStatusSections != null && cachedSelfStatusSectionSource == selfStatuses && cachedSelfStatusSectionToggles == num)
		{
			return cachedSelfStatusSections;
		}
		List<StatusIconSection> sections = new List<StatusIconSection>();
		IReadOnlyList<StatusEntry> source = OrderSelfHudStatuses(selfStatuses);
		AddSection("弱化", SelfDebuffColor, source.Where((StatusEntry status) => !status.IsBuff), 20, config.ShowSelfEnfeeblements);
		AddSection("其他", PersonalSkillColor, source.Where(IsOtherSelfStatus), 20, config.ShowSelfOtherStatuses);
		AddSection("强化", SelfBuffColor, source.Where((StatusEntry status) => status.IsBuff && !IsOtherSelfStatus(status)), 20, config.ShowSelfBuffs);
		cachedSelfStatusSectionSource = selfStatuses;
		cachedSelfStatusSectionToggles = num;
		cachedSelfStatusSections = sections;
		return sections;
		void AddSection(string label, Vector4 color, IEnumerable<StatusEntry> statuses, int maxCount, bool enabled)
		{
			if (enabled)
			{
				List<StatusEntry> list = statuses.Take(maxCount).ToList();
				if (list.Count != 0)
				{
					sections.Add(new StatusIconSection(label, color, list));
				}
			}
		}
	}

	private static IReadOnlyList<StatusEntry> OrderSelfHudStatuses(IEnumerable<StatusEntry> statuses)
	{
		return (from status in statuses
			orderby status.StatusIndex, status.PartyListPriority descending, (!status.CanDispel) ? 1 : 0, status.RemainingSeconds, status.StatusId
			select status).ToList();
	}

	private static bool IsOtherSelfStatus(StatusEntry status)
	{
		return status.StatusId == 48;
	}

	private IReadOnlyList<StatusEntry> GetOrderedTargetStatuses(IReadOnlyList<StatusEntry> statuses)
	{
		if (cachedTargetOrderedStatuses != null && cachedTargetStatusSplitSource == statuses)
		{
			return cachedTargetOrderedStatuses;
		}
		List<StatusEntry> list = new List<StatusEntry>(statuses.Count);
		foreach (StatusEntry status in statuses)
		{
			if (status.IsSelfApplied)
			{
				list.Add(status);
			}
		}
		foreach (StatusEntry status2 in statuses)
		{
			if (!status2.IsSelfApplied)
			{
				list.Add(status2);
			}
		}
		cachedTargetStatusSplitSource = statuses;
		cachedTargetOrderedStatuses = list;
		return list;
	}

	private static IReadOnlyList<StatusEntry> OrderTargetStatuses(IEnumerable<StatusEntry> statuses)
	{
		return (from status in statuses
			orderby status.IsBuff ? 1 : 0, status.StatusIndex, status.PartyListPriority descending, (!status.CanDispel) ? 1 : 0, status.RemainingSeconds, status.StatusId
			select status).ToList();
	}

	private IReadOnlyList<StatusEntry> CreatePreviewTargetStatuses()
	{
		List<StatusEntry> list = TrackedDefinitions.All.Where((TrackedStatusDefinition definition) => definition.StatusId != 0 && definition.IsTargetDebuff).Select((TrackedStatusDefinition definition, int index) => CreatePreviewStatus(definition, index, isBuff: false)).ToList();
		list.AddRange(CreateCustomPreviewStatuses(CustomTrackType.TargetStatus, isBuff: false, list.Count));
		return list;
	}

	private TargetInfoEntry CreatePreviewTargetInfo()
	{
		return new TargetInfoEntry(3413770241uL, 901u, "木人", 1u, 444321u, 456789u, IsCasting: true, IsCastInterruptible: true, 7549u, "目标情报预览咏唱", 1.65f, 3f, new TargetOfTargetEntry(3413770242uL, "预览目标的目标", 62345u, 98765u), CreatePreviewTargetInfoStatuses());
	}

	private IReadOnlyList<StatusEntry> CreatePreviewTargetInfoStatuses()
	{
		List<StatusEntry> list = CreatePreviewTargetStatuses().ToList();
		if (list.Count == 0)
		{
			return Array.Empty<StatusEntry>();
		}
		List<StatusEntry> list2 = new List<StatusEntry>(30);
		for (int i = 0; i < 30; i++)
		{
			StatusEntry statusEntry = list[i % list.Count];
			list2.Add(statusEntry with
			{
				IsBuff = false,
				IsSelfApplied = (i < 3),
				RemainingSeconds = GetPreviewRemainingSeconds(Math.Max(1f, statusEntry.MaxSeconds), i),
				StatusIndex = i
			});
		}
		return list2;
	}

	private IReadOnlyList<StatusEntry> CreatePreviewSelfStatuses()
	{
		List<StatusEntry> list = TrackedDefinitions.All.Where((TrackedStatusDefinition definition) => definition.StatusId != 0 && !definition.IsTargetDebuff).Select((TrackedStatusDefinition definition, int index) => CreatePreviewStatus(definition, index, isBuff: true)).ToList();
		list.AddRange(CreateCustomPreviewStatuses(CustomTrackType.SelfStatus, isBuff: true, list.Count));
		return list;
	}

	private IReadOnlyList<StatusEntry> CreatePreviewSelfHudStatuses()
	{
		List<StatusEntry> source = CreatePreviewSelfStatuses().ToList();
		List<StatusEntry> list = new List<StatusEntry>();
		list.AddRange(source.Take(8).Select((StatusEntry status, int index) => status with
		{
			HolderName = "我",
			IsBuff = true,
			PartyListPriority = false,
			MaxSeconds = Math.Min(status.MaxSeconds, 180f),
			StatusIndex = index
		}));
		list.AddRange(source.Skip(8).Take(3).Select((StatusEntry status, int index) => status with
		{
			HolderName = "我",
			IsBuff = true,
			PartyListPriority = true,
			MaxSeconds = Math.Min(status.MaxSeconds, 60f),
			StatusIndex = 30 + index
		}));
		list.AddRange(CreatePreviewSelfEnfeeblements(50));
		uint statusIconId = combatState.GetStatusIconId(48u);
		list.Add(new StatusEntry(48u, statusIconId, "食物效果", "我", "系统", string.Empty, 13333380uL, 1599f, 1800f, IsBuff: true, IsSelfApplied: true, 70));
		return list;
	}

	private IReadOnlyList<StatusEntry> CreatePreviewSelfEnfeeblements(int startIndex)
	{
		return new(uint, string, float, float)[5]
		{
			(43u, "虚弱", 4f, 100f),
			(44u, "濒死", 9f, 100f),
			(17u, "麻痹", 10f, 30f),
			(18u, "中毒", 8f, 30f),
			(14u, "加重", 3f, 20f)
		}.Select(((uint StatusId, string Name, float Remaining, float Max) status, int index) => CreatePreviewStatus(status.StatusId, status.Name, isBuff: false, isSelfApplied: false, status.Remaining, status.Max, (ulong)(700 + startIndex + index), startIndex + index)).ToList();
	}

	private IEnumerable<StatusEntry> CreateCustomPreviewStatuses(CustomTrackType type, bool isBuff, int startIndex)
	{
		if (config.CustomTrackedDefinitions != null)
		{
			List<CustomTrackedDefinition> customItems = config.CustomTrackedDefinitions.Where((CustomTrackedDefinition definition) => definition.Enabled && definition.Type == type && definition.StatusId != 0).ToList();
			for (int index = 0; index < customItems.Count; index++)
			{
				CustomTrackedDefinition customTrackedDefinition = customItems[index];
				float maxSeconds = Math.Max(1f, customTrackedDefinition.DurationSeconds);
				float previewRemainingSeconds = GetPreviewRemainingSeconds(maxSeconds, startIndex + index);
				yield return CreatePreviewStatus(customTrackedDefinition.StatusId, string.IsNullOrWhiteSpace(customTrackedDefinition.Name) ? $"定制状态 {customTrackedDefinition.StatusId}" : customTrackedDefinition.Name, isBuff, index % 2 == 0, previewRemainingSeconds, maxSeconds, (ulong)(500 + startIndex + index), startIndex + index);
			}
		}
	}

	private StatusEntry CreatePreviewStatus(TrackedStatusDefinition definition, int index, bool isBuff)
	{
		float maxSeconds = Math.Max(1f, definition.DurationSeconds);
		float previewRemainingSeconds = GetPreviewRemainingSeconds(maxSeconds, index);
		return CreatePreviewStatus(definition.StatusId, definition.Name, isBuff, index % 2 == 0, previewRemainingSeconds, maxSeconds, (ulong)(index + 1), index);
	}

	private StatusEntry CreatePreviewStatus(uint statusId, string name, bool isBuff, bool isSelfApplied, float remainingSeconds, float maxSeconds, ulong sourceOffset, int statusIndex = int.MaxValue)
	{
		uint statusIconId = combatState.GetStatusIconId(statusId);
		if (statusIconId == 0)
		{
			statusIconId = combatState.GetStatusIconId(48u);
		}
		return new StatusEntry(statusId, statusIconId, name, "测试目标", isSelfApplied ? "我" : "队友", isSelfApplied ? "当前职业" : "队友职业", 13332480 + sourceOffset, remainingSeconds, maxSeconds, isBuff, isSelfApplied, statusIndex);
	}

	private static float GetPreviewRemainingSeconds(float maxSeconds, int index)
	{
		if (maxSeconds >= 300f)
		{
			return Math.Max(1f, maxSeconds - (float)index * 37f);
		}
		float num = 0.25f + (float)(index % 4) * 0.18f;
		return Math.Clamp(maxSeconds * num, 1f, maxSeconds);
	}

	private void TrackStatusOverlayPosition()
	{
		if (config.StatusBarLocked)
		{
			return;
		}
		Vector2 windowPos = ImGui.GetWindowPos();
		if (windowPos != config.StatusOverlayPosition)
		{
			config.StatusOverlayPosition = windowPos;
			statusOverlayPositionSaveDueAt = DateTime.UtcNow.Add(OverlayPositionSaveDelay);
		}
		DateTime? dateTime = statusOverlayPositionSaveDueAt;
		if (dateTime.HasValue)
		{
			DateTime valueOrDefault = dateTime.GetValueOrDefault();
			if (!(DateTime.UtcNow < valueOrDefault) && !(config.StatusOverlayPosition == lastSavedStatusOverlayPosition))
			{
				saveConfig();
				lastSavedStatusOverlayPosition = config.StatusOverlayPosition;
				statusOverlayPositionSaveDueAt = null;
			}
		}
	}

	private static Vector2 GetClampedStatusOverlayPosition(Vector2 position, Vector2 windowSize)
	{
		ImGuiViewportPtr mainViewport = ImGui.GetMainViewport();
		Vector2 workPos = mainViewport.WorkPos;
		Vector2 vector = mainViewport.WorkPos + mainViewport.WorkSize;
		float max = Math.Max(workPos.X, vector.X - windowSize.X);
		float max2 = Math.Max(workPos.Y, vector.Y - windowSize.Y);
		return SnapToPixel(new Vector2(Math.Clamp(position.X, workPos.X, max), Math.Clamp(position.Y, workPos.Y, max2)));
	}

	private static void DrawNeonBorder(Vector2 min, Vector2 max, Vector4 color, float glow, float rounding, float thickness = 1.2f)
	{
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		if (glow > 0.5f)
		{
			float num = glow * 1.4f;
			windowDrawList.AddRect(min - new Vector2(num, num), max + new Vector2(num, num), ColorWithAlpha(color.W * 0.05f), rounding + num, ImDrawFlags.None, thickness + 1f);
			float num2 = glow * 0.7f;
			windowDrawList.AddRect(min - new Vector2(num2, num2), max + new Vector2(num2, num2), ColorWithAlpha(color.W * 0.15f), rounding + num2, ImDrawFlags.None, thickness + 0.5f);
		}
		windowDrawList.AddRect(min, max, ColorWithAlpha(color.W * 0.55f), rounding, ImDrawFlags.None, thickness);
		uint ColorWithAlpha(float alpha)
		{
			Vector4 col = color;
			col.W = alpha;
			return ImGui.GetColorU32(col);
		}
	}

	private static float GetStatusIconSpacing(float scale)
	{
		return MathF.Round(-8f * scale);
	}

	private static float GetStatusSectionLabelWidth(float scale)
	{
		return MathF.Round(78f * scale);
	}

	private static float GetStatusSectionCardPadding(float scale)
	{
		return MathF.Round(8f * scale);
	}

	private void DrawMergedStatusIconSections(IReadOnlyList<StatusSectionLayout> layouts, float sectionGap, float labelWidth, float gridWidth, float iconSize, float timerHeight, float spacing, float scale)
	{
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		ThemePalette effectiveTheme = GetEffectiveTheme();
		Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
		float statusSectionCardPadding = GetStatusSectionCardPadding(scale);
		float num = 0f;
		float x = labelWidth + gridWidth + statusSectionCardPadding * 2f + 18f * scale;
		float num2 = (float)Math.Max(0, layouts.Count - 1) * sectionGap;
		for (int i = 0; i < layouts.Count; i++)
		{
			num2 += layouts[i].Size.Y;
		}
		DrawStatusSectionCard(windowDrawList, cursorScreenPos, cursorScreenPos + new Vector2(x, num2), layouts[0].Section.Color, scale, splitMode: false);
		for (int j = 0; j < layouts.Count; j++)
		{
			StatusSectionLayout statusSectionLayout = layouts[j];
			StatusIconSection section = statusSectionLayout.Section;
			Vector2 gridSize = statusSectionLayout.GridSize;
			float y = statusSectionLayout.Size.Y;
			Vector2 vector = new Vector2(cursorScreenPos.X, cursorScreenPos.Y + num);
			Vector2 vector2 = vector + new Vector2(x, y);
			if (j > 0)
			{
				windowDrawList.AddLine(vector + new Vector2(statusSectionCardPadding, 0f), new Vector2(vector2.X - statusSectionCardPadding, vector.Y), ImGui.GetColorU32(WithOpacity(effectiveTheme.Border, 0.2f)), 1f * scale);
			}
			ImGui.SetCursorScreenPos(vector + new Vector2(statusSectionCardPadding, statusSectionCardPadding));
			DrawStatusSectionHeader(section.Label, section.Color, labelWidth, gridSize.Y, scale, splitMode: false);
			DrawStatusIconGrid(section.Statuses, statusSectionLayout.Columns, iconSize, timerHeight, spacing, splitMode: false);
			ImGui.SetCursorScreenPos(vector);
			ImGui.Dummy(new Vector2(x, y));
			num += y + ((j + 1 < layouts.Count) ? sectionGap : 0f);
		}
	}

	private void DrawSplitStatusIconSections(IReadOnlyList<StatusSectionLayout> layouts, float sectionGap, float labelWidth, float iconSize, float timerHeight, float spacing, float scale)
	{
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
		float statusSectionCardPadding = GetStatusSectionCardPadding(scale);
		float num = 0f;
		for (int i = 0; i < layouts.Count; i++)
		{
			StatusSectionLayout statusSectionLayout = layouts[i];
			StatusIconSection section = statusSectionLayout.Section;
			Vector2 gridSize = statusSectionLayout.GridSize;
			float x = statusSectionLayout.Size.X;
			float y = statusSectionLayout.Size.Y;
			Vector2 vector = new Vector2(cursorScreenPos.X, cursorScreenPos.Y + num);
			Vector2 max = vector + new Vector2(x, y);
			DrawStatusSectionCard(windowDrawList, vector, max, section.Color, scale, splitMode: true);
			ImGui.SetCursorScreenPos(vector + new Vector2(statusSectionCardPadding, statusSectionCardPadding));
			DrawStatusSectionHeader(section.Label, section.Color, labelWidth, gridSize.Y, scale, splitMode: true);
			DrawStatusIconGrid(section.Statuses, statusSectionLayout.Columns, iconSize, timerHeight, spacing, splitMode: true);
			ImGui.SetCursorScreenPos(vector);
			ImGui.Dummy(new Vector2(x, y));
			num += y + ((i + 1 < layouts.Count) ? sectionGap : 0f);
		}
	}

	private void DrawStatusSectionCard(ImDrawListPtr drawList, Vector2 min, Vector2 max, Vector4 accentColor, float scale, bool splitMode)
	{
		ThemePalette effectiveTheme = GetEffectiveTheme();
		float num = (splitMode ? (18f * scale) : (16f * scale));
		drawList.AddRectFilled(min + new Vector2(0f, 2f * scale), max + new Vector2(0f, 2f * scale), ImGui.GetColorU32(WithOpacity(effectiveTheme.TextShadow, 0.12f)), num);
		drawList.AddRectFilled(min, max, ImGui.GetColorU32(WithOpacity(effectiveTheme.Surface, 0.8f)), num);
		Vector2 pMin = min + new Vector2(1f * scale, 1f * scale);
		Vector2 pMax = new Vector2(max.X - 1f * scale, min.Y + Math.Max(1f, (max.Y - min.Y) * 0.42f));
		drawList.AddRectFilled(pMin, pMax, ImGui.GetColorU32(WithOpacity(effectiveTheme.TaskBarGradientStart, 0.24f)), num, ImDrawFlags.RoundCornersTop);
		drawList.AddRect(min, max, ImGui.GetColorU32(WithOpacity(effectiveTheme.Border, 0.82f)), num, ImDrawFlags.None, 1f * scale);
		if (splitMode)
		{
			drawList.AddRect(min + new Vector2(1f, 1f), max - new Vector2(1f, 1f), ImGui.GetColorU32(new Vector4(accentColor.X, accentColor.Y, accentColor.Z, 0.12f)), num * 0.85f, ImDrawFlags.None, 1f * scale);
		}
	}

	private void DrawStatusSectionHeader(string label, Vector4 accentColor, float labelWidth, float gridHeight, float scale, bool splitMode)
	{
		ThemePalette effectiveTheme = GetEffectiveTheme();
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
		float num = MathF.Round(24f * scale);
		Vector2 vector = cursorScreenPos + new Vector2(0f, Math.Max(0f, (gridHeight - num) * 0.5f));
		Vector2 pMax = vector + new Vector2(labelWidth, num);
		windowDrawList.AddRectFilled(vector, pMax, ImGui.GetColorU32(WithOpacity(effectiveTheme.SurfaceAlt, 0.88f)), 999f);
		windowDrawList.AddRect(vector, pMax, ImGui.GetColorU32(WithOpacity(effectiveTheme.Border, 0.55f)), 999f, ImDrawFlags.None, 1f * scale);
		windowDrawList.AddCircleFilled(vector + new Vector2(12f * scale, num * 0.5f), 3.5f * scale, ImGui.GetColorU32(accentColor));
		DrawShadowText(windowDrawList, vector + new Vector2(22f * scale, Math.Max(0f, (num - ImGui.GetTextLineHeight()) * 0.5f)), ImGui.GetColorU32(WithOpacity(effectiveTheme.Text, 0.98f)), label, GetFontSize(scale));
		ImGui.SetCursorScreenPos(new Vector2(pMax.X + 18f * scale, cursorScreenPos.Y));
	}

	private void DrawStatusIconGrid(IReadOnlyList<StatusEntry> statuses, int columns, float iconSize, float timerHeight, float spacing, bool splitMode)
	{
		Vector2 cursorPos = ImGui.GetCursorPos();
		float num = iconSize + spacing;
		float num2 = iconSize + spacing + timerHeight;
		for (int i = 0; i < statuses.Count; i++)
		{
			int num3 = i / columns;
			int num4 = i % columns;
			ImGui.SetCursorPos(cursorPos + new Vector2((float)num4 * num, (float)num3 * num2));
			DrawStatusIcon(statuses[i], iconSize);
		}
		Vector2 statusIconGridSize = GetStatusIconGridSize(statuses.Count, columns, iconSize, spacing, timerHeight);
		ImGui.SetCursorPos(cursorPos);
		ImGui.Dummy(statusIconGridSize + new Vector2(0f, splitMode ? 1f : 0f));
	}

	private static Vector2 GetStatusIconGridSize(int count, int columns, float iconSize, float spacing, float timerHeight)
	{
		if (count <= 0)
		{
			return Vector2.Zero;
		}
		columns = Math.Max(1, columns);
		int num = (int)Math.Ceiling((float)count / (float)columns);
		float x = (float)columns * iconSize + (float)Math.Max(0, columns - 1) * spacing;
		float y = (float)num * iconSize + (float)Math.Max(0, num - 1) * (spacing + timerHeight) + timerHeight;
		return new Vector2(x, y);
	}

	private static float GetNativeStatusOverlayIconSize(float scale)
	{
		return Math.Clamp(38f * Math.Clamp(scale, 0.5f, 2.5f), 12f, 64f);
	}

	private static float GetNativeStatusTimerHeight(float iconSize)
	{
		float fontSize = ImGui.GetFontSize();
		return MathF.Round(Math.Clamp(iconSize * 0.5f, fontSize * 0.92f, fontSize * 1.18f)) + 2f;
	}

	private static float GetStatusTimerTextHeight(float iconSize, float fontScale = 1f)
	{
		float num = ImGui.GetFontSize() * fontScale;
		return Math.Clamp(iconSize * 0.52f, num * 0.92f, num * 1.4f) + 8f;
	}

	private void DrawStatusIcon(StatusEntry status, float size)
	{
		ImGui.GetWindowDrawList();
		DrawGameIcon(status.IconId, size);
		Vector2 itemRectMin = ImGui.GetItemRectMin();
		Vector2 itemRectMax = ImGui.GetItemRectMax();
		if (ShouldShowStatusTimer(status))
		{
			uint textColor = (status.IsSelfApplied ? ImGui.GetColorU32(config.SelfAppliedTimerColor) : ImGui.GetColorU32(config.OtherAppliedTimerColor));
			DrawStatusTimerText(FormatCooldownIconTime(status.RemainingSeconds), itemRectMin, itemRectMax, textColor, size);
		}
		if (ImGui.IsItemHovered())
		{
			DrawStatusTooltip(status, config.ShowRawStatusIds);
		}
	}

	private void DrawStatusTooltip(StatusEntry status, bool showRawStatusId)
	{
		DrawStyledTooltip(delegate
		{
			ImGui.TextUnformatted(GetStatusTooltipTitle(status));
			ImGui.Separator();
			ImU8String text = new ImU8String(3, 1);
			text.AppendLiteral("来源：");
			text.AppendFormatted(FormatSourceLabel(status.SourceName, status.SourceJobName, config.ShowSourceJobNames));
			ImGui.TextUnformatted(text);
			if (showRawStatusId)
			{
				ImU8String text2 = new ImU8String(5, 1);
				text2.AppendLiteral("状态ID：");
				text2.AppendFormatted(status.StatusId);
				ImGui.TextUnformatted(text2);
			}
		});
	}

	private static string GetStatusTooltipTitle(StatusEntry status)
	{
		return $"{status.Name} ({status.StatusId})";
	}

	private void DrawGameIcon(uint iconId, float size)
	{
		Vector2 vector = new Vector2(size, size);
		if (iconId == 0)
		{
			ImGui.Dummy(vector);
			return;
		}
		if (!TryGetFrameGameIconWrap(iconId, out IDalamudTextureWrap wrap) || wrap == null)
		{
			ImGui.Dummy(vector);
			return;
		}
		Vector2 aspectFitSize = GetAspectFitSize(wrap.Size, vector);
		Vector2 cursorPos = ImGui.GetCursorPos();
		ImGui.SetCursorPos(cursorPos + (vector - aspectFitSize) * 0.5f);
		ImGui.Image(wrap.Handle, aspectFitSize);
		ImGui.SetCursorPos(cursorPos + vector);
	}

	private bool DrawGameIconImage(ImDrawListPtr drawList, uint iconId, Vector2 min, Vector2 max, bool fillBounds = false, bool bypassMissingRetry = false)
	{
		if (iconId == 0)
		{
			return false;
		}
		if (!TryGetFrameGameIconWrap(iconId, out IDalamudTextureWrap wrap, bypassMissingRetry) || wrap == null)
		{
			return false;
		}
		if (fillBounds)
		{
			Vector2 vector = max - min;
			float num = wrap.Size.X / Math.Max(1f, wrap.Size.Y);
			float num2 = vector.X / Math.Max(1f, vector.Y);
			Vector2 zero = Vector2.Zero;
			Vector2 one = Vector2.One;
			if (num > num2)
			{
				float num3 = num2 / num;
				one.X = 1f - (zero.X = (1f - num3) * 0.5f);
			}
			else if (num < num2)
			{
				float num4 = num / num2;
				one.Y = 1f - (zero.Y = (1f - num4) * 0.5f);
			}
			drawList.AddImage(wrap.Handle, min, max, zero, one);
		}
		else
		{
			Vector2 vector2 = max - min;
			Vector2 aspectFitSize = GetAspectFitSize(wrap.Size, vector2);
			Vector2 vector3 = min + (vector2 - aspectFitSize) * 0.5f;
			drawList.AddImage(wrap.Handle, vector3, vector3 + aspectFitSize);
		}
		return true;
	}

	private bool TryGetFrameGameIconWrap(uint iconId, out IDalamudTextureWrap? wrap, bool bypassMissingRetry = false)
	{
		wrap = null;
		if (iconId == 0)
		{
			return false;
		}
		DateTime utcNow = DateTime.UtcNow;
		if (!bypassMissingRetry && missingGameIconRetryAt.TryGetValue(iconId, out var value) && utcNow < value)
		{
			return false;
		}
		if (frameGameIconWrapCache.TryGetValue(iconId, out wrap))
		{
			return wrap != null;
		}
		if (TryGetGameIconWrap(iconId, hiRes: true, out wrap) || TryGetGameIconWrap(iconId, hiRes: false, out wrap))
		{
			frameGameIconWrapCache[iconId] = wrap;
			missingGameIconRetryAt.Remove(iconId);
			return true;
		}
		frameGameIconWrapCache[iconId] = null;
		missingGameIconRetryAt[iconId] = utcNow + MissingGameIconRetryDelay;
		TrimMissingGameIconRetryCache();
		wrap = null;
		return false;
	}

	private bool TryGetGameIconWrap(uint iconId, bool hiRes, out IDalamudTextureWrap? wrap)
	{
		wrap = null;
		GameIconCacheKey key = new GameIconCacheKey(iconId, hiRes);
		try
		{
			Exception exception;
			if (gameIconTextureCache.TryGetValue(key, out ISharedImmediateTexture value))
			{
				if (value.TryGetWrap(out wrap, out exception) && wrap != null)
				{
					return true;
				}
				gameIconTextureCache.Remove(key);
			}
			GameIconLookup lookup = new GameIconLookup
			{
				IconId = iconId,
				HiRes = hiRes
			};
			if (textureProvider.TryGetFromGameIcon(in lookup, out ISharedImmediateTexture texture) && texture != null)
			{
				gameIconTextureCache[key] = texture;
				if (texture.TryGetWrap(out wrap, out exception) && wrap != null)
				{
					TrimGameIconTextureCache();
					return true;
				}
				gameIconTextureCache.Remove(key);
			}
		}
		catch
		{
			gameIconTextureCache.Remove(key);
		}
		wrap = null;
		return false;
	}

	private void TrimGameIconTextureCache()
	{
		if (gameIconTextureCache.Count <= 512)
		{
			return;
		}
		foreach (GameIconCacheKey item in gameIconTextureCache.Keys.Take(Math.Max(1, gameIconTextureCache.Count - 512)).ToList())
		{
			gameIconTextureCache.Remove(item);
		}
	}

	private void TrimMissingGameIconRetryCache()
	{
		if (missingGameIconRetryAt.Count <= 256)
		{
			return;
		}
		DateTime now = DateTime.UtcNow;
		foreach (uint item in (from pair in missingGameIconRetryAt
			where pair.Value <= now
			select pair.Key).ToList())
		{
			missingGameIconRetryAt.Remove(item);
		}
		if (missingGameIconRetryAt.Count > 256)
		{
			missingGameIconRetryAt.Clear();
		}
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

	private void DrawCenteredIconText(ImDrawListPtr drawList, string text, Vector2 min, Vector2 max, uint color, float iconSize, bool bold = false, float horizontalPadding = 2f)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return;
		}
		float fontSize = ImGui.GetFontSize();
		float num = Math.Clamp(iconSize * 0.4f, fontSize * 0.78f, fontSize * 1.08f);
		IFontHandle hudFont = GetHudFont(GetNearestHudFontSize(num));
		if (!hudFont.Available)
		{
			return;
		}
		IDisposable disposable = hudFont.Push();
		Vector2 vector = ImGui.CalcTextSize(text);
		float num2 = Math.Max(1f, max.X - min.X - horizontalPadding * 2f);
		float num3 = Math.Max(1f, max.Y - min.Y - 2f);
		if (vector.X > num2 || vector.Y > num3)
		{
			disposable.Dispose();
			float num4 = Math.Min(num2 / Math.Max(1f, vector.X), num3 / Math.Max(1f, vector.Y));
			num = Math.Max(fontSize * 0.52f, num * num4);
			hudFont = GetHudFont(GetNearestHudFontSize(num));
			if (!hudFont.Available)
			{
				return;
			}
			disposable = hudFont.Push();
			vector = ImGui.CalcTextSize(text);
		}
		Vector2 vector2 = SnapToPixel((min + max) * 0.5f - vector * 0.5f);
		uint colorU = ImGui.GetColorU32(WithOpacity(GetEffectiveTheme().TextShadow, 0.95f));
		drawList.AddText(vector2 + new Vector2(1f, 1f), colorU, text);
		if (bold)
		{
			drawList.AddText(vector2 + new Vector2(1f, 0f), color, text);
		}
		drawList.AddText(vector2, color, text);
		disposable.Dispose();
	}

	private void DrawStatusTimerText(string text, Vector2 min, Vector2 max, uint textColor, float iconSize)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return;
		}
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		float fontSize = ImGui.GetFontSize();
		float num = MathF.Round(Math.Clamp(iconSize * 0.5f, fontSize * 0.92f, fontSize * 1.18f));
		using (PushHudFont(num))
		{
			Vector2 vector = ImGui.CalcTextSize(text);
			float num2 = MathF.Round(num * 0.42f);
			Vector2 vector2 = new Vector2(MathF.Round(min.X + (max.X - min.X - vector.X) * 0.5f), MathF.Round(max.Y - num2));
			uint colorU = ImGui.GetColorU32(WithOpacity(GetEffectiveTheme().TextShadow, 0.96f));
			windowDrawList.AddText(vector2 + new Vector2(-1f, -1f), colorU, text);
			windowDrawList.AddText(vector2 + new Vector2(-1f, 0f), colorU, text);
			windowDrawList.AddText(vector2 + new Vector2(-1f, 1f), colorU, text);
			windowDrawList.AddText(vector2 + new Vector2(0f, -1f), colorU, text);
			windowDrawList.AddText(vector2 + new Vector2(0f, 1f), colorU, text);
			windowDrawList.AddText(vector2 + new Vector2(1f, -1f), colorU, text);
			windowDrawList.AddText(vector2 + new Vector2(1f, 0f), colorU, text);
			windowDrawList.AddText(vector2 + new Vector2(1f, 1f), colorU, text);
			windowDrawList.AddText(vector2 + new Vector2(1f, 0f), textColor, text);
			windowDrawList.AddText(vector2, textColor, text);
		}
	}

	private bool IsMitigationCooldownsVisible()
	{
		if (!config.ShowPartyMitigationCooldowns && !config.ShowTargetMitigationCooldowns && !config.ShowPersonalMitigationCooldowns)
		{
			return config.ShowMitigationCooldowns;
		}
		return true;
	}

	private static Vector4 GetCooldownSectionColor(CooldownGroup group)
	{
		return NormalizeCooldownGroup(group) switch
		{
			CooldownGroup.Common => PersonalSkillColor, 
			CooldownGroup.Personal => PersonalSkillColor, 
			CooldownGroup.Burst => PersonalSkillColor, 
			CooldownGroup.PartyMitigation => MitigationColor, 
			CooldownGroup.PersonalMitigation => SelfBuffColor, 
			CooldownGroup.RaidBuff => RaidBuffColor, 
			CooldownGroup.Mitigation => MitigationColor, 
			_ => HeaderColor, 
		};
	}

	private static string FormatStatusTime(StatusEntry status)
	{
		if (status.RemainingSeconds >= 9999f)
		{
			return "永久";
		}
		if (!IsLongOrPermanentStatus(status))
		{
			return $"{status.RemainingSeconds:0.0}秒";
		}
		return $"{status.RemainingSeconds / 60f:0.0}分";
	}

	private static bool IsLongOrPermanentStatus(StatusEntry status)
	{
		if (!(status.RemainingSeconds >= 9999f) && !(status.RemainingSeconds >= 300f))
		{
			return status.MaxSeconds >= 300f;
		}
		return true;
	}

	private static bool ShouldShowStatusTimer(StatusEntry status)
	{
		if (status.RemainingSeconds > 0.05f && status.RemainingSeconds < 9999f)
		{
			return status.MaxSeconds > 0f;
		}
		return false;
	}

	private string FormatCooldownIconTime(float seconds)
	{
		int num = Math.Max(0, (int)MathF.Ceiling(seconds));
		if (cooldownIconTimeTextCache.TryGetValue(num, out string value))
		{
			return value;
		}
		string text;
		if (num <= 120)
		{
			text = num.ToString();
		}
		else
		{
			int value2 = num / 60;
			int value3 = num % 60;
			text = $"{value2}:{value3:00}";
		}
		cooldownIconTimeTextCache[num] = text;
		return text;
	}

	private static CooldownGroup NormalizeCooldownGroup(CooldownGroup group)
	{
		if (group != CooldownGroup.TargetMitigation)
		{
			return group;
		}
		return CooldownGroup.PartyMitigation;
	}

	private static string FormatSourceLabel(string sourceName, string sourceJobName, bool showJobName)
	{
		if (string.IsNullOrWhiteSpace(sourceName) || sourceName == "Unknown")
		{
			return string.Empty;
		}
		if (!showJobName || string.IsNullOrWhiteSpace(sourceJobName))
		{
			return sourceName;
		}
		return sourceJobName;
	}

	private void DrawCustomTargetInfoWindow(ImGuiWindowFlags flags)
	{
		if (!config.ShowCustomTargetInfo)
		{
			return;
		}
		if (config.TargetInfoLocked)
		{
			flags |= ImGuiWindowFlags.NoMove;
		}
		if (IsNativeContextMenuVisible())
		{
			flags |= ImGuiWindowFlags.NoInputs;
		}
		TargetInfoEntry targetInfo = (config.ShowTargetInfoPreview ? CreatePreviewTargetInfo() : combatState.GetTargetInfo(config));
		if ((object)targetInfo == null || targetInfo.MaxHp == 0)
		{
			return;
		}
		float mainScale = Math.Clamp(config.CustomTargetInfoScale, 0.6f, 2f);
		if (!IsHudFontReady(GetFontSize(mainScale)))
		{
			return;
		}
		float num = 500f * mainScale;
		float num2 = 6f * mainScale;
		float num3 = 6f * mainScale;
		float num4 = 24f * mainScale;
		float hpBarHeight = 24f * mainScale;
		float targetOfTargetHeight = hpBarHeight;
		float targetOfTargetGap = 18f * mainScale;
		float castBarHeight = hpBarHeight;
		float num5 = 4f * mainScale;
		float otherIconSize = 38f * mainScale;
		float selfAppliedIconSize = 40f * mainScale;
		float iconGap = -8f * mainScale;
		float statusTimerHeight = 18f * mainScale;
		float contentWidth = num - num2 * 2f;
		bool flag = (object)targetInfo.TargetOfTarget != null;
		float targetOfTargetWidth = (flag ? Math.Clamp(contentWidth * 0.56f, 120f, 180f) : 0f);
		float x = num + (flag ? (targetOfTargetGap + targetOfTargetWidth) : 0f);
		int maxRows = Math.Clamp(config.CustomTargetInfoStatusRows, 1, 2);
		bool customTargetInfoStatusesAboveHp = config.CustomTargetInfoStatusesAboveHp;
		IReadOnlyList<TargetStatusIconLayout> readOnlyList = BuildTargetStatusIconLayouts(GetOrderedTargetStatuses(targetInfo.Statuses), contentWidth, maxRows, 15, selfAppliedIconSize, otherIconSize, iconGap, statusTimerHeight, customTargetInfoStatusesAboveHp, out var height);
		float num6 = ((readOnlyList.Count == 0) ? 0f : (num5 + height));
		float castBarWidth = Math.Clamp(contentWidth * 0.46f, 190f * mainScale, 240f * mainScale);
		bool num7 = !config.CustomTargetInfoSplitCastBar;
		bool flag2 = !config.CustomTargetInfoSplitStatusBar;
		int num8 = Math.Clamp(config.CustomTargetInfoCastBarPlacement, 0, 2);
		bool flag3 = ShouldDrawTargetCastBar(targetInfo);
		bool flag4 = (num7 & flag3) && num8 == 0;
		bool flag5 = (num7 & flag3) && num8 == 1;
		bool flag6 = (num7 & flag3) && num8 == 2;
		float num9 = (flag2 ? num6 : 0f);
		float num10 = 5f * mainScale;
		float num11 = (flag5 ? (castBarHeight + num10) : 0f);
		float num12 = (flag6 ? (num10 + castBarHeight) : 0f);
		float y = (customTargetInfoStatusesAboveHp ? (num3 * 2f + num11 + num9 + hpBarHeight + 2f * mainScale + Math.Max(num4, flag4 ? castBarHeight : 0f) + num12) : (num3 * 2f + num11 + num4 + 2f * mainScale + hpBarHeight + num9 + num12));
		Vector2 vector = new Vector2(x, y);
		ImGui.SetNextWindowPos(config.CustomTargetInfoPosition, ImGuiCond.FirstUseEver);
		ImGui.SetNextWindowSize(vector);
		flags |= ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground;
		if (!ImGui.Begin("AllHud 目标情报", flags))
		{
			ImGui.End();
			return;
		}
		TrackTargetInfoPosition();
		ImDrawListPtr drawList = ImGui.GetWindowDrawList();
		Vector2 windowPos = ImGui.GetWindowPos();
		Vector2 vector2 = windowPos + vector;
		ThemePalette effectiveTheme = GetEffectiveTheme();
		float num13 = Math.Clamp(config.CustomTargetInfoBackgroundOpacity, 0f, 0.8f);
		if (num13 > 0.001f)
		{
			drawList.AddRectFilled(windowPos + new Vector2(1f, 1f), vector2 + new Vector2(1f, 1f), ImGui.GetColorU32(WithOpacity(effectiveTheme.TextShadow, Math.Min(0.45f, num13 * 1.7f))), 4f);
			drawList.AddRectFilled(windowPos, vector2, ImGui.GetColorU32(WithOpacity(effectiveTheme.Surface, num13)), 4f);
			drawList.AddRect(windowPos, vector2, ImGui.GetColorU32(WithOpacity(effectiveTheme.Border, num13 * 0.72f)), 4f, ImDrawFlags.None, 1f);
		}
		Vector2 vector3 = windowPos + new Vector2(num2, num3);
		if (flag5)
		{
			DrawMainCastBar(vector3, fullWidth: true);
			vector3.Y += castBarHeight + num10;
		}
		Vector2 pos2;
		Vector2 size;
		if (customTargetInfoStatusesAboveHp)
		{
			if (flag2 && readOnlyList.Count > 0)
			{
				DrawTargetInfoStatusLayouts(readOnlyList, vector3);
				vector3.Y += height + num5;
			}
			Vector2 vector4 = vector3;
			DrawTargetInfoHpBar(drawList, targetInfo, vector4, contentWidth, hpBarHeight, mainScale, config.CustomTargetInfoHideHpNumbers, config.CustomTargetInfoHideMaxHp);
			DrawTargetOfTarget(vector4);
			vector3.Y += hpBarHeight + 2f * mainScale;
			Vector2 pos = vector3;
			DrawTargetInfoHeader(drawList, targetInfo, pos, contentWidth, num4, mainScale);
			if (flag4)
			{
				DrawMainCastBar(vector3, fullWidth: false);
			}
			vector3.Y += Math.Max(num4, flag4 ? castBarHeight : 0f);
			if (flag6)
			{
				vector3.Y += num10;
				DrawMainCastBar(vector3, fullWidth: true);
				vector3.Y += castBarHeight;
			}
			pos2 = vector4;
			size = new Vector2(contentWidth, hpBarHeight + 2f + num4);
		}
		else
		{
			pos2 = vector3;
			DrawTargetInfoHeader(drawList, targetInfo, vector3, contentWidth, num4, mainScale);
			vector3.Y += num4 + 2f * mainScale;
			Vector2 vector5 = vector3;
			DrawTargetInfoHpBar(drawList, targetInfo, vector5, contentWidth, hpBarHeight, mainScale, config.CustomTargetInfoHideHpNumbers, config.CustomTargetInfoHideMaxHp);
			if (flag4)
			{
				DrawTargetInfoCastBar(pos: new Vector2(vector5.X + contentWidth - castBarWidth, vector5.Y - castBarHeight - 5f * mainScale), drawList: drawList, targetInfo: targetInfo, width: castBarWidth, height: castBarHeight, textScale: mainScale);
			}
			DrawTargetOfTarget(vector5);
			vector3.Y += hpBarHeight;
			if (flag2 && readOnlyList.Count > 0)
			{
				vector3.Y += num5;
				DrawTargetInfoStatusLayouts(readOnlyList, vector3);
				vector3.Y += height;
			}
			if (flag6)
			{
				vector3.Y += num10;
				DrawMainCastBar(vector3, fullWidth: true);
				vector3.Y += castBarHeight;
			}
			size = new Vector2(contentWidth, num4 + 2f * mainScale + hpBarHeight);
		}
		HandleTargetSelectionHitArea(targetInfo.ObjectId, pos2, size, 3f);
		ImGui.End();
		if (config.CustomTargetInfoSplitCastBar)
		{
			DrawSplitTargetInfoCastBar(flags, targetInfo);
		}
		if (config.CustomTargetInfoSplitStatusBar)
		{
			DrawSplitTargetInfoStatusBar(flags, targetInfo);
		}
		void DrawMainCastBar(Vector2 rowPos, bool fullWidth)
		{
			float num14 = Math.Max(120f * mainScale, contentWidth - 28f * mainScale);
			float num15 = (fullWidth ? num14 : castBarWidth);
			Vector2 pos3 = (fullWidth ? (rowPos + new Vector2((contentWidth - num15) * 0.5f, 0f)) : new Vector2(rowPos.X + contentWidth - num15, rowPos.Y));
			DrawTargetInfoCastBar(drawList, targetInfo, pos3, num15, castBarHeight, mainScale);
		}
		void DrawTargetOfTarget(Vector2 hpPos)
		{
			if ((object)targetInfo.TargetOfTarget != null)
			{
				Vector2 pos3 = new Vector2(hpPos.X + contentWidth + targetOfTargetGap, hpPos.Y + (hpBarHeight - targetOfTargetHeight) * 0.5f);
				DrawTargetConnector(pos: new Vector2(hpPos.X + contentWidth + Math.Max(0f, targetOfTargetGap - 18f) * 0.5f, hpPos.Y), drawList: drawList, targetBarY: pos3.Y, targetBarHeight: targetOfTargetHeight);
				DrawTargetOfTargetBar(drawList, targetInfo.TargetOfTarget, pos3, targetOfTargetWidth, targetOfTargetHeight, mainScale);
			}
		}
	}

	private void DrawSplitTargetInfoCastBar(ImGuiWindowFlags flags, TargetInfoEntry targetInfo)
	{
		if (ShouldDrawTargetCastBar(targetInfo))
		{
			float num = Math.Clamp(config.CustomTargetInfoCastBarScale, 0.6f, 2f);
			float num2 = 240f * num;
			float num3 = 24f * num;
			Vector2 nextWindowSize = new Vector2(num2, num3);
			ImGui.SetNextWindowPos(config.CustomTargetInfoCastBarPosition, ImGuiCond.FirstUseEver);
			ImGui.SetNextWindowSize(nextWindowSize);
			flags |= ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground;
			if (!ImGui.Begin("AllHud 目标咏唱栏", flags))
			{
				ImGui.End();
				return;
			}
			TrackTargetInfoCastBarPosition();
			DrawTargetInfoCastBar(ImGui.GetWindowDrawList(), targetInfo, ImGui.GetWindowPos(), num2, num3, num);
			ImGui.End();
		}
	}

	private static bool ShouldDrawTargetCastBar(TargetInfoEntry targetInfo)
	{
		if (!targetInfo.IsCasting || targetInfo.TotalCastTime <= 0f)
		{
			return false;
		}
		return targetInfo.CurrentCastTime < targetInfo.TotalCastTime;
	}

	private void DrawSplitTargetInfoStatusBar(ImGuiWindowFlags flags, TargetInfoEntry targetInfo)
	{
		float num = Math.Clamp(config.CustomTargetInfoStatusBarScale, 0.6f, 2f);
		float num2 = 500f * num;
		float otherIconSize = 38f * num;
		float selfAppliedIconSize = 40f * num;
		float iconGap = -8f * num;
		float statusTimerHeight = 18f * num;
		int maxRows = Math.Clamp(config.CustomTargetInfoStatusRows, 1, 2);
		IReadOnlyList<TargetStatusIconLayout> readOnlyList = BuildTargetStatusIconLayouts(GetOrderedTargetStatuses(targetInfo.Statuses), num2, maxRows, 15, selfAppliedIconSize, otherIconSize, iconGap, statusTimerHeight, config.CustomTargetInfoStatusesAboveHp, out var height);
		if (readOnlyList.Count != 0)
		{
			Vector2 nextWindowSize = new Vector2(num2, height);
			ImGui.SetNextWindowPos(config.CustomTargetInfoStatusBarPosition, ImGuiCond.FirstUseEver);
			ImGui.SetNextWindowSize(nextWindowSize);
			flags |= ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground;
			if (!ImGui.Begin("AllHud 目标状态栏", flags))
			{
				ImGui.End();
				return;
			}
			TrackTargetInfoStatusBarPosition();
			DrawTargetInfoStatusLayouts(readOnlyList, ImGui.GetWindowPos());
			ImGui.End();
		}
	}

	private void HandleTargetSelectionHitArea(ulong objectId, Vector2 pos, Vector2 size, float rounding)
	{
		if (objectId != 0L && objectId != ulong.MaxValue && !(size.X <= 1f) && !(size.Y <= 1f) && !IsNativeContextMenuVisible() && ImGui.IsMouseHoveringRect(pos, pos + size, clip: false))
		{
			RequestNativeClickableCursor();
			if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
			{
				combatState.SelectTargetByObjectId(objectId);
			}
			else if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
			{
				combatState.OpenNativeContextMenuByObjectId(objectId, GetTargetContextMenuPosition(ImGui.GetMousePos(), pos, size));
			}
		}
	}

	private bool IsNativeContextMenuVisible()
	{
		if (!IsNativeAddonVisible("ContextMenu") && !IsNativeAddonVisible("ContextIconMenu"))
		{
			return IsNativeAddonVisible("AddonContextSub");
		}
		return true;
	}

	private unsafe bool IsNativeAddonVisible(string addonName)
	{
		AtkUnitBasePtr addonByName = gameGui.GetAddonByName(addonName);
		if (addonByName.IsNull)
		{
			return false;
		}
		AtkUnitBase* address = (AtkUnitBase*)addonByName.Address;
		if (address != null)
		{
			return address->IsVisible;
		}
		return false;
	}

	private static Vector2 GetTargetContextMenuPosition(Vector2 mousePos, Vector2 targetMin, Vector2 targetSize)
	{
		ImGuiViewportPtr mainViewport = ImGui.GetMainViewport();
		Vector2 workPos = mainViewport.WorkPos;
		Vector2 vector = mainViewport.WorkPos + mainViewport.WorkSize;
		Vector2 vector2 = new Vector2(230f, 190f);
		float num = 8f;
		Vector2 vector3 = targetMin + targetSize;
		float num2 = mousePos.X + num;
		float num3 = mousePos.Y + num;
		if (RectsOverlap(new Vector2(num2, num3), new Vector2(num2 + vector2.X, num3 + vector2.Y), targetMin - new Vector2(num), vector3 + new Vector2(num)))
		{
			float num4 = vector3.Y + num;
			float num5 = targetMin.Y - vector2.Y - num;
			num3 = ((num4 + vector2.Y <= vector.Y) ? num4 : num5);
		}
		num2 = Math.Clamp(num2, workPos.X + num, vector.X - vector2.X - num);
		num3 = Math.Clamp(num3, workPos.Y + num, vector.Y - vector2.Y - num);
		return SnapToPixel(new Vector2(num2, num3));
	}

	private static bool RectsOverlap(Vector2 minA, Vector2 maxA, Vector2 minB, Vector2 maxB)
	{
		if (minA.X < maxB.X && maxA.X > minB.X && minA.Y < maxB.Y)
		{
			return maxA.Y > minB.Y;
		}
		return false;
	}

	private void RequestNativeClickableCursor()
	{
		if (!nativeCursorForced)
		{
			addonEventManager.SetCursor(AddonCursorType.Clickable);
			nativeCursorForced = true;
		}
		nativeCursorRequestedThisFrame = true;
	}

	private void ResetNativeCursorIfNeeded()
	{
		if (nativeCursorForced)
		{
			addonEventManager.ResetCursor();
			nativeCursorForced = false;
		}
	}

	private void DrawTargetConnector(ImDrawListPtr drawList, Vector2 pos, float targetBarY, float targetBarHeight)
	{
		float num = (MathF.Sin((float)ImGui.GetTime() * 5.6f) + 1f) * 0.5f;
		float num2 = num * 3f;
		ThemePalette effectiveTheme = GetEffectiveTheme();
		uint colorU = ImGui.GetColorU32(WithOpacity(effectiveTheme.Text, 0.62f + num * 0.28f));
		uint colorU2 = ImGui.GetColorU32(WithOpacity(effectiveTheme.TextShadow, 0.58f));
		float y = targetBarY + targetBarHeight * 0.5f;
		Vector2 vector = new Vector2(pos.X + 1f + num2, y);
		Vector2 center = vector + new Vector2(7f, 0f);
		DrawTriangle(vector, colorU2, new Vector2(1f, 1f));
		DrawTriangle(center, colorU2, new Vector2(1f, 1f));
		DrawTriangle(vector, colorU, Vector2.Zero);
		DrawTriangle(center, colorU, Vector2.Zero);
		void DrawTriangle(Vector2 vector2, uint drawColor, Vector2 offset)
		{
			Vector2 p = vector2 + new Vector2(-3f, -5.5f) + offset;
			Vector2 p2 = vector2 + new Vector2(4f, 0f) + offset;
			Vector2 p3 = vector2 + new Vector2(-3f, 5.5f) + offset;
			drawList.AddTriangleFilled(p, p2, p3, drawColor);
		}
	}

	private void DrawTargetOfTargetBar(ImDrawListPtr drawList, TargetOfTargetEntry targetOfTarget, Vector2 pos, float width, float height, float textScale)
	{
		float num = ((targetOfTarget.MaxHp != 0) ? Math.Clamp((float)targetOfTarget.CurrentHp / (float)targetOfTarget.MaxHp, 0f, 1f) : 0f);
		string text = ((targetOfTarget.MaxHp != 0) ? $"{num * 100f:0.0}%" : string.Empty);
		float maxWidth = Math.Max(24f, width - CalcTextSize(text, textScale).X - 8f);
		string leftText = TruncateTextToWidth(targetOfTarget.Name, maxWidth, textScale);
		DrawTargetHealthBar(drawList, pos, width, height, num, new Vector4(0.84f, 0.2f, 0.84f, 0.92f), textScale, leftText, string.Empty, text, 5f * textScale, 6f * textScale, ImGui.GetColorU32(WithOpacity(GetEffectiveTheme().Text, 0.96f)), ImGui.GetColorU32(WithOpacity(GetEffectiveTheme().Text, 0.98f)), ImGui.GetColorU32(WithOpacity(GetEffectiveTheme().Text, 0.96f)));
		HandleTargetSelectionHitArea(targetOfTarget.ObjectId, pos, new Vector2(width, height), 2f);
	}

	private void DrawTargetInfoHeader(ImDrawListPtr drawList, TargetInfoEntry targetInfo, Vector2 pos, float width, float height, float textScale)
	{
		string text = ((targetInfo.Level != 0) ? $"Lv{targetInfo.Level} " : string.Empty);
		string text2 = ((targetInfo.DataId != 0) ? $" [{targetInfo.DataId}]" : string.Empty);
		float num = 4f * textScale;
		string text3 = TruncateTextToWidth(text + targetInfo.Name + text2, width - num * 2f, textScale);
		Vector2 pos2 = pos + new Vector2(num, Math.Max(0f, (height - CalcTextSize(text3, textScale).Y) * 0.5f));
		DrawShadowText(drawList, pos2, ImGui.GetColorU32(WithOpacity(GetEffectiveTheme().Text, 0.98f)), text3, GetFontSize(textScale));
	}

	private void DrawTargetInfoHpBar(ImDrawListPtr drawList, TargetInfoEntry targetInfo, Vector2 pos, float width, float height, float textScale, bool hideHpNumbers, bool hideMaxHp)
	{
		float num = ((targetInfo.MaxHp != 0) ? Math.Clamp((float)targetInfo.CurrentHp / (float)targetInfo.MaxHp, 0f, 1f) : 0f);
		Vector4 fillColor = ((num < 0.2f) ? new Vector4(0.95f, 0.18f, 0.16f, 0.92f) : ((num < 0.5f) ? new Vector4(0.82f, 0.32f, 0.15f, 0.9f) : new Vector4(0.78f, 0.12f, 0.13f, 0.9f)));
		float value = ((targetInfo.MaxHp != 0) ? ((float)targetInfo.CurrentHp / (float)targetInfo.MaxHp * 100f) : 0f);
		string rightText = $"{value:0.0}%";
		string centerText = string.Empty;
		if (!hideHpNumbers)
		{
			centerText = (hideMaxHp ? FormatNumber(targetInfo.CurrentHp) : (FormatNumber(targetInfo.CurrentHp) + " / " + FormatNumber(targetInfo.MaxHp)));
		}
		DrawTargetHealthBar(drawList, pos, width, height, num, fillColor, textScale, string.Empty, centerText, rightText, 0f, 12f * textScale, ImGui.GetColorU32(WithOpacity(GetEffectiveTheme().Text, 0.96f)), ImGui.GetColorU32(WithOpacity(GetEffectiveTheme().Text, 0.96f)), ImGui.GetColorU32(WithOpacity(GetEffectiveTheme().Text, 0.98f)));
	}

	private void DrawTargetHealthBar(ImDrawListPtr drawList, Vector2 pos, float width, float height, float hpRatio, Vector4 fillColor, float textScale, string leftText, string centerText, string rightText, float leftInset, float rightInset, uint centerColor, uint leftColor, uint rightColor)
	{
		Vector2 max = pos + new Vector2(width, height);
		DrawEpicHealthBar(fillMax: new Vector2(pos.X + width * hpRatio, max.Y), drawList: drawList, min: pos, max: max, fill: fillColor, compact: false);
		float fontSize = GetFontSize(textScale);
		float num = 0f - MathF.Round(Math.Clamp(fontSize * 0.08f, 1f, 2f));
		if (!string.IsNullOrWhiteSpace(leftText))
		{
			Vector2 vector = CalcTextSize(leftText, textScale);
			DrawShadowText(drawList, pos + new Vector2(leftInset, (height - vector.Y) * 0.5f + num), leftColor, leftText, fontSize);
		}
		if (!string.IsNullOrWhiteSpace(centerText))
		{
			Vector2 vector2 = CalcTextSize(centerText, textScale);
			DrawShadowText(drawList, pos + new Vector2((width - vector2.X) * 0.5f, (height - vector2.Y) * 0.5f + num), centerColor, centerText, fontSize);
		}
		if (!string.IsNullOrWhiteSpace(rightText))
		{
			Vector2 vector3 = CalcTextSize(rightText, textScale);
			DrawShadowText(drawList, new Vector2(max.X - vector3.X - rightInset, pos.Y + (height - vector3.Y) * 0.5f + num), rightColor, rightText, fontSize);
		}
	}

	private void DrawCapsuleBar(ImDrawListPtr drawList, Vector2 min, Vector2 max, Vector2 fillMax, Vector4 background, Vector4 fill, Vector4 border)
	{
		float num = Math.Max(1f, max.Y - min.Y);
		float num2 = num * 0.5f;
		Vector2 vector = new Vector2(0f, 1f);
		ThemePalette effectiveTheme = GetEffectiveTheme();
		drawList.AddRectFilled(min + vector, max + vector, ImGui.GetColorU32(WithOpacity(effectiveTheme.TextShadow, 0.24f)), num2);
		drawList.AddRectFilled(min, max, ImGui.GetColorU32(background), num2);
		if (fillMax.X > min.X + 1f)
		{
			Vector2 vector2 = new Vector2(Math.Clamp(fillMax.X, min.X, max.X), max.Y);
			drawList.AddRectFilled(min, vector2, ImGui.GetColorU32(fill), num2);
			if (vector2.X - min.X > 3f)
			{
				Vector2 vector3 = new Vector2(vector2.X, min.Y + num * 0.42f);
				Vector2 pMin = new Vector2(min.X, min.Y + num * 0.58f);
				drawList.AddRectFilled(min + new Vector2(1f, 1f), vector3 - new Vector2(1f, 0f), ImGui.GetColorU32(WithOpacity(effectiveTheme.Text, 0.08f)), num2 * 0.75f);
				drawList.AddRectFilled(pMin, vector2 - new Vector2(0f, 1f), ImGui.GetColorU32(WithOpacity(effectiveTheme.TextShadow, 0.1f)), num2 * 0.75f);
			}
		}
		drawList.AddLine(min + new Vector2(num2 * 0.55f, 1f), new Vector2(max.X - num2 * 0.55f, min.Y + 1f), ImGui.GetColorU32(WithOpacity(effectiveTheme.Text, 0.06f)), 1f);
		drawList.AddRect(min, max, ImGui.GetColorU32(WithOpacity(effectiveTheme.Border, border.W * 0.45f)), num2, ImDrawFlags.None, 0.7f);
	}

	private void DrawEpicHealthBar(ImDrawListPtr drawList, Vector2 min, Vector2 max, Vector2 fillMax, Vector4 fill, bool compact)
	{
		float num = Math.Max(1f, max.Y - min.Y);
		float num2 = MathF.Min(4f, num * 0.26f);
		float num3 = (compact ? 2f : 2.5f);
		Vector2 vector = min + new Vector2(num3, num3);
		Vector2 pMax = max - new Vector2(num3, num3);
		float num4 = Math.Max(1f, pMax.Y - vector.Y);
		float num5 = MathF.Min(2.5f, num4 * 0.24f);
		Vector2 vector2 = new Vector2(Math.Clamp(fillMax.X - num3, vector.X, pMax.X), pMax.Y);
		ThemePalette effectiveTheme = GetEffectiveTheme();
		drawList.AddRectFilled(min + new Vector2(0f, 1f), max + new Vector2(0f, 1f), ImGui.GetColorU32(WithOpacity(effectiveTheme.TextShadow, 0.2f)), num2);
		drawList.AddRectFilled(min, max, ImGui.GetColorU32(WithOpacity(effectiveTheme.SurfaceAlt, 0.86f)), num2);
		drawList.AddRect(min + new Vector2(0.5f, 0.5f), max - new Vector2(0.5f, 0.5f), ImGui.GetColorU32(WithOpacity(effectiveTheme.TextShadow, 0.52f)), num2, ImDrawFlags.None, 0.9f);
		drawList.AddRect(min + new Vector2(1.5f, 1.5f), max - new Vector2(1.5f, 1.5f), ImGui.GetColorU32(WithOpacity(effectiveTheme.Text, 0.1f)), num2 * 0.75f, ImDrawFlags.None, 0.6f);
		drawList.AddRectFilled(vector, pMax, ImGui.GetColorU32(WithOpacity(effectiveTheme.Surface, 0.96f)), num5);
		if (vector2.X > vector.X + 1f)
		{
			drawList.AddRectFilled(vector, vector2, ImGui.GetColorU32(fill), num5);
			if (vector2.X - vector.X > 4f)
			{
				Vector2 vector3 = new Vector2(vector2.X, vector.Y + num4 * 0.34f);
				Vector2 pMin = new Vector2(vector.X, vector.Y + num4 * 0.36f);
				Vector2 pMin2 = new Vector2(vector.X, vector.Y + num4 * 0.68f);
				drawList.AddRectFilled(vector + new Vector2(1f, 1f), vector3 - new Vector2(1f, 0f), ImGui.GetColorU32(WithOpacity(effectiveTheme.Text, 0.1f)), num5 * 0.7f);
				drawList.AddRectFilled(pMin, new Vector2(vector2.X, pMin2.Y), ImGui.GetColorU32(new Vector4(1f, 0.16f, 0.12f, compact ? 0.04f : 0.06f)), 0f);
				drawList.AddRectFilled(pMin2, vector2 - new Vector2(0f, 1f), ImGui.GetColorU32(WithOpacity(effectiveTheme.TextShadow, 0.12f)), num5 * 0.7f);
			}
		}
		drawList.AddLine(vector + new Vector2(num5, 1f), new Vector2(pMax.X - num5, vector.Y + 1f), ImGui.GetColorU32(WithOpacity(effectiveTheme.Text, 0.06f)), 1f);
		drawList.AddLine(new Vector2(vector.X + num5, pMax.Y - 1f), new Vector2(pMax.X - num5, pMax.Y - 1f), ImGui.GetColorU32(WithOpacity(effectiveTheme.TextShadow, 0.22f)), 1f);
	}

	private void DrawTargetInfoCastBar(ImDrawListPtr drawList, TargetInfoEntry targetInfo, Vector2 pos, float width, float height, float textScale)
	{
		Vector2 max = pos + new Vector2(width, height);
		float num = ((targetInfo.TotalCastTime > 0f) ? Math.Clamp(targetInfo.CurrentCastTime / targetInfo.TotalCastTime, 0f, 1f) : 0f);
		Vector2 fillMax = new Vector2(pos.X + width * num, max.Y);
		ThemePalette effectiveTheme = GetEffectiveTheme();
		Vector4 fill = new Vector4(0.76f, 0.55f, 0.2f, 0.96f);
		Vector4 border = effectiveTheme.Border;
		DrawCapsuleBar(drawList, pos, max, fillMax, WithOpacity(effectiveTheme.Surface, 0.84f), fill, border);
		float num2 = Math.Max(0f, targetInfo.TotalCastTime - targetInfo.CurrentCastTime);
		string text = ((num2 > 0f) ? $"{num2:00.00}" : string.Empty);
		Vector2 vector = CalcTextSize(text, textScale);
		float maxWidth = Math.Max(30f * textScale, width - vector.X - 12f * textScale);
		string text2 = TruncateTextToWidth(string.IsNullOrWhiteSpace(targetInfo.CastActionName) ? $"Action {targetInfo.CastActionId}" : targetInfo.CastActionName, maxWidth, textScale);
		Vector2 vector2 = CalcTextSize(text2, textScale);
		Vector2 pos2 = new Vector2(pos.X + 6f * textScale, pos.Y + (height - vector2.Y) * 0.5f);
		DrawShadowText(drawList, pos2, ImGui.GetColorU32(WithOpacity(effectiveTheme.Text, 0.98f)), text2, GetFontSize(textScale));
		if (!string.IsNullOrWhiteSpace(text))
		{
			Vector2 pos3 = new Vector2(max.X - vector.X - 6f * textScale, pos.Y + (height - vector.Y) * 0.5f);
			DrawShadowText(drawList, pos3, ImGui.GetColorU32(WithOpacity(effectiveTheme.Text, 0.96f)), text, GetFontSize(textScale));
		}
	}

	private static IReadOnlyList<TargetStatusIconLayout> BuildTargetStatusIconLayouts(IReadOnlyList<StatusEntry> statuses, float width, int maxRows, int maxColumns, float selfAppliedIconSize, float otherIconSize, float iconGap, float statusTimerHeight, bool bottomUp, out float height)
	{
		List<TargetStatusIconLayout> list = new List<TargetStatusIconLayout>();
		height = 0f;
		if (statuses.Count == 0 || width <= 1f || maxRows <= 0)
		{
			return list;
		}
		List<(List<TargetStatusIconLayout> Layouts, float Height)> rows = new List<(List<TargetStatusIconLayout>, float)>();
		List<TargetStatusIconLayout> currentRow = new List<TargetStatusIconLayout>();
		int row = 0;
		float x = 0f;
		float rowHeight = 0f;
		int column = 0;
		for (int i = 0; i < statuses.Count; i++)
		{
			StatusEntry statusEntry = statuses[i];
			float num = (statusEntry.IsSelfApplied ? selfAppliedIconSize : otherIconSize);
			float targetStatusVisualIconSize = GetTargetStatusVisualIconSize(statusEntry, num);
			float val = targetStatusVisualIconSize + statusTimerHeight;
			if (column >= maxColumns || (x > 0f && x + targetStatusVisualIconSize > width))
			{
				CommitRow();
				if (row >= maxRows)
				{
					break;
				}
			}
			float x2 = x - (num - targetStatusVisualIconSize) * 0.5f;
			currentRow.Add(new TargetStatusIconLayout(statusEntry, new Vector2(x2, 0f), num, list.Count + currentRow.Count));
			x += targetStatusVisualIconSize + iconGap;
			rowHeight = Math.Max(rowHeight, val);
			column++;
		}
		CommitRow();
		float num2 = Math.Max(0f, iconGap);
		height = rows.Sum<(List<TargetStatusIconLayout>, float)>(((List<TargetStatusIconLayout> Layouts, float Height) rowEntry) => rowEntry.Height) + (float)Math.Max(0, rows.Count - 1) * num2;
		float y = (bottomUp ? height : 0f);
		foreach (var item in rows)
		{
			y = (bottomUp ? (y - item.Height) : y);
			list.AddRange(item.Layouts.Select((TargetStatusIconLayout layout) => layout with
			{
				Offset = new Vector2(layout.Offset.X, y)
			}));
			y = (bottomUp ? (y - num2) : (y + item.Height + num2));
		}
		return list;
		void CommitRow()
		{
			if (currentRow.Count != 0)
			{
				rows.Add((currentRow, rowHeight));
				currentRow = new List<TargetStatusIconLayout>();
				x = 0f;
				rowHeight = 0f;
				column = 0;
				row++;
			}
		}
	}

	private void DrawTargetInfoStatusLayouts(IReadOnlyList<TargetStatusIconLayout> layouts, Vector2 pos)
	{
		foreach (TargetStatusIconLayout layout in layouts)
		{
			DrawTargetInfoStatusIcon(layout.Status, pos + layout.Offset, layout.Size, layout.Index);
		}
	}

	private void DrawTargetInfoStatuses(IReadOnlyList<StatusEntry> statuses, Vector2 pos, int columns, float iconSize, float iconGap, float rowHeight, int indexOffset)
	{
		for (int i = 0; i < statuses.Count; i++)
		{
			int num = i / columns;
			int num2 = i % columns;
			Vector2 min = pos + new Vector2((float)num2 * (iconSize + iconGap), (float)num * (rowHeight + iconGap));
			DrawTargetInfoStatusIcon(statuses[i], min, iconSize, indexOffset + i);
		}
	}

	private void DrawTargetInfoStatusIcon(StatusEntry status, Vector2 min, float size, int index)
	{
		_ = min + new Vector2(size, size);
		float targetStatusVisualIconSize = GetTargetStatusVisualIconSize(status, size);
		Vector2 vector = min + new Vector2((size - targetStatusVisualIconSize) * 0.5f, 0f);
		Vector2 vector2 = vector + new Vector2(targetStatusVisualIconSize, targetStatusVisualIconSize);
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		if (!DrawGameIconImage(windowDrawList, status.IconId, vector, vector2))
		{
			windowDrawList.AddRectFilled(vector, vector2, ImGui.GetColorU32(WithOpacity(GetEffectiveTheme().SurfaceAlt, 0.82f)), 2f);
		}
		if (ShouldShowStatusTimer(status))
		{
			uint textColor = (status.IsSelfApplied ? ImGui.GetColorU32(config.SelfAppliedTimerColor) : ImGui.GetColorU32(config.OtherAppliedTimerColor));
			uint colorU = ImGui.GetColorU32(WithOpacity(GetEffectiveTheme().TextShadow, 0.96f));
			DrawTargetStatusTimerText(FormatTargetStatusTime(status.RemainingSeconds), vector, vector2, textColor, colorU, targetStatusVisualIconSize);
		}
		ImGui.SetCursorScreenPos(min);
		ImU8String strId = new ImU8String(15, 2);
		strId.AppendLiteral("target_status_");
		strId.AppendFormatted(index);
		strId.AppendLiteral("_");
		strId.AppendFormatted(status.StatusId);
		ImGui.InvisibleButton(strId, new Vector2(size, size));
		if (ImGui.IsItemHovered())
		{
			DrawStatusTooltip(status, showRawStatusId: false);
		}
	}

	private static float GetTargetStatusVisualIconSize(StatusEntry status, float layoutSize)
	{
		return layoutSize + (status.IsSelfApplied ? 4f : 0f);
	}

	private void DrawTargetStatusTimerText(string text, Vector2 min, Vector2 max, uint textColor, uint edgeColor, float iconSize)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return;
		}
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		float fontSize = ImGui.GetFontSize();
		float num = MathF.Round(Math.Clamp(iconSize * 0.5f, fontSize * 0.92f, fontSize * 1.18f));
		_ = num / Math.Max(1f, fontSize);
		using (PushHudFont(num))
		{
			Vector2 vector = ImGui.CalcTextSize(text);
			Vector2 vector2 = new Vector2(MathF.Round(min.X + (max.X - min.X - vector.X) * 0.5f), MathF.Round(max.Y - num * 0.12f));
			windowDrawList.AddText(vector2 + new Vector2(-1f, -1f), edgeColor, text);
			windowDrawList.AddText(vector2 + new Vector2(-1f, 0f), edgeColor, text);
			windowDrawList.AddText(vector2 + new Vector2(-1f, 1f), edgeColor, text);
			windowDrawList.AddText(vector2 + new Vector2(0f, -1f), edgeColor, text);
			windowDrawList.AddText(vector2 + new Vector2(0f, 1f), edgeColor, text);
			windowDrawList.AddText(vector2 + new Vector2(1f, -1f), edgeColor, text);
			windowDrawList.AddText(vector2 + new Vector2(1f, 0f), edgeColor, text);
			windowDrawList.AddText(vector2 + new Vector2(1f, 1f), edgeColor, text);
			windowDrawList.AddText(vector2 + new Vector2(1f, 0f), textColor, text);
			windowDrawList.AddText(vector2, textColor, text);
		}
	}

	private string FormatTargetStatusTime(float seconds)
	{
		int num = Math.Max(0, (int)MathF.Ceiling(seconds));
		if (targetStatusTimeTextCache.TryGetValue(num, out string value))
		{
			return value;
		}
		string text = ((num < 60) ? num.ToString() : $"{Math.Max(1, (int)MathF.Ceiling((float)num / 60f))}m");
		targetStatusTimeTextCache[num] = text;
		return text;
	}

	private void TrackTargetInfoPosition()
	{
		if (config.TargetInfoLocked)
		{
			return;
		}
		Vector2 windowPos = ImGui.GetWindowPos();
		if (windowPos != config.CustomTargetInfoPosition)
		{
			config.CustomTargetInfoPosition = windowPos;
			targetInfoPositionSaveDueAt = DateTime.UtcNow.Add(OverlayPositionSaveDelay);
		}
		DateTime? dateTime = targetInfoPositionSaveDueAt;
		if (dateTime.HasValue)
		{
			DateTime valueOrDefault = dateTime.GetValueOrDefault();
			if (!(DateTime.UtcNow < valueOrDefault) && !(config.CustomTargetInfoPosition == lastSavedTargetInfoPosition))
			{
				saveConfig();
				lastSavedTargetInfoPosition = config.CustomTargetInfoPosition;
				targetInfoPositionSaveDueAt = null;
			}
		}
	}

	private void TrackTargetInfoCastBarPosition()
	{
		if (config.TargetInfoLocked)
		{
			return;
		}
		Vector2 windowPos = ImGui.GetWindowPos();
		if (windowPos != config.CustomTargetInfoCastBarPosition)
		{
			config.CustomTargetInfoCastBarPosition = windowPos;
			targetInfoCastBarPositionSaveDueAt = DateTime.UtcNow.Add(OverlayPositionSaveDelay);
		}
		DateTime? dateTime = targetInfoCastBarPositionSaveDueAt;
		if (dateTime.HasValue)
		{
			DateTime valueOrDefault = dateTime.GetValueOrDefault();
			if (!(DateTime.UtcNow < valueOrDefault) && !(config.CustomTargetInfoCastBarPosition == lastSavedTargetInfoCastBarPosition))
			{
				saveConfig();
				lastSavedTargetInfoCastBarPosition = config.CustomTargetInfoCastBarPosition;
				targetInfoCastBarPositionSaveDueAt = null;
			}
		}
	}

	private void TrackTargetInfoStatusBarPosition()
	{
		if (config.TargetInfoLocked)
		{
			return;
		}
		Vector2 windowPos = ImGui.GetWindowPos();
		if (windowPos != config.CustomTargetInfoStatusBarPosition)
		{
			config.CustomTargetInfoStatusBarPosition = windowPos;
			targetInfoStatusBarPositionSaveDueAt = DateTime.UtcNow.Add(OverlayPositionSaveDelay);
		}
		DateTime? dateTime = targetInfoStatusBarPositionSaveDueAt;
		if (dateTime.HasValue)
		{
			DateTime valueOrDefault = dateTime.GetValueOrDefault();
			if (!(DateTime.UtcNow < valueOrDefault) && !(config.CustomTargetInfoStatusBarPosition == lastSavedTargetInfoStatusBarPosition))
			{
				saveConfig();
				lastSavedTargetInfoStatusBarPosition = config.CustomTargetInfoStatusBarPosition;
				targetInfoStatusBarPositionSaveDueAt = null;
			}
		}
	}

	private void DrawShadowText(ImDrawListPtr drawList, Vector2 pos, uint color, string text, float fontSize = 0f)
	{
		uint colorU = ImGui.GetColorU32(WithOpacity(GetEffectiveTheme().TextShadow, 0.92f));
		if (fontSize <= 0f)
		{
			pos = SnapToPixel(pos);
			drawList.AddText(pos + new Vector2(-1f, 0f), colorU, text);
			drawList.AddText(pos + new Vector2(1f, 0f), colorU, text);
			drawList.AddText(pos + new Vector2(0f, -1f), colorU, text);
			drawList.AddText(pos + new Vector2(0f, 1f), colorU, text);
			drawList.AddText(pos + new Vector2(1f, 1f), colorU, text);
			drawList.AddText(pos, color, text);
			return;
		}
		using (PushHudFont(fontSize))
		{
			pos = SnapToPixel(pos);
			float num = Math.Clamp(MathF.Round(fontSize / Math.Max(1f, ImGui.GetFontSize())), 1f, 2f);
			drawList.AddText(pos + new Vector2(0f - num, 0f), colorU, text);
			drawList.AddText(pos + new Vector2(num, 0f), colorU, text);
			drawList.AddText(pos + new Vector2(0f, 0f - num), colorU, text);
			drawList.AddText(pos + new Vector2(0f, num), colorU, text);
			drawList.AddText(pos + new Vector2(num, num), colorU, text);
			drawList.AddText(pos, color, text);
		}
	}

	private static float GetFontSize(float scale)
	{
		return ImGui.GetFontSize() * Math.Clamp(scale, 0.6f, 2f);
	}

	private Vector2 CalcTextSize(string text, float scale)
	{
		using (PushHudFont(GetFontSize(scale)))
		{
			return ImGui.CalcTextSize(text);
		}
	}

	private string TruncateTextToWidth(string text, float maxWidth, float scale = 1f)
	{
		if (CalcTextSize(text, scale).X <= maxWidth)
		{
			return text;
		}
		string text2 = text;
		while (text2.Length > 0 && CalcTextSize(text2 + "...", scale).X > maxWidth)
		{
			string text3 = text2;
			text2 = text3.Substring(0, text3.Length - 1);
		}
		if (text2.Length != 0)
		{
			return text2 + "...";
		}
		return "...";
	}

	private float GetTaskBarHorizontalOffsetPixels(float workWidth, float width)
	{
		float num = Math.Max(0f, workWidth - width);
		return (Math.Clamp(config.TaskBarHorizontalOffset, 0f, 1f) - 0.5f) * num;
	}

	private void DrawTaskBarWindow(ImGuiWindowFlags flags)
	{
		if (!config.ShowTaskBar)
		{
			return;
		}
		float scale = Math.Clamp(config.TaskBarScale, 0.6f, 2f);
		float opacity = Math.Clamp(config.TaskBarOpacity, 0.15f, 1f);
		Vector2 mainPadding = new Vector2(14f * scale, 9f * scale);
		float mainItemSpacing = 14f * scale;
		Vector2 dtrPadding = new Vector2(16f * scale, 7f * scale);
		float dtrSpacing = 8f * scale;
		float dtrItemHeight = 34f * scale;
		Vector2 dtrOuterPadding = new Vector2(8f * scale, 5f * scale);
		float num = 7f * scale;
		ImGuiViewportPtr mainViewport = ImGui.GetMainViewport();
		TaskBarEdge taskBarEdge = ((config.TaskBarEdge == 1) ? TaskBarEdge.Bottom : TaskBarEdge.Top);
		bool stretchToEdges = config.TaskBarStretchToEdges;
		List<TaskBarItem> dtrItems = BuildDtrTaskBarItems();
		List<TaskBarItem> leftItems = new List<TaskBarItem>();
		List<TaskBarItem> centerItems = new List<TaskBarItem>();
		List<TaskBarItem> rightItems = new List<TaskBarItem>();
		List<TaskBarItem> localItems = (stretchToEdges ? new List<TaskBarItem>() : BuildLocalTaskBarItems(dtrItems));
		if (stretchToEdges)
		{
			leftItems = BuildTaskBarItemsForOrder(config.TaskBarLeftComponentOrder, dtrItems);
			centerItems = BuildTaskBarItemsForOrder(config.TaskBarCenterComponentOrder, dtrItems);
			rightItems = BuildTaskBarItemsForOrder(config.TaskBarRightComponentOrder, dtrItems);
			localItems.AddRange(leftItems);
			localItems.AddRange(centerItems);
			localItems.AddRange(rightItems);
		}
		float num2 = 20f * scale;
		if (!IsHudFontReady(num2) || !AreTaskBarItemFontsReady(localItems, num2))
		{
			return;
		}
		if (config.TaskBarServerInfoBarMode == 1 && dtrItems.Count > 0)
		{
			dtrItems = new List<TaskBarItem>();
		}
		if (localItems.Count == 0 && dtrItems.Count == 0)
		{
			TaskBarItem item = new TaskBarItem("主栏组件未启用", string.Empty, null);
			if (stretchToEdges)
			{
				centerItems.Add(item);
			}
			localItems.Add(item);
		}
		TaskBarRowMetrics leftMetrics;
		TaskBarRowMetrics centerMetrics;
		TaskBarRowMetrics rightMetrics;
		TaskBarRowMetrics mainMetrics;
		TaskBarRowMetrics dtrMetrics;
		Vector2 windowSize;
		ImDrawListPtr drawList;
		Vector2 windowMin;
		using (PushTaskBarFont(scale))
		{
			leftMetrics = CalculateTaskBarMainRowMetrics(leftItems, mainPadding, mainItemSpacing, scale);
			centerMetrics = CalculateTaskBarMainRowMetrics(centerItems, mainPadding, mainItemSpacing, scale);
			rightMetrics = CalculateTaskBarMainRowMetrics(rightItems, mainPadding, mainItemSpacing, scale);
			mainMetrics = (stretchToEdges ? new TaskBarRowMetrics(SnapToPixel(mainViewport.WorkSize.X), MathF.Max(leftMetrics.Height, MathF.Max(centerMetrics.Height, rightMetrics.Height))) : CalculateTaskBarMainRowMetrics(localItems, mainPadding, mainItemSpacing, scale));
			dtrMetrics = CalculateTaskBarDtrRowMetrics(dtrItems, dtrPadding, dtrSpacing, dtrItemHeight, dtrOuterPadding, scale);
			float num3 = Math.Max(mainMetrics.Width, dtrMetrics.Width);
			int num4 = ((mainMetrics.Height > 0f) ? 1 : 0) + ((dtrMetrics.Width > 0f) ? 1 : 0);
			float num5 = mainMetrics.Height + dtrMetrics.Height + ((num4 > 1) ? num : 0f);
			float num6 = 0f;
			float num7 = (stretchToEdges ? mainViewport.WorkSize.X : (num3 + num6 * 2f));
			float num8 = num5 + num6 * 2f;
			windowSize = SnapToPixel(new Vector2(num7, num8));
			float x = (stretchToEdges ? mainViewport.WorkPos.X : (mainViewport.WorkPos.X + Math.Max(0f, (mainViewport.WorkSize.X - num7) * 0.5f) + GetTaskBarHorizontalOffsetPixels(mainViewport.WorkSize.X, num7)));
			Vector2 value = ((taskBarEdge != TaskBarEdge.Bottom) ? new Vector2(x, mainViewport.WorkPos.Y) : new Vector2(x, mainViewport.WorkPos.Y + mainViewport.WorkSize.Y - num8));
			ImGui.SetNextWindowPos(SnapToPixel(value), ImGuiCond.Always);
			ImGui.SetNextWindowSize(windowSize, ImGuiCond.Always);
			ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
			ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
			flags |= ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoBackground;
			if (!ImGui.Begin("AllHud 主栏", flags))
			{
				ImGui.End();
				ImGui.PopStyleVar(2);
				return;
			}
			drawList = ImGui.GetWindowDrawList();
			windowMin = ImGui.GetWindowPos();
			float num9 = SnapToPixel(windowMin.X + Math.Max(num6, (windowSize.X - num3) * 0.5f));
			float num10 = SnapToPixel(windowMin.Y + num6);
			if (taskBarEdge != TaskBarEdge.Bottom)
			{
				Vector2 mainPos = SnapToPixel(new Vector2(num9 + Math.Max(0f, (num3 - mainMetrics.Width) * 0.5f), num10));
				DrawMainSections(mainPos);
				Vector2 rowPos = SnapToPixel(new Vector2(num9 + Math.Max(0f, (num3 - dtrMetrics.Width) * 0.5f), num10 + mainMetrics.Height + ((mainMetrics.Width > 0f && dtrMetrics.Width > 0f) ? num : 0f)));
				DrawDtrRow(rowPos);
			}
			else
			{
				Vector2 rowPos2 = SnapToPixel(new Vector2(num9 + Math.Max(0f, (num3 - dtrMetrics.Width) * 0.5f), num10));
				DrawDtrRow(rowPos2);
				Vector2 mainPos2 = SnapToPixel(new Vector2(num9 + Math.Max(0f, (num3 - mainMetrics.Width) * 0.5f), num10 + dtrMetrics.Height + ((mainMetrics.Width > 0f && dtrMetrics.Width > 0f) ? num : 0f)));
				DrawMainSections(mainPos2);
			}
			DrawMainMenuPopup(opacity, scale);
			DrawQuickMenuPopup(opacity, scale);
			DrawVolumePopup(opacity, scale);
			DrawPluginListPopup(opacity, scale);
			DrawCoordinatesPopup(opacity, scale);
			DrawGearsetSwitcherPopup(opacity, scale);
			DrawCurrencyPopup(opacity, scale);
			ImGui.End();
			ImGui.PopStyleVar(2);
		}
		void DrawDtrItem(TaskBarItem taskBarItem, int index)
		{
			ThemePalette effectiveTheme = GetEffectiveTheme();
			Action<DtrInteractionEvent> onClick = taskBarItem.OnClick;
			bool num11 = onClick != null;
			Vector2 vector = ImGui.CalcTextSize(taskBarItem.Text);
			Vector2 vector2 = SnapToPixel(new Vector2(vector.X + dtrPadding.X * 2f, dtrItemHeight));
			Vector2 vector3 = SnapToPixel(ImGui.GetCursorScreenPos());
			ImU8String strId = new ImU8String(15, 1);
			strId.AppendLiteral("##taskbar_item_");
			strId.AppendFormatted(index);
			ImGui.InvisibleButton(strId, vector2);
			bool flag = ImGui.IsItemHovered();
			bool flag2 = ImGui.IsItemActive();
			if (flag && !string.IsNullOrWhiteSpace(taskBarItem.Tooltip))
			{
				DrawTaskBarTooltip(taskBarItem.Tooltip);
			}
			if (onClick != null && ImGui.IsItemClicked(ImGuiMouseButton.Left))
			{
				onClick(CreateDtrInteractionEvent(MouseClickType.Left));
			}
			if (onClick != null && ImGui.IsItemClicked(ImGuiMouseButton.Right))
			{
				onClick(CreateDtrInteractionEvent(MouseClickType.Right));
			}
			Vector2 vector4 = SnapToPixel(vector3 + vector2);
			float rounding = 6f * scale;
			Vector4 col = WithOpacity(num11 ? effectiveTheme.TaskBarCardAlt : effectiveTheme.TaskBarCard, opacity);
			Vector4 col2 = WithOpacity(num11 ? effectiveTheme.Accent : effectiveTheme.Border, opacity * 0.72f);
			if (!num11 && index % 2 == 1)
			{
				col = WithOpacity(effectiveTheme.TaskBarCardAlt, opacity);
			}
			if (flag)
			{
				col = WithOpacity(effectiveTheme.TaskBarCardHovered, opacity);
				col2 = WithOpacity(effectiveTheme.Accent, opacity * 0.92f);
			}
			if (flag2)
			{
				col = WithOpacity(effectiveTheme.TaskBarCardActive, opacity);
			}
			drawList.AddRectFilled(vector3, vector4, ImGui.GetColorU32(col), rounding);
			drawList.AddRect(vector3, vector4, ImGui.GetColorU32(col2), rounding, ImDrawFlags.None, 1f * scale);
			if (ThemeDrawing.IsLiquidGlass(config.ThemeMode))
			{
				ThemeDrawing.DrawLiquidGlassPanel(drawList, vector3, vector4, rounding, config, opacity * (flag ? 0.52f : 0.3f), drawShadow: false);
			}
			Vector2 pos = SnapToPixel(new Vector2(vector3.X + dtrPadding.X, vector3.Y + Math.Max(0f, (dtrItemHeight - vector.Y) * 0.5f)));
			uint colorU = ImGui.GetColorU32(WithOpacity(effectiveTheme.TaskBarText, opacity));
			DrawTaskBarText(drawList, pos, colorU, taskBarItem.Text, opacity, scale);
		}
		void DrawDtrRow(Vector2 vector)
		{
			ThemePalette effectiveTheme = GetEffectiveTheme();
			if (dtrItems.Count != 0 && !(dtrMetrics.Width <= 0f))
			{
				vector = SnapToPixel(vector);
				TaskBarRowMetrics taskBarRowMetrics = CalculateTaskBarDtrRowMetrics(dtrItems, dtrPadding, dtrSpacing, dtrItemHeight, dtrOuterPadding, scale, useMeasureText: false);
				Vector2 vector2 = SnapToPixel(vector + new Vector2(Math.Max(0f, (dtrMetrics.Width - taskBarRowMetrics.Width) * 0.5f), 0f));
				Vector2 vector3 = SnapToPixel(vector2 + new Vector2(taskBarRowMetrics.Width, dtrMetrics.Height));
				float num11 = (ThemeDrawing.IsLiquidGlass(config.ThemeMode) ? ThemeDrawing.GetLiquidGlassRounding(config, scale) : (8f * scale));
				if (ThemeDrawing.IsLiquidGlass(config.ThemeMode))
				{
					ThemeDrawing.AddLiquidGlassBlur(drawList, vector2, vector3, num11, config, opacity);
				}
				drawList.AddRectFilled(vector2 + new Vector2(0f, 2f * scale), vector3 + new Vector2(0f, 2f * scale), ImGui.GetColorU32(WithOpacity(effectiveTheme.TextShadow, opacity * 0.34f)), num11);
				drawList.AddRectFilled(vector2, vector3, ImGui.GetColorU32(WithOpacity(effectiveTheme.TaskBarBackground, opacity * 0.84f)), num11);
				drawList.AddRectFilledMultiColor(vector2 + new Vector2(1f * scale, 1f * scale), vector3 - new Vector2(1f * scale, Math.Max(1f, dtrMetrics.Height * 0.45f)), ImGui.GetColorU32(WithOpacity(effectiveTheme.TaskBarGradientStart, opacity)), ImGui.GetColorU32(WithOpacity(effectiveTheme.TaskBarGradientEnd, opacity)), ImGui.GetColorU32(WithOpacity(effectiveTheme.TaskBarGradientEnd, opacity * 0.18f)), ImGui.GetColorU32(WithOpacity(effectiveTheme.TaskBarGradientStart, opacity * 0.2f)));
				drawList.AddRect(vector2, vector3, ImGui.GetColorU32(WithOpacity(effectiveTheme.Border, opacity)), num11, ImDrawFlags.None, 1f * scale);
				drawList.AddLine(vector2 + new Vector2(num11, 1f * scale), new Vector2(vector3.X - num11, vector2.Y + 1f * scale), ImGui.GetColorU32(WithOpacity(effectiveTheme.TaskBarGradientStart, opacity * 0.54f)), 1f * scale);
				if (ThemeDrawing.IsLiquidGlass(config.ThemeMode))
				{
					ThemeDrawing.DrawLiquidGlassPanel(drawList, vector2, vector3, num11, config, opacity * 0.82f, drawShadow: false);
				}
				ImGui.SetCursorScreenPos(SnapToPixel(vector2 + dtrOuterPadding));
				for (int i = 0; i < dtrItems.Count; i++)
				{
					if (i > 0)
					{
						ImGui.SameLine(0f, dtrSpacing);
					}
					DrawDtrItem(dtrItems[i], i);
				}
			}
		}
		void DrawMainRow(Vector2 vector, IReadOnlyList<TaskBarItem> rowItems, TaskBarRowMetrics rowMetrics, string idPrefix, bool drawBackground = true)
		{
			if (rowItems.Count != 0 && !(rowMetrics.Width <= 0f))
			{
				vector = SnapToPixel(vector);
				if (drawBackground)
				{
					DrawMainRowBackground(vector, rowMetrics);
				}
				float num11 = vector.X + mainPadding.X;
				for (int i = 0; i < rowItems.Count; i++)
				{
					TaskBarItem item2 = rowItems[i];
					uint colorU = ImGui.GetColorU32(WithOpacity(item2.TextColor ?? GetEffectiveTheme().TaskBarText, opacity));
					Action<DtrInteractionEvent> onClick = item2.OnClick;
					Vector2 value2 = CalcTaskBarItemLayoutSize(item2, scale);
					Vector2 vector2 = CalcTaskBarItemDrawTextSize(item2, scale);
					bool flag = IsTaskBarIconLikeItem(item2);
					Vector2 pos = SnapToPixel(new Vector2(num11 + Math.Max(0f, (value2.X - vector2.X) * 0.5f), vector.Y + Math.Max(0f, (rowMetrics.Height - vector2.Y) * 0.5f)));
					float num12 = MathF.Round(Math.Min(rowMetrics.Height - 8f * scale, 36f * scale));
					Vector2 vector3 = (flag ? SnapToPixel(new Vector2(num11, vector.Y + Math.Max(0f, (rowMetrics.Height - value2.Y) * 0.5f))) : SnapToPixel(new Vector2(num11 - 4f * scale, vector.Y + Math.Max(0f, (rowMetrics.Height - num12) * 0.5f))));
					Vector2 vector4 = (flag ? SnapToPixel(value2) : SnapToPixel(new Vector2(value2.X + 8f * scale, num12)));
					ImGui.SetCursorScreenPos(vector3);
					ImU8String strId = new ImU8String(22, 2);
					strId.AppendLiteral("##taskbar_local_item_");
					strId.AppendFormatted(idPrefix);
					strId.AppendLiteral("_");
					strId.AppendFormatted(i);
					ImGui.InvisibleButton(strId, vector4);
					bool flag2 = ImGui.IsItemHovered();
					bool active = ImGui.IsItemActive();
					DrawTaskBarItemCard(drawList, vector3, vector4, i, flag2, active, onClick != null, flag, opacity, scale);
					if (flag2 && onClick != null)
					{
						if (!string.IsNullOrWhiteSpace(item2.Tooltip))
						{
							DrawTaskBarTooltip(item2.Tooltip);
						}
						if (item2.AdjustVolumeOnWheel)
						{
							float mouseWheel = ImGui.GetIO().MouseWheel;
							if (Math.Abs(mouseWheel) > 0.001f)
							{
								AdjustMasterVolume((mouseWheel > 0f) ? 5 : (-5));
							}
						}
					}
					if (onClick != null && ImGui.IsItemClicked(ImGuiMouseButton.Left))
					{
						onClick(CreateDtrInteractionEvent(MouseClickType.Left));
						OpenTaskBarItemPopup(item2);
					}
					if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
					{
						(item2.OnRightClick ?? onClick)?.Invoke(CreateDtrInteractionEvent(MouseClickType.Right));
					}
					if (item2.DrawIcon != TaskBarDrawIcon.None)
					{
						DrawTaskBarCustomIcon(drawList, pos, vector2.X, item2.DrawIcon, opacity, scale);
					}
					else if (item2.IsDalamudIcon)
					{
						DrawTaskBarDalamudIcon(drawList, pos, vector2.X, opacity, scale);
					}
					else if (!string.IsNullOrWhiteSpace(item2.PluginShortcutInternalName))
					{
						DrawPluginShortcutIcon(item2, pos, vector2.X, scale);
					}
					else if (item2.GameIconId != 0)
					{
						DrawTaskBarGameIconItem(drawList, pos, item2, colorU, opacity, scale);
					}
					else
					{
						using (PushTaskBarItemFont(item2, scale))
						{
							DrawTaskBarText(drawList, pos, colorU, item2.Text, opacity, scale);
						}
					}
					num11 += value2.X;
					if (i < rowItems.Count - 1)
					{
						num11 += mainItemSpacing;
					}
				}
			}
		}
		void DrawMainRowBackground(Vector2 vector, TaskBarRowMetrics rowMetrics)
		{
			if (!(rowMetrics.Width <= 0f) && !(rowMetrics.Height <= 0f))
			{
				vector = SnapToPixel(vector);
				Vector2 value2 = vector;
				Vector2 value3 = vector + new Vector2(rowMetrics.Width, rowMetrics.Height);
				value2 = SnapToPixel(value2);
				value3 = SnapToPixel(value3);
				float rounding = (ThemeDrawing.IsLiquidGlass(config.ThemeMode) ? ThemeDrawing.GetLiquidGlassRounding(config, scale) : (4f * scale));
				ThemePalette effectiveTheme = GetEffectiveTheme();
				if (ThemeDrawing.IsLiquidGlass(config.ThemeMode))
				{
					ThemeDrawing.AddLiquidGlassBlur(drawList, value2, value3, rounding, config, opacity);
				}
				drawList.AddRectFilled(value2 + new Vector2(0f, 1f * scale), value3 + new Vector2(0f, 1f * scale), ImGui.GetColorU32(WithOpacity(effectiveTheme.TextShadow, opacity * 0.32f)), rounding);
				drawList.AddRectFilled(value2, value3, ImGui.GetColorU32(WithOpacity(effectiveTheme.TaskBarBackground, opacity)), rounding);
				drawList.AddRectFilledMultiColor(value2 + new Vector2(1f * scale, 1f * scale), value3 - new Vector2(1f * scale, Math.Max(1f, rowMetrics.Height * 0.42f)), ImGui.GetColorU32(WithOpacity(effectiveTheme.TaskBarGradientStart, opacity)), ImGui.GetColorU32(WithOpacity(effectiveTheme.TaskBarGradientEnd, opacity)), ImGui.GetColorU32(WithOpacity(effectiveTheme.TaskBarGradientEnd, opacity * 0.18f)), ImGui.GetColorU32(WithOpacity(effectiveTheme.TaskBarGradientStart, opacity * 0.2f)));
				drawList.AddRect(value2, value3, ImGui.GetColorU32(WithOpacity(effectiveTheme.Border, opacity)), rounding, ImDrawFlags.None, 1f * scale);
				if (ThemeDrawing.IsLiquidGlass(config.ThemeMode))
				{
					ThemeDrawing.DrawLiquidGlassPanel(drawList, value2, value3, rounding, config, opacity * 0.82f, drawShadow: false);
				}
			}
		}
		void DrawMainSections(Vector2 rowPos3)
		{
			if (!stretchToEdges)
			{
				DrawMainRow(rowPos3, localItems, mainMetrics, "adaptive");
			}
			else
			{
				float height = mainMetrics.Height;
				DrawMainRowBackground(new Vector2(windowMin.X, rowPos3.Y), new TaskBarRowMetrics(windowSize.X, height));
				if (leftItems.Count > 0 && leftMetrics.Width > 0f)
				{
					DrawMainRow(new Vector2(windowMin.X, rowPos3.Y), leftItems, new TaskBarRowMetrics(leftMetrics.Width, height), "left", drawBackground: false);
				}
				if (centerItems.Count > 0 && centerMetrics.Width > 0f)
				{
					DrawMainRow(new Vector2(windowMin.X + Math.Max(0f, (windowSize.X - centerMetrics.Width) * 0.5f), rowPos3.Y), centerItems, new TaskBarRowMetrics(centerMetrics.Width, height), "center", drawBackground: false);
				}
				if (rightItems.Count > 0 && rightMetrics.Width > 0f)
				{
					DrawMainRow(new Vector2(windowMin.X + Math.Max(0f, windowSize.X - rightMetrics.Width), rowPos3.Y), rightItems, new TaskBarRowMetrics(rightMetrics.Width, height), "right", drawBackground: false);
				}
			}
		}
	}

	private void DrawTaskBarItemCard(ImDrawListPtr drawList, Vector2 itemMin, Vector2 itemSize, int index, bool hovered, bool active, bool clickable, bool iconLike, float opacity, float scale)
	{
		Vector2 vector = SnapToPixel(itemMin + itemSize);
		float num = MathF.Min(6f * scale, MathF.Max(3f, itemSize.Y * 0.22f));
		bool num2 = index % 2 == 0;
		ThemePalette effectiveTheme = GetEffectiveTheme();
		Vector4 col = WithOpacity(num2 ? effectiveTheme.TaskBarCard : effectiveTheme.TaskBarCardAlt, opacity);
		Vector4 col2 = WithOpacity(effectiveTheme.Border, opacity * (iconLike ? 0.9f : 0.7f));
		if (hovered)
		{
			col = WithOpacity(effectiveTheme.TaskBarCardHovered, opacity);
			col2 = WithOpacity(effectiveTheme.Accent, opacity * 0.78f);
		}
		if (active)
		{
			col = WithOpacity(effectiveTheme.TaskBarCardActive, opacity);
		}
		drawList.AddRectFilled(itemMin, vector, ImGui.GetColorU32(col), num);
		drawList.AddLine(itemMin + new Vector2(num, 1f * scale), new Vector2(vector.X - num, itemMin.Y + 1f * scale), ImGui.GetColorU32(WithOpacity(effectiveTheme.TaskBarGradientStart, opacity * 0.56f)), Math.Max(1f, scale));
		drawList.AddRect(itemMin, vector, ImGui.GetColorU32(col2), num, ImDrawFlags.None, Math.Max(1f, scale));
		if (ThemeDrawing.IsLiquidGlass(config.ThemeMode))
		{
			ThemeDrawing.DrawLiquidGlassPanel(drawList, itemMin, vector, num, config, opacity * (hovered ? 0.52f : 0.28f), drawShadow: false);
		}
	}

	private void DrawTaskBarText(ImDrawListPtr drawList, Vector2 pos, uint color, string text, float opacity, float scale)
	{
		uint colorU = ImGui.GetColorU32(WithOpacity(GetEffectiveTheme().TextShadow, opacity));
		Vector2 vector = SnapToPixel(pos);
		float textLineHeight = ImGui.GetTextLineHeight();
		if (text.IndexOf('\n', StringComparison.Ordinal) < 0)
		{
			drawList.AddText(vector + new Vector2(0f, Math.Max(1f, SnapToPixel(scale * 0.5f))), colorU, text);
			drawList.AddText(vector, color, text);
			return;
		}
		float x = CalcMultilineTextSize(text).X;
		int num = 0;
		int num2 = 0;
		while (num2 <= text.Length)
		{
			int num3 = text.IndexOf('\n', num2);
			int num4 = ((num3 < 0) ? text.Length : num3);
			int num5 = num2;
			string text2 = text.Substring(num5, num4 - num5);
			float x2 = ImGui.CalcTextSize(text2).X;
			Vector2 vector2 = vector + new Vector2(Math.Max(0f, (x - x2) * 0.5f), textLineHeight * (float)num);
			drawList.AddText(vector2 + new Vector2(0f, Math.Max(1f, SnapToPixel(scale * 0.5f))), colorU, text2);
			drawList.AddText(vector2, color, text2);
			num++;
			if (num3 >= 0)
			{
				num2 = num3 + 1;
				continue;
			}
			break;
		}
	}

	private void DrawTaskBarGameIconItem(ImDrawListPtr drawList, Vector2 pos, TaskBarItem item, uint textColor, float opacity, float scale)
	{
		float num = MathF.Round(GetTaskBarGameIconSize(item, scale));
		using (PushTaskBarItemFont(item, scale))
		{
			Vector2 vector = (string.IsNullOrWhiteSpace(item.Text) ? Vector2.Zero : CalcMultilineTextSize(item.Text));
			float num2 = Math.Max(num, vector.Y);
			Vector2 vector2 = SnapToPixel(pos + new Vector2(0f, Math.Max(0f, (num2 - num) * 0.5f)));
			Vector2 vector3 = vector2 + new Vector2(num, num);
			if (!DrawGameIconImage(drawList, item.GameIconId, vector2, vector3, fillBounds: true, bypassMissingRetry: true))
			{
				ThemePalette effectiveTheme = GetEffectiveTheme();
				drawList.AddRectFilled(vector2, vector3, ImGui.GetColorU32(WithOpacity(effectiveTheme.Accent, opacity * 0.25f)), 4f * scale);
				drawList.AddRect(vector2, vector3, ImGui.GetColorU32(WithOpacity(effectiveTheme.Accent, opacity * 0.55f)), 4f * scale);
			}
			if (!string.IsNullOrWhiteSpace(item.Text))
			{
				Vector2 pos2 = SnapToPixel(new Vector2(vector3.X + 6f * scale, pos.Y + Math.Max(0f, (num2 - vector.Y) * 0.5f)));
				DrawTaskBarText(drawList, pos2, textColor, item.Text, opacity, scale);
			}
		}
	}

	private void DrawTaskBarCustomIcon(ImDrawListPtr drawList, Vector2 pos, float size, TaskBarDrawIcon icon, float opacity, float scale)
	{
		Vector2 center = SnapToPixel(SnapToPixel(pos) + new Vector2(size * 0.5f));
		uint colorU = ImGui.GetColorU32(WithOpacity(GetEffectiveTheme().TaskBarText, opacity));
		switch (icon)
		{
		case TaskBarDrawIcon.MainMenu:
			DrawTaskBarMainMenuGlyph(drawList, center, size, colorU, scale);
			break;
		case TaskBarDrawIcon.Volume:
			DrawVolumeGlyph(drawList, center, size, colorU, scale, GetMasterVolume(), IsMasterVolumeMuted());
			break;
		case TaskBarDrawIcon.PluginList:
			DrawTaskBarPluginListGlyph(drawList, center, size, colorU, scale);
			break;
		case TaskBarDrawIcon.QuickMenu:
			DrawTaskBarQuickMenuGlyph(drawList, center, size, colorU, scale);
			break;
		case TaskBarDrawIcon.Walking:
			DrawWalkingGlyph(drawList, center, size, colorU, scale, IsPlayerWalking());
			break;
		}
	}

	private static void DrawWalkingGlyph(ImDrawListPtr drawList, Vector2 center, float size, uint color, float scale, bool walking)
	{
		float thickness = Math.Max(1.5f, size * 0.085f * scale);
		Vector2 center2 = center + new Vector2(size * 0.04f, (0f - size) * 0.28f);
		drawList.AddCircle(center2, size * 0.105f, color, 14, thickness);
		drawList.AddLine(center + new Vector2(0f, (0f - size) * 0.14f), center + new Vector2((0f - size) * 0.08f, size * 0.12f), color, thickness);
		drawList.AddLine(center + new Vector2((0f - size) * 0.08f, size * 0.12f), center + new Vector2((0f - size) * 0.27f, size * 0.36f), color, thickness);
		drawList.AddLine(center + new Vector2((0f - size) * 0.08f, size * 0.12f), center + new Vector2(size * 0.18f, size * 0.34f), color, thickness);
		drawList.AddLine(center + new Vector2(0f, (0f - size) * 0.13f), center + new Vector2(size * 0.22f, (0f - size) * 0.01f), color, thickness);
		if (walking)
		{
			drawList.AddCircleFilled(center + new Vector2(size * 0.28f, (0f - size) * 0.02f), Math.Max(1f, size * 0.045f), color, 10);
		}
	}

	private unsafe static bool IsPlayerWalking()
	{
		try
		{
			Control* ptr = Control.Instance();
			return ptr != null && ptr->IsWalking;
		}
		catch
		{
			return false;
		}
	}

	private unsafe static void TogglePlayerWalking()
	{
		try
		{
			Control* ptr = Control.Instance();
			if (ptr != null)
			{
				ptr->IsWalking = !ptr->IsWalking;
			}
		}
		catch
		{
		}
	}

	private static void DrawTaskBarMainMenuGlyph(ImDrawListPtr drawList, Vector2 center, float size, uint color, float scale)
	{
		float thickness = Math.Max(1.5f, 1.8f * scale);
		float radius = size * 0.09f;
		float num = size * 0.18f;
		float num2 = size * 0.27f;
		for (int i = 0; i < 8; i++)
		{
			float x = (float)Math.PI * 2f * (float)i / 8f;
			Vector2 vector = new Vector2(MathF.Cos(x), MathF.Sin(x));
			drawList.AddLine(SnapToPixel(center + vector * num), SnapToPixel(center + vector * num2), color, thickness);
		}
		drawList.AddCircle(center, num, color, 32, thickness);
		drawList.AddCircle(center, radius, color, 20, thickness);
	}

	private static void DrawVolumeGlyph(ImDrawListPtr drawList, Vector2 center, float size, uint color, float scale, uint volume, bool muted)
	{
		float thickness = Math.Max(1.5f, 1.8f * scale);
		Vector2[] array = new Vector2[6]
		{
			center + new Vector2((0f - size) * 0.26f, (0f - size) * 0.1f),
			center + new Vector2((0f - size) * 0.12f, (0f - size) * 0.1f),
			center + new Vector2(size * 0.04f, (0f - size) * 0.24f),
			center + new Vector2(size * 0.04f, size * 0.24f),
			center + new Vector2((0f - size) * 0.12f, size * 0.1f),
			center + new Vector2((0f - size) * 0.26f, size * 0.1f)
		};
		drawList.PathClear();
		for (int i = 0; i < 6; i++)
		{
			drawList.PathLineTo(array[i]);
		}
		drawList.PathStroke(color, ImDrawFlags.Closed, thickness);
		if (muted || volume == 0)
		{
			Vector2 vector = center + new Vector2(size * 0.2f, 0f);
			float num = size * 0.08f;
			drawList.AddLine(vector + new Vector2(0f - num, 0f - num), vector + new Vector2(num, num), color, thickness);
			drawList.AddLine(vector + new Vector2(num, 0f - num), vector + new Vector2(0f - num, num), color, thickness);
			return;
		}
		Vector2 center2 = center + new Vector2((0f - size) * 0.04f, 0f);
		drawList.PathClear();
		drawList.PathArcTo(center2, size * 0.18f, -0.7f, 0.7f, 8);
		drawList.PathStroke(color, ImDrawFlags.None, thickness);
		if (volume >= 50)
		{
			drawList.PathClear();
			drawList.PathArcTo(center2, size * 0.28f, -0.6f, 0.6f, 10);
			drawList.PathStroke(color, ImDrawFlags.None, thickness);
		}
	}

	private static void DrawTaskBarPluginListGlyph(ImDrawListPtr drawList, Vector2 center, float size, uint color, float scale)
	{
		float thickness = Math.Max(1.5f, 1.8f * scale);
		Vector2 pMin = center + new Vector2((0f - size) * 0.16f, (0f - size) * 0.08f);
		Vector2 pMax = center + new Vector2(size * 0.16f, size * 0.18f);
		drawList.AddRect(pMin, pMax, color, size * 0.04f, ImDrawFlags.None, thickness);
		float y = (0f - size) * 0.08f;
		float y2 = (0f - size) * 0.24f;
		drawList.AddLine(center + new Vector2((0f - size) * 0.07f, y), center + new Vector2((0f - size) * 0.07f, y2), color, thickness);
		drawList.AddLine(center + new Vector2(size * 0.07f, y), center + new Vector2(size * 0.07f, y2), color, thickness);
		drawList.AddLine(center + new Vector2(0f, size * 0.18f), center + new Vector2(0f, size * 0.28f), color, thickness);
	}

	private static void DrawTaskBarQuickMenuGlyph(ImDrawListPtr drawList, Vector2 center, float size, uint color, float scale)
	{
		float thickness = Math.Max(1.5f, 1.8f * scale);
		Vector2[] array = new Vector2[5]
		{
			center + new Vector2(0f, (0f - size) * 0.28f),
			center + new Vector2(size * 0.12f, (0f - size) * 0.1f),
			center + new Vector2(size * 0.12f, size * 0.16f),
			center + new Vector2((0f - size) * 0.12f, size * 0.16f),
			center + new Vector2((0f - size) * 0.12f, (0f - size) * 0.1f)
		};
		drawList.PathClear();
		for (int i = 0; i < 5; i++)
		{
			drawList.PathLineTo(array[i]);
		}
		drawList.PathStroke(color, ImDrawFlags.Closed, thickness);
		drawList.AddLine(center + new Vector2((0f - size) * 0.12f, size * 0.04f), center + new Vector2((0f - size) * 0.24f, size * 0.16f), color, thickness);
		drawList.AddLine(center + new Vector2((0f - size) * 0.24f, size * 0.16f), center + new Vector2((0f - size) * 0.12f, size * 0.16f), color, thickness);
		drawList.AddLine(center + new Vector2(size * 0.12f, size * 0.04f), center + new Vector2(size * 0.24f, size * 0.16f), color, thickness);
		drawList.AddLine(center + new Vector2(size * 0.24f, size * 0.16f), center + new Vector2(size * 0.12f, size * 0.16f), color, thickness);
		drawList.AddLine(center + new Vector2(0f, size * 0.16f), center + new Vector2(0f, size * 0.26f), color, thickness);
	}

	private void DrawTaskBarDalamudIcon(ImDrawListPtr drawList, Vector2 pos, float size, float opacity, float scale)
	{
		Vector2 center = SnapToPixel(SnapToPixel(pos) + new Vector2(size * 0.5f));
		DrawPluginPlugGlyph(drawList, center, size, opacity, scale);
	}

	private bool TryGetEmbeddedDalamudIconTexture(out IDalamudTextureWrap? texture)
	{
		texture = embeddedDalamudIconTexture;
		if (texture != null)
		{
			return true;
		}
		if (embeddedDalamudIconTextureTask == null)
		{
			embeddedDalamudIconTextureTask = LoadEmbeddedDalamudIconAsync();
			return false;
		}
		if (!embeddedDalamudIconTextureTask.IsCompletedSuccessfully)
		{
			return false;
		}
		embeddedDalamudIconTexture = embeddedDalamudIconTextureTask.Result;
		texture = embeddedDalamudIconTexture;
		return texture != null;
	}

	private async Task<IDalamudTextureWrap?> LoadEmbeddedDalamudIconAsync()
	{
		try
		{
			byte[] array = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAQsSURBVFhHxZbfaxNZFMezLkXdZRUqUkV3NTLVJkMSrIUkahIZ6kNbC6KyXasi9kGIixalv2wSFbfrg/+A4IM+iA+++OSD4pMiqOCDCCIi6kxSY42m02lB2W3JLJ+bpkyvWWstjQcCmTP33HPu9/s9Z67LVWH71OxaaGqe3bJ/3uxTs+tHEi7x2D9YUSWW2+3rltfMm1la3VYrqjSMRX7+5dXxmvFcm3JGXjMvZm1b8durnpqCtXmt24rVanrXylw2rlzj3WioapEZWV0tx8zJFt8sLBjdXrMUfgfjyvVsXLly/491VUbnytfDjWoHp4eCUa16uRlS/HL8nMwMBTaaMXWnOGnPmsLIFk8TkOs9a8aHG9WDIEGBw0H3BtbK8d9sJDGSXsvU/Pv1/vU2/ykCeD/s8p6YLGic5GhhJKqGcm3ePnmfWRtQomoSGinfRPbI5qt6yl/I7/AcgfPcvvozJMx2rL0g1sZqd5AcOnh27kVxzuf/NU4Lp4hLT0XsTHf43tt4/UUKMBIBe6ij4Xym0/0Ajmk7hEYcFIhi4so14p17mjF1Lyg5fZ8ZLfTuQMMAHAOrntTecFLjVNAmsZEMp9+3eftJxDM8p7u8zymU9dBDLAdw7sv6GYfRh13eY4Lfsy02P6DVz7aMcaJ0IvpUJD0VtM2wqpBEFJQI2NAx1L7u3OCf7hsoHlqc0Jsx9fd8s+8wiMiUCBN9Ggps1Af22Jx+tGnZTxQA7HBGMLxChQwra9Pd9Y/hn+TATEtCC7E884MqEHLGFjcIVS3S/2639YE9/5Z4JJhirMiqVYiJ0xvJcB6/M5b3xknfR7QhEEn5JugOvc+dh2s6gDaFVg5Isc744iaxWo0+lv34JqkYA3L5PcMGOrLx0GWRPOm1QErE9a+3M0eVW2JAHfr1EgcrKz6EQ3Wy/0tWgjLdG3zBL3MscAehkoAPDyihi3Sn+4l+srZAUcyGz7gXLabVbZ3mnMHMsLqCk2e6wg/RAx2hpyITiBAqSIKQRTfE1L0gIdRfbhSnE82PZN+XDBjRAqcd7AzcBmrRMcyIrvDDkaCnjnUUg25QPu1ZpGP13embadXLZTXPZPrp1mH9dOu7Un9DH8KkAFBBkMIfVRpoQwoGDeig0GkCnu0HgmFDVzg34RCvB5oEx0zF6REul9HreWkkAv+IEZ7yTbDH1MuvnseTZiRanplb3KrTB8cMKoqQBSYGV2/wBaiANLSw3rlmVsac4NtfeiaB/lfrR5LLswHoxcTs2/S+5Ct9P+RCv9ooANFyIniFDp7LIYn4RjT/digq+WhR8b2YZctPGaIS07I4MW0EKK8pmbiQStcvihFinOu17Fsh5NpGAbK/Yla8pvv3l6OsIkYn0K7frYDiXTKcl/0VM74Fzq6ouM14FZtvk6doRY0J+t3Eh5W7hv0Hk1mQnvHVoeMAAAAASUVORK5CYII=");
			return await textureProvider.CreateFromImageAsync(array, "AllHud Dalamud icon").ConfigureAwait(continueOnCapturedContext: false);
		}
		catch
		{
			return null;
		}
	}

	private static bool TryGetTextureHandle(IDalamudTextureWrap texture, out ImTextureID handle)
	{
		try
		{
			handle = texture.Handle;
			return true;
		}
		catch
		{
			handle = default(ImTextureID);
			return false;
		}
	}

	private void DrawPluginPlugGlyph(ImDrawListPtr drawList, Vector2 center, float size, float opacity, float scale)
	{
		ThemePalette effectiveTheme = GetEffectiveTheme();
		Vector4 text = effectiveTheme.Text;
		Vector4 surface = effectiveTheme.Surface;
		Vector4 accent = effectiveTheme.Accent;
		uint colorU = ImGui.GetColorU32(new Vector4(text.X, text.Y, text.Z, opacity));
		uint colorU2 = ImGui.GetColorU32(new Vector4(surface.X, surface.Y, surface.Z, opacity * 0.92f));
		uint colorU3 = ImGui.GetColorU32(new Vector4(accent.X, accent.Y, accent.Z, opacity * 0.96f));
		float thickness = Math.Max(1.3f, 1.9f * scale);
		Vector2 pMin = center + new Vector2((0f - size) * 0.14f, (0f - size) * 0.08f);
		Vector2 pMax = center + new Vector2(size * 0.14f, size * 0.17f);
		drawList.AddLine(center + new Vector2((0f - size) * 0.07f, (0f - size) * 0.08f), center + new Vector2((0f - size) * 0.07f, (0f - size) * 0.23f), colorU, thickness);
		drawList.AddLine(center + new Vector2(size * 0.07f, (0f - size) * 0.08f), center + new Vector2(size * 0.07f, (0f - size) * 0.23f), colorU, thickness);
		drawList.AddRectFilled(pMin, pMax, colorU2, 3f * scale);
		drawList.AddRect(pMin, pMax, colorU, 3f * scale, ImDrawFlags.None, thickness);
		drawList.AddBezierCubic(center + new Vector2(0f, size * 0.17f), center + new Vector2(0f, size * 0.31f), center + new Vector2(size * 0.16f, size * 0.3f), center + new Vector2(size * 0.24f, size * 0.3f), colorU, thickness);
		drawList.AddCircleFilled(center + new Vector2(size * 0.24f, size * 0.3f), Math.Max(1.2f, size * 0.045f), colorU3, 10);
	}

	private void DrawTaskBarTooltip(string tooltip)
	{
		DrawStyledTooltip(delegate
		{
			ImGui.TextUnformatted(tooltip);
		});
	}

	private void DrawStyledTooltip(System.Action drawContent)
	{
		ThemePalette effectiveTheme = GetEffectiveTheme();
		Vector4 popupBg = effectiveTheme.PopupBg;
		Vector4 text = effectiveTheme.Text;
		Vector4 border = effectiveTheme.Border;
		ImGui.PushStyleColor(ImGuiCol.PopupBg, popupBg);
		ImGui.PushStyleColor(ImGuiCol.Text, text);
		ImGui.PushStyleColor(ImGuiCol.Border, border);
		ImGui.PushStyleColor(ImGuiCol.Separator, border);
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

	private void UpdateTaskBarFpsText()
	{
		DateTime utcNow = DateTime.UtcNow;
		if (!(utcNow < nextTaskBarFpsUpdateAt))
		{
			nextTaskBarFpsUpdateAt = utcNow.AddMilliseconds(500.0);
			taskBarFpsText = $"FPS {Math.Clamp((int)MathF.Round(ImGui.GetIO().Framerate), 0, 999):000}";
		}
	}

	private List<TaskBarItem> BuildLocalTaskBarItems(IReadOnlyList<TaskBarItem> dtrItems)
	{
		return BuildTaskBarItemsForOrder(config.TaskBarComponentOrder, dtrItems);
	}

	private List<TaskBarItem> BuildTaskBarItemsForOrder(IEnumerable<string> componentOrder, IReadOnlyList<TaskBarItem> dtrItems)
	{
		List<TaskBarItem> list = ((componentOrder is ICollection<string> collection) ? new List<TaskBarItem>(collection.Count) : new List<TaskBarItem>(6));
		foreach (string item4 in componentOrder)
		{
			int num;
			switch (item4)
			{
			case "time":
				if (config.TaskBarShowLocalTime || config.TaskBarShowEorzeaTime)
				{
					string taskBarTimeText = GetTaskBarTimeText();
					list.Add(new TaskBarItem(taskBarTimeText, taskBarTimeText, null, null, "", AdjustVolumeOnWheel: false, IsIcon: false, GetTaskBarTimeMeasureText(), IsDalamudIcon: false, 0u, GetTaskBarTimeTextScale()));
					continue;
				}
				goto IL_03fa;
			case "fps":
				if (config.TaskBarShowFps)
				{
					UpdateTaskBarFpsText();
					list.Add(new TaskBarItem(taskBarFpsText, taskBarFpsText, null, null, "", AdjustVolumeOnWheel: false, IsIcon: false, "FPS 000"));
					continue;
				}
				goto IL_03fa;
			case "volume":
				if (config.TaskBarShowVolume)
				{
					list.Add(new TaskBarItem(string.Empty, GetVolumeTaskBarTooltip(), delegate
					{
					}, HandleVolumeTaskBarClick, "AllHud 音量控制", AdjustVolumeOnWheel: true, IsIcon: false, "", IsDalamudIcon: false, 0u, 1f, "", null, TaskBarDrawIcon.Volume));
					continue;
				}
				goto IL_03fa;
			case "main_menu":
				if (config.TaskBarShowMainMenu)
				{
					list.Add(new TaskBarItem(string.Empty, string.Empty, delegate
					{
					}, null, "AllHud 主菜单", AdjustVolumeOnWheel: false, IsIcon: false, "", IsDalamudIcon: false, 0u, 1f, "", null, TaskBarDrawIcon.MainMenu));
					continue;
				}
				goto IL_03fa;
			case "plugin_list":
				if (config.TaskBarShowPluginList)
				{
					list.Add(new TaskBarItem(string.Empty, "左键查看已添加插件，右键打开 Dalamud 插件管理器", delegate
					{
					}, delegate
					{
						commandManager.ProcessCommand("/xlplugins");
					}, "AllHud 插件列表", AdjustVolumeOnWheel: false, IsIcon: false, "", IsDalamudIcon: false, 0u, 1f, "", null, TaskBarDrawIcon.PluginList));
					continue;
				}
				goto IL_03fa;
			default:
				{
					num = 8;
					break;
				}
				IL_03fa:
				num = 5;
				break;
			}
			if (Configuration.IsPluginShortcutComponentId(item4))
			{
				goto IL_0415;
			}
			if (num != 5)
			{
				if (num != 8)
				{
					goto IL_0415;
				}
				num = 9;
			}
			else
			{
				num = 6;
			}
			string componentId = item4;
			if (!Configuration.IsCustomShortcutComponentId(componentId))
			{
				if (num != 6)
				{
					if (num != 9)
					{
						goto IL_0452;
					}
					num = 10;
				}
				else
				{
					num = 7;
				}
				string componentId2 = item4;
				if (!Configuration.IsQuickMenuComponentId(componentId2))
				{
					switch (num)
					{
					case 10:
						switch (item4)
						{
						case "server_info":
							if (config.TaskBarShowServerInfoBar && config.TaskBarServerInfoBarMode == 1)
							{
								list.AddRange(dtrItems);
							}
							break;
						case "inventory":
							if (config.TaskBarShowInventory)
							{
								list.Add(CreateInventoryTaskBarItem(saddlebag: false));
							}
							break;
						case "saddlebag":
							if (config.TaskBarShowSaddlebag)
							{
								list.Add(CreateInventoryTaskBarItem(saddlebag: true));
							}
							break;
						case "teleport":
							if (config.TaskBarShowTeleport)
							{
								list.Add(CreateTeleportTaskBarItem());
							}
							break;
						case "coordinates":
							if (config.TaskBarShowCoordinates)
							{
								list.Add(CreateCoordinatesTaskBarItem());
							}
							break;
						case "gearset_switcher":
							if (config.TaskBarShowGearsetSwitcher)
							{
								list.Add(CreateGearsetSwitcherTaskBarItem());
							}
							break;
						case "currency":
							if (config.TaskBarShowCurrency)
							{
								list.Add(CreateCurrencyTaskBarItem());
							}
							break;
						case "walking_indicator":
							if (config.TaskBarShowWalkingIndicator && (!config.TaskBarWalkingOnlyWhenWalking || IsPlayerWalking()))
							{
								bool flag = IsPlayerWalking();
								list.Add(new TaskBarItem(string.Empty, flag ? "步行中（点击切换跑步）" : "跑步中（点击切换步行）", delegate
								{
									TogglePlayerWalking();
								}, null, "", AdjustVolumeOnWheel: false, IsIcon: false, "", IsDalamudIcon: false, 0u, 1f, "", null, TaskBarDrawIcon.Walking));
							}
							break;
						}
						continue;
					case 7:
						continue;
					}
				}
				if (TryCreateQuickMenuTaskBarItem(componentId2, out var item))
				{
					list.Add(item);
				}
				continue;
			}
			goto IL_0452;
			IL_0452:
			if (TryCreateCustomShortcutTaskBarItem(componentId, out var item2))
			{
				list.Add(item2);
			}
			continue;
			IL_0415:
			if (TryCreatePluginShortcutTaskBarItem(item4, out var item3))
			{
				list.Add(item3);
			}
		}
		return list;
	}

	private List<TaskBarItem> BuildDtrTaskBarItems()
	{
		DateTime utcNow = DateTime.UtcNow;
		if (utcNow < nextDtrTaskBarRefreshAt)
		{
			return cachedDtrTaskBarItems;
		}
		nextDtrTaskBarRefreshAt = utcNow + DtrTaskBarCacheDuration;
		if (!config.TaskBarShowServerInfoBar)
		{
			if (cachedDtrTaskBarItems.Count > 0)
			{
				cachedDtrTaskBarItems = new List<TaskBarItem>();
				cachedDtrTaskBarSnapshots = new List<DtrTaskBarSnapshot>();
			}
			return cachedDtrTaskBarItems;
		}
		BuildDtrTaskBarSnapshots(dtrTaskBarSnapshotBuffer);
		if (DtrSnapshotsEqual(cachedDtrTaskBarSnapshots, dtrTaskBarSnapshotBuffer))
		{
			return cachedDtrTaskBarItems;
		}
		List<TaskBarItem> list = new List<TaskBarItem>(dtrTaskBarSnapshotBuffer.Count);
		foreach (DtrTaskBarSnapshot item in dtrTaskBarSnapshotBuffer)
		{
			list.Add(new TaskBarItem(item.Text, item.Tooltip, item.HasClickAction ? item.OnClick : null, null, "", AdjustVolumeOnWheel: false, IsIcon: false, GetDtrMeasureText(item.Text)));
		}
		cachedDtrTaskBarSnapshots = dtrTaskBarSnapshotBuffer.ToList();
		cachedDtrTaskBarItems = list;
		return cachedDtrTaskBarItems;
	}

	private void BuildDtrTaskBarSnapshots(List<DtrTaskBarSnapshot> snapshots)
	{
		snapshots.Clear();
		IReadOnlyList<IReadOnlyDtrBarEntry> entries = dtrBar.Entries;
		if (snapshots.Capacity < Math.Min(entries.Count, 8))
		{
			snapshots.Capacity = Math.Min(entries.Count, 8);
		}
		foreach (IReadOnlyDtrBarEntry item in entries)
		{
			if (item.Shown && !item.UserHidden)
			{
				string text = item.Text?.ToString() ?? string.Empty;
				if (!string.IsNullOrWhiteSpace(text))
				{
					string text2 = item.Tooltip?.ToString();
					snapshots.Add(new DtrTaskBarSnapshot(item.Title, text, string.IsNullOrWhiteSpace(text2) ? text : text2, item.HasClickAction, item.OnClick));
				}
			}
		}
	}

	private static bool DtrSnapshotsEqual(IReadOnlyList<DtrTaskBarSnapshot> previous, IReadOnlyList<DtrTaskBarSnapshot> current)
	{
		if (previous.Count != current.Count)
		{
			return false;
		}
		for (int i = 0; i < previous.Count; i++)
		{
			if (!previous[i].Equals(current[i]))
			{
				return false;
			}
		}
		return true;
	}

	private static DtrInteractionEvent CreateDtrInteractionEvent(MouseClickType clickType)
	{
		return new DtrInteractionEvent
		{
			ClickType = clickType,
			ModifierKeys = GetDtrModifierKeys(),
			Position = ImGui.GetMousePos(),
			ScrollDirection = MouseScrollDirection.None
		};
	}

	private static ClickModifierKeys GetDtrModifierKeys()
	{
		ClickModifierKeys clickModifierKeys = ClickModifierKeys.None;
		ImGuiIOPtr iO = ImGui.GetIO();
		if (iO.KeyCtrl)
		{
			clickModifierKeys |= ClickModifierKeys.Ctrl;
		}
		if (iO.KeyAlt)
		{
			clickModifierKeys |= ClickModifierKeys.Alt;
		}
		if (iO.KeyShift)
		{
			clickModifierKeys |= ClickModifierKeys.Shift;
		}
		return clickModifierKeys;
	}

	private TaskBarRowMetrics CalculateTaskBarMainRowMetrics(IReadOnlyList<TaskBarItem> items, Vector2 padding, float spacing, float scale)
	{
		if (items.Count == 0)
		{
			return new TaskBarRowMetrics(0f, 0f);
		}
		float num = 0f;
		foreach (TaskBarItem item in items)
		{
			num += CalcTaskBarItemLayoutSize(item, scale).X;
		}
		float num2 = ((items.Count > 1) ? (spacing * (float)(items.Count - 1)) : 0f);
		float num3 = ImGui.CalcTextSize("Hg").Y;
		foreach (TaskBarItem item2 in items)
		{
			num3 = Math.Max(num3, CalcTaskBarItemLayoutSize(item2, scale).Y);
		}
		float val = 50f * scale;
		return new TaskBarRowMetrics(SnapToPixel(num + num2 + padding.X * 2f), SnapToPixel(Math.Max(val, num3 + padding.Y * 2f)));
	}

	private static TaskBarRowMetrics CalculateTaskBarDtrRowMetrics(IReadOnlyList<TaskBarItem> items, Vector2 itemPadding, float spacing, float itemHeight, Vector2 outerPadding, float scale, bool useMeasureText = true)
	{
		if (items.Count == 0)
		{
			return new TaskBarRowMetrics(0f, 0f);
		}
		float num = 0f;
		foreach (TaskBarItem item in items)
		{
			string text = ((useMeasureText && !string.IsNullOrWhiteSpace(item.MeasureText)) ? item.MeasureText : item.Text);
			num += ImGui.CalcTextSize(text).X + itemPadding.X * 2f;
		}
		float num2 = ((items.Count > 1) ? (spacing * (float)(items.Count - 1)) : 0f);
		return new TaskBarRowMetrics(SnapToPixel(num + num2 + outerPadding.X * 2f), SnapToPixel(itemHeight + outerPadding.Y * 2f));
	}

	private static string GetEorzeaTimeText()
	{
		int num = (int)((double)DateTimeOffset.UtcNow.ToUnixTimeSeconds() * 20.5714285714 / 60.0) % 1440;
		int value = num / 60;
		int value2 = num % 60;
		return $"{value:00}:{value2:00}";
	}

	private void OpenTaskBarItemPopup(TaskBarItem item)
	{
		if (!string.IsNullOrWhiteSpace(item.PopupId))
		{
			if (item.PopupId == "AllHud 主菜单")
			{
				mainMenuPopupAnchor = CaptureTaskBarPopupAnchor();
				ImGui.OpenPopup(item.PopupId);
			}
			else if (!string.IsNullOrWhiteSpace(item.QuickMenuComponentId))
			{
				quickMenuPopupAnchor = CaptureTaskBarPopupAnchor();
				activeQuickMenuComponentId = item.QuickMenuComponentId;
				ImGui.OpenPopup(GetQuickMenuPopupId(item.QuickMenuComponentId));
			}
			else if (item.PopupId == "AllHud 音量控制")
			{
				volumePopupAnchor = CaptureTaskBarPopupAnchor();
				ImGui.OpenPopup(item.PopupId);
			}
			else if (item.PopupId == "AllHud 插件列表")
			{
				pluginPopupAnchor = CaptureTaskBarPopupAnchor();
				pendingPluginListPopupOpenFrames = 2;
			}
			else
			{
				simplePopupAnchor = CaptureTaskBarPopupAnchor();
				ImGui.OpenPopup(item.PopupId);
			}
		}
	}

	private static TaskBarPopupAnchor CaptureTaskBarPopupAnchor()
	{
		return new TaskBarPopupAnchor(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
	}

	private static Vector2 GetTaskBarPopupPosition(TaskBarPopupAnchor anchor, Vector2 popupSize, float scale, float fallbackHeight = 0f)
	{
		ImGuiViewportPtr mainViewport = ImGui.GetMainViewport();
		float num = 6f * scale;
		Vector2 vector = new Vector2(Math.Max(1f, popupSize.X), Math.Max(1f, (popupSize.Y > 0f) ? popupSize.Y : fallbackHeight));
		Vector2 value = new Vector2(anchor.Min.X, anchor.Max.Y + num);
		if (value.Y + vector.Y > mainViewport.WorkPos.Y + mainViewport.WorkSize.Y)
		{
			value.Y = anchor.Min.Y - vector.Y - num;
		}
		if (value.X + vector.X > mainViewport.WorkPos.X + mainViewport.WorkSize.X)
		{
			value.X = anchor.Max.X - vector.X;
		}
		value.X = Math.Clamp(value.X, mainViewport.WorkPos.X, mainViewport.WorkPos.X + mainViewport.WorkSize.X - vector.X);
		value.Y = Math.Clamp(value.Y, mainViewport.WorkPos.Y, mainViewport.WorkPos.Y + mainViewport.WorkSize.Y - vector.Y);
		return SnapToPixel(value);
	}

	private static string GetQuickMenuPopupId(string componentId)
	{
		return "AllHud 快捷菜单##" + componentId;
	}

	private void DrawQuickMenuPopup(float opacity, float scale)
	{
		if (string.IsNullOrWhiteSpace(activeQuickMenuComponentId))
		{
			return;
		}
		string quickMenuPopupId = GetQuickMenuPopupId(activeQuickMenuComponentId);
		if (!ImGui.IsPopupOpen(quickMenuPopupId) || !config.QuickMenus.TryGetValue(activeQuickMenuComponentId, out QuickMenuDefinition value) || value == null)
		{
			return;
		}
		List<QuickMenuRuntimeItem> list = BuildQuickMenuRuntimeItems(value);
		float num = MathF.Round(38f * scale);
		float num2 = MathF.Round(240f * scale);
		float num3 = MathF.Round(8f * scale);
		float num4 = MathF.Round(4f * scale);
		float y = MathF.Round(((list.Count > 0) ? ((float)list.Count * num + (float)Math.Max(0, list.Count - 1) * num4) : num) + num3 * 2f);
		ImGui.SetNextWindowPos(GetTaskBarPopupPosition(quickMenuPopupAnchor, new Vector2(num2, y), scale), ImGuiCond.Always);
		ImGui.SetNextWindowSize(new Vector2(num2, y), ImGuiCond.Always);
		ThemePalette effectiveTheme = GetEffectiveTheme();
		ImGui.PushStyleColor(ImGuiCol.PopupBg, WithOpacity(effectiveTheme.PopupBg, opacity));
		ImGui.PushStyleColor(ImGuiCol.Text, effectiveTheme.Text);
		ImGui.PushStyleColor(ImGuiCol.Border, WithOpacity(effectiveTheme.Border, opacity));
		ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(9f * scale, num3));
		ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, ThemeDrawing.IsLiquidGlass(config.ThemeMode) ? ThemeDrawing.GetLiquidGlassRounding(config, scale) : (8f * scale));
		ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(num4, num4));
		if (ImGui.BeginPopup(quickMenuPopupId, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
		{
			DrawLiquidGlassPopupSurface(opacity, scale);
			if (list.Count == 0)
			{
				ImGui.TextDisabled("没有项目");
			}
			else
			{
				for (int i = 0; i < list.Count; i++)
				{
					DrawQuickMenuEntry(list[i].Item, list[i].Label, i, num, num2 - 18f * scale, opacity, scale);
				}
			}
			ImGui.EndPopup();
		}
		ImGui.PopStyleVar(3);
		ImGui.PopStyleColor(3);
	}

	private List<QuickMenuRuntimeItem> BuildQuickMenuRuntimeItems(QuickMenuDefinition menu)
	{
		List<QuickMenuRuntimeItem> list = new List<QuickMenuRuntimeItem>();
		foreach (string item2 in menu.ComponentOrder)
		{
			if (Configuration.IsQuickMenuComponentId(item2))
			{
				continue;
			}
			TaskBarItem item;
			string text;
			switch (Configuration.GetComponentBaseId(item2))
			{
			case "volume":
				item = new TaskBarItem(string.Empty, GetVolumeTaskBarTooltip(), delegate
				{
				}, HandleVolumeTaskBarClick, "AllHud 音量控制", AdjustVolumeOnWheel: true, IsIcon: false, "", IsDalamudIcon: false, 0u, 1f, "", null, TaskBarDrawIcon.Volume);
				text = "音量控制";
				break;
			case "plugin_list":
				item = new TaskBarItem(string.Empty, "左键查看已添加插件，右键打开 Dalamud 插件管理器", delegate
				{
				}, delegate
				{
					commandManager.ProcessCommand("/xlplugins");
				}, "AllHud 插件列表", AdjustVolumeOnWheel: false, IsIcon: false, "", IsDalamudIcon: false, 0u, 1f, "", null, TaskBarDrawIcon.PluginList);
				text = "插件列表";
				break;
			case "inventory":
				item = CreateInventoryTaskBarItem(saddlebag: false);
				text = "背包";
				break;
			case "saddlebag":
				item = CreateInventoryTaskBarItem(saddlebag: true);
				text = "陆行鸟鞍囊";
				break;
			case "teleport":
				item = CreateTeleportTaskBarItem();
				text = "传送";
				break;
			case "coordinates":
				item = CreateCoordinatesTaskBarItem();
				text = "坐标";
				break;
			case "gearset_switcher":
				item = CreateGearsetSwitcherTaskBarItem();
				text = "套装切换";
				break;
			case "currency":
				item = CreateCurrencyTaskBarItem();
				text = "货币";
				break;
			case "walking_indicator":
			{
				bool flag = IsPlayerWalking();
				item = new TaskBarItem(string.Empty, flag ? "步行中（点击切换跑步）" : "跑步中（点击切换步行）", delegate
				{
					TogglePlayerWalking();
				}, null, "", AdjustVolumeOnWheel: false, IsIcon: false, "", IsDalamudIcon: false, 0u, 1f, "", null, TaskBarDrawIcon.Walking);
				text = "步行指示器";
				break;
			}
			case "plugin_shortcut":
				if (!TryCreatePluginShortcutTaskBarItem(item2, out item))
				{
					continue;
				}
				text = item.Tooltip;
				break;
			case "custom_shortcut":
				if (!TryCreateCustomShortcutTaskBarItem(item2, out item))
				{
					continue;
				}
				text = item.Tooltip;
				break;
			default:
				continue;
			}
			list.Add(new QuickMenuRuntimeItem(string.IsNullOrWhiteSpace(text) ? "项目" : text, item));
		}
		return list;
	}

	private void DrawQuickMenuEntry(TaskBarItem item, string label, int index, float rowHeight, float rowWidth, float opacity, float scale)
	{
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		Vector2 vector = SnapToPixel(ImGui.GetCursorScreenPos());
		Vector2 vector2 = SnapToPixel(new Vector2(rowWidth, rowHeight));
		ImU8String strId = new ImU8String(17, 1);
		strId.AppendLiteral("##QuickMenuEntry_");
		strId.AppendFormatted(index);
		ImGui.InvisibleButton(strId, vector2);
		bool flag = ImGui.IsItemHovered();
		bool active = ImGui.IsItemActive();
		if (flag && item.OnClick != null && !string.IsNullOrWhiteSpace(item.Tooltip))
		{
			DrawTaskBarTooltip(item.Tooltip);
		}
		DrawTaskBarItemCard(windowDrawList, vector, vector2, index, flag, active, item.OnClick != null, iconLike: false, opacity, scale);
		float num = MathF.Round(28f * scale);
		Vector2 vector3 = SnapToPixel(vector + new Vector2(8f * scale, Math.Max(0f, (rowHeight - num) * 0.5f)));
		Vector2 vector4 = vector3 + new Vector2(num, num);
		ThemePalette effectiveTheme = GetEffectiveTheme();
		uint colorU = ImGui.GetColorU32(WithOpacity(effectiveTheme.Text, opacity));
		if (item.DrawIcon != TaskBarDrawIcon.None)
		{
			DrawTaskBarCustomIcon(windowDrawList, vector3, num, item.DrawIcon, opacity, scale);
		}
		else if (item.IsDalamudIcon)
		{
			DrawTaskBarDalamudIcon(windowDrawList, vector3, num, opacity, scale);
		}
		else if (!string.IsNullOrWhiteSpace(item.PluginShortcutInternalName))
		{
			DrawPluginShortcutIcon(item, vector3, num, scale);
		}
		else if (item.GameIconId != 0)
		{
			if (!DrawGameIconImage(windowDrawList, item.GameIconId, vector3, vector4, fillBounds: true, bypassMissingRetry: true))
			{
				windowDrawList.AddRect(vector3, vector4, colorU, 4f * scale);
			}
		}
		else
		{
			windowDrawList.AddCircle(vector3 + new Vector2(num * 0.5f), num * 0.32f, colorU, 18, Math.Max(1f, 1.5f * scale));
		}
		Vector2 pos = SnapToPixel(new Vector2(vector4.X + 8f * scale, vector.Y + Math.Max(0f, (rowHeight - ImGui.GetTextLineHeight()) * 0.5f)));
		windowDrawList.AddText(pos, ImGui.GetColorU32(WithOpacity(effectiveTheme.Text, opacity)), label);
		if (item.OnClick != null && ImGui.IsItemClicked(ImGuiMouseButton.Left))
		{
			item.OnClick(CreateDtrInteractionEvent(MouseClickType.Left));
			OpenTaskBarItemPopup(item);
			if (string.IsNullOrWhiteSpace(item.PopupId) && string.IsNullOrWhiteSpace(item.QuickMenuComponentId))
			{
				ImGui.CloseCurrentPopup();
			}
		}
		if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
		{
			(item.OnRightClick ?? item.OnClick)?.Invoke(CreateDtrInteractionEvent(MouseClickType.Right));
			if (item.OnRightClick != null)
			{
				ImGui.CloseCurrentPopup();
			}
		}
	}

	private string GetTaskBarTimeText()
	{
		bool taskBarShowLocalTime = config.TaskBarShowLocalTime;
		bool taskBarShowEorzeaTime = config.TaskBarShowEorzeaTime;
		if (taskBarShowLocalTime & taskBarShowEorzeaTime)
		{
			return $"LT {DateTime.Now:HH:mm}\nET {GetEorzeaTimeText()}";
		}
		if (taskBarShowLocalTime)
		{
			return $"LT {DateTime.Now:HH:mm}";
		}
		if (!taskBarShowEorzeaTime)
		{
			return string.Empty;
		}
		return "ET " + GetEorzeaTimeText();
	}

	private string GetTaskBarTimeMeasureText()
	{
		bool taskBarShowLocalTime = config.TaskBarShowLocalTime;
		bool taskBarShowEorzeaTime = config.TaskBarShowEorzeaTime;
		if (taskBarShowLocalTime & taskBarShowEorzeaTime)
		{
			return "LT 00:00\nET 00:00";
		}
		if (taskBarShowLocalTime)
		{
			return "LT 00:00";
		}
		if (!taskBarShowEorzeaTime)
		{
			return string.Empty;
		}
		return "ET 00:00";
	}

	private static string GetDtrMeasureText(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return string.Empty;
		}
		StringBuilder stringBuilder = new StringBuilder(text.Length + 8);
		int num = 0;
		foreach (char c in text)
		{
			if (char.IsDigit(c))
			{
				num++;
				continue;
			}
			if (num > 0)
			{
				stringBuilder.Append('0', Math.Max(4, num));
				num = 0;
			}
			stringBuilder.Append(c);
		}
		if (num > 0)
		{
			stringBuilder.Append('0', Math.Max(4, num));
		}
		return stringBuilder.ToString();
	}

	private float GetTaskBarTimeTextScale()
	{
		if (!config.TaskBarShowLocalTime || !config.TaskBarShowEorzeaTime)
		{
			return 1f;
		}
		return 0.82f;
	}

	private TaskBarItem CreateInventoryTaskBarItem(bool saddlebag)
	{
		(int Used, int Total) tuple = GetCachedInventoryUsage(saddlebag);
		int item = tuple.Used;
		int item2 = tuple.Total;
		string value = (saddlebag ? "鞍囊" : "背包");
		return new TaskBarItem(string.Empty, $"{value} {item}/{item2}", delegate
		{
			OpenInventoryWindow(saddlebag);
		}, null, "", AdjustVolumeOnWheel: false, IsIcon: false, "", IsDalamudIcon: false, GetInventoryIconId(saddlebag, item, item2));
	}

	private TaskBarItem CreateTeleportTaskBarItem()
	{
		return new TaskBarItem(string.Empty, "打开游戏传送窗口", delegate
		{
			OpenTeleportWindow();
		}, null, "", AdjustVolumeOnWheel: false, IsIcon: false, "", IsDalamudIcon: false, GetGeneralActionIconId(7u));
	}

	private uint GetGeneralActionIconId(uint actionId)
	{
		try
		{
			return (uint)dataManager.GetExcelSheet<GeneralAction>().GetRow(actionId).Icon;
		}
		catch
		{
			return 0u;
		}
	}

	private TaskBarItem CreateCoordinatesTaskBarItem()
	{
		string coordinatesText = GetCoordinatesText();
		return new TaskBarItem(coordinatesText, coordinatesText + "\n左键打开地图", delegate
		{
			OpenMapWindow();
		}, null, "", AdjustVolumeOnWheel: false, IsIcon: false, GetCoordinatesMeasureText(), IsDalamudIcon: false, 0u, 0.88f);
	}

	private TaskBarItem CreateGearsetSwitcherTaskBarItem()
	{
		GearsetDisplayInfo? displayInfo = GetCachedGearsetDisplayInfo();
		return new TaskBarItem(GetGearsetTaskBarText(displayInfo), "左键打开套装切换列表", delegate
		{
		}, null, "AllHud 套装", AdjustVolumeOnWheel: false, IsIcon: false, GetGearsetMeasureText(displayInfo), IsDalamudIcon: false, displayInfo.HasValue ? GetGearsetIconId(displayInfo.Value.ClassJobId) : 0u);
	}

	private TaskBarItem CreateCurrencyTaskBarItem()
	{
		CurrencyDisplayInfo selectedCurrencyDisplayInfo = GetSelectedCurrencyDisplayInfo();
		long num = GetCachedCurrencyCount(selectedCurrencyDisplayInfo.ItemId);
		string text = ((num >= 0) ? $"{num:N0}" : "--");
		long currencyCapacity = GetCurrencyCapacity(selectedCurrencyDisplayInfo.ItemId);
		string text2 = ((currencyCapacity > 0 && config.TaskBarCurrencyShowCap) ? $" / {currencyCapacity:N0}" : string.Empty);
		string text3 = (config.TaskBarCurrencyShowWeeklyCap ? GetCurrencyWeeklyText(selectedCurrencyDisplayInfo.ItemId) : string.Empty);
		return new TaskBarItem(config.TaskBarCurrencyShowName ? $"{selectedCurrencyDisplayInfo.Name} {text}{text2}{text3}" : (text + text2 + text3), TextColor: GetCurrencyValueColor(selectedCurrencyDisplayInfo.ItemId, num, currencyCapacity), Tooltip: $"{selectedCurrencyDisplayInfo.Name}：{text}{text2}{text3}", OnClick: delegate
		{
		}, OnRightClick: delegate
		{
			OpenCurrenciesWindow();
		}, PopupId: "AllHud 货币", AdjustVolumeOnWheel: false, IsIcon: false, MeasureText: config.TaskBarCurrencyShowName ? (selectedCurrencyDisplayInfo.Name + " 000,000,000 / 000,000") : "000,000,000 / 000,000", IsDalamudIcon: false, GameIconId: selectedCurrencyDisplayInfo.IconId);
	}

	private bool TryCreateQuickMenuTaskBarItem(string componentId, out TaskBarItem item)
	{
		item = default(TaskBarItem);
		if (!config.QuickMenus.TryGetValue(componentId, out QuickMenuDefinition value) || value == null)
		{
			return false;
		}
		string tooltip = (string.IsNullOrWhiteSpace(value.Name) ? "快捷菜单" : value.Name.Trim());
		item = new TaskBarItem(string.Empty, tooltip, delegate
		{
		}, null, GetQuickMenuPopupId(componentId), AdjustVolumeOnWheel: false, IsIcon: false, "", IsDalamudIcon: false, value.IconId, 1f, "", null, (value.IconId == 0) ? TaskBarDrawIcon.QuickMenu : TaskBarDrawIcon.None, componentId);
		return true;
	}

	private bool TryCreatePluginShortcutTaskBarItem(string componentId, out TaskBarItem item)
	{
		item = default(TaskBarItem);
		string pluginShortcutInternalName = GetPluginShortcutInternalName(componentId);
		if (string.IsNullOrWhiteSpace(pluginShortcutInternalName))
		{
			return false;
		}
		IExposedPlugin plugin = FindInstalledPlugin(pluginShortcutInternalName);
		if (plugin == null)
		{
			return false;
		}
		item = new TaskBarItem(string.Empty, plugin.Name, delegate
		{
			OpenPluginShortcut(plugin);
		}, delegate
		{
			commandManager.ProcessCommand("/xlplugins");
		}, "", AdjustVolumeOnWheel: false, IsIcon: false, "", IsDalamudIcon: false, 0u, 1f, plugin.InternalName, plugin);
		return true;
	}

	private bool TryCreateCustomShortcutTaskBarItem(string componentId, out TaskBarItem item)
	{
		item = default(TaskBarItem);
		if (!config.CustomShortcuts.TryGetValue(componentId, out CustomShortcutDefinition value) || value == null)
		{
			return false;
		}
		string text = (string.IsNullOrWhiteSpace(value.Name) ? "快捷方式" : value.Name.Trim());
		string command = value.Command?.Trim() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(command))
		{
			return false;
		}
		item = new TaskBarItem((value.IconId == 0) ? text : string.Empty, text, delegate
		{
			ExecuteCustomShortcut(command);
		}, null, "", AdjustVolumeOnWheel: false, IsIcon: false, GameIconId: value.IconId, MeasureText: (value.IconId == 0) ? text : string.Empty);
		return true;
	}

	private void ExecuteCustomShortcut(string commandText)
	{
		string[] array = commandText.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
		for (int i = 0; i < array.Length; i++)
		{
			string text = array[i].Trim();
			if (text.Length != 0)
			{
				commandManager.ProcessCommand(text.StartsWith("/", StringComparison.Ordinal) ? text : ("/" + text));
			}
		}
	}

	private string GetPluginShortcutInternalName(string componentId)
	{
		if (!config.PluginShortcutInternalNames.TryGetValue(componentId, out string value))
		{
			return config.TaskBarPluginShortcutInternalName;
		}
		return value;
	}

	private IExposedPlugin? FindInstalledPlugin(string internalName)
	{
		if (DateTime.UtcNow >= nextPluginLookupRefreshAt)
		{
			installedPluginLookupCache.Clear();
			foreach (IExposedPlugin installedPlugin in pluginInterface.InstalledPlugins)
			{
				if (!string.IsNullOrWhiteSpace(installedPlugin.InternalName) && (!installedPluginLookupCache.TryGetValue(installedPlugin.InternalName, out IExposedPlugin value) || IsPreferredPluginCandidate(installedPlugin, value)))
				{
					installedPluginLookupCache[installedPlugin.InternalName] = installedPlugin;
				}
			}
			nextPluginLookupRefreshAt = DateTime.UtcNow + PluginLookupCacheDuration;
		}
		if (!installedPluginLookupCache.TryGetValue(internalName, out IExposedPlugin value2))
		{
			return null;
		}
		return value2;
	}

	private void OpenPluginShortcut(IExposedPlugin plugin)
	{
		try
		{
			if (plugin.HasMainUi)
			{
				plugin.OpenMainUi();
			}
			else if (plugin.HasConfigUi)
			{
				plugin.OpenConfigUi();
			}
			else
			{
				commandManager.ProcessCommand("/xlplugins");
			}
		}
		catch
		{
			commandManager.ProcessCommand("/xlplugins");
		}
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

	private void DrawPluginShortcutIcon(TaskBarItem item, Vector2 pos, float size, float scale)
	{
		IExposedPlugin exposedPlugin = item.PluginShortcutPlugin ?? FindInstalledPlugin(item.PluginShortcutInternalName);
		if (exposedPlugin != null)
		{
			DrawPluginListIconImage(exposedPlugin, pos, size, scale, MathF.Round(36f * scale));
		}
	}

	private static uint GetInventoryIconId(bool saddlebag, int used, int total)
	{
		if (total > 0)
		{
			int num = total - used;
			if (num <= 1)
			{
				return 60074u;
			}
			if (num <= 6)
			{
				return 60073u;
			}
		}
		if (!saddlebag)
		{
			return 2u;
		}
		return 74u;
	}

	private void OpenInventoryWindow(bool saddlebag)
	{
		if (!saddlebag)
		{
			OpenPlayerInventory();
			return;
		}
		uint? num = saddlebagMainCommandId ?? (saddlebagMainCommandId = FindMainCommandId("陆行鸟鞍囊", "鞍囊", "陆行鸟背包", "Saddlebag"));
		if (num.HasValue)
		{
			ExecuteMainCommand(num.Value);
		}
	}

	private void OpenMapWindow()
	{
		uint? num = mapMainCommandId ?? (mapMainCommandId = FindMainCommandId("地图", "Map"));
		if (num.HasValue)
		{
			ExecuteMainCommand(num.Value);
		}
	}

	private unsafe static void OpenPlayerInventory()
	{
		UIModule* ptr = UIModule.Instance();
		if (ptr != null)
		{
			if (ptr->IsInventoryOpen())
			{
				ptr->CloseInventory();
			}
			else
			{
				ptr->OpenInventory(0);
			}
		}
	}

	private uint? FindMainCommandId(params string[] names)
	{
		foreach (MainCommand item in dataManager.GetExcelSheet<MainCommand>())
		{
			string displayName = GetMainCommandDisplayName(item.RowId, GetExcelText(item.Name.ExtractText()));
			if (!string.IsNullOrWhiteSpace(displayName) && names.Any((string name) => displayName.Contains(name, StringComparison.OrdinalIgnoreCase)))
			{
				return item.RowId;
			}
		}
		return null;
	}

	private unsafe static void ExecuteMainCommand(uint commandId)
	{
		UIModule* ptr = UIModule.Instance();
		if (ptr != null)
		{
			ptr->ExecuteMainCommand(commandId);
		}
	}

	private (int Used, int Total) GetInventoryUsage(ReadOnlySpan<GameInventoryType> inventoryTypes)
	{
		int num = 0;
		int num2 = 0;
		ReadOnlySpan<GameInventoryType> readOnlySpan = inventoryTypes;
		for (int i = 0; i < readOnlySpan.Length; i++)
		{
			GameInventoryType type = readOnlySpan[i];
			try
			{
				ReadOnlySpan<GameInventoryItem> inventoryItems = gameInventory.GetInventoryItems(type);
				for (int j = 0; j < inventoryItems.Length; j++)
				{
					GameInventoryItem gameInventoryItem = inventoryItems[j];
					num2++;
					if (!gameInventoryItem.IsEmpty)
					{
						num++;
					}
				}
			}
			catch
			{
			}
		}
		return (Used: num, Total: num2);
	}

	private (int Used, int Total) GetCachedInventoryUsage(bool saddlebag)
	{
		DateTime utcNow = DateTime.UtcNow;
		if (utcNow >= nextInventoryUsageRefreshAt)
		{
			cachedInventoryUsage = GetInventoryUsage(new GameInventoryType[4]
			{
				GameInventoryType.Inventory1,
				GameInventoryType.Inventory2,
				GameInventoryType.Inventory3,
				GameInventoryType.Inventory4
			});
			cachedSaddlebagUsage = GetInventoryUsage(new GameInventoryType[4]
			{
				GameInventoryType.SaddleBag1,
				GameInventoryType.SaddleBag2,
				GameInventoryType.PremiumSaddleBag1,
				GameInventoryType.PremiumSaddleBag2
			});
			nextInventoryUsageRefreshAt = utcNow + InventoryUsageCacheDuration;
		}
		if (!saddlebag)
		{
			return cachedInventoryUsage;
		}
		return cachedSaddlebagUsage;
	}

	private string GetCoordinatesText()
	{
		DateTime utcNow = DateTime.UtcNow;
		if (cachedCoordinatesText != null && utcNow < nextCoordinatesTextRefreshAt)
		{
			return cachedCoordinatesText;
		}
		cachedCoordinatesText = BuildCoordinatesText();
		nextCoordinatesTextRefreshAt = utcNow + CoordinatesTextCacheDuration;
		return cachedCoordinatesText;
	}

	private string BuildCoordinatesText()
	{
		bool flag = config.TaskBarShowCoordinatesTerritory || !config.TaskBarShowCoordinatesPosition;
		bool flag2 = config.TaskBarShowCoordinatesPosition || !config.TaskBarShowCoordinatesTerritory;
		string territoryName = GetTerritoryName();
		IPlayerCharacter localPlayer = objectTable.LocalPlayer;
		string text = ((localPlayer == null) ? "坐标 --" : $"X{localPlayer.Position.X:0} · Y{localPlayer.Position.Y:0} · Z{localPlayer.Position.Z:0}");
		if (flag & flag2)
		{
			return territoryName + "\n" + text;
		}
		if (flag)
		{
			return territoryName;
		}
		if (flag2)
		{
			return text;
		}
		return territoryName;
	}

	private string GetCoordinatesMeasureText()
	{
		bool flag = config.TaskBarShowCoordinatesTerritory || !config.TaskBarShowCoordinatesPosition;
		bool flag2 = config.TaskBarShowCoordinatesPosition || !config.TaskBarShowCoordinatesTerritory;
		string territoryName = GetTerritoryName();
		if (flag & flag2)
		{
			return territoryName + "\nX000 · Y000 · Z000";
		}
		if (!flag)
		{
			return "X000 · Y000 · Z000";
		}
		return territoryName;
	}

	private string GetTerritoryName()
	{
		uint territoryType = clientState.TerritoryType;
		if (territoryType == cachedTerritoryNameId)
		{
			return cachedTerritoryName;
		}
		cachedTerritoryName = ResolveTerritoryName(territoryType);
		cachedTerritoryNameId = territoryType;
		return cachedTerritoryName;
	}

	private string ResolveTerritoryName(uint territoryId)
	{
		if (territoryId == 0)
		{
			return "区域 --";
		}
		try
		{
			if (dataManager.GetExcelSheet<TerritoryType>().TryGetRow(territoryId, out var row))
			{
				string text = row.PlaceName.ValueNullable?.Name.ExtractText();
				if (!string.IsNullOrWhiteSpace(text))
				{
					return text;
				}
			}
		}
		catch
		{
		}
		return $"区域 #{territoryId}";
	}

	private string GetGearsetTaskBarText(GearsetDisplayInfo? displayInfo = null)
	{
		if (displayInfo.HasValue)
		{
			GearsetTaskBarCache? gearsetTaskBarCache = this.gearsetTaskBarCache;
			GearsetJobMetadata gearsetJobMetadata = GetGearsetJobMetadata(displayInfo.Value.ClassJobId);
			short gearsetJobLevel = GetGearsetJobLevel(gearsetJobMetadata);
			if (gearsetTaskBarCache.HasValue)
			{
				GearsetTaskBarCache valueOrDefault = gearsetTaskBarCache.GetValueOrDefault();
				if (valueOrDefault.GearsetId == displayInfo.Value.GearsetId && valueOrDefault.ClassJobId == displayInfo.Value.ClassJobId && valueOrDefault.JobLevel == gearsetJobLevel && valueOrDefault.ItemLevel == displayInfo.Value.ItemLevel && valueOrDefault.ShowNumber == config.TaskBarGearsetShowNumber && valueOrDefault.ShowName == config.TaskBarGearsetShowName && valueOrDefault.ShowLevel == config.TaskBarGearsetShowLevel && valueOrDefault.ShowItemLevel == config.TaskBarGearsetShowItemLevel)
				{
					return valueOrDefault.Text;
				}
			}
			string text = BuildGearsetTaskBarJobText(displayInfo.Value, gearsetJobMetadata, gearsetJobLevel);
			string measureText = BuildGearsetTaskBarJobMeasureText(displayInfo.Value, gearsetJobMetadata, gearsetJobLevel);
			this.gearsetTaskBarCache = new GearsetTaskBarCache(displayInfo.Value.GearsetId, displayInfo.Value.ClassJobId, gearsetJobLevel, displayInfo.Value.ItemLevel, config.TaskBarGearsetShowNumber, config.TaskBarGearsetShowName, config.TaskBarGearsetShowLevel, config.TaskBarGearsetShowItemLevel, text, measureText, gearsetJobMetadata.IconId);
			return text;
		}
		return "套装切换";
	}

	private string GetGearsetMeasureText(GearsetDisplayInfo? displayInfo = null)
	{
		if (displayInfo.HasValue)
		{
			GearsetTaskBarCache? gearsetTaskBarCache = this.gearsetTaskBarCache;
			GearsetJobMetadata gearsetJobMetadata = GetGearsetJobMetadata(displayInfo.Value.ClassJobId);
			short gearsetJobLevel = GetGearsetJobLevel(gearsetJobMetadata);
			if (gearsetTaskBarCache.HasValue)
			{
				GearsetTaskBarCache valueOrDefault = gearsetTaskBarCache.GetValueOrDefault();
				if (valueOrDefault.GearsetId == displayInfo.Value.GearsetId && valueOrDefault.ClassJobId == displayInfo.Value.ClassJobId && valueOrDefault.JobLevel == gearsetJobLevel && valueOrDefault.ItemLevel == displayInfo.Value.ItemLevel && valueOrDefault.ShowNumber == config.TaskBarGearsetShowNumber && valueOrDefault.ShowName == config.TaskBarGearsetShowName && valueOrDefault.ShowLevel == config.TaskBarGearsetShowLevel && valueOrDefault.ShowItemLevel == config.TaskBarGearsetShowItemLevel)
				{
					return valueOrDefault.MeasureText;
				}
			}
			string text = BuildGearsetTaskBarJobText(displayInfo.Value, gearsetJobMetadata, gearsetJobLevel);
			string text2 = BuildGearsetTaskBarJobMeasureText(displayInfo.Value, gearsetJobMetadata, gearsetJobLevel);
			this.gearsetTaskBarCache = new GearsetTaskBarCache(displayInfo.Value.GearsetId, displayInfo.Value.ClassJobId, gearsetJobLevel, displayInfo.Value.ItemLevel, config.TaskBarGearsetShowNumber, config.TaskBarGearsetShowName, config.TaskBarGearsetShowLevel, config.TaskBarGearsetShowItemLevel, text, text2, gearsetJobMetadata.IconId);
			return text2;
		}
		return "职业名 套装 00";
	}

	private string BuildGearsetTaskBarJobText(GearsetDisplayInfo displayInfo, GearsetJobMetadata metadata, short jobLevel)
	{
		string name = metadata.Name;
		string text = ((config.TaskBarGearsetShowLevel && jobLevel > 0) ? $"Lv {jobLevel}" : string.Empty);
		string text2 = ((config.TaskBarGearsetShowItemLevel && displayInfo.ItemLevel > 0) ? $"装等 {displayInfo.ItemLevel}" : string.Empty);
		bool num = config.TaskBarGearsetShowName && !string.IsNullOrWhiteSpace(name);
		bool taskBarGearsetShowNumber = config.TaskBarGearsetShowNumber;
		string text3 = (num ? name : string.Empty);
		string text4 = (taskBarGearsetShowNumber ? $"套装 {displayInfo.GearsetId + 1:00}" : string.Empty);
		return string.Join(' ', new string[4] { text, text2, text3, text4 }.Where((string value) => !string.IsNullOrWhiteSpace(value)));
	}

	private string BuildGearsetTaskBarJobMeasureText(GearsetDisplayInfo displayInfo, GearsetJobMetadata metadata, short jobLevel)
	{
		string text = (string.IsNullOrWhiteSpace(metadata.Name) ? "职业名" : metadata.Name);
		string text2 = (config.TaskBarGearsetShowLevel ? "Lv 100" : string.Empty);
		string text3 = (config.TaskBarGearsetShowItemLevel ? "装等 999" : string.Empty);
		string text4 = (config.TaskBarGearsetShowName ? text : string.Empty);
		string text5 = (config.TaskBarGearsetShowNumber ? "套装 00" : string.Empty);
		return string.Join(' ', new string[4] { text2, text3, text4, text5 }.Where((string value) => !string.IsNullOrWhiteSpace(value)));
	}

	private GearsetDisplayInfo? GetCachedGearsetDisplayInfo()
	{
		DateTime utcNow = DateTime.UtcNow;
		if (utcNow < nextGearsetDisplayInfoRefreshAt)
		{
			return cachedGearsetDisplayInfo;
		}
		cachedGearsetDisplayInfo = TryGetCurrentGearsetDisplayInfo();
		nextGearsetDisplayInfoRefreshAt = utcNow + GearsetDisplayInfoCacheDuration;
		return cachedGearsetDisplayInfo;
	}

	private unsafe GearsetDisplayInfo? TryGetCurrentGearsetDisplayInfo()
	{
		try
		{
			RaptureGearsetModule* raptureGearsetModule = UIModule.Instance()->GetRaptureGearsetModule();
			if (raptureGearsetModule != null)
			{
				int currentGearsetIndex = raptureGearsetModule->CurrentGearsetIndex;
				if (currentGearsetIndex >= 0)
				{
					string text = string.Empty;
					uint classJobId = 0u;
					short itemLevel = 0;
					if (raptureGearsetModule->IsValidGearset((byte)currentGearsetIndex))
					{
						RaptureGearsetModule.GearsetEntry gearsetEntry = raptureGearsetModule->Entries[(byte)currentGearsetIndex];
						text = gearsetEntry.NameString;
						classJobId = gearsetEntry.ClassJob;
						itemLevel = gearsetEntry.ItemLevel;
					}
					return new GearsetDisplayInfo((byte)currentGearsetIndex, string.IsNullOrWhiteSpace(text) ? string.Empty : text, classJobId, itemLevel);
				}
			}
		}
		catch
		{
		}
		return null;
	}

	private static string BuildGearsetDisplayText(byte gearsetId, string name, bool showNumber, bool showName)
	{
		string text = $"套装 {gearsetId + 1:00}";
		string text2 = name.Trim();
		if (showNumber & showName)
		{
			if (!string.IsNullOrWhiteSpace(text2))
			{
				return text + " · " + text2;
			}
			return text;
		}
		if (showNumber)
		{
			return text;
		}
		if (!string.IsNullOrWhiteSpace(text2))
		{
			return text2;
		}
		return text;
	}

	private static string BuildGearsetMeasureText(byte gearsetId, string name, bool showNumber, bool showName)
	{
		string text = $"套装 {gearsetId + 1:00}";
		string text2 = name.Trim();
		if (showNumber & showName)
		{
			if (!string.IsNullOrWhiteSpace(text2))
			{
				return text + " · " + text2;
			}
			return text + " · 套装名称";
		}
		if (showNumber)
		{
			return text;
		}
		if (!string.IsNullOrWhiteSpace(text2))
		{
			return text2;
		}
		return "套装切换";
	}

	private unsafe long GetGil()
	{
		try
		{
			return InventoryManager.Instance()->GetGil();
		}
		catch
		{
			return -1L;
		}
	}

	private long GetCachedCurrencyCount(uint itemId)
	{
		DateTime utcNow = DateTime.UtcNow;
		if (cachedCurrencyCount >= 0 && cachedCurrencyCountItemId == itemId && utcNow < nextCurrencyCountRefreshAt)
		{
			return cachedCurrencyCount;
		}
		cachedCurrencyCount = GetCurrencyCount(itemId);
		cachedCurrencyCountItemId = itemId;
		nextCurrencyCountRefreshAt = utcNow + CurrencyCountCacheDuration;
		return cachedCurrencyCount;
	}

	private unsafe long GetCurrencyCount(uint itemId)
	{
		if (itemId == 1)
		{
			return GetGil();
		}
		try
		{
			return InventoryManager.Instance()->GetInventoryItemCount(itemId, isHq: false, checkEquipped: true, checkArmory: true, 0);
		}
		catch
		{
			return -1L;
		}
	}

	private unsafe void OpenCurrenciesWindow()
	{
		try
		{
			UIModule* ptr = UIModule.Instance();
			if (ptr != null)
			{
				ptr->ExecuteMainCommand(66u);
			}
		}
		catch
		{
		}
	}

	private CurrencyDisplayInfo GetSelectedCurrencyDisplayInfo()
	{
		uint itemId = config.TaskBarCurrencyItemId;
		CurrencyDisplayInfo currencyDisplayInfo = GetCurrencyDisplayOptions().FirstOrDefault((CurrencyDisplayInfo option) => option.ItemId == itemId) ?? CurrencyDisplayOptions[0];
		try
		{
			if (dataManager.GetExcelSheet<Item>().TryGetRow(itemId, out var row))
			{
				string excelText = GetExcelText(row.Name.ExtractText());
				return new CurrencyDisplayInfo(itemId, string.IsNullOrWhiteSpace(excelText) ? currencyDisplayInfo.Name : excelText, row.Icon);
			}
		}
		catch
		{
		}
		return currencyDisplayInfo;
	}

	private IReadOnlyList<CurrencyDisplayInfo> GetCurrencyDisplayOptions()
	{
		string b = config.TaskBarCurrencyCustomItemIds ?? string.Empty;
		DateTime utcNow = DateTime.UtcNow;
		if (cachedCurrencyDisplayOptions != null && utcNow < nextCurrencyDisplayOptionsRefreshAt && string.Equals(cachedCurrencyDisplayOptionsCustomIds, b, StringComparison.Ordinal))
		{
			return cachedCurrencyDisplayOptions;
		}
		IEnumerable<CurrencyDisplayInfo> enumerable = from option in CurrencyDisplayOptions.Concat(GetTomestoneCurrencyOptions()).Concat(ParseCustomCurrencyOptions())
			group option by option.ItemId into @group
			select @group.First();
		List<CurrencyDisplayInfo> list = new List<CurrencyDisplayInfo>();
		foreach (CurrencyDisplayInfo item2 in enumerable)
		{
			CurrencyDisplayInfo item = item2;
			try
			{
				if (dataManager.GetExcelSheet<Item>().TryGetRow(item2.ItemId, out var row))
				{
					string excelText = GetExcelText(row.Name.ExtractText());
					item = new CurrencyDisplayInfo(item2.ItemId, string.IsNullOrWhiteSpace(excelText) ? item2.Name : excelText, row.Icon);
				}
			}
			catch
			{
			}
			list.Add(item);
		}
		cachedCurrencyDisplayOptions = list;
		cachedCurrencyDisplayOptionsCustomIds = b;
		nextCurrencyDisplayOptionsRefreshAt = utcNow.AddSeconds(10.0);
		return list;
	}

	private IEnumerable<CurrencyDisplayInfo> GetTomestoneCurrencyOptions()
	{
		List<CurrencyDisplayInfo> list = new List<CurrencyDisplayInfo>();
		try
		{
			foreach (TomestonesItem item in dataManager.GetExcelSheet<TomestonesItem>())
			{
				uint rowId = item.Tomestones.RowId;
				if (rowId - 2 <= 1)
				{
					Item value = item.Item.Value;
					list.Add(new CurrencyDisplayInfo(value.RowId, "神典石", value.Icon));
				}
			}
		}
		catch
		{
		}
		return list;
	}

	private IEnumerable<CurrencyDisplayInfo> ParseCustomCurrencyOptions()
	{
		string[] array = (config.TaskBarCurrencyCustomItemIds ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries);
		foreach (string obj in array)
		{
			if (uint.TryParse(obj.Trim(), out var id) && id != 0 && !CurrencyDisplayOptions.Any((CurrencyDisplayInfo option) => option.ItemId == id))
			{
				yield return new CurrencyDisplayInfo(id, $"物品 {id}", 0u);
			}
		}
	}

	private bool IsCurrencyVisibleInPopup(CurrencyDisplayInfo currency)
	{
		if (config.TaskBarCurrencyVisibleItemIds.Count != 0)
		{
			return config.TaskBarCurrencyVisibleItemIds.Contains(currency.ItemId);
		}
		return true;
	}

	private uint GetLimitedTomestoneItemId()
	{
		DateTime utcNow = DateTime.UtcNow;
		if (utcNow < nextLimitedTomestoneLookupAt)
		{
			return cachedLimitedTomestoneItemId;
		}
		nextLimitedTomestoneLookupAt = utcNow + LimitedTomestoneLookupCacheDuration;
		try
		{
			cachedLimitedTomestoneItemId = dataManager.GetExcelSheet<TomestonesItem>().FirstOrDefault((TomestonesItem tomestone) => tomestone.Tomestones.RowId == 3).Item.RowId;
		}
		catch
		{
			cachedLimitedTomestoneItemId = 0u;
		}
		return cachedLimitedTomestoneItemId;
	}

	private unsafe (long Count, long Capacity)? GetCurrencyWeeklyProgress(uint itemId)
	{
		if (itemId != GetLimitedTomestoneItemId())
		{
			return null;
		}
		try
		{
			int limitedTomestoneWeeklyLimit = InventoryManager.GetLimitedTomestoneWeeklyLimit();
			int weeklyAcquiredTomestoneCount = InventoryManager.Instance()->GetWeeklyAcquiredTomestoneCount();
			return (limitedTomestoneWeeklyLimit > 0) ? new(long, long)?((weeklyAcquiredTomestoneCount, limitedTomestoneWeeklyLimit)) : (((long, long)?)null);
		}
		catch
		{
			return null;
		}
	}

	private Vector4? GetCurrencyValueColor(uint itemId, long count, long capacity)
	{
		if (!config.TaskBarCurrencyUseThresholdColors || count < 0)
		{
			return null;
		}
		(long, long)? tuple = GetCurrencyWeeklyProgress(itemId) ?? ((capacity > 0) ? new(long, long)?((count, capacity)) : (((long, long)?)null));
		if (tuple.HasValue)
		{
			(long, long) valueOrDefault = tuple.GetValueOrDefault();
			if (valueOrDefault.Item2 > 0)
			{
				float num = (float)valueOrDefault.Item1 * 100f / (float)valueOrDefault.Item2;
				int num2 = Math.Clamp(config.TaskBarCurrencyThresholdPercentage, 0, 100);
				if (num >= (float)num2)
				{
					return new Vector4(1f, 0.48f, 0.42f, 1f);
				}
				return new Vector4(0.62f, 1f, 0.72f, 1f);
			}
		}
		return null;
	}

	private unsafe long GetCurrencyCapacity(uint itemId)
	{
		try
		{
			if ((itemId - 20 <= 2 || itemId == 27 || itemId == 10307) ? true : false)
			{
				PlayerState* ptr = PlayerState.Instance();
				if (ptr != null)
				{
					byte grandcompanyId = itemId switch
					{
						20u => 0, 
						21u => 1, 
						22u => 2, 
						_ => ptr->GrandCompany, 
					};
					return InventoryManager.Instance()->GetMaxCompanySeals(grandcompanyId);
				}
			}
			if (dataManager.GetExcelSheet<Item>().TryGetRow(itemId, out var row))
			{
				return (row.StackSize > 1) ? row.StackSize : 0;
			}
		}
		catch
		{
		}
		return 0L;
	}

	private string GetCurrencyWeeklyText(uint itemId)
	{
		(long, long)? currencyWeeklyProgress = GetCurrencyWeeklyProgress(itemId);
		if (currencyWeeklyProgress.HasValue)
		{
			(long, long) valueOrDefault = currencyWeeklyProgress.GetValueOrDefault();
			return $"（周 {valueOrDefault.Item1:N0}/{valueOrDefault.Item2:N0}）";
		}
		return string.Empty;
	}

	private unsafe void OpenTeleportWindow()
	{
		try
		{
			ActionManager.Instance()->UseAction(ActionType.GeneralAction, 7u, 3758096384uL, 0u, ActionManager.UseActionMode.None, 0u, null);
		}
		catch
		{
		}
	}

	private unsafe void EquipGearset(byte gearsetId)
	{
		try
		{
			RaptureGearsetModule* raptureGearsetModule = UIModule.Instance()->GetRaptureGearsetModule();
			if (raptureGearsetModule != null)
			{
				raptureGearsetModule->EquipGearset(gearsetId, 0);
			}
		}
		catch
		{
		}
	}

	private void HandleVolumeTaskBarClick(DtrInteractionEvent interactionEvent)
	{
		if (interactionEvent.ClickType == MouseClickType.Right)
		{
			ToggleMasterVolumeMute();
		}
	}

	private string GetVolumeTaskBarTooltip()
	{
		bool num = IsMasterVolumeMuted();
		uint masterVolume = GetMasterVolume();
		string text = (num ? $"静音（{masterVolume}%）" : $"{masterVolume}%");
		return "音量 " + text + "；左键打开音量控制，右键静音，滚轮微调";
	}

	private uint GetMasterVolume()
	{
		return GetVolume(SystemConfigOption.SoundMaster);
	}

	private bool IsMasterVolumeMuted()
	{
		return IsVolumeMuted(SystemConfigOption.IsSndMaster);
	}

	private void SetMasterVolume(uint volume)
	{
		SetVolume(SystemConfigOption.SoundMaster, SystemConfigOption.IsSndMaster, volume);
	}

	private void AdjustMasterVolume(int delta)
	{
		int masterVolume = (int)GetMasterVolume();
		SetMasterVolume((uint)Math.Clamp(masterVolume + delta, 0, 100));
	}

	private void ToggleMasterVolumeMute()
	{
		ToggleVolumeMute(SystemConfigOption.IsSndMaster);
	}

	private uint GetVolume(SystemConfigOption volumeOption)
	{
		if (!gameConfig.TryGet(volumeOption, out uint value))
		{
			return 0u;
		}
		return Math.Clamp(value, 0u, 100u);
	}

	private bool IsVolumeMuted(SystemConfigOption muteOption)
	{
		if (gameConfig.TryGet(muteOption, out uint value))
		{
			return value != 0;
		}
		return false;
	}

	private void SetVolume(SystemConfigOption volumeOption, SystemConfigOption muteOption, uint volume)
	{
		uint num = Math.Clamp(volume, 0u, 100u);
		gameConfig.Set(volumeOption, num);
		if (num != 0 && IsVolumeMuted(muteOption))
		{
			gameConfig.Set(muteOption, 0u);
		}
	}

	private void ToggleVolumeMute(SystemConfigOption muteOption)
	{
		gameConfig.Set(muteOption, (!IsVolumeMuted(muteOption)) ? 1u : 0u);
	}

	private void DrawMainMenuPopup(float opacity, float scale)
	{
		if (ImGui.IsPopupOpen("AllHud 主菜单"))
		{
			EnsureTaskBarMainMenuLoaded();
			Vector2 vector = new Vector2(620f * scale, GetMainMenuPopupHeight(scale, ImGui.GetMainViewport().WorkSize.Y));
			ImGui.SetNextWindowPos(GetTaskBarPopupPosition(mainMenuPopupAnchor, vector, scale), ImGuiCond.Always);
			ImGui.SetNextWindowSize(vector, ImGuiCond.Always);
			ThemePalette effectiveTheme = GetEffectiveTheme();
			ImGui.PushStyleColor(ImGuiCol.PopupBg, WithOpacity(effectiveTheme.PopupBg, opacity));
			ImGui.PushStyleColor(ImGuiCol.Text, effectiveTheme.Text);
			ImGui.PushStyleColor(ImGuiCol.Border, WithOpacity(effectiveTheme.Border, opacity));
			ImGui.PushStyleColor(ImGuiCol.ChildBg, WithOpacity(effectiveTheme.Surface, opacity));
			ImGui.PushStyleColor(ImGuiCol.Header, effectiveTheme.Header);
			ImGui.PushStyleColor(ImGuiCol.HeaderHovered, effectiveTheme.HeaderHovered);
			ImGui.PushStyleColor(ImGuiCol.HeaderActive, effectiveTheme.HeaderActive);
			ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, effectiveTheme.ScrollbarBg);
			ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, effectiveTheme.ScrollbarGrab);
			ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, effectiveTheme.ScrollbarGrabHovered);
			ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, effectiveTheme.ScrollbarGrabActive);
			ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10f * scale, 9f * scale));
			ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, ThemeDrawing.IsLiquidGlass(config.ThemeMode) ? ThemeDrawing.GetLiquidGlassRounding(config, scale) : (9f * scale));
			ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5f * scale);
			ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6f * scale, 5f * scale));
			if (ImGui.BeginPopup("AllHud 主菜单"))
			{
				DrawLiquidGlassPopupSurface(opacity, scale);
				DrawTaskBarMainMenuBody(scale);
				ImGui.EndPopup();
			}
			ImGui.PopStyleVar(4);
			ImGui.PopStyleColor(11);
		}
	}

	private float GetMainMenuPopupHeight(float scale, float viewportHeight)
	{
		if (mainMenuCategories.Count == 0)
		{
			return 610f * scale;
		}
		float rowHeight = GetMainMenuEntryHeight(scale);
		float separatorHeight = MathF.Round(13f * scale);
		float num = MathF.Round(30f * scale);
		float num2 = 38f * scale;
		float num3 = mainMenuCategories.Select((TaskBarMainMenuCategory category) => category.Entries.Sum((TaskBarMainMenuEntry entry) => (!entry.IsSeparator) ? rowHeight : separatorHeight)).DefaultIfEmpty(0f).Max();
		float val = Math.Max(610f * scale, num + num3 + num2);
		float val2 = Math.Max(610f * scale, viewportHeight - 48f * scale);
		return SnapToPixel(Math.Min(val, val2));
	}

	private static float GetMainMenuEntryHeight(float scale)
	{
		return MathF.Round(26f * scale);
	}

	private IDisposable PushTaskBarPopupScrollbarStyle(float scale)
	{
		ThemePalette effectiveTheme = GetEffectiveTheme();
		ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, effectiveTheme.ScrollbarBg);
		ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, effectiveTheme.ScrollbarGrab);
		ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, effectiveTheme.ScrollbarGrabHovered);
		ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, effectiveTheme.ScrollbarGrabActive);
		ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, MathF.Max(8f, 10f * scale));
		return new StyleScope(4, 1);
	}

	private void DrawTaskBarMainMenuBody(float scale)
	{
		if (mainMenuCategories.Count == 0)
		{
			ImGui.TextDisabled("没有可用菜单项");
			return;
		}
		selectedMainMenuCategoryIndex = Math.Clamp(selectedMainMenuCategoryIndex, 0, mainMenuCategories.Count - 1);
		float num = 158f * scale;
		float y = Math.Max(340f * scale, ImGui.GetContentRegionAvail().Y);
		if (ImGui.BeginChild("##AllHudMainMenuCategories", new Vector2(num, y), border: true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
		{
			for (int i = 0; i < mainMenuCategories.Count; i++)
			{
				DrawMainMenuCategory(mainMenuCategories[i], i, num, scale);
			}
		}
		ImGui.EndChild();
		ImGui.SameLine(0f, 8f * scale);
		TaskBarMainMenuCategory taskBarMainMenuCategory = mainMenuCategories[selectedMainMenuCategoryIndex];
		using (PushTaskBarPopupScrollbarStyle(scale))
		{
			if (ImGui.BeginChild("##AllHudMainMenuEntries", new Vector2(0f, y), border: true))
			{
				ImGui.TextUnformatted(taskBarMainMenuCategory.Name);
				ImGui.Separator();
				foreach (TaskBarMainMenuEntry entry in taskBarMainMenuCategory.Entries)
				{
					DrawMainMenuEntry(entry, scale);
				}
			}
			ImGui.EndChild();
		}
	}

	private void DrawMainMenuCategory(TaskBarMainMenuCategory category, int index, float categoryWidth, float scale)
	{
		bool flag = index == selectedMainMenuCategoryIndex;
		float mainMenuEntryHeight = GetMainMenuEntryHeight(scale);
		Vector2 vector = SnapToPixel(ImGui.GetCursorScreenPos());
		float num = Math.Max(80f * scale, Math.Min(categoryWidth - 8f * scale, ImGui.GetContentRegionAvail().X));
		Vector2 vector2 = SnapToPixel(vector + new Vector2(2f * scale, 2f * scale));
		Vector2 vector3 = SnapToPixel(vector + new Vector2(num - 2f * scale, mainMenuEntryHeight - 2f * scale));
		ImGui.SetCursorScreenPos(vector2);
		ImU8String strId = new ImU8String(24, 1);
		strId.AppendLiteral("##AllHudMainMenuCategory");
		strId.AppendFormatted(category.RowId);
		ImGui.InvisibleButton(strId, vector3 - vector2);
		bool flag2 = ImGui.IsItemHovered();
		if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
		{
			selectedMainMenuCategoryIndex = index;
		}
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		ThemePalette effectiveTheme = GetEffectiveTheme();
		if (flag | flag2)
		{
			Vector4 col = (flag ? effectiveTheme.HeaderActive : effectiveTheme.HeaderHovered);
			windowDrawList.AddRectFilled(vector2, vector3, ImGui.GetColorU32(col), 4f * scale);
		}
		if (flag)
		{
			Vector2 pMin = SnapToPixel(new Vector2(vector2.X, vector2.Y + 5f * scale));
			Vector2 pMax = SnapToPixel(new Vector2(vector2.X + 3f * scale, vector3.Y - 5f * scale));
			windowDrawList.AddRectFilled(pMin, pMax, ImGui.GetColorU32(effectiveTheme.Accent), 2f * scale);
		}
		DrawMainMenuGameIconImage(category.IconId, vector + new Vector2(5f * scale, 0f), mainMenuEntryHeight, scale);
		Vector2 pos = SnapToPixel(new Vector2(vector.X + 36f * scale, vector.Y + Math.Max(0f, (mainMenuEntryHeight - ImGui.GetTextLineHeight()) * 0.5f)));
		uint colorU = ImGui.GetColorU32(flag ? effectiveTheme.Text : effectiveTheme.TextMuted);
		windowDrawList.AddText(pos, colorU, category.Name);
		ImGui.SetCursorScreenPos(new Vector2(vector.X, vector.Y + mainMenuEntryHeight));
	}

	private void DrawMainMenuEntry(TaskBarMainMenuEntry entry, float scale)
	{
		if (entry.IsSeparator)
		{
			ImGui.Spacing();
			ImGui.Separator();
			ImGui.Spacing();
			return;
		}
		bool flag = IsMainMenuEntryEnabled(entry);
		float num = MathF.Round(30f * scale);
		Vector2 vector = SnapToPixel(ImGui.GetCursorScreenPos());
		float num2 = Math.Max(120f * scale, ImGui.GetContentRegionAvail().X);
		Vector2 vector2 = SnapToPixel(vector + new Vector2(2f * scale, 2f * scale));
		Vector2 vector3 = SnapToPixel(vector + new Vector2(num2 - 2f * scale, num - 2f * scale));
		ImGui.SetCursorScreenPos(vector2);
		ImU8String strId = new ImU8String(21, 1);
		strId.AppendLiteral("##AllHudMainMenuEntry");
		strId.AppendFormatted(entry.CommandId?.ToString() ?? entry.ChatCommand);
		ImGui.InvisibleButton(strId, vector3 - vector2);
		bool num3 = flag && ImGui.IsItemHovered();
		bool flag2 = flag && ImGui.IsItemActive();
		if (flag && ImGui.IsItemClicked(ImGuiMouseButton.Left))
		{
			ExecuteMainMenuEntry(entry);
			ImGui.CloseCurrentPopup();
		}
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		ThemePalette effectiveTheme = GetEffectiveTheme();
		if (num3 | flag2)
		{
			Vector4 col = (flag2 ? effectiveTheme.HeaderActive : effectiveTheme.HeaderHovered);
			Vector4 col2 = (flag2 ? effectiveTheme.Accent : effectiveTheme.Border);
			windowDrawList.AddRectFilled(vector2, vector3, ImGui.GetColorU32(col), 5f * scale);
			windowDrawList.AddRect(vector2, vector3, ImGui.GetColorU32(col2), 5f * scale, ImDrawFlags.None, 1f * scale);
		}
		DrawMainMenuEntryIcon(entry, vector + new Vector2(5f * scale, 0f), num, scale, flag ? 1f : 0.42f);
		Vector2 pos = SnapToPixel(new Vector2(vector.X + 36f * scale, vector.Y + Math.Max(0f, (num - ImGui.GetTextLineHeight()) * 0.5f)));
		uint colorU = ImGui.GetColorU32(flag ? effectiveTheme.Text : effectiveTheme.TextMuted);
		windowDrawList.AddText(pos, colorU, entry.Name);
		ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X, vector.Y + num));
	}

	private void DrawMainMenuGameIcon(uint iconId, Vector2 rowStart, float rowHeight, float scale, float alpha = 1f)
	{
		DrawMainMenuGameIconImage(iconId, rowStart, rowHeight, scale, alpha);
		ImGui.Dummy(new Vector2(MathF.Round(24f * scale) + 2f * scale, rowHeight));
	}

	private void DrawMainMenuEntryIcon(TaskBarMainMenuEntry entry, Vector2 rowStart, float rowHeight, float scale, float alpha = 1f)
	{
		if (entry.IconId != 0)
		{
			DrawMainMenuGameIconImage(entry.IconId, rowStart, rowHeight, scale, alpha);
		}
	}

	private void DrawMainMenuGameIconImage(uint iconId, Vector2 rowStart, float rowHeight, float scale, float alpha = 1f)
	{
		float num = MathF.Round(24f * scale);
		Vector2 vector = SnapToPixel(rowStart + new Vector2(1f * scale, Math.Max(0f, (rowHeight - num) * 0.5f)));
		Vector2 max = vector + new Vector2(num, num);
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		DrawGameIconImage(windowDrawList, iconId, vector, max, fillBounds: true, bypassMissingRetry: true);
	}

	private void EnsureTaskBarMainMenuLoaded()
	{
		if (mainMenuLoaded)
		{
			return;
		}
		mainMenuLoaded = true;
		mainMenuCategories.Clear();
		ILookup<uint, MainCommand> lookup = (from command in dataManager.GetExcelSheet<MainCommand>()
			where !string.IsNullOrWhiteSpace(GetExcelText(command.Name.ExtractText()))
			select command).ToLookup((MainCommand command) => command.MainCommandCategory.RowId);
		foreach (MainCommandCategory item in from category in dataManager.GetExcelSheet<MainCommandCategory>()
			orderby category.RowId
			select category)
		{
			string excelText = GetExcelText(item.Name.ExtractText());
			if (!string.IsNullOrWhiteSpace(excelText))
			{
				List<TaskBarMainMenuEntry> list = (from entry in lookup[item.RowId].OrderBy((MainCommand command) => command.SortID).Select(CreateMainMenuEntry)
					where !string.IsNullOrWhiteSpace(entry.Name)
					select entry).ToList();
				if (list.Count != 0)
				{
					mainMenuCategories.Add(new TaskBarMainMenuCategory(item.RowId, excelText, GetMainMenuCategoryIconId(item.RowId), list));
				}
			}
		}
	}

	private TaskBarMainMenuEntry CreateMainMenuEntry(MainCommand command)
	{
		uint iconId = ((command.Icon > 0) ? ((uint)command.Icon) : 0u);
		string mainCommandDisplayName = GetMainCommandDisplayName(command.RowId, GetExcelText(command.Name.ExtractText()));
		if (mainCommandDisplayName.Contains("许可证", StringComparison.OrdinalIgnoreCase))
		{
			return new TaskBarMainMenuEntry(string.Empty, null, string.Empty, 0u);
		}
		return new TaskBarMainMenuEntry(mainCommandDisplayName, command.RowId, string.Empty, iconId);
	}

	private static uint GetMainMenuCategoryIconId(uint categoryId)
	{
		return categoryId switch
		{
			1u => 1u, 
			2u => 5u, 
			3u => 21u, 
			4u => 7u, 
			5u => 17u, 
			6u => 20u, 
			7u => 14u, 
			_ => 0u, 
		};
	}

	private static string GetExcelText(string text)
	{
		return text.Replace("\u00ad", string.Empty, StringComparison.Ordinal).Trim();
	}

	private unsafe string GetMainCommandDisplayName(uint commandId, string fallback)
	{
		AgentHUD* ptr = AgentHUD.Instance();
		if (ptr == null)
		{
			return fallback;
		}
		string excelText = GetExcelText(ptr->GetMainCommandString(commandId).ExtractText());
		if (string.IsNullOrWhiteSpace(excelText))
		{
			return fallback;
		}
		int num = excelText.IndexOf('[', StringComparison.Ordinal);
		if (num <= 0)
		{
			return excelText;
		}
		return excelText.Substring(0, num).Trim();
	}

	private unsafe bool IsMainMenuEntryEnabled(TaskBarMainMenuEntry entry)
	{
		if (!entry.CommandId.HasValue)
		{
			return !string.IsNullOrWhiteSpace(entry.ChatCommand);
		}
		UIModule* ptr = UIModule.Instance();
		AgentHUD* ptr2 = AgentHUD.Instance();
		if (ptr != null && ptr2 != null && ptr2->IsMainCommandEnabled(entry.CommandId.Value))
		{
			return ptr->IsMainCommandUnlocked(entry.CommandId.Value);
		}
		return false;
	}

	private unsafe void ExecuteMainMenuEntry(TaskBarMainMenuEntry entry)
	{
		if (entry.CommandId.HasValue)
		{
			UIModule* ptr = UIModule.Instance();
			if (ptr != null)
			{
				ptr->ExecuteMainCommand(entry.CommandId.Value);
			}
		}
		else if (!string.IsNullOrWhiteSpace(entry.ChatCommand))
		{
			commandManager.ProcessCommand(entry.ChatCommand);
		}
	}

	private void DrawVolumePopup(float opacity, float scale)
	{
		if (ImGui.IsPopupOpen("AllHud 音量控制"))
		{
			Vector2 vector = new Vector2(268f * scale, 0f);
			ImGui.SetNextWindowPos(GetTaskBarPopupPosition(volumePopupAnchor, vector, scale, 280f * scale), ImGuiCond.Always);
			ImGui.SetNextWindowSize(vector, ImGuiCond.Always);
			ThemePalette effectiveTheme = GetEffectiveTheme();
			ImGui.PushStyleColor(ImGuiCol.PopupBg, WithOpacity(effectiveTheme.PopupBg, opacity));
			ImGui.PushStyleColor(ImGuiCol.Text, effectiveTheme.Text);
			ImGui.PushStyleColor(ImGuiCol.Border, WithOpacity(effectiveTheme.Border, opacity));
			ImGui.PushStyleColor(ImGuiCol.FrameBg, effectiveTheme.FrameBg);
			ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, effectiveTheme.FrameHovered);
			ImGui.PushStyleColor(ImGuiCol.FrameBgActive, effectiveTheme.FrameActive);
			ImGui.PushStyleColor(ImGuiCol.SliderGrab, effectiveTheme.Accent);
			ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, effectiveTheme.HeaderActive);
			ImGui.PushStyleColor(ImGuiCol.Button, effectiveTheme.Button);
			ImGui.PushStyleColor(ImGuiCol.ButtonHovered, effectiveTheme.ButtonHovered);
			ImGui.PushStyleColor(ImGuiCol.ButtonActive, effectiveTheme.ButtonActive);
			ImGui.PushStyleColor(ImGuiCol.TextSelectedBg, effectiveTheme.AccentSoft);
			ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12f * scale, 7f * scale));
			ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, ThemeDrawing.IsLiquidGlass(config.ThemeMode) ? ThemeDrawing.GetLiquidGlassRounding(config, scale) : (7f * scale));
			if (ImGui.BeginPopup("AllHud 音量控制"))
			{
				DrawLiquidGlassPopupSurface(opacity, scale);
				DrawVolumeChannelControl("主音量", SystemConfigOption.SoundMaster, SystemConfigOption.IsSndMaster, scale);
				DrawVolumeChannelControl("背景音乐", SystemConfigOption.SoundBgm, SystemConfigOption.IsSndBgm, scale);
				DrawVolumeChannelControl("音效", SystemConfigOption.SoundSe, SystemConfigOption.IsSndSe, scale);
				DrawVolumeChannelControl("语音", SystemConfigOption.SoundVoice, SystemConfigOption.IsSndVoice, scale);
				DrawVolumeChannelControl("环境", SystemConfigOption.SoundEnv, SystemConfigOption.IsSndEnv, scale);
				DrawVolumeChannelControl("系统", SystemConfigOption.SoundSystem, SystemConfigOption.IsSndSystem, scale);
				ImGui.EndPopup();
			}
			ImGui.PopStyleVar(2);
			ImGui.PopStyleColor(12);
		}
	}

	private void DrawCoordinatesPopup(float opacity, float scale)
	{
		DrawSimpleTaskBarPopup("AllHud 坐标", new Vector2(300f * scale, 0f), opacity, scale, delegate
		{
			ImGui.TextUnformatted(GetCoordinatesText());
		});
	}

	private unsafe void DrawWorldMarkers()
	{
		if (!config.ShowWorldMarkers || clientState.TerritoryType == 0)
		{
			return;
		}
		try
		{
			IPlayerCharacter localPlayer = objectTable.LocalPlayer;
			if (localPlayer == null)
			{
				return;
			}
			AgentMap* ptr = AgentMap.Instance();
			if (ptr == null)
			{
				return;
			}
			uint currentMapId = ptr->CurrentMapId;
			uint territoryType = clientState.TerritoryType;
			DateTime utcNow = DateTime.UtcNow;
			if (utcNow >= nextWorldMarkerRefreshAt || territoryType != cachedWorldMarkerTerritory || currentMapId != cachedWorldMarkerMap)
			{
				cachedWorldMarkers = BuildWorldMarkerSnapshot(ptr, territoryType, currentMapId);
				cachedWorldMarkerTerritory = territoryType;
				cachedWorldMarkerMap = currentMapId;
				nextWorldMarkerRefreshAt = utcNow.AddMilliseconds(150.0);
			}
			if (cachedWorldMarkers.Count == 0)
			{
				return;
			}
			ImDrawListPtr backgroundDrawList = ImGui.GetBackgroundDrawList();
			ImGuiViewportPtr mainViewport = ImGui.GetMainViewport();
			bool hasPlayerScreenPosition = false;
			Vector2 screenPos = Vector2.Zero;
			try
			{
				hasPlayerScreenPosition = gameGui.WorldToScreen(localPlayer.Position, out screenPos, out var _) && IsFinite(screenPos);
			}
			catch
			{
			}
			foreach (WorldMarkerRenderInfo cachedWorldMarker in cachedWorldMarkers)
			{
				DrawWorldMarker(backgroundDrawList, mainViewport, localPlayer.Position, screenPos, hasPlayerScreenPosition, cachedWorldMarker);
			}
		}
		catch
		{
			cachedWorldMarkers.Clear();
		}
	}

	private unsafe List<WorldMarkerRenderInfo> BuildWorldMarkerSnapshot(AgentMap* agentMap, uint territoryId, uint mapId)
	{
		List<WorldMarkerRenderInfo> list = new List<WorldMarkerRenderInfo>(9);
		if (config.WorldMarkersShowFlag && agentMap->FlagMarkerCount > 0)
		{
			ref FlagMapMarker reference = ref agentMap->FlagMapMarkers[0];
			if ((reference.TerritoryId == 0 || reference.TerritoryId == territoryId) && (mapId == 0 || reference.MapId == 0 || reference.MapId == mapId))
			{
				Vector3 vector = new Vector3(reference.XFloat, 0f, reference.YFloat);
				if (IsFinite(vector))
				{
					list.Add(new WorldMarkerRenderInfo("flag", "地图旗标", vector, (reference.MapMarker.IconId == 0) ? 60561u : reference.MapMarker.IconId, config.WorldMarkersShowCompass));
				}
			}
		}
		if (!config.WorldMarkersShowWaymarks)
		{
			return list;
		}
		try
		{
			MarkingController* ptr = MarkingController.Instance();
			if (ptr == null)
			{
				return list;
			}
			for (int i = 0; i < WorldMarkerWaymarkIcons.Length; i++)
			{
				ref FFXIVClientStructs.FFXIV.Client.Game.UI.FieldMarker reference2 = ref ptr->FieldMarkers[i];
				if (reference2.Active)
				{
					Vector3 vector2 = new Vector3((float)reference2.X / 1000f, (float)reference2.Y / 1000f, (float)reference2.Z / 1000f);
					if (IsFinite(vector2) && !(vector2 == Vector3.Zero))
					{
						list.Add(new WorldMarkerRenderInfo($"waymark_{i}", (i < 4) ? $"场地标点 {(char)(ushort)(65 + i)}" : $"场地标点 {i - 3}", vector2, WorldMarkerWaymarkIcons[i], config.WorldMarkersShowCompass));
					}
				}
			}
		}
		catch
		{
		}
		return list;
	}

	private void DrawWorldMarker(ImDrawListPtr drawList, ImGuiViewportPtr viewport, Vector3 playerPosition, Vector2 playerScreenPosition, bool hasPlayerScreenPosition, WorldMarkerRenderInfo marker)
	{
		if (!IsFinite(viewport.WorkPos) || !IsFinite(viewport.WorkSize) || viewport.WorkSize.X <= 48f || viewport.WorkSize.Y <= 48f)
		{
			return;
		}
		float num = Vector3.Distance(playerPosition, marker.Position);
		if (config.WorldMarkersMaxVisibleDistance > 0f && num > config.WorldMarkersMaxVisibleDistance)
		{
			return;
		}
		float worldMarkerAlpha = GetWorldMarkerAlpha(num);
		if (worldMarkerAlpha <= 0.01f)
		{
			return;
		}
		bool flag = false;
		bool inView = false;
		Vector2 screenPos = Vector2.Zero;
		try
		{
			flag = gameGui.WorldToScreen(marker.Position, out screenPos, out inView);
		}
		catch
		{
			return;
		}
		bool flag2 = flag & inView;
		if (!flag2 && !marker.ShowOnCompass)
		{
			return;
		}
		int num2;
		Vector2 direction;
		if (!flag2)
		{
			num2 = (marker.ShowOnCompass ? 1 : 0);
			if (num2 != 0)
			{
				Vector2? directionHint = null;
				if (hasPlayerScreenPosition && IsFinite(screenPos))
				{
					Vector2 value = (flag ? (screenPos - playerScreenPosition) : (playerScreenPosition - screenPos));
					if (IsFinite(value) && value.LengthSquared() >= 0.001f)
					{
						directionHint = value;
					}
				}
				screenPos = GetWorldMarkerEdgePosition(viewport, screenPos, marker.Position, playerPosition, out direction, directionHint);
				goto IL_0148;
			}
		}
		else
		{
			num2 = 0;
		}
		direction = Vector2.Zero;
		goto IL_0148;
		IL_0148:
		float num3 = Math.Clamp(config.WorldMarkersIconScale, 0.5f, 2.5f);
		float num4 = 30f * num3;
		Vector2 vector = new Vector2(MathF.Round(screenPos.X), MathF.Round(screenPos.Y));
		Vector2 vector2 = vector - new Vector2(num4 * 0.5f);
		Vector2 vector3 = vector + new Vector2(num4 * 0.5f);
		ThemePalette effectiveTheme = GetEffectiveTheme();
		Vector4 col = new Vector4(config.WorldMarkersColor.X, config.WorldMarkersColor.Y, config.WorldMarkersColor.Z, worldMarkerAlpha);
		drawList.AddCircleFilled(vector, num4 * 0.56f, ImGui.GetColorU32(WithOpacity(effectiveTheme.TextShadow, worldMarkerAlpha * 0.68f)), 24);
		drawList.AddCircle(vector, num4 * 0.56f, ImGui.GetColorU32(col), 24, Math.Max(1f, num3));
		if (!DrawGameIconImage(drawList, marker.IconId, vector2 + new Vector2(3f * num3), vector3 - new Vector2(3f * num3), fillBounds: true, bypassMissingRetry: true))
		{
			drawList.AddCircleFilled(vector, num4 * 0.22f, ImGui.GetColorU32(col), 16);
		}
		if (num2 != 0)
		{
			DrawWorldMarkerDirectionArrow(drawList, vector, direction, num4, ImGui.GetColorU32(col));
		}
		if (config.WorldMarkersShowLabels || config.WorldMarkersShowDistance)
		{
			string text = (config.WorldMarkersShowLabels ? marker.Label : string.Empty);
			if (config.WorldMarkersShowDistance)
			{
				text = (string.IsNullOrWhiteSpace(text) ? $"{num:0}m" : $"{text} · {num:0}m");
			}
			if (!string.IsNullOrWhiteSpace(text))
			{
				Vector2 vector4 = ImGui.CalcTextSize(text);
				Vector2 vector5 = new Vector2(vector.X - vector4.X * 0.5f, vector3.Y + 3f * num3);
				drawList.AddRectFilled(vector5 - new Vector2(4f * num3, 2f * num3), vector5 + vector4 + new Vector2(4f * num3, 2f * num3), ImGui.GetColorU32(WithOpacity(effectiveTheme.TooltipBg, worldMarkerAlpha * 0.72f)), 4f * num3);
				drawList.AddText(vector5, ImGui.GetColorU32(col), text);
			}
		}
	}

	private float GetWorldMarkerAlpha(float distance)
	{
		float num = Math.Max(0f, config.WorldMarkersFadeDistance);
		float num2 = Math.Max(1f, config.WorldMarkersFadeAttenuation);
		if (distance <= num)
		{
			return 1f;
		}
		return Math.Clamp(1f - (distance - num) / num2, 0f, 1f);
	}

	private static bool IsFinite(Vector2 value)
	{
		if (float.IsFinite(value.X))
		{
			return float.IsFinite(value.Y);
		}
		return false;
	}

	private static bool IsFinite(Vector3 value)
	{
		if (float.IsFinite(value.X) && float.IsFinite(value.Y))
		{
			return float.IsFinite(value.Z);
		}
		return false;
	}

	private static Vector2 GetWorldMarkerEdgePosition(ImGuiViewportPtr viewport, Vector2 projected, Vector3 markerPosition, Vector3 playerPosition, out Vector2 direction, Vector2? directionHint = null)
	{
		Vector2 vector = viewport.WorkPos + viewport.WorkSize * 0.5f;
		Vector2 workSize = viewport.WorkSize;
		if (!IsFinite(vector) || !IsFinite(workSize) || workSize.X <= 48f || workSize.Y <= 48f)
		{
			direction = new Vector2(0f, -1f);
			return vector;
		}
		direction = directionHint ?? (IsFinite(projected) ? (projected - vector) : Vector2.Zero);
		if (!IsFinite(direction) || direction.LengthSquared() < 0.001f)
		{
			direction = new Vector2(markerPosition.X - playerPosition.X, markerPosition.Z - playerPosition.Z);
		}
		if (!IsFinite(direction) || direction.LengthSquared() < 0.001f)
		{
			direction = new Vector2(0f, -1f);
		}
		direction = Vector2.Normalize(direction);
		float num = MathF.Min(workSize.X, workSize.Y) * 0.42f;
		Vector2 vector2 = vector + direction * num;
		float num2 = MathF.Min(32f, MathF.Min(workSize.X, workSize.Y) * 0.1f);
		return new Vector2(Math.Clamp(vector2.X, viewport.WorkPos.X + num2, viewport.WorkPos.X + workSize.X - num2), Math.Clamp(vector2.Y, viewport.WorkPos.Y + num2, viewport.WorkPos.Y + workSize.Y - num2));
	}

	private static void DrawWorldMarkerDirectionArrow(ImDrawListPtr drawList, Vector2 center, Vector2 direction, float iconSize, uint color)
	{
		if (IsFinite(direction) && !(direction.LengthSquared() < 0.001f))
		{
			direction = Vector2.Normalize(direction);
			Vector2 vector = new Vector2(0f - direction.Y, direction.X);
			Vector2 p = center + direction * (iconSize * 0.92f);
			Vector2 vector2 = center + direction * (iconSize * 0.48f);
			float num = MathF.Max(3f, iconSize * 0.18f);
			Vector2 p2 = vector2 + vector * num;
			Vector2 p3 = vector2 - vector * num;
			drawList.AddTriangleFilled(p, p2, p3, color);
		}
	}
}
