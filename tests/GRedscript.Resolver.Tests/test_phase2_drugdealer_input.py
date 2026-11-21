"""Historical Pass 2.1 DrugDealer five independent input migrations."""
from pathlib import Path
import json
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
DLL = ROOT/'src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll'
FRAMEWORK = '''module GRedRuntime
public class Runtime extends ScriptableSystem {
 public static func Get(game: GameInstance) -> ref<Runtime> { return null; }
 public func IsReady() -> Bool { return true; }
 public func GetInputHub() -> ref<InputHub> { return null; }
}
public class InputListener extends IScriptable { public func OnGRedInput(evt: ref<InputEvent>) -> Void {} }
public class InputEvent extends IScriptable {
 public let actionName: CName; public let actionType: gameinputActionType; public let consumed: Bool;
}
public class InputHub extends IScriptable {
 public func Subscribe(name: CName, cb: ref<InputListener>) -> Int32 { return 1; }
 public func Unsubscribe(id: Int32) -> Bool { return true; }
}
'''
ACTIONS = '''module DrugDealer.Settings
public class Constants {
 public static func InputRegisteredActions() -> array<CName> = [n"DDInteractionActionMain", n"DDInteractionActionSecondary"];
}
'''
HELPERS = '''module DrugDealer.Settings
public func IsMainAction(action: ListenerAction) -> Bool {
    let registeredActions = Constants.InputRegisteredActions();
    return Equals(ListenerAction.GetName(action), registeredActions[0])
        && Equals(ListenerAction.GetType(action), gameinputActionType.BUTTON_HOLD_COMPLETE);
}
public func IsSecondaryAction(action: ListenerAction) -> Bool {
    let registeredActions = Constants.InputRegisteredActions();
    return Equals(ListenerAction.GetName(action), registeredActions[1])
        && Equals(ListenerAction.GetType(action), gameinputActionType.BUTTON_HOLD_COMPLETE);
}
'''
OWNERS = [
 ('Crackhouse/Crackhouse.reds','DrugDealer.Crackhouse','CrackhouseInputListener','crackhouseInputListener',True,True),
 ('Job/SellDrugs/Interaction.reds','DrugDealer.Job.SellDrugs','DDInputListener','ddInputListener',False,False),
 ('Lab/Lab.reds','DrugDealer.Lab','LabInputListener','labInputListener',False,True),
 ('NightMarket/NightMarket.reds','DrugDealer.NightMarket','NightMarketInputListener','nightMarketInputListener',False,False),
 ('Operation/OperationProximity.reds','DrugDealer.Operation','OperationInputListener','operationInputListener',False,True),
]

def stock_source(row):
    path,module,cls,field,consumes,second=row
    main='''        if IsMainAction(action) { controller.DoMain(); }'''
    sec='''\n        if IsSecondaryAction(action) { controller.DoSecondary(); }''' if second else ''
    true_return='\n        return true;' if consumes else ''
    return f'''module {module}
public class {cls} {{
    public let gameInstance: GameInstance;
    protected cb func OnAction(action: ListenerAction, consumer: ListenerActionConsumer) -> Bool {{
        let actionName = ListenerAction.GetName(action);
        // GCO-GENERIC:ACTION_LITERAL_MEMBERSHIP_V1:Constants.InputRegisteredActions
        if !Equals(actionName, n"DDInteractionActionMain") && !Equals(actionName, n"DDInteractionActionSecondary") {{ return false; }}
        if !Equals(ListenerAction.GetType(action), gameinputActionType.BUTTON_HOLD_COMPLETE) {{
            return false;
        }}
        let controller = DrugDealerController.Get();
        if !IsDefined(controller) {{ return false; }}
{main}{sec}{true_return}
    }}
}}
@addField(PlayerPuppet)
private let {field}: ref<{cls}>;
@wrapMethod(PlayerPuppet)
protected cb func OnGameAttached() -> Bool {{
    wrappedMethod();
    let handler: ref<inkISystemRequestsHandler> = GameInstance.GetSystemRequestsHandler();
    if IsDefined(handler) && handler.IsPreGame() {{ return true; }}
    this.{field} = new {cls}();
    this.{field}.gameInstance = GetGameInstance();
    this.RegisterInputListener(this.{field});
    let unrelated: Int32 = 44;
    return true;
}}
@wrapMethod(PlayerPuppet)
protected cb func OnDetach() -> Bool {{
    wrappedMethod();
    if !IsDefined(this.{field}) {{ return true; }}
    this.UnregisterInputListener(this.{field});
    this.{field} = null;
    return true;
}}
'''

class DrugDealerInputTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name)
        self.calls=0

    def generate(self, changes=None, framework=True, helpers=True, rows=OWNERS):
        self.calls+=1
        game=self.root/f'game-{self.calls}'
        for path,module,cls,field,consumes,second in rows:
            file=game/'r6/scripts/DrugDealer'/path
            file.parent.mkdir(parents=True,exist_ok=True)
            text=(changes or {}).get(cls,stock_source((path,module,cls,field,consumes,second)))
            file.write_text(text,encoding='utf8')
        if rows:
            settings=game/'r6/scripts/DrugDealer/Settings'
            settings.mkdir(parents=True,exist_ok=True)
            (settings/'Constants.reds').write_text(ACTIONS,encoding='utf8')
            (settings/'Input.reds').write_text(HELPERS if helpers else HELPERS.replace('registeredActions[0]','registeredActions[1]'),encoding='utf8')
        if framework:
            target=game/'r6/scripts/MyRelocatedRuntime/core/Input.reds'
            target.parent.mkdir(parents=True,exist_ok=True)
            target.write_text(FRAMEWORK,encoding='utf8')
        out=self.root/f'out-{self.calls}'
        argv=['dotnet',str(DLL),'phase2-drugdealer-input','--game',str(game),'--out',str(out)]
        result=subprocess.run(argv,text=True,capture_output=True,timeout=150)
        self.assertEqual(result.returncode,0,result.stdout+result.stderr)
        report=json.loads((out/'phase2-result.json').read_text('utf8'))
        generated={r[2]:(out/'r6/scripts/DrugDealer'/r[0]).read_text('utf8') for r in rows if (out/'r6/scripts/DrugDealer'/r[0]).exists()}
        return report,generated

    def test_stock_all_five_and_repeat_are_complete(self):
        report,files=self.generate()
        self.assertEqual(report['status'],'APPLIED',report)
        self.assertEqual(len(files),5,report)
        self.assertEqual(len(report['refusals']),0,report)
        for row in OWNERS:
            s=files[row[2]]
            self.assertIn('OnGRedInput(evt: ref<InputEvent>)',s)
            self.assertIn('ACTION_LITERAL_MEMBERSHIP_V1',s)
            self.assertIn('EngineFallback',s)
            self.assertIn('RegisterInputListener(this.',s)
            self.assertIn('Unsubscribe(this.',s)
            self.assertIn('unrelated: Int32 = 44',s)
            if row[4]:self.assertIn('evt.consumed = true',s)
            else:self.assertNotIn('evt.consumed = true',s)
        rep2,second=self.generate(changes=files)
        self.assertEqual(rep2['status'],'COMPLETE',rep2)
        self.assertEqual(second,{},rep2)

    def test_partial_missing_field_completed(self):
        _,files=self.generate()
        s=files['CrackhouseInputListener']
        s=s.replace('@addField(PlayerPuppet)\nprivate let gcoCrackhouseInputListenerEngineFallback: Bool;\n','')
        self.assertNotEqual(s,files['CrackhouseInputListener'])
        rep,out=self.generate(changes={**files, 'CrackhouseInputListener':s})
        self.assertEqual(rep['status'],'APPLIED',rep)
        self.assertIn('gcoCrackhouseInputListenerEngineFallback: Bool',out['CrackhouseInputListener'])
        self.assertEqual(len(out),1)

    def test_one_incompatible_does_not_block_four_others(self):
        changed=stock_source(OWNERS[2]).replace('BUTTON_HOLD_COMPLETE','BUTTON_RELEASED')
        rep,files=self.generate(changes={'LabInputListener':changed})
        self.assertEqual(len(files),4,rep)
        self.assertTrue(any('LabInputListener' in x for x in rep['refusals']),rep)

    def test_custom_source_preserved_and_lifecycle_unchanged(self):
        s=stock_source(OWNERS[4]).replace('controller.DoMain();','controller.DoMain(); controller.CustomAudit();')
        rep,files=self.generate(changes={'OperationInputListener':s})
        self.assertEqual(rep['status'],'APPLIED',rep)
        self.assertIn('controller.CustomAudit();',files['OperationInputListener'])
        self.assertIn('return true;',files['OperationInputListener'])

    def test_bad_action_selector_refuses_without_output(self):
        rep,files=self.generate(helpers=False)
        self.assertEqual(rep['status'],'REFUSED',rep)
        self.assertEqual(files,{})

    def test_missing_framework_refuses_without_output(self):
        rep,files=self.generate(framework=False)
        self.assertEqual(rep['status'],'REFUSED',rep)
        self.assertEqual(files,{})

    def test_absent_owner_does_not_emit_mod(self):
        rep,files=self.generate(rows=[])
        self.assertEqual(rep['status'],'NOT_APPLICABLE',rep)
        self.assertEqual(files,{})

if __name__=='__main__':unittest.main()
