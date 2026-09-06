using System.Collections.Generic;
using System.Numerics;

namespace AllHud;

public static class ThemeCatalog
{
	public static readonly ThemePalette Pink = new ThemePalette("粉白", "AllHud 原有的明亮暖粉配色。", new Vector4(0.992f, 0.94f, 0.948f, 1f), new Vector4(0.982f, 0.9f, 0.918f, 1f), new Vector4(1f, 0.972f, 0.978f, 1f), new Vector4(1f, 0.97f, 0.976f, 1f), new Vector4(1f, 0.925f, 0.94f, 1f), new Vector4(0.42f, 0.28f, 0.35f, 1f), new Vector4(0.56f, 0.4f, 0.47f, 0.86f), new Vector4(0.91f, 0.7f, 0.75f, 0.65f), new Vector4(0.88f, 0.36f, 0.58f, 1f), new Vector4(0.96f, 0.68f, 0.8f, 0.42f), new Vector4(1f, 0.985f, 0.98f, 1f), new Vector4(0.985f, 0.905f, 0.92f, 1f), new Vector4(0.955f, 0.795f, 0.835f, 1f), new Vector4(1f, 0.985f, 0.98f, 1f), new Vector4(0.975f, 0.85f, 0.875f, 1f), new Vector4(0.925f, 0.68f, 0.735f, 1f), new Vector4(0.965f, 0.84f, 0.87f, 1f), new Vector4(0.94f, 0.76f, 0.81f, 1f), new Vector4(0.9f, 0.66f, 0.72f, 1f), new Vector4(0.94f, 0.85f, 0.86f, 0.35f), new Vector4(0.9f, 0.56f, 0.64f, 0.68f), new Vector4(0.87f, 0.46f, 0.56f, 0.82f), new Vector4(0.82f, 0.38f, 0.5f, 1f), new Vector4(1f, 0.925f, 0.94f, 1f), new Vector4(1f, 0.98f, 0.97f, 0.72f), new Vector4(1f, 0.9f, 0.95f, 1f), new Vector4(0.16f, 0.1f, 0.14f, 0.96f), new Vector4(1f, 0.92f, 0.96f, 1f), new Vector4(1f, 0.84f, 0.92f, 0.92f), new Vector4(1f, 0.96f, 0.99f, 0.48f), new Vector4(0.97f, 0.88f, 1f, 0.34f), new Vector4(1f, 0.93f, 0.97f, 0.52f), new Vector4(1f, 0.88f, 0.95f, 0.48f), new Vector4(1f, 0.96f, 0.99f, 0.78f), new Vector4(1f, 0.86f, 0.94f, 0.88f), new Vector4(0.2f, 0.13f, 0.16f, 1f), new Vector4(1f, 0.97f, 0.99f, 0.55f), 8f);

	public static readonly ThemePalette Dark = new ThemePalette("深色", "低亮度的蓝灰底色与紫蓝强调色。", new Vector4(0.075f, 0.082f, 0.105f, 1f), new Vector4(0.09f, 0.1f, 0.13f, 1f), new Vector4(0.105f, 0.115f, 0.145f, 1f), new Vector4(0.12f, 0.13f, 0.165f, 1f), new Vector4(0.145f, 0.155f, 0.195f, 1f), new Vector4(0.9f, 0.92f, 0.97f, 1f), new Vector4(0.67f, 0.7f, 0.78f, 0.92f), new Vector4(0.3f, 0.34f, 0.46f, 0.82f), new Vector4(0.47f, 0.64f, 1f, 1f), new Vector4(0.42f, 0.54f, 0.92f, 0.34f), new Vector4(0.145f, 0.155f, 0.195f, 1f), new Vector4(0.185f, 0.2f, 0.255f, 1f), new Vector4(0.22f, 0.235f, 0.305f, 1f), new Vector4(0.155f, 0.17f, 0.215f, 1f), new Vector4(0.205f, 0.225f, 0.29f, 1f), new Vector4(0.27f, 0.3f, 0.4f, 1f), new Vector4(0.175f, 0.195f, 0.255f, 1f), new Vector4(0.23f, 0.26f, 0.345f, 1f), new Vector4(0.285f, 0.33f, 0.445f, 1f), new Vector4(0.055f, 0.06f, 0.08f, 0.72f), new Vector4(0.28f, 0.32f, 0.44f, 0.82f), new Vector4(0.36f, 0.43f, 0.6f, 0.92f), new Vector4(0.43f, 0.53f, 0.76f, 1f), new Vector4(0.11f, 0.12f, 0.155f, 1f), new Vector4(0.16f, 0.18f, 0.24f, 0.92f), new Vector4(0.095f, 0.105f, 0.14f, 0.98f), new Vector4(0.035f, 0.04f, 0.055f, 0.98f), new Vector4(0.93f, 0.95f, 1f, 1f), new Vector4(0.08f, 0.09f, 0.125f, 0.94f), new Vector4(0.19f, 0.23f, 0.34f, 0.48f), new Vector4(0.1f, 0.12f, 0.18f, 0.2f), new Vector4(0.15f, 0.17f, 0.23f, 0.72f), new Vector4(0.12f, 0.14f, 0.2f, 0.7f), new Vector4(0.22f, 0.26f, 0.36f, 0.88f), new Vector4(0.18f, 0.22f, 0.32f, 0.94f), new Vector4(0.91f, 0.93f, 0.98f, 1f), new Vector4(0f, 0f, 0f, 0.64f), 9f);

