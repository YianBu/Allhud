using System;
using System.Collections.Generic;
using System.Linq;

namespace AllHud.Data;

public static class TrackedActionCatalog
{
	public const uint Paladin = 19u;

	public const uint Monk = 20u;

	public const uint Warrior = 21u;

	public const uint Dragoon = 22u;

	public const uint Bard = 23u;

	public const uint WhiteMage = 24u;

	public const uint BlackMage = 25u;

	public const uint Summoner = 27u;

	public const uint Scholar = 28u;

	public const uint Ninja = 30u;

	public const uint Machinist = 31u;

	public const uint DarkKnight = 32u;

	public const uint Astrologian = 33u;

	public const uint Samurai = 34u;

	public const uint RedMage = 35u;

	public const uint Gunbreaker = 37u;

	public const uint Dancer = 38u;

	public const uint Reaper = 39u;

	public const uint Sage = 40u;

	public const uint Viper = 41u;

	public const uint Pictomancer = 42u;

	public static readonly IReadOnlyList<JobCatalogEntry> Jobs = new global::_003C_003Ez__ReadOnlyArray<JobCatalogEntry>(new JobCatalogEntry[21]
	{
		new JobCatalogEntry(19u, "骑士"),
		new JobCatalogEntry(21u, "战士"),
		new JobCatalogEntry(32u, "暗骑"),
		new JobCatalogEntry(37u, "枪刃"),
		new JobCatalogEntry(20u, "武僧"),
		new JobCatalogEntry(22u, "龙骑"),
		new JobCatalogEntry(30u, "忍者"),
		new JobCatalogEntry(34u, "武士"),
		new JobCatalogEntry(39u, "钐镰"),
		new JobCatalogEntry(41u, "蝰蛇"),
		new JobCatalogEntry(23u, "诗人"),
		new JobCatalogEntry(31u, "机工"),
		new JobCatalogEntry(38u, "舞者"),
		new JobCatalogEntry(25u, "黑魔"),
		new JobCatalogEntry(27u, "召唤"),
		new JobCatalogEntry(35u, "赤魔"),
		new JobCatalogEntry(42u, "绘灵"),
		new JobCatalogEntry(24u, "白魔"),
		new JobCatalogEntry(28u, "学者"),
		new JobCatalogEntry(33u, "占星"),
		new JobCatalogEntry(40u, "贤者")
	});

	private static readonly uint[] TankJobs = new uint[4] { 19u, 21u, 32u, 37u };

	private static readonly uint[] MeleeJobs = new uint[6] { 20u, 22u, 30u, 34u, 39u, 41u };

	private static readonly uint[] PhysicalRangedJobs = new uint[3] { 23u, 31u, 38u };

	private static readonly uint[] CasterJobs = new uint[4] { 25u, 27u, 35u, 42u };

	private static readonly uint[] PhysicalDpsJobs = new uint[9] { 20u, 22u, 30u, 34u, 39u, 41u, 23u, 31u, 38u };

	private static readonly uint[] HealerJobs = new uint[4] { 24u, 28u, 33u, 40u };

	private static readonly uint[] MagicJobs = new uint[8] { 25u, 27u, 35u, 42u, 24u, 28u, 33u, 40u };

	private static readonly uint[] ArmLengthJobs = new uint[13]
	{
		19u, 21u, 32u, 37u, 20u, 22u, 30u, 34u, 39u, 41u,
		23u, 31u, 38u
	};

	private static readonly uint[] InterruptJobs = new uint[7] { 19u, 21u, 32u, 37u, 23u, 31u, 38u };

	public static readonly IReadOnlySet<uint> IndependentMonitorCommonActionIds = new HashSet<uint> { 3u, 7533u, 7537u, 7548u, 7559u, 7541u, 7542u };

	public static readonly IReadOnlySet<uint> PartyInfoExtraMitigationActionIds = new HashSet<uint>
	{
		157u, 2241u, 7394u, 7432u, 7408u, 7498u, 16015u, 24404u, 25799u, 25861u,
		34685u, 34686u, 36962u
	};

	private static readonly IReadOnlySet<string> CuratedRaidBuffActionNames = new HashSet<string>(StringComparer.Ordinal)
	{
		"战斗之声", "光明神的最终乐章", "战斗连祷", "义结金兰", "介毒之术", "夺取", "神秘环", "技巧舞步", "技术舞步结束", "灼热之光",
		"鼓励", "星空构想", "连环计", "占卜", "Battle Voice", "Radiant Finale", "Battle Litany", "Brotherhood", "Dokumori", "Mug",
		"Arcane Circle", "Technical Finish", "Searing Light", "Embolden", "Starry Muse", "Chain Stratagem", "Divination"
	};

