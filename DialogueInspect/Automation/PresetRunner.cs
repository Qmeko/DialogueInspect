using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using DialogueInspect.Inspection;
using DialogueInspect.Models;
using CSGameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace DialogueInspect.Automation;

internal sealed class PresetRunner
{
    private const float InteractRange = 6f;
    private static readonly TimeSpan ClickGap = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan InteractGap = TimeSpan.FromSeconds(1.2);
    private static readonly TimeSpan StallLimit = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan RepeatWait = TimeSpan.FromSeconds(1.5);

    private DialoguePreset? preset;
    private bool[] done = [];
    private int? awaitingIndex;
    private DateTime awaitingSinceUtc = DateTime.MinValue;
    private int pendingPartyCount = -1;
    private bool conversationOpen;
    private DateTime nextActionUtc = DateTime.MinValue;
    private DateTime lastProgressUtc = DateTime.MinValue;

    public bool IsRunning => preset != null;
    public string Status { get; private set; } = "停止中";
    public string? ActiveName => preset?.Name;

    public bool Start(DialoguePreset source)
    {
        if (source.Parent.BaseId == 0)
        {
            Plugin.Chat.PrintError("親NPCがありません。先にターゲットして「このNPCを親にする」を押してください。");
            return false;
        }

        if (source.Flow.Count == 0)
        {
            Plugin.Chat.PrintError("実行フローが空です。見えている選択窓を開いて、項目にチェックしてください。");
            return false;
        }

        StopInternal("開始");
        preset = source.Clone();
        done = new bool[preset.Flow.Count];
        awaitingIndex = null;
        conversationOpen = false;
        lastProgressUtc = DateTime.UtcNow;
        nextActionUtc = DateTime.UtcNow;
        Status = $"実行中: {preset.Name}";
        Plugin.Chat.Print($"DialogueInspect: 「{preset.Name}」を開始しました。NPCの近くにいてください。");
        return true;
    }

    public void Stop(string reason = "停止")
    {
        if (preset == null)
            return;
        Plugin.Chat.Print($"DialogueInspect: {reason}しました。");
        StopInternal(reason);
    }

    public void Tick()
    {
        if (preset == null)
            return;

        if (Plugin.Condition[ConditionFlag.BetweenAreas] ||
            Plugin.Condition[ConditionFlag.Unconscious])
        {
            lastProgressUtc = DateTime.UtcNow;
            Status = "移動中または戦闘不能。再開待ち";
            return;
        }

        if (Plugin.Condition[ConditionFlag.InCombat])
        {
            Stop("戦闘開始のため停止");
            return;
        }

        if (DateTime.UtcNow < nextActionUtc)
            return;

        if (DateTime.UtcNow - lastProgressUtc > StallLimit)
        {
            Stop("一定時間進まなかったため停止");
            return;
        }

        // 次のリストが来ているなら、会話送りより先にそれを選ぶ。
        if (TryFlowSteps())
            return;
        if (TryAdvanceTalk())
            return;

        if (FirstIncomplete() == null)
        {
            if (Plugin.Inspector.Live.Count > 0 || Plugin.Inspector.TalkVisible || IsBusyTalking())
            {
                lastProgressUtc = DateTime.UtcNow;
                Status = "フロー完了。会話が終わるのを待っています";
                return;
            }

            conversationOpen = false;
            if (preset.OnceOnly)
            {
                Stop("実行フローが終わったため停止");
                return;
            }

            done = new bool[preset.Flow.Count];
            awaitingIndex = null;
            nextActionUtc = DateTime.UtcNow + RepeatWait;
            Status = "再会話待ち";
            return;
        }

        var next = preset.Flow[FirstIncomplete()!.Value];
        if (conversationOpen || Plugin.Inspector.Live.Count > 0 || Plugin.Inspector.TalkVisible || IsBusyTalking())
        {
            if (Plugin.Inspector.Live.Count > 0 || Plugin.Inspector.TalkVisible || IsBusyTalking())
                lastProgressUtc = DateTime.UtcNow;
            Status = $"次の選択を待っています: 「{next.Text}」";
            return;
        }

        TryInteract();
    }

    private bool TryAdvanceTalk()
    {
        unsafe
        {
            var addon = Plugin.GameGui.GetAddonByName<AddonTalk>("Talk");
            if (!AddonReader.IsUsable((AtkUnitBase*)addon, true))
                return false;
            if (!AddonClick.FireInt((AtkUnitBase*)addon, 0))
                return false;
            MarkProgress("会話送り");
            conversationOpen = true;
            return true;
        }
    }

    private bool TryFlowSteps()
    {
        if (preset == null || done.Length != preset.Flow.Count)
            return false;

        var skipCurrent = false;
        if (awaitingIndex != null)
        {
            var waiting = awaitingIndex.Value;
            if (waiting < 0 || waiting >= preset.Flow.Count)
            {
                awaitingIndex = null;
            }
            else if (IsStepConfirmed(waiting))
            {
                CompleteStep(waiting, "確定");
                awaitingIndex = null;
            }
            else if (DateTime.UtcNow - awaitingSinceUtc > TimeSpan.FromMilliseconds(800))
            {
                CompleteStep(waiting, "選択");
                awaitingIndex = null;
            }
            else
            {
                Status = $"反映待ち: 「{preset.Flow[waiting].Text}」";
                return true;
            }
        }

        var first = FirstIncomplete();
        if (first == null)
            return false;

        if (preset.Flow[first.Value].IsWait)
            return RunWaitStep(first.Value);
        if (preset.Flow[first.Value].IsInteract)
            return RunInteractStep(first.Value);

        var firstResult = UiClickResult.Missed;
        if (!skipCurrent)
        {
            firstResult = TryClickStep(first.Value);
            if (firstResult == UiClickResult.Clicked)
                return true;
            if (firstResult == UiClickResult.Disabled)
            {
                lastProgressUtc = DateTime.UtcNow;
                Status = $"「{preset.Flow[first.Value].Text}」はまだ押せません。先に他の項目を処理します";
            }
        }

        for (var i = first.Value + 1; i < preset.Flow.Count; i++)
        {
            if (done[i])
                continue;
            if (preset.Flow[i].IsAction)
                break;
            if (!IsStepVisible(preset.Flow[i]))
                continue;
            var later = TryClickStep(i);
            if (later == UiClickResult.Clicked)
                return true;
        }

        return firstResult != UiClickResult.Missed;
    }

    private UiClickResult TryClickStep(int index)
    {
        if (preset == null || index < 0 || index >= preset.Flow.Count || done[index])
            return UiClickResult.Missed;
        if (preset.Flow[index].IsAction)
            return UiClickResult.Missed;

        var step = preset.Flow[index];
        if (AddonClick.IsChoiceSelected(step.AddonName, step.Text))
        {
            CompleteStep(index, "選択済");
            return UiClickResult.Clicked;
        }

        foreach (var live in Plugin.Inspector.Live)
        {
            if (!step.Matches(live.Name, live.Rows))
                continue;
            var rowIndex = step.FindIndex(live.Rows);
            if (rowIndex == null)
                continue;
            var row = live.Rows[rowIndex.Value];
            if (!ResourceAllows(step, row, out var reason))
            {
                Plugin.Chat.Print($"DialogueInspect: {reason}");
                Stop("所持数が下限のため停止");
                return UiClickResult.Clicked;
            }

            pendingPartyCount = AddonClick.ReadPartyCount(step.AddonName);
            var result = AddonClick.ClickChoice(step.AddonName, rowIndex.Value, row.Text);
            if (result == UiClickResult.Disabled)
                return result;
            if (result != UiClickResult.Clicked)
            {
                Status = $"クリックできませんでした: 「{row.Text}」";
                nextActionUtc = DateTime.UtcNow + ClickGap;
                return result;
            }

            conversationOpen = true;
            awaitingIndex = index;
            awaitingSinceUtc = DateTime.UtcNow;
            nextActionUtc = DateTime.UtcNow + ClickGap;
            Status = $"反映待ち: 「{row.Text}」";
            Plugin.Log.Information($"{CompletedCount() + 1}/{preset.Flow.Count} 「{row.Text}」をクリック。編成への反映を待ちます");
            return UiClickResult.Clicked;
        }

        return UiClickResult.Missed;
    }

    private bool RunWaitStep(int index)
    {
        if (preset == null)
            return false;

        var seconds = preset.Flow[index].WaitSeconds;
        if (seconds <= 0)
            seconds = 1f;
        seconds = Math.Clamp(seconds, 0.1f, 300f);
        CompleteStep(index, "待機");
        nextActionUtc = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
        lastProgressUtc = DateTime.UtcNow;
        Status = $"{seconds:0.#}秒待ち";
        return true;
    }

    private bool RunInteractStep(int index)
    {
        if (preset == null)
            return false;

        if (Plugin.Inspector.TalkVisible || IsBusyTalking())
        {
            CompleteStep(index, "アクセス");
            return true;
        }

        if (!PerformInteract(out var message))
        {
            Status = message;
            return true;
        }

        conversationOpen = true;
        CompleteStep(index, "アクセス");
        nextActionUtc = DateTime.UtcNow + InteractGap;
        return true;
    }

    private void CompleteStep(int index, string verb)
    {
        if (preset == null || index < 0 || index >= done.Length)
            return;
        done[index] = true;
        awaitingIndex = null;
        var finished = CompletedCount();
        MarkProgress($"{finished}/{preset.Flow.Count} 「{preset.Flow[index].Text}」を{verb}");
    }

    private int? FirstIncomplete()
    {
        if (preset == null)
            return null;
        for (var i = 0; i < done.Length; i++)
        {
            if (!done[i])
                return i;
        }

        return null;
    }

    private int CompletedCount()
    {
        var count = 0;
        foreach (var flag in done)
        {
            if (flag)
                count++;
        }

        return count;
    }

    private bool IsStepConfirmed(int index)
    {
        if (preset == null || index < 0 || index >= preset.Flow.Count)
            return false;
        var step = preset.Flow[index];
        if (IsStepGone(step))
            return true;
        if (NextDifferentAddonIsVisible(index))
            return true;
        var party = AddonClick.ReadPartyCount(step.AddonName);
        if (pendingPartyCount >= 0 && party > pendingPartyCount)
            return true;
        return AddonClick.IsChoiceSelected(step.AddonName, step.Text);
    }

    private bool ShouldWaitForUi(int index)
    {
        if (preset == null)
            return false;
        var current = preset.Flow[index];
        for (var i = index + 1; i < preset.Flow.Count; i++)
        {
            if (done[i] || preset.Flow[i].IsAction)
                continue;
            return !current.AddonName.Equals(preset.Flow[i].AddonName, StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    private bool NextDifferentAddonIsVisible(int index)
    {
        if (preset == null)
            return false;
        var currentAddon = preset.Flow[index].AddonName;
        for (var i = index + 1; i < preset.Flow.Count; i++)
        {
            if (done[i] || preset.Flow[i].IsAction)
                continue;
            if (currentAddon.Equals(preset.Flow[i].AddonName, StringComparison.OrdinalIgnoreCase))
                continue;
            return IsStepVisible(preset.Flow[i]);
        }

        return false;
    }

    private static bool IsStepVisible(FlowStep step)
        => Plugin.Inspector.Live.Any(live => step.Matches(live.Name, live.Rows));

    private static bool IsStepGone(FlowStep step)
        => !IsStepVisible(step);

    private bool ResourceAllows(FlowStep step, ChoiceRow row, out string reason)
    {
        reason = string.Empty;
        if (preset == null || !preset.Resource.Enabled || preset.Resource.ItemId == 0)
            return true;
        if (step.Kind != MarkKind.ItemId && row.ItemId == 0)
            return true;

        var held = Plugin.Resources.Count(preset.Resource.ItemId);
        if (held <= preset.Resource.MinRemaining)
        {
            reason = $"所持数が下限のためスキップ（今 {held} / 下限 {preset.Resource.MinRemaining}）";
            return false;
        }

        return true;
    }

    private void TryInteract()
    {
        if (!PerformInteract(out var message))
        {
            Status = message;
            return;
        }

        nextActionUtc = DateTime.UtcNow + InteractGap;
        lastProgressUtc = DateTime.UtcNow;
    }

    private bool PerformInteract(out string message)
    {
        message = string.Empty;
        var npc = FindNpc();
        if (npc == null)
        {
            message = "対象NPCが見つかりません。近づいてください。";
            return false;
        }

        var player = Plugin.Objects.LocalPlayer;
        if (player == null)
        {
            message = "自分のキャラクターが見つかりません。";
            return false;
        }

        var distance = Vector3.Distance(player.Position, npc.Position);
        if (distance > InteractRange)
        {
            message = $"NPCまで {distance:0.0}。もう少し近づいてください。";
            return false;
        }

        Plugin.Targets.Target = npc;
        unsafe
        {
            var system = TargetSystem.Instance();
            if (system == null)
            {
                message = "話しかけに失敗しました。";
                return false;
            }

            var native = (CSGameObject*)npc.Address;
            system->SetHardTarget(native);
            system->InteractWithObject(native, false);
        }

        message = $"「{npc.Name.TextValue}」に話しかけました";
        Status = message;
        return true;
    }

    private static bool IsBusyTalking()
        => Plugin.Condition[ConditionFlag.OccupiedInEvent]
           || Plugin.Condition[ConditionFlag.OccupiedInQuestEvent]
           || Plugin.Condition[ConditionFlag.OccupiedInCutSceneEvent]
           || Plugin.Condition[ConditionFlag.Occupied30]
           || Plugin.Condition[ConditionFlag.Occupied33]
           || Plugin.Condition[ConditionFlag.Occupied38]
           || Plugin.Condition[ConditionFlag.Occupied39];

    private IGameObject? FindNpc()
    {
        if (preset == null)
            return null;
        var player = Plugin.Objects.LocalPlayer;
        if (player == null)
            return null;

        IGameObject? best = null;
        var bestDist = float.MaxValue;
        foreach (var obj in Plugin.Objects)
        {
            if (obj.BaseId != preset.Parent.BaseId)
                continue;
            var dist = Vector3.Distance(player.Position, obj.Position);
            if (dist < bestDist)
            {
                best = obj;
                bestDist = dist;
            }
        }

        return best;
    }

    private void MarkProgress(string status)
    {
        Status = status;
        lastProgressUtc = DateTime.UtcNow;
        nextActionUtc = DateTime.UtcNow + ClickGap;
        Plugin.Log.Information(status);
    }

    private void StopInternal(string reason)
    {
        preset = null;
        done = [];
        awaitingIndex = null;
        conversationOpen = false;
        Status = reason == "開始" ? "実行中" : "停止中";
    }
}