	public static readonly ThemePalette LiquidGlass = new ThemePalette("液态玻璃", "iOS 26 风格的清透无色玻璃、实时背景模糊与柔和立体边缘。", new Vector4(0.075f, 0.09f, 0.11f, 0.18f), new Vector4(0.09f, 0.108f, 0.132f, 0.16f), new Vector4(0.07f, 0.085f, 0.105f, 0.13f), new Vector4(0.105f, 0.125f, 0.15f, 0.18f), new Vector4(0.145f, 0.17f, 0.2f, 0.23f), new Vector4(0.97f, 0.98f, 1f, 1f), new Vector4(0.79f, 0.82f, 0.87f, 0.94f), new Vector4(0.82f, 0.9f, 1f, 0.28f), new Vector4(0.12f, 0.56f, 1f, 1f), new Vector4(0.12f, 0.56f, 1f, 0.28f), new Vector4(0.105f, 0.125f, 0.15f, 0.2f), new Vector4(0.155f, 0.185f, 0.22f, 0.3f), new Vector4(0.205f, 0.24f, 0.285f, 0.42f), new Vector4(0.115f, 0.138f, 0.165f, 0.22f), new Vector4(0.175f, 0.205f, 0.245f, 0.32f), new Vector4(0.23f, 0.265f, 0.315f, 0.45f), new Vector4(0.125f, 0.15f, 0.18f, 0.22f), new Vector4(0.19f, 0.225f, 0.27f, 0.34f), new Vector4(0.245f, 0.285f, 0.34f, 0.47f), new Vector4(0.03f, 0.04f, 0.052f, 0.28f), new Vector4(0.48f, 0.57f, 0.7f, 0.58f), new Vector4(0.59f, 0.7f, 0.84f, 0.72f), new Vector4(0.7f, 0.8f, 0.94f, 0.84f), new Vector4(0.105f, 0.125f, 0.15f, 0.2f), new Vector4(0.14f, 0.18f, 0.23f, 0.28f), new Vector4(0.075f, 0.09f, 0.11f, 0.16f), new Vector4(0.035f, 0.045f, 0.06f, 0.86f), new Vector4(0.97f, 0.98f, 1f, 1f), new Vector4(0.08f, 0.1f, 0.125f, 0.18f), new Vector4(0.88f, 0.94f, 1f, 0.2f), new Vector4(0.72f, 0.84f, 1f, 0.1f), new Vector4(0.105f, 0.13f, 0.16f, 0.2f), new Vector4(0.09f, 0.115f, 0.145f, 0.18f), new Vector4(0.175f, 0.21f, 0.255f, 0.34f), new Vector4(0.15f, 0.185f, 0.225f, 0.42f), new Vector4(0.98f, 0.985f, 1f, 1f), new Vector4(0f, 0f, 0f, 0.76f), 18f);

	public static IEnumerable<(AllHudThemeMode Mode, ThemePalette Palette)> All
	{
		get
		{
			yield return (Mode: AllHudThemeMode.Pink, Palette: Pink);
			yield return (Mode: AllHudThemeMode.Dark, Palette: Dark);
			yield return (Mode: AllHudThemeMode.LiquidGlass, Palette: LiquidGlass);
		}
	}

	public static ThemePalette Get(AllHudThemeMode mode)
	{
		return mode switch
		{
			AllHudThemeMode.Dark => Dark, 
			AllHudThemeMode.LiquidGlass => LiquidGlass, 
			_ => Pink, 
		};
	}
}
