namespace AllHud;

public sealed class CustomTrackedDefinition
{
	public bool Enabled { get; set; } = true;

	public string Name { get; set; } = "定制技能";

	public CustomTrackType Type { get; set; } = CustomTrackType.MitigationCooldown;

	public uint ActionId { get; set; }

	public uint StatusId { get; set; }

	public float CooldownSeconds { get; set; } = 60f;

	public float DurationSeconds { get; set; } = 10f;
}
