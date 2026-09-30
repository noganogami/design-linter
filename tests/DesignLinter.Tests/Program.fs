module DesignLinter.Tests

open System
open System.IO
open System.Text.Json.Nodes
open DesignLinter

let assertEqual expected actual = if expected <> actual then failwithf "Expected %A, got %A" expected actual
let assertTrue condition message = if not condition then failwith message
let mutable passed = 0
let mutable failed = 0
let test name action =
    try action(); passed <- passed + 1; printfn "PASS %s" name
    with ex -> failed <- failed + 1; eprintfn "FAIL %s: %s" name ex.Message

let project (report: ReviewReport) =
    {| status = report.status
       checks = report.checks |> List.map (fun c -> {| ruleId = c.ruleId; status = c.status; metrics = Option.defaultValue Map.empty c.metrics; findingStatuses = c.findings |> List.map (fun f -> string f.status) |> List.sort |})
       analysis = report.analysis
       diagnostics = report.diagnostics |> List.map _.code |> List.sort
       requirements = report.designIR.requirements |> List.map _.id |> List.sort
       entities = report.designIR.entities |> List.map _.id |> List.sort
       files = report.designIR.files |> List.map (fun f -> {| path = f.path; kind = f.kind; change = f.change |}) |> List.sortBy _.path
       relations = report.designIR.relations |> List.map (fun r -> $"{r.kind}|{r.from}|{r.``to``}|{r.resolution}") |> List.sort
       diff = report.semanticDiff.groups |> List.map (fun g -> {| area = g.area; status = g.status; changes = g.changes |> List.map (fun c -> {| kind = c.kind; subject = c.subject; before = c.before; after = c.after |}) |})
       focusCount = report.humanReviewFocus.items.Length
       aiEligible = report.aiReview.eligible |}

let fixtures = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "migration.json"))).AsArray()
let get (key: string) (node: JsonNode) = node[key].GetValue<string>()
let valid = fixtures |> Seq.find (fun f -> get "name" f = "valid")
let requirements, design = get "requirements" valid, get "design" valid
let parsed () = Parser.parse requirements design
let at : Evidence = { file = "design.md"; startLine = 1; endLine = 1; section = "Test" }
let relation kind from target resolution : Relation = { kind = kind; from = from; ``to`` = target; resolution = resolution; evidence = [at] }
let entity kind name : Entity = { id = kind + ":" + name; name = name; kind = kind; layer = None; dependencyDeclared = Some true; evidence = [at] }
let graph () =
    { Domain.empty { requirements = "requirements.md"; design = "design.md" } with
        requirements = [{ id = "1.1"; text = "Record payment"; evidence = [at] }]
        requirementReferences = [{ id = "1.1"; evidence = [at] }]
        entities = [entity "component" "A"]
        relations = [relation "realizedBy" "1.1" "component:A" "resolved"; relation "implementedAt" "component:A" "src/a.ts" "resolved"]
        files = [{ path = "src/a.ts"; kind = "file"; change = "new"; evidence = [at] }]
        filePlan = { present = true; placeholders = []; evidence = [at] }
        boundary = ["This Spec Owns"; "Out of Boundary"; "Allowed Dependencies"; "Revalidation Triggers"] |> List.map (fun name -> { name = name; text = "Concrete statement"; placeholderOnly = false; evidence = [at] }) }

