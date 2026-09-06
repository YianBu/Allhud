using System.Collections.Generic;

namespace AllHud.Models;

public sealed record TargetInfoEntry(ulong ObjectId, uint DataId, string Name, uint Level, uint CurrentHp, uint MaxHp, bool IsCasting, bool IsCastInterruptible, uint CastActionId, string CastActionName, float CurrentCastTime, float TotalCastTime, TargetOfTargetEntry? TargetOfTarget, IReadOnlyList<StatusEntry> Statuses);
