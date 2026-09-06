namespace AllHud.Models;

public sealed record TaskBarSnapshot(string PlayerName, string JobName, uint ClassJobId, uint CurrentHp, uint MaxHp, uint CurrentMp, uint MaxMp, uint TerritoryId, string TerritoryName);
