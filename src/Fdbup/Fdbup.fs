namespace Fdbup

module Script =
  open System.Text.RegularExpressions
  open System.IO
  open System.Reflection
  open System.Data.SqlClient
  
  type Script = {
    ScriptName : string;
    Statements : string seq
  }

  let splitByGo scriptContents = 
    Regex.Split(scriptContents, "^\\s*GO\\s*$", RegexOptions.IgnoreCase ||| RegexOptions.Multiline) 
    |> Array.filter(fun x-> x.Trim().Length > 0)

  let getScriptsFileSystem path = seq { 
    for file in Directory.GetFiles(path) do 
      let fileInfo = FileInfo(file)        
      let fileContents = File.ReadAllText(file)        
      yield { ScriptName = fileInfo.Name; Statements = splitByGo fileContents } 
  }

  let resoureAsScript (assembly:Assembly) scriptName =
    let resourceStream = assembly.GetManifestResourceStream(scriptName)
    use resourceStreamReader = new StreamReader(resourceStream)
    let contents = resourceStreamReader.ReadToEnd()
    {ScriptName = scriptName; Statements = splitByGo contents}

  let getScriptsAssembly (assembly:Assembly) filter = 
    assembly.GetManifestResourceNames() 
    |> Seq.filter(filter) 
    |> Seq.map(resoureAsScript assembly)

  let scriptsToExecute allScripts executed =
    let scripts = allScripts     
    let scriptsToExec = Set.ofSeq (scripts|> Seq.map(fun s -> s.ScriptName)) - Set.ofSeq(executed)
    scripts |> Seq.filter(fun x -> (Seq.exists(fun a -> a = x.ScriptName) scriptsToExec))
    
  let executeScript script connString = 
    use connection = new SqlConnection(connString)
    connection.Open()
    for s in script.Statements do
      use command = new SqlCommand(s, connection)
      command.ExecuteNonQuery() |> ignore     

module Version = 
  open System.Data.SqlClient
  
  type VersionConfig = {
    ConnectionString : string;
    VersionTable : string
  }

  type IVersion =
    abstract member Check : unit -> bool
    abstract member Executed : unit -> string seq
    abstract member Update : string -> unit

  let doesVersionTableExist config = 
    use conn = new SqlConnection(config.ConnectionString)
    do conn.Open()
    let cmdText = sprintf "select count(*) from sys.objects where type='U' and name='%s'" config.VersionTable
    use comm = new SqlCommand(cmdText, conn)
    let result = (comm.ExecuteScalar()) :?> int
    match result with 
      | 0 -> false 
      | _ -> true

  let createVersionTable config = 
    use conn = new SqlConnection(config.ConnectionString)
    do conn.Open()
    let cmdText = sprintf @"create table %s (
      [Id] int identity(1,1) not null constraint PK_SchemaVersions_Id primary key nonclustered ,
      [ScriptName] nvarchar(255) not null,
      [Applied] datetime not null)" config.VersionTable
    use comm = new SqlCommand(cmdText, conn)
    comm.ExecuteScalar() 
    |> ignore

  let checkVersionTable =
    (fun doesVersionTableExist -> 
      (fun createVersionTable ->
        (fun (config:VersionConfig) -> 
          match doesVersionTableExist config with
          | false -> createVersionTable config; false
          | _ -> true)))

  let executedScripts config = seq {
    use conn = new SqlConnection(config.ConnectionString)
    let cmdText = sprintf "select ScriptName from %s" config.VersionTable
    use comm = new SqlCommand(cmdText, conn)
    do conn.Open()
    use reader = comm.ExecuteReader()
    while reader.Read() do
      yield reader.GetString 0
    }

  let storeScript cfg (script:string) = 
    use conn = new SqlConnection(cfg.ConnectionString)
    do conn.Open()
    let cmdText = sprintf "insert into %s (ScriptName, Applied) values (@scriptName, (getutcdate()))" cfg.VersionTable
    use comm = new SqlCommand(cmdText, conn)
    comm.Parameters.AddWithValue("scriptName", script) |> ignore
    comm.ExecuteNonQuery() |> ignore 

module Upgrader = 
  open Script
  open Fdbup.Log
  open Version
  open System.Data.SqlClient
  open Printf

  let performUpgrade getScripts executeScript (version:IVersion) = log {
    try 
      do! logMessage (Information("Beginning database upgrade."))
      do! match version.Check() with 
            | true -> logMessage (Information("Version table exists."))
            | false -> logMessage (Information("Version table created"))
      let scriptsToExecute = getScripts()
      match scriptsToExecute |> Seq.length with           
        | l when l > 0 ->
          for script in scriptsToExecute do
            do! logMessage (Information(sprintf "Executing script: %s" script.ScriptName))
            executeScript script 
            version.Update script.ScriptName
        | _ -> do! logMessage (Information("Database up to date. No script executed."))
      return true
    with 
      | :? SqlException as e -> 
        do! logMessage (Error(sprintf "%s %s %s %s" (e.LineNumber.ToString()) e.Procedure (e.Number.ToString()) e.Message))
        return false
      | _ as e ->
        do! logMessage (Error(sprintf "%s" e.Message))
        return false
    }