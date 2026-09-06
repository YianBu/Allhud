namespace AllHud.Data;

public sealed class TrackedActionDefinition
{
	public string Key { get; }

	public uint ClassJobId { get; }

	public string JobName { get; }

	public TrackedStatusDefinition Definition { get; }

	public bool EnabledByDefault { get; }

	public bool IsSharedSkill { get; }

	public TrackedActionDefinition(string key, uint classJobId, string jobName, TrackedStatusDefinition definition, bool enabledByDefault = true, bool isSharedSkill = false)
	{
		Key = key;
		ClassJobId = classJobId;
		JobName = jobName;
		Definition = definition;
		EnabledByDefault = enabledByDefault;
		IsSharedSkill = isSharedSkill;
	}
}
