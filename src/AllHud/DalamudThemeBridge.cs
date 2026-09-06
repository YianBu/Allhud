using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace AllHud;

public sealed class DalamudThemeBridge : IDisposable
{
	private const string BaseStyleName = "AllHud Liquid Glass";

	private const BindingFlags AnyMember = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

	private readonly Configuration config;

	private readonly IDalamudPluginInterface pluginInterface;

	private readonly IPluginLog log;

	private readonly Action saveConfig;

	private readonly Assembly dalamudAssembly;

	private Type? configurationType;

	private Type? styleModelType;

	private Type? styleModelV1Type;

	private Type? interfaceManagerType;

	private object? dalamudConfiguration;

	private bool disposed;

	private bool suppressStyleChanged;

	private bool styleChangedPending;

	private bool applyPending;

	private bool restorePending;

	private bool hasObservedManagedStyle;

	private string status = "尚未检测卫月主题接口。";

	private string currentChosenStyle = string.Empty;

	public bool IsAvailable { get; private set; }

	public string Status => status;

	public string CurrentChosenStyle => currentChosenStyle;

	public bool IsManagedStyleActive
	{
		get
		{
			if (!string.IsNullOrEmpty(config.DalamudThemeManagedStyleName))
			{
				return string.Equals(currentChosenStyle, config.DalamudThemeManagedStyleName, StringComparison.Ordinal);
			}
			return false;
		}
	}

	public bool CanRestore => config.DalamudThemeRestoreArmed;

	public string RestoreTarget
	{
		get
		{
			if (!config.DalamudThemeRestoreArmed)
			{
				return "无（当前主题不会被更改）";
			}
			return config.DalamudThemeOriginalChosenStyle ?? "卫月默认主题";
		}
	}

	public DalamudThemeBridge(Configuration config, IDalamudPluginInterface pluginInterface, IPluginLog log, Action saveConfig)
	{
		this.config = config;
		this.pluginInterface = pluginInterface;
		this.log = log;
		this.saveConfig = saveConfig;
		dalamudAssembly = typeof(IDalamudPluginInterface).Assembly;
		this.pluginInterface.UiBuilder.DefaultStyleChanged += OnDefaultStyleChanged;
		RefreshState();
	}

	public void Tick()
	{
		if (applyPending)
		{
			applyPending = false;
			Apply(config);
		}
		else if (restorePending)
		{
			restorePending = false;
			Restore();
		}
		else if (styleChangedPending)
		{
			styleChangedPending = false;
			RefreshState();
		}
	}

	public void RequestApply()
	{
		restorePending = false;
		applyPending = true;
		status = "已排队，将在下一帧安全应用卫月主题。";
	}

	public void RequestRestore()
	{
		applyPending = false;
		restorePending = true;
		status = "已排队，将在下一帧安全还原卫月主题。";
	}

	public void RefreshState()
	{
		if (disposed)
		{
			return;
		}
		try
		{
			EnsureReflection();
			currentChosenStyle = GetRequiredProperty<string>(dalamudConfiguration, "ChosenStyle") ?? string.Empty;
			if (config.DalamudThemeRestoreArmed && !string.Equals(currentChosenStyle, config.DalamudThemeManagedStyleName, StringComparison.Ordinal))
			{
				config.DalamudThemeRestoreArmed = false;
				config.DalamudThemeOriginalChosenStyle = null;
				config.DalamudBlurRestoreArmed = false;
				saveConfig();
			}
			if (string.Equals(currentChosenStyle, config.DalamudThemeManagedStyleName, StringComparison.Ordinal))
			{
				hasObservedManagedStyle = true;
			}
			IsAvailable = true;
			status = (IsManagedStyleActive ? "AllHud 液态玻璃已应用到卫月主题。" : "反射接口可用，可以创建并应用卫月全局主题。");
		}
		catch (Exception exception)
		{
			IsAvailable = false;
			status = "反射不可用：" + GetRootMessage(exception);
			log.Warning(exception, "AllHud could not inspect the Dalamud theme bridge.");
		}
	}

