namespace DesignLinter

open System
open System.Collections.Generic
open System.Text.RegularExpressions

module Parser =
    open Md

    let private parseRequirements source file (b: Builder) =
        let root = Md.parse source file
        let areas = sections root |> List.filter (fun s -> matchesI @"^(?:Requirement\s+\S+|\d+[. :]\s*)" s.title)
        for area in areas do
            let m = rxi @"^(?:Requirement\s+)?(\d+)(?:\s*[:.]|\s|$)" area.title
            if not m.Success then b.Diagnose("requirements", "invalid-requirement-heading", $"Numeric requirement heading required: {area.title}", [area.evidence])
            else
                let number = group 1 m
                let criteria = area.children |> Seq.filter (fun s -> matchesI "^Acceptance Criteria$" s.title) |> Seq.toList
                let count = b.IR.requirements.Length
                if criteria.IsEmpty then b.Diagnose("requirements", "missing-criteria", $"Acceptance Criteria not recognized for Requirement {number}", [area.evidence])
                for section in criteria do
                    for node in section.nodes do
                        if node.kind <> "html" then
                            if node.kind <> "list" || not node.ordered then
                                b.Diagnose("requirements", "unsupported-criteria", "Acceptance criteria must be an ordered list in this profile.", [evidence file section.title node])
                            else
                                for index, item in List.indexed node.children do
                                    let id = $"{number}.{node.start + index}"
                                    let at = evidence file area.title item
                                    if b.IR.requirements |> List.exists (fun r -> r.id = id) then b.Diagnose("requirements", "duplicate-requirement", $"Duplicate requirement {id}", [at])
                                    else b.IR <- { b.IR with requirements = b.IR.requirements @ [{ id = id; text = text item; evidence = [at] }] }
                    if section.children.Count > 0 then b.Diagnose("requirements", "nested-criteria", "Nested acceptance criteria headings are not supported.", section.children |> Seq.map _.evidence |> Seq.toList)
                if not criteria.IsEmpty && count = b.IR.requirements.Length then b.Diagnose("requirements", "empty-criteria", $"No unique acceptance criteria extracted for Requirement {number}", [area.evidence])
        for parent in sections root |> List.filter (fun s -> s.title = "Requirements") do
            for child in parent.children do
                if not (areas |> List.exists (fun a -> obj.ReferenceEquals(a, child))) then b.Diagnose("requirements", "unsupported-requirement-heading", $"Unrecognized requirement heading: {child.title}", [child.evidence])
        if b.IR.requirements.IsEmpty then b.Diagnose("requirements", "no-requirements", "No canonical requirements extracted.", [root.evidence])

    let private parseDesign source file (b: Builder) =
        let root = Md.parse source file
        let flat = sections root
        let find title = flat |> List.filter (fun s -> String.Equals(s.title, title, StringComparison.OrdinalIgnoreCase))
        let addEntity kind name at layer = b.AddEntity(kind, name, at, layer)
        let references value at =
            [ for token in names value do
                let range = rx @"^([1-9][0-9]*)\.([1-9][0-9]*)\s*[-–]\s*([1-9][0-9]*)\.([1-9][0-9]*)$" token
                let ids =
                    if matches @"^[1-9][0-9]*\.[1-9][0-9]*$" token then [token]
                    elif range.Success then
                        match Int32.TryParse(group 2 range), Int32.TryParse(group 4 range) with
                        | (true, first), (true, last) when group 1 range = group 3 range && first <= last && int64 last - int64 first < 1000L ->
                            [ for n in first .. last -> $"{group 1 range}.{n}" ]
                        | _ ->
                            b.Diagnose("mappings", "unsupported-requirement-range", $"Range must ascend within one parent and contain at most 1000 IDs: {token}", [at])
                            []
                    else
                        b.Diagnose("mappings", "ambiguous-requirement-reference", $"Expected canonical N.M ID or N.M–N.K range, found: {token}", [at])
                        []
                for id in ids do
                    b.IR <- { b.IR with requirementReferences = b.IR.requirementReferences @ [{ id = id; evidence = [at] }] }
                    yield id ] |> List.distinct
        let mapping ids target at =
            for id in ids do b.AddRelation { kind = "realizedBy"; from = id; ``to`` = target; resolution = "unresolved"; evidence = [at] }
        let declarations = Dictionary<string, ResizeArray<string list * Evidence>>()
        let recordCoverage id value at =
            let ids = references value at
            mapping ids id at
            if not (declarations.ContainsKey id) then declarations[id] <- ResizeArray()
            declarations[id].Add(List.sort (List.distinct ids), at)
        let noneDeclarations = Dictionary<string, Evidence list>()
        let pending = ResizeArray<string * string * Evidence>()
        let details = ResizeArray<Section * string>()
        let summaries, detailIds = HashSet<string>(), HashSet<string>()
        let containers = find "Components and Interfaces" @ find "Components & Interfaces"
        // Read explicit names first so heading normalization is independent of section order.
        let summaryRow (cells: Map<string,string>) =
            cells.ContainsKey "component" && not (cells.ContainsKey "direction" || cells.ContainsKey "type")
        let summaryNames =
            containers |> List.collect (fun c -> c :: sections c)
            |> List.collect (fun s -> s.nodes |> Seq.filter (fun n -> n.kind = "table") |> Seq.collect rows |> Seq.toList)
            |> List.choose (fun (cells, _) -> if summaryRow cells then Some (cell "component" cells) else None)
            |> Set.ofList
        let componentName title =
            if summaryNames.Contains title then title
            else
                // Only a concrete path annotation and an exact summary name establish an alias.
                let m = rx @"^(.+?)\s*(?:\(([A-Za-z0-9_./@-]+\.[A-Za-z0-9_-]+)\)|（([A-Za-z0-9_./@-]+\.[A-Za-z0-9_-]+)）)$" title
                let name = (group 1 m).Trim()
                if m.Success && summaryNames.Contains name then name else title
        for container in containers do
            for section in container :: sections container do
                let fields = section.nodes |> Seq.filter (fun n -> n.kind = "table") |> Seq.collect rows |> Seq.toList
                let fieldRows = fields |> List.filter (fun (c, _) -> c.ContainsKey "field" && c.ContainsKey "detail")
                let isDetail = section.depth = 4 || fieldRows |> List.exists (fun (c, _) -> matchesI "^intent|requirements$" (cell "field" c))
                let placeholderName = isDetail && onlyPlaceholder section.title
                if placeholderName then b.Diagnose("components", "placeholder-component", "Component name is a placeholder.", [section.evidence])
                else
                    if isDetail then
                        let id = addEntity "component" (componentName section.title) section.evidence None
                        if not (detailIds.Add id) then b.Diagnose("components", "duplicate-component", $"Duplicate detailed definition: {section.title}", [section.evidence])
                        details.Add(section, id)
                        for cells, node in fieldRows do
                            if matchesI "^requirements$" (cell "field" cells) then recordCoverage id (cell "detail" cells) (evidence file section.title node)
                    // Dependency tables are references, not component definitions.
                    for cells, node in fields |> List.filter (fun (c, _) -> summaryRow c) do
                        let name, at = cell "component" cells, evidence file section.title node
                        if onlyPlaceholder name then b.Diagnose("components", "empty-component", "Empty or placeholder component name.", [at])
                        else
                            let id = addEntity "component" name at (firstCell ["domain/layer"; "domain"] cells)
                            if not (summaries.Add id) then b.Diagnose("components", "duplicate-summary", $"Duplicate component summary: {name}", [at])
                            match firstCell ["req coverage"; "requirement coverage"; "requirements"] cells with
                            | Some coverage -> recordCoverage id coverage at
                            | None -> b.Diagnose("mappings", "missing-coverage-column", $"No recognized requirement coverage column for {name}", [at])
                            match firstCell ["key dependencies (p0/p1)"; "key dependencies"; "dependencies"] cells with
                            | Some deps when isNone deps -> b.DeclareDependencies id; noneDeclarations[id] <- [at]
                            | Some deps -> for target in names deps do pending.Add(id, target, at)
                            | None -> ()
                    for node in section.nodes |> Seq.filter (fun n -> n.kind = "list") do
                        for item in items node do
                            let m = rxi @"^Component:\s*([^;—]+)(?:\s*[;—]\s*Requirements:\s*([^;]+))?" (text item)
                            let at = evidence file section.title item
                            if m.Success then
                                let id = addEntity "component" ((group 1 m).Trim()) at None
                                if group 2 m <> "" then recordCoverage id (group 2 m) at
                                else b.Diagnose("mappings", "missing-compact-coverage", "Compact component requires explicit Requirements field.", [at])
                            elif section.depth <= 3 then b.Diagnose("components", "unsupported-component-list", "Compact component lists require explicit Component: and Requirements: fields in this profile.", [at])
                    for table in section.nodes |> Seq.filter (fun n -> n.kind = "table") do
                        if section.depth <= 3 && not (rows table |> List.exists (fun (c, _) -> c.ContainsKey "component" || c.ContainsKey "field")) then
                            b.Diagnose("components", "unsupported-component-table", "Unrecognized component summary table.", [evidence file section.title table])
        for KeyValue(id, defs) in declarations do
            if defs |> Seq.map fst |> Seq.distinct |> Seq.length > 1 then b.Diagnose("mappings", "conflicting-coverage", $"Summary and detailed requirement declarations disagree for {id}.", defs |> Seq.map snd |> Seq.toList)
        if containers.IsEmpty || (Domain.components b.IR).IsEmpty then b.Diagnose("components", "no-components", "No supported component definitions found.", [root.evidence])

        for section, id in details do
            let mutable inDependencies = false
            let declaration value at =
                let m = rxi @"^(Inbound|Outbound|External):\s*(.*)$" value
                if not m.Success then false
                else
                    b.DeclareDependencies id
                    let direction = (group 1 m).ToLowerInvariant()
                    let targetText = (Regex.Split(group 2 m, @"\s+[—–]\s+|\s+-\s+")[0]).Trim()
                    if targetText = "" then b.Diagnose("dependencies", "empty-dependency", $"Empty dependency in {section.title}", [at])
                    elif not (isNone targetText) then
                        for target in names targetText do
                            let targetId = if direction = "external" then addEntity "external" target at None else Domain.entityId "component" target
                            b.AddRelation { kind = "dependsOn"; from = (if direction = "inbound" then targetId else id); ``to`` = (if direction = "inbound" then id else targetId); resolution = "unresolved"; evidence = [at] }
                    true
            for node in section.nodes do
                let value = (text node).Trim()
                if node.kind = "paragraph" && matchesI @"^Dependencies(?:\s*:\s*.*)?$" value then
                    inDependencies <- true
                    if matchesI @"^Dependencies:\s*(none|なし|n/a)$" value then b.DeclareDependencies id; noneDeclarations[id] <- [evidence file section.title node]
                else
                    if node.kind = "paragraph" && matchesI "^(Contracts|Responsibilities|Implementation)" value then inDependencies <- false
                    if node.kind = "list" then
                        for item in items node do
                            let at = evidence file section.title item
                            if not (declaration ((text item).Trim()) at) && inDependencies then b.Diagnose("dependencies", "unsupported-dependency", "Unrecognized dependency declaration.", [at])
                    if inDependencies && node.kind = "table" then
                        for cells, row in rows node do
                            let direction = firstCell ["direction"; "type"] cells |> Option.defaultValue ""
                            let target = firstCell ["component"; "dependency"; "target"] cells |> Option.defaultValue ""
                            let at = evidence file section.title row
                            if direction = "" || target = "" || not (declaration $"{direction}: {target}" at) then b.Diagnose("dependencies", "unsupported-dependency-table", "Dependency table requires Direction and Component columns.", [at])
            for child in sections section do
                if matches "Contract|Interface" child.title then
                    let kind = if child.title.Contains "Interface" then "interface" else "contract"
                    let contractId = addEntity kind $"{componentName section.title} {child.title}" child.evidence None
                    b.AddRelation { kind = "providedBy"; from = contractId; ``to`` = id; resolution = "resolved"; evidence = [child.evidence] }
                    for node in child.nodes |> Seq.filter (fun n -> n.kind = "code") do
                        for m in Regex.Matches(node.value, @"\binterface\s+([A-Za-z_$][\w$]*)") do
                            let at = evidence file child.title node
                            let interfaceId = addEntity "interface" (group 1 m) at None
                            b.AddRelation { kind = "providedBy"; from = interfaceId; ``to`` = id; resolution = "resolved"; evidence = [at] }
        for KeyValue(id, at) in noneDeclarations do
            if b.IR.relations |> List.exists (fun r -> r.kind = "dependsOn" && (r.from = id || r.``to`` = id)) then b.Diagnose("dependencies", "conflicting-dependencies", $"Explicit None conflicts with dependency declarations for {id}.", at)
        for owner, target, at in pending do
            let targetId, externalId = Domain.entityId "component" target, Domain.entityId "external" target
            if not (b.IR.relations |> List.exists (fun r -> r.kind = "dependsOn" && ((r.from = owner && (r.``to`` = targetId || r.``to`` = externalId)) || (r.``to`` = owner && r.from = targetId)))) then
                b.Diagnose("dependencies", "dependency-direction-unknown", $"Direction unknown: {owner} / {target}", [at])
        for c in Domain.components b.IR do
            if c.dependencyDeclared <> Some true then b.Diagnose("dependencies", "dependencies-undeclared", $"Dependencies not explicitly declared for {c.name}", c.evidence)

        for title, kind in ["System Flows", "flow"; "Data Models", "dataModel"; "Operational Decisions", "decision"] do
            for parent in find title do
                for child in sections parent do addEntity kind child.title child.evidence None |> ignore
                if parent.children.Count = 0 && parent.nodes |> Seq.exists (fun n -> n.kind <> "html") then b.Diagnose("elements", "unnamed-design-elements", $"{title} has no named subsections; references to its elements cannot be resolved reliably.", [parent.evidence])
        for section in find "Requirements Traceability" do
            for node in section.nodes do
                if node.kind = "table" then
                    for cells, row in rows node do
                        let at = evidence file section.title row
                        if not (cells.ContainsKey "requirement") then b.Diagnose("mappings", "unsupported-traceability", "Traceability table has no Requirement column.", [at])
                        else
                            let ids = references (cell "requirement" cells) at
                            for column, kind in ["components", "component"; "interfaces", "interface"; "flows", "flow"; "contracts", "contract"; "data models", "dataModel"; "operational decisions", "decision"] do
                                for name in names (cell column cells) do
                                    let target = Domain.entityId kind name
                                    mapping ids target at
                                    if kind <> "component" && not (b.IR.entities |> List.exists (fun e -> e.id = target)) then
                                        b.Diagnose("mappings", "ambiguous-traceability-target", $"Unresolved {column} cell may be a reference or description: {name}", [at])
                if node.kind = "list" then
                    for item in items node do
                        let m = rx @"^([0-9]+(?:\.[0-9]+)?(?:\s*[-–]\s*[0-9]+\.[0-9]+)?)\s*(?:->|→|:)\s*(.+)$" (text item)
                        let at = evidence file section.title item
                        if not m.Success then b.Diagnose("mappings", "unsupported-traceability-bullet", "Unrecognized traceability bullet.", [at])
                        else
                            for target in names (group 2 m) do mapping (references (group 1 m) at) (Domain.entityId "component" target) at
                if not (List.contains node.kind ["table"; "list"; "html"; "blockquote"]) then b.Diagnose("mappings", "unsupported-traceability-body", "Traceability prose is not automatically interpreted.", [evidence file section.title node])

        let normalizePath (path: string) =
            let parts = ResizeArray<string>()
            for part in path.Split('/') do
                match part with
                | "" | "." -> ()
                | ".." when parts.Count > 0 && parts[parts.Count - 1] <> ".." -> parts.RemoveAt(parts.Count - 1)
                | ".." when path.StartsWith('/') -> ()
                | p -> parts.Add p
            let prefix = if path.StartsWith('/') then "/" else ""
            let joined = prefix + String.concat "/" parts
            let joined = if joined = "" then "." else joined
            if path.EndsWith('/') && not (joined.EndsWith('/')) then joined + "/" else joined
        let addFile (path: string) at modified owners =
            let cleaned = path.Trim('`').Trim()
            if cleaned <> "" && not (placeholder cleaned) then
                if not (matches @"^[A-Za-z0-9_.@{}*/-]+$" cleaned) then b.Diagnose("files", "unsupported-path", $"Path syntax is unsupported: {cleaned}", [at])
                else
                    let kind = if matches "[*{}]" cleaned then "pattern" elif cleaned.EndsWith('/') then "directory" else "file"
                    let normalized = normalizePath cleaned
                    let files =
                        if b.IR.files |> List.exists (fun f -> f.path = normalized) then b.IR.files |> List.map (fun f -> if f.path = normalized then { f with evidence = f.evidence @ [at] } else f)
                        else b.IR.files @ [{ path = normalized; kind = kind; change = (if modified then "modified" else "unspecified"); evidence = [at] }]
                    b.IR <- { b.IR with files = files }
                    for owner in owners do b.AddRelation { kind = "implementedAt"; from = Domain.entityId "component" owner; ``to`` = normalized; resolution = "unresolved"; evidence = [at] }
        let owners (description: string) =
            let m = rxi @"Components?:\s*(.+?)(?:\s+[—–]\s+|$)" description
            if m.Success then names (group 1 m)
            else Domain.components b.IR |> List.filter (fun c -> description = c.name || description.StartsWith(c.name + " — ") || description.StartsWith(c.name + " - ")) |> List.map _.name
        let plans = find "File Structure Plan"
        b.IR <- { b.IR with filePlan = { b.IR.filePlan with present = not plans.IsEmpty; evidence = plans |> List.map _.evidence } }
        for plan in plans do
            for section in plan :: sections plan do
                let modified = section.title = "Modified Files"
                for node in section.nodes do
                    if node.kind <> "html" && node.kind <> "blockquote" then
                        if placeholder (text node) then b.IR <- { b.IR with filePlan = { b.IR.filePlan with placeholders = b.IR.filePlan.placeholders @ [evidence file section.title node] } }
                        if node.kind = "table" then
                            for cells, row in rows node do
                                let at = evidence file section.title row
                                match firstCell ["path"; "file"; "directory"] cells with
                                | Some path when path <> "" -> addFile path at modified (firstCell ["component"; "components"] cells |> Option.defaultValue "" |> names)
                                | _ -> b.Diagnose("files", "unsupported-file-table", "File table requires Path, File or Directory column.", [at])
                        if node.kind = "code" then
                            if not (List.contains node.lang ["text"; "plaintext"; ""]) then b.Diagnose("files", "unsupported-file-code", "Only plain file trees are supported.", [evidence file section.title node])
                            else
                                let parents = ResizeArray<string>()
                                for lineIndex, line in node.value.Split('\n') |> Array.indexed do
                                    if not (String.IsNullOrWhiteSpace line) then
                                        let m = rx @"^(.*?)(?:├── |└── )([^#]+?)(?:\s+#\s*(.*))?$" line
                                        let simple = rx @"^\s*([^#]+?)(?:\s+#\s*(.*))?$" line
                                        if simple.Success then
                                            let entry = (if m.Success then group 2 m else group 1 simple).Trim()
                                            if not (matches @"^[A-Za-z0-9_.@{}*/-]+$" entry) then b.Diagnose("files", "unsupported-file-tree", $"Unrecognized tree entry: {entry}", [evidence file section.title node])
                                            else
                                                let level = if m.Success then (group 1 m).Length / 4 + 1 else 0
                                                let path = (parents |> Seq.truncate level |> String.concat "") + entry
                                                if entry.EndsWith('/') then
                                                    while parents.Count <= level do parents.Add ""
                                                    parents[level] <- entry
                                                    while parents.Count > level + 1 do parents.RemoveAt(parents.Count - 1)
                                                let at = { evidence file section.title node with startLine = node.startLine + lineIndex + 1; endLine = node.startLine + lineIndex + 1 }
                                                let description = (if m.Success then group 3 m else group 2 simple).Trim()
                                                addFile path at modified (owners description)
                        if node.kind = "list" then
                            for item in items node do
                                let value = text item
                                let m = rx @"^([A-Za-z0-9_.@{}*/-]+)\s*(?:[—–-]\s*(.*))?$" value
                                if m.Success && ((group 1 m).Contains('/') || (group 1 m).Contains('.')) then addFile (group 1 m) (evidence file section.title item) modified (owners ((group 2 m).Trim()))
                                elif not (placeholder value) then b.Diagnose("files", "unsupported-file-entry", $"Unrecognized file entry: {value}", [evidence file section.title item])
                        if node.kind = "paragraph" && not (onlyPlaceholder (text node)) then b.Diagnose("files", "file-plan-prose", "File plan prose/pattern needs manual interpretation; explicit table, list or tree mappings can still be checked.", [evidence file section.title node])
        if plans.IsEmpty then b.Diagnose("files", "missing-file-plan", "File Structure Plan section not recognized.", [root.evidence])
        for section in find "Boundary Commitments" do
            for title in ["This Spec Owns"; "Out of Boundary"; "Allowed Dependencies"; "Revalidation Triggers"] do
                let child = section.children |> Seq.tryFind (fun s -> s.title = title)
                let fragments = child |> Option.map (allNodes >> List.filter (fun n -> n.kind <> "html") >> List.collect (items >> List.map text) >> List.filter (String.IsNullOrWhiteSpace >> not)) |> Option.defaultValue []
                let entry = { name = title; text = String.concat "\n" fragments; placeholderOnly = fragments.IsEmpty || List.forall onlyPlaceholder fragments; evidence = [child |> Option.map _.evidence |> Option.defaultValue section.evidence] }
                b.IR <- { b.IR with boundary = b.IR.boundary @ [entry] }
        if b.IR.boundary.IsEmpty then b.Diagnose("boundary", "missing-boundary", "Boundary Commitments section not recognized.", [root.evidence])
        let ids = b.IR.entities |> List.map _.id |> Set.ofList
        let resolved = b.IR.relations |> List.map (fun r ->
            let exists =
                match r.kind with
                | "implementedAt" -> ids.Contains r.from && b.IR.files |> List.exists (fun f -> f.path = r.``to``)
                | "realizedBy" -> ids.Contains r.``to``
                | _ -> ids.Contains r.from && ids.Contains r.``to``
            { r with resolution = if exists then "resolved" else "unresolved" })
        b.IR <- { b.IR with relations = resolved }
        for relation in resolved do
            if relation.kind = "dependsOn" && relation.resolution <> "resolved" then b.Diagnose("dependencies", "unresolved-dependency", $"Undefined or unclassified dependency: {relation.from} → {relation.``to``}", relation.evidence)

    let parseWithPaths requirements design paths =
        let b = Builder paths
        parseRequirements requirements paths.requirements b
        parseDesign design paths.design b
        b.IR

    let parse requirements design = parseWithPaths requirements design { requirements = "requirements.md"; design = "design.md" }
