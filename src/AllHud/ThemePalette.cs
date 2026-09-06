using System.Numerics;

namespace AllHud;

public sealed record ThemePalette(string Name, string Description, Vector4 WindowBg, Vector4 NavBg, Vector4 ContentBg, Vector4 Surface, Vector4 SurfaceAlt, Vector4 Text, Vector4 TextMuted, Vector4 Border, Vector4 Accent, Vector4 AccentSoft, Vector4 FrameBg, Vector4 FrameHovered, Vector4 FrameActive, Vector4 Button, Vector4 ButtonHovered, Vector4 ButtonActive, Vector4 Header, Vector4 HeaderHovered, Vector4 HeaderActive, Vector4 ScrollbarBg, Vector4 ScrollbarGrab, Vector4 ScrollbarGrabHovered, Vector4 ScrollbarGrabActive, Vector4 TitleBar, Vector4 TitlePill, Vector4 PopupBg, Vector4 TooltipBg, Vector4 TooltipText, Vector4 TaskBarBackground, Vector4 TaskBarGradientStart, Vector4 TaskBarGradientEnd, Vector4 TaskBarCard, Vector4 TaskBarCardAlt, Vector4 TaskBarCardHovered, Vector4 TaskBarCardActive, Vector4 TaskBarText, Vector4 TextShadow, float Rounding);
