namespace DesignLinter

open System
open System.Text.RegularExpressions
open System.Text.Json.Serialization

type Focus = { question: string; reason: string; evidence: Evidence list; detailRefs: string list }
type FocusResult = { items: Focus list; omitted: int }
type InputDocuments(current: Documents, baseline: Documents option) =
    member _.current = current
    [<JsonIgnore(Condition = JsonIgnoreCondition.Never)>]
    member _.baseline = baseline
type Counts = { fail: int; unknown: int; reviewRequired: int }
type AiReview = { status: string; eligible: bool }
type ReviewReport = {
    schemaVersion: int; profile: Profile; inputs: InputDocuments; status: Status; counts: Counts
    checks: Check list; analysis: Map<string,string>; diagnostics: Diagnostic list; designIR: DesignIR
    semanticDiff: SemanticDiff; humanReviewFocus: FocusResult; aiReview: AiReview; limitations: string list
}

module Report =
    let limitations = [
        "Mechanical PASS is not design approval or a complete cc-sdd review gate result."
        "Dependency direction, prohibited dependencies, interface quality, boundary meaning and code conformance are not validated."
        "Semantic diff covers extracted structure only; responsibility prose, trade-offs and contract content may change without a structural diff."
        "Only the pinned English-heading profile is supported; local rule customizations are not automatically applied."
    ]
    let focus (checks: Check list) (diff: SemanticDiff) =
        let candidates = ResizeArray<Focus>()
        for check in checks |> List.sortBy (fun c -> if c.status = FAIL then 0 else 1) do
            if not check.findings.IsEmpty then
                let question =
                    match check.ruleId with
                    | "M1" -> "未対応・未解決の要件参照を、どの設計要素に対応させるべきですか？"
                    | "M2" -> "Component と具体的なファイルの対応を、この記述から確定できますか？"
                    | "M2B" -> "未記入または未解析の責務境界を、具体的に示せますか？"
                    | "M3" when check.findings |> List.exists (fun f -> f.status = REVIEW_REQUIRED) -> "検出した依存循環は、この設計の制約と責務境界で許容されますか？"
                    | "M3" -> "依存の方向・対象を明示して、検証可能にできますか？"
                    | _ -> check.title
                candidates.Add { question = question; reason = $"{check.status}: {check.findings.Length} findings. {check.findings.Head.message}"; evidence = check.findings |> List.collect _.evidence; detailRefs = [check.ruleId] }
        for group in diff.groups |> List.filter (fun g -> g.status = "UNKNOWN") do
            candidates.Add { question = $"{group.area} の変更前後を比較できるよう、未解析の記述を確認できますか？"; reason = group.reason |> Option.defaultValue ""; evidence = []; detailRefs = ["diff:" + group.area] }
        let display values = if List.isEmpty values then "∅" else String.concat ", " values
        for area in ["Requirement Mapping"; "Dependencies"; "Components"; "Component–File Mapping"; "Requirements"; "Files"] do
            match diff.groups |> List.tryFind (fun g -> g.area = area && not g.changes.IsEmpty) with
            | None -> ()
            | Some group ->
                let question =
                    match area with
                    | "Requirement Mapping" -> "要件の対応先を変更した解釈と責務分割は妥当ですか？"
                    | "Dependencies" -> "変更された依存は、許可された境界と依存方向に沿っていますか？"
                    | "Components" -> "追加・削除した Component の責務と抽象化の粒度は妥当ですか？"
                    | "Requirements" -> "要件の追加・削除による対象範囲の変更は意図どおりですか？"
                    | _ -> "変更されたファイル配置は、Component の責務境界に沿っていますか？"
                let reason = group.changes |> List.map (fun c ->
                    if c.kind = "changed" then $"{c.subject}: {display c.before} → {display c.after}"
                    else (if c.kind = "added" then "+ " else "− ") + c.subject) |> String.concat "; "
                candidates.Add { question = question; reason = reason; evidence = group.changes |> List.collect _.evidence; detailRefs = ["diff:" + area] }
        { items = candidates |> Seq.truncate 3 |> Seq.map (fun f -> { f with reason = if f.reason.Length > 300 then f.reason[..296] + "…" else f.reason }) |> Seq.toList
          omitted = max 0 (candidates.Count - 3) }

    let build (ir: DesignIR) before =
        let checks = Rules.validate ir
        let diff = SemanticDiff.compare before ir
        let findings = checks |> List.collect _.findings
        let status = checks |> List.map _.status |> Domain.aggregate
        { schemaVersion = 1; profile = ir.profile; inputs = InputDocuments(ir.documents, before |> Option.map _.documents)
          status = status
          counts = { fail = findings |> List.filter (fun f -> f.status = FAIL) |> List.length; unknown = findings |> List.filter (fun f -> f.status = UNKNOWN) |> List.length; reviewRequired = findings |> List.filter (fun f -> f.status = REVIEW_REQUIRED) |> List.length }
          checks = checks; analysis = ir.completeness; diagnostics = ir.diagnostics; designIR = ir; semanticDiff = diff
          humanReviewFocus = focus checks diff; aiReview = { status = "NOT_RUN"; eligible = status = PASS }; limitations = limitations }

    let private escape (value: string) = Regex.Replace(value, @"[\\`*_\[\]<>|]", "\\$0").Replace("\r\n", " ").Replace("\n", " ")
    let evidenceLinks (evidence: Evidence list) =
        evidence |> List.distinctBy (fun e -> e.file, e.startLine) |> List.map (fun e ->
            let path = e.file.Replace("%", "%25").Replace(" ", "%20").Replace("#", "%23").Replace("<", "%3C").Replace(">", "%3E")
            $"[{escape e.file}:{e.startLine}](<{path}#L{e.startLine}>)") |> String.concat ", "
    let markdown (report: ReviewReport) =
        let lines = ResizeArray<string>()
        let add values = lines.AddRange values
        add ["# Design Review Summary"; ""; $"Mechanical: **{report.status}**"; ""; $"FAIL: {report.counts.fail} / UNKNOWN: {report.counts.unknown} / REVIEW_REQUIRED: {report.counts.reviewRequired}"; ""; "## Human Review Focus"; ""]
        for i, f in List.indexed report.humanReviewFocus.items do
            let links = evidenceLinks (List.truncate 3 f.evidence)
            let links = if links = "" then "See diagnostics and input documents." else links
            add [$"{i + 1}. {escape f.question}"; "   - " + escape f.reason; "   - Evidence: " + links; "   - Details: " + (f.detailRefs |> List.map escape |> String.concat ", "); ""]
        if report.humanReviewFocus.items.IsEmpty then add ["構造上の根拠から追加の確認項目は生成されませんでした。意味的レビューは未実施です。"; ""]
        if report.humanReviewFocus.omitted > 0 then add [$"Additional focus groups: {report.humanReviewFocus.omitted}. All findings and changes remain below."; ""]
        add ["## Mechanical Checks"; ""]
        for check in report.checks do
            add [$"- **{check.status} {escape check.title}** ({check.ruleId})"]
            match check.metrics with
            | Some metrics -> add ["  - " + (metrics |> Map.toList |> List.map (fun (k,v) -> $"{k}: {v}") |> String.concat " / ")]
            | None -> ()
        add [""; "## Semantic Changes"; ""; "Status: " + report.semanticDiff.status; ""]
        for group in report.semanticDiff.groups do
            add [""; "### " + group.area; ""]
            if group.status = "UNKNOWN" then add ["UNKNOWN: " + escape (Option.defaultValue "" group.reason); ""]
            elif group.changes.IsEmpty then add ["No extracted structural changes."; ""]
            else
                for change in group.changes do
                    let sign = match change.kind with "added" -> "+" | "removed" -> "−" | _ -> "~"
                    add [$"- {sign} {escape change.subject}"]
                    if change.kind = "changed" then
                        let display values = if List.isEmpty values then "∅" else String.concat ", " values |> escape
                        add ["  - before: " + display change.before; "  - after: " + display change.after]
                    add ["  - " + evidenceLinks change.evidence]
        add [""; "## Detailed Findings"; ""]
        for check in report.checks do
            for finding in check.findings do
                let links = evidenceLinks finding.evidence
                add [$"- **{finding.status} {finding.ruleId}** {escape finding.message}"; "  - Evidence: " + (if links = "" then "Document-level finding." else links); "  - Source: " + finding.source; ""]
        add ["## Parse Diagnostics"; ""]
        for d in report.diagnostics do add [$"- {escape d.area} / {escape d.code}: {escape d.message}"; "  - " + evidenceLinks d.evidence]
        add [""; "## AI Review"; ""; $"NOT_RUN (eligible after mechanical checks: {report.aiReview.eligible.ToString().ToLowerInvariant()}). No LLM judgment was performed."; ""; "## Analysis Scope"; ""; $"Profile: {report.profile.id} / Parser: {report.profile.parserVersion} / cc-sdd: {report.profile.sourceRevision}"; ""]
        for KeyValue(area, state) in report.analysis do add [$"- {area}: {state}"]
        add [""]
        add (report.limitations |> List.map (fun l -> "- " + l))
        add [""]
        String.concat "\n" lines
