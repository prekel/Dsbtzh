open System
open System.Diagnostics
open System.Threading
open System.Threading.Tasks
open System.Web

open Akkling

open DSharpPlus
open DSharpPlus.Entities
open DSharpPlus.VoiceNext

module YouTubeId =
    type t = YouTubeId of string

    let parseSecs (secs: string) =
        match secs with
        | null -> TimeSpan.Zero
        | secs -> secs.TrimEnd('s') |> int |> TimeSpan.FromSeconds

    let parse (url: string) =
        let url = Uri(url.TrimEnd('/'))
        let query = HttpUtility.ParseQueryString(url.Query)

        match url.Host, url.Segments, query.["v"], query.["t"] with
        | ("www.youtube.com" | "youtube.com"), [| "/"; "live/"; id |], null, time -> Some(YouTubeId id, parseSecs time)
        | ("www.youtube.com" | "youtube.com"), [| "/"; "watch" |], id, time -> Some(YouTubeId id, parseSecs time)
        | ("www.youtube.com" | "youtube.com"), [| "/"; "shorts/"; id |], null, time ->
            Some(YouTubeId id, parseSecs time)
        | "youtu.be", [| "/"; id |], null, time -> Some(YouTubeId id, parseSecs time)
        | _ -> None

module GuildHandlerMsg =
    type t =
        | Message of DiscordClient * EventArgs.MessageCreateEventArgs
        | Ended
        | Downloaded of yid: YouTubeId.t * filename: string * title: string

module Downloader =
    type t = Youtube of YouTubeId.t

    let behavior (m: Actor<t>) =
        let rec loop () =
            actor {
                let! msg = m.Receive()
                let sender = m.Parent()

                match msg with
                | Youtube(YouTubeId.YouTubeId url) ->
                    let ytdlp =
                        Process.Start(
                            ProcessStartInfo(
                                FileName = "yt-dlp",
                                Arguments =
                                    $"""-f 251 -o "%%(id)s.%%(ext)s" --print "%%(id)s.%%(ext)s %%(title)s" --no-simulate %s{url}""",
                                RedirectStandardOutput = true,
                                UseShellExecute = false
                            )
                        )

                    let! (printed: string) = ytdlp.StandardOutput.ReadLineAsync()
                    let sepInd = printed.IndexOf(' ')
                    let fileName = printed.Substring(0, sepInd)
                    let title = printed.Substring(sepInd + 1)
                    do! ytdlp.WaitForExitAsync()
                    sender <! GuildHandlerMsg.Downloaded(YouTubeId.YouTubeId url, fileName, title)
                    return! loop ()
            }

        loop ()

module Player =
    type msg =
        | Play of VoiceTransmitSink * string * TimeSpan
        | Stop
        | Pause
        | Resume
        | Reset
        | Seek of TimeSpan

    type playState =
        { ffmpeg: Process
          pcm: System.IO.Stream
          transmit: VoiceTransmitSink
          filePath: string }

    type state =
        | Ready
        | Playing of playState * CancellationTokenSource
        | Paused of playState

    let copy (pcm: IO.Stream) (transmit: VoiceTransmitSink) self sender =
        let cancelTokenSource = new CancellationTokenSource()

        let _ =
            pcm
                .CopyToAsync(transmit, cancellationToken = cancelTokenSource.Token)
                .ContinueWith(fun t ->
                    if not cancelTokenSource.IsCancellationRequested then
                        self <! Reset
                        sender <! GuildHandlerMsg.Ended)

        cancelTokenSource

    let play (filePath: string) (time: TimeSpan) transmit self sender =
        let filePath = filePath.Replace("\"", "\\\"")

        let ffmpeg =
            Process.Start(
                ProcessStartInfo(
                    FileName = "ffmpeg",
                    Arguments =
                        $"""-ss %s{time.ToString("hh\:mm\:ss")} -i "%s{filePath}" -ac 2 -f s16le -ar 48000 pipe:1""",
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                )
            )

        let pcm = ffmpeg.StandardOutput.BaseStream
        let cancelTokenSource = copy pcm transmit self sender

        { ffmpeg = ffmpeg
          pcm = pcm
          transmit = transmit
          filePath = filePath },
        cancelTokenSource

    let behavior (m: Actor<msg>) =
        let rec loop state =
            actor {
                let! msg = m.Receive()
                let sender = m.Parent()

                match msg, state with
                | Play(transmit, filePath, time), Ready ->
                    let playState = play filePath time transmit m.Self sender
                    return! loop (Playing(playState))
                | Play(_transmit, filePath, time), Paused { ffmpeg = ffmpeg; transmit = transmit } ->
                    ffmpeg.Kill()
                    let playState = play filePath time transmit m.Self sender
                    return! loop (Playing(playState))
                | Pause, Playing(playState, cancelTokenSource) ->
                    cancelTokenSource.Cancel()
                    cancelTokenSource.Dispose()
                    loop (Paused playState)
                | Resume, Paused({ pcm = pcm; transmit = transmit } as playState) ->
                    let cancelTokenSource = copy pcm transmit m.Self sender
                    loop (Playing(playState, cancelTokenSource))
                | Stop, Playing({ ffmpeg = ffmpeg }, cancelTokenSource) ->
                    cancelTokenSource.Cancel()
                    cancelTokenSource.Dispose()
                    ffmpeg.Kill()
                    sender <! GuildHandlerMsg.Ended
                    loop Ready
                | Seek time,
                  Playing({ transmit = transmit
                            filePath = filePath },
                          _) ->
                    m.Self <! Pause
                    m.Self <! Play(transmit, filePath, time)
                    loop state
                | Seek time,
                  Paused { transmit = transmit
                           filePath = filePath } ->
                    m.Self <! Play(transmit, filePath, time)
                    loop state
                | Reset, _ -> loop Ready
                | msg, state -> failwithf $"msg: %A{msg} state: %A{state}"
            }

        loop Ready

