namespace AllHud.Models;

public sealed record StatusEntry(uint StatusId, uint IconId, string Name, string HolderName, string SourceName, string SourceJobName, ulong SourceObjectId, float RemainingSeconds, float MaxSeconds, bool IsBuff, bool IsSelfApplied, int StatusIndex = int.MaxValue, bool CanDispel = false, bool PartyListPriority = false);
