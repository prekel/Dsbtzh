open System
open System.Threading.Tasks

open FSharp.Control.Tasks
open DSharpPlus
open Emzi0767.Utilities

[<EntryPoint>]
let main argv =
    use discord =
        new DiscordClient(DiscordConfiguration(Token = argv.[0]))

    discord.add_MessageCreated (
        AsyncEventHandler<_, _>
            (fun a b ->
                task {
                    if b.Author.Id <> a.CurrentUser.Id then
                        let! _ = a.SendMessageAsync(b.Channel, b.Message.Content)
                        return ()
                    else
                        return ()
                }
                :> Task)
    )

    discord.ConnectAsync()
    |> Async.AwaitTask
    |> Async.RunSynchronously

    Task.Delay(-1)
    |> Async.AwaitTask
    |> Async.RunSynchronously

    0