module GuildHandler =
    type entry = YouTubeId.t * TimeSpan

    module Sender =
        type msg =
            | StartingPlaying of entry * string
            | AddingToQueue of entry
            | Seeking of entry * TimeSpan
            | Skipping of entry
            | Pausing of entry
            | Resuming of entry
            | Stopping of entry
            | Ending of entry

        let behavior (m: Actor<DiscordClient * DiscordChannel * msg>) =
            let rec loop () =
                actor {
                    let! discord, channel, msg = m.Receive()

                    let! _ =
                        match msg with
                        | StartingPlaying((YouTubeId.YouTubeId yid, time), title) ->
                            discord.SendMessageAsync(
                                channel,
                                $"Playing **%s{title}** [[`%s{yid}`](<https://youtu.be/%s{yid}>)] from `%A{time}`"
                            )
                        | AddingToQueue(YouTubeId.YouTubeId yid, time) ->
                            discord.SendMessageAsync(
                                channel,
                                $"Added [[`%s{yid}`](<https://youtu.be/%s{yid}>)] from `%A{time}`"
                            )
                        | Seeking(_, time) -> discord.SendMessageAsync(channel, $"Seeking to `%A{time}`")
                        | Skipping(YouTubeId.YouTubeId yid, _) -> discord.SendMessageAsync(channel, $"Skipped")
                        | Pausing(YouTubeId.YouTubeId yid, _) -> discord.SendMessageAsync(channel, $"Paused")
                        | Resuming(YouTubeId.YouTubeId yid, _) -> discord.SendMessageAsync(channel, $"Resumed")
                        | Stopping(YouTubeId.YouTubeId yid, _) -> discord.SendMessageAsync(channel, $"Stopped")
                        | Ending(YouTubeId.YouTubeId yid, _) ->
                            // discord.SendMessageAsync(channel, $"Ended")
                            Task.FromResult Unchecked.defaultof<_>

                    return loop ()
                }

            loop ()

    type connectedState =
        { connection: VoiceNextConnection
          current: entry
          queue: entry list }

    type dsState = DiscordClient * DiscordChannel

    type state =
        | Waiting
        | Connected of connectedState

    let behavior (m: Actor<GuildHandlerMsg.t>) =
        let playerRef = spawn m "player" (props Player.behavior)
        let downloaderRef = spawn m "downloader" (props Downloader.behavior)
        let senderRef = spawn m "sender" (props Sender.behavior)

        let rec loop dsState state =
            actor {
                let! msg = m.Receive()

                try 
                    match dsState, msg with
                    | _, GuildHandlerMsg.Message(discord, event) ->
                        if event.Author.Id = discord.CurrentUser.Id then
                            return loop dsState state
                        else
                            let author = event.Author :?> DiscordMember
                            let channel = author.VoiceState.Channel
                            let msgChannel = event.Channel
                            let parsed = event.Message.Content.Split(" ", StringSplitOptions.RemoveEmptyEntries)

                            let! connection =
                                match parsed, state with
                                | [| "%play"; _ |], Waiting -> task { return! channel.ConnectAsync() }
                                | _, Connected { connection = connection } -> Task.FromResult connection
                                | _, _ -> Unchecked.defaultof<_>

                            let state =
                                match parsed, state with
                                | [| "%play"; url |], Waiting ->

                                    match YouTubeId.parse url with
                                    | None -> state
                                    | Some(yid, time) ->
                                        downloaderRef <! Downloader.Youtube(yid)
                                        senderRef <! (discord, msgChannel, Sender.AddingToQueue(yid, time))

                                        Connected
                                            { connection = connection
                                              current = (yid, time)
                                              queue = [] }
                                | [| "%play"; url |], Connected({ queue = queue } as connectedState) ->
                                    match YouTubeId.parse url with
                                    | None -> state
                                    | Some(yid, time) ->
                                        senderRef <! (discord, msgChannel, Sender.AddingToQueue(yid, time))

                                        Connected
                                            { connectedState with
                                                queue = queue @ [ (yid, time) ] }
                                | [| "%skip" |], (Connected { current = current } as state) ->
                                    playerRef <! Player.Stop
                                    senderRef <! (discord, msgChannel, Sender.Skipping current)
                                    state
                                | [| "%pause" |], (Connected { current = current } as state) ->
                                    playerRef <! Player.Pause
                                    senderRef <! (discord, msgChannel, Sender.Pausing current)
                                    state
                                | [| "%stop" |],
                                  Connected { connection = connection
                                              current = current } ->
                                    playerRef <! Player.Stop
                                    connection.Disconnect()
                                    connection.Dispose()
                                    senderRef <! (discord, msgChannel, Sender.Stopping current)
                                    Waiting
                                | [| "%resume" |], (Connected { current = current } as state) ->
                                    playerRef <! Player.Resume
                                    senderRef <! (discord, msgChannel, Sender.Resuming current)
                                    state
                                | [| "%seek"; time |], (Connected { current = current } as state) ->
                                    let time =
                                        try
                                            TimeSpan.FromSeconds(int time)
                                        with :? FormatException ->
                                            try
                                                TimeSpan.ParseExact(
                                                    time,
                                                    "m\:s",
                                                    Globalization.CultureInfo.InvariantCulture
                                                )
                                            with :? FormatException ->
                                                TimeSpan.ParseExact(
                                                    time,
                                                    "h\:m\:s",
                                                    Globalization.CultureInfo.InvariantCulture
                                                )

                                    playerRef <! Player.Seek time
                                    senderRef <! (discord, msgChannel, Sender.Seeking(current, time))
                                    state
                                | _, state -> state

                            return! loop (Some(discord, msgChannel)) state
                    | Some(discord, msgChannel), GuildHandlerMsg.Ended ->
                        match state with
                        | Connected { connection = connection
                                      queue = (url, time) :: xs } ->
                            downloaderRef <! Downloader.Youtube(url)

                            return!
                                loop
                                    dsState
                                    (Connected
                                        { connection = connection
                                          current = (url, time)
                                          queue = xs })
                        | Connected { connection = connection
                                      current = current
                                      queue = [] } ->
                            connection.Disconnect()
                            connection.Dispose()
                            senderRef <! (discord, msgChannel, Sender.Ending current)
                            return! loop dsState Waiting
                        | Waiting -> return! loop dsState Waiting
                    | Some(discord, msgChannel), GuildHandlerMsg.Downloaded(yid, filename, title) ->
                        match state with
                        | Connected { connection = connection
                                      current = (currYid, time)
                                      queue = xs } when currYid = yid ->
                            let transmit = connection.GetTransmitSink()
                            playerRef <! Player.Play(transmit, filename, time)

                            senderRef
                            <! (discord, msgChannel, Sender.StartingPlaying((currYid, time), title))

                            return!
                                loop
                                    dsState
                                    (Connected
                                        { connection = connection
                                          current = (currYid, time)
                                          queue = xs })
                        | _ -> return! loop dsState state
                    | None, _ -> return! loop dsState state
                with
                | exn ->
                    eprintf $"%A{exn}"
                    loop dsState state
            }

        loop None Waiting

