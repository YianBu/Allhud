namespace AllHud.Models;

public sealed record PartyFoodStatusEntry(string SourceName, string SourceJobName, ulong SourceObjectId, uint SourceEntityId, bool IsLocalPlayer, int PartySlot, bool HasFood, uint IconId, string Name, float RemainingSeconds, float MaxSeconds);
