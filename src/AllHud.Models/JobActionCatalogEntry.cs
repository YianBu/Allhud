using AllHud.Data;

namespace AllHud.Models;

public sealed record JobActionCatalogEntry(string Key, uint ClassJobId, uint ActionId, uint IconId, string Name, uint Level, float CooldownSeconds, uint CooldownGroupId, CooldownGroup Group, bool HasKnownStatus, bool IsSharedAction);
