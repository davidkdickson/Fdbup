#light
#load "Log.fs"

open System.IO
open System.Data.SqlClient
open System.Text.RegularExpressions
open Fdbup.Log

let validateArguments = 
    match fsi.CommandLineArgs.Length with
    | 4 -> ()
    | _ -> printfn "usage: fsi.exe Fdbup.fsx path connectionString versionTable"; exit 1

let path = fsi.CommandLineArgs.[1]
let connectionString = fsi.CommandLineArgs.[2] 
let versionTable = fsi.CommandLineArgs.[3]

type Script = {
    ScriptName : string;
    Statements : string []
}

let doesVersionTableExist() = 
    use conn = new SqlConnection(connectionString)
    do conn.Open()
    let cmdText = sprintf "select count(*) from sys.objects where type='U' and name='%s'" versionTable
    use comm = new SqlCommand(cmdText, conn)
    let result = (comm.ExecuteScalar()) :?> int
    match result with 
        | 0 -> false 
        | _ -> true

let createVersionTable() = 
    use conn = new SqlConnection(connectionString)
    do conn.Open()
    let cmdText = sprintf @"create table %s (
        [Id] int identity(1,1) not null constraint PK_SchemaVersions_Id primary key nonclustered ,
        [ScriptName] nvarchar(255) not null,
        [Applied] datetime not null)" versionTable
    use comm = new SqlCommand(cmdText, conn)
    comm.ExecuteScalar() 
    |> ignore

let allScripts() = 
    let splitByGo scriptContents = 
        Regex.Split(scriptContents, "^\\s*GO\\s*$", RegexOptions.IgnoreCase ||| RegexOptions.Multiline) 
        |> Array.filter(fun x-> x.Trim().Length > 0)
    seq { 
        for file in Directory.GetFiles(path) do 
            let fileInfo = FileInfo(file)        
            let fileContents = File.ReadAllText(file)        
            yield { ScriptName = fileInfo.Name; Statements = splitByGo fileContents } 
    }

let executedScripts() = seq {
    use conn = new SqlConnection(connectionString)
    let cmdText = sprintf "select ScriptName from %s" versionTable
    use comm = new SqlCommand(cmdText, conn)
    do conn.Open()
    use reader = comm.ExecuteReader()
    while reader.Read() do
        yield reader.GetString 0
    }

let scriptsNotExecuted() =     
    let scriptsToExec = Set.ofSeq (allScripts() |> Seq.map(fun s -> s.ScriptName)) - Set.ofSeq(executedScripts())
    allScripts() 
    |> Seq.filter(fun x -> (Seq.exists(fun a -> a = x.ScriptName) scriptsToExec)) 

let storeScript script = 
    use conn = new SqlConnection(connectionString)
    do conn.Open()
    let cmdText = sprintf "insert into %s (ScriptName, Applied) values (@scriptName, (getutcdate()))" versionTable
    use comm = new SqlCommand(cmdText, conn)
    comm.Parameters.AddWithValue("scriptName", script.ScriptName) |> ignore
    comm.ExecuteNonQuery() |> ignore 

let executeScript script = 
    use connection = new SqlConnection(connectionString)
    connection.Open()
    for s in script.Statements do
        use command = new SqlCommand(s, connection)
        command.ExecuteNonQuery() |> ignore
    storeScript script

let performUpgrade (scriptsToExecute:unit->seq<Script>) (format:ILogFormat) = log {
    try 
        do! format.writeInformation "Beginning database upgrade." []
        do! format.writeInformation "Connection string is '%s'." [connectionString]        
        do! match doesVersionTableExist() with 
            | true -> format.writeInformation "Version table [%s] already exists." [versionTable]
            | false -> createVersionTable();  format.writeInformation "Version table [%s] created" [versionTable]
        let scriptsToExecute = scriptsToExecute()
        match scriptsToExecute |> Seq.length with           
            | l when l > 0 ->
                for script in scriptsToExecute do
                    do! format.writeInformation "Executing script: %s" [script.ScriptName]
                    executeScript script       
            | _ -> do! format.writeInformation "Database up to date. No script executed." []
        return true
    with 
        | :? SqlException as e -> 
            do! format.writeError "%s %s %s %s" [e.LineNumber.ToString(); e.Procedure; e.Number.ToString(); e.Message] 
            return false
        | _ as e ->
            do! format.writeError "%s" [e.Message]
            return false
    }

let upgrade print =
    let dbUpgrade = performUpgrade scriptsNotExecuted (StringLogger())
    let upgradeResult = runLog dbUpgrade
    print upgradeResult

upgrade printLog