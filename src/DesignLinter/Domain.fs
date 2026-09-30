namespace DesignLinter

open System.Text.Json
open System.Text.Json.Serialization

type Status = PASS | FAIL | UNKNOWN | REVIEW_REQUIRED | NOT_APPLICABLE

type Evidence = { file: string; startLine: int; endLine: int; section: string }
type Profile = { id: string; parserVersion: string; sourceRevision: string }
type Documents = { requirements: string; design: string }
type Requirement = { id: string; text: string; evidence: Evidence list }
type Entity = { id: string; name: string; kind: string; evidence: Evidence list; layer: string option; dependencyDeclared: bool option }
type FileEntry = { path: string; kind: string; change: string; evidence: Evidence list }
type Relation = { kind: string; from: string; ``to``: string; resolution: string; evidence: Evidence list }
type RequirementReference = { id: string; evidence: Evidence list }
type Diagnostic = { code: string; area: string; message: string; evidence: Evidence list }
type BoundaryEntry = { name: string; text: string; placeholderOnly: bool; evidence: Evidence list }
type FilePlan = { present: bool; placeholders: Evidence list; evidence: Evidence list }
type DesignIR = {
    profile: Profile
    documents: Documents
    requirements: Requirement list
    entities: Entity list
    files: FileEntry list
    relations: Relation list
    requirementReferences: RequirementReference list
    boundary: BoundaryEntry list
    filePlan: FilePlan
    completeness: Map<string, string>
    diagnostics: Diagnostic list
}
type Finding = { ruleId: string; status: Status; message: string; evidence: Evidence list; source: string }
type Check = { ruleId: string; title: string; status: Status; findings: Finding list; metrics: Map<string, int> option; source: string }

module Domain =
    let profile = { id = "cc-sdd-english-v1"; parserVersion = "0.2.0"; sourceRevision = "e2a0c671aef37a482404905b409ab5c27092b25b" }
    let areas = [ "requirements"; "components"; "mappings"; "files"; "dependencies"; "elements"; "boundary" ]
    let empty documents = {
        profile = profile; documents = documents; requirements = []; entities = []; files = []; relations = []
        requirementReferences = []; boundary = []; filePlan = { present = false; placeholders = []; evidence = [] }
        completeness = areas |> List.map (fun a -> a, "complete") |> Map.ofList; diagnostics = []
    }
    let components (ir: DesignIR) = ir.entities |> List.filter (fun e -> e.kind = "component")
    let entityId kind (name: string) = kind + ":" + name.Trim()
    let aggregate statuses =
        [ FAIL; UNKNOWN; REVIEW_REQUIRED ] |> List.tryFind (fun s -> List.contains s statuses) |> Option.defaultValue PASS
    let gateSource = $"https://github.com/gotalab/cc-sdd/blob/{profile.sourceRevision}/tools/cc-sdd/templates/shared/settings/rules/design-review-gate.md"
    let cycleSource = "User-requested graph analysis; cycles are not universally prohibited by cc-sdd."

// Mutation is confined to extraction; the IR handed to rules is immutable.
type internal Builder(documents) =
    member val IR = Domain.empty documents with get, set
    member this.Diagnose(area, code, message, evidence) =
        this.IR <- { this.IR with completeness = this.IR.completeness.Add(area, "partial"); diagnostics = this.IR.diagnostics @ [ { area = area; code = code; message = message; evidence = evidence } ] }
    member this.AddRelation(relation: Relation) =
        let same (r: Relation) = r.kind = relation.kind && r.from = relation.from && r.``to`` = relation.``to``
        let relations =
            if this.IR.relations |> List.exists same then this.IR.relations |> List.map (fun r -> if same r then { r with evidence = r.evidence @ relation.evidence } else r)
            else this.IR.relations @ [ relation ]
        this.IR <- { this.IR with relations = relations }
    member this.AddEntity(kind, name: string, at, layer: string option) =
        let id = Domain.entityId kind name
        match this.IR.entities |> List.tryFind (fun e -> e.id = id) with
        | Some previous ->
            this.IR <- { this.IR with entities = this.IR.entities |> List.map (fun e -> if e.id = id then { e with evidence = e.evidence @ [ at ] } else e) }
            if previous.layer.IsSome && layer.IsSome && previous.layer <> layer then
                this.Diagnose("components", "conflicting-layer", $"Conflicting layers for {name}", [ at ])
        | None ->
            this.IR <- { this.IR with entities = this.IR.entities @ [ { id = id; name = name.Trim(); kind = kind; evidence = [ at ]; layer = layer; dependencyDeclared = None } ] }
        id
    member this.DeclareDependencies(id) =
        this.IR <- { this.IR with entities = this.IR.entities |> List.map (fun e -> if e.id = id then { e with dependencyDeclared = Some true } else e) }

module Json =
    let options =
        let options = JsonSerializerOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)
        options.Converters.Add(JsonFSharpConverter(unionEncoding = (JsonUnionEncoding.Default ||| JsonUnionEncoding.UnwrapFieldlessTags)))
        options
    let serialize value = JsonSerializer.Serialize(value, options)
    let deserialize<'T> (value: string) = JsonSerializer.Deserialize<'T>(value, options)
