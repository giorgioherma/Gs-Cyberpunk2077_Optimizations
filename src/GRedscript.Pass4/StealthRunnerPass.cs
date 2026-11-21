using System.Text.RegularExpressions;

namespace GRedscript.Resolver;

/// <summary>
/// Historical Pass 4 NPC player handle: only a weak cached handle, initialized
/// in the existing NPCInitTask callback, reset on NPC detach and independently
/// re-acquired when the cache is missing. No event, detection or quest logic
/// is removed.
/// </summary>
internal static class StealthRunnerPass
{
    private const string Field="m_stealthRunnerPlayer";
    private static readonly Regex InitPlayer=new(
        @"\blet\s+player\s*:\s*wref<PlayerPuppet>\s*=\s*GameInstance\s*\.\s*GetPlayerSystem\s*\(\s*npc\s*\.\s*GetGame\s*\(\s*\)\s*\)\s*\.\s*GetLocalPlayerMainGameObject\s*\(\s*\)\s+as\s+PlayerPuppet\s*;");
    private static readonly Regex DetectOld=new(
        @"\blet\s+player\s*:\s*ref<PlayerPuppet>\s*=\s*GameInstance\s*\.\s*GetPlayerSystem\s*\(\s*this\s*\.\s*GetGame\s*\(\s*\)\s*\)\s*\.\s*GetLocalPlayerMainGameObject\s*\(\s*\)\s+as\s+PlayerPuppet\s*;");
    private static readonly Regex DetectCached=new(
        @"\blet\s+player\s*:\s*wref<PlayerPuppet>\s*=\s*this\s*\.\s*m_stealthRunnerPlayer\s*;");
    private static readonly Regex FieldDecl=new(
        @"@addField\s*\(\s*NPCPuppet\s*\)\s*private\s+let\s+m_stealthRunnerPlayer\s*:\s*wref<PlayerPuppet>\s*;");
    private static readonly Regex FieldAnchor=new(
        @"@addField\s*\(\s*NPCPuppet\s*\)\s*private\s+let\s+m_stealthRunnerStatus\s*:\s*ref<NPCPuppetStealthRunnerStatus>\s*;");

    internal static Pass4Rewrite Rewrite(string original)
    {
        if(!Regex.IsMatch(original,@"\bmodule\s+StealthRunner\b"))
            return Pass4Rewrite.Refuse("SR_MODULE_UNPROVEN");
        string source=original;
        int edits=0,done=0;
        var declaration=FieldDecl.Matches(source);
        var anchor=FieldAnchor.Matches(source);
        if(declaration.Count>1||anchor.Count!=1||
            (Regex.IsMatch(source,@"@addField\s*\(\s*NPCPuppet\s*\)[\s\S]{0,100}\bm_stealthRunnerPlayer\b")&&
             declaration.Count==0))
            return Pass4Rewrite.Refuse("SR_NPC_FIELD_CONFLICT");
        if(declaration.Count==0) {
            var m=anchor[0];
            string nl=Pass4Source.NewLine(source);
            source=source.Insert(m.Index+m.Length,nl+nl+
                "@addField(NPCPuppet)"+nl+
                "private let m_stealthRunnerPlayer: wref<PlayerPuppet>;");
            edits++;
        } else done++;

        // Process original callbacks individually, using the already validated
        // SourceAdmission lexer to locate the exact method bodies.
        var activate=Pass4Source.FindMethod(source,"OnActivate");
        var detach=Pass4Source.FindMethod(source,"OnDetach");
        var detection=Pass4Source.FindMethod(source,"OnPlayerDetectionChangedEvent");
        if(activate is null||detach is null||detection is null||
            !Pass4Source.HasDirective(source,activate,"@wrapMethod(NPCInitTask)")||
            !Pass4Source.HasDirective(source,detach,"@wrapMethod(NPCPuppet)")||
            !Pass4Source.HasDirective(source,detection,"@wrapMethod(NPCPuppet)"))
            return Pass4Rewrite.Refuse("SR_ORIGINAL_CALLBACK_OWNERSHIP_UNPROVEN");

        string activBody=Pass4Source.Body(source,activate);
        if(!activBody.Contains("wrappedMethod(context)",StringComparison.Ordinal)||
           !activBody.Contains("StealthRunnerQuestEvent.NPCInitEvent",StringComparison.Ordinal)||
           InitPlayer.Matches(activBody).Count!=1)
            return Pass4Rewrite.Refuse("SR_INITIALIZATION_OR_QUEST_EVENT_UNPROVEN");
        if(Regex.IsMatch(activBody,@"\bnpc\s*\.\s*m_stealthRunnerPlayer\s*=\s*player\s*;")) done++;
        else {
            var match=InitPlayer.Match(activBody);
            activBody=activBody.Insert(match.Index+match.Length,
                Pass4Source.NewLine(source)+"    npc.m_stealthRunnerPlayer = player;");
            source=Pass4Source.ReplaceBody(source,activate,activBody);
            edits++;
        }
        // Locate again because earlier edits affect byte offsets.
        detach=Pass4Source.FindMethod(source,"OnDetach")!;
        string detachBody=Pass4Source.Body(source,detach);
        if(!detachBody.Contains("wrappedMethod()",StringComparison.Ordinal)||
           !detachBody.Contains("StealthRunnerQuestEvent.UnregisterEnemyEvent",StringComparison.Ordinal)||
           !Regex.IsMatch(detachBody,@"\bthis\s*\.\s*m_stealthRunnerStatus\s*=\s*null\s*;"))
            return Pass4Rewrite.Refuse("SR_DETACH_LIFECYCLE_UNPROVEN");
        if(Regex.IsMatch(detachBody,@"\bthis\s*\.\s*m_stealthRunnerPlayer\s*=\s*null\s*;")) done++;
        else {
            detachBody=Pass4Source.NewLine(source)+
                "  this.m_stealthRunnerPlayer = null;"+detachBody;
            source=Pass4Source.ReplaceBody(source,detach,detachBody);
            edits++;
        }
        detection=Pass4Source.FindMethod(source,"OnPlayerDetectionChangedEvent")!;
        string detectBody=Pass4Source.Body(source,detection);
        if(!detectBody.Contains("wrappedMethod(evt)",StringComparison.Ordinal)||
            !detectBody.Contains("currentHighLevelState",StringComparison.Ordinal))
            return Pass4Rewrite.Refuse("SR_DETECTION_CALLBACK_UNPROVEN");
        bool old=DetectOld.IsMatch(detectBody), cached=DetectCached.IsMatch(detectBody);
        if(old==cached||DetectOld.Matches(detectBody).Count>1||
            DetectCached.Matches(detectBody).Count>1)
            return Pass4Rewrite.Refuse("SR_DETECTION_PLAYER_SOURCE_AMBIGUOUS");
        if(old) {
            var m=DetectOld.Match(detectBody);
            detectBody=detectBody[..m.Index]+
                "let player: wref<PlayerPuppet> = this.m_stealthRunnerPlayer;"+
                detectBody[(m.Index+m.Length)..];
            edits++;
        } else done++;
        if(!Regex.IsMatch(detectBody,
            @"\bplayer\s*=\s*GameInstance\s*\.\s*GetPlayerSystem\s*\(\s*this\s*\.\s*GetGame\s*\(\s*\)\s*\)\s*\.\s*GetLocalPlayerMainGameObject\s*\(\s*\)\s+as\s+PlayerPuppet\s*;")) {
            var m=DetectCached.Match(detectBody);
            if(!m.Success) return Pass4Rewrite.Refuse("SR_CACHED_LOCAL_MISSING");
            string nl=Pass4Source.NewLine(source);
            string fallback=nl+"  if !IsDefined(player) {"+nl+
                "    player = GameInstance.GetPlayerSystem(this.GetGame()).GetLocalPlayerMainGameObject() as PlayerPuppet;"+nl+
                "    this.m_stealthRunnerPlayer = player;"+nl+"  }";
            detectBody=detectBody.Insert(m.Index+m.Length,fallback);
            edits++;
        } else if(!Regex.IsMatch(detectBody,
            @"\bthis\s*\.\s*m_stealthRunnerPlayer\s*=\s*player\s*;"))
            return Pass4Rewrite.Refuse("SR_PLAYER_FALLBACK_WITHOUT_CACHE_UPDATE");
        else done++;
        source=Pass4Source.ReplaceBody(source,detection,detectBody);
        if(!FieldDecl.IsMatch(source)||
           !source.Contains("npc.m_stealthRunnerPlayer = player;",StringComparison.Ordinal)||
           !source.Contains("this.m_stealthRunnerPlayer = null;",StringComparison.Ordinal)||
           !source.Contains("this.m_stealthRunnerPlayer = player;",StringComparison.Ordinal))
            throw new InvalidDataException("SR_POSTCONDITION_MISSING");
        return edits==0?Pass4Rewrite.Complete(done):
            Pass4Rewrite.Applied(source,edits,done);
    }
}