	public bool Apply(Configuration source)
	{
		try
		{
			EnsureReflection();
			string text = GetRequiredProperty<string>(dalamudConfiguration, "ChosenStyle") ?? string.Empty;
			if (string.IsNullOrEmpty(config.DalamudThemeManagedStyleName) || !string.Equals(text, config.DalamudThemeManagedStyleName, StringComparison.Ordinal))
			{
				config.DalamudThemeOriginalChosenStyle = text;
				config.DalamudThemeRestoreArmed = true;
			}
			IList orCreateSavedStyles = GetOrCreateSavedStyles();
			string text2 = ResolveWritableStyleName(orCreateSavedStyles);
			object obj = CreateLiquidGlassStyle(source, text2);
			ReplaceOrAppendStyle(orCreateSavedStyles, text2, obj);
			config.DalamudBlurRestoreArmed = true;
			config.DalamudLastAppliedBlurStrength = GetStyleBlurStrength(obj);
			config.DalamudThemeManagedStyleName = text2;
			config.DalamudThemeManagedStyleFingerprint = Fingerprint(obj);
			saveConfig();
			SetRequiredProperty(dalamudConfiguration, "ChosenStyle", text2);
			suppressStyleChanged = true;
			try
			{
				InvokeVoid(obj, "Apply");
				QueueDalamudSave();
				InvokeStyleChanged();
			}
			finally
			{
				suppressStyleChanged = false;
			}
			currentChosenStyle = text2;
			hasObservedManagedStyle = true;
			IsAvailable = true;
			status = "已创建或更新 AllHud 专属样式，并应用到卫月及标准插件窗口。";
			log.Information("Applied AllHud liquid glass to Dalamud style {StyleName}.", text2);
			return true;
		}
		catch (Exception exception)
		{
			IsAvailable = false;
			status = "应用失败：" + GetRootMessage(exception);
			log.Warning(exception, "AllHud failed to apply its liquid glass Dalamud theme.");
			return false;
		}
	}

	public bool Restore()
	{
		try
		{
			EnsureReflection();
			List<string> list = new List<string>();
			string text = GetRequiredProperty<string>(dalamudConfiguration, "ChosenStyle") ?? string.Empty;
			bool flag = false;
			if (config.DalamudThemeRestoreArmed)
			{
				if (string.Equals(text, config.DalamudThemeManagedStyleName, StringComparison.Ordinal))
				{
					string dalamudThemeOriginalChosenStyle = config.DalamudThemeOriginalChosenStyle;
					IList orCreateSavedStyles = GetOrCreateSavedStyles();
					object obj = FindRestorableStyle(orCreateSavedStyles, dalamudThemeOriginalChosenStyle);
					if (obj == null)
					{
						list.Add("原卫月主题已不存在，未更改当前主题");
					}
					else
					{
						string text2 = GetRequiredProperty<string>(obj, "Name") ?? string.Empty;
						object obj2 = FindStyle(orCreateSavedStyles, text);
						suppressStyleChanged = true;
						try
						{
							InvokeVoid(obj, "Apply");
							SetRequiredProperty(dalamudConfiguration, "ChosenStyle", text2);
							InvokeStyleChanged();
							QueueDalamudSave();
						}
						catch
						{
							try
							{
								if (obj2 != null)
								{
									InvokeVoid(obj2, "Apply");
								}
								SetRequiredProperty(dalamudConfiguration, "ChosenStyle", text);
								InvokeStyleChanged();
							}
							catch (Exception exception)
							{
								log.Error(exception, "AllHud failed to roll back a partial Dalamud theme restore.");
							}
							throw;
						}
						finally
						{
							suppressStyleChanged = false;
						}
						text = text2;
						flag = true;
						list.Add("已恢复主题：" + text);
					}
				}
				else
				{
					list.Add("你已手动切换卫月主题，未覆盖当前选择");
				}
				if (flag || !string.Equals(text, config.DalamudThemeManagedStyleName, StringComparison.Ordinal))
				{
					config.DalamudThemeRestoreArmed = false;
					config.DalamudThemeOriginalChosenStyle = null;
				}
			}
			if (flag)
			{
				list.Add("原主题的全局模糊参数已一并恢复");
				config.DalamudBlurRestoreArmed = false;
			}
			else if (!config.DalamudThemeRestoreArmed)
			{
				config.DalamudBlurRestoreArmed = false;
			}
			if (flag || list.Count > 0)
			{
				QueueDalamudSave();
			}
			saveConfig();
			currentChosenStyle = text;
			status = ((list.Count > 0) ? (string.Join("；", list) + "。") : "没有可还原的卫月主题状态。");
			return true;
		}
		catch (Exception exception2)
		{
			IsAvailable = false;
			status = "还原失败：" + GetRootMessage(exception2);
			log.Warning(exception2, "AllHud failed to restore the previous Dalamud theme.");
			return false;
		}
	}

