module DesignLinter.Program

open System
open System.IO

let help = """Usage: design-linter --requirements <file> --design <file>
  [--before-requirements <file> --before-design <file>]
  [--format markdown|json]

Output is written to stdout. Inputs are never modified.
Exit: 0 PASS, 1 FAIL, 2 input/usage error, 3 UNKNOWN or REVIEW_REQUIRED.
This is a structural PoC, not a complete cc-sdd review gate or AI review.
"""

let run (args: string array) (output: TextWriter) (error: TextWriter) =
    try
        let allowed = Set.ofList ["--requirements"; "--design"; "--before-requirements"; "--before-design"; "--format"]
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
                Parser.parseWithPaths (File.ReadAllText paths.requirements) (File.ReadAllText paths.design) paths
            let ir = load requirements design
            let before = values.TryFind "--before-requirements" |> Option.map (fun r -> load r values["--before-design"])
            let report = Report.build ir before
            output.Write(if format = "json" then Json.serialize report + "\n" else Report.markdown report)
            match report.status with FAIL -> 1 | PASS -> 0 | _ -> 3
    with ex ->
        error.WriteLine $"design-linter: {ex.Message}\nUse --help for usage."
        2

[<EntryPoint>]
let main args = run args Console.Out Console.Error
