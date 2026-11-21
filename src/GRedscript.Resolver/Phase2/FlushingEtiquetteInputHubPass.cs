using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GRedscript.Resolver.Phase2;

/// <summary>
/// Historical Pass 2.0 FE input: one atomic InputHub subscription set with an
/// exclusive engine registration fallback. Preserve FEInput's generic early
/// name gate and factor the original gameplay body into one shared handler.
/// Never change action consumption, modifier transitions or status hotkey
/// compilation guards. Missing runtime ABI refuses only this operation.
/// </summary>
internal static class FlushingEtiquetteInputHubPass
{
    internal const string Rule = "FLUSHING_ETIQUETTE_INPUT_HUB_FALLBACK_V1";
    internal sealed record Result(string RuleId, string Status, int AppliedEdits,
        int AlreadyComplete, string? SourcePath, string? Reason,
        bool SourceUnchanged, bool GameplayValidated);
    private sealed record Method(int Start, int Open, int Close, string Signature);
    private sealed record Edit(int Start, int End, string Value);
    private sealed record Decision(string Status, string? Reason, string? Source, int Edits);
    private static string Nl(string src) => src.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    private static string Normalize(string value) =>
        string.Join("\u001f", SourceAdmission.ReadTokens(value).Select(t => t.Value));

    private static int Close(IReadOnlyList<SourceAdmission.Token> t,
        int start, string begin, string end)
    {
        if (start < 0 || start >= t.Count || t[start].Value != begin) return -1;
        int depth = 0;
        for (int i = start; i < t.Count; i++)
        {
            if (t[i].Value == begin) depth++;
            else if (t[i].Value == end && --depth == 0) return i;
        }
        return -1;
    }
    private static Method? Find(string src, string name, string argWitness,
        string? annotation = null)
    {
        var t = SourceAdmission.ReadTokens(src);
        Method? found = null;
        for (int i = 0; i + 2 < t.Count; i++)
        {
            if (t[i].Value != "func" || t[i + 1].Value != name ||
                t[i + 2].Value != "(") continue;
            int endArg = Close(t, i + 2, "(", ")");
            if (endArg < 0) return null;
            string args = string.Join(" ", t.Skip(i + 3).Take(endArg - i - 3)
                .Select(x => x.Value));
            if (!args.Contains(argWitness, StringComparison.Ordinal)) continue;
            string prior = src[Math.Max(0, t[i].Start - 240)..t[i].Start];
            if (annotation is not null && !Regex.IsMatch(prior,
                @"@wrapMethod\s*\(\s*" + Regex.Escape(annotation) +
                @"\s*\)\s*(?:(?:public|protected|private|cb|final)\s+)*$"))
                continue;
            int open = endArg + 1;
            while (open < t.Count && t[open].Value is not ("{" or ";")) open++;
            if (open == t.Count || t[open].Value != "{") return null;
            int close = Close(t, open, "{", "}");
            if (close < 0 || found is not null) return null;
            found = new(t[i].Start,t[open].Start,t[close].Start,args);
            i = close;
        }
        return found;
    }
    private static bool Present(string src, string pattern) =>
        Regex.IsMatch(src, pattern, RegexOptions.CultureInvariant);

    // Add exactly the owner's original four input categories on fallback;
    // the optional status action remains controlled by the existing
    // compile-gated FERegisterStatusHotkey() owner method.
    private static string Attach(string nl) => string.Join(nl, new string[]
    {
        "  this.FECleanupInputRoutes();",
        "  if !this.FETryHubInputRoutes() {",
        "    this.RegisterInputListener(this, n\"Flush_Relief\");",
        "    this.RegisterInputListener(this, n\"Flush_VehicleRelief\");",
        "    this.RegisterInputListener(this, n\"Flush_WashHands\");",
        "    let mods = FEInput.ModifierActions();",
        "    let i = 0;",
        "    while i < ArraySize(mods) {",
        "      this.RegisterInputListener(this, mods[i]);",
        "      i += 1;",
        "    }",
        "    this.FERegisterStatusHotkey();",
        "    this.m_feFallbackInputRegistered = true;",
        "  }"
    });

    private static string Helpers(string nl) => string.Join(nl, new string[]
    {
        "@addMethod(PlayerPuppet)",
        "@if(ModuleExists(\"StatusEffectsHotkey\"))",
        "private func FEHubStatusHotkey() -> CName { return n\"ShowStatusEffects\"; }",
        "",
        "@addMethod(PlayerPuppet)",
        "@if(!ModuleExists(\"StatusEffectsHotkey\"))",
        "private func FEHubStatusHotkey() -> CName { return n\"\"; }",
        "",
        "@addMethod(PlayerPuppet)",
        "private func FETryHubInputRoutes() -> Bool {",
        "  let feRuntime = Runtime.Get(this.GetGame());",
        "  if !IsDefined(feRuntime) || !feRuntime.IsReady() { return false; }",
        "  let feHub = feRuntime.GetInputHub();",
        "  if !IsDefined(feHub) { return false; }",
        "  let feListener = new FEGRedInputListener();",
        "  feListener.Init(this);",
        "  this.m_feGRedInputHub = feHub;",
        "  this.m_feGRedInputListener = feListener;",
        "  let feNames: array<CName> = [n\"Flush_Relief\", n\"Flush_VehicleRelief\", n\"Flush_WashHands\"];",
        "  let feMods = FEInput.ModifierActions();",
        "  let feIndex: Int32 = 0;",
        "  while feIndex < ArraySize(feMods) {",
        "    ArrayPush(feNames, feMods[feIndex]);",
        "    feIndex += 1;",
        "  }",
        "  let feStatus = this.FEHubStatusHotkey();",
        "  if NotEquals(feStatus, n\"\") { ArrayPush(feNames, feStatus); }",
        "  feIndex = 0;",
        "  while feIndex < ArraySize(feNames) {",
        "    let feSubscription = feHub.Subscribe(feNames[feIndex], feListener);",
        "    if feSubscription <= 0 {",
        "      this.FECleanupInputRoutes();",
        "      return false;",
        "    }",
        "    ArrayPush(this.m_feGRedInputSubscriptions, feSubscription);",
        "    feIndex += 1;",
        "  }",
        "  this.m_feHubInputActive = true;",
        "  return true;",
        "}",
        "",
        "@addMethod(PlayerPuppet)",
        "private func FECleanupInputRoutes() -> Void {",
        "  if IsDefined(this.m_feGRedInputHub) {",
        "    let feIndex: Int32 = 0;",
        "    while feIndex < ArraySize(this.m_feGRedInputSubscriptions) {",
        "      this.m_feGRedInputHub.Unsubscribe(this.m_feGRedInputSubscriptions[feIndex]);",
        "      feIndex += 1;",
        "    }",
        "  }",
        "  ArrayClear(this.m_feGRedInputSubscriptions);",
        "  this.m_feHubInputActive = false;",
        "  this.m_feGRedInputListener = null;",
        "  this.m_feGRedInputHub = null;",
        "  if this.m_feFallbackInputRegistered {",
        "    this.UnregisterInputListener(this, n\"Flush_Relief\");",
        "    this.UnregisterInputListener(this, n\"Flush_VehicleRelief\");",
        "    this.UnregisterInputListener(this, n\"Flush_WashHands\");",
        "    let feMods = FEInput.ModifierActions();",
        "    let feIndex: Int32 = 0;",
        "    while feIndex < ArraySize(feMods) {",
        "      this.UnregisterInputListener(this, feMods[feIndex]);",
        "      feIndex += 1;",
        "    }",
        "    let feStatus = this.FEHubStatusHotkey();",
        "    if NotEquals(feStatus, n\"\") { this.UnregisterInputListener(this, feStatus); }",
        "  }",
        "  this.m_feFallbackInputRegistered = false;",
        "}",
        "",
        "public class FEGRedInputListener extends InputListener {",
        "  private let m_player: wref<PlayerPuppet>;",
        "  public func Init(player: wref<PlayerPuppet>) -> Void { this.m_player = player; }",
        "  public func OnGRedInput(evt: ref<InputEvent>) -> Void {",
        "    if !IsDefined(this.m_player) || !IsDefined(evt) { return; }",
        "    if FEInput.HandleValues(this.m_player, evt.actionName, evt.actionType) {",
        "      evt.consumed = true;",
        "    }",
        "  }",
        "}",
        ""
    }) + nl;

    private static bool HasRuntimeAbi(string scripts)
    {
        // Check actual declared GRedRuntime APIs; never an exact folder,
        // framework version or historical file location.
        bool runtime = false, input = false;
        foreach (string f in Directory.EnumerateFiles(scripts,"*.reds",SearchOption.AllDirectories))
        {
            var v = File.ReadAllText(f,Encoding.UTF8);
            if (!Present(v,@"\bmodule\s+GRedRuntime\b")) continue;
            if (Present(v,@"\bclass\s+Runtime\b") &&
                Present(v,@"\bfunc\s+IsReady\s*\(") &&
                Present(v,@"\bfunc\s+GetInputHub\s*\(") &&
                Present(v,@"\bfunc\s+Get\s*\(\s*game\s*:\s*GameInstance")) runtime=true;
            if (Present(v,@"\bclass\s+InputHub\b") &&
                Present(v,@"\bfunc\s+Subscribe\s*\(") &&
                Present(v,@"\bfunc\s+Unsubscribe\s*\(") &&
                Present(v,@"\bclass\s+InputListener\b") &&
                Present(v,@"\bclass\s+InputEvent\b")) input=true;
            if(runtime&&input)return true;
        }
        return false;
    }

    private static Decision Rewrite(string original)
    {
        string src = original, nl=Nl(src);
        if (!Present(src,@"\bmodule\s+FlushingEtiquette\b") ||
            !Present(src,@"\bclass\s+FEInput\b") ||
            !Present(src,@"\bclass\s+FEModifierState\b"))
            return new("REFUSED","FE_HUB_MODULE_OR_STATE_MISSING",null,0);
        var attach=Find(src,"OnGameAttached","", "PlayerPuppet");
        var action=Find(src,"OnAction","action : ListenerAction", "PlayerPuppet");
        var handle=Find(src,"Handle","action : ListenerAction");
        var values=Find(src,"HandleValues","name : CName");
        if(attach is null || action is null || (handle is null && values is null))
            return new("REFUSED","FE_HUB_REQUIRED_OWNER_METHOD_MISSING",null,0);
        string aBody=src[(attach.Open+1)..attach.Close];
        string onBody=src[(action.Open+1)..action.Close];
        if(!Present(aBody,@"\bwrappedMethod\s*\(") ||
           !Present(aBody,@"\breturn\s+result\s*;") ||
           !Present(onBody,@"\bwrappedMethod\s*\(\s*action\s*,\s*consumer\s*\)") ||
           !Present(onBody,@"\bFEInput\s*\.\s*Handle\s*\(\s*this\s*,\s*action\s*\)"))
            return new("REFUSED","FE_HUB_CALLBACK_ORDER_OR_FALLBACK_UNPROVEN",null,0);
        var changes=new List<Edit>();
        void Add(int a,int b,string v){changes.Add(new(a,b,v));}
        // The original legacy engine registrations are one contiguous method
        // slice. Preserve the enclosing wrappedMethod and return unchanged.
        if(!Present(aBody,@"\bFETryHubInputRoutes\s*\("))
        {
            var start=Regex.Match(aBody,
                @"this\s*\.\s*RegisterInputListener\s*\(\s*this\s*,\s*n\""Flush_Relief\""\s*\)\s*;");
            var end=Regex.Match(aBody,@"\bthis\s*\.\s*FERegisterStatusHotkey\s*\(\s*\)\s*;");
            if(!start.Success||!end.Success||end.Index<=start.Index||
               !aBody[start.Index..(end.Index+end.Length)].Contains("ModifierActions",StringComparison.Ordinal))
                return new("REFUSED","FE_HUB_ATTACH_REGISTRATION_SHAPE_UNPROVEN",null,0);
            string slice=aBody[start.Index..(end.Index+end.Length)];
            if(!Present(slice,@"RegisterInputListener\s*\(\s*this\s*,\s*n\""Flush_VehicleRelief\""") ||
               !Present(slice,@"RegisterInputListener\s*\(\s*this\s*,\s*n\""Flush_WashHands\""") ||
               !Present(slice,@"while\s+i\s*<\s*ArraySize\s*\(\s*mods\s*\)"))
                return new("REFUSED","FE_HUB_INPUT_ACTION_COVERAGE_UNPROVEN",null,0);
            Add(attach.Open+1+start.Index,attach.Open+1+end.Index+end.Length,
                Attach(nl));
        }
        else if(!Present(aBody,@"\bFECleanupInputRoutes\s*\(") ||
                !Present(aBody,@"\bm_feFallbackInputRegistered\s*=\s*true"))
            return new("REFUSED","FE_HUB_PARTIAL_ATTACH_UNPROVEN",null,0);
        if(!Present(onBody,@"\bm_feHubInputActive\b"))
        {
            var call=Regex.Match(onBody,
                @"\bif\s+FEInput\s*\.\s*Handle\s*\(\s*this\s*,\s*action\s*\)\s*\{\s*return\s+true\s*;\s*\}");
            if(!call.Success)return new("REFUSED","FE_HUB_ENGINE_WRAPPER_SHAPE_UNPROVEN",null,0);
            Add(action.Open+1+call.Index,action.Open+1+call.Index+call.Length,
                "if !this.m_feHubInputActive && FEInput.Handle(this, action) { return true; }");
        }
        else if(!Present(onBody,@"!\s*this\s*\.\s*m_feHubInputActive\s*&&\s*FEInput\s*\.\s*Handle"))
            return new("REFUSED","FE_HUB_DOUBLE_DISPATCH_RISK",null,0);

        if(handle is not null && values is null)
        {
            string hb=src[(handle.Open+1)..handle.Close];
            // Preserve the already-generated early unrelated-action gate
            // byte-for-byte in this ListenerAction adapter.
            var name=Regex.Match(hb,
                @"\blet\s+name\s*=\s*ListenerAction\s*\.\s*GetName\s*\(\s*action\s*\)\s*;");
            var typ=Regex.Match(hb,
                @"\blet\s+type\s*=\s*ListenerAction\s*\.\s*GetType\s*\(\s*action\s*\)\s*;");
            var gi=Regex.Match(hb,
                @"\blet\s+gi\s*=\s*player\s*\.\s*GetGame\s*\(\s*\)\s*;");
            if(!name.Success||!typ.Success||!gi.Success||name.Index>=typ.Index||typ.Index>=gi.Index)
                return new("REFUSED","FE_HUB_HANDLE_ACCESSOR_FLOW_UNPROVEN",null,0);
            string between=hb[(name.Index+name.Length)..typ.Index];
            if(!Present(between,@"\bif\b") || !between.Contains("return false",StringComparison.Ordinal) ||
               !between.Contains("Equals",StringComparison.Ordinal))
                return new("REFUSED","FE_HUB_GENERIC_NAME_GATE_MISSING",null,0);
            // The base FE gameplay branch starts at the GetGame statement.
            // Moving this complete source slice keeps later mod/custom code.
            string gameplay=hb[(typ.Index+typ.Length)..];
            string adapter=hb[..(typ.Index+typ.Length)] + nl +
                "    return FEInput.HandleValues(player, name, type);"+nl+"  ";
            string newMethod="public static func HandleValues(player: ref<PlayerPuppet>, "+
                "name: CName, type: gameinputActionType) -> Bool {"+nl+
                gameplay.TrimEnd()+nl+"  }";
            Add(handle.Open+1,handle.Close,adapter);
            Add(handle.Close+1,handle.Close+1,nl+nl+"  "+newMethod+nl);
        }
        else if(handle is null || values is null ||
                !Present(src[(handle.Open+1)..handle.Close],
                    @"\bFEInput\s*\.\s*HandleValues\s*\(\s*player\s*,\s*name\s*,\s*type\s*\)"))
            return new("REFUSED","FE_HUB_PARTIAL_HANDLE_FLOW_UNPROVEN",null,0);

        if(!Present(src,@"\bimport\s+GRedRuntime\s*\.\s*\*"))
        {
            var m=Regex.Match(src,@"\bmodule\s+FlushingEtiquette\s*(?:\r?\n)");
            if(!m.Success)return new("REFUSED","FE_HUB_IMPORT_ANCHOR_UNPROVEN",null,0);
            Add(m.Index+m.Length,m.Index+m.Length,"import GRedRuntime.*"+nl);
        }
        string[] fields =
        [
            "m_feGRedInputListener: ref<FEGRedInputListener>",
            "m_feGRedInputHub: wref<InputHub>",
            "m_feGRedInputSubscriptions: array<Int32>",
            "m_feHubInputActive: Bool",
            "m_feFallbackInputRegistered: Bool"
        ];
        var missing=new List<string>();
        foreach(string field in fields)
        {
            string key=field.Split(':')[0];
            string value=field[(field.IndexOf(':')+1)..].Trim();
            var any=Regex.Matches(src,@"\blet\s+"+Regex.Escape(key)+@"\s*:\s*(?<type>[^;]+);");
            if(any.Count>1||(any.Count==1 &&
               string.Join("",any[0].Groups["type"].Value.Where(c=>!char.IsWhiteSpace(c)))!=
               string.Join("",value.Where(c=>!char.IsWhiteSpace(c)))))
                return new("REFUSED","FE_HUB_FIELD_COLLISION:"+key,null,0);
            if(any.Count==0)missing.Add(field);
        }
        if(missing.Count>0)
        {
            var m=Regex.Match(src,@"@wrapMethod\s*\(\s*PlayerPuppet\s*\)");
            if(!m.Success)return new("REFUSED","FE_HUB_FIELDS_ANCHOR_MISSING",null,0);
            Add(m.Index,m.Index,
                string.Join(nl,missing.Select(f=>
                    "@addField(PlayerPuppet)"+nl+"private let "+f+";"))+nl+nl);
        }
        // Historical implementations are recognized structurally by their
        // helper names; only missing independent helpers are generated.
        string helperText=Helpers(nl);
        string blockStart="@addMethod(PlayerPuppet)"+nl;
        int tryPos=helperText.IndexOf(blockStart+"private func FETryHubInputRoutes(",StringComparison.Ordinal);
        int cleanupPos=helperText.IndexOf(blockStart+"private func FECleanupInputRoutes(",StringComparison.Ordinal);
        int listenerPos=helperText.IndexOf("public class FEGRedInputListener extends InputListener",StringComparison.Ordinal);
        if(tryPos<0||cleanupPos<=tryPos||listenerPos<=cleanupPos)
            throw new InvalidDataException("FE_HUB_INTERNAL_TEMPLATE_BROKEN");
        var segments=new (string Name,string Text,string Proof)[] {
            ("FEHubStatusHotkey",helperText[..tryPos],@"\bfunc\s+FEHubStatusHotkey\s*\("),
            ("FETryHubInputRoutes",helperText[tryPos..cleanupPos],@"\bfunc\s+FETryHubInputRoutes\s*\("),
            ("FECleanupInputRoutes",helperText[cleanupPos..listenerPos],@"\bfunc\s+FECleanupInputRoutes\s*\("),
            ("FEGRedInputListener",helperText[listenerPos..],@"\bclass\s+FEGRedInputListener\b")
        };
        var missingHelpers=new List<string>();
        foreach(var item in segments)
        {
            if(!Present(src,item.Proof))
                missingHelpers.Add(item.Text);
            else if(item.Name=="FETryHubInputRoutes" &&
                    (!Present(src,@"\bSubscribe\s*\(") ||
                     !Present(src,@"\bFECleanupInputRoutes\s*\(")))
                return new("REFUSED","FE_HUB_PARTIAL_SUBSCRIPTION_HELPER_UNPROVEN",null,0);
            else if(item.Name=="FECleanupInputRoutes" &&
                    !Present(src,@"\bUnsubscribe\s*\("))
                return new("REFUSED","FE_HUB_PARTIAL_CLEANUP_UNPROVEN",null,0);
        }
        if(missingHelpers.Count>0)
        {
            var m=Regex.Match(src,@"\bpublic\s+class\s+FEModifierState\b");
            if(!m.Success)return new("REFUSED","FE_HUB_LISTENER_ANCHOR_MISSING",null,0);
            Add(m.Index,m.Index,string.Join(nl,missingHelpers)+nl);
        }

        var detached=Find(src,"OnDetach","", "PlayerPuppet");
        if(detached is null)
        {
            var m=Regex.Match(src,@"\bpublic\s+class\s+FEModifierState\b");
            if(!m.Success)return new("REFUSED","FE_HUB_DETACH_ANCHOR_MISSING",null,0);
            Add(m.Index,m.Index,"@wrapMethod(PlayerPuppet)"+nl+
                "protected cb func OnDetach() -> Bool {"+nl+
                "  this.FECleanupInputRoutes();"+nl+
                "  return wrappedMethod();"+nl+"}"+nl+nl);
        }
        else
        {
            string body=src[(detached.Open+1)..detached.Close];
            if(!Present(body,@"\bwrappedMethod\s*\(") ||
               !Present(body,@"\breturn\s+wrappedMethod\s*\(") )
                return new("REFUSED","FE_HUB_DETACH_LIFECYCLE_INCOMPATIBLE",null,0);
            if(!Present(body,@"\bFECleanupInputRoutes\s*\(") )
            {
                var r=Regex.Match(body,@"\breturn\s+wrappedMethod\s*\(\s*\)\s*;");
                if(!r.Success)return new("REFUSED","FE_HUB_DETACH_RETURN_UNPROVEN",null,0);
                Add(detached.Open+1+r.Index,detached.Open+1+r.Index,
                    "this.FECleanupInputRoutes();"+nl+"  ");
            }
        }
        if(changes.Count==0)return new("COMPLETE",null,null,0);
        // Refuse overlap rather than corrupting two independently rewritten
        // method bodies; same-position insertions are merged deterministically.
        var ordered=changes.OrderByDescending(e=>e.Start).ThenByDescending(e=>e.End).ToArray();
        for(int i=1;i<ordered.Length;i++)
            if(ordered[i].End>ordered[i-1].Start)
                return new("REFUSED","FE_HUB_OVERLAPPING_SEMANTIC_EDITS",null,0);
        string updated=src;
        foreach(var e in ordered)updated=updated[..e.Start]+e.Value+updated[e.End..];
        if(!Present(updated,@"\bfunc\s+FETryHubInputRoutes\s*\(") ||
           !Present(updated,@"\bfunc\s+HandleValues\s*\(") ||
           !Present(updated,@"\bfunc\s+FECleanupInputRoutes\s*\(") ||
           !Present(updated,@"\bfunc\s+OnGRedInput\s*\(") ||
           !Present(updated,@"\bm_feFallbackInputRegistered\s*=\s*true") ||
           !updated.Contains("return FEInput.HandleValues(player, name, type)",StringComparison.Ordinal) ||
           Regex.Matches(original,@"(?:GCO|GRSP)-GENERIC:[A-Z0-9_]+")
               .Select(m=>m.Value).Distinct(StringComparer.Ordinal)
               .Any(m=>!updated.Contains(m,StringComparison.Ordinal)))
            throw new InvalidDataException("FE_HUB_SOURCE_POSTCONDITION_FAILED");
        return new("APPLIED",null,updated,changes.Count);
    }

    internal static Result Generate(string gameRoot,string outputRoot,
        string? basePreviewRoot=null)
    {
        string game=Path.GetFullPath(gameRoot), output=Path.GetFullPath(outputRoot);
        string scripts=Path.Combine(game,"r6","scripts");
        if(!Directory.Exists(scripts)||Directory.Exists(output)||File.Exists(output)||
           Inside(output,game))throw new InvalidDataException("FE_HUB_OUTPUT_UNSAFE");
        if(basePreviewRoot is not null &&
           (!Directory.Exists(basePreviewRoot)||Inside(basePreviewRoot,game)||
            Inside(output,basePreviewRoot)||Inside(basePreviewRoot,output)))
            throw new InvalidDataException("FE_HUB_OVERLAY_UNSAFE");
        var admission=new SourceAdmission(game);
        _=admission.AuditAll();
        string hash=admission.SourceStackSha256??throw new InvalidDataException("FE_HUB_HASH_MISSING");
        string? rel=null,rewritten=null,reason=null;
        string status="NOT_APPLICABLE";int edits=0;
        string owner=Path.Combine(scripts,"FlushingEtiquette");
        if(Directory.Exists(owner))
        {
            var choices=Directory.EnumerateFiles(owner,"*.reds",SearchOption.AllDirectories)
                .Where(f=>Present(File.ReadAllText(f,Encoding.UTF8),
                    @"\bmodule\s+FlushingEtiquette\b")&&
                    Present(File.ReadAllText(f,Encoding.UTF8),
                    @"\bclass\s+FEInput\b")).ToArray();
            if(choices.Length!=1){status="REFUSED";reason=choices.Length==0?
                "FE_HUB_OWNER_INPUT_MISSING":"FE_HUB_OWNER_INPUT_AMBIGUOUS";}
            else if(!HasRuntimeAbi(scripts))
                {status="REFUSED";reason="FE_HUB_RUNTIME_API_MISSING";}
            else
            {
                string file=choices[0];
                if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)
                    throw new InvalidDataException("FE_HUB_SOURCE_LINK_UNSAFE");
                rel="r6/scripts/"+Path.GetRelativePath(scripts,file).Replace('\\','/');
                string source=File.ReadAllText(file,Encoding.UTF8);
                if(basePreviewRoot is not null)
                {
                    string staged=Path.GetFullPath(Path.Combine(basePreviewRoot,
                        rel.Replace('/',Path.DirectorySeparatorChar)));
                    if(!Inside(staged,basePreviewRoot))throw new InvalidDataException("FE_HUB_PREVIEW_ESCAPE");
                    if(File.Exists(staged))source=File.ReadAllText(staged,Encoding.UTF8);
                }
                Decision d=Rewrite(source);
                status=d.Status;reason=d.Reason;edits=d.Edits;rewritten=d.Source;
            }
        }
        if(!string.Equals(hash,admission.FingerprintCurrentSources(),StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("FE_HUB_SOURCE_CHANGED_DURING_GENERATE");
        Directory.CreateDirectory(output);
        if(rewritten is not null && rel is not null)
        {
            string target=Path.GetFullPath(Path.Combine(output,
                rel.Replace('/',Path.DirectorySeparatorChar)));
            if(!Inside(target,output)||!target.EndsWith(".reds",StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("FE_HUB_TARGET_UNSAFE");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target,rewritten,new UTF8Encoding(false));
        }
        Result result=new(Rule,status,edits,status=="COMPLETE"?1:0,
            rel,reason,true,false);
        File.WriteAllText(Path.Combine(output,"phase2-result.json"),
            JsonSerializer.Serialize(result,new JsonSerializerOptions{
                PropertyNamingPolicy=JsonNamingPolicy.CamelCase,WriteIndented=true}),
            new UTF8Encoding(false));
        return result;
    }
    private static bool Inside(string child,string root)=>
        Path.GetFullPath(child).StartsWith(Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
}
