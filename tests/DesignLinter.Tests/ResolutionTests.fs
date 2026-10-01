module DesignLinter.ResolutionTests

open System
open System.IO
open DesignLinter

let run test requirements (design: string) =
    let eq expected actual = if expected <> actual then failwithf "Expected %A, got %A" expected actual
    let yes condition message = if not condition then failwith message
    let trace ids target column = $"\n## Requirements Traceability\n| Requirement | {column} |\n|---|---|\n| {ids} | {target} |\n"
    test "ranges expand explicit endpoints and preserve missing IDs" (fun () ->
        for separator in ["-"; "–"; " – "] do
            let ir = Parser.parse requirements (design + trace ("1.1" + separator + "1.3") "PaymentService" "Components")
            eq ["1.1";"1.2";"1.3"] (ir.requirementReferences |> List.map _.id |> List.distinct |> List.sort)
            let check = Rules.requirementCoverage ir
            eq FAIL check.status
            yes (check.findings |> List.exists (fun f -> f.message = "Unknown requirement ID: 1.3")) "missing range ID was silently dropped")
    test "range upper bound and invalid syntax stay UNKNOWN without throwing" (fun () ->
        for ids in ["1.2–1.1"; "1.1–2.1"; "1.1–1.1001"; "1.1–1.999999999999999999999"; "01.1–01.2"; "1.0–1.2"; "1.1–"; "1"] do
            let ir = Parser.parse requirements (design + trace ids "PaymentService" "Components")
            eq "partial" ir.completeness["mappings"]
            yes (ir.diagnostics |> List.exists (fun d -> d.area = "mappings")) "range diagnostic missing"
        let ir = Parser.parse requirements (design + trace "1.1–1.1000" "PaymentService" "Components")
        eq 1000 (ir.requirementReferences |> List.map _.id |> List.distinct |> List.length))
    test "range lists, singletons and bullet mappings are deterministic" (fun () ->
        let input = design + trace "1.1–1.2、1.2;1.1-1.1" "PaymentService" "Components" + "\n- 1.1–1.2 → PaymentRetryWorker\n"
        let a = Parser.parse requirements input
        eq PASS (Rules.requirementCoverage a).status
        eq (Json.serialize a) (Parser.parse requirements input |> Json.serialize)
        yes (a.relations |> List.exists (fun r -> r.from = "1.1" && r.``to`` = "component:PaymentRetryWorker")) "range bullet lost")
    test "ambiguous noncomponent prose is UNKNOWN while explicit component typo fails" (fun () ->
        let ir = Parser.parse requirements (design + trace "1.1–1.2" "exit 2、段 2" "Interfaces")
        eq UNKNOWN (Rules.requirementCoverage ir).status
        let report = Report.build ir None |> Report.withInputs requirements (design + trace "1.1–1.2" "exit 2、段 2" "Interfaces") None
        eq 1 report.reviewIssues.Length
        eq ["1.1"; "1.2"] report.reviewIssues.Head.affectedRequirements
        eq 2 report.reviewIssues.Head.targets.Length
        yes (not report.reviewIssues.Head.excerpts.IsEmpty) "source excerpt missing"
        yes (report.reviewIssues.Head.excerpts.Head.text.Contains "exit 2") "wrong excerpt"
        eq FAIL (Parser.parse requirements (design + trace "1.1" "MissingComponent" "Components") |> Rules.requirementCoverage).status)
    test "grouped unresolved aliases preserve per-requirement findings" (fun () ->
        let input = design + trace "1.1–1.2" "MissingComponent" "Components"
        let report = Parser.parse requirements input |> fun ir -> Report.build ir None
        eq 1 report.reviewIssues.Length
        eq ["1.1";"1.2"] report.reviewIssues.Head.affectedRequirements
        eq 2 report.checks.Head.findings.Length)

    let contract = "\n##### Service Interface\nExisting contract.\n"
    let row = "| 1.1–1.2 | Retry API |"
    let input = design + contract + trace "1.1–1.2" "Retry API" "Interfaces"
    let ir = Parser.parse requirements input
    let entry : BindingEntry = {
        id = "retry-api"; kind = "referenceAlias"; from = "interface:Retry API"; ``to`` = "interface:PaymentRetryWorker Service Interface"
        state = "proposed"; author = "test"; rationale = "Candidate contract identity for review."
        evidence = [{ document = "design"; quote = row }; {document = "design"; quote = "##### Service Interface"}]
    }
    let document : BindingDocument = { schemaVersion = 1; inputs = Bindings.fingerprint requirements input ir.profile; entries = [entry] }
    let evaluate doc = Bindings.evaluate requirements input ir doc
    test "binding candidate is conditional and source IR remains unchanged" (fun () ->
        let before = Json.serialize ir
        let result = evaluate document
        eq "CONDITIONAL" result.status
        eq (Some PASS) result.mechanicalStatus
        eq UNKNOWN (Rules.requirementCoverage ir).status
        eq before (Json.serialize ir)
        eq "proposed" result.entries.Head.state
        eq result (evaluate document))
    test "accepted binding remains an interpretation, not unconditional proof" (fun () ->
        let result = evaluate {document with entries = [{entry with state = "accepted"}]}
        eq "CONDITIONAL" result.status
        let report = Report.build ir None |> Report.withInputs requirements input (Some document)
        eq UNKNOWN report.status
        eq false report.aiReview.eligible
        yes (Report.markdown report |> fun s -> s.Contains "Supplemental Review") "conditional result omitted")
    test "binding schema is strict and round-trips" (fun () ->
        eq document (document |> Json.serialize |> Bindings.read)
        for text in [(Json.serialize document).Replace("\"author\"", "\"auther\""); "{}"; "null"; (Json.serialize document).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 1, \"schemaVersion\": 1")] do
            let mutable rejected = false
            try Bindings.read text |> ignore with _ -> rejected <- true
            yes rejected "bad schema accepted")
    test "stale documents and parser versions never apply bindings" (fun () ->
        for result in [Bindings.evaluate requirements (input + "\nChanged") ir document; evaluate {document with inputs = {document.inputs with parserVersion = "old"}}] do
            eq "STALE" result.status
            eq None result.designIR
            eq [] result.checks)
    test "invalid binding targets and unsupported operations are atomic" (fun () ->
        for bad in [{entry with ``to`` = "interface:Missing"}; {entry with from = "interface:Unknown"}; {entry with kind = "ignore"}; {entry with state = "verified"}; {entry with author = ""}; {entry with rationale = ""}; {entry with ``to`` = "component:PaymentService"}] do
            let result = evaluate {document with entries = [bad]}
            eq "INVALID" result.status
            eq None result.designIR
            eq [] result.checks)
    test "binding evidence must cover every source and target, and quote uniquely" (fun () ->
        for evidence in [[]; [{document = "design"; quote = row}]; [{document = "design"; quote = "payment"}]; [{document = "design"; quote = "NOT IN SOURCE"}]; [{document = "other"; quote = row}]; [{document = "design"; quote = "Retry API"};entry.evidence[1]]] do
            eq "INVALID" (evaluate {document with entries = [{entry with evidence = evidence}]}).status)
    test "empty bindings and wrong schema versions do not produce conditional success" (fun () ->
        for doc in [{document with entries = []}; {document with schemaVersion = 2}] do
            eq "INVALID" (evaluate doc).status)
    test "cause categories distinguish conflict, absence and unsupported syntax" (fun () ->
        let cases = ["conflicting-coverage","CONFLICT";"dependencies-undeclared","MISSING_INFORMATION";"unsupported-file-entry","UNSUPPORTED_SYNTAX"]
        for code, expected in cases do
            let diagnostic = {code=code;area="dependencies";message="test";evidence=ir.entities.Head.evidence}
            let parsed = {ir with diagnostics=[diagnostic];relations=[]}
            let issues = ReviewIssues.build parsed []
            eq expected issues.Head.category)
    test "full report keeps confirmed failures ahead of resolution questions" (fun () ->
        let input = input + trace "9.9" "MissingComponent" "Components"
        let parsed = Parser.parse requirements input
        let report = Report.build parsed None |> Report.withInputs requirements input None
        eq FAIL report.status
        yes (report.humanReviewFocus.items.Head.reason.StartsWith "FAIL") "confirmed failure was hidden"
        yes (report.humanReviewFocus.items.Length <= 3) "too many primary questions"
        eq (Json.serialize report) (Report.build parsed None |> Report.withInputs requirements input None |> Json.serialize))
    test "duplicate binding IDs and aliases are rejected" (fun () ->
        for entries in [[entry;entry];[entry;{entry with id = "another"}]] do
            eq "INVALID" (evaluate {document with entries = entries}).status)
    test "unrelated ambiguous cells remain unresolved after binding" (fun () ->
        let extra = input + "| 1.1 | Other API |\n"
        let parsed = Parser.parse requirements extra
        let result = Bindings.evaluate requirements extra parsed {document with inputs = Bindings.fingerprint requirements extra parsed.profile}
        eq "CONDITIONAL" result.status
        eq (Some UNKNOWN) result.mechanicalStatus)
    test "file mapping requires an existing component and concrete file" (fun () ->
        let input = design.Replace("| src/payment/service.ts | PaymentService |", "| src/payment/service.ts | |")
        let parsed = Parser.parse requirements input
        let mapping = {entry with kind = "fileMapping"; from = "component:PaymentService"; ``to`` = "src/payment/service.ts"
                                  evidence = [{document="design";quote="#### PaymentService"};{document="design";quote="| src/payment/service.ts | | Record payment results |"}]}
        let doc = {document with inputs = Bindings.fingerprint requirements input parsed.profile; entries = [mapping]}
        let result = Bindings.evaluate requirements input parsed doc
        eq "CONDITIONAL" result.status
        eq (Some PASS) result.mechanicalStatus
        eq FAIL (Rules.filePlanConsistency parsed).status
        let result = Bindings.evaluate requirements input parsed {doc with entries = [{mapping with ``to`` = "src/"}]}
        eq "INVALID" result.status)
    test "CLI bindings are noninteractive, preserve raw exit and report errors" (fun () ->
        let dir = Path.Combine(Path.GetTempPath(), "design-linter-binding-" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory dir |> ignore
        try
            let req, doc, binding = Path.Combine(dir,"requirements.md"), Path.Combine(dir,"design.md"), Path.Combine(dir,"bindings.json")
            File.WriteAllText(req,requirements); File.WriteAllText(doc,input); File.WriteAllText(binding,Json.serialize document)
            let run () =
                use stdout = new StringWriter()
                use stderr = new StringWriter()
                let code = Program.run [|"--requirements";req;"--design";doc;"--bindings";binding;"--format";"json"|] stdout stderr
                code, stdout.ToString(), stderr.ToString()
            let code, output, error = run()
            eq 3 code; eq "" error
            yes (output.Contains "CONDITIONAL") "missing conditional report"
            eq input (File.ReadAllText doc)
            File.WriteAllText(binding,Json.serialize {document with entries = [{entry with ``to`` = "interface:Missing"}]})
            let code, output, _ = run()
            eq 2 code; yes (output.Contains "INVALID") "missing invalid report"
            File.WriteAllText(binding,Json.serialize document); File.AppendAllText(doc,"\nChanged")
            let code, output, _ = run()
            eq 3 code; yes (output.Contains "STALE") "missing stale report"
        finally Directory.Delete(dir,true))
