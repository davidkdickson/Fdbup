#light
#load "Log.fs"
#load "Fdbup.fs"

open Fdbup.Upgrader
open Fdbup.Version
open Fdbup.Script
open Fdbup.Log

//let validateArguments = 
//    match fsi.CommandLineArgs.Length with
//    | 4 -> ()
//    | _ -> printfn "usage: fsi.exe Fdbup.fsx path connectionString versionTable"; exit 1

//let path = fsi.CommandLineArgs.[1]
//let connectionString = fsi.CommandLineArgs.[2] 
//let versionTable = fsi.CommandLineArgs.[3]

let path = "C:\\Projects\\Fdbup\\src\\SampleSQL"

let config = {
    ConnectionString = "Data Source=.\sqlexpress;Initial Catalog=test;Integrated Security=SSPI";
    VersionTable = "VersionTable";
    }

let version = { new IVersion with 
            member this.Check () = checkVersionTable doesVersionTableExist createVersionTable config
            member this.Executed () = executedScripts config
            member this.Update script = storeScript config script
    }
let scriptsToExecute = fun () -> scriptsToExecute (getScriptsFileSystem path) (version.Executed())

let executeScript script = executeScript script config.ConnectionString 

let upgrade() = runLog (performUpgrade scriptsToExecute executeScript version)