	private static readonly IReadOnlySet<string> CuratedBurstActionNames = new HashSet<string>(StringComparer.Ordinal)
	{
		"进攻之探戈", "安魂祈祷", "绝对统治", "战逃反应", "原初的解放", "血乱", "掠影示现", "血壤", "倾泻弹雨", "连祷终结",
		"整备", "野火", "超荷", "枪管加热", "Devilment", "Requiescat", "Imperator", "Fight or Flight", "Inner Release", "Delirium",
		"Living Shadow", "Bloodfest", "Reassemble", "Wildfire", "Hypercharge", "Barrel Stabilizer"
	};

	private static readonly IReadOnlySet<string> CuratedTargetMitigationActionNames = new HashSet<string>(StringComparer.Ordinal) { "雪仇", "牵制", "昏乱", "Reprisal", "Feint", "Addle" };

	private static readonly IReadOnlySet<string> CuratedPersonalMitigationActionNames = new HashSet<string>(StringComparer.Ordinal)
	{
		"铁壁", "预警", "极致防御", "干预", "原初的勇猛", "原初的血气", "戮罪", "暗影墙", "暗影守夜", "至黑之夜",
		"星云", "大星云", "刚玉之心", "水流幕", "神祝祷", "深谋远虑之策", "生命回生法", "擢升", "天星交错", "白牛清汁",
		"输血", "混合", "四液混合", "坦培拉涂层", "坦培拉油彩", "灿烂之盾", "神秘纹", "残影", "金刚极意", "第三眼",
		"天诛势", "大地神的抒情恋歌", "治疗之华尔兹", "魔罩", "Rampart", "Sentinel", "Guardian", "Intervention", "Nascent Flash", "Bloodwhetting",
		"Damnation", "Shadow Wall", "Shadowed Vigil", "The Blackest Night", "Nebula", "Great Nebula", "Heart of Corundum", "Aquaveil", "Divine Benison", "Excogitation",
		"Protraction", "Exaltation", "Celestial Intersection", "Taurochole", "Haima", "Krasis", "Tempera Coat", "Tempera Grassa", "Radiant Aegis", "Arcane Crest",
		"Shade Shift", "Riddle of Earth", "Third Eye", "Tengentsu", "Nature's Minne", "Curing Waltz", "Manaward"
	};

	private static readonly IReadOnlySet<string> CuratedPartyMitigationActionNames = new HashSet<string>(StringComparer.Ordinal)
	{
		"真言", "圣光幕帘", "武装戍卫", "摆脱", "暗黑布道", "光之心", "行吟", "策动", "防守之桑巴", "魔法屏障",
		"节制", "野战治疗阵", "异想的幻光", "展开战术", "慰藉", "生命回生法", "疾风怒涛之计", "疾风怒涛", "命运之轮", "中间学派",
		"擢升", "太阳星座", "坚角清汁", "整体论", "泛输血", "魂灵风息", "武装解除", "Mantra", "Divine Veil", "Passage of Arms",
		"Shake It Off", "Dark Missionary", "Heart of Light", "Troubadour", "Tactician", "Shield Samba", "Magick Barrier", "Temperance", "Sacred Soil", "Fey Illumination",
		"Deployment Tactics", "Consolation", "Protraction", "Expedient", "Collective Unconscious", "Neutral Sect", "Sun Sign", "Kerachole", "Holos", "Panhaima",
		"Pneuma", "Dismantle"
	};

	private static readonly IReadOnlySet<string> CuratedCommonActionNames = new HashSet<string>(StringComparer.Ordinal)
	{
		"疾跑", "挑衅", "退避", "沉稳咏唱", "醒梦", "即刻咏唱", "插言", "下踢", "亲疏自行", "内丹",
		"浴血", "Sprint", "Provoke", "Shirk", "Surecast", "Lucid Dreaming", "Swiftcast", "Interject", "Low Blow", "Arm's Length",
		"Second Wind", "Bloodbath"
	};

	public static readonly IReadOnlyList<TrackedActionDefinition> CommonSkills = new global::_003C_003Ez__ReadOnlyArray<TrackedActionDefinition>(new TrackedActionDefinition[15]
	{
		CommonSkill("common-sprint", 50u, "疾跑", CooldownGroup.Common, 60f, 10f, false, 3u),
		CommonSkillForJobs("common-rampart", 1191u, "铁壁", CooldownGroup.PersonalMitigation, 90f, 20f, false, TankJobs, 7531u),
		CommonSkillForJobs("common-provoke", 0u, "挑衅", CooldownGroup.Common, 30f, 0f, false, TankJobs, 7533u),
		CommonSkillForJobs("common-reprisal", 1193u, "雪仇", CooldownGroup.PartyMitigation, 60f, 15f, true, TankJobs, 7535u),
		CommonSkillForJobs("common-shirk", 0u, "退避", CooldownGroup.Common, 120f, 0f, false, TankJobs, 7537u),
		CommonSkillForJobs("common-feint", 1195u, "牵制", CooldownGroup.PartyMitigation, 90f, 10f, true, MeleeJobs, 7549u),
		CommonSkillForJobs("common-addle", 1203u, "昏乱", CooldownGroup.PartyMitigation, 90f, 10f, true, CasterJobs, 7560u),
		CommonSkillForJobs("common-surecast", 0u, "沉稳咏唱", CooldownGroup.Common, 120f, 6f, false, MagicJobs, 7559u),
		CommonSkillForJobs("common-lucid-dreaming", 1204u, "醒梦", CooldownGroup.Common, 60f, 21f, false, MagicJobs, 7562u),
		CommonSkillForJobs("common-swiftcast", 167u, "即刻咏唱", CooldownGroup.Common, 40f, 10f, false, MagicJobs, 7561u),
		CommonSkillForJobs("common-interject", 0u, "插言", CooldownGroup.Common, 30f, 0f, false, InterruptJobs, 7538u),
		CommonSkillForJobs("common-low-blow", 0u, "下踢", CooldownGroup.Common, 25f, 0f, false, TankJobs, 7540u),
		CommonSkillForJobs("common-arms-length", 0u, "亲疏自行", CooldownGroup.Common, 120f, 6f, false, ArmLengthJobs, 7548u),
		CommonSkillForJobs("common-second-wind", 0u, "内丹", CooldownGroup.Common, 120f, 0f, false, PhysicalDpsJobs, 7541u),
		CommonSkillForJobs("common-bloodbath", 84u, "浴血", CooldownGroup.Common, 90f, 20f, false, MeleeJobs, 7542u)
	});

	public static readonly IReadOnlyList<TrackedActionDefinition> Skills = new global::_003C_003Ez__ReadOnlyArray<TrackedActionDefinition>(new TrackedActionDefinition[79]
	{
		Skill(19u, "骑士", "pld-divine-veil", 726u, "圣光幕帘", CooldownGroup.PartyMitigation, 90f, 30f, false, 3540u),
		Skill(19u, "骑士", "pld-passage-of-arms", 1175u, "武装戍卫", CooldownGroup.PartyMitigation, 120f, 18f, false, 7385u),
		Skill(19u, "骑士", "pld-imperator", 0u, "绝对统治", CooldownGroup.Burst, 60f, 0f, false, 36921u, 7383u),
		Skill(19u, "骑士", "pld-hallowed-ground", 82u, "神圣领域", CooldownGroup.PersonalMitigation, 420f, 10f, false, 30u),
		Skill(19u, "骑士", "pld-intervention", 1174u, "干预", CooldownGroup.PersonalMitigation, 10f, 8f, false, 7382u),
		Skill(19u, "骑士", "pld-holy-sheltron", 2674u, "圣盾阵", CooldownGroup.PersonalMitigation, 5f, 8f, false, 25746u),
		Skill(19u, "骑士", "pld-guardian", 3829u, "极致防御", CooldownGroup.PersonalMitigation, 120f, 15f, false, 36920u, 17u),
		Skill(21u, "战士", "war-shake-it-off", 1457u, "摆脱", CooldownGroup.PartyMitigation, 90f, 30f, false, 7388u),
		Skill(21u, "战士", "war-inner-release", 0u, "原初的解放", CooldownGroup.Burst, 60f, 0f, false, 7389u, 38u),
		Skill(21u, "战士", "war-thrill-of-battle", 87u, "战栗", CooldownGroup.Personal, 90f, 10f, false, 40u),
		Skill(21u, "战士", "war-holmgang", 409u, "死斗", CooldownGroup.PersonalMitigation, 240f, 10f, false, 43u),
		Skill(21u, "战士", "war-damnation", 3832u, "戮罪", CooldownGroup.PersonalMitigation, 120f, 15f, false, 36923u, 44u),
		Skill(21u, "战士", "war-bloodwhetting", 2678u, "原初的血气", CooldownGroup.PersonalMitigation, 25f, 8f, false, 25751u, 16464u),
		Skill(32u, "暗骑", "drk-dark-missionary", 1894u, "暗黑布道", CooldownGroup.PartyMitigation, 90f, 15f, false, 16471u),
		Skill(32u, "暗骑", "drk-delirium", 0u, "血乱", CooldownGroup.Burst, 60f, 0f, false, 7390u),
		Skill(32u, "暗骑", "drk-living-shadow", 0u, "掠影示现", CooldownGroup.Burst, 120f, 0f, false, 16472u),
		Skill(32u, "暗骑", "drk-dark-mind", 746u, "弃明投暗", CooldownGroup.PersonalMitigation, 60f, 10f, false, 3634u),
		Skill(32u, "暗骑", "drk-shadowed-vigil", 3835u, "暗影守夜", CooldownGroup.PersonalMitigation, 120f, 15f, false, 36927u, 3636u),
		Skill(32u, "暗骑", "drk-living-dead", 810u, "行尸走肉", CooldownGroup.PersonalMitigation, 300f, 10f, false, 3638u),
		Skill(32u, "暗骑", "drk-the-blackest-night", 1178u, "至黑之夜", CooldownGroup.PersonalMitigation, 15f, 7f, false, 7393u),
		Skill(32u, "暗骑", "drk-oblation", 2682u, "献奉", CooldownGroup.PersonalMitigation, 60f, 10f, false, 25754u),
		Skill(37u, "枪刃", "gnb-heart-of-light", 1839u, "光之心", CooldownGroup.PartyMitigation, 90f, 15f, false, 16160u),
		Skill(37u, "枪刃", "gnb-bloodfest", 0u, "血壤", CooldownGroup.Burst, 120f, 0f, false, 16164u),
		Skill(37u, "枪刃", "gnb-camouflage", 1832u, "伪装", CooldownGroup.PersonalMitigation, 90f, 20f, false, 16140u),
		Skill(37u, "枪刃", "gnb-great-nebula", 3838u, "大星云", CooldownGroup.PersonalMitigation, 120f, 15f, false, 36935u, 16148u),
		Skill(37u, "枪刃", "gnb-aurora", 1835u, "极光", CooldownGroup.PersonalMitigation, 60f, 18f, false, 16151u),
		Skill(37u, "枪刃", "gnb-superbolide", 1836u, "超火流星", CooldownGroup.PersonalMitigation, 360f, 10f, false, 16152u),
		Skill(37u, "枪刃", "gnb-heart-of-corundum", 2683u, "刚玉之心", CooldownGroup.PersonalMitigation, 25f, 8f, false, 25758u),
		Skill(20u, "武僧", "mnk-brotherhood", 1185u, "义结金兰", CooldownGroup.RaidBuff, 120f, 20f, false, 7396u),
		Skill(20u, "武僧", "mnk-mantra", 102u, "真言", CooldownGroup.PartyMitigation, 120f, 15f, false, 65u),
		Skill(20u, "武僧", "mnk-riddle-of-earth", 1179u, "金刚极意", CooldownGroup.PersonalMitigation, 120f, 10f, false, 7394u),
		Skill(22u, "龙骑", "drg-battle-litany", 786u, "战斗连祷", CooldownGroup.RaidBuff, 120f, 20f, false, 3557u),
		Skill(30u, "忍者", "nin-dokumori", 3849u, "介毒之术", CooldownGroup.RaidBuff, 120f, 20f, true, 36957u),
		Skill(30u, "忍者", "nin-shade-shift", 488u, "残影", CooldownGroup.PersonalMitigation, 120f, 20f, false, 2241u),
		Skill(34u, "武士", "sam-third-eye", 1232u, "第三眼", CooldownGroup.PersonalMitigation, 15f, 4f, false, 7498u),
		Skill(34u, "武士", "sam-tengentsu", 3853u, "天诛势", CooldownGroup.PersonalMitigation, 15f, 4f, false, 36962u),
		Skill(39u, "钐镰", "rpr-arcane-circle", 2599u, "神秘环", CooldownGroup.RaidBuff, 120f, 20f, false, 24405u),
		Skill(39u, "钐镰", "rpr-arcane-crest", 2598u, "神秘纹", CooldownGroup.PersonalMitigation, 30f, 5f, false, 24404u),
		Skill(23u, "诗人", "brd-battle-voice", 141u, "战斗之声", CooldownGroup.RaidBuff, 120f, 20f, false, 118u),
		Skill(23u, "诗人", "brd-radiant-finale", 2964u, "光明神的最终乐章", CooldownGroup.RaidBuff, 110f, 15f, false, 25785u),
		Skill(23u, "诗人", "brd-troubadour", 1934u, "行吟", CooldownGroup.PartyMitigation, 120f, 15f, false, 7405u),
		Skill(23u, "诗人", "brd-natures-minne", 1202u, "大地神的抒情恋歌", CooldownGroup.PersonalMitigation, 120f, 15f, false, 7408u),
		Skill(31u, "机工", "mch-tactician", 1951u, "策动", CooldownGroup.PartyMitigation, 120f, 15f, false, 16889u),
		Skill(31u, "机工", "mch-dismantle", 860u, "武装解除", CooldownGroup.PartyMitigation, 120f, 10f, true, 2887u),
		Skill(31u, "机工", "mch-reassemble", 0u, "整备", CooldownGroup.Burst, 55f, 0f, false, 2876u),
		Skill(31u, "机工", "mch-wildfire", 0u, "野火", CooldownGroup.Burst, 120f, 0f, false, 2878u),
		Skill(31u, "机工", "mch-barrel-stabilizer", 0u, "枪管加热", CooldownGroup.Burst, 120f, 0f, false, 7414u),
		Skill(38u, "舞者", "dnc-technical-finish", 1822u, "技巧舞步", CooldownGroup.RaidBuff, 120f, 20f, false, 15998u),
		Skill(38u, "舞者", "dnc-devilment", 1825u, "进攻之探戈", CooldownGroup.Burst, 120f, 20f, false, 16011u),
		Skill(38u, "舞者", "dnc-shield-samba", 1826u, "防守之桑巴", CooldownGroup.PartyMitigation, 120f, 15f, false, 16012u),
		Skill(38u, "舞者", "dnc-curing-waltz", 0u, "治疗之华尔兹", CooldownGroup.PersonalMitigation, 60f, 0f, false, 16015u),
		Skill(25u, "黑魔", "blm-manaward", 168u, "魔罩", CooldownGroup.PersonalMitigation, 120f, 20f, false, 157u),
		Skill(27u, "召唤", "smn-searing-light", 2703u, "灼热之光", CooldownGroup.RaidBuff, 120f, 30f, false, 25801u),
		Skill(27u, "召唤", "smn-radiant-aegis", 2702u, "灿烂之盾", CooldownGroup.PersonalMitigation, 60f, 30f, false, 25799u),
		Skill(35u, "赤魔", "rdm-embolden", 1239u, "鼓励", CooldownGroup.RaidBuff, 120f, 20f, false, 7520u),
		Skill(35u, "赤魔", "rdm-magick-barrier", 2707u, "魔法屏障", CooldownGroup.PartyMitigation, 120f, 10f, false, 25857u),
		Skill(42u, "绘灵", "pct-starry-muse", 3685u, "星空构想", CooldownGroup.RaidBuff, 120f, 20f, false, 34675u),
		Skill(42u, "绘灵", "pct-tempera-coat", 3686u, "坦培拉涂层", CooldownGroup.PersonalMitigation, 120f, 10f, false, 34685u),
		Skill(42u, "绘灵", "pct-tempera-grassa", 3687u, "坦培拉油彩", CooldownGroup.PersonalMitigation, 1f, 10f, false, 34686u),
		Skill(24u, "白魔", "whm-temperance", 1872u, "节制", CooldownGroup.PartyMitigation, 120f, 20f, false, 16536u),
		Skill(24u, "白魔", "whm-aquaveil", 2708u, "水流幕", CooldownGroup.PersonalMitigation, 60f, 8f, false, 25861u),
		Skill(24u, "白魔", "whm-divine-benison", 1218u, "神祝祷", CooldownGroup.PersonalMitigation, 30f, 15f, false, 7432u),
		Skill(28u, "学者", "sch-chain-stratagem", 1221u, "连环计", CooldownGroup.RaidBuff, 120f, 15f, true, 7436u),
		Skill(28u, "学者", "sch-sacred-soil", 299u, "野战治疗阵", CooldownGroup.PartyMitigation, 30f, 15f, false, 188u),
		Skill(28u, "学者", "sch-expedient", 2711u, "疾风怒涛", CooldownGroup.PartyMitigation, 120f, 20f, false, 25868u),
		Skill(28u, "学者", "sch-deployment-tactics", 0u, "展开战术", CooldownGroup.PartyMitigation, 120f, 30f, false, 3585u),
		Skill(28u, "学者", "sch-excogitation", 1220u, "深谋远虑之策", CooldownGroup.PersonalMitigation, 45f, 45f, false, 7434u),
		Skill(28u, "学者", "sch-protraction", 2710u, "生命回生法", CooldownGroup.PersonalMitigation, 60f, 10f, false, 25867u),
		Skill(33u, "占星", "ast-divination", 1878u, "占卜", CooldownGroup.RaidBuff, 120f, 20f, false, 16552u),
		Skill(33u, "占星", "ast-collective-unconscious", 849u, "命运之轮", CooldownGroup.PartyMitigation, 60f, 18f, false, 3613u),
		Skill(33u, "占星", "ast-neutral-sect", 1892u, "中间学派", CooldownGroup.PartyMitigation, 120f, 20f, false, 16559u),
		Skill(33u, "占星", "ast-celestial-intersection", 1889u, "天星交错", CooldownGroup.PersonalMitigation, 30f, 30f, false, 16556u),
		Skill(33u, "占星", "ast-exaltation", 2717u, "擢升", CooldownGroup.PersonalMitigation, 60f, 8f, false, 25873u),
		Skill(40u, "贤者", "sge-kerachole", 2618u, "坚角清汁", CooldownGroup.PartyMitigation, 30f, 15f, false, 24298u),
		Skill(40u, "贤者", "sge-panhaima", 2613u, "泛输血", CooldownGroup.PartyMitigation, 120f, 15f, false, 24311u),
		Skill(40u, "贤者", "sge-holos", 3003u, "整体论", CooldownGroup.PartyMitigation, 120f, 20f, false, 24310u),
		Skill(40u, "贤者", "sge-taurochole", 2619u, "白牛清汁", CooldownGroup.PersonalMitigation, 45f, 15f, false, 24303u),
		Skill(40u, "贤者", "sge-haima", 2612u, "输血", CooldownGroup.PersonalMitigation, 120f, 15f, false, 24305u),
		Skill(40u, "贤者", "sge-krasis", 2622u, "混合", CooldownGroup.PersonalMitigation, 60f, 10f, false, 24317u)
	});

