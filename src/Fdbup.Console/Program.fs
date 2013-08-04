open Fdbup.Upgrader
open Fdbup.Version
open Fdbup.Script
open Fdbup.Log
open System.Reflection

let validateArgs (args:string[]) =
    match args.Length with
    | 1 -> true
    | _ -> false;

[<EntryPoint>]
let main argv = 
    
    if not (validateArgs argv) then
        printfn "usage: Fdbup.Console.exe connectionString";
        exit 1
        
    let connectionString = argv.[0]

    let config = {ConnectionString = connectionString; VersionTable = "SchemaVersions"}

    let version = { new IVersion with 
            member this.Check () = checkVersionTable doesVersionTableExist createVersionTable config
            member this.Executed () = executedScripts config
            member this.Update script = storeScript config script
    }

    let filter = fun (x:string) -> x.ToLower().EndsWith("sql")
    
    let scriptsToExecute = fun () -> scriptsToExecute (getScriptsAssembly (Assembly.GetExecutingAssembly()) filter ) (version.Executed())

    let executeScript script = executeScript script config.ConnectionString 

    let upgrade() = printConsole (runLog (performUpgrade scriptsToExecute executeScript version))
    
    upgrade() |> ignore
    
    0 