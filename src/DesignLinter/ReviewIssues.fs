namespace DesignLinter

open System
open System.Security.Cryptography
open System.Text

type SourceExcerpt = { evidence: Evidence; text: string }
type ReviewIssue = {
    id: string; category: string; area: string; messages: string list
    ruleIds: string list; affectedRequirements: string list; targets: string list
    evidence: Evidence list; excerpts: SourceExcerpt list; question: string
}

module ReviewIssues =
    let private rules = function
        | "requirements" | "mappings" | "elements" -> ["M1"]
        | "components" -> ["M1"; "M2"; "M3"]
        | "files" -> ["M2"]
        | "dependencies" -> ["M3"]
        | "boundary" -> ["M2B"]
        | _ -> []
    let private category (code: string) =
        if code.StartsWith("conflicting-") || code.StartsWith("duplicate-") then "CONFLICT"
        elif code.StartsWith("ambiguous-") || code = "dependency-direction-unknown" || code = "file-plan-prose" then "AMBIGUOUS"
        elif code.StartsWith("missing-") || code.StartsWith("no-") || code.StartsWith("empty-") || code = "dependencies-undeclared" then "MISSING_INFORMATION"
        elif code = "unresolved-dependency" || code = "unresolved-reference" then "UNRESOLVED_REFERENCE"
        else "UNSUPPORTED_SYNTAX"
    let private samePlace (a: Evidence) (b: Evidence) = a.file = b.file && a.startLine = b.startLine && a.endLine = b.endLine
    let private question = function
        | "CONFLICT" -> "どの定義が正しいですか？矛盾する原文と影響範囲を確認してください。"
        | "MISSING_INFORMATION" -> "必要な情報は文書のどこにありますか？未記載なら設計判断として補ってください。"
        | "AMBIGUOUS" -> "この記述の解釈と対応先を、文書中の根拠で示せますか？"
        | "UNRESOLVED_REFERENCE" -> "この参照の対応先はどれですか？定義と原文を確認してください。"
        | _ -> "この未対応の記述を、どの構造として解釈すべきですか？"
    let build (ir: DesignIR) (checks: Check list) =
        let unresolved = ir.relations |> List.filter (fun r -> r.resolution <> "resolved")
        let extra = unresolved |> List.collect (fun r ->
            let area = match r.kind with "realizedBy" -> "mappings" | "implementedAt" -> "files" | _ -> "dependencies"
            r.evidence |> List.choose (fun e ->
                if ir.diagnostics |> List.exists (fun d -> d.area = area && d.evidence |> List.exists (samePlace e)) then None
                else Some { code = "unresolved-reference"; area = area; message = $"{r.kind}: {r.from} → {r.``to``}"; evidence = [e] }))
        let grouped = ir.diagnostics @ extra |> List.groupBy (fun d -> d.area, d.code, d.evidence |> List.map (fun e -> e.file, e.startLine, e.endLine) |> List.sort)
        let issues = grouped |> List.map (fun ((area, code, locations), ds) ->
            let evidence = ds |> List.collect _.evidence |> List.distinct |> List.sortBy (fun e -> e.file,e.startLine,e.endLine)
            let related = ir.relations |> List.filter (fun r -> r.evidence |> List.exists (fun e -> evidence |> List.exists (samePlace e)))
            let ids = related |> List.filter (fun r -> r.kind = "realizedBy") |> List.map _.from |> List.distinct |> List.sort
            let targets = related |> List.map _.``to`` |> List.distinct |> List.sort
            let key = Json.serialize (area, code, locations)
            let hash = SHA256.HashData(Encoding.UTF8.GetBytes key) |> Convert.ToHexString |> fun s -> s[..15].ToLowerInvariant()
            let c = category code
            { id = "issue:" + hash; category = c; area = area; messages = ds |> List.map _.message |> List.distinct |> List.sort
              ruleIds = rules area; affectedRequirements = ids; targets = targets; evidence = evidence; excerpts = []; question = question c })
        let fallback = checks |> List.choose (fun c ->
            if c.status <> UNKNOWN || issues |> List.exists (fun i -> List.contains c.ruleId i.ruleIds) then None
            else Some { id = "issue:" + c.ruleId; category = "MISSING_INFORMATION"; area = "check"; messages = c.findings |> List.map _.message
                        ruleIds = [c.ruleId]; affectedRequirements = []; targets = []; evidence = c.findings |> List.collect _.evidence |> List.distinct; excerpts = []; question = question "MISSING_INFORMATION" })
        issues @ fallback |> List.sortBy (fun i -> i.area, i.evidence |> List.tryHead |> Option.map _.startLine, i.id)

    let withSources (sources: Map<string,string>) (issues: ReviewIssue list) =
        issues |> List.map (fun issue ->
            let excerpts = issue.evidence |> List.choose (fun e ->
                sources.TryFind e.file |> Option.map (fun source ->
                    let lines = source.Replace("\r\n", "\n").Split('\n')
                    let text = lines |> Array.skip (min lines.Length (max 0 (e.startLine - 1))) |> Array.truncate (max 0 (e.endLine - e.startLine + 1)) |> String.concat "\n"
                    { evidence = e; text = text }))
            { issue with excerpts = excerpts })
