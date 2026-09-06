using System;
using AllHud.Services;
using AllHud.Windows;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace AllHud;

public sealed class Plugin : IDalamudPlugin, IDisposable
{
	private readonly IDalamudPluginInterface pluginInterface;

	private readonly IPluginLog log;

	private readonly Configuration config;

	private readonly CombatStateTracker combatState;

	private readonly OverlayRenderer overlayRenderer;

	private readonly ConfigWindow configWindow;

	private readonly DalamudThemeBridge dalamudThemeBridge;

	public Plugin(IDalamudPluginInterface pluginInterface, IDataManager dataManager, IClientState clientState, ICondition condition, IFramework framework, IObjectTable objectTable, IPartyList partyList, ITargetManager targetManager, IGameGui gameGui, IAddonEventManager addonEventManager, IGameInteropProvider gameInteropProvider, ITextureProvider textureProvider, ICommandManager commandManager, IGameConfig gameConfig, IGameInventory gameInventory, IDtrBar dtrBar, IPluginLog log)
	{
		this.pluginInterface = pluginInterface;
		this.log = log;
		try
		{
			this.log.Information("AllHud initializing: loading configuration.");
			config = (this.pluginInterface.GetPluginConfig() as Configuration) ?? new Configuration();
			if (config.ApplyMigrations())
			{
				this.log.Information("AllHud initializing: saving migrated configuration.");
				this.pluginInterface.SavePluginConfig(config);
			}
			this.pluginInterface.UiBuilder.OverrideGameCursor = false;
			this.log.Information("AllHud initializing: creating combat tracker.");
			combatState = new CombatStateTracker(dataManager, clientState, condition, framework, objectTable, partyList, targetManager, gameGui, gameInteropProvider, log);
			this.log.Information("AllHud initializing: creating overlay renderer.");
			overlayRenderer = new OverlayRenderer(config, combatState, dataManager, textureProvider, gameGui, addonEventManager, commandManager, gameConfig, gameInventory, clientState, objectTable, dtrBar, this.pluginInterface, SaveConfig);
			this.log.Information("AllHud initializing: creating Dalamud theme bridge.");
			dalamudThemeBridge = new DalamudThemeBridge(config, this.pluginInterface, this.log, SaveConfig);
			this.log.Information("AllHud initializing: creating config window.");
			configWindow = new ConfigWindow(config, combatState, dataManager, textureProvider, this.pluginInterface, dalamudThemeBridge, SaveConfig);
			this.log.Information("AllHud initializing: registering UI callbacks.");
			this.pluginInterface.UiBuilder.Draw += Draw;
			this.pluginInterface.UiBuilder.OpenConfigUi += OpenConfigUi;
			this.pluginInterface.UiBuilder.OpenMainUi += OpenConfigUi;
			this.log.Information("AllHud loaded.");
		}
		catch (Exception exception)
		{
			this.log.Error(exception, "AllHud failed during initialization.");
			throw;
		}
	}

	public void Dispose()
	{
		pluginInterface.UiBuilder.Draw -= Draw;
		pluginInterface.UiBuilder.OpenConfigUi -= OpenConfigUi;
		pluginInterface.UiBuilder.OpenMainUi -= OpenConfigUi;
		pluginInterface.UiBuilder.OverrideGameCursor = true;
		dalamudThemeBridge.Dispose();
		overlayRenderer.Dispose();
		combatState.Dispose();
		log.Information("AllHud disposed.");
	}

	private void Draw()
	{
		dalamudThemeBridge.Tick();
		overlayRenderer.Draw();
		configWindow.Draw();
	}

	private void OpenConfigUi()
	{
		configWindow.IsOpen = !configWindow.IsOpen;
	}

	private void SaveConfig()
	{
		pluginInterface.SavePluginConfig(config);
	}
}