	public void Dispose()
	{
		if (!disposed)
		{
			disposed = true;
			pluginInterface.UiBuilder.DefaultStyleChanged -= OnDefaultStyleChanged;
		}
	}

	private void OnDefaultStyleChanged()
	{
		if (suppressStyleChanged)
		{
			return;
		}
		if (config.DalamudThemeRestoreArmed && hasObservedManagedStyle)
		{
			try
			{
				EnsureReflection();
				if (!string.Equals(GetRequiredProperty<string>(dalamudConfiguration, "ChosenStyle") ?? string.Empty, config.DalamudThemeManagedStyleName, StringComparison.Ordinal))
				{
					config.DalamudThemeRestoreArmed = false;
					config.DalamudThemeOriginalChosenStyle = null;
					config.DalamudBlurRestoreArmed = false;
					saveConfig();
				}
			}
			catch (Exception exception)
			{
				log.Debug(exception, "AllHud could not reconcile an external Dalamud style change.");
			}
		}
		styleChangedPending = true;
	}

	private void EnsureReflection()
	{
		if (dalamudConfiguration == null)
		{
			configurationType = RequireType("Dalamud.Configuration.Internal.DalamudConfiguration");
			styleModelType = RequireType("Dalamud.Interface.Style.StyleModel");
			styleModelV1Type = RequireType("Dalamud.Interface.Style.StyleModelV1");
			interfaceManagerType = RequireType("Dalamud.Interface.Internal.InterfaceManager");
			dalamudConfiguration = GetInternalService(configurationType) ?? throw new InvalidOperationException("无法获取 DalamudConfiguration 服务");
			RequireProperty(configurationType, "SavedStyles", writable: true);
			RequireProperty(configurationType, "ChosenStyle", writable: true);
			RequireMethod(configurationType, "QueueSave");
			RequireMethod(styleModelType, "Clone");
			RequireMethod(styleModelType, "Apply");
			RequireMethod(styleModelType, "Serialize");
			RequireProperty(styleModelType, "Name", writable: true);
			RequireProperty(styleModelV1Type, "Colors", writable: true);
			RequireProperty(styleModelV1Type, "WindowBlurStrength", writable: true);
			RequireProperty(styleModelV1Type, "WindowBlurTint", writable: true);
			RequireProperty(styleModelV1Type, "WindowBlurTintActive", writable: true);
			RequireProperty(styleModelV1Type, "WindowBlurLuminosity", writable: true);
		}
	}

	private IList GetOrCreateSavedStyles()
	{
		PropertyInfo propertyInfo = RequireProperty(configurationType, "SavedStyles", writable: true);
		if (propertyInfo.GetValue(dalamudConfiguration) is IList result)
		{
			return result;
		}
		IList list = ((IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(styleModelType))) ?? throw new InvalidOperationException("无法创建卫月样式列表");
		propertyInfo.SetValue(dalamudConfiguration, list);
		return list;
	}

	private string ResolveWritableStyleName(IList styles)
	{
		string dalamudThemeManagedStyleName = config.DalamudThemeManagedStyleName;
		if (!string.IsNullOrEmpty(dalamudThemeManagedStyleName))
		{
			object obj = FindStyle(styles, dalamudThemeManagedStyleName);
			if (obj == null || string.Equals(Fingerprint(obj), config.DalamudThemeManagedStyleFingerprint, StringComparison.OrdinalIgnoreCase))
			{
				return dalamudThemeManagedStyleName;
			}
		}
		for (int i = 1; i < 1000; i++)
		{
			string text = ((i == 1) ? "AllHud Liquid Glass" : $"{"AllHud Liquid Glass"} ({i})");
			if (FindStyle(styles, text) == null)
			{
				return text;
			}
		}
		throw new InvalidOperationException("无法为 AllHud 分配独立的卫月主题名称");
	}

