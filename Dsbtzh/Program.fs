open System

open Discord

open Discord.WebSocket

let from whom =
    sprintf "from %s" whom

[<EntryPoint>]
let main argv =
    let message = from "F#" 
    printfn "Hello world %s" message
    0 
