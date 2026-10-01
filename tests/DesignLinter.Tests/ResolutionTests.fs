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
