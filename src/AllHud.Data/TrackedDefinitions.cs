using System.Collections.Generic;
using System.Linq;

namespace AllHud.Data;

public static class TrackedDefinitions
{
	public static readonly IReadOnlyList<TrackedStatusDefinition> RaidBuffs = new global::_003C_003Ez__ReadOnlyArray<TrackedStatusDefinition>(new TrackedStatusDefinition[11]
	{
		new TrackedStatusDefinition(141u, "战斗之声", CooldownGroup.RaidBuff, 120f, 20f, isTargetDebuff: false, new uint[1] { 118u }),
		new TrackedStatusDefinition(786u, "战斗连祷", CooldownGroup.RaidBuff, 120f, 20f, isTargetDebuff: false, new uint[1] { 3557u }),
		new TrackedStatusDefinition(1185u, "义结金兰", CooldownGroup.RaidBuff, 120f, 20f, isTargetDebuff: false, new uint[1] { 7396u }),
		new TrackedStatusDefinition(1221u, "连环计", CooldownGroup.RaidBuff, 120f, 15f, isTargetDebuff: true, new uint[1] { 7436u }),
		new TrackedStatusDefinition(1239u, "鼓励", CooldownGroup.RaidBuff, 120f, 20f, isTargetDebuff: false, new uint[1] { 7520u }),
		new TrackedStatusDefinition(1825u, "进攻之探戈", CooldownGroup.Burst, 120f, 20f, isTargetDebuff: false, new uint[1] { 16011u }),
		new TrackedStatusDefinition(1822u, "技术舞步结束", CooldownGroup.RaidBuff, 120f, 20f, isTargetDebuff: false, new uint[1] { 15998u }),
		new TrackedStatusDefinition(1878u, "占卜", CooldownGroup.RaidBuff, 120f, 20f, isTargetDebuff: false, new uint[1] { 16552u }),
		new TrackedStatusDefinition(2599u, "神秘环", CooldownGroup.RaidBuff, 120f, 20f, isTargetDebuff: false, new uint[1] { 24405u }),
		new TrackedStatusDefinition(2703u, "灼热之光", CooldownGroup.RaidBuff, 120f, 30f, isTargetDebuff: false, new uint[1] { 25801u }),
		new TrackedStatusDefinition(3849u, "介毒之术", CooldownGroup.RaidBuff, 120f, 20f, isTargetDebuff: true, new uint[1] { 36957u })
	});

	public static readonly IReadOnlyList<TrackedStatusDefinition> Mitigations = new global::_003C_003Ez__ReadOnlyArray<TrackedStatusDefinition>(new TrackedStatusDefinition[52]
	{
		new TrackedStatusDefinition(1193u, "雪仇", CooldownGroup.PartyMitigation, 60f, 15f, isTargetDebuff: true, new uint[1] { 7535u }),
		new TrackedStatusDefinition(1195u, "牵制", CooldownGroup.PartyMitigation, 90f, 10f, isTargetDebuff: true, new uint[1] { 7549u }),
		new TrackedStatusDefinition(1203u, "昏乱", CooldownGroup.PartyMitigation, 90f, 10f, isTargetDebuff: true, new uint[1] { 7560u }),
		new TrackedStatusDefinition(1191u, "铁壁", CooldownGroup.PersonalMitigation, 90f, 20f, isTargetDebuff: false, new uint[1] { 7531u }),
		new TrackedStatusDefinition(82u, "神圣领域", CooldownGroup.PersonalMitigation, 420f, 10f, isTargetDebuff: false, new uint[1] { 30u }),
		new TrackedStatusDefinition(2674u, "圣盾阵", CooldownGroup.PersonalMitigation, 5f, 8f, isTargetDebuff: false, new uint[1] { 25746u }),
		new TrackedStatusDefinition(3829u, "极致防御", CooldownGroup.PersonalMitigation, 120f, 15f, isTargetDebuff: false, new uint[2] { 36920u, 17u }),
		new TrackedStatusDefinition(1174u, "干预", CooldownGroup.PersonalMitigation, 10f, 8f, isTargetDebuff: false, new uint[1] { 7382u }),
		new TrackedStatusDefinition(3832u, "戮罪", CooldownGroup.PersonalMitigation, 120f, 15f, isTargetDebuff: false, new uint[2] { 36923u, 44u }),
		new TrackedStatusDefinition(87u, "战栗", CooldownGroup.Personal, 90f, 10f, isTargetDebuff: false, new uint[1] { 40u }),
		new TrackedStatusDefinition(409u, "死斗", CooldownGroup.PersonalMitigation, 240f, 10f, isTargetDebuff: false, new uint[1] { 43u }),
		new TrackedStatusDefinition(2678u, "原初的血气", CooldownGroup.PersonalMitigation, 25f, 8f, isTargetDebuff: false, new uint[2] { 25751u, 16464u }),
		new TrackedStatusDefinition(3835u, "暗影守夜", CooldownGroup.PersonalMitigation, 120f, 15f, isTargetDebuff: false, new uint[2] { 36927u, 3636u }),
		new TrackedStatusDefinition(746u, "弃明投暗", CooldownGroup.PersonalMitigation, 60f, 10f, isTargetDebuff: false, new uint[1] { 3634u }),
		new TrackedStatusDefinition(810u, "行尸走肉", CooldownGroup.PersonalMitigation, 300f, 10f, isTargetDebuff: false, new uint[1] { 3638u }),
		new TrackedStatusDefinition(1178u, "至黑之夜", CooldownGroup.PersonalMitigation, 15f, 7f, isTargetDebuff: false, new uint[1] { 7393u }),
		new TrackedStatusDefinition(2682u, "献奉", CooldownGroup.PersonalMitigation, 60f, 10f, isTargetDebuff: false, new uint[1] { 25754u }),
		new TrackedStatusDefinition(1832u, "伪装", CooldownGroup.PersonalMitigation, 90f, 20f, isTargetDebuff: false, new uint[1] { 16140u }),
		new TrackedStatusDefinition(3838u, "大星云", CooldownGroup.PersonalMitigation, 120f, 15f, isTargetDebuff: false, new uint[2] { 36935u, 16148u }),
		new TrackedStatusDefinition(1836u, "超火流星", CooldownGroup.PersonalMitigation, 360f, 10f, isTargetDebuff: false, new uint[1] { 16152u }),
		new TrackedStatusDefinition(1835u, "极光", CooldownGroup.PersonalMitigation, 60f, 18f, isTargetDebuff: false, new uint[1] { 16151u }),
		new TrackedStatusDefinition(2683u, "刚玉之心", CooldownGroup.PersonalMitigation, 25f, 8f, isTargetDebuff: false, new uint[1] { 25758u }),
		new TrackedStatusDefinition(168u, "魔罩", CooldownGroup.PersonalMitigation, 120f, 20f, isTargetDebuff: false, new uint[1] { 157u }),
		new TrackedStatusDefinition(2708u, "水流幕", CooldownGroup.PersonalMitigation, 60f, 8f, isTargetDebuff: false, new uint[1] { 25861u }),
		new TrackedStatusDefinition(1218u, "神祝祷", CooldownGroup.PersonalMitigation, 30f, 15f, isTargetDebuff: false, new uint[1] { 7432u }),
		new TrackedStatusDefinition(1220u, "深谋远虑之策", CooldownGroup.PersonalMitigation, 45f, 45f, isTargetDebuff: false, new uint[1] { 7434u }),
		new TrackedStatusDefinition(2710u, "生命回生法", CooldownGroup.PersonalMitigation, 60f, 10f, isTargetDebuff: false, new uint[1] { 25867u }),
		new TrackedStatusDefinition(1889u, "天星交错", CooldownGroup.PersonalMitigation, 30f, 30f, isTargetDebuff: false, new uint[1] { 16556u }),
		new TrackedStatusDefinition(2717u, "擢升", CooldownGroup.PersonalMitigation, 60f, 8f, isTargetDebuff: false, new uint[1] { 25873u }),
		new TrackedStatusDefinition(2619u, "白牛清汁", CooldownGroup.PersonalMitigation, 45f, 15f, isTargetDebuff: false, new uint[1] { 24303u }),
		new TrackedStatusDefinition(2612u, "输血", CooldownGroup.PersonalMitigation, 120f, 15f, isTargetDebuff: false, new uint[1] { 24305u }),
		new TrackedStatusDefinition(2622u, "混合", CooldownGroup.PersonalMitigation, 60f, 10f, isTargetDebuff: false, new uint[1] { 24317u }),
		new TrackedStatusDefinition(2702u, "灿烂之盾", CooldownGroup.PersonalMitigation, 60f, 30f, isTargetDebuff: false, new uint[1] { 25799u }),
		new TrackedStatusDefinition(726u, "圣光幕帘", CooldownGroup.PartyMitigation, 90f, 30f, isTargetDebuff: false, new uint[1] { 3540u }),
		new TrackedStatusDefinition(1175u, "武装戍卫", CooldownGroup.PartyMitigation, 120f, 18f, isTargetDebuff: false, new uint[1] { 7385u }),
		new TrackedStatusDefinition(1457u, "摆脱", CooldownGroup.PartyMitigation, 90f, 30f, isTargetDebuff: false, new uint[1] { 7388u }),
		new TrackedStatusDefinition(1826u, "防守之桑巴", CooldownGroup.PartyMitigation, 120f, 15f, isTargetDebuff: false, new uint[1] { 16012u }),
		new TrackedStatusDefinition(1839u, "光之心", CooldownGroup.PartyMitigation, 90f, 15f, isTargetDebuff: false, new uint[1] { 16160u }),
		new TrackedStatusDefinition(1894u, "暗黑布道", CooldownGroup.PartyMitigation, 90f, 15f, isTargetDebuff: false, new uint[1] { 16471u }),
		new TrackedStatusDefinition(1934u, "行吟", CooldownGroup.PartyMitigation, 120f, 15f, isTargetDebuff: false, new uint[1] { 7405u }),
		new TrackedStatusDefinition(1951u, "策动", CooldownGroup.PartyMitigation, 120f, 15f, isTargetDebuff: false, new uint[1] { 16889u }),
		new TrackedStatusDefinition(860u, "武装解除", CooldownGroup.PartyMitigation, 120f, 10f, isTargetDebuff: true, new uint[1] { 2887u }),
		new TrackedStatusDefinition(1872u, "节制", CooldownGroup.PartyMitigation, 120f, 20f, isTargetDebuff: false, new uint[1] { 16536u }),
		new TrackedStatusDefinition(299u, "野战治疗阵", CooldownGroup.PartyMitigation, 30f, 15f, isTargetDebuff: false, new uint[1] { 188u }),
		new TrackedStatusDefinition(2711u, "疾风怒涛", CooldownGroup.PartyMitigation, 120f, 20f, isTargetDebuff: false, new uint[1] { 25868u }),
		new TrackedStatusDefinition(2618u, "坚角清汁", CooldownGroup.PartyMitigation, 30f, 15f, isTargetDebuff: false, new uint[1] { 24298u }),
		new TrackedStatusDefinition(2613u, "泛输血", CooldownGroup.PartyMitigation, 120f, 15f, isTargetDebuff: false, new uint[1] { 24311u }),
		new TrackedStatusDefinition(3003u, "整体论", CooldownGroup.PartyMitigation, 120f, 20f, isTargetDebuff: false, new uint[1] { 24310u }),
		new TrackedStatusDefinition(849u, "命运之轮", CooldownGroup.PartyMitigation, 60f, 18f, isTargetDebuff: false, new uint[1] { 3613u }),
		new TrackedStatusDefinition(1892u, "中间学派", CooldownGroup.PartyMitigation, 120f, 20f, isTargetDebuff: false, new uint[1] { 16559u }),
		new TrackedStatusDefinition(2707u, "魔法屏障", CooldownGroup.PartyMitigation, 120f, 10f, isTargetDebuff: false, new uint[1] { 25857u }),
		new TrackedStatusDefinition(102u, "真言", CooldownGroup.PartyMitigation, 120f, 15f, isTargetDebuff: false, new uint[1] { 65u })
	});

	public static readonly IReadOnlyList<TrackedStatusDefinition> All = RaidBuffs.Concat(Mitigations).ToList();

	public static readonly IReadOnlyDictionary<uint, TrackedStatusDefinition> ByStatusId = (from definition in All
		where definition.StatusId != 0
		group definition by definition.StatusId).ToDictionary((IGrouping<uint, TrackedStatusDefinition> group) => group.Key, (IGrouping<uint, TrackedStatusDefinition> group) => group.First());

	public static readonly IReadOnlyDictionary<uint, TrackedStatusDefinition> ByActionId = (from pair in All.SelectMany((TrackedStatusDefinition definition) => definition.ActionIds.Select((uint actionId) => new { actionId, definition }))
		group pair by pair.actionId).ToDictionary(group => group.Key, group => group.First().definition);
}
