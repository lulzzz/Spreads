﻿namespace Spreads.Internals

open System
open System.Collections.Generic
open System.Linq.Expressions
open System.Threading
open System.Threading.Tasks

//thanks to http://v2matveev.blogspot.ru/2010/06/f-performance-of-events.html for the idea


type internal EventV2<'D, 'A when 'D :> Delegate and 'D : delegate<'A, unit> and 'D : null>() = 
  static let invoker =
    let d = Expression.Parameter(typeof<'D>, "dlg")
    let sender = Expression.Parameter(typeof<obj>, "sender")
    let arg = Expression.Parameter(typeof<'A>, "arg")
    let lambda = Expression.Lambda<Action<'D,obj,'A>>(Expression.Invoke(d, sender, arg), d, sender, arg)
    lambda.Compile()

  let mutable multicast : 'D = null     
  // this inline gives 10x better performance for SortedMap Add/Insert operation
  member inline x.Trigger(args: 'A) =
      match multicast with
      | null -> ()
      | d -> invoker.Invoke(d, null, args) // Using this instead of d.DynamicInvoke(null,args) |> ignore makes an empty call more than 20x faster 

  member inline x.Publish =
      { new IDelegateEvent<'D> with
          member x.AddHandler(d) =
              multicast <- System.Delegate.Combine(multicast, d) :?> 'D
          member x.RemoveHandler(d) =
              multicast <- System.Delegate.Remove(multicast, d)  :?> 'D }



type AsyncManualResetEvent () =
  //http://blogs.msdn.com/b/pfxteam/archive/2012/02/11/10266920.aspx
  [<VolatileFieldAttribute>]
  let mutable m_tcs = TaskCompletionSource<bool>()

  member this.WaitAsync() = m_tcs.Task
  member this.Set() = m_tcs.TrySetResult(true)
  member this.Reset() =
          let rec loop () =
              let tcs = m_tcs
              if not tcs.Task.IsCompleted || 
                  Interlocked.CompareExchange(&m_tcs, new TaskCompletionSource<bool>(), tcs) = tcs then
                  ()
              else
                  loop()
          loop ()



type AsyncAutoResetEvent () =
  //http://blogs.msdn.com/b/pfxteam/archive/2012/02/11/10266923.aspx
  static let mutable s_completed = Task.FromResult(true)
  let m_waits = new Queue<TaskCompletionSource<bool>>()
  let mutable m_signaled = false

  member this.WaitAsync(timeout:int) = 
      Monitor.Enter(m_waits)
      try
          if m_signaled then
              m_signaled <- false
              s_completed
          else
              let ct = new CancellationTokenSource(timeout)
              let tcs = new TaskCompletionSource<bool>()
              ct.Token.Register(Action(fun _ -> tcs.TrySetResult(false) |> ignore)) |> ignore
              m_waits.Enqueue(tcs)
              tcs.Task
      finally
          Monitor.Exit(m_waits)

  member this.Set() = 
      let mutable toRelease = Unchecked.defaultof<TaskCompletionSource<bool>>
      Monitor.Enter(m_waits)
      try
          if m_waits.Count > 0 then
              toRelease <- m_waits.Dequeue() 
          else 
              if not m_signaled then m_signaled <- true
          if toRelease <> null then toRelease.TrySetResult(true) |> ignore
      finally
          Monitor.Exit(m_waits)