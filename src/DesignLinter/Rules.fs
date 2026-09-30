namespace DesignLinter

open System.Collections.Generic

module Rules =
    type private ResultBuilder(ruleId, title, source) =
        let findings = ResizeArray<Finding>()
        member _.Add(status, message, evidence) = findings.Add { ruleId = ruleId; status = status; message = message; evidence = evidence; source = source }
        member this.Incomplete(ir: DesignIR, areas) =
            for area in areas do
                if ir.completeness[area] <> "complete" then
                    let diagnostics = ir.diagnostics |> List.filter (fun d -> d.area = area)
                    let messages = diagnostics |> List.map _.message |> String.concat " "
                    this.Add(UNKNOWN, $"{area}: extraction {ir.completeness[area]}. {messages}", diagnostics |> List.collect _.evidence)
        member _.Result(metrics) : Check = { ruleId = ruleId; title = title; source = source; status = Domain.aggregate (findings |> Seq.map _.status |> Seq.toList); findings = List.ofSeq findings; metrics = metrics }

    let requirementCoverage (ir: DesignIR) =
        let result = ResultBuilder("M1", "Requirement coverage", Domain.gateSource)
        let areas = ["requirements"; "mappings"; "components"; "elements"]
        result.Incomplete(ir, areas)
        let known = ir.requirements |> List.map _.id |> Set.ofList
        let referenced = ir.requirementReferences |> List.map _.id |> Set.ofList
        let links = ir.relations |> List.filter (fun r -> r.kind = "realizedBy")
        let mapped = links |> List.filter (fun r -> r.resolution = "resolved") |> List.map _.from |> Set.ofList
        let complete = areas |> List.forall (fun a -> ir.completeness[a] = "complete")
        let missingStatus = if complete then FAIL else UNKNOWN
        if known.IsEmpty then result.Add(UNKNOWN, "No requirements extracted; coverage cannot be established.", [])
        for reference in ir.requirementReferences do
            if not (known.Contains reference.id) then result.Add((if ir.completeness["requirements"] = "complete" then FAIL else UNKNOWN), $"Unknown requirement ID: {reference.id}", reference.evidence)
        for req in ir.requirements do
            if not (referenced.Contains req.id) then result.Add(missingStatus, $"Requirement {req.id} is not referenced in a supported design mapping.", req.evidence)
            elif not (mapped.Contains req.id) then result.Add(missingStatus, $"Requirement {req.id} has no resolved design target.", req.evidence)
        for link in links do
            if link.resolution <> "resolved" then result.Add(missingStatus, $"Dangling design target for {link.from}: {link.``to``}", link.evidence)
        result.Result(Some (Map.ofList ["total", known.Count; "referenced", (Set.intersect known referenced).Count; "mapped", (Set.intersect known mapped).Count]))

    let filePlanConsistency (ir: DesignIR) =
        let result = ResultBuilder("M2", "Component / File Structure Plan consistency", Domain.gateSource)
        result.Incomplete(ir, ["files"; "components"])
        let concrete = ir.files |> List.filter (fun f -> f.kind = "file")
        let abstractLayout = ir.files |> List.exists (fun f -> f.kind <> "file")
        if not ir.filePlan.present then result.Add(UNKNOWN, "File Structure Plan is absent or unsupported by this profile.", ir.filePlan.evidence)
        elif ir.files.IsEmpty && ir.completeness["files"] = "complete" then result.Add(FAIL, "File Structure Plan has no concrete paths.", ir.filePlan.evidence)
        for at in ir.filePlan.placeholders do result.Add(FAIL, "File Structure Plan contains an unresolved placeholder.", [at])
        let links = ir.relations |> List.filter (fun r -> r.kind = "implementedAt")
        let mapped = links |> List.filter (fun r -> r.resolution = "resolved" && concrete |> List.exists (fun f -> f.path = r.``to``)) |> List.map _.from |> Set.ofList
        for comp in Domain.components ir do
            if not (mapped.Contains comp.id) then
                let uncertain = abstractLayout || ir.completeness["files"] <> "complete" || ir.completeness["components"] <> "complete"
                result.Add((if uncertain then UNKNOWN else FAIL), $"No explicit concrete file mapping for {comp.name}.", comp.evidence)
        for link in links do
            if link.resolution <> "resolved" then result.Add((if ir.completeness["components"] = "complete" then FAIL else UNKNOWN), $"File plan references an undefined component: {link.from}", link.evidence)
        result.Result(Some (Map.ofList ["total", (Domain.components ir).Length; "mapped", mapped.Count; "files", concrete.Length]))

    let boundaryPopulated (ir: DesignIR) =
        let result = ResultBuilder("M2B", "Boundary fields populated (presence only)", Domain.gateSource)
        result.Incomplete(ir, ["boundary"])
        for field in ["This Spec Owns"; "Out of Boundary"; "Allowed Dependencies"; "Revalidation Triggers"] do
            match ir.boundary |> List.tryFind (fun b -> b.name = field) with
            | Some entry when entry.placeholderOnly -> result.Add(FAIL, $"Boundary field is empty or placeholder-only: {field}", entry.evidence)
            | None when ir.completeness["boundary"] = "complete" -> result.Add(FAIL, $"Missing boundary field: {field}", [])
            | _ -> ()
        result.Result None

    // Tarjan's SCC algorithm; mutation is local to traversal.
    let stronglyConnected (vertices: string list) (edges: (string * string) list) =
        let adjacency = vertices |> List.map (fun v -> v, (edges |> List.choose (fun (a,b) -> if a = v && List.contains b vertices then Some b else None))) |> Map.ofList
        let index, low = Dictionary<string,int>(), Dictionary<string,int>()
        let stack, active, groups = Stack<string>(), HashSet<string>(), ResizeArray<string list>()
        let mutable next = 0
        let rec visit v =
            index[v] <- next; low[v] <- next; next <- next + 1
            stack.Push v; active.Add v |> ignore
            for w in adjacency[v] do
                if not (index.ContainsKey w) then visit w; low[v] <- min low[v] low[w]
                elif active.Contains w then low[v] <- min low[v] index[w]
            if low[v] = index[v] then
                let group = ResizeArray<string>()
                let mutable finished = false
                while not finished do
                    let w = stack.Pop()
                    active.Remove w |> ignore
                    group.Add w
                    finished <- w = v
                groups.Add(group |> Seq.sort |> Seq.toList)
        for v in List.sort vertices do if not (index.ContainsKey v) then visit v
        groups |> Seq.filter (fun g -> g.Length > 1 || List.contains g.Head adjacency[g.Head]) |> Seq.toList

    let private representativeCycle group edges =
        let start = List.head group
        let rec search v path =
            edges |> List.filter (fun (a,b) -> a = v && List.contains b group) |> List.sort |> List.tryPick (fun (_, w) ->
                if w = start then Some (path @ [start])
                elif List.contains w path then None
                else search w (path @ [w]))
        search start [start] |> Option.defaultValue []

    let circularDependencies (ir: DesignIR) =
        let result = ResultBuilder("M3", "Circular dependencies (extracted graph only)", Domain.cycleSource)
        result.Incomplete(ir, ["dependencies"; "components"])
        let ids = Domain.components ir |> List.map _.id
        let relations = ir.relations |> List.filter (fun r -> r.kind = "dependsOn" && r.resolution = "resolved" && List.contains r.from ids && List.contains r.``to`` ids)
        let edges = relations |> List.map (fun r -> r.from, r.``to``)
        let groups = stronglyConnected ids edges
        for group in groups do
            let cycle = representativeCycle group edges |> List.map (fun s -> s.Substring("component:".Length)) |> String.concat " → "
            let evidence = relations |> List.filter (fun r -> List.contains r.from group && List.contains r.``to`` group) |> List.collect _.evidence
            result.Add(REVIEW_REQUIRED, $"Cycle detected; confirm whether it is allowed: {cycle}", evidence)
        result.Result(Some (Map.ofList ["components", ids.Length; "edges", edges.Length; "cycles", groups.Length]))

    let validate ir = [requirementCoverage ir; filePlanConsistency ir; boundaryPopulated ir; circularDependencies ir]