	private object CreateLiquidGlassStyle(Configuration source, string name)
	{
		IList orCreateSavedStyles = GetOrCreateSavedStyles();
		object obj = FindStyle(orCreateSavedStyles, config.DalamudThemeManagedStyleName);
		object obj2 = InvokeStaticRequired(styleModelType, "GetConfiguredStyle");
		object target = ((obj != null && styleModelV1Type.IsInstanceOfType(obj) && !string.IsNullOrEmpty(config.DalamudThemeManagedStyleFingerprint) && string.Equals(Fingerprint(obj), config.DalamudThemeManagedStyleFingerprint, StringComparison.OrdinalIgnoreCase)) ? obj : ((obj2 == null || !styleModelV1Type.IsInstanceOfType(obj2)) ? (RequireProperty(styleModelV1Type, "DalamudStandard", writable: false).GetValue(null) ?? throw new InvalidOperationException("无法读取卫月标准主题")) : obj2));
		object obj3 = InvokeRequired(target, "Clone");
		SetRequiredProperty(obj3, "Name", name);
		DalamudThemeStyleParameters effectiveDalamudThemeParameters = source.GetEffectiveDalamudThemeParameters();
		float rounding = effectiveDalamudThemeParameters.Rounding;
		SetRequiredProperty(obj3, "WindowRounding", rounding);
		SetRequiredProperty(obj3, "ChildRounding", Math.Min(rounding, 16f));
		SetRequiredProperty(obj3, "PopupRounding", Math.Min(rounding, 14f));
		SetRequiredProperty(obj3, "FrameRounding", Math.Min(rounding * 0.67f, 12f));
		SetRequiredProperty(obj3, "TabRounding", Math.Min(rounding * 0.67f, 12f));
		SetRequiredProperty(obj3, "GrabRounding", Math.Min(rounding * 0.5f, 9f));
		SetRequiredProperty(obj3, "ScrollbarRounding", Math.Min(rounding * 0.5f, 9f));
		SetRequiredProperty(obj3, "WindowBorderSize", 1f);
		SetRequiredProperty(obj3, "ChildBorderSize", 1f);
		SetRequiredProperty(obj3, "PopupBorderSize", 1f);
		SetRequiredProperty(obj3, "FrameBorderSize", 0f);
		ThemePalette liquidGlass = ThemeCatalog.LiquidGlass;
		float layerStrength = effectiveDalamudThemeParameters.LayerStrength;
		float windowOpacity = effectiveDalamudThemeParameters.WindowOpacity;
		float tintStrength = effectiveDalamudThemeParameters.TintStrength;
		Vector4 tintColorForStyle = effectiveDalamudThemeParameters.TintColor;
		float alpha = Math.Clamp(windowOpacity * 0.52f, 0.04f, 0.52f);
		float alpha2 = Math.Clamp(0.56f + 0.42f * windowOpacity, 0.56f, 0.96f);
		IDictionary colors = (GetRequiredProperty<object>(obj3, "Colors") as IDictionary) ?? throw new InvalidOperationException("卫月样式 Colors 不是可写字典");
		SetColor(colors, "Text", liquidGlass.Text);
		Vector4 textMuted = liquidGlass.TextMuted;
		textMuted.W = 0.86f;
		SetColor(colors, "TextDisabled", textMuted);
		SetColor(colors, "WindowBg", Tone(liquidGlass.WindowBg, 0.08f, windowOpacity));
		SetColor(colors, "ChildBg", Tone(liquidGlass.Surface, 0.1f, alpha));
		SetColor(colors, "PopupBg", Tone(liquidGlass.PopupBg, 0.1f, alpha2));
		SetColor(colors, "Border", Tone(liquidGlass.Border, 0.12f, 0.18f + 0.16f * layerStrength));
		SetColor(colors, "BorderShadow", new Vector4(0f, 0f, 0f, 0.18f));
		textMuted = liquidGlass.FrameBg;
		textMuted.W = 0.3f + 0.16f * layerStrength;
		SetColor(colors, "FrameBg", textMuted);
		textMuted = liquidGlass.FrameHovered;
		textMuted.W = 0.44f + 0.16f * layerStrength;
		SetColor(colors, "FrameBgHovered", textMuted);
		textMuted = liquidGlass.FrameActive;
		textMuted.W = 0.56f + 0.16f * layerStrength;
		SetColor(colors, "FrameBgActive", textMuted);
		SetColor(colors, "TitleBg", Tone(liquidGlass.TitleBar, 0.1f, 0.46f + 0.14f * layerStrength));
		SetColor(colors, "TitleBgActive", new Vector4(Vector3.Lerp(new Vector3(liquidGlass.TitleBar.X, liquidGlass.TitleBar.Y, liquidGlass.TitleBar.Z), new Vector3(liquidGlass.Accent.X, liquidGlass.Accent.Y, liquidGlass.Accent.Z), 0.16f), 0.58f + 0.14f * layerStrength));
		textMuted = liquidGlass.WindowBg;
		textMuted.W = Math.Clamp(windowOpacity - 0.06f, 0.2f, 0.58f);
		SetColor(colors, "TitleBgCollapsed", textMuted);
		textMuted = liquidGlass.Surface;
		textMuted.W = 0.4f + 0.14f * layerStrength;
		SetColor(colors, "MenuBarBg", textMuted);
		textMuted = liquidGlass.ScrollbarBg;
		textMuted.W = 0.1f + 0.08f * layerStrength;
		SetColor(colors, "ScrollbarBg", textMuted);
		textMuted = liquidGlass.ScrollbarGrab;
		textMuted.W = 0.5f + 0.12f * layerStrength;
		SetColor(colors, "ScrollbarGrab", textMuted);
		textMuted = liquidGlass.ScrollbarGrabHovered;
		textMuted.W = 0.64f + 0.12f * layerStrength;
		SetColor(colors, "ScrollbarGrabHovered", textMuted);
		textMuted = liquidGlass.ScrollbarGrabActive;
		textMuted.W = 0.76f + 0.12f * layerStrength;
		SetColor(colors, "ScrollbarGrabActive", textMuted);
		SetColor(colors, "CheckMark", liquidGlass.Accent);
		textMuted = liquidGlass.Accent;
		textMuted.W = 0.9f;
		SetColor(colors, "SliderGrab", textMuted);
		SetColor(colors, "SliderGrabActive", new Vector4(0.38f, 0.72f, 1f, 1f));
		textMuted = liquidGlass.Button;
		textMuted.W = 0.38f + 0.16f * layerStrength;
		SetColor(colors, "Button", textMuted);
		textMuted = liquidGlass.ButtonHovered;
		textMuted.W = 0.52f + 0.16f * layerStrength;
		SetColor(colors, "ButtonHovered", textMuted);
		textMuted = liquidGlass.ButtonActive;
		textMuted.W = 0.64f + 0.16f * layerStrength;
		SetColor(colors, "ButtonActive", textMuted);
		textMuted = liquidGlass.Header;
		textMuted.W = 0.3f + 0.14f * layerStrength;
		SetColor(colors, "Header", textMuted);
		textMuted = liquidGlass.HeaderHovered;
		textMuted.W = 0.46f + 0.16f * layerStrength;
		SetColor(colors, "HeaderHovered", textMuted);
		textMuted = liquidGlass.HeaderActive;
		textMuted.W = 0.58f + 0.16f * layerStrength;
		SetColor(colors, "HeaderActive", textMuted);
		textMuted = liquidGlass.Border;
		textMuted.W = 0.18f + 0.14f * layerStrength;
		SetColor(colors, "Separator", textMuted);
		textMuted = liquidGlass.AccentSoft;
		textMuted.W = 0.68f;
		SetColor(colors, "SeparatorHovered", textMuted);
		SetColor(colors, "SeparatorActive", liquidGlass.Accent);
		textMuted = liquidGlass.Accent;
		textMuted.W = 0.18f;
		SetColor(colors, "ResizeGrip", textMuted);
		textMuted = liquidGlass.Accent;
		textMuted.W = 0.48f;
		SetColor(colors, "ResizeGripHovered", textMuted);
		textMuted = liquidGlass.Accent;
		textMuted.W = 0.72f;
		SetColor(colors, "ResizeGripActive", textMuted);
		textMuted = liquidGlass.Surface;
		textMuted.W = 0.44f + 0.12f * layerStrength;
		SetColor(colors, "Tab", textMuted);
		textMuted = liquidGlass.HeaderHovered;
		textMuted.W = 0.58f + 0.14f * layerStrength;
		SetColor(colors, "TabHovered", textMuted);
		textMuted = liquidGlass.Header;
		textMuted.W = 0.64f + 0.14f * layerStrength;
		SetColor(colors, "TabActive", textMuted);
		textMuted = liquidGlass.WindowBg;
		textMuted.W = 0.34f + 0.12f * layerStrength;
		SetColor(colors, "TabUnfocused", textMuted);
		textMuted = liquidGlass.Header;
		textMuted.W = 0.5f + 0.12f * layerStrength;
		SetColor(colors, "TabUnfocusedActive", textMuted);
		textMuted = liquidGlass.Accent;
		textMuted.W = 0.42f;
		SetColor(colors, "DockingPreview", textMuted);
		textMuted = liquidGlass.WindowBg;
		textMuted.W = 1f;
		SetColor(colors, "DockingEmptyBg", textMuted);
		textMuted = liquidGlass.Accent;
		textMuted.W = 0.9f;
		SetColor(colors, "PlotLines", textMuted);
		textMuted = Vector4.Lerp(liquidGlass.Accent, Vector4.One, 0.25f);
		textMuted.W = 1f;
		SetColor(colors, "PlotLinesHovered", textMuted);
		textMuted = liquidGlass.Accent;
		textMuted.W = 0.78f;
		SetColor(colors, "PlotHistogram", textMuted);
		textMuted = Vector4.Lerp(liquidGlass.Accent, Vector4.One, 0.25f);
		textMuted.W = 1f;
		SetColor(colors, "PlotHistogramHovered", textMuted);
		textMuted = liquidGlass.SurfaceAlt;
		textMuted.W = 0.46f + 0.12f * layerStrength;
		SetColor(colors, "TableHeaderBg", textMuted);
		textMuted = liquidGlass.Border;
		textMuted.W = 0.3f + 0.1f * layerStrength;
		SetColor(colors, "TableBorderStrong", textMuted);
		textMuted = liquidGlass.Border;
		textMuted.W = 0.16f + 0.08f * layerStrength;
		SetColor(colors, "TableBorderLight", textMuted);
		SetColor(colors, "TableRowBg", Vector4.Zero);
		SetColor(colors, "TableRowBgAlt", new Vector4(1f, 1f, 1f, 0.025f + 0.035f * layerStrength));
		textMuted = liquidGlass.Accent;
		textMuted.W = 0.3f + 0.08f * layerStrength;
		SetColor(colors, "TextSelectedBg", textMuted);
		SetColor(colors, "DragDropTarget", liquidGlass.Accent);
		textMuted = liquidGlass.Accent;
		textMuted.W = 0.85f;
		SetColor(colors, "NavHighlight", textMuted);
		SetColor(colors, "NavWindowingHighlight", new Vector4(1f, 1f, 1f, 0.74f));
		SetColor(colors, "NavWindowingDimBg", new Vector4(0.03f, 0.05f, 0.08f, 0.28f));
		SetColor(colors, "ModalWindowDimBg", new Vector4(0.02f, 0.04f, 0.07f, 0.42f));
		float num = Math.Clamp(effectiveDalamudThemeParameters.BlurStrength / 14f, 0f, 1f);
		float num2 = tintStrength * windowOpacity;
		Vector4 vector = new Vector4(tintColorForStyle.X, tintColorForStyle.Y, tintColorForStyle.Z, num2 * 0.55f);
		Vector4 vector2 = new Vector4(tintColorForStyle.X, tintColorForStyle.Y, tintColorForStyle.Z, num2 * 0.8f);
		Vector4 vector3 = new Vector4(effectiveDalamudThemeParameters.Brightness, effectiveDalamudThemeParameters.Brightness, effectiveDalamudThemeParameters.Brightness, effectiveDalamudThemeParameters.LuminosityStrength);
		SetRequiredProperty(obj3, "WindowBlurStrength", num);
		SetRequiredProperty(obj3, "WindowBlurTint", vector);
		SetRequiredProperty(obj3, "WindowBlurTintActive", vector2);
		SetRequiredProperty(obj3, "WindowBlurLuminosity", vector3);
		return obj3;
		Vector4 Tone(Vector4 color, float tintMix, float value)
		{
			return new Vector4(float.Lerp(color.X, tintColorForStyle.X, tintMix * tintStrength), float.Lerp(color.Y, tintColorForStyle.Y, tintMix * tintStrength), float.Lerp(color.Z, tintColorForStyle.Z, tintMix * tintStrength), Math.Clamp(value, 0f, 1f));
		}
	}

	private void ReplaceOrAppendStyle(IList styles, string name, object replacement)
	{
		for (int i = 0; i < styles.Count; i++)
		{
			object obj = styles[i];
			if (obj != null && string.Equals(GetRequiredProperty<string>(obj, "Name"), name, StringComparison.Ordinal))
			{
				styles[i] = replacement;
				return;
			}
		}
		styles.Add(replacement);
	}

	private object? FindStyle(IList styles, string? name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return null;
		}
		foreach (object style in styles)
		{
			if (style != null && string.Equals(GetRequiredProperty<string>(style, "Name"), name, StringComparison.Ordinal))
			{
				return style;
			}
		}
		return null;
	}

	private object? FindRestorableStyle(IList styles, string? name)
	{
		object obj = FindStyle(styles, name);
		if (obj != null)
		{
			return obj;
		}
		string[] array = new string[3] { "DalamudStandard", "DalamudClassic", "DalamudHazy" };
		foreach (string text in array)
		{
			object obj2 = styleModelV1Type.GetProperty(text, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null);
			if (obj2 != null)
			{
				string requiredProperty = GetRequiredProperty<string>(obj2, "Name");
				if ((string.IsNullOrWhiteSpace(name) && text == "DalamudStandard") || string.Equals(name, requiredProperty, StringComparison.Ordinal))
				{
					return obj2;
				}
			}
		}
		return null;
	}

	private float GetStyleBlurStrength(object style)
	{
		if (!styleModelV1Type.IsInstanceOfType(style))
		{
			return 0f;
		}
		return GetRequiredProperty<float>(style, "WindowBlurStrength");
	}

	private void QueueDalamudSave()
	{
		InvokeVoid(dalamudConfiguration, "QueueSave");
	}

	private void InvokeStyleChanged()
	{
		InvokeVoid(GetInternalService(interfaceManagerType) ?? throw new InvalidOperationException("无法获取 InterfaceManager 服务"), "InvokeStyleChanged");
	}

	private object? GetInternalService(Type serviceType)
	{
		return RequireMethod((dalamudAssembly.GetType("Dalamud.Service`1", throwOnError: false) ?? throw new MissingMemberException("Dalamud.Service<T>")).MakeGenericType(serviceType), "Get").Invoke(null, null);
	}

	private Type RequireType(string fullName)
	{
		return dalamudAssembly.GetType(fullName, throwOnError: false) ?? throw new TypeLoadException(fullName);
	}

	private static PropertyInfo RequireProperty(Type type, string name, bool writable)
	{
		PropertyInfo propertyInfo = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) ?? throw new MissingMemberException(type.FullName, name);
		if (!propertyInfo.CanRead || (writable && !propertyInfo.CanWrite))
		{
			throw new MissingMemberException(type.FullName, name);
		}
		return propertyInfo;
	}

	private static MethodInfo RequireMethod(Type type, string name)
	{
		return type.GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null) ?? throw new MissingMethodException(type.FullName, name);
	}

	private static T? GetRequiredProperty<T>(object target, string name)
	{
		object value = RequireProperty(target.GetType(), name, writable: false).GetValue(target);
		if (value != null)
		{
			return (T)value;
		}
		return default(T);
	}

	private static void SetRequiredProperty(object target, string name, object? value)
	{
		RequireProperty(target.GetType(), name, writable: true).SetValue(target, value);
	}

	private static object InvokeRequired(object target, string name)
	{
		return RequireMethod(target.GetType(), name).Invoke(target, null) ?? throw new InvalidOperationException(target.GetType().FullName + "." + name + " 返回 null");
	}

	private static void InvokeVoid(object target, string name)
	{
		RequireMethod(target.GetType(), name).Invoke(target, null);
	}

	private static object? InvokeStaticRequired(Type type, string name)
	{
		return RequireMethod(type, name).Invoke(null, null);
	}

	private static void SetColor(IDictionary colors, string name, Vector4 value)
	{
		if (colors.Contains(name))
		{
			colors[name] = value;
		}
	}

	private static string Fingerprint(object style)
	{
		string s = (InvokeRequired(style, "Serialize") as string) ?? throw new InvalidOperationException("卫月主题序列化结果为空");
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
	}

	private static string GetRootMessage(Exception exception)
	{
		while (exception is TargetInvocationException && exception.InnerException != null)
		{
			exception = exception.InnerException;
		}
		return exception.Message;
	}
}