	private static readonly IReadOnlyDictionary<uint, IReadOnlyList<TrackedActionDefinition>> SharedSkillsByJob = ((IEnumerable<JobCatalogEntry>)Jobs).ToDictionary((Func<JobCatalogEntry, uint>)((JobCatalogEntry job) => job.ClassJobId), (Func<JobCatalogEntry, IReadOnlyList<TrackedActionDefinition>>)((JobCatalogEntry job) => (from skill in CommonSkills
		where IsCommonSkillAllowedForJob(skill, job.ClassJobId)
		select CloneCommonSkillForJob(skill, job.ClassJobId, job.Name)).ToList()));

	public static readonly IReadOnlyList<TrackedStatusDefinition> PartyMitigationDefinitions = (from skill in CommonSkills.Concat(Skills)
		where skill.Definition.Group == CooldownGroup.PartyMitigation || skill.Definition.ActionIds.Any(PartyInfoExtraMitigationActionIds.Contains)
		select skill.Definition).ToList();

	public static readonly IReadOnlySet<uint> PartyMitigationActionIds = (from actionId in PartyMitigationDefinitions.SelectMany((TrackedStatusDefinition definition) => definition.ActionIds)
		where actionId != 0
		select actionId).ToHashSet();

	public static readonly IReadOnlyList<TrackedStatusDefinition> PartyTrackedDefinitions = (from skill in CommonSkills.Concat(Skills).Where(delegate(TrackedActionDefinition skill)
		{
			CooldownGroup cooldownGroup = skill.Definition.Group;
			return ((uint)(cooldownGroup - 1) <= 1u || (uint)(cooldownGroup - 5) <= 1u) ? true : false;
		})
		where !skill.Definition.ActionIds.Any(PartyMitigationActionIds.Contains)
		select skill.Definition).ToList();

	public static readonly IReadOnlySet<uint> PartyTrackedActionIds = (from actionId in PartyTrackedDefinitions.SelectMany((TrackedStatusDefinition definition) => definition.ActionIds)
		where actionId != 0
		select actionId).ToHashSet();

	public static void EnsureSelectionInitialized(Configuration config)
	{
		if (config.EnabledJobSkillKeys == null)
		{
			List<string> list = (config.EnabledJobSkillKeys = new List<string>());
		}
		if (!config.JobSkillSelectionInitialized)
		{
			config.EnabledJobSkillKeys = (from skill in Skills
				where skill.EnabledByDefault
				select skill.Key).ToList();
			config.JobSkillSelectionInitialized = true;
		}
	}

	public static void EnsureActionSelectionInitialized(Configuration config)
	{
		if (config.EnabledJobActionKeys == null)
		{
			List<string> list = (config.EnabledJobActionKeys = new List<string>());
		}
		MigrateCommonActionKeys(config);
		RemovePartyInfoActionKeys(config.EnabledJobActionKeys);
		if (!config.JobActionSelectionInitialized)
		{
			HashSet<string> enabledSkillKeys = (config.EnabledJobSkillKeys ?? new List<string>()).ToHashSet<string>(StringComparer.Ordinal);
			IEnumerable<TrackedActionDefinition> source = ((config.JobSkillSelectionInitialized && enabledSkillKeys.Count > 0) ? Skills.Where((TrackedActionDefinition skill) => enabledSkillKeys.Contains(skill.Key)) : Skills.Where((TrackedActionDefinition skill) => skill.EnabledByDefault));
			config.EnabledJobActionKeys = (from skill in source
				where skill.Definition.ActionIds.FirstOrDefault() != 0
				where !skill.Definition.ActionIds.Any(PartyMitigationActionIds.Contains)
				select GetActionKey(skill.ClassJobId, skill.Definition.ActionIds.First())).Distinct<string>(StringComparer.Ordinal).ToList();
			config.JobActionSelectionInitialized = true;
		}
	}

	public static string GetActionKey(uint classJobId, uint actionId)
	{
		return $"{classJobId}:{actionId}";
	}

	public static string GetCommonActionKey(uint actionId)
	{
		return GetActionKey(0u, actionId);
	}

	public static IReadOnlyList<uint> GetClassJobFamilyIds(uint classJobId)
	{
		uint baseClassJobId = GetBaseClassJobId(classJobId);
		if (baseClassJobId != 0 && baseClassJobId != classJobId)
		{
			return new global::_003C_003Ez__ReadOnlyArray<uint>(new uint[2] { classJobId, baseClassJobId });
		}
		return new global::_003C_003Ez__ReadOnlySingleElementList<uint>(classJobId);
	}

	public static uint GetBaseClassJobId(uint classJobId)
	{
		switch (classJobId)
		{
		case 19u:
			return 1u;
		case 20u:
			return 2u;
		case 21u:
			return 3u;
		case 22u:
			return 4u;
		case 23u:
			return 5u;
		case 24u:
			return 6u;
		case 25u:
			return 7u;
		case 27u:
		case 28u:
			return 26u;
		case 30u:
			return 29u;
		default:
			return 0u;
		}
	}

	public static TrackedActionDefinition? FindKnownSkill(uint classJobId, uint actionId)
	{
		return GetSkillsForJob(classJobId).FirstOrDefault((TrackedActionDefinition skill) => skill.ClassJobId == classJobId && skill.Definition.ActionIds.Contains(actionId));
	}

	public static TrackedActionDefinition? FindCommonSkill(uint actionId)
	{
		return CommonSkills.FirstOrDefault((TrackedActionDefinition skill) => skill.Definition.ActionIds.Contains(actionId));
	}

	public static CooldownGroup? FindCuratedActionGroup(string actionName)
	{
		if (string.IsNullOrWhiteSpace(actionName))
		{
			return null;
		}
		string item = actionName.Trim();
		if (CuratedRaidBuffActionNames.Contains(item))
		{
			return CooldownGroup.RaidBuff;
		}
		if (CuratedBurstActionNames.Contains(item))
		{
			return CooldownGroup.Burst;
		}
		if (CuratedTargetMitigationActionNames.Contains(item))
		{
			return CooldownGroup.PartyMitigation;
		}
		if (CuratedPersonalMitigationActionNames.Contains(item))
		{
			return CooldownGroup.PersonalMitigation;
		}
		if (CuratedPartyMitigationActionNames.Contains(item))
		{
			return CooldownGroup.PartyMitigation;
		}
		if (CuratedCommonActionNames.Contains(item))
		{
			return CooldownGroup.Common;
		}
		return null;
	}

	public static IReadOnlySet<string> GetEnabledSkillKeys(Configuration config)
	{
		EnsureSelectionInitialized(config);
		return config.EnabledJobSkillKeys.ToHashSet<string>(StringComparer.Ordinal);
	}

	public static IReadOnlyList<TrackedStatusDefinition> GetEnabledDefinitions(Configuration config)
	{
		IReadOnlySet<string> enabledKeys = GetEnabledSkillKeys(config);
		EnsureActionSelectionInitialized(config);
		HashSet<string> enabledActionKeys = (config.EnabledJobActionKeys ?? new List<string>()).ToHashSet<string>(StringComparer.Ordinal);
		return (from skill in GetAllActionSkills()
			where enabledKeys.Contains(skill.Key) || enabledActionKeys.Contains(GetActionKey(skill.ClassJobId, skill.Definition.ActionIds.FirstOrDefault()))
			select skill.Definition).ToList();
	}

	public static IReadOnlyList<TrackedActionDefinition> GetSkillsForJob(uint classJobId)
	{
		return (from skill in Skills.Where((TrackedActionDefinition skill) => skill.ClassJobId == classJobId).Concat(GetSharedSkillsForJob(classJobId))
			orderby skill.Definition.Group, skill.Definition.Name
			select skill).ToList();
	}

	public static IReadOnlyList<TrackedActionDefinition> GetSharedSkillsForJob(uint classJobId)
	{
		if (!SharedSkillsByJob.TryGetValue(classJobId, out IReadOnlyList<TrackedActionDefinition> value))
		{
			return Array.Empty<TrackedActionDefinition>();
		}
		return value;
	}

	private static IEnumerable<TrackedActionDefinition> GetAllActionSkills()
	{
		return Skills.Concat(GetAllSharedSkills());
	}

	private static IEnumerable<TrackedActionDefinition> GetAllSharedSkills()
	{
		return SharedSkillsByJob.Values.SelectMany((IReadOnlyList<TrackedActionDefinition> skills) => skills);
	}

	private static bool IsCommonSkillAllowedForJob(TrackedActionDefinition skill, uint classJobId)
	{
		if (skill.Definition.SourceClassJobIds.Count != 0)
		{
			return skill.Definition.SourceClassJobIds.Contains(classJobId);
		}
		return true;
	}

	private static TrackedActionDefinition CloneCommonSkillForJob(TrackedActionDefinition skill, uint classJobId, string jobName)
	{
		TrackedStatusDefinition definition = skill.Definition;
		return new TrackedActionDefinition($"{skill.Key}-{classJobId}", classJobId, jobName, new TrackedStatusDefinition(definition.StatusId, definition.Name, definition.Group, definition.CooldownSeconds, definition.DurationSeconds, definition.IsTargetDebuff, definition.ActionIds, new uint[1] { classJobId }), skill.EnabledByDefault, isSharedSkill: true);
	}

