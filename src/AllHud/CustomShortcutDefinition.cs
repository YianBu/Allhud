namespace AllHud;

public sealed class CustomShortcutDefinition
{
	public string Name { get; set; } = "快捷方式";

	public uint IconId { get; set; }

	public string Command { get; set; } = string.Empty;
}
