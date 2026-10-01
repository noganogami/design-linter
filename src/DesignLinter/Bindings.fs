namespace DesignLinter

open System
open System.Security.Cryptography
open System.Text
open System.Text.Json

type InputFingerprint = { requirements: string; design: string; parserVersion: string; profile: string; sourceRevision: string }
type BindingQuote = { document: string; quote: string }
type BindingEntry = {
    id: string; kind: string; from: string; ``to``: string; state: string
    author: string; rationale: string; evidence: BindingQuote list
}
type BindingDocument = { schemaVersion: int; inputs: InputFingerprint; entries: BindingEntry list }
type SupplementalReview = {
    status: string; mechanicalStatus: Status option; entries: BindingEntry list
    errors: string list; checks: Check list; designIR: DesignIR option
}

module Bindings =
    let fingerprint requirements design (profile: Profile) =
        let hash (text: string) = SHA256.HashData(Encoding.UTF8.GetBytes text) |> Convert.ToHexString |> _.ToLowerInvariant()
        { requirements = hash requirements; design = hash design; parserVersion = profile.parserVersion; profile = profile.id; sourceRevision = profile.sourceRevision }

    let read text =
        // Reject misspelled fields instead of silently accepting ignored configuration.
        use json = JsonDocument.Parse(text: string)
        let fields names (element: JsonElement) =
            if element.ValueKind <> JsonValueKind.Object then invalidArg "bindings" "Expected an object."
            let actual = element.EnumerateObject() |> Seq.map _.Name |> Seq.toList
            if actual.Length <> (Set.ofList actual).Count || Set.ofList actual <> Set.ofList names then
                invalidArg "bindings" ("Expected exactly these fields: " + String.concat ", " names)
        fields ["schemaVersion"; "inputs"; "entries"] json.RootElement
        fields ["requirements"; "design"; "parserVersion"; "profile"; "sourceRevision"] (json.RootElement.GetProperty "inputs")
        for entry in (json.RootElement.GetProperty "entries").EnumerateArray() do
            fields ["id"; "kind"; "from"; "to"; "state"; "author"; "rationale"; "evidence"] entry
            for quote in (entry.GetProperty "evidence").EnumerateArray() do fields ["document"; "quote"] quote
        Json.deserialize<BindingDocument> text

    let evaluate requirements design (ir: DesignIR) (document: BindingDocument) =
        let invalid errors status = { status = status; mechanicalStatus = None; entries = document.entries; errors = errors; checks = []; designIR = None }
        if document.schemaVersion <> 1 then invalid ["Unsupported binding schemaVersion."] "INVALID"
        elif document.inputs <> fingerprint requirements design ir.profile then
            invalid ["Input fingerprint changed. Review the bindings against the current documents before updating the fingerprint."] "STALE"
        else
            let errors = ResizeArray<string>()
            let check condition message = if not condition then errors.Add message
            check (not document.entries.IsEmpty) "At least one binding entry is required."
            let nonempty = String.IsNullOrWhiteSpace >> not
            let quotesFor (entry: BindingEntry) =
                entry.evidence |> List.choose (fun q ->
                    let source, file =
                        match q.document with
                        | "requirements" -> requirements, ir.documents.requirements
                        | "design" -> design, ir.documents.design
                        | _ -> "", ""
                    if file = "" || not (nonempty q.quote) then
                        errors.Add $"{entry.id}: quote requires a document (requirements/design) and nonempty text."
                        None
                    else
                        let start = source.IndexOf(q.quote, StringComparison.Ordinal)
                        if start < 0 || source.IndexOf(q.quote, start + 1, StringComparison.Ordinal) >= 0 then
                            errors.Add $"{entry.id}: quote must match exactly once in {q.document}."
                            None
                        elif (start > 0 && source[start - 1] <> '\n') ||
                             (start + q.quote.Length < source.Length && source[start + q.quote.Length] <> '\r' && source[start + q.quote.Length] <> '\n' && not (q.quote.EndsWith('\n'))) then
                            errors.Add $"{entry.id}: quote must contain complete source lines."
                            None
                        else
                            let line = source.AsSpan(0,start).Count('\n') + 1
                            Some { file = file; startLine = line; endLine = line + q.quote.AsSpan().Count('\n'); section = "binding:" + entry.id })
            let covers (quotes: Evidence list) (e: Evidence) =
                quotes |> List.exists (fun q -> q.file = e.file && q.startLine <= e.startLine && q.endLine >= e.endLine)
            check (document.entries |> List.map _.id |> List.distinct |> List.length = document.entries.Length) "Binding IDs must be unique."
            let mutable candidate = ir
            for entry in document.entries do
                check ([entry.id; entry.from; entry.``to``; entry.author; entry.rationale] |> List.forall nonempty) "Binding identifiers, author and rationale must be nonempty."
                check (List.contains entry.state ["proposed"; "accepted"]) $"{entry.id}: state must be proposed or accepted."
                check (not entry.evidence.IsEmpty) $"{entry.id}: evidence is required."
                let quotes = quotesFor entry
                match entry.kind with
                | "referenceAlias" ->
                    let links = ir.relations |> List.filter (fun r -> r.kind = "realizedBy" && r.``to`` = entry.from && r.resolution = "unresolved")
                    let target = ir.entities |> List.tryFind (fun e -> e.id = entry.``to``)
                    check (not links.IsEmpty) $"{entry.id}: from must be an unresolved requirement mapping target."
                    check (links |> List.collect _.evidence |> List.forall (covers quotes)) $"{entry.id}: evidence must cover every occurrence of the source reference."
                    check (target |> Option.exists (fun e -> e.evidence |> List.exists (covers quotes))) $"{entry.id}: evidence must cover an existing target definition."
                    check (target |> Option.exists (fun e -> entry.from.StartsWith(e.kind + ":", StringComparison.Ordinal))) $"{entry.id}: alias must preserve the entity kind."
                    check (document.entries |> List.filter (fun e -> e.kind = entry.kind && e.from = entry.from) |> List.length = 1) $"{entry.id}: conflicting or duplicate alias."
                    let relations = candidate.relations |> List.map (fun r ->
                        if r.kind = "realizedBy" && r.``to`` = entry.from && r.resolution = "unresolved" then
                            { r with ``to`` = entry.``to``; resolution = "resolved"; evidence = r.evidence @ quotes }
                        else r)
                    candidate <- { candidate with relations = relations }
                | "fileMapping" ->
                    let comp = Domain.components ir |> List.tryFind (fun e -> e.id = entry.from)
                    let file = ir.files |> List.tryFind (fun f -> f.path = entry.``to`` && f.kind = "file")
                    check (comp |> Option.exists (fun c -> c.evidence |> List.exists (covers quotes))) $"{entry.id}: evidence must cover an existing component definition."
                    check (file |> Option.exists (fun f -> f.evidence |> List.exists (covers quotes))) $"{entry.id}: evidence must cover an existing concrete file."
                    check (not (candidate.relations |> List.exists (fun r -> r.kind = "implementedAt" && r.from = entry.from && r.``to`` = entry.``to``))) $"{entry.id}: duplicate file mapping."
                    candidate <- { candidate with relations = candidate.relations @ [{kind = "implementedAt"; from = entry.from; ``to`` = entry.``to``; resolution = "resolved"; evidence = quotes}] }
                | _ -> errors.Add $"{entry.id}: kind must be referenceAlias or fileMapping."
            if errors.Count > 0 then invalid (List.ofSeq errors) "INVALID"
            else
                // Only retire ambiguity diagnostics whose whole source row is now resolved.
                let diagnostics = candidate.diagnostics |> List.filter (fun d ->
                    if d.code <> "ambiguous-traceability-target" then true
                    else candidate.relations |> List.exists (fun r -> r.resolution <> "resolved" && r.evidence |> List.exists (fun e -> d.evidence |> List.exists (fun at -> at.file = e.file && at.startLine = e.startLine))))
                let removedAreas = ir.diagnostics |> List.filter (fun d -> not (List.contains d diagnostics)) |> List.map _.area |> List.distinct
                let completeness = removedAreas |> List.fold (fun (state: Map<string,string>) area -> if diagnostics |> List.exists (fun d -> d.area = area) then state else state.Add(area,"complete")) candidate.completeness
                candidate <- { candidate with diagnostics = diagnostics; completeness = completeness }
                let checks = Rules.validate candidate
                { status = "CONDITIONAL"; mechanicalStatus = Some (checks |> List.map _.status |> Domain.aggregate)
                  entries = document.entries; errors = []; checks = checks; designIR = Some candidate }