module MainHandler =
    type msg = MessageCreated of DiscordClient * EventArgs.MessageCreateEventArgs

    type state = Map<uint64, IActorRef<GuildHandlerMsg.t>>

    let behavior (m: Actor<msg>) =
        let rec loop state =
            actor {
                let! msg = m.Receive()

                match msg with
                | MessageCreated(discord, event) ->
                    let guildId = event.Guild.Id

                    let guildHandlerRef, state =
                        match Map.tryFind guildId state with
                        | Some guildHandlerRef -> guildHandlerRef, state
                        | None ->
                            let guildHandlerRef = spawn m (string guildId) (props GuildHandler.behavior)
                            let state = Map.add guildId guildHandlerRef state
                            guildHandlerRef, state

                    guildHandlerRef <! GuildHandlerMsg.Message(discord, event)
                    return loop state
            }

        loop Map.empty

[<EntryPoint>]
let main argv =
    use discord =
        new DiscordClient(
            DiscordConfiguration(
                Token = argv.[0],
                Intents = (DiscordIntents.AllUnprivileged ||| DiscordIntents.MessageContents)
            )
        )

    let system = System.create "sys" <| Configuration.defaultConfig ()

    let mainRef = spawn system "main" (props MainHandler.behavior)

    discord.UseVoiceNext() |> ignore

    discord.add_MessageCreated (
        (fun discord event ->
            mainRef <! MainHandler.MessageCreated(discord, event)
            Task.CompletedTask)
    )

    discord.ConnectAsync() |> Async.AwaitTask |> Async.RunSynchronously

    Task.Delay(-1) |> Async.AwaitTask |> Async.RunSynchronously

    0
