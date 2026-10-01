module DesignLinter.Program

open System
open System.IO

let help = """Usage: design-linter --requirements <file> --design <file>
  [--before-requirements <file> --before-design <file>]
  [--format markdown|json]
  [--bindings <json-file>]

Output is written to stdout. Inputs are never modified.
Exit: 0 PASS, 1 source FAIL, 2 input/invalid bindings, 3 UNKNOWN, REVIEW_REQUIRED,
stale bindings or conditional review. Bindings never upgrade the source result.
This is a structural PoC, not a complete cc-sdd review gate or AI review.
"""

let run (args: string array) (output: TextWriter) (error: TextWriter) =
    try
        let allowed = Set.ofList ["--requirements"; "--design"; "--before-requirements"; "--before-design"; "--format"; "--bindings"]
        let rec parse arguments values =
            match arguments with
            | [] -> values
            | ("--help" | "-h") :: rest -> parse rest (Map.add "--help" "true" values)
            | key :: value :: rest when allowed.Contains key && not (value.StartsWith "--") -> parse rest (Map.add key value values)
            | key :: _ -> invalidArg "args" $"Unknown option or missing value: {key}"
        let values = parse (List.ofArray args) Map.empty
        if values.ContainsKey "--help" then output.Write help; 0
        else
            let required key = values.TryFind key |> Option.defaultWith (fun () -> invalidArg "args" $"{key} is required.")
            let requirements, design = required "--requirements", required "--design"
            if values.ContainsKey "--before-requirements" <> values.ContainsKey "--before-design" then invalidArg "args" "Both baseline files are required."
            let format = values.TryFind "--format" |> Option.defaultValue "markdown"
            if format <> "markdown" && format <> "json" then invalidArg "args" "--format must be markdown or json."
            let load requirements design =
                let paths = { requirements = Path.GetFullPath requirements; design = Path.GetFullPath design }
                let req, doc = File.ReadAllText paths.requirements, File.ReadAllText paths.design
                Parser.parseWithPaths req doc paths, req, doc
            let ir, req, doc = load requirements design
            let before = values.TryFind "--before-requirements" |> Option.map (fun r -> let ir, _, _ = load r values["--before-design"] in ir)
            let bindings = values.TryFind "--bindings" |> Option.map (File.ReadAllText >> Bindings.read)
            let report = Report.build ir before |> Report.withInputs req doc bindings
            output.Write(if format = "json" then Json.serialize report + "\n" else Report.markdown report)
            match report.supplementalReview with
            | Some s when s.status = "INVALID" -> 2
            | Some s when s.status = "STALE" -> 3
            | _ -> match report.status with FAIL -> 1 | PASS when bindings.IsNone -> 0 | _ -> 3
    with ex ->
        error.WriteLine $"design-linter: {ex.Message}\nUse --help for usage."
        2

[<EntryPoint>]
let main args = run args Console.Out Console.Error