	private static void MigrateCommonActionKeys(Configuration config)
	{
		if (config.EnabledJobActionKeys.Count == 0)
		{
			return;
		}
		bool flag = false;
		HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
		foreach (string enabledJobActionKey in config.EnabledJobActionKeys)
		{
			if (!TryParseActionKey(enabledJobActionKey, out var classJobId, out var actionId) || classJobId != 0)
			{
				hashSet.Add(enabledJobActionKey);
				continue;
			}
			TrackedActionDefinition commonSkill = FindCommonSkill(actionId);
			if (commonSkill == null)
			{
				hashSet.Add(enabledJobActionKey);
				continue;
			}
			foreach (JobCatalogEntry item in Jobs.Where((JobCatalogEntry job) => IsCommonSkillAllowedForJob(commonSkill, job.ClassJobId)))
			{
				hashSet.Add(GetActionKey(item.ClassJobId, actionId));
			}
			flag = true;
		}
		if (flag)
		{
			config.EnabledJobActionKeys = hashSet.ToList();
		}
	}

	private static void RemovePartyInfoActionKeys(List<string> keys)
	{
		keys.RemoveAll((string key) => TryParseActionKey(key, out var _, out var actionId) && PartyMitigationActionIds.Contains(actionId));
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

	private static TrackedActionDefinition TankRole(uint classJobId, string jobName, string key)
	{
		return SharedSkill(classJobId, jobName, key, 1193u, "雪仇", CooldownGroup.PartyMitigation, 60f, 15f, true, 7535u);
	}

	private static TrackedActionDefinition Sprint(uint classJobId, string jobName)
	{
		return SharedSkill(classJobId, jobName, $"common-sprint-{classJobId}", 50u, "疾跑", CooldownGroup.Common, 60f, 10f, false, 3u);
	}

	private static TrackedActionDefinition MeleeRole(uint classJobId, string jobName, string key)
	{
		return SharedSkill(classJobId, jobName, key, 1195u, "牵制", CooldownGroup.PartyMitigation, 90f, 10f, true, 7549u);
	}

	private static TrackedActionDefinition MagicRole(uint classJobId, string jobName, string key)
	{
		return SharedSkill(classJobId, jobName, key, 1203u, "昏乱", CooldownGroup.PartyMitigation, 90f, 10f, true, 7560u);
	}

	private static TrackedActionDefinition SurecastRole(uint classJobId, string jobName, string key)
	{
		return SharedSkill(classJobId, jobName, key, 0u, "沉稳咏唱", CooldownGroup.Common, 120f, 6f, false, 7559u);
	}

	private static TrackedActionDefinition CommonSkill(string key, uint statusId, string name, CooldownGroup group, float cooldownSeconds, float durationSeconds, bool isTargetDebuff, params uint[] actionIds)
	{
		return new TrackedActionDefinition(key, 0u, "通用", new TrackedStatusDefinition(statusId, name, group, cooldownSeconds, durationSeconds, isTargetDebuff, actionIds, Array.Empty<uint>()), enabledByDefault: true, isSharedSkill: true);
	}

	private static TrackedActionDefinition CommonSkillForJobs(string key, uint statusId, string name, CooldownGroup group, float cooldownSeconds, float durationSeconds, bool isTargetDebuff, IReadOnlyList<uint> sourceClassJobIds, params uint[] actionIds)
	{
		return new TrackedActionDefinition(key, 0u, "通用", new TrackedStatusDefinition(statusId, name, group, cooldownSeconds, durationSeconds, isTargetDebuff, actionIds, sourceClassJobIds), enabledByDefault: true, isSharedSkill: true);
	}

	private static TrackedActionDefinition Skill(uint classJobId, string jobName, string key, uint statusId, string name, CooldownGroup group, float cooldownSeconds, float durationSeconds, bool isTargetDebuff, params uint[] actionIds)
	{
		return CreateSkill(classJobId, jobName, key, statusId, name, group, cooldownSeconds, durationSeconds, isTargetDebuff, isSharedSkill: false, actionIds);
	}

	private static TrackedActionDefinition SharedSkill(uint classJobId, string jobName, string key, uint statusId, string name, CooldownGroup group, float cooldownSeconds, float durationSeconds, bool isTargetDebuff, params uint[] actionIds)
	{
		return CreateSkill(classJobId, jobName, key, statusId, name, group, cooldownSeconds, durationSeconds, isTargetDebuff, isSharedSkill: true, actionIds);
	}

	private static TrackedActionDefinition CreateSkill(uint classJobId, string jobName, string key, uint statusId, string name, CooldownGroup group, float cooldownSeconds, float durationSeconds, bool isTargetDebuff, bool isSharedSkill, IReadOnlyList<uint> actionIds)
	{
		return new TrackedActionDefinition(key, classJobId, jobName, new TrackedStatusDefinition(statusId, name, group, cooldownSeconds, durationSeconds, isTargetDebuff, actionIds, new uint[1] { classJobId }), enabledByDefault: true, isSharedSkill);
	}
}
