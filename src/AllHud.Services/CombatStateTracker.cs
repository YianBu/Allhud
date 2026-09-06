using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using AllHud.Data;
using AllHud.Models;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Party;
using Dalamud.Game.NativeWrapper;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace AllHud.Services;

public sealed class CombatStateTracker : IDisposable
{
	private readonly record struct CooldownKey(uint StatusId, ulong SourceObjectId, CooldownGroup Group);

	private readonly record struct PartyMemberSnapshot(int SortIndex, string Name, string JobName, uint ClassJobId, uint EntityId, ulong GameObjectId, bool IsLocalPlayer);

	private readonly record struct ReflectedPropertyCacheKey(Type Type, string Name, bool IgnoreCase);

	private readonly record struct ReflectedPropertyCacheValue(PropertyInfo? Property);

	private readonly record struct ObservedActionUse(uint CasterEntityId, uint ActionId, DateTime ObservedAt);

	private unsafe delegate void ReceiveActionEffectDelegate(uint casterEntityId, Character* casterPtr, Vector3* targetPos, ActionEffectHandler.Header* header, ActionEffectHandler.TargetEffects* effects, GameObjectId* targetEntityIds);

	private sealed class ObservedCooldown
	{
		public TrackedStatusDefinition Definition { get; set; }

		public uint IconId { get; set; }

		public string SourceName { get; set; }

		public string SourceJobName { get; set; }

		public ulong SourceObjectId { get; set; }

		public DateTime ReadyAt { get; set; }

		public DateTime LastSeenAt { get; set; }

		public DateTime ActiveUpdatedAt { get; set; }

		public bool IsActive { get; set; }

		public float ActiveRemainingSeconds { get; set; }

		public CooldownObservationKind ObservationKind { get; set; }

		public ObservedCooldown(TrackedStatusDefinition definition, uint iconId, string sourceName, string sourceJobName, ulong sourceObjectId, DateTime readyAt, DateTime lastSeenAt, bool isActive, float activeRemainingSeconds, CooldownObservationKind observationKind)
		{
			Definition = definition;
			IconId = iconId;
			SourceName = sourceName;
			SourceJobName = sourceJobName;
			SourceObjectId = sourceObjectId;
			ReadyAt = readyAt;
			LastSeenAt = lastSeenAt;
			IsActive = isActive;
			ActiveRemainingSeconds = activeRemainingSeconds;
			ActiveUpdatedAt = lastSeenAt;
			ObservationKind = observationKind;
		}

		public CooldownEntry ToEntry()
		{
			return new CooldownEntry(Definition.StatusId, Definition.ActionIds.FirstOrDefault(), IconId, Definition.Name, Definition.Group, Definition.CooldownSeconds, Definition.DurationSeconds, SourceName, SourceJobName, SourceObjectId, ReadyAt, LastSeenAt, IsActive, ActiveRemainingSeconds, ObservationKind);
		}
	}

	private readonly IDataManager dataManager;

	private readonly IClientState clientState;

	private readonly ICondition condition;

	private readonly IFramework framework;

	private readonly IObjectTable objectTable;

	private readonly IPartyList partyList;

	private readonly ITargetManager targetManager;

	private readonly IGameGui gameGui;

	private readonly IPluginLog log;

	private readonly ExcelSheet<Lumina.Excel.Sheets.Status> statusSheet;

	private readonly ExcelSheet<ClassJob> classJobSheet;

	private static readonly ConcurrentDictionary<ReflectedPropertyCacheKey, ReflectedPropertyCacheValue> ReflectedPropertyCache = new ConcurrentDictionary<ReflectedPropertyCacheKey, ReflectedPropertyCacheValue>();

	private readonly Dictionary<uint, float> maxDurations = new Dictionary<uint, float>();

	private readonly Dictionary<CooldownKey, ObservedCooldown> observedCooldowns = new Dictionary<CooldownKey, ObservedCooldown>();

	private readonly Dictionary<ulong, IGameObject> objectLookupByGameObjectId = new Dictionary<ulong, IGameObject>();

	private readonly Dictionary<ulong, IGameObject> objectLookupByEntityOrGameObjectId = new Dictionary<ulong, IGameObject>();

	private bool objectLookupActive;

	private readonly List<CooldownKey> expiredCooldownScratch = new List<CooldownKey>();

	private readonly Dictionary<uint, IReadOnlyList<JobActionCatalogEntry>> jobActionCatalogCache = new Dictionary<uint, IReadOnlyList<JobActionCatalogEntry>>();

	private readonly ConcurrentQueue<ObservedActionUse> observedActionUses = new ConcurrentQueue<ObservedActionUse>();

	private readonly ConcurrentQueue<ObservedActionUse> recentActionUses = new ConcurrentQueue<ObservedActionUse>();

	private readonly List<ObservedActionUse> observedActionLog = new List<ObservedActionUse>();

	private readonly List<RecentActionEntry> recentActions = new List<RecentActionEntry>();

	private readonly Hook<ReceiveActionEffectDelegate>? actionEffectHook;

	private readonly ExcelSheet<Lumina.Excel.Sheets.Action> actionSheet;

	private readonly ExcelSheet<Addon> addonSheet;

	private readonly string castFallbackName;

	private IReadOnlySet<uint> trackedActionIds = new HashSet<uint>(TrackedDefinitions.ByActionId.Keys);

	private uint lastLocalClassJobId;

	private bool wasLocalPlayerDead;

	private bool wasInDuty;

	private Vector2 pendingNativeContextMenuPosition;

	private int pendingNativeContextMenuRepositionFrames;

	private TaskBarSnapshot? cachedTaskBarSnapshot;

	private DateTime cachedTaskBarSnapshotExpiresAt;

	private IReadOnlyList<PartyCooldownGroupEntry>? cachedPartyCooldownTracking;

	private DateTime cachedPartyCooldownTrackingExpiresAt;

	private IReadOnlyList<PartyCooldownGroupEntry>? cachedPartyCooldownTrackingPreview;

	private DateTime cachedPartyCooldownTrackingPreviewExpiresAt;

	private string cachedPartyCooldownTrackingPreviewKey = string.Empty;

	private IReadOnlyList<StatusEntry>? cachedSelfHudStatuses;

	private DateTime cachedSelfHudStatusesExpiresAt;

	private IReadOnlyList<StatusEntry>? cachedTargetStatuses;

	private ulong cachedTargetStatusesObjectId;

	private DateTime cachedTargetStatusesExpiresAt;

	private const uint FoodStatusId = 48u;

	private const int MaxRecentActions = 40;

	private static readonly TimeSpan ObservedActionLogRetention = TimeSpan.FromSeconds(4.0);

	private const int MaxNativeStatusSlots = 60;

	private const ulong PreviewSourceObjectIdBase = 13336576uL;

	private const uint PreviewSourceEntityIdBase = 13336832u;

	private static readonly TimeSpan TaskBarSnapshotCacheDuration = TimeSpan.FromMilliseconds(250L);

	private static readonly TimeSpan PartyCooldownTrackingCacheDuration = TimeSpan.FromMilliseconds(150L);

	private static readonly TimeSpan PartyCooldownTrackingPreviewCacheDuration = TimeSpan.FromSeconds(1.0);

	private static readonly TimeSpan SelfHudStatusCacheDuration = TimeSpan.FromMilliseconds(100L);

	private static readonly TimeSpan TargetStatusCacheDuration = TimeSpan.FromMilliseconds(100L);

	public bool IsPartyCooldownTrackingActive => IsInDuty();

	public bool IsInDutyActive => IsInDuty();

	public bool IsPartyStatusTrackingActive => objectTable.LocalPlayer != null;

	public IReadOnlyList<RecentActionEntry> GetRecentActionCandidates()
	{
		ProcessRecentActionUses();
		return recentActions.ToList();
	}

	public IReadOnlyList<JobActionCatalogEntry> GetJobActionCatalog(uint classJobId)
	{
		if (jobActionCatalogCache.TryGetValue(classJobId, out IReadOnlyList<JobActionCatalogEntry> value))
		{
			return value;
		}
		List<JobActionCatalogEntry> list = new List<JobActionCatalogEntry>();
		foreach (TrackedActionDefinition knownSkill in TrackedActionCatalog.GetSkillsForJob(classJobId))
		{
			if (knownSkill.Definition.ActionIds.FirstOrDefault() != 0 && !list.Any((JobActionCatalogEntry action) => knownSkill.Definition.ActionIds.Contains(action.ActionId)))
			{
				list.Add(CreateKnownJobActionCatalogEntry(classJobId, knownSkill));
			}
		}
		value = (from action in list
			group action by action.ActionId into @group
			select @group.OrderByDescending((JobActionCatalogEntry action) => action.Level).First() into action
			orderby action.Level, action.Name
			select action).ToList();
		jobActionCatalogCache[classJobId] = value;
		return value;
	}

	public IReadOnlyList<JobActionCatalogEntry> GetCommonActionCatalog()
	{
		return (from skill in TrackedActionCatalog.CommonSkills
			select CreateKnownJobActionCatalogEntry(0u, skill) into action
			orderby action.Group, action.Name
			select action).ToList();
	}

	public uint GetClassJobIconId(uint classJobId)
	{
		if (classJobId == 0 || !classJobSheet.TryGetRow(classJobId, out var _))
		{
			return 0u;
		}
		return 62100 + classJobId;
	}

	private string ResolveClassJobName(uint classJobId)
	{
		if (classJobId == 0 || !classJobSheet.TryGetRow(classJobId, out var row))
		{
			return string.Empty;
		}
		string text = row.Name.ToString();
		if (!string.IsNullOrWhiteSpace(text))
		{
			return text;
		}
		return GetJobName(classJobId);
	}

	private JobActionCatalogEntry CreateKnownJobActionCatalogEntry(uint classJobId, TrackedActionDefinition skill)
	{
		uint num = skill.Definition.ActionIds.First();
		float num2 = skill.Definition.CooldownSeconds;
		uint actionIconId = GetActionIconId(num);
		uint level = 0u;
		if (actionSheet.TryGetRow(num, out var row))
		{
			object row2 = row;
			num2 = Math.Max(num2, GetActionCooldownSeconds(row2));
			level = GetNumericProperty(row2, "ClassJobLevel");
		}
		return new JobActionCatalogEntry(TrackedActionCatalog.GetActionKey(classJobId, num), classJobId, num, actionIconId, skill.Definition.Name, level, num2, 0u, skill.Definition.Group, skill.Definition.StatusId != 0, skill.IsSharedSkill);
	}

	public uint GetStatusIconId(uint statusId)
	{
		if (!statusSheet.TryGetRow(statusId, out var row))
		{
			return 0u;
		}
		return row.Icon;
	}

	private uint GetActionIconId(uint actionId)
	{
		if (!actionSheet.TryGetRow(actionId, out var row))
		{
			return 0u;
		}
		return row.Icon;
	}

	private string ResolveCastActionName(uint actionId)
	{
		if (actionId == 0)
		{
			return string.Empty;
		}
		if (actionSheet.TryGetRow(actionId, out var row))
		{
			string text = row.Name.ToString();
			if (!string.IsNullOrWhiteSpace(text))
			{
				return text;
			}
		}
		if (string.IsNullOrWhiteSpace(castFallbackName))
		{
			return $"Action {actionId}";
		}
		return castFallbackName;
	}

	private string GetActionName(uint actionId)
	{
		if (actionSheet.TryGetRow(actionId, out var row))
		{
			string text = row.Name.ToString();
			if (!string.IsNullOrWhiteSpace(text))
			{
				return text;
			}
		}
		return $"Action {actionId}";
	}

	private static bool IsPvPAction(object row)
	{
		if (!GetBoolProperty(row, "IsPvP"))
		{
			return GetBoolProperty(row, "IsPvPAction");
		}
		return true;
	}

	private static bool IsActionAvailableToClassJob(object actionRow, uint classJobId)
	{
		object rowReferenceValue = GetRowReferenceValue(actionRow, "ClassJobCategory");
		if (rowReferenceValue == null)
		{
			return false;
		}
		foreach (string classJobCategoryPropertyName in GetClassJobCategoryPropertyNames(classJobId))
		{
			if (GetBoolProperty(rowReferenceValue, classJobCategoryPropertyName))
			{
				return true;
			}
		}
		return false;
	}

	private static object? GetRowReferenceValue(object row, string propertyName)
	{
		object obj = FindProperty(row, propertyName, ignoreCase: false)?.GetValue(row);
		if (obj == null)
		{
			return null;
		}
		Type type = obj.GetType();
		string[] array = new string[2] { "ValueNullable", "Value" };
		foreach (string propertyName2 in array)
		{
			object obj2 = FindProperty(type, propertyName2, ignoreCase: false)?.GetValue(obj);
			if (obj2 != null && obj2 != obj)
			{
				return obj2;
			}
		}
		return obj;
	}

	private static IEnumerable<string> GetClassJobCategoryPropertyNames(uint classJobId)
	{
		foreach (string singleClassJobCategoryPropertyName in GetSingleClassJobCategoryPropertyNames(classJobId))
		{
			yield return singleClassJobCategoryPropertyName;
		}
	}

	private static IEnumerable<string> GetSingleClassJobCategoryPropertyNames(uint classJobId)
	{
		string[] array = classJobId switch
		{
			1u => new string[2] { "GLA", "Gladiator" }, 
			2u => new string[2] { "PGL", "Pugilist" }, 
			3u => new string[2] { "MRD", "Marauder" }, 
			4u => new string[2] { "LNC", "Lancer" }, 
			5u => new string[2] { "ARC", "Archer" }, 
			6u => new string[2] { "CNJ", "Conjurer" }, 
			7u => new string[2] { "THM", "Thaumaturge" }, 
			19u => new string[2] { "PLD", "Paladin" }, 
			20u => new string[2] { "MNK", "Monk" }, 
			21u => new string[2] { "WAR", "Warrior" }, 
			22u => new string[2] { "DRG", "Dragoon" }, 
			23u => new string[2] { "BRD", "Bard" }, 
			24u => new string[2] { "WHM", "WhiteMage" }, 
			25u => new string[2] { "BLM", "BlackMage" }, 
			26u => new string[2] { "ACN", "Arcanist" }, 
			27u => new string[2] { "SMN", "Summoner" }, 
			28u => new string[2] { "SCH", "Scholar" }, 
			29u => new string[2] { "ROG", "Rogue" }, 
			30u => new string[2] { "NIN", "Ninja" }, 
			31u => new string[2] { "MCH", "Machinist" }, 
			32u => new string[2] { "DRK", "DarkKnight" }, 
			33u => new string[2] { "AST", "Astrologian" }, 
			34u => new string[2] { "SAM", "Samurai" }, 
			35u => new string[2] { "RDM", "RedMage" }, 
			36u => new string[2] { "BLU", "BlueMage" }, 
			37u => new string[2] { "GNB", "Gunbreaker" }, 
			38u => new string[2] { "DNC", "Dancer" }, 
			39u => new string[2] { "RPR", "Reaper" }, 
			40u => new string[2] { "SGE", "Sage" }, 
			41u => new string[2] { "VPR", "Viper" }, 
			42u => new string[2] { "PCT", "Pictomancer" }, 
			_ => Array.Empty<string>(), 
		};
		for (int i = 0; i < array.Length; i++)
		{
			yield return array[i];
		}
	}

	private static float GetActionCooldownSeconds(object row)
	{
		uint numericProperty = GetNumericProperty(row, "Recast100ms");
		if (numericProperty != 0)
		{
			return (float)numericProperty / 10f;
		}
		float floatProperty = GetFloatProperty(row, "RecastSeconds");
		if (floatProperty > 0f)
		{
			return floatProperty;
		}
		float floatProperty2 = GetFloatProperty(row, "Recast");
		return Math.Max(0f, floatProperty2);
	}

	private static uint GetActionCooldownGroupId(object row)
	{
		string[] array = new string[2] { "AdditionalCooldownGroup", "EquivalenceGroup" };
		foreach (string propertyName in array)
		{
			uint numericProperty = GetNumericProperty(row, propertyName);
			if (numericProperty != 0)
			{
				return numericProperty;
			}
		}
		return 0u;
	}

	private static uint GetRowReferenceId(object row, string propertyName)
	{
		PropertyInfo propertyInfo = FindProperty(row, propertyName, ignoreCase: false);
		if ((object)propertyInfo == null)
		{
			return 0u;
		}
		return ExtractRowId(propertyInfo.GetValue(row));
	}

	private static uint ExtractRowId(object? value)
	{
		if (value == null)
		{
			return 0u;
		}
		if (TryConvertUInt(value, out var result))
		{
			return result;
		}
		Type type = value.GetType();
		string[] array = new string[2] { "RowId", "Id" };
		foreach (string propertyName in array)
		{
			PropertyInfo propertyInfo = FindProperty(type, propertyName, ignoreCase: false);
			if ((object)propertyInfo != null && TryConvertUInt(propertyInfo.GetValue(value), out result))
			{
				return result;
			}
		}
		array = new string[2] { "ValueNullable", "Value" };
		foreach (string propertyName2 in array)
		{
			object obj = FindProperty(type, propertyName2, ignoreCase: false)?.GetValue(value);
			if (obj != null && obj != value)
			{
				uint num = ExtractRowId(obj);
				if (num != 0)
				{
					return num;
				}
			}
		}
		return 0u;
	}

	private static uint GetNumericProperty(object row, string propertyName)
	{
		PropertyInfo propertyInfo = FindProperty(row, propertyName, ignoreCase: false);
		if ((object)propertyInfo == null || !TryConvertUInt(propertyInfo.GetValue(row), out var result))
		{
			return 0u;
		}
		return result;
	}

	private static float GetFloatProperty(object row, string propertyName)
	{
		object obj = FindProperty(row, propertyName, ignoreCase: false)?.GetValue(row);
		if (!(obj is float result))
		{
			if (!(obj is double num))
			{
				if (!(obj is decimal num2))
				{
					if (!(obj is byte b))
					{
						if (!(obj is sbyte b2))
						{
							if (!(obj is short num3))
							{
								if (!(obj is ushort num4))
								{
									if (!(obj is int num5))
									{
										if (!(obj is uint num6))
										{
											if (!(obj is long num7))
											{
												if (obj is ulong num8)
												{
													return num8;
												}
												return 0f;
											}
											return num7;
										}
										return num6;
									}
									return num5;
								}
								return (int)num4;
							}
							return num3;
						}
						return b2;
					}
					return (int)b;
				}
				return (float)num2;
			}
			return (float)num;
		}
		return result;
	}

	private static bool GetBoolProperty(object row, string propertyName)
	{
		object obj = FindProperty(row, propertyName)?.GetValue(row);
		if (obj is bool)
		{
			return (bool)obj;
		}
		return false;
	}

	private static bool TryGetBoolProperty(object row, out bool result, params string[] propertyNames)
	{
		foreach (string propertyName in propertyNames)
		{
			if (FindProperty(row, propertyName)?.GetValue(row) is bool flag)
			{
				result = flag;
				return true;
			}
		}
		result = false;
		return false;
	}

	private static bool TryGetUIntProperty(object row, out uint result, params string[] propertyNames)
	{
		foreach (string propertyName in propertyNames)
		{
			PropertyInfo propertyInfo = FindProperty(row, propertyName);
			if ((object)propertyInfo != null && TryConvertUInt(propertyInfo.GetValue(row), out result))
			{
				return true;
			}
		}
		result = 0u;
		return false;
	}

	private static bool TryGetFloatProperty(object row, out float result, params string[] propertyNames)
	{
		foreach (string propertyName in propertyNames)
		{
			PropertyInfo propertyInfo = FindProperty(row, propertyName);
			if ((object)propertyInfo != null)
			{
				object value = propertyInfo.GetValue(row);
				if (value is float num)
				{
					result = num;
					return true;
				}
				if (value is double num2)
				{
					result = (float)num2;
					return true;
				}
				if (value is decimal num3)
				{
					result = (float)num3;
					return true;
				}
				if (value is byte b)
				{
					result = (int)b;
					return true;
				}
				if (value is sbyte b2)
				{
					result = b2;
					return true;
				}
				if (value is short num4)
				{
					result = num4;
					return true;
				}
				if (value is ushort num5)
				{
					result = (int)num5;
					return true;
				}
				if (value is int num6)
				{
					result = num6;
					return true;
				}
				if (value is uint num7)
				{
					result = num7;
					return true;
				}
				if (value is long num8)
				{
					result = num8;
					return true;
				}
				if (value is ulong num9)
				{
					result = num9;
					return true;
				}
			}
		}
		result = 0f;
		return false;
	}

	private static PropertyInfo? FindProperty(object row, string propertyName, bool ignoreCase = true)
	{
		return FindProperty(row.GetType(), propertyName, ignoreCase);
	}

	private static PropertyInfo? FindProperty(Type type, string propertyName, bool ignoreCase = true)
	{
		ReflectedPropertyCacheKey key = new ReflectedPropertyCacheKey(type, propertyName, ignoreCase);
		return ReflectedPropertyCache.GetOrAdd(key, delegate(ReflectedPropertyCacheKey reflectedPropertyCacheKey)
		{
			StringComparison comparison = (reflectedPropertyCacheKey.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
			return new ReflectedPropertyCacheValue(reflectedPropertyCacheKey.Type.GetProperty(reflectedPropertyCacheKey.Name, BindingFlags.Instance | BindingFlags.Public) ?? reflectedPropertyCacheKey.Type.GetProperties(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((PropertyInfo candidate) => string.Equals(candidate.Name, reflectedPropertyCacheKey.Name, comparison)));
		}).Property;
	}

	private static bool TryConvertUInt(object? value, out uint result)
	{
		result = 0u;
		if (!(value is byte b))
		{
			if (!(value is sbyte b2))
			{
				if (!(value is short num))
				{
					if (value is ushort num2)
					{
						result = num2;
						return true;
					}
					if (!(value is int num3))
					{
						if (value is uint num4)
						{
							result = num4;
							return true;
						}
						if (!(value is long num5))
						{
							if (value is ulong num6 && num6 <= uint.MaxValue)
							{
								result = (uint)num6;
								return true;
							}
						}
						else if (num5 >= 0 && num5 <= uint.MaxValue)
						{
							result = (uint)num5;
							return true;
						}
					}
					else if (num3 >= 0)
					{
						result = (uint)num3;
						return true;
					}
				}
				else if (num >= 0)
				{
					result = (uint)num;
					return true;
				}
			}
			else if (b2 >= 0)
			{
				result = (uint)b2;
				return true;
			}
			return false;
		}
		result = b;
		return true;
	}

	private uint GetDefinitionIconId(TrackedStatusDefinition definition)
	{
		foreach (uint actionId in definition.ActionIds)
		{
			uint actionIconId = GetActionIconId(actionId);
			if (actionIconId != 0)
			{
				return actionIconId;
			}
		}
		uint statusIconId = GetStatusIconId(definition.StatusId);
		if (statusIconId != 0)
		{
			return statusIconId;
		}
		return 0u;
	}

	private static uint GetDefinitionKeyId(TrackedStatusDefinition definition)
	{
		if (definition.StatusId == 0)
		{
			return definition.ActionIds.FirstOrDefault();
		}
		return definition.StatusId;
	}

	private void UpsertCooldown(TrackedStatusDefinition definition, string sourceName, string sourceJobName, ulong sourceObjectId, DateTime readyAt, DateTime observedAt, bool isActive, float activeRemainingSeconds, CooldownObservationKind observationKind)
	{
		CooldownKey key = new CooldownKey(GetDefinitionKeyId(definition), sourceObjectId, definition.Group);
		uint definitionIconId = GetDefinitionIconId(definition);
		if (!observedCooldowns.TryGetValue(key, out ObservedCooldown value))
		{
			observedCooldowns[key] = new ObservedCooldown(definition, definitionIconId, sourceName, sourceJobName, sourceObjectId, readyAt, observedAt, isActive && activeRemainingSeconds > 0.05f, Math.Max(0f, activeRemainingSeconds), observationKind);
			return;
		}
		value.IconId = definitionIconId;
		value.Definition = definition;
		if (!string.IsNullOrWhiteSpace(sourceJobName))
		{
			value.SourceJobName = sourceJobName;
		}
		value.LastSeenAt = observedAt;
		if (isActive && activeRemainingSeconds > 0.05f)
		{
			value.IsActive = true;
			value.ActiveRemainingSeconds = activeRemainingSeconds;
			value.ActiveUpdatedAt = observedAt;
		}
		else if (isActive)
		{
			value.IsActive = false;
			value.ActiveRemainingSeconds = 0f;
			value.ActiveUpdatedAt = observedAt;
		}
		if (GetObservationPriority(observationKind) >= GetObservationPriority(value.ObservationKind) || value.ReadyAt <= DateTime.UtcNow)
		{
			value.SourceName = sourceName;
			value.SourceJobName = sourceJobName;
			value.SourceObjectId = sourceObjectId;
			value.ReadyAt = readyAt;
			value.ObservationKind = observationKind;
		}
	}

	private static int GetObservationPriority(CooldownObservationKind observationKind)
	{
		return observationKind switch
		{
			CooldownObservationKind.LocalRecast => 2, 
			CooldownObservationKind.ActionEvent => 1, 
			_ => 0, 
		};
	}

	private void RemoveExpiredCooldowns(Configuration config)
	{
		DateTime utcNow = DateTime.UtcNow;
		DateTime dateTime = utcNow.AddSeconds(0f - Math.Max(1f, config.ExpiredCooldownGraceSeconds));
		expiredCooldownScratch.Clear();
		foreach (KeyValuePair<CooldownKey, ObservedCooldown> observedCooldown in observedCooldowns)
		{
			ObservedCooldown value = observedCooldown.Value;
			if (value.IsActive)
			{
				if (value.ActiveRemainingSeconds > 0f)
				{
					value.ActiveRemainingSeconds = Math.Max(0f, value.ActiveRemainingSeconds - (float)(utcNow - value.ActiveUpdatedAt).TotalSeconds);
					value.ActiveUpdatedAt = utcNow;
				}
				if (value.ActiveRemainingSeconds <= 0.05f)
				{
					value.IsActive = false;
					value.ActiveRemainingSeconds = 0f;
				}
			}
			if (config.HideExpiredCooldowns && value.ReadyAt < dateTime)
			{
				expiredCooldownScratch.Add(observedCooldown.Key);
			}
		}
		foreach (CooldownKey item in expiredCooldownScratch)
		{
			observedCooldowns.Remove(item);
		}
		expiredCooldownScratch.Clear();
	}

	private unsafe void OnReceiveActionEffect(uint casterEntityId, Character* casterPtr, Vector3* targetPos, ActionEffectHandler.Header* header, ActionEffectHandler.TargetEffects* effects, GameObjectId* targetEntityIds)
	{
		try
		{
			if (header != null && header->ActionType == 1 && header->ActionId != 0)
			{
				ObservedActionUse item = new ObservedActionUse(casterEntityId, header->ActionId, DateTime.UtcNow);
				recentActionUses.Enqueue(item);
				if (trackedActionIds.Contains(header->ActionId))
				{
					observedActionUses.Enqueue(item);
				}
			}
		}
		catch (Exception exception)
		{
			log.Warning(exception, "处理技能释放监听时发生错误");
		}
		finally
		{
			actionEffectHook.Original(casterEntityId, casterPtr, targetPos, header, effects, targetEntityIds);
		}
	}

	public IReadOnlyList<CooldownEntry> GetCooldowns(Configuration config)
	{
		BeginObjectLookupScope();
		try
		{
			ProcessRecentActionUses();
			IReadOnlyList<TrackedStatusDefinition> cooldownDefinitions = GetCooldownDefinitions(config);
			IReadOnlyDictionary<uint, IReadOnlyList<TrackedStatusDefinition>> byStatusId = BuildStatusLookup(cooldownDefinitions);
			IReadOnlyDictionary<uint, IReadOnlyList<TrackedStatusDefinition>> readOnlyDictionary = BuildActionLookup(cooldownDefinitions);
			trackedActionIds = readOnlyDictionary.Keys.Concat(TrackedActionCatalog.PartyMitigationActionIds).ToHashSet();
			ProcessObservedActionUses(config, readOnlyDictionary);
			ObserveKnownCooldownStatuses(config, byStatusId);
			ObserveLocalActionCooldowns(config, cooldownDefinitions);
			RemoveExpiredCooldowns(config);
			return (from value in observedCooldowns.Values
				select value.ToEntry() into entry
				orderby entry.Group, entry.IsReady, entry.RemainingCooldownSeconds, entry.Name
				select entry).ToList();
		}
		finally
		{
			EndObjectLookupScope();
		}
	}

	public IReadOnlyList<PartyCooldownGroupEntry> GetPartyCooldownGroups(Configuration config)
	{
		if (!IsPartyCooldownTrackingActive)
		{
			return Array.Empty<PartyCooldownGroupEntry>();
		}
		BeginObjectLookupScope();
		try
		{
			ProcessRecentActionUses();
			IReadOnlyList<TrackedStatusDefinition> definitions = TrackedActionCatalog.PartyMitigationDefinitions;
			IReadOnlyDictionary<uint, IReadOnlyList<TrackedStatusDefinition>> byStatusId = BuildStatusLookup(definitions);
			IReadOnlyDictionary<uint, IReadOnlyList<TrackedStatusDefinition>> byActionId = BuildActionLookup(definitions);
			RefreshTrackedActionIds(config);
			ProcessObservedActionUses(config, byActionId);
			ObserveKnownCooldownStatuses(config, byStatusId);
			ObserveLocalActionCooldowns(config, definitions);
			RemoveExpiredCooldowns(config);
			List<CooldownEntry> observedEntries = observedCooldowns.Values.Select((ObservedCooldown value) => value.ToEntry()).ToList();
			return (from member in GetPartyMembers()
				select CreatePartyCooldownGroup(member, definitions, observedEntries, config) into @group
				where @group.Cooldowns.Count > 0
				select @group).ToList();
		}
		finally
		{
			EndObjectLookupScope();
		}
	}

	public IReadOnlyList<PartyCooldownGroupEntry> GetPartyCooldownGroupsPreview()
	{
		IReadOnlyList<TrackedStatusDefinition> partyMitigationDefinitions = TrackedActionCatalog.PartyMitigationDefinitions;
		uint[] array = TrackedActionCatalog.Jobs.Select((JobCatalogEntry job) => job.ClassJobId).ToArray();
		List<PartyCooldownGroupEntry> list = new List<PartyCooldownGroupEntry>();
		for (int num = 0; num < array.Length; num++)
		{
			PartyMemberSnapshot member = BuildPreviewMember(num, array[num]);
			List<CooldownEntry> cooldowns = BuildPreviewCooldownsForMember(member, partyMitigationDefinitions, num);
			list.Add(new PartyCooldownGroupEntry(member.Name, member.JobName, member.ClassJobId, GetClassJobIconId(member.ClassJobId), member.GameObjectId, member.EntityId, member.IsLocalPlayer, member.SortIndex, cooldowns));
		}
		return list;
	}

	public IReadOnlyList<PartyFoodStatusEntry> GetPartyFoodStatusesPreview()
	{
		uint statusIconId = GetStatusIconId(48u);
		uint[] array = TrackedActionCatalog.Jobs.Select((JobCatalogEntry job) => job.ClassJobId).ToArray();
		List<PartyFoodStatusEntry> list = new List<PartyFoodStatusEntry>();
		for (int num = 0; num < array.Length; num++)
		{
			PartyMemberSnapshot partyMemberSnapshot = BuildPreviewMember(num, array[num]);
			bool flag = num % 2 == 0;
			list.Add(new PartyFoodStatusEntry(partyMemberSnapshot.Name, partyMemberSnapshot.JobName, partyMemberSnapshot.GameObjectId, partyMemberSnapshot.EntityId, partyMemberSnapshot.IsLocalPlayer, partyMemberSnapshot.SortIndex, flag, statusIconId, "食物", flag ? (1200f - (float)num * 120f) : 0f, 1800f));
		}
		return list;
	}

	private PartyMemberSnapshot BuildPreviewMember(int slot, uint classJobId)
	{
		return new PartyMemberSnapshot(slot, $"预览队员{slot + 1}", GetJobName(classJobId), classJobId, (uint)(13336832uL + (ulong)slot), 13336576uL + (ulong)slot, slot == 0);
	}

	public IReadOnlyList<PartyCooldownGroupEntry> GetPartyCooldownTracking(Configuration config)
	{
		if (!IsPartyCooldownTrackingActive)
		{
			return Array.Empty<PartyCooldownGroupEntry>();
		}
		DateTime utcNow = DateTime.UtcNow;
		if (cachedPartyCooldownTracking != null && utcNow < cachedPartyCooldownTrackingExpiresAt)
		{
			return cachedPartyCooldownTracking;
		}
		BeginObjectLookupScope();
		try
		{
			ProcessRecentActionUses();
			List<TrackedStatusDefinition> definitions = (from @group in GetSelectedPartyDefinitions(config).GroupBy(GetDefinitionIdentity)
				select @group.First()).ToList();
			IReadOnlyDictionary<uint, IReadOnlyList<TrackedStatusDefinition>> byStatusId = BuildStatusLookup(definitions);
			IReadOnlyDictionary<uint, IReadOnlyList<TrackedStatusDefinition>> byActionId = BuildActionLookup(definitions);
			RefreshTrackedActionIds(config);
			ProcessObservedActionUses(config, byActionId, bypassEnabledGate: true);
			ObserveKnownCooldownStatuses(config, byStatusId, bypassEnabledGate: true);
			ObserveLocalActionCooldowns(config, definitions, bypassEnabledGate: true);
			RemoveExpiredCooldowns(config);
			List<CooldownEntry> observedEntries = observedCooldowns.Values.Select((ObservedCooldown value) => value.ToEntry()).ToList();
			HashSet<string> enabledKeys = GetEnabledActionKeySet(config);
			IReadOnlyList<PartyMemberSnapshot> source = GetPartyMembers();
			if (config.SelfCooldownBarSelfOnly)
			{
				source = source.Where((PartyMemberSnapshot member) => member.IsLocalPlayer).ToList();
			}
			else if (config.SelfCooldownBarHideSelf)
			{
				source = source.Where((PartyMemberSnapshot member) => !member.IsLocalPlayer).ToList();
			}
			cachedPartyCooldownTracking = (from member in source
				select CreatePartyTrackedGroup(member, definitions, observedEntries, enabledKeys) into @group
				where @group.Cooldowns.Count > 0
				orderby @group.PartySlot
				select @group).ToList();
			cachedPartyCooldownTrackingExpiresAt = utcNow + PartyCooldownTrackingCacheDuration;
			return cachedPartyCooldownTracking;
		}
		finally
		{
			EndObjectLookupScope();
		}
	}

	public IReadOnlyList<PartyCooldownGroupEntry> GetPartyCooldownTrackingPreview(Configuration config)
	{
		DateTime utcNow = DateTime.UtcNow;
		string partyCooldownTrackingPreviewCacheKey = GetPartyCooldownTrackingPreviewCacheKey(config);
		if (cachedPartyCooldownTrackingPreview != null && utcNow < cachedPartyCooldownTrackingPreviewExpiresAt && string.Equals(cachedPartyCooldownTrackingPreviewKey, partyCooldownTrackingPreviewCacheKey, StringComparison.Ordinal))
		{
			return cachedPartyCooldownTrackingPreview;
		}
		List<TrackedStatusDefinition> definitions = (from @group in GetSelectedPartyDefinitions(config).GroupBy(GetDefinitionIdentity)
			select @group.First()).ToList();
		HashSet<string> enabledActionKeySet = GetEnabledActionKeySet(config);
		IReadOnlyList<PartyMemberSnapshot> previewPartyMembers = GetPreviewPartyMembers(config, definitions, enabledActionKeySet);
		List<PartyCooldownGroupEntry> list = new List<PartyCooldownGroupEntry>();
		for (int num = 0; num < previewPartyMembers.Count; num++)
		{
			PartyMemberSnapshot member = previewPartyMembers[num];
			List<CooldownEntry> list2 = BuildPreviewCooldownsForMember(member, definitions, num, enabledActionKeySet);
			if (list2.Count > 0)
			{
				list.Add(new PartyCooldownGroupEntry(member.Name, member.JobName, member.ClassJobId, GetClassJobIconId(member.ClassJobId), member.GameObjectId, member.EntityId, member.IsLocalPlayer, member.SortIndex, list2));
			}
		}
		cachedPartyCooldownTrackingPreview = list;
		cachedPartyCooldownTrackingPreviewKey = partyCooldownTrackingPreviewCacheKey;
		cachedPartyCooldownTrackingPreviewExpiresAt = utcNow + PartyCooldownTrackingPreviewCacheDuration;
		return cachedPartyCooldownTrackingPreview;
	}

	private static string GetPartyCooldownTrackingPreviewCacheKey(Configuration config)
	{
		List<string> source = config.EnabledJobActionKeys ?? new List<string>();
		InlineArray4<object> buffer = default(InlineArray4<object>);
		buffer[0] = (config.SelfCooldownBarSelfOnly ? 1 : 0);
		buffer[1] = (config.SelfCooldownBarHideSelf ? 1 : 0);
		buffer[2] = config.SelectedJobSkillConfigClassJobId;
		buffer[3] = string.Join(',', source.OrderBy<string, string>((string key) => key, StringComparer.Ordinal));
		return string.Join('|', (ReadOnlySpan<object?>)buffer);
	}

	private IReadOnlyList<PartyMemberSnapshot> GetPreviewPartyMembers(Configuration config, IReadOnlyList<TrackedStatusDefinition> definitions, HashSet<string> enabledKeys)
	{
		if (config.SelfCooldownBarSelfOnly)
		{
			return new global::_003C_003Ez__ReadOnlySingleElementList<PartyMemberSnapshot>(BuildPreviewMember(0, config.SelectedJobSkillConfigClassJobId));
		}
		HashSet<uint> selectedJobIds = GetPreviewClassJobIds(config, definitions, enabledKeys).ToHashSet();
		List<PartyMemberSnapshot> list = (from member in GetPartyMembers(includeUnavailable: true)
			where selectedJobIds.Contains(member.ClassJobId)
			where !config.SelfCooldownBarHideSelf || !member.IsLocalPlayer
			orderby member.SortIndex
			select member).ToList();
		if (list.Count > 0)
		{
			return list;
		}
		return GetPreviewClassJobIds(config, definitions, enabledKeys).Select((uint classJobId, int index) => BuildPreviewMember(index, classJobId)).ToList();
	}

	private static IReadOnlyList<uint> GetPreviewClassJobIds(Configuration config, IReadOnlyList<TrackedStatusDefinition> definitions, HashSet<string> enabledKeys)
	{
		if (config.SelfCooldownBarSelfOnly)
		{
			return new global::_003C_003Ez__ReadOnlySingleElementList<uint>(config.SelectedJobSkillConfigClassJobId);
		}
		HashSet<uint> hashSet = (from classJobId in definitions.SelectMany((TrackedStatusDefinition definition) => definition.SourceClassJobIds)
			where classJobId != 0
			select classJobId).ToHashSet();
		return (from classJobId in TrackedActionCatalog.Jobs.Select((JobCatalogEntry job) => job.ClassJobId).Where(hashSet.Contains)
			where !config.SelfCooldownBarHideSelf || classJobId != config.SelectedJobSkillConfigClassJobId
			select classJobId).ToList();
	}

	private List<CooldownEntry> BuildPreviewCooldownsForMember(PartyMemberSnapshot member, IReadOnlyList<TrackedStatusDefinition> definitions, int memberIndex, HashSet<string>? enabledKeys = null)
	{
		DateTime now = DateTime.UtcNow;
		int cooldownIndex = 0;
		return (from definition in definitions
			where IsSourceClassJobAllowed(definition, member.ClassJobId)
			where definition.SourceClassJobIds.Count > 0
			where enabledKeys == null || IsDefinitionSelected(definition, member.ClassJobId, enabledKeys)
			group definition by (Group: definition.Group, StatusId: definition.StatusId, definition.ActionIds.FirstOrDefault()) into @group
			select @group.First()).Select(delegate(TrackedStatusDefinition definition)
		{
			float num = (((memberIndex + cooldownIndex) % 3 != 0) ? Math.Max(3f, definition.CooldownSeconds * (0.25f + 0.12f * (float)(cooldownIndex % 4 + 1))) : 0f);
			cooldownIndex++;
			return new CooldownEntry(definition.StatusId, definition.ActionIds.FirstOrDefault(), GetDefinitionIconId(definition), definition.Name, definition.Group, definition.CooldownSeconds, definition.DurationSeconds, member.Name, member.JobName, member.GameObjectId, now.AddSeconds(num), now, IsActive: false, 0f, CooldownObservationKind.StatusFallback);
		}).OrderBy<CooldownEntry, string>((CooldownEntry entry) => entry.Name, StringComparer.CurrentCulture).ThenBy((CooldownEntry entry) => entry.ActionId)
			.ToList();
	}

	private PartyCooldownGroupEntry CreatePartyTrackedGroup(PartyMemberSnapshot member, IReadOnlyList<TrackedStatusDefinition> definitions, IReadOnlyList<CooldownEntry> observedEntries, HashSet<string> enabledKeys)
	{
		List<CooldownEntry> cooldowns = (from definition in definitions
			where IsSourceClassJobAllowed(definition, member.ClassJobId)
			where definition.SourceClassJobIds.Count > 0
			where IsDefinitionSelected(definition, member.ClassJobId, enabledKeys)
			select FindObservedCooldown(member, definition, observedEntries) ?? CreateReadyCooldown(member, definition) into entry
			group entry by (Group: entry.Group, StatusId: entry.StatusId, ActionId: entry.ActionId) into @group
			select @group.First()).OrderBy<CooldownEntry, string>((CooldownEntry entry) => entry.Name, StringComparer.CurrentCulture).ThenBy((CooldownEntry entry) => entry.ActionId).ToList();
		return new PartyCooldownGroupEntry(member.Name, member.JobName, member.ClassJobId, GetClassJobIconId(member.ClassJobId), member.GameObjectId, member.EntityId, member.IsLocalPlayer, member.SortIndex, cooldowns);
	}

	private void RefreshTrackedActionIds(Configuration config)
	{
		trackedActionIds = TrackedActionCatalog.PartyMitigationActionIds.Concat(TrackedActionCatalog.PartyTrackedActionIds).Concat(GetSelectedActionIds(config)).Concat(GetSelectedPartyActionIds(config))
			.ToHashSet();
	}

	private IEnumerable<uint> GetSelectedPartyActionIds(Configuration config)
	{
		return from actionId in GetSelectedPartyDefinitions(config).SelectMany((TrackedStatusDefinition definition) => definition.ActionIds)
			where actionId != 0
			select actionId;
	}

	public IReadOnlyList<PartyFoodStatusEntry> GetPartyFoodStatuses(Configuration config)
	{
		if (!IsPartyStatusTrackingActive)
		{
			return Array.Empty<PartyFoodStatusEntry>();
		}
		uint foodIconId = GetStatusIconId(48u);
		BeginObjectLookupScope();
		try
		{
			return (from member in GetPartyMembers()
				select CreatePartyFoodStatusEntry(member, config, foodIconId)).ToList();
		}
		finally
		{
			EndObjectLookupScope();
		}
	}

	private PartyFoodStatusEntry CreatePartyFoodStatusEntry(PartyMemberSnapshot member, Configuration config, uint foodIconId)
	{
		IGameObject? obj = ResolveObjectByEntityOrGameObjectId(member.EntityId) ?? ResolveObjectByEntityOrGameObjectId((uint)member.GameObjectId);
		StatusEntry statusEntry = null;
		if (obj is IBattleChara battleChara)
		{
			statusEntry = (from status in ScanBattleChara(battleChara, member.GameObjectId, member.Name, config)
				where status.StatusId == 48
				orderby status.RemainingSeconds descending
				select status).FirstOrDefault();
		}
		return new PartyFoodStatusEntry(member.Name, member.JobName, member.GameObjectId, member.EntityId, member.IsLocalPlayer, member.SortIndex, (object)statusEntry != null, ((statusEntry?.IconId ?? 0) != 0) ? statusEntry.IconId : foodIconId, statusEntry?.Name ?? "椋熺墿", statusEntry?.RemainingSeconds ?? 0f, statusEntry?.MaxSeconds ?? 0f);
	}

	private PartyCooldownGroupEntry CreatePartyCooldownGroup(PartyMemberSnapshot member, IReadOnlyList<TrackedStatusDefinition> definitions, IReadOnlyList<CooldownEntry> observedEntries, Configuration config)
	{
		List<CooldownEntry> cooldowns = (from definition in definitions
			where IsDefinitionEnabled(definition, config)
			where IsSourceClassJobAllowed(definition, member.ClassJobId)
			select FindObservedCooldown(member, definition, observedEntries) ?? CreateReadyCooldown(member, definition) into entry
			orderby entry.Group, entry.IsReady, entry.RemainingCooldownSeconds, entry.Name
			select entry).ToList();
		return new PartyCooldownGroupEntry(member.Name, member.JobName, member.ClassJobId, GetClassJobIconId(member.ClassJobId), member.GameObjectId, member.EntityId, member.IsLocalPlayer, member.SortIndex, cooldowns);
	}

	private CooldownEntry CreateReadyCooldown(PartyMemberSnapshot member, TrackedStatusDefinition definition)
	{
		return new CooldownEntry(definition.StatusId, definition.ActionIds.FirstOrDefault(), GetDefinitionIconId(definition), definition.Name, definition.Group, definition.CooldownSeconds, definition.DurationSeconds, member.Name, member.JobName, (member.GameObjectId != 0L) ? member.GameObjectId : member.EntityId, DateTime.UtcNow, DateTime.MinValue, IsActive: false, 0f, CooldownObservationKind.StatusFallback);
	}

	private static CooldownEntry? FindObservedCooldown(PartyMemberSnapshot member, TrackedStatusDefinition definition, IEnumerable<CooldownEntry> observedEntries)
	{
		return observedEntries.FirstOrDefault((CooldownEntry entry) => IsSameCooldownDefinition(entry, definition) && IsSameCooldownSource(entry, member));
	}

	private static bool IsSameCooldownDefinition(CooldownEntry entry, TrackedStatusDefinition definition)
	{
		if (entry.Group != definition.Group)
		{
			return false;
		}
		if (definition.StatusId != 0)
		{
			return entry.StatusId == definition.StatusId;
		}
		if (entry.ActionId != 0)
		{
			return definition.ActionIds.Contains(entry.ActionId);
		}
		return false;
	}

	private static bool IsSameCooldownSource(CooldownEntry entry, PartyMemberSnapshot member)
	{
		if (entry.SourceObjectId != 0L)
		{
			if (entry.SourceObjectId != member.GameObjectId)
			{
				return entry.SourceObjectId == member.EntityId;
			}
			return true;
		}
		return false;
	}

	private IReadOnlyList<PartyMemberSnapshot> GetPartyMembers(bool includeUnavailable = false)
	{
		List<PartyMemberSnapshot> list = new List<PartyMemberSnapshot>();
		IPlayerCharacter localPlayer = objectTable.LocalPlayer;
		ulong num = localPlayer?.GameObjectId ?? 0;
		uint num2 = localPlayer?.EntityId ?? 0;
		int num3 = 0;
		foreach (IPartyMember party in partyList)
		{
			IGameObject gameObject = party.GameObject;
			uint entityId = party.EntityId;
			ulong num4 = gameObject?.GameObjectId ?? entityId;
			uint rowId = party.ClassJob.RowId;
			bool isLocalPlayer = (num != 0L && num4 == num) || (num2 != 0 && entityId == num2);
			if (rowId != 0 && (includeUnavailable || gameObject != null))
			{
				list.Add(new PartyMemberSnapshot(num3, party.Name.ToString(), GetJobName(rowId), rowId, entityId, num4, isLocalPlayer));
			}
			num3++;
		}
		if (localPlayer != null && !list.Any((PartyMemberSnapshot member) => member.IsLocalPlayer))
		{
			uint rowId2 = localPlayer.ClassJob.RowId;
			list.Insert(0, new PartyMemberSnapshot(-1, localPlayer.Name.ToString(), GetJobName(rowId2), rowId2, localPlayer.EntityId, localPlayer.GameObjectId, IsLocalPlayer: true));
		}
		return (from member in list
			group member by (member.GameObjectId == 0L) ? member.EntityId : member.GameObjectId into @group
			select @group.First() into member
			orderby member.IsLocalPlayer descending, member.SortIndex
			select member).ToList();
	}

	private void ObserveKnownCooldownStatuses(Configuration config, IReadOnlyDictionary<uint, IReadOnlyList<TrackedStatusDefinition>> byStatusId, bool bypassEnabledGate = false)
	{
		ulong localPlayerId = objectTable.LocalPlayer?.GameObjectId ?? 0;
		foreach (IGameObject item in objectTable)
		{
			if (item is IBattleChara battleChara)
			{
				ObserveDefinitionsOnBattleChara(battleChara, localPlayerId, battleChara.Name.ToString(), config, byStatusId, bypassEnabledGate);
			}
		}
		if (targetManager.Target is IBattleChara battleChara2)
		{
			ObserveDefinitionsOnBattleChara(battleChara2, localPlayerId, battleChara2.Name.ToString(), config, byStatusId, bypassEnabledGate);
		}
	}

	private unsafe void ObserveDefinitionsOnBattleChara(IBattleChara battleChara, ulong localPlayerId, string holderName, Configuration config, IReadOnlyDictionary<uint, IReadOnlyList<TrackedStatusDefinition>> byStatusId, bool bypassEnabledGate = false)
	{
		BattleChara* address = (BattleChara*)battleChara.Address;
		if (address == null)
		{
			return;
		}
		ref StatusManager reference = ref address->StatusManager;
		int num = Math.Clamp((int)reference.NumValidStatuses, 0, 60);
		for (int i = 0; i < num; i++)
		{
			ref FFXIVClientStructs.FFXIV.Client.Game.Status reference2 = ref reference.Status[i];
			ushort statusId = reference2.StatusId;
			if (statusId == 0 || !byStatusId.TryGetValue(statusId, out IReadOnlyList<TrackedStatusDefinition> value))
			{
				continue;
			}
			ulong num2 = reference2.SourceObject.Id;
			if (num2 == 0L || num2 == ulong.MaxValue)
			{
				num2 = battleChara.GameObjectId;
			}
			string sourceName = ResolveObjectName(num2) ?? holderName;
			string text = ResolveObjectJobName(num2);
			uint num3 = ResolveObjectClassJobId(num2);
			if (string.IsNullOrWhiteSpace(text) && num2 == battleChara.GameObjectId)
			{
				text = GetCharacterJobName(battleChara);
			}
			if (num3 == 0 && num2 == battleChara.GameObjectId)
			{
				num3 = GetCharacterClassJobId(battleChara);
			}
			float num4 = Math.Max(0f, reference2.RemainingTime);
			if (num4 <= 0.05f)
			{
				continue;
			}
			foreach (TrackedStatusDefinition item in value)
			{
				if ((bypassEnabledGate || IsDefinitionEnabled(item, config)) && IsSourceClassJobAllowed(item, num3))
				{
					float maxDuration = GetMaxDuration(statusId, num4, isPermanent: false);
					float num5 = Math.Max(item.DurationSeconds, maxDuration);
					float num6 = Math.Max(0f, item.CooldownSeconds - num5 + num4);
					DateTime utcNow = DateTime.UtcNow;
					DateTime readyAt = utcNow.AddSeconds(num6);
					UpsertCooldown(item, sourceName, text, num2, readyAt, utcNow, isActive: true, num4, CooldownObservationKind.StatusFallback);
				}
			}
		}
	}

	private void ProcessObservedActionUses(Configuration config, IReadOnlyDictionary<uint, IReadOnlyList<TrackedStatusDefinition>> byActionId, bool bypassEnabledGate = false)
	{
		ObservedActionUse result;
		while (observedActionUses.TryDequeue(out result))
		{
			observedActionLog.Add(result);
		}
		DateTime cutoff = DateTime.UtcNow - ObservedActionLogRetention;
		observedActionLog.RemoveAll((ObservedActionUse entry) => entry.ObservedAt < cutoff);
		foreach (ObservedActionUse item in observedActionLog)
		{
			if (!byActionId.TryGetValue(item.ActionId, out IReadOnlyList<TrackedStatusDefinition> value))
			{
				continue;
			}
			IGameObject? gameObject = ResolveObjectByEntityOrGameObjectId(item.CasterEntityId);
			ulong sourceObjectId = gameObject?.GameObjectId ?? item.CasterEntityId;
			string sourceName = gameObject?.Name.ToString() ?? $"#{item.CasterEntityId:X8}";
			string characterJobName = GetCharacterJobName(gameObject);
			uint characterClassJobId = GetCharacterClassJobId(gameObject);
			foreach (TrackedStatusDefinition item2 in value)
			{
				if ((bypassEnabledGate || IsDefinitionEnabled(item2, config)) && IsSourceClassJobAllowed(item2, characterClassJobId))
				{
					UpsertCooldown(item2, sourceName, characterJobName, sourceObjectId, item.ObservedAt.AddSeconds(item2.CooldownSeconds), item.ObservedAt, isActive: false, 0f, CooldownObservationKind.ActionEvent);
				}
			}
		}
	}

	private void ProcessRecentActionUses()
	{
		while (true)
		{
			if (recentActionUses.TryDequeue(out var observedActionUse))
			{
				IGameObject gameObject = ResolveObjectByEntityOrGameObjectId(observedActionUse.CasterEntityId);
				ulong sourceObjectId = gameObject?.GameObjectId ?? observedActionUse.CasterEntityId;
				string sourceName = gameObject?.Name.ToString() ?? $"#{observedActionUse.CasterEntityId:X8}";
				string characterJobName = GetCharacterJobName(gameObject);
				recentActions.RemoveAll((RecentActionEntry action) => action.ActionId == observedActionUse.ActionId && action.SourceObjectId == sourceObjectId);
				recentActions.Insert(0, new RecentActionEntry(observedActionUse.ActionId, GetActionIconId(observedActionUse.ActionId), GetActionName(observedActionUse.ActionId), sourceName, characterJobName, sourceObjectId, observedActionUse.ObservedAt));
				if (recentActions.Count > 40)
				{
					recentActions.RemoveRange(40, recentActions.Count - 40);
				}
				continue;
			}
			break;
		}
	}

	private void ObserveLocalActionCooldowns(Configuration config, IReadOnlyList<TrackedStatusDefinition> definitions, bool bypassEnabledGate = false)
	{
		IPlayerCharacter localPlayer = objectTable.LocalPlayer;
		if (localPlayer == null)
		{
			return;
		}
		uint characterClassJobId = GetCharacterClassJobId(localPlayer);
		foreach (TrackedStatusDefinition item in definitions.Where((TrackedStatusDefinition definition) => bypassEnabledGate || IsDefinitionEnabled(definition, config)))
		{
			if (!IsSourceClassJobAllowed(item, characterClassJobId))
			{
				continue;
			}
			foreach (uint actionId in item.ActionIds)
			{
				float localActionCooldownRemaining = GetLocalActionCooldownRemaining(actionId);
				if (!(localActionCooldownRemaining <= 0.05f))
				{
					DateTime utcNow = DateTime.UtcNow;
					UpsertCooldown(item, localPlayer.Name.ToString(), GetCharacterJobName(localPlayer), localPlayer.GameObjectId, utcNow.AddSeconds(localActionCooldownRemaining), utcNow, isActive: false, 0f, CooldownObservationKind.LocalRecast);
					break;
				}
			}
		}
	}

	private unsafe IReadOnlyList<StatusEntry> ScanBattleChara(IBattleChara battleChara, ulong localPlayerId, string holderName, Configuration config)
	{
		List<StatusEntry> list = new List<StatusEntry>();
		BattleChara* address = (BattleChara*)battleChara.Address;
		if (address == null)
		{
			return list;
		}
		ref StatusManager reference = ref address->StatusManager;
		int num = Math.Clamp((int)reference.NumValidStatuses, 0, 60);
		for (int i = 0; i < num; i++)
		{
			ref FFXIVClientStructs.FFXIV.Client.Game.Status reference2 = ref reference.Status[i];
			if (reference2.StatusId != 0 && statusSheet.TryGetRow(reference2.StatusId, out var row))
			{
				float remainingTime = reference2.RemainingTime;
				if (!(remainingTime <= 0f) || row.IsPermanent)
				{
					float maxDuration = GetMaxDuration(reference2.StatusId, remainingTime, row.IsPermanent);
					ulong id = reference2.SourceObject.Id;
					string sourceName = ResolveObjectName(id) ?? "Unknown";
					string sourceJobName = ResolveObjectJobName(id);
					TryGetBoolProperty(row, out var result, "CanDispel", "CanEsuna");
					TryGetBoolProperty(row, out var result2, "PartyListPriority", "PartyListPrio", "PriorityPartyList");
					list.Add(new StatusEntry(reference2.StatusId, row.Icon, GetStatusDisplayName(reference2.StatusId, row.Name.ToString(), config), holderName, sourceName, sourceJobName, id, row.IsPermanent ? 99999f : remainingTime, maxDuration, row.StatusCategory == 1, localPlayerId != 0L && id == localPlayerId, i, result, result2));
				}
			}
		}
		return list;
	}

	private float GetMaxDuration(uint statusId, float remaining, bool isPermanent)
	{
		if (isPermanent)
		{
			return 99999f;
		}
		if (!maxDurations.TryGetValue(statusId, out var value) || remaining > value)
		{
			value = remaining;
			maxDurations[statusId] = value;
		}
		return Math.Max(value, 1f);
	}

	private void BeginObjectLookupScope()
	{
		objectLookupByGameObjectId.Clear();
		objectLookupByEntityOrGameObjectId.Clear();
		foreach (IGameObject item in objectTable)
		{
			if (item != null)
			{
				objectLookupByGameObjectId.TryAdd(item.GameObjectId, item);
				objectLookupByEntityOrGameObjectId.TryAdd(item.EntityId, item);
				objectLookupByEntityOrGameObjectId.TryAdd(item.GameObjectId, item);
			}
		}
		objectLookupActive = true;
	}

	private void EndObjectLookupScope()
	{
		objectLookupActive = false;
		objectLookupByGameObjectId.Clear();
		objectLookupByEntityOrGameObjectId.Clear();
	}

	private IGameObject? LookupByGameObjectId(ulong objectId)
	{
		if (objectLookupActive)
		{
			if (!objectLookupByGameObjectId.TryGetValue(objectId, out IGameObject value))
			{
				return null;
			}
			return value;
		}
		foreach (IGameObject item in objectTable)
		{
			if (item != null && item.GameObjectId == objectId)
			{
				return item;
			}
		}
		return null;
	}

	private string? ResolveObjectName(ulong objectId)
	{
		if (objectId == 0L || objectId == ulong.MaxValue)
		{
			return null;
		}
		return LookupByGameObjectId(objectId)?.Name.ToString();
	}

	private string ResolveObjectJobName(ulong objectId)
	{
		if (objectId == 0L || objectId == ulong.MaxValue)
		{
			return string.Empty;
		}
		return GetCharacterJobName(LookupByGameObjectId(objectId));
	}

	private uint ResolveObjectClassJobId(ulong objectId)
	{
		if (objectId == 0L || objectId == ulong.MaxValue)
		{
			return 0u;
		}
		return GetCharacterClassJobId(LookupByGameObjectId(objectId));
	}

	private IGameObject? ResolveObjectByEntityOrGameObjectId(uint id)
	{
		if (objectLookupActive)
		{
			if (!objectLookupByEntityOrGameObjectId.TryGetValue(id, out IGameObject value))
			{
				return null;
			}
			return value;
		}
		foreach (IGameObject item in objectTable)
		{
			if (item != null && (item.EntityId == id || item.GameObjectId == id))
			{
				return item;
			}
		}
		return null;
	}

	private static string GetCharacterJobName(IGameObject? gameObject)
	{
		if (!(gameObject is ICharacter { ClassJob: var classJob }))
		{
			return string.Empty;
		}
		return GetJobName(classJob.RowId);
	}

	private static uint GetCharacterClassJobId(IGameObject? gameObject)
	{
		if (!(gameObject is ICharacter { ClassJob: var classJob }))
		{
			return 0u;
		}
		return classJob.RowId;
	}

	private static string GetJobName(uint classJobId)
	{
		return classJobId switch
		{
			1u => "剑术师", 
			2u => "格斗家", 
			3u => "斧术师", 
			4u => "枪术师", 
			5u => "弓箭手", 
			6u => "幻术师", 
			7u => "咒术师", 
			19u => "骑士", 
			20u => "武僧", 
			21u => "战士", 
			22u => "龙骑士", 
			23u => "吟游诗人", 
			24u => "白魔法师", 
			25u => "黑魔法师", 
			26u => "秘术师", 
			27u => "召唤师", 
			28u => "学者", 
			29u => "双剑师", 
			30u => "忍者", 
			31u => "机工士", 
			32u => "暗黑骑士", 
			33u => "占星术士", 
			34u => "武士", 
			35u => "赤魔法师", 
			36u => "青魔法师", 
			37u => "绝枪战士", 
			38u => "舞者", 
			39u => "钐镰客", 
			40u => "贤者", 
			41u => "蝰蛇剑士", 
			42u => "绘灵法师", 
			_ => string.Empty, 
		};
	}

	private IReadOnlyList<TrackedStatusDefinition> GetCooldownDefinitions(Configuration config)
	{
		TrackedActionCatalog.EnsureActionSelectionInitialized(config);
		return (from @group in (from definition in GetSelectedJobActionDefinitions(config).Concat(GetCustomCooldownDefinitions(config))
				where IsDefinitionEnabled(definition, config)
				select definition).GroupBy(GetDefinitionIdentity)
			select @group.First()).ToList();
	}

	private IEnumerable<uint> GetSelectedActionIds(Configuration config)
	{
		return from actionId in GetCooldownDefinitions(config).SelectMany((TrackedStatusDefinition definition) => definition.ActionIds)
			where actionId != 0
			select actionId;
	}

	private IEnumerable<TrackedStatusDefinition> GetSelectedJobActionDefinitions(Configuration config)
	{
		IEnumerable<string> enabledJobActionKeys = config.EnabledJobActionKeys;
		foreach (string item in enabledJobActionKeys ?? Enumerable.Empty<string>())
		{
			if (!TryParseActionKey(item, out var classJobId, out var actionId) || TrackedActionCatalog.PartyMitigationActionIds.Contains(actionId))
			{
				continue;
			}
			if (classJobId == 0)
			{
				TrackedActionDefinition trackedActionDefinition = TrackedActionCatalog.FindCommonSkill(actionId);
				if (trackedActionDefinition != null)
				{
					yield return trackedActionDefinition.Definition;
				}
				continue;
			}
			TrackedActionDefinition trackedActionDefinition2 = TrackedActionCatalog.FindKnownSkill(classJobId, actionId);
			if (trackedActionDefinition2 != null)
			{
				if (IsVisibleCooldownGroup(trackedActionDefinition2.Definition.Group))
				{
					yield return trackedActionDefinition2.Definition;
				}
				continue;
			}
			JobActionCatalogEntry jobActionCatalogEntry = GetJobActionCatalog(classJobId).FirstOrDefault((JobActionCatalogEntry entry) => entry.ActionId == actionId);
			if ((object)jobActionCatalogEntry != null && !(jobActionCatalogEntry.CooldownSeconds <= 0.05f) && IsVisibleCooldownGroup(jobActionCatalogEntry.Group))
			{
				yield return new TrackedStatusDefinition(0u, jobActionCatalogEntry.Name, jobActionCatalogEntry.Group, jobActionCatalogEntry.CooldownSeconds, 0f, isTargetDebuff: false, new uint[1] { actionId }, new uint[1] { classJobId });
			}
		}
	}

	private IEnumerable<TrackedStatusDefinition> GetSelectedPartyDefinitions(Configuration config)
	{
		TrackedActionCatalog.EnsureActionSelectionInitialized(config);
		IEnumerable<string> enabledJobActionKeys = config.EnabledJobActionKeys;
		foreach (string item in enabledJobActionKeys ?? Enumerable.Empty<string>())
		{
			if (!TryParseActionKey(item, out var classJobId, out var actionId))
			{
				continue;
			}
			if (classJobId == 0)
			{
				TrackedActionDefinition trackedActionDefinition = TrackedActionCatalog.FindCommonSkill(actionId);
				if (trackedActionDefinition != null && IsPartyVisibleDefinition(trackedActionDefinition.Definition))
				{
					yield return trackedActionDefinition.Definition;
				}
				continue;
			}
			TrackedActionDefinition trackedActionDefinition2 = TrackedActionCatalog.FindKnownSkill(classJobId, actionId);
			if (trackedActionDefinition2 != null)
			{
				if (IsPartyVisibleDefinition(trackedActionDefinition2.Definition))
				{
					yield return trackedActionDefinition2.Definition;
				}
				continue;
			}
			JobActionCatalogEntry jobActionCatalogEntry = GetJobActionCatalog(classJobId).FirstOrDefault((JobActionCatalogEntry entry) => entry.ActionId == actionId);
			if ((object)jobActionCatalogEntry != null && !(jobActionCatalogEntry.CooldownSeconds <= 0.05f) && IsPartyVisibleAction(jobActionCatalogEntry))
			{
				yield return new TrackedStatusDefinition(0u, jobActionCatalogEntry.Name, jobActionCatalogEntry.Group, jobActionCatalogEntry.CooldownSeconds, 0f, isTargetDebuff: false, new uint[1] { actionId }, new uint[1] { classJobId });
			}
		}
	}

	private static IEnumerable<TrackedStatusDefinition> GetCustomCooldownDefinitions(Configuration config)
	{
		IEnumerable<CustomTrackedDefinition> customTrackedDefinitions = config.CustomTrackedDefinitions;
		foreach (CustomTrackedDefinition item in customTrackedDefinitions ?? Enumerable.Empty<CustomTrackedDefinition>())
		{
			if (item.Enabled && IsCooldownType(item.Type) && (item.StatusId != 0 || item.ActionId != 0))
			{
				CooldownGroup cooldownGroup = ((item.Type == CustomTrackType.RaidBuffCooldown) ? CooldownGroup.RaidBuff : CooldownGroup.PartyMitigation);
				string name = (string.IsNullOrWhiteSpace(item.Name) ? "定制技能" : item.Name.Trim());
				IReadOnlyList<uint> actionIds = ((item.ActionId == 0) ? ((IReadOnlyList<uint>)Array.Empty<uint>()) : ((IReadOnlyList<uint>)new uint[1] { item.ActionId }));
				yield return new TrackedStatusDefinition(item.StatusId, name, cooldownGroup, Math.Max(0f, item.CooldownSeconds), Math.Max(0f, item.DurationSeconds), item.Type == CustomTrackType.MitigationCooldown, actionIds);
			}
		}
	}

	private static string GetDefinitionIdentity(TrackedStatusDefinition definition)
	{
		uint value = definition.ActionIds.FirstOrDefault();
		return $"{definition.Group}:{definition.StatusId}:{value}:{string.Join(',', definition.SourceClassJobIds)}";
	}

	private static bool TryParseActionKey(string key, out uint classJobId, out uint actionId)
	{
		classJobId = 0u;
		actionId = 0u;
		if (string.IsNullOrWhiteSpace(key))
		{
			return false;
		}
		string[] array = key.Split(':', 2);
		if (array.Length == 2 && uint.TryParse(array[0], out classJobId) && uint.TryParse(array[1], out actionId))
		{
			return actionId != 0;
		}
		return false;
	}

	private static IReadOnlyDictionary<uint, IReadOnlyList<TrackedStatusDefinition>> BuildStatusLookup(IEnumerable<TrackedStatusDefinition> definitions)
	{
		return (from definition in definitions
			where definition.StatusId != 0
			group definition by definition.StatusId).ToDictionary((Func<IGrouping<uint, TrackedStatusDefinition>, uint>)((IGrouping<uint, TrackedStatusDefinition> group) => group.Key), (Func<IGrouping<uint, TrackedStatusDefinition>, IReadOnlyList<TrackedStatusDefinition>>)((IGrouping<uint, TrackedStatusDefinition> group) => group.ToList()));
	}

	private static IReadOnlyDictionary<uint, IReadOnlyList<TrackedStatusDefinition>> BuildActionLookup(IEnumerable<TrackedStatusDefinition> definitions)
	{
		return (from pair in definitions.SelectMany((TrackedStatusDefinition definition) => definition.ActionIds.Select((uint actionId) => new { actionId, definition }))
			where pair.actionId != 0
			group pair by pair.actionId).ToDictionary(group => group.Key, group => (IReadOnlyList<TrackedStatusDefinition>)group.Select(pair => pair.definition).ToList());
	}

	private static bool IsTrackedTargetStatus(uint statusId, Configuration config)
	{
		if (statusId != 0)
		{
			if (!TrackedActionCatalog.GetEnabledDefinitions(config).Any((TrackedStatusDefinition definition) => definition.StatusId == statusId) && !IsCustomStatusType(statusId, config, CustomTrackType.TargetStatus) && !IsCustomStatusType(statusId, config, CustomTrackType.RaidBuffCooldown))
			{
				return IsCustomStatusType(statusId, config, CustomTrackType.MitigationCooldown);
			}
			return true;
		}
		return false;
	}

	private static bool IsCustomStatusType(uint statusId, Configuration config, CustomTrackType type)
	{
		if (statusId != 0)
		{
			IEnumerable<CustomTrackedDefinition> customTrackedDefinitions = config.CustomTrackedDefinitions;
			return (customTrackedDefinitions ?? Enumerable.Empty<CustomTrackedDefinition>()).Any((CustomTrackedDefinition custom) => custom.Enabled && custom.StatusId == statusId && custom.Type == type);
		}
		return false;
	}

	private static bool IsCooldownType(CustomTrackType type)
	{
		if ((uint)type <= 1u)
		{
			return true;
		}
		return false;
	}

	private static string GetStatusDisplayName(uint statusId, string fallback, Configuration config)
	{
		IEnumerable<CustomTrackedDefinition> customTrackedDefinitions = config.CustomTrackedDefinitions;
		string text = (from custom in customTrackedDefinitions ?? Enumerable.Empty<CustomTrackedDefinition>()
			where custom.Enabled && custom.StatusId == statusId && !string.IsNullOrWhiteSpace(custom.Name)
			select custom.Name.Trim()).FirstOrDefault();
		if (!string.IsNullOrWhiteSpace(text))
		{
			return text;
		}
		string text2 = (from definition in TrackedActionCatalog.GetEnabledDefinitions(config)
			where definition.StatusId == statusId && !string.IsNullOrWhiteSpace(definition.Name)
			select definition.Name).FirstOrDefault();
		if (!string.IsNullOrWhiteSpace(text2))
		{
			return text2;
		}
		return fallback;
	}

	private static bool IsSourceClassJobAllowed(TrackedStatusDefinition definition, uint sourceClassJobId)
	{
		if (definition.SourceClassJobIds.Count != 0 && sourceClassJobId != 0)
		{
			return definition.SourceClassJobIds.Contains(sourceClassJobId);
		}
		return true;
	}

	private static HashSet<string> GetEnabledActionKeySet(Configuration config)
	{
		IEnumerable<string> enabledJobActionKeys = config.EnabledJobActionKeys;
		return (enabledJobActionKeys ?? Enumerable.Empty<string>()).ToHashSet<string>(StringComparer.Ordinal);
	}

	private static bool IsDefinitionSelected(TrackedStatusDefinition definition, uint classJobId, HashSet<string> enabledKeys)
	{
		bool flag = false;
		foreach (uint actionId in definition.ActionIds)
		{
			if (actionId == 0)
			{
				continue;
			}
			flag = true;
			if (enabledKeys.Contains(TrackedActionCatalog.GetCommonActionKey(actionId)))
			{
				return true;
			}
			foreach (uint classJobFamilyId in TrackedActionCatalog.GetClassJobFamilyIds(classJobId))
			{
				if (enabledKeys.Contains(TrackedActionCatalog.GetActionKey(classJobFamilyId, actionId)))
				{
					return true;
				}
			}
		}
		return !flag;
	}

	private static bool IsDefinitionEnabled(TrackedStatusDefinition definition, Configuration config)
	{
		bool flag = config.ShowPartyMitigationCooldowns || config.ShowTargetMitigationCooldowns || config.ShowPersonalMitigationCooldowns || config.ShowMitigationCooldowns;
		return definition.Group switch
		{
			CooldownGroup.Common => false, 
			CooldownGroup.Burst => false, 
			CooldownGroup.PartyMitigation => flag, 
			CooldownGroup.TargetMitigation => flag, 
			CooldownGroup.PersonalMitigation => flag, 
			CooldownGroup.Personal => false, 
			CooldownGroup.RaidBuff => false, 
			CooldownGroup.Mitigation => flag, 
			_ => true, 
		};
	}

	private static bool IsVisibleCooldownGroup(CooldownGroup group)
	{
		if ((uint)(group - 3) <= 4u)
		{
			return true;
		}
		return false;
	}

	private static bool IsPartyVisibleGroup(CooldownGroup group)
	{
		CooldownGroup cooldownGroup = ((group == CooldownGroup.TargetMitigation) ? CooldownGroup.PartyMitigation : group);
		if ((uint)(cooldownGroup - 1) <= 1u || (uint)(cooldownGroup - 5) <= 2u)
		{
			return true;
		}
		return false;
	}

	private static bool IsPartyVisibleDefinition(TrackedStatusDefinition definition)
	{
		if (!IsPartyVisibleGroup(definition.Group))
		{
			if (definition.Group == CooldownGroup.Common)
			{
				return definition.ActionIds.Any(TrackedActionCatalog.IndependentMonitorCommonActionIds.Contains);
			}
			return false;
		}
		return true;
	}

	private static bool IsPartyVisibleAction(JobActionCatalogEntry action)
	{
		if (!IsPartyVisibleGroup(action.Group))
		{
			if (action.Group == CooldownGroup.Common)
			{
				return TrackedActionCatalog.IndependentMonitorCommonActionIds.Contains(action.ActionId);
			}
			return false;
		}
		return true;
	}

	private unsafe static float GetLocalActionCooldownRemaining(uint actionId)
	{
		ActionManager* ptr = ActionManager.Instance();
		if (ptr == null)
		{
			return 0f;
		}
		uint adjustedActionId = ptr->GetAdjustedActionId(actionId);
		float localActionCooldownRemaining = GetLocalActionCooldownRemaining(ptr, adjustedActionId);
		if (localActionCooldownRemaining <= 0.05f && adjustedActionId != actionId)
		{
			localActionCooldownRemaining = GetLocalActionCooldownRemaining(ptr, actionId);
		}
		return localActionCooldownRemaining;
	}

	private unsafe static float GetLocalActionCooldownRemaining(ActionManager* actionManager, uint actionId)
	{
		if (actionId == 0 || !actionManager->IsRecastTimerActive(ActionType.Action, actionId))
		{
			return 0f;
		}
		float recastTime = actionManager->GetRecastTime(ActionType.Action, actionId);
		float recastTimeElapsed = actionManager->GetRecastTimeElapsed(ActionType.Action, actionId);
		return Math.Max(0f, recastTime - recastTimeElapsed);
	}

	public unsafe CombatStateTracker(IDataManager dataManager, IClientState clientState, ICondition condition, IFramework framework, IObjectTable objectTable, IPartyList partyList, ITargetManager targetManager, IGameGui gameGui, IGameInteropProvider gameInteropProvider, IPluginLog log)
	{
		this.dataManager = dataManager;
		this.clientState = clientState;
		this.condition = condition;
		this.framework = framework;
		this.objectTable = objectTable;
		this.partyList = partyList;
		this.targetManager = targetManager;
		this.gameGui = gameGui;
		this.log = log;
		statusSheet = this.dataManager.GetExcelSheet<Lumina.Excel.Sheets.Status>();
		classJobSheet = this.dataManager.GetExcelSheet<ClassJob>();
		actionSheet = this.dataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>();
		addonSheet = this.dataManager.GetExcelSheet<Addon>();
		castFallbackName = ((addonSheet.TryGetRow(1032u, out var row) && !string.IsNullOrWhiteSpace(row.Text.ToString())) ? row.Text.ToString() : string.Empty);
		try
		{
			actionEffectHook = gameInteropProvider.HookFromAddress<ReceiveActionEffectDelegate>(ActionEffectHandler.Addresses.Receive.Value, OnReceiveActionEffect);
			actionEffectHook.Enable();
		}
		catch (Exception exception)
		{
			this.log.Warning(exception, "无法启用技能释放监听，队友 CD 将只使用状态估算");
		}
		InitializeLifecycleSnapshot();
		this.clientState.TerritoryChanged += OnTerritoryChanged;
		this.condition.ConditionChange += OnConditionChanged;
		this.framework.Update += OnFrameworkUpdate;
	}

	public void Dispose()
	{
		clientState.TerritoryChanged -= OnTerritoryChanged;
		condition.ConditionChange -= OnConditionChanged;
		framework.Update -= OnFrameworkUpdate;
		actionEffectHook?.Dispose();
	}

	public TaskBarSnapshot? GetTaskBarSnapshot()
	{
		DateTime utcNow = DateTime.UtcNow;
		if ((object)cachedTaskBarSnapshot != null && utcNow < cachedTaskBarSnapshotExpiresAt)
		{
			return cachedTaskBarSnapshot;
		}
		IPlayerCharacter localPlayer = objectTable.LocalPlayer;
		if (localPlayer == null)
		{
			cachedTaskBarSnapshot = null;
			return null;
		}
		uint rowId = localPlayer.ClassJob.RowId;
		cachedTaskBarSnapshot = new TaskBarSnapshot(localPlayer.Name.ToString(), ResolveClassJobName(rowId), rowId, localPlayer.CurrentHp, localPlayer.MaxHp, localPlayer.CurrentMp, localPlayer.MaxMp, clientState.TerritoryType, $"区域 #{clientState.TerritoryType}");
		cachedTaskBarSnapshotExpiresAt = utcNow.Add(TaskBarSnapshotCacheDuration);
		return cachedTaskBarSnapshot;
	}

	private void InitializeLifecycleSnapshot()
	{
		IPlayerCharacter localPlayer = objectTable.LocalPlayer;
		lastLocalClassJobId = localPlayer?.ClassJob.RowId ?? 0;
		wasLocalPlayerDead = localPlayer != null && localPlayer.CurrentHp == 0;
		wasInDuty = IsInDuty();
	}

	private void OnTerritoryChanged(uint territoryType)
	{
		ResetObservedCooldowns($"territory changed: {territoryType}");
		InitializeLifecycleSnapshot();
	}

	private void OnConditionChanged(ConditionFlag flag, bool value)
	{
		if ((flag == ConditionFlag.BoundByDuty || flag == ConditionFlag.BoundByDuty56 || flag == ConditionFlag.InDeepDungeon) ? true : false)
		{
			ResetObservedCooldowns($"duty condition changed: {flag}={value}");
			wasInDuty = IsInDuty();
		}
	}

	private void OnFrameworkUpdate(IFramework framework)
	{
		ProcessPendingNativeContextMenuPosition();
		IPlayerCharacter localPlayer = objectTable.LocalPlayer;
		if (localPlayer != null)
		{
			bool flag = IsInDuty();
			if (flag != wasInDuty)
			{
				ResetObservedCooldowns($"duty state changed: {wasInDuty} -> {flag}");
				wasInDuty = flag;
			}
			uint rowId = localPlayer.ClassJob.RowId;
			if (rowId != 0 && lastLocalClassJobId != 0 && rowId != lastLocalClassJobId)
			{
				ResetObservedCooldowns($"class/job changed: {lastLocalClassJobId} -> {rowId}");
			}
			if (rowId != 0)
			{
				lastLocalClassJobId = rowId;
			}
			bool flag2 = localPlayer.CurrentHp == 0;
			if (flag2 && !wasLocalPlayerDead)
			{
				ResetObservedCooldowns("local player died");
			}
			wasLocalPlayerDead = flag2;
		}
	}

	private void ResetObservedCooldowns(string reason)
	{
		observedCooldowns.Clear();
		ObservedActionUse result;
		while (observedActionUses.TryDequeue(out result))
		{
		}
		cachedPartyCooldownTracking = null;
		cachedSelfHudStatuses = null;
		cachedTargetStatuses = null;
		log.Debug("已重置队伍技能冷却记录：" + reason);
	}

	private bool IsInDuty()
	{
		return condition.Any(ConditionFlag.BoundByDuty, ConditionFlag.BoundByDuty56, ConditionFlag.InDeepDungeon);
	}

	public IReadOnlyList<StatusEntry> GetSelfStatuses(Configuration config)
	{
		IPlayerCharacter localPlayer = objectTable.LocalPlayer;
		if (localPlayer == null)
		{
			return Array.Empty<StatusEntry>();
		}
		return (from status in ScanBattleChara(localPlayer, localPlayer.GameObjectId, localPlayer.Name.ToString(), config)
			where status.IsBuff || config.ShowRawStatusIds || IsCustomStatusType(status.StatusId, config, CustomTrackType.SelfStatus)
			orderby status.IsSelfApplied descending, status.RemainingSeconds
			select status).ToList();
	}

	public IReadOnlyList<StatusEntry> GetSelfHudStatuses(Configuration config)
	{
		IPlayerCharacter localPlayer = objectTable.LocalPlayer;
		if (localPlayer == null)
		{
			cachedSelfHudStatuses = null;
			return Array.Empty<StatusEntry>();
		}
		DateTime utcNow = DateTime.UtcNow;
		if (cachedSelfHudStatuses != null && utcNow < cachedSelfHudStatusesExpiresAt)
		{
			return cachedSelfHudStatuses;
		}
		cachedSelfHudStatuses = (from status in ScanBattleChara(localPlayer, localPlayer.GameObjectId, localPlayer.Name.ToString(), config)
			orderby status.StatusIndex, status.RemainingSeconds
			select status).ToList();
		cachedSelfHudStatusesExpiresAt = utcNow + SelfHudStatusCacheDuration;
		return cachedSelfHudStatuses;
	}

	public IReadOnlyList<StatusEntry> GetSelfStatusCandidates(Configuration config)
	{
		IPlayerCharacter localPlayer = objectTable.LocalPlayer;
		if (localPlayer == null)
		{
			return Array.Empty<StatusEntry>();
		}
		return (from status in ScanBattleChara(localPlayer, localPlayer.GameObjectId, localPlayer.Name.ToString(), config)
			where status.IsBuff
			orderby status.IsSelfApplied descending, status.RemainingSeconds
			select status).ToList();
	}

	public IReadOnlyList<StatusEntry> GetTargetStatuses(Configuration config)
	{
		if (!(targetManager.Target is IBattleChara battleChara))
		{
			return Array.Empty<StatusEntry>();
		}
		ulong localPlayerId = objectTable.LocalPlayer?.GameObjectId ?? 0;
		string holderName = battleChara.Name.ToString();
		return (from status in ScanBattleChara(battleChara, localPlayerId, holderName, config)
			where !status.IsBuff
			where !config.OnlyShowSelfAppliedTargetStatuses || status.IsSelfApplied || IsTrackedTargetStatus(status.StatusId, config)
			where config.ShowRawStatusIds || status.MaxSeconds <= config.MaxTargetStatusDurationSeconds || IsTrackedTargetStatus(status.StatusId, config)
			orderby status.IsSelfApplied descending, status.RemainingSeconds
			select status).ToList();
	}

	public IReadOnlyList<StatusEntry> GetTargetStatusCandidates(Configuration config)
	{
		if (!(targetManager.Target is IBattleChara battleChara))
		{
			return Array.Empty<StatusEntry>();
		}
		ulong localPlayerId = objectTable.LocalPlayer?.GameObjectId ?? 0;
		return (from status in ScanBattleChara(battleChara, localPlayerId, battleChara.Name.ToString(), config)
			where !status.IsBuff
			orderby status.IsSelfApplied descending, status.RemainingSeconds
			select status).ToList();
	}

	public TargetInfoEntry? GetTargetInfo(Configuration config)
	{
		if (!TryGetAvailableWorldPlayer(out IPlayerCharacter player))
		{
			return null;
		}
		if (!(targetManager.Target is IBattleChara battleChara))
		{
			return null;
		}
		if (!IsObjectInCurrentTable(battleChara.GameObjectId))
		{
			return null;
		}
		ulong gameObjectId = player.GameObjectId;
		IReadOnlyList<StatusEntry> statuses = GetCachedTargetStatuses(battleChara, gameObjectId, config);
		uint num = (battleChara.IsCasting ? battleChara.CastActionId : 0u);
		TargetOfTargetEntry targetOfTarget = CreateTargetOfTargetEntry(battleChara.TargetObject);
		return new TargetInfoEntry(battleChara.GameObjectId, battleChara.BaseId, battleChara.Name.ToString(), battleChara.Level, battleChara.CurrentHp, battleChara.MaxHp, battleChara.IsCasting, battleChara.IsCastInterruptible, num, ResolveCastActionName(num), battleChara.IsCasting ? battleChara.CurrentCastTime : 0f, battleChara.IsCasting ? battleChara.TotalCastTime : 0f, targetOfTarget, statuses);
	}

	private IReadOnlyList<StatusEntry> GetCachedTargetStatuses(IBattleChara target, ulong playerId, Configuration config)
	{
		DateTime utcNow = DateTime.UtcNow;
		if (cachedTargetStatuses != null && cachedTargetStatusesObjectId == target.GameObjectId && utcNow < cachedTargetStatusesExpiresAt)
		{
			return cachedTargetStatuses;
		}
		cachedTargetStatuses = (from status in ScanBattleChara(target, playerId, target.Name.ToString(), config)
			where !config.OnlyShowSelfAppliedTargetStatuses || status.IsSelfApplied || status.IsBuff || IsTrackedTargetStatus(status.StatusId, config)
			where config.ShowRawStatusIds || status.MaxSeconds <= config.MaxTargetStatusDurationSeconds || IsTrackedTargetStatus(status.StatusId, config) || status.IsBuff
			orderby status.IsSelfApplied descending, status.IsBuff ? 1 : 0, status.StatusIndex, status.RemainingSeconds
			select status).ToList();
		cachedTargetStatusesObjectId = target.GameObjectId;
		cachedTargetStatusesExpiresAt = utcNow + TargetStatusCacheDuration;
		return cachedTargetStatuses;
	}

	private bool TryGetAvailableWorldPlayer([NotNullWhen(true)] out IPlayerCharacter? player)
	{
		player = objectTable.LocalPlayer;
		if (player != null && player.GameObjectId != 0L && player.GameObjectId != ulong.MaxValue && clientState.TerritoryType != 0)
		{
			return !condition.Any(ConditionFlag.BetweenAreas, ConditionFlag.BetweenAreas51);
		}
		return false;
	}

	private bool IsObjectInCurrentTable(ulong objectId)
	{
		if (objectId == 0L || objectId == ulong.MaxValue)
		{
			return false;
		}
		foreach (IGameObject item in objectTable)
		{
			if (item != null && item.GameObjectId == objectId && item.Address != IntPtr.Zero)
			{
				return true;
			}
		}
		return false;
	}

	public void SelectTargetOfCurrentTarget()
	{
		IGameObject gameObject = targetManager.Target?.TargetObject;
		if (gameObject != null)
		{
			targetManager.Target = gameObject;
		}
	}

	public bool SelectTargetByObjectId(ulong objectId)
	{
		if (objectId == 0L || objectId == ulong.MaxValue)
		{
			return false;
		}
		foreach (IGameObject item in objectTable)
		{
			if (item != null && item.GameObjectId == objectId)
			{
				targetManager.Target = item;
				return true;
			}
		}
		return false;
	}

	public unsafe bool OpenNativeContextMenuByObjectId(ulong objectId, Vector2? menuPosition = null)
	{
		if (objectId == 0L || objectId == ulong.MaxValue)
		{
			return false;
		}
		AgentHUD* agentHUD = AgentModule.Instance()->GetAgentHUD();
		if (agentHUD == null)
		{
			return false;
		}
		foreach (IGameObject item in objectTable)
		{
			if (item != null && item.GameObjectId == objectId && item.Address != IntPtr.Zero)
			{
				agentHUD->OpenContextMenuFromTarget((GameObject*)item.Address);
				if (menuPosition.HasValue)
				{
					Vector2 valueOrDefault = menuPosition.GetValueOrDefault();
					pendingNativeContextMenuPosition = valueOrDefault;
					pendingNativeContextMenuRepositionFrames = 6;
					ProcessPendingNativeContextMenuPosition();
				}
				return true;
			}
		}
		return false;
	}

	private void ProcessPendingNativeContextMenuPosition()
	{
		if (pendingNativeContextMenuRepositionFrames > 0)
		{
			pendingNativeContextMenuRepositionFrames--;
			TryMoveNativeContextMenuAddon("ContextMenu", pendingNativeContextMenuPosition);
			TryMoveNativeContextMenuAddon("ContextIconMenu", pendingNativeContextMenuPosition);
			TryMoveNativeContextMenuAddon("AddonContextSub", pendingNativeContextMenuPosition);
		}
	}

	private unsafe void TryMoveNativeContextMenuAddon(string addonName, Vector2 position)
	{
		AtkUnitBasePtr addonByName = gameGui.GetAddonByName(addonName);
		if (!addonByName.IsNull)
		{
			AtkUnitBase* address = (AtkUnitBase*)addonByName.Address;
			if (address != null && address->IsVisible)
			{
				short x = (short)Math.Clamp(MathF.Round(position.X), -32768f, 32767f);
				short y = (short)Math.Clamp(MathF.Round(position.Y), -32768f, 32767f);
				address->SetPosition(x, y);
			}
		}
	}

	private static TargetOfTargetEntry? CreateTargetOfTargetEntry(IGameObject? targetOfTarget)
	{
		if (!(targetOfTarget is ICharacter { MaxHp: not 0u } character))
		{
			return null;
		}
		return new TargetOfTargetEntry(character.GameObjectId, character.Name.ToString(), character.CurrentHp, character.MaxHp);
	}
}
