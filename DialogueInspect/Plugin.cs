using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using DialogueInspect.Automation;
using DialogueInspect.Inspection;
using DialogueInspect.Models;
using DialogueInspect.Windows;

namespace DialogueInspect;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/di";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IChatGui Chat { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static ITargetManager Targets { get; private set; } = null!;
    [PluginService] internal static IObjectTable Objects { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IGameInventory GameInventory { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;

    internal static Configuration Configuration { get; private set; } = null!;
    internal static DialoguePreset Working { get; private set; } = new();
    internal static DialogueInspector Inspector { get; private set; } = null!;
    internal static ResourceCounter Resources { get; private set; } = null!;
    internal static PresetRunner Runner { get; private set; } = null!;

    private readonly WindowSystem windows = new("DialogueInspect");
    private readonly MainWindow mainWindow;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Inspector = new DialogueInspector();
        Inspector.Register();
        Resources = new ResourceCounter();
        Runner = new PresetRunner();
        ResetWorking();
        if (!string.IsNullOrWhiteSpace(Configuration.SelectedPresetName))
            LoadPreset(Configuration.SelectedPresetName);

        mainWindow = new MainWindow();
        windows.AddWindow(mainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "DialogueInspect: /di  /di start [名前]  /di stop  /di list  /di save [名前]  /di status",
        });

        PluginInterface.UiBuilder.Draw += windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMain;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleMain;
        Framework.Update += OnUpdate;
    }

    public void Dispose()
    {
        Framework.Update -= OnUpdate;
        PluginInterface.UiBuilder.Draw -= windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMain;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleMain;
        windows.RemoveAllWindows();
        CommandManager.RemoveHandler(CommandName);
        Inspector.Unregister();
        Runner.Stop("プラグイン無効化");
    }

    internal void ToggleMain() => mainWindow.Toggle();

    internal static void ResetWorking()
    {
        Working = new DialoguePreset
        {
            Name = Configuration.SelectedPresetName,
            OnceOnly = true,
        };
    }

    internal static bool LoadPreset(string name)
    {
        var found = Configuration.Find(name);
        if (found == null)
            return false;
        Working = found.Clone();
        Configuration.SelectedPresetName = found.Name;
        Configuration.Save();
        return true;
    }

    internal static bool SaveWorking(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            Chat.PrintError("保存名を書いてください。");
            return false;
        }

        Working.Name = name.Trim();
        Configuration.Upsert(Working.Clone());
        return true;
    }

    private void OnUpdate(IFramework framework)
    {
        try
        {
            Inspector.Tick();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "選択窓の更新に失敗");
        }

        try
        {
            Runner.Tick();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "自動実行の更新に失敗");
        }
    }

    private void OnCommand(string command, string args)
    {
        var parts = (args ?? string.Empty).Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            ToggleMain();
            return;
        }

        var sub = parts[0];
        var rest = parts.Length > 1 ? parts[1].Trim() : string.Empty;

        if (IsToken(sub, "start", "開始"))
        {
            StartPreset(rest);
            return;
        }

        if (IsToken(sub, "stop", "停止"))
        {
            if (Runner.IsRunning)
                Runner.Stop("手動停止");
            else
                Chat.Print("DialogueInspect: 実行していません。");
            return;
        }

        if (IsToken(sub, "list", "一覧"))
        {
            if (Configuration.Presets.Count == 0)
            {
                Chat.Print("DialogueInspect: プリセットはありません。");
                return;
            }

            foreach (var preset in Configuration.Presets)
                Chat.Print($"- {preset.Name}  (BaseId {preset.Parent.BaseId} {preset.Parent.Name})");
            return;
        }

        if (IsToken(sub, "save", "保存"))
        {
            var name = string.IsNullOrWhiteSpace(rest) ? Working.Name : rest;
            if (SaveWorking(name))
                Chat.Print($"DialogueInspect: 「{name}」を保存しました。");
            return;
        }

        if (IsToken(sub, "status", "状態"))
        {
            Chat.Print(Runner.IsRunning
                ? $"DialogueInspect: 実行中「{Runner.ActiveName}」 {Runner.Status}"
                : $"DialogueInspect: 停止中。{Runner.Status}");
            return;
        }

        Chat.Print("使い方: /di  /di start [名前]  /di stop  /di list  /di save [名前]  /di status");
    }

    private static void StartPreset(string name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            if (!LoadPreset(name))
            {
                Chat.PrintError($"プリセット「{name}」はありません。 /di list で確認してください。");
                return;
            }
        }
        else if (!string.IsNullOrWhiteSpace(Configuration.SelectedPresetName))
        {
            LoadPreset(Configuration.SelectedPresetName);
        }

        Runner.Start(Working);
    }

    private static bool IsToken(string value, params string[] names)
        => names.Any(n => value.Equals(n, StringComparison.OrdinalIgnoreCase));
}
