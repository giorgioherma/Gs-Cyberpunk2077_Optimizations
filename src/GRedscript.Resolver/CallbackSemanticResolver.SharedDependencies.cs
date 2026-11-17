namespace GRedscript.Resolver;

/// <summary>
/// R5 read-only direct-operand dependency evidence. Called by the existing
/// SharedWork scan on the one authoritative SourceAdmission token stream and
/// CallbackSemanticResolver method spans. This vocabulary identifies source
/// hints, NEVER a complete dependency graph, event producer, safe skip or
/// generated optimization.
/// </summary>
internal static partial class CallbackSemanticResolver
{
    private sealed record DirectDependencyProfile(
        string[] InstanceFields, string[] InstanceMethodCalls,
        string[] UsedCallbackParameters, string[] Categories,
        string[] CandidateTriggers, string PrimaryCandidateTrigger);

    // Generic REDscript/engine lexemes, not an allowlist of third-party mods.
    // These are intentionally *hints*: "GetInt" does not itself prove a
    // blackboard receiver; "SetData" does not prove an ink widget receiver.
    // No optimization may be admitted based solely on these strings.
    private static readonly IReadOnlyDictionary<string, string[]> DirectDependencyLexemes =
        new SortedDictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["CLOCK_TIME"] = [
                "deltaTime", "GetGameTime", "GetSimTime", "GetTime",
                "GetTimeDilation", "GetSystemTime", "GetTimeRemaining",
                "timeLeft", "timeRemaining", "dt", "tickCount", "m_duration",
                "GetFrameTime", "GetPausedTime", "GetTotalTime", "GameTime",
                "GetDay", "GetHour", "GetTimestamp", "GetDuration",
                "GetRemainingTime", "GetTickCount", "GetCooldown",
                "GetCooldownRemaining"
            ],
            ["PLAYER_IDENTITY"] = [
                "GetPlayer", "GetLocalPlayerMainGameObject",
                "GetPlayerControlledObject", "GetAttachedPlayer",
                "HasAuthoritativePlayer", "GetPlayerSignalGeneration",
                "GetPlayerSystem", "GetLocalPlayer", "GetPlayerPuppet",
                "PlayerPuppet"
            ],
            ["PLAYER_DYNAMIC"] = [
                "IsInCombat", "IsInCombatWithTarget", "GetMountedVehicle",
                "GetVehicle", "GetMountedObject", "IsMounted",
                "GetBlackboard", "GetPlayerState", "GetSceneTier",
                "GetLocomotionState", "GetStamina", "GetHealth",
                "GetStatValue", "GetStat", "GetStatPoolValue",
                "GetStatusEffect", "HasStatusEffect", "IsPlayerInCombat",
                "GetCurrentWeapon", "GetActiveWeapon"
            ],
            ["WORLD_SPATIAL"] = [
                "GetWorldPosition", "GetWorldTransform", "GetPosition",
                "GetDistance", "Vector4", "Distance", "DistanceSquared",
                "GetDistanceTo", "IsInRange", "IsNear", "GetNearby",
                "GetNearbyNPCs", "IsPointIn", "GetAngle", "GetHeading",
                "GetForward", "GetCameraWorldPosition", "GetPlayerPosition",
                "GetLookAtObject"
            ],
            ["QUEST_FACT_JOURNAL"] = [
                "GetQuestsSystem", "GetFact", "GetFactStr", "GetFactValue",
                "GetJournalManager", "GetJournal", "GetQuestSystem",
                "GetQuest", "GetEntryByString", "GetEntry",
                "SetFact", "SetFactStr"
            ],
            ["INVENTORY_EQUIPMENT_STATS"] = [
                "GetTransactionSystem", "GetInventory", "GetItem",
                "GetItemData", "GetItemQuantity", "GetEquipmentSystem",
                "GetStatsSystem", "GetStatPoolsSystem", "GetStatPool",
                "GetStatValue", "GetStat", "HasItem", "GetEquippedItem",
                "GetEquippedItemID", "GetItemID", "GetItemCount"
            ],
            ["BLACKBOARD_ACCESS"] = [
                "GetAllBlackboardDefs", "GetBlackboardSystem",
                "GetBlackboard", "GetInt", "GetBool", "GetFloat",
                "GetVariant"
            ],
            ["UI_WIDGET"] = [
                "SetData", "SetText", "SetVisible", "SetOpacity",
                "SetSize", "SetTranslation", "SetAnchor",
                "SetEffectParamValue", "SetTimeRemaining", "SetStackCount",
                "Reparent", "RemoveChild", "SpawnFromLocal",
                "GetController", "GetWidget", "GetRootWidget",
                "SetState", "GetState", "inkWidget", "inkText",
                "inkImage", "inkCompoundWidget", "SetTintColor",
                "SetRotation", "PlayAnimation", "StopAnimation",
                "GetAnimation", "SetMargin", "GetChildren",
                "SetTexturePart"
            ],
            ["INPUT_DEVICE"] = [
                "OnAction", "IsAction", "GetName", "GetType",
                "RegisterInputListener", "UnregisterInputListener",
                "InputHub", "ListenerAction", "GetLastUsedKBM",
                "SetLastUsedKBM", "GetInput", "GetButtonState"
            ],
            ["EVENT_LIFECYCLE"] = [
                "QueueEvent", "SendEvent", "RegisterListener",
                "UnregisterListener", "RegisterInputListener",
                "UnregisterInputListener", "DelayCallback",
                "CancelCallback", "OnAttach", "OnDetach",
                "OnPlayerAttach", "OnPlayerDetach", "OnGameAttached",
                "OnGameDetached", "GetDelaySystem", "RegisterCallback",
                "UnregisterCallback"
            ],
            ["RESOURCE_ASSET"] = [
                "LoadResource", "GetResource", "IsResourceReady",
                "GetResourcePath", "ResourceRef", "ResourceToken",
                "GetPath", "LoadTexture", "GetAtlas",
                "GetTweakDBID", "TweakDBInterface", "GetRecord"
            ],
            ["CONFIG_SETTINGS"] = [
                "GetSettings", "GetConfig", "GetOption", "GetOptions",
                "GetSetting", "GetSettingValue", "Settings",
                "GetTweakDBID", "GetRecord", "GetProperty"
            ],
            ["NETWORK_EXTERNAL"] = [
                "HTTP", "JSON", "ModStorage", "ReadFile", "WriteFile",
                "GetStorage", "Storage", "RestApi", "Json",
                "Reflection", "GetClasses", "GetFunction",
                "CallLua", "CallNative", "ExecuteFunction"
            ],
            ["COLLECTION"] = [
                "ArraySize", "ArrayPush", "ArrayErase", "ArrayClear",
                "ArrayInsert", "ArrayRemove", "ArrayResize", "ArrayPop",
                "ArraySort", "ArrayContains", "ArrayFindFirst",
                "ArrayFindLast", "ArrayAppend", "ArrayRemoveByValue",
                "for", "while"
            ],
        };

    private static DirectDependencyProfile InspectDirectDependencies(
        MethodSource method, IReadOnlyList<SourceAdmission.Token> body)
    {
        var tokens = method.File.Tokens;
        var bodyNames = body.Select(t => t.Value)
            .ToHashSet(StringComparer.Ordinal);
        var fields = new SortedSet<string>(StringComparer.Ordinal);
        var calls = new SortedSet<string>(StringComparer.Ordinal);
        var parameters = new SortedSet<string>(StringComparer.Ordinal);
        for (int i = 0; i + 2 < body.Count; i++)
        {
            if (body[i].Value != "this" || body[i + 1].Value != "." ||
                !Ident(body[i + 2].Value))
                continue;
            if (i + 3 < body.Count && body[i + 3].Value == "(")
                calls.Add(body[i + 2].Value);
            else
                fields.Add(body[i + 2].Value);
        }
        for (int i = method.SignatureOpen + 2; i < method.SignatureClose; i++)
        {
            if (tokens[i].Value == ":" && Ident(tokens[i - 1].Value) &&
                bodyNames.Contains(tokens[i - 1].Value))
                parameters.Add(tokens[i - 1].Value);
        }

        var categories = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var entry in DirectDependencyLexemes)
            if (entry.Value.Any(bodyNames.Contains))
                categories.Add(entry.Key);
        if (fields.Count > 0) categories.Add("INSTANCE_FIELDS");
        if (calls.Count > 0) categories.Add("INSTANCE_METHOD_CALLS");
        if (parameters.Count > 0) categories.Add("CALLBACK_ARGUMENTS");

        // Every trigger below is only a *research candidate*. E.g. reading
        // player identity cannot make a moving player's combat state or
        // position immutable. No subscriptions are created by this scan.
        var triggers = new SortedSet<string>(StringComparer.Ordinal);
        void Add(string category, string trigger)
        {
            if (categories.Contains(category)) triggers.Add(trigger);
        }
        Add("CLOCK_TIME", "TIMER_ANIMATION_FRAME");
        if (bodyNames.Overlaps(new[] {
                "PlayAnimation", "SetTimeRemaining", "SetEffectParamValue" }))
            triggers.Add("TIMER_ANIMATION_FRAME");
        Add("EVENT_LIFECYCLE", "EVENT_DELIVERY_SIDE_EFFECT");
        Add("INPUT_DEVICE", "INPUT_ACTION_NEEDS_CONSUMER_PROOF");
        Add("WORLD_SPATIAL", "POSITION_NEARFAR_NEEDS_EXACT_PROOF");
        Add("QUEST_FACT_JOURNAL", "QUEST_FACT_NEEDS_PUBLISHER");
        Add("INVENTORY_EQUIPMENT_STATS", "INVENTORY_STATS_NEEDS_PUBLISHER");
        if (categories.Contains("PLAYER_DYNAMIC") ||
            categories.Contains("BLACKBOARD_ACCESS"))
            triggers.Add("PLAYER_BLACKBOARD_NEEDS_LIVE_PUBLISHER");
        Add("UI_WIDGET", "UI_WIDGET_DIRTY_LIFETIME_NEEDS_PROOF");
        Add("CONFIG_SETTINGS", "CONFIG_CHANGE_NEEDS_PUBLISHER");
        Add("RESOURCE_ASSET", "RESOURCE_READY_NEEDS_PUBLISHER");
        Add("PLAYER_IDENTITY", "PLAYER_ATTACH_DETACH_IDENTITY_ONLY");
        Add("CALLBACK_ARGUMENTS", "CALLBACK_ARGUMENT_FRESHNESS");
        Add("INSTANCE_FIELDS", "OWNER_FIELD_WRITERS_NOT_CLOSED");
        Add("INSTANCE_METHOD_CALLS", "TRANSITIVE_METHOD_EFFECTS_NOT_RESOLVED");
        Add("NETWORK_EXTERNAL", "EXTERNAL_STATE_OR_REFLECTION_UNKNOWN");
        if (triggers.Count == 0)
            triggers.Add("LOCAL_OR_UNRESOLVED_DEPENDENCIES");

        string[] priority = [
            "TIMER_ANIMATION_FRAME", "EVENT_DELIVERY_SIDE_EFFECT",
            "INPUT_ACTION_NEEDS_CONSUMER_PROOF",
            "POSITION_NEARFAR_NEEDS_EXACT_PROOF",
            "QUEST_FACT_NEEDS_PUBLISHER",
            "INVENTORY_STATS_NEEDS_PUBLISHER",
            "PLAYER_BLACKBOARD_NEEDS_LIVE_PUBLISHER",
            "UI_WIDGET_DIRTY_LIFETIME_NEEDS_PROOF",
            "CONFIG_CHANGE_NEEDS_PUBLISHER",
            "RESOURCE_READY_NEEDS_PUBLISHER",
            "PLAYER_ATTACH_DETACH_IDENTITY_ONLY",
            "CALLBACK_ARGUMENT_FRESHNESS",
            "OWNER_FIELD_WRITERS_NOT_CLOSED",
            "TRANSITIVE_METHOD_EFFECTS_NOT_RESOLVED",
            "EXTERNAL_STATE_OR_REFLECTION_UNKNOWN",
            "LOCAL_OR_UNRESOLVED_DEPENDENCIES"
        ];
        string primary = priority.First(triggers.Contains);
        return new(fields.ToArray(), calls.ToArray(), parameters.ToArray(),
            categories.ToArray(), triggers.ToArray(), primary);
    }
}