[<EntryPoint>]
let main _ =
    for case in fixtures do
        test ("migration: " + get "name" case) (fun () ->
            let ir = Parser.parse (get "requirements" case) (get "design" case)
            let before = if isNull case["beforeDesign"] then None else Some (Parser.parse (get "beforeRequirements" case) (get "beforeDesign" case))
            let actual = Report.build ir before |> project |> Json.serialize |> JsonNode.Parse
            for property in case["expected"].AsObject() do
                if not (JsonNode.DeepEquals(property.Value, actual[property.Key])) then
                    failwithf "%s\nexpected: %O\nactual: %O" property.Key property.Value actual[property.Key])

    test "M1 resolved coverage" (fun () ->
        let result = Rules.requirementCoverage (graph())
        assertEqual PASS result.status
        assertEqual (Some (Map.ofList ["total",1; "referenced",1; "mapped",1])) result.metrics)
    test "M1 uncovered and unknown requirement" (fun () ->
        let ir = { graph() with relations = []; requirementReferences = [{ id = "9.9"; evidence = [at] }] }
        let result = Rules.requirementCoverage ir
        assertEqual FAIL result.status
        assertTrue (result.findings |> List.exists (fun f -> f.message.Contains "9.9")) "missing unknown ID")
    test "M1 dangling target and reference-only" (fun () ->
        let ir = { graph() with relations = [relation "realizedBy" "1.1" "component:Missing" "unresolved"] }
        let result = Rules.requirementCoverage ir
        assertEqual FAIL result.status
        assertEqual 0 result.metrics.Value["mapped"])
    test "M1 noncomponent target" (fun () ->
        let ir = { graph() with entities = [entity "decision" "RetryPolicy"]; relations = [relation "realizedBy" "1.1" "decision:RetryPolicy" "resolved"] }
        assertEqual PASS (Rules.requirementCoverage ir).status)
    test "M1 partial extraction and empty requirements" (fun () ->
        let ir = graph()
        assertEqual UNKNOWN (Rules.requirementCoverage {ir with relations = []; completeness = ir.completeness.Add("mappings", "partial")}).status
        assertEqual UNKNOWN (Rules.requirementCoverage (Domain.empty ir.documents)).status)
    test "M2 unrelated files and external systems are allowed" (fun () ->
        let ir = graph()
        let ir = {ir with entities = ir.entities @ [entity "external" "Gateway"]; files = ir.files @ [{path = "config.json"; kind = "file"; change = "new"; evidence = [at]}]}
        assertEqual PASS (Rules.filePlanConsistency ir).status)
    test "M2 orphan and dangling reference" (fun () ->
        let result = Rules.filePlanConsistency {graph() with relations = [relation "implementedAt" "component:Missing" "src/a.ts" "unresolved"]}
        assertEqual FAIL result.status
        assertEqual 2 result.findings.Length)
    test "M2 directory patterns are UNKNOWN" (fun () ->
        assertEqual UNKNOWN (Rules.filePlanConsistency {graph() with relations = []; files = [{path = "src/"; kind = "directory"; change = "new"; evidence = [at]}]}).status)
    test "M2 empty plan and placeholders fail" (fun () ->
        assertEqual FAIL (Rules.filePlanConsistency {graph() with files = []; relations = []}).status
        let ir = graph()
        assertEqual FAIL (Rules.filePlanConsistency {ir with filePlan = {ir.filePlan with placeholders = [at]}}).status)
    test "M2B boundary presence only" (fun () ->
        let ir = graph()
        assertEqual PASS (Rules.boundaryPopulated ir).status
        assertEqual FAIL (Rules.boundaryPopulated {ir with boundary = []}).status
        assertEqual FAIL (Rules.boundaryPopulated {ir with boundary = ir.boundary |> List.map (fun b -> {b with placeholderOnly = true})}).status
        assertEqual UNKNOWN (Rules.boundaryPopulated {ir with boundary = []; completeness = ir.completeness.Add("boundary", "unsupported")}).status)
    test "SCC DAG, self-loop, multiple and multi-node cycles" (fun () ->
        assertEqual [] (Rules.stronglyConnected ["A";"B"] ["A","B"])
        assertEqual [["A"]] (Rules.stronglyConnected ["A"] ["A","A"])
        assertEqual [["A";"B"];["C";"D"]] (Rules.stronglyConnected ["A";"B";"C";"D"] ["A","B";"B","A";"C","D";"D","C"])
        assertEqual [["A";"B";"C"]] (Rules.stronglyConnected ["A";"B";"C"] ["A","B";"B","C";"C","A"]))
    test "M3 cycle is REVIEW_REQUIRED" (fun () ->
        let ir = graph()
        let result = Rules.circularDependencies {ir with relations = ir.relations @ [relation "dependsOn" "component:A" "component:A" "resolved"]}
        assertEqual REVIEW_REQUIRED result.status
        assertTrue (result.findings.Head.message.Contains "A → A") "no witness")
    test "M3 incomplete extraction never passes" (fun () ->
        let ir = graph()
        assertEqual PASS (Rules.circularDependencies ir).status
        assertEqual UNKNOWN (Rules.circularDependencies {ir with completeness = ir.completeness.Add("dependencies", "partial")}).status)
    test "diff suppresses incomplete areas and preserves independent changes" (fun () ->
        let before = parsed()
        let after = {before with completeness = before.completeness.Add("components", "partial"); entities = []; files = before.files @ [{path = "new.ts"; kind = "file"; change = "new"; evidence = [at]}]}
        let diff = SemanticDiff.compare (Some before) after
        assertEqual "PARTIAL" diff.status
        assertEqual [] (diff.groups |> List.find (fun g -> g.area = "Components")).changes
        assertEqual 1 (diff.groups |> List.find (fun g -> g.area = "Files")).changes.Length)
    test "unresolved mapping cannot be a reliable diff" (fun () ->
        let ir = parsed()
        let after = {ir with relations = ir.relations |> List.map (fun r -> if r.kind = "realizedBy" then {r with resolution = "unresolved"} else r)}
        assertEqual "UNKNOWN" ((SemanticDiff.compare (Some ir) after).groups |> List.find (fun g -> g.area = "Requirement Mapping")).status)
    test "profile mismatch suppresses comparison" (fun () ->
        let ir = parsed()
        assertTrue ((SemanticDiff.compare (Some ir) {ir with profile = {ir.profile with parserVersion = "other"}}).groups |> List.forall (fun g -> g.status = "UNKNOWN")) "compared incompatible profiles")
    test "focus max three, evidence and no fabricated AI judgment" (fun () ->
        let case = fixtures |> Seq.find (fun f -> get "name" f = "baseline-diff")
        let report = Report.build (parsed()) (Some (Parser.parse requirements (get "beforeDesign" case)))
        assertEqual 3 report.humanReviewFocus.items.Length
        assertTrue (report.humanReviewFocus.items |> List.forall (fun f -> not f.evidence.IsEmpty)) "missing evidence"
        assertTrue (report.humanReviewFocus.omitted > 0) "no omitted count"
        assertEqual "NOT_RUN" report.aiReview.status
        assertTrue ((Report.markdown report).Contains "#L") "missing source links")
    test "focus does not pad questions" (fun () ->
        let report = Report.build (parsed()) None
        assertEqual [] report.humanReviewFocus.items
        assertTrue ((Report.markdown report).Contains "意味的レビューは未実施") "missing semantic scope")
    test "same input yields byte-identical JSON" (fun () ->
        assertEqual (Report.build (parsed()) None |> Json.serialize) (Report.build (parsed()) None |> Json.serialize))
    test "parser merges summaries and retains line evidence" (fun () ->
        let ir = parsed()
        assertEqual 2 (Domain.components ir).Length
        assertEqual 2 (ir.entities |> List.find (fun e -> e.name = "PaymentService")).evidence.Length
        let lines = design.Split('\n')
        for e in Domain.components ir do
            for location in e.evidence do
                assertTrue (lines[location.startLine - 1].Contains e.name) "wrong source line")
    test "parser excludes code examples and versions from IDs" (fun () ->
        let ir = Parser.parse (requirements + "\n```text\n### Requirement 9: Fake\n1. Fake\n```") (design + "\n## Notes\nVersion 5.9; 99.9")
        assertEqual 2 ir.requirements.Length
        assertTrue (ir.requirementReferences |> List.forall (fun r -> r.id <> "99.9")) "version became ID")
    test "parser section order independent" (fun () ->
        let fileStart, compStart = design.IndexOf("## File Structure Plan"), design.IndexOf("## Components")
        let reordered = design[..fileStart-1] + design[compStart..] + "\n" + design[fileStart..compStart-1]
        let facts (ir: DesignIR) = ir.relations |> List.map (fun r -> r.kind,r.from,r.``to``,r.resolution) |> List.sort
        assertEqual (facts (parsed())) (facts (Parser.parse requirements reordered)))
    test "dependency table is not a component definition (migration fix)" (fun () ->
        let input = design.Replace("- Outbound: PaymentService — retry payment (P0)", "| Direction | Component |\n|---|---|\n| Outbound | PaymentService |")
        let ir = Parser.parse requirements input
        assertEqual PASS (Report.build ir None).status
        assertEqual 2 (Domain.components ir).Length)
    test "interface names and owners" (fun () ->
        let input = design + "\n##### Service Interface\n```typescript\ninterface RetryContract { retry(): void; }\n```\n## Requirements Traceability\n| Requirement | Interfaces |\n|---|---|\n| 1.2 | RetryContract |"
        let ir = Parser.parse requirements input
        assertTrue (ir.relations |> List.exists (fun r -> r.kind = "providedBy" && r.from = "interface:RetryContract" && r.``to`` = "component:PaymentRetryWorker")) "owner missing"
        assertEqual PASS (Rules.requirementCoverage ir).status)

    let dir = Path.Combine(Path.GetTempPath(), "design-linter-fsharp-tests-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory dir |> ignore
    try
        let r, d = Path.Combine(dir,"requirements.md"), Path.Combine(dir,"design.md")
        File.WriteAllText(r, requirements); File.WriteAllText(d, design)
        let baseArgs = [|"--requirements";r;"--design";d|]
        let run args =
            use stdout = new StringWriter()
            use stderr = new StringWriter()
            let status = Program.run args stdout stderr
            status, stdout.ToString(), stderr.ToString()
        test "CLI JSON-only stdout and input immutability" (fun () ->
            let code, output, error = run (Array.append baseArgs [|"--format";"json"|])
            assertEqual 0 code; assertEqual "" error
            let parsedOutput = JsonNode.Parse(output)
            assertEqual "PASS" (parsedOutput["status"].GetValue<string>())
            assertTrue (parsedOutput["inputs"].AsObject().ContainsKey("baseline") && isNull (parsedOutput["inputs"]["baseline"])) "baseline must remain explicit null in schema v1"
            assertEqual design (File.ReadAllText d); assertEqual requirements (File.ReadAllText r))
        test "CLI input and usage errors" (fun () ->
            for args in [[|"--requirements";"missing";"--design";"missing"|]; Array.append baseArgs [|"--before-design";"old.md"|]; Array.append baseArgs [|"--format";"html"|]; Array.append baseArgs [|"--invalid"|]; [|"--design"|]] do
                let code, output, error = run args
                assertEqual 2 code; assertEqual "" output; assertTrue (error <> "") "missing stderr")
        test "CLI baseline and Markdown" (fun () ->
            let old = fixtures |> Seq.find (fun f -> get "name" f = "baseline-diff") |> get "beforeDesign"
            let baseline = Path.Combine(dir,"old.md")
            File.WriteAllText(baseline, old)
            let code, output, _ = run (Array.append baseArgs [|"--before-requirements";r;"--before-design";baseline|])
            assertEqual 0 code; assertTrue (output.Contains "Human Review Focus") "missing focus"
            assertTrue (output.Contains "Requirement Mapping") "missing diff")
        test "CLI distinguishes FAIL and UNKNOWN" (fun () ->
            File.WriteAllText(d,design.Replace("1.2","9.9"))
            let code, _, _ = run baseArgs
            assertEqual 1 code
            File.WriteAllText(d,"# Unsupported\nNo structure")
            let code, _, _ = run baseArgs
            assertEqual 3 code)
        test "CLI help" (fun () ->
            let code, output, error = run [|"--help"|]
            assertEqual 0 code; assertTrue (output.Contains "Usage:") "missing usage"; assertEqual "" error)
    finally Directory.Delete(dir,true)
    printfn "%d passed; %d failed" passed failed
    if failed = 0 then 0 else 1
