open System

open Discord

open Discord.WebSocket

let from whom = sprintf "from %s" whom

let cl (client: DiscordSocketClient) =
    async {
        let! loginAsync =
            client.LoginAsync(TokenType.Bot, "")
            |> Async.AwaitTask

        0 |> ignore
    }

[<EntryPoint>]
let main argv =
    let message = from "F#"
    printfn "Hello world %s" message
    let client = new DiscordSocketClient()
    cl client |> ignore
    0
