namespace Fdbup.Test

open NUnit.Framework
open Fdbup
open Fdbup.Script

type ``script split by go tests`` ()=
    [<Test>] 
    member test.``should split sql statements`` ()=
        @"select * from table 
        go
        insert 1 into table 
        go" |> Script.splitByGo |> Array.length |> fun x -> Assert.AreEqual(2, x)
    [<Test>] 
    member test.``should ignore case on split`` ()=
        @"select * from table 
        GO
        insert 1 into table 
        GO" |> Script.splitByGo |> Array.length |> fun x -> Assert.AreEqual(2, x)
            
type ``scripts to execute tests`` ()=    
    [<Test>] 
    member test.``should return all scripts if none previously executed`` ()=                          
        let aScripts = [{ScriptName = "script1"; Statements = Seq.empty}]
        let executed = Seq.empty            
        Script.scriptsToExecute aScripts executed
        |> Seq.length |> fun x -> Assert.AreEqual(1, x)
    [<Test>] 
    member test.``should return subset if script previously executed`` ()=                          
        let aScripts = [{ScriptName = "script1"; Statements = Seq.empty};
            {ScriptName = "script2"; Statements = Seq.empty};
            {ScriptName = "script3"; Statements = Seq.empty}]
        let executed = ["script1"]
        Script.scriptsToExecute aScripts executed
        |> Seq.length |> fun x -> Assert.AreEqual(2, x)

            
            
          