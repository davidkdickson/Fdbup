// Module allows logging to be written as a computation expression (Monad).
// code taken from blog post by Matthew Podwysocki see 
// http://codebetter.com/matthewpodwysocki/2010/02/02/a-kick-in-the-monads-writer-edition/
// Also see https://gist.github.com/292392 for full source code.

namespace Fdbup
module Log =
  open System
  open System.Collections.Generic

  type Log<'W, 'T> = |Log of (unit -> 'T * 'W)
  let runLog (Log l) = l()
  
  type IMonoid<'T> =
    abstract member mempty  : unit -> 'T
    abstract member mappend : 'T * 'T -> 'T

  type MonoidAssociations private() = 
    static let associations = new Dictionary<Type, obj>()
    static member Add<'T>(monoid : IMonoid<'T>) = associations.Add(typeof<'T>, monoid)
    static member Get<'T>() = 
      match associations.TryGetValue(typeof<'T>) with 
        | true, assoc -> assoc :?> IMonoid<'T>
        | false, _    -> failwithf "Type %O does not have an implementation of IMonoid" <| typeof<'T>

  let mempty<'T> = MonoidAssociations.Get<'T>().mempty
  let mappend<'T> a b = MonoidAssociations.Get<'T>().mappend(a, b)

  type ListMonoid<'T>() =
    interface IMonoid<'T list> with
      member this.mempty() = []
      member this.mappend(a, b) = a @ b

  MonoidAssociations.Add(new ListMonoid<string>())

  type LogBuilder() =
    member this.Return<'W,'T>(a : 'T) : Log<'W,'T> = 
      Log(fun () -> a, mempty())
    member this.ReturnFrom<'W,'T>(w : Log<'W,'T>) = w
    member this.Bind<'W,'T,'U>(m : Log<'W,'T>, k : 'T -> Log<'W,'U>)  : Log<'W,'U> =
      Log(fun () ->
        let (a, w)  = runLog m
        let (b, w') = runLog (k a)
        in  (b, mappend<'W> w w'))
    member this.Zero<'W>() : Log<'W,unit> = this.Return ()
    member this.TryWith<'W,'T>(log : Log<'W,'T>, handler : exn -> Log<'W,'T>) =
      Log(fun () ->
        try runLog log
        with e -> runLog (handler e))
    member this.TryFinally<'W,'T>(log : Log<'W,'T>, compensation : unit -> unit) =
      Log(fun () ->
        try runLog log
        finally compensation())
    member this.Using<'D,'W,'T when 'D :> IDisposable and 'D : null>(resource : 'D, body : 'D -> Log<'W,'T>) =
      this.TryFinally(body resource, (fun () -> match resource with null -> () | disp -> disp.Dispose()))
    member this.Delay<'W,'T>(f : unit -> Log<'W,'T>) =
      this.Bind(this.Return (), f)
    member this.Combine<'W,'T>(comp1 : Log<'W,unit>, comp2 : Log<'W,'T>) =
      this.Bind(comp1, (fun () -> comp2))
    member this.Yield(log : Log<'W,'T>) = log
    member this.While<'W>(pred : unit -> bool, body : Log<'W,unit>) =
      match pred() with 
      | true -> this.Bind(body, (fun () -> this.While(pred,body))) 
      | _ -> this.Return ()
    member this.For<'W,'T>(items : seq<'T>, body : 'T -> Log<'W,unit>) =
      this.Using(items.GetEnumerator(), (fun enum -> this.While((fun () -> enum.MoveNext()), this.Delay(fun () -> body enum.Current))))

  let log = new LogBuilder()

  let logMessage (msg : string) = Log(fun () -> (), [msg])