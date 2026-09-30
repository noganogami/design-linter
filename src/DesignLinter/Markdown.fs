namespace DesignLinter

open System
open System.Text.RegularExpressions
open Markdig
open Markdig.Syntax
open Markdig.Syntax.Inlines
open Markdig.Extensions.Tables

type internal MdNode = { kind: string; value: string; children: MdNode list; startLine: int; endLine: int; depth: int; ordered: bool; start: int; lang: string }
type internal Section = { title: string; depth: int; nodes: ResizeArray<MdNode>; children: ResizeArray<Section>; evidence: Evidence }

module internal Md =
    let rx pattern value = Regex.Match(value, pattern, RegexOptions.CultureInvariant)
    let rxi pattern value = Regex.Match(value, pattern, RegexOptions.IgnoreCase ||| RegexOptions.CultureInvariant)
    let matches pattern value = (rx pattern value).Success
    let matchesI pattern value = (rxi pattern value).Success
    let group (index: int) (m: Match) = m.Groups[index].Value
    let rec inlineText (node: Inline) =
        if isNull node then "" else
        match node with
        | :? LiteralInline as n -> n.Content.ToString()
        | :? CodeInline as n -> n.Content
        | :? LineBreakInline -> "\n"
        | :? HtmlInline -> ""
        | :? ContainerInline as n -> n |> Seq.map inlineText |> String.concat ""
        | _ -> ""
    let rec text (node: MdNode) =
        if node.kind = "html" then ""
        elif node.children.IsEmpty then node.value
        else node.children |> List.map text |> String.concat (if node.kind = "list" then "\n" else "")
    let evidence file section (node: MdNode) : Evidence = { file = file; section = section; startLine = node.startLine; endLine = node.endLine }
    let parse (source: string) file =
        let pipeline = MarkdownPipelineBuilder().UsePipeTables().UsePreciseSourceLocation().Build()
        let ast = Markdown.Parse(source, pipeline)
        let lineAt offset = source.AsSpan(0, max 0 (min source.Length offset)).Count('\n') + 1
        let rec convert (block: Block) =
            let baseNode = { kind = "unknown"; value = ""; children = []; startLine = block.Line + 1; endLine = lineAt (block.Span.End + 1); depth = 0; ordered = false; start = 1; lang = "" }
            let children (b: ContainerBlock) = b |> Seq.map convert |> Seq.toList
            match block with
            | :? HeadingBlock as b -> { baseNode with kind = "heading"; value = inlineText b.Inline; depth = b.Level }
            | :? ParagraphBlock as b -> { baseNode with kind = "paragraph"; value = inlineText b.Inline }
            | :? FencedCodeBlock as b -> { baseNode with kind = "code"; value = b.Lines.ToString().TrimEnd('\r', '\n'); lang = if isNull b.Info then "" else b.Info }
            | :? CodeBlock as b -> { baseNode with kind = "code"; value = b.Lines.ToString().TrimEnd('\r', '\n') }
            | :? HtmlBlock -> { baseNode with kind = "html" }
            | :? ListBlock as b ->
                let start = match Int32.TryParse b.OrderedStart with true, v -> v | _ -> 1
                { baseNode with kind = "list"; children = children b; ordered = b.IsOrdered; start = start }
            | :? ListItemBlock as b -> { baseNode with kind = "listItem"; children = children b }
            | :? Table as b -> { baseNode with kind = "table"; children = children b }
            | :? TableRow as b -> { baseNode with kind = "row"; children = children b }
            | :? TableCell as b -> { baseNode with kind = "cell"; children = children b }
            | :? QuoteBlock as b -> { baseNode with kind = "blockquote"; children = children b }
            | :? ContainerBlock as b -> { baseNode with children = children b }
            | _ -> baseNode
        let root = { title = ""; depth = 0; nodes = ResizeArray(); children = ResizeArray(); evidence = { file = file; section = ""; startLine = 1; endLine = lineAt source.Length } }
        let stack = ResizeArray<Section>([ root ])
        for block in ast do
            let node = convert block
            if node.kind = "heading" then
                while stack[stack.Count - 1].depth >= node.depth do stack.RemoveAt(stack.Count - 1)
                let title = (text node).Trim()
                let section = { title = title; depth = node.depth; nodes = ResizeArray(); children = ResizeArray(); evidence = evidence file title node }
                stack[stack.Count - 1].children.Add section
                stack.Add section
            else stack[stack.Count - 1].nodes.Add node
        root
    let rec sections (root: Section) = root.children |> Seq.collect (fun s -> seq { yield s; yield! sections s }) |> Seq.toList
    let rec allNodes (section: Section) = List.ofSeq section.nodes @ (section.children |> Seq.collect allNodes |> Seq.toList)
    let items (node: MdNode) = if node.kind = "list" then node.children else [ node ]
    let rows (node: MdNode) =
        match node.children with
        | header :: rest ->
            let headers = header.children |> List.map (text >> fun s -> s.Trim().ToLowerInvariant())
            rest |> List.map (fun row ->
                let cells = headers |> List.mapi (fun i h -> h, (row.children |> List.tryItem i |> Option.map (text >> fun s -> s.Trim()) |> Option.defaultValue "")) |> Map.ofList
                cells, row)
        | [] -> []
    let names (value: string) =
        Regex.Split(value, "[,;\n]") |> Array.map (fun s -> Regex.Replace(s, @"\s*\(P[012]\)\s*$", "", RegexOptions.IgnoreCase).Trim())
        |> Array.filter (fun s -> s <> "" && not (matchesI @"^(?:none|n/a|—|-|なし)$" s)) |> Array.toList
    let isNone (value: string) = matchesI @"^(?:none|n/a|—|-|なし)\.?$" (value.Trim())
    let placeholder value = matchesI @"\b(?:TBD|TODO)\b|\{\{[^}]+\}\}|\[(?:Component Name|ComponentName|specific value|name and brief rationale)\]" value
    let onlyPlaceholder (value: string) = String.IsNullOrWhiteSpace value || matchesI @"^(?:TBD|TODO|\{\{[^}]+\}\}|\[[^\]]+\]|[. …-]+)[.!]?$" (value.Trim())
    let cell key (cells: Map<string,string>) = cells.TryFind key |> Option.defaultValue ""
    let firstCell keys (cells: Map<string,string>) = keys |> List.tryPick cells.TryFind
