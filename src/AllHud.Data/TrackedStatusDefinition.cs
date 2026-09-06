using System;
using System.Collections.Generic;

namespace AllHud.Data;

public sealed class TrackedStatusDefinition
{
	public uint StatusId { get; }

	public string Name { get; }

	public CooldownGroup Group { get; }

	public float CooldownSeconds { get; }

	public float DurationSeconds { get; }

	public bool IsTargetDebuff { get; }

	public IReadOnlyList<uint> ActionIds { get; }

	public IReadOnlyList<uint> SourceClassJobIds { get; }

	public TrackedStatusDefinition(uint statusId, string name, CooldownGroup group, float cooldownSeconds, float durationSeconds, bool isTargetDebuff = false, IReadOnlyList<uint>? actionIds = null, IReadOnlyList<uint>? sourceClassJobIds = null)
	{
		StatusId = statusId;
		Name = name;
		Group = group;
		CooldownSeconds = cooldownSeconds;
		DurationSeconds = durationSeconds;
		IsTargetDebuff = isTargetDebuff;
		ActionIds = actionIds ?? Array.Empty<uint>();
		SourceClassJobIds = sourceClassJobIds ?? Array.Empty<uint>();
	}
}
