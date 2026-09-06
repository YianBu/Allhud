using System;

namespace AllHud.Models;

public sealed record RecentActionEntry(uint ActionId, uint IconId, string Name, string SourceName, string SourceJobName, ulong SourceObjectId, DateTime ObservedAt);
