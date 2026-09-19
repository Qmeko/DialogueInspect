using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using DialogueInspect.Inspection;
using DialogueInspect.Models;

namespace DialogueInspect.Windows;

public sealed class MainWindow : Window
{
    private string saveName = string.Empty;
    private DialogueInspector.LiveAddon? opened;
    private float waitSeconds = 1f;

    public MainWindow()
        : base("DialogueInspect##DialogueInspectMain")
    {
        Size = new Vector2(760, 620);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(520, 400),
            MaximumSize = new Vector2(4000, 4000),
        };
    }

    public override void Draw()
    {
        var working = Plugin.Working;
        DrawPresetBar(working);
        ImGui.Separator();
        DrawParent(working);
        ImGui.Separator();
        DrawTalk();
        ImGui.Separator();
        DrawLiveButtons();
        ImGui.Separator();
        DrawOpenedChoices(working);
        ImGui.Separator();
        DrawFlow(working);
    }

    private void DrawPresetBar(DialoguePreset working)
    {
        var names = Plugin.Configuration.Presets.Select(p => p.Name).ToArray();
        var current = Array.FindIndex(names, n => n.Equals(Plugin.Configuration.SelectedPresetName, StringComparison.OrdinalIgnoreCase));
        ImGui.SetNextItemWidth(220);
        if (ImGui.Combo("プリセット", ref current, names, names.Length) && current >= 0)
        {
            Plugin.LoadPreset(names[current]);
            saveName = names[current];
            opened = null;
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(160);
        ImGui.InputTextWithHint("##saveName", "保存名", ref saveName, 64);
        ImGui.SameLine();
        if (ImGui.Button("保存") && Plugin.SaveWorking(saveName))
            Plugin.Chat.Print($"DialogueInspect: 「{saveName}」を保存しました。");
        ImGui.SameLine();
        if (ImGui.Button("削除") && !string.IsNullOrWhiteSpace(Plugin.Configuration.SelectedPresetName))
        {
            Plugin.Configuration.Remove(Plugin.Configuration.SelectedPresetName);
            Plugin.ResetWorking();
            saveName = string.Empty;
            opened = null;
        }

        var running = Plugin.Runner.IsRunning;
        if (running)
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.7f, 0.2f, 0.2f, 1f));
        if (ImGui.Button(running ? "停止" : "開始"))
        {
            if (running)
                Plugin.Runner.Stop("手動停止");
            else
                Plugin.Runner.Start(working);
        }

        if (running)
            ImGui.PopStyleColor();

        ImGui.SameLine();
        var once = working.OnceOnly;
        if (ImGui.Checkbox("1回だけ", ref once))
            working.OnceOnly = once;

        ImGui.TextDisabled(Plugin.Runner.Status);

        var limit = working.Resource;
        var enabled = limit.Enabled;
        if (ImGui.Checkbox("リソース下限", ref enabled))
            limit.Enabled = enabled;

        var currencies = Plugin.Resources.ListHeldCurrencies();
        var labels = currencies
            .Select(c => $"{c.Name} ({c.Quantity})  ID:{c.ItemId}")
            .ToArray();
        var selected = 0;
        for (var i = 0; i < currencies.Count; i++)
        {
            if (currencies[i].ItemId == limit.ItemId)
                selected = i;
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(260);
        if (labels.Length == 0)
            ImGui.TextDisabled("通貨タブにアイテムがありません");
        else if (ImGui.Combo("通貨", ref selected, labels, labels.Length))
            limit.ItemId = currencies[selected].ItemId;

        ImGui.SameLine();
        ImGui.SetNextItemWidth(80);
        var min = limit.MinRemaining;
        if (ImGui.InputInt("残す枚数", ref min))
            limit.MinRemaining = Math.Max(0, min);

        if (limit.ItemId != 0)
        {
            var held = Plugin.Resources.Count(limit.ItemId);
            ImGui.SameLine();
            ImGui.TextDisabled($"今 {held}");
        }
    }

    private static void DrawParent(DialoguePreset working)
    {
        var parent = working.Parent;
        ImGui.Text($"親NPC: {(parent.IsSet ? $"{parent.Name}  BaseId {parent.BaseId}  ({parent.ObjectKind})" : "未設定")}");
        if (ImGui.Button("今のターゲットを親にする"))
        {
            var npc = Plugin.Inspector.TargetAsNpc();
            if (npc == null)
                Plugin.Chat.PrintError("ターゲットがいません。");
            else
            {
                working.Parent = npc;
                Plugin.Chat.Print($"親NPCを「{npc.Name}」（BaseId {npc.BaseId}）にしました。");
            }
        }

        var target = Plugin.Inspector.TargetAsNpc();
        if (target != null)
            ImGui.TextDisabled($"今のターゲット: {target.Name}  BaseId {target.BaseId}  {target.ObjectKind}");
        else
            ImGui.TextDisabled("今のターゲット: なし");
    }

    private static void DrawTalk()
    {
        if (!Plugin.Inspector.TalkVisible)
        {
            ImGui.TextDisabled("会話窓 Talk: 閉じています");
            return;
        }

        ImGui.Text($"会話: {Plugin.Inspector.TalkSpeaker}");
        ImGui.TextWrapped(Plugin.Inspector.TalkBody);
    }

    private void DrawLiveButtons()
    {
        ImGui.Text("今見えている選択窓");
        var live = Plugin.Inspector.Live;
        if (live.Count == 0)
        {
            ImGui.TextDisabled("選択窓は見えていません。NPCに話しかけてリストを出してください。");
            return;
        }

        foreach (var addon in live)
        {
            if (ImGui.Button($"{addon.ButtonLabel()}##live{addon.Name}{addon.ListKey()}"))
            {
                opened = new DialogueInspector.LiveAddon
                {
                    Name = addon.Name,
                    Rows = addon.Rows.Select(r => new ChoiceRow
                    {
                        Index = r.Index,
                        Text = r.Text,
                        ItemId = r.ItemId,
                    }).ToList(),
                };
            }
        }
    }

    private void DrawOpenedChoices(DialoguePreset working)
    {
        if (opened == null)
        {
            ImGui.TextDisabled("上のボタンを押すと、選択肢が一覧されます。");
            return;
        }

        ImGui.Text($"{opened.Name} の選択肢");
        ImGui.TextDisabled("チェックすると、下の実行フローに追加されます。");

        foreach (var row in opened.Rows)
        {
            var inFlow = working.Flow.Any(s => SameStep(s, opened, row, MarkKind.DialogueText));
            if (ImGui.Checkbox($" {row.Index}: {row.Text}##choice{row.Index}", ref inFlow))
                ToggleFlow(working, opened, row, MarkKind.DialogueText, inFlow);

            if (row.ItemId == 0)
                continue;
            ImGui.SameLine();
            var idInFlow = working.Flow.Any(s => SameStep(s, opened, row, MarkKind.ItemId));
            if (ImGui.Checkbox($"ID {row.ItemId}##id{row.Index}{row.ItemId}", ref idInFlow))
                ToggleFlow(working, opened, row, MarkKind.ItemId, idInFlow);
        }
    }

    private void DrawFlow(DialoguePreset working)
    {
        ImGui.Text($"実行フロー（{working.Flow.Count}）");
        ImGui.SetNextItemWidth(70);
        if (ImGui.InputFloat("秒##waitAdd", ref waitSeconds))
            waitSeconds = Math.Clamp(waitSeconds, 0.1f, 300f);
        ImGui.SameLine();
        if (ImGui.Button("Waitを追加"))
        {
            waitSeconds = Math.Clamp(waitSeconds, 0.1f, 300f);
            working.Flow.Add(FlowStep.CreateWait(waitSeconds));
        }

        ImGui.SameLine();
        if (ImGui.Button("NPCアクセスを追加"))
            working.Flow.Add(FlowStep.CreateInteract());

        ImGui.TextDisabled("Waitはその位置で待ちます。NPCアクセスは親NPCに話しかけるだけで、選択肢は押しません。");

        if (working.Flow.Count == 0)
        {
            ImGui.TextDisabled("まだありません。上で選択肢にチェックするか、Wait / NPCアクセスを追加してください。");
            return;
        }

        for (var i = 0; i < working.Flow.Count; i++)
        {
            var step = working.Flow[i];
            ImGui.PushID(i);
            ImGui.AlignTextToFramePadding();
            ImGui.Text($"{i + 1}.");
            ImGui.SameLine();
            if (step.IsWait)
            {
                ImGui.TextUnformatted("Wait");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(70);
                var seconds = step.WaitSeconds <= 0 ? 1f : step.WaitSeconds;
                if (ImGui.InputFloat("秒##waitStep", ref seconds))
                {
                    seconds = Math.Clamp(seconds, 0.1f, 300f);
                    step.WaitSeconds = seconds;
                    step.Text = $"{seconds:0.#}秒";
                }
            }
            else if (step.IsInteract)
            {
                ImGui.TextUnformatted("NPCアクセス");
            }
            else
            {
                ImGui.TextUnformatted($"{step.AddonName} → {step.Text}");
            }

            ImGui.SameLine();
            if (ImGui.SmallButton("上") && i > 0)
                (working.Flow[i - 1], working.Flow[i]) = (working.Flow[i], working.Flow[i - 1]);
            ImGui.SameLine();
            if (ImGui.SmallButton("下") && i < working.Flow.Count - 1)
                (working.Flow[i + 1], working.Flow[i]) = (working.Flow[i], working.Flow[i + 1]);
            ImGui.SameLine();
            if (ImGui.SmallButton("削除"))
            {
                working.Flow.RemoveAt(i);
                ImGui.PopID();
                break;
            }

            ImGui.PopID();
        }
    }

    private static bool SameStep(
        FlowStep step,
        DialogueInspector.LiveAddon list,
        ChoiceRow row,
        MarkKind kind)
    {
        if (step.Kind != kind || step.IsAction || !step.AddonName.Equals(list.Name, StringComparison.OrdinalIgnoreCase))
            return false;
        if (step.ListKey != list.ListKey())
            return false;
        return kind == MarkKind.ItemId ? step.ItemId == row.ItemId : step.Text == row.Text;
    }

    private static void ToggleFlow(
        DialoguePreset working,
        DialogueInspector.LiveAddon list,
        ChoiceRow row,
        MarkKind kind,
        bool enabled)
    {
        working.Flow.RemoveAll(s => SameStep(s, list, row, kind));
        if (!enabled)
            return;

        working.Flow.Add(new FlowStep
        {
            AddonName = list.Name,
            ListKey = list.ListKey(),
            Text = row.Text,
            ItemId = row.ItemId,
            Kind = kind,
        });
    }
}
