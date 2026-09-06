using System.Collections.Generic;
using System.Numerics;

namespace AllHud;

public sealed class AuxiliaryBarDefinition
{
	public bool Enabled { get; set; }

	public string Name { get; set; } = "辅助栏";

	public int PositionMode { get; set; }

	public bool StretchToEdges { get; set; }

	public int LayoutDirection { get; set; }

	public float VerticalOffset { get; set; } = 0.5f;

	public Vector2 CustomPosition { get; set; } = new Vector2(120f, 240f);

	public float Scale { get; set; } = 1f;

	public float Opacity { get; set; } = 1f;

	public List<string> ComponentOrder { get; set; } = new List<string>();

	public List<string> SectionStartComponentOrder { get; set; } = new List<string>();

	public List<string> SectionCenterComponentOrder { get; set; } = new List<string>();

	public List<string> SectionEndComponentOrder { get; set; } = new List<string>();
}
