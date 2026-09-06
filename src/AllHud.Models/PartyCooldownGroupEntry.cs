using System.Collections.Generic;

namespace AllHud.Models;

public sealed record PartyCooldownGroupEntry(string SourceName, string SourceJobName, uint SourceClassJobId, uint SourceJobIconId, ulong SourceObjectId, uint SourceEntityId, bool IsLocalPlayer, int PartySlot, IReadOnlyList<CooldownEntry> Cooldowns);
