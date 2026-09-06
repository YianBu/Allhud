using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace AllHud;

public static class ThemeDrawing
{
	public static bool IsLiquidGlass(AllHudThemeMode mode)
	{
		return mode == AllHudThemeMode.LiquidGlass;
	}

	public static void PrependLiquidGlassBlur(ImDrawListPtr drawList, Vector2 min, Vector2 max, float rounding, Configuration config, float opacity = 1f)
	{
		if (!(max.X <= min.X) && !(max.Y <= min.Y))
		{
			float num = Math.Clamp(config.LiquidGlassOpacity, 0.1f, 1f);
			float num2 = Math.Clamp(opacity, 0f, 1f) * num;
			Vector4 liquidGlassTintColor = config.LiquidGlassTintColor;
			Vector4 tintColor = new Vector4(liquidGlassTintColor.X, liquidGlassTintColor.Y, liquidGlassTintColor.Z, config.LiquidGlassTintStrength * num2);
			float liquidGlassBrightness = config.LiquidGlassBrightness;
			Vector4 luminosityColor = new Vector4(liquidGlassBrightness, liquidGlassBrightness, liquidGlassBrightness, 0.38f * num2);
			if (config.LiquidGlassBlurStrength > 0.001f)
			{
				ImGuiHelpers.PrependBlurBehind(drawList, min, max, config.LiquidGlassBlurStrength, ClampRounding(rounding, min, max), tintColor, luminosityColor, config.LiquidGlassNoise * num2);
			}
		}
	}

	public static void AddLiquidGlassBlur(ImDrawListPtr drawList, Vector2 min, Vector2 max, float rounding, Configuration config, float opacity = 1f)
	{
		if (!(max.X <= min.X) && !(max.Y <= min.Y))
		{
			float num = Math.Clamp(config.LiquidGlassOpacity, 0.1f, 1f);
			float num2 = Math.Clamp(opacity, 0f, 1f) * num;
			Vector4 liquidGlassTintColor = config.LiquidGlassTintColor;
			Vector4 tintColor = new Vector4(liquidGlassTintColor.X, liquidGlassTintColor.Y, liquidGlassTintColor.Z, config.LiquidGlassTintStrength * num2);
			float liquidGlassBrightness = config.LiquidGlassBrightness;
			Vector4 luminosityColor = new Vector4(liquidGlassBrightness, liquidGlassBrightness, liquidGlassBrightness, 0.38f * num2);
			if (config.LiquidGlassBlurStrength > 0.001f)
			{
				ImGuiHelpers.AddBlurBehind(drawList, min, max, config.LiquidGlassBlurStrength, ClampRounding(rounding, min, max), tintColor, luminosityColor, config.LiquidGlassNoise * num2);
			}
		}
	}

	public static float GetLiquidGlassRounding(Configuration config, float scale = 1f)
	{
		return Math.Max(0f, config.LiquidGlassRounding * scale);
	}

	public static float GetLiquidGlassSurfaceOpacity(Configuration config, float opacity = 1f)
	{
		return Math.Clamp(opacity, 0f, 1f) * Math.Clamp(config.LiquidGlassOpacity, 0.1f, 1f);
	}

	public static Vector4 ApplyLiquidGlassOpacity(Configuration config, Vector4 color, float opacity = 1f)
	{
		Vector4 result = color;
		result.W = color.W * GetLiquidGlassSurfaceOpacity(config, opacity);
		return result;
	}

	public static void DrawLiquidGlassPanel(ImDrawListPtr drawList, Vector2 min, Vector2 max, float rounding, Configuration config, float opacity = 1f, bool drawShadow = true)
	{
		if (!(max.X <= min.X) && !(max.Y <= min.Y))
		{
			rounding = ClampRounding(rounding, min, max);
			opacity = GetLiquidGlassSurfaceOpacity(config, opacity);
			if (drawShadow)
			{
				drawList.AddRectFilled(min + new Vector2(0f, 4f), max + new Vector2(0f, 6f), ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.3f * opacity)), rounding + 2f);
			}
			float value = Math.Max(1f, rounding * 0.12f);
			Vector2 pMin = min + new Vector2(value);
			Vector2 vector = max - new Vector2(value);
			drawList.AddRectFilledMultiColor(pMax: new Vector2(vector.X, Math.Min(vector.Y, pMin.Y + Math.Max(8f, (vector.Y - pMin.Y) * 0.48f))), pMin: pMin, colUprLeft: ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.13f * opacity)), colUprRight: ImGui.GetColorU32(new Vector4(0.88f, 0.94f, 1f, 0.08f * opacity)), colBotRight: ImGui.GetColorU32(new Vector4(0.68f, 0.74f, 0.82f, 0.014f * opacity)), colBotLeft: ImGui.GetColorU32(new Vector4(0.78f, 0.84f, 0.92f, 0.022f * opacity)));
			float num = Math.Max(6f, rounding * 0.7f);
			drawList.AddLine(min + new Vector2(num, 1.5f), new Vector2(max.X - num, min.Y + 1.5f), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.48f * opacity)), 1.15f);
			drawList.AddLine(min + new Vector2(1.5f, num), new Vector2(min.X + 1.5f, Math.Min(max.Y - num, min.Y + (max.Y - min.Y) * 0.6f)), ImGui.GetColorU32(new Vector4(0.9f, 0.96f, 1f, 0.22f * opacity)), 1f);
			drawList.AddRect(min + new Vector2(1f), max - new Vector2(1f), ImGui.GetColorU32(new Vector4(0.92f, 0.96f, 1f, 0.22f * opacity)), Math.Max(1f, rounding - 1f), ImDrawFlags.None, 1f);
		}
	}

	private static float ClampRounding(float rounding, Vector2 min, Vector2 max)
	{
		return Math.Clamp(rounding, 0f, Math.Max(0f, Math.Min(max.X - min.X, max.Y - min.Y) * 0.5f));
	}
}
