open System.Diagnostics
open System.Threading.Tasks
open System.Linq

open DSharpPlus
open DSharpPlus.VoiceNext

[<EntryPoint>]
let main argv =
    use discord =
        new DiscordClient(
            DiscordConfiguration(
                Token = argv.[0],
                Intents = (DiscordIntents.AllUnprivileged ||| DiscordIntents.MessageContents)
            )
        )

    discord.UseVoiceNext() |> ignore

    discord.add_MessageCreated (
        (fun a b ->
            let channel =
                discord.Guilds
                    .Where(fun g -> g.Value.Name = "misterptits's server")
                    .First()
                    .Value.Channels.Where(fun channel -> channel.Value.Name = "General")
                    .First()
                    .Value

            task {
                if b.Author.Id <> a.CurrentUser.Id then
                    let! _ = a.SendMessageAsync(b.Channel, b.Message.Content)
                    let! connection = channel.ConnectAsync()

                    let filePath =
                        "/home/vladislav/Downloads/ТРИ ДНЯ ГОВНА - ГЕОМЕТРИЯ КАЛА \"(ПОЛНЫЙ АЛЬБОМ)\" [0EhgW5vf7eg].webm".Replace("\"", "\\\"")

                    let ffmpeg =
                        Process.Start(
                            ProcessStartInfo(
                                FileName = "ffmpeg",
                                Arguments = $"-i \"%s{filePath}\" -ac 2 -f s16le -ar 48000 pipe:1",
                                RedirectStandardOutput = true,
                                UseShellExecute = false
                            )
                        )
                        
                    let pcm = ffmpeg.StandardOutput.BaseStream
                    let transmit = connection.GetTransmitSink()
                    let! () = pcm.CopyToAsync(transmit)

                    return ()
                else
                    return ()
            }
            :> Task)
    )

    discord.ConnectAsync() |> Async.AwaitTask |> Async.RunSynchronously

    Task.Delay(-1) |> Async.AwaitTask |> Async.RunSynchronously

    0
