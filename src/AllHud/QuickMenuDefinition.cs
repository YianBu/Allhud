using System.Collections.Generic;

namespace AllHud;

public sealed class QuickMenuDefinition
{
	public string Name { get; set; } = "快捷菜单";

	public uint IconId { get; set; }

	public List<string> ComponentOrder { get; set; } = new List<string>();
}
