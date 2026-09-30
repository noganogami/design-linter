namespace DesignLinter

type Change = { kind: string; subject: string; before: string list; after: string list; evidence: Evidence list }
type DiffGroup = { area: string; status: string; reason: string option; changes: Change list }
type SemanticDiff = { status: string; groups: DiffGroup list }

module SemanticDiff =
    let private ordered values = values |> List.distinct |> List.sort
    let private setDiff (old: (string * Evidence list) list) current =
        let oldKeys, newKeys = old |> List.map fst |> Set.ofList, current |> List.map fst |> Set.ofList
        [ for key in Set.difference newKeys oldKeys do
            yield { kind = "added"; subject = key; before = []; after = [key]; evidence = current |> List.filter (fst >> (=) key) |> List.collect snd }
          for key in Set.difference oldKeys newKeys do
            yield { kind = "removed"; subject = key; before = [key]; after = []; evidence = old |> List.filter (fst >> (=) key) |> List.collect snd } ]
    let compare (before: DesignIR option) (after: DesignIR) : SemanticDiff =
        match before with
        | None -> { status = "NOT_RUN"; groups = [] }
        | Some before ->
            let check area prerequisites relationships build =
                let incomplete = prerequisites |> List.filter (fun a -> before.completeness[a] <> "complete" || after.completeness[a] <> "complete")
                let unresolved = [before; after] |> List.exists (fun ir -> ir.relations |> List.exists (fun r -> List.contains r.kind relationships && r.resolution <> "resolved"))
                let reason =
                    if before.profile <> after.profile then Some "Parser profiles differ."
                    elif unresolved then Some "Unresolved relationships prevent reliable comparison."
                    elif not incomplete.IsEmpty then Some ("Incomplete extraction: " + String.concat ", " incomplete)
                    else None
                { area = area; status = (if reason.IsSome then "UNKNOWN" else "COMPARED"); reason = reason; changes = if reason.IsSome then [] else build() }
            let entities ir = Domain.components ir |> List.map (fun e -> e.id, e.evidence)
            let deps ir = ir.relations |> List.filter (fun r -> r.kind = "dependsOn") |> List.map (fun r -> $"{r.from} → {r.``to``}", r.evidence)
            let files ir = ir.files |> List.map (fun f -> $"{f.kind}:{f.path}", f.evidence)
            let requirements (ir: DesignIR) = ir.requirements |> List.map (fun r -> r.id, r.evidence)
            let mappings kind subjects =
                ordered subjects |> List.choose (fun subject ->
                    let old = before.relations |> List.filter (fun r -> r.kind = kind && r.from = subject)
                    let current = after.relations |> List.filter (fun r -> r.kind = kind && r.from = subject)
                    let a, b = old |> List.map _.``to`` |> ordered, current |> List.map _.``to`` |> ordered
                    if a = b then None
                    else
                        let reqEvidence = if kind = "realizedBy" then before.requirements @ after.requirements |> List.filter (fun r -> r.id = subject) |> List.collect _.evidence else []
                        Some { kind = "changed"; subject = subject; before = a; after = b; evidence = (old @ current |> List.collect _.evidence) @ reqEvidence })
            let groups = [
                check "Components" ["components"] [] (fun () -> setDiff (entities before) (entities after))
                check "Dependencies" ["components"; "dependencies"] ["dependsOn"] (fun () -> setDiff (deps before) (deps after))
                check "Requirements" ["requirements"] [] (fun () -> setDiff (requirements before) (requirements after))
                check "Requirement Mapping" ["requirements"; "components"; "mappings"; "elements"] ["realizedBy"] (fun () -> mappings "realizedBy" (before.requirements |> List.filter (fun r -> after.requirements |> List.exists (fun a -> a.id = r.id)) |> List.map _.id))
                check "Files" ["files"] [] (fun () -> setDiff (files before) (files after))
                check "Component–File Mapping" ["components"; "files"] ["implementedAt"] (fun () -> mappings "implementedAt" (Domain.components before @ Domain.components after |> List.map _.id))
            ]
            { status = (if groups |> List.exists (fun g -> g.status = "UNKNOWN") then "PARTIAL" else "COMPARED"); groups = groups }
