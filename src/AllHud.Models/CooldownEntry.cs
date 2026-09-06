using System;
using AllHud.Data;

namespace AllHud.Models;

public sealed record CooldownEntry(uint StatusId, uint ActionId, uint IconId, string Name, CooldownGroup Group, float CooldownSeconds, float DurationSeconds, string SourceName, string SourceJobName, ulong SourceObjectId, DateTime ReadyAt, DateTime LastSeenAt, bool IsActive, float ActiveRemainingSeconds, CooldownObservationKind ObservationKind)
{
	public float RemainingCooldownSeconds => Math.Max(0f, (float)(ReadyAt - DateTime.UtcNow).TotalSeconds);

	public bool IsReady => RemainingCooldownSeconds <= 0f;
}
