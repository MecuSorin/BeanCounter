module BeanCounterApp
open System
open System.IO
open Elmish
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.FuncUI.Elmish
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Hosts
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Themes.Fluent
open Avalonia.Platform


type EventLabel = {
    name: string
    htmlColor: string
}
type Event = {
    eventLabel: EventLabel
    counter: int
    percentage: float
}

type Model = {
    events: Event list
    lastPress: DateTime
    total: int
    eventsLog: string list
    followUpMatrix: Map<string, Map<string, float>>
}

type Msg =
    | AddEvent of string
    | UndoAddEvent

let mutable logFilePath = "events.txt_log"
let mutable eventsFilePath = "events.txt"
let buttonCooldown = TimeSpan.FromSeconds(0.4)
let mutable colors = Map.empty
let mutable eventsDefaultOrder = Map.empty

let updateColors =
    List.map (fun ev -> ev.name, SolidColorBrush(Color.Parse ev.htmlColor))
    >> Map.ofList
    >> fun m -> colors <- m

let buildProbabilityMatrix eventsLog =
    let transitions =
        eventsLog
        |> Seq.pairwise
    let transitionCounts =
        transitions
        |> Seq.groupBy id
        |> Seq.map (fun (pair, list) -> pair, Seq.length list)
    let groupedCounts =
        transitionCounts
        |> Seq.groupBy (fun ((fromLabel, _), _) -> fromLabel)
        |> Seq.map (fun (fromLabel, transitions) ->
            let total = transitions |> Seq.sumBy snd |> float
            let probs =
                if 0.0 = total then
                    Map.empty
                else
                    transitions
                    |> Seq.map (fun ((_, toLabel), count) ->
                        toLabel, float count / total)
                    |> Map.ofSeq
            fromLabel, probs
        )
        |> Map.ofSeq
    groupedCounts

let addEventToLog short =
    File.AppendAllText(logFilePath, $"{short}\n")
let removeLastEventFromLog () =
    if not (File.Exists logFilePath) 
        then ()
        else
            let lines = File.ReadAllLines logFilePath |> Array.toList
            match lines with
                | [] -> ()
                | _ -> File.WriteAllLines(logFilePath, lines |> List.rev |> List.tail |> List.rev)
// #region Init
let initModel () =
    // Read event types from eventtypes.txt
    let eventTypes =
        if File.Exists eventsFilePath 
            then
                File.ReadAllLines eventsFilePath
                    |> Array.choose (fun line ->
                        let parts = line.Split '|'
                        if parts.Length = 2 
                            then Some { name = parts.[0].Trim(); htmlColor = parts.[1].Trim() }
                            else None)
                    |> Array.toList
        else [
            { name = "dStraight"; htmlColor = "#FF0000" }
            { name = "d1"; htmlColor = "#FF0000" }
            { name = "d2"; htmlColor = "#FF0000" }
            { name = "d3"; htmlColor = "#FF0000" }
            { name = "d4"; htmlColor = "#FF0000" }
            { name = "d5"; htmlColor = "#FF0000" }
            { name = "uStraight"; htmlColor = "#00FF00" }
            { name = "u1"; htmlColor = "#00FF00" }
            { name = "u2"; htmlColor = "#00FF00" }
            { name = "u3"; htmlColor = "#00FF00" }
            { name = "u4"; htmlColor = "#00FF00" }
            { name = "u5"; htmlColor = "#00FF00" }
        ]
    updateColors eventTypes
    eventsDefaultOrder <-
        eventTypes
            |> List.mapi (fun i ev -> ev.name, i)
            |> Map.ofList
    // Read event logs and compute counters
    let logEntries =
        if File.Exists logFilePath 
            then
                File.ReadAllLines logFilePath
                    |> Array.map (fun l -> l.Trim())
                    |> Array.filter (fun l -> l <> "")
                    |> Array.toList
        else [ ]

    let eventCounts =
        logEntries
        |> List.groupBy id
        |> List.map (fun (short, entries) -> short, List.length entries)
        |> Map.ofList

    let totalCount = logEntries.Length

    let events =
        eventTypes
        |> List.map (fun et ->
            let count = eventCounts |> Map.tryFind et.name |> Option.defaultValue 0
            {   eventLabel = et
                counter = count
                percentage = if totalCount = 0 then 0.0 else float count / float totalCount * 100.0
            })
    let followUpMatrix = buildProbabilityMatrix logEntries
    { 
        events = events
        total = totalCount
        lastPress = DateTime.Now
        eventsLog = logEntries
        followUpMatrix = followUpMatrix
    }
// #region Update
let update (msg: Msg) (model: Model) =
    let now = DateTime.Now
    if now - model.lastPress < buttonCooldown 
        then model, Cmd.none
        else
            match msg with
                | AddEvent short ->
                    model.events
                        |> List.tryFind (fun ev -> ev.eventLabel.name = short)
                        |> Option.map (fun ev ->
                            let updatedEvent = { ev with counter = ev.counter + 1 }
                            let total = model.total + 1
                            let updatedEvents = 
                                model.events 
                                |> List.map (fun ev -> 
                                    let myEvent = if ev.eventLabel.name = short then updatedEvent else ev
                                    { myEvent with
                                        percentage = if 0 = total then 0 else float myEvent.counter / float total * 100.0 })
                            addEventToLog short
                            let inMemoryLog = model.eventsLog @ [short]
                            { events = updatedEvents; total = total; lastPress = now; followUpMatrix = buildProbabilityMatrix inMemoryLog; eventsLog = inMemoryLog })
                        |> Option.defaultValue model
                    , Cmd.none

                | UndoAddEvent ->
                    removeLastEventFromLog ()
                    initModel (), Cmd.none
// #region View Helpers
let centeredTextBlockInGrid text (color: Brush) gridRow gridColumn =
    TextBlock.create [
        TextBlock.foreground color
        TextBlock.text text
        TextBlock.horizontalAlignment HorizontalAlignment.Center
        TextBlock.verticalAlignment VerticalAlignment.Center
        Grid.row gridRow
        Grid.column gridColumn
    ] :> FuncUI.Types.IView

let centeredTextBlock text =
    TextBlock.create [
        TextBlock.foreground Brushes.White
        TextBlock.text text
        TextBlock.horizontalAlignment HorizontalAlignment.Center
        TextBlock.verticalAlignment VerticalAlignment.Center
    ] :> FuncUI.Types.IView

let followUpCell followUpValue gridRow gridColumn =
    let label = Math.Round(followUpValue * 100.0, 0) |> int |> string
    Border.create [
        Border.borderThickness (Thickness 1.0)
        Border.borderBrush Brushes.Gray
        Border.background (SolidColorBrush(Colors.RoyalBlue, followUpValue))
        Border.child (centeredTextBlock label)
        Border.margin (Thickness 2.0)
        Grid.row gridRow
        Grid.column gridColumn
    ] :> FuncUI.Types.IView
                    
let viewFollowUpMatrix (model: Model) =
    let matrix = model.followUpMatrix

    let eventLabels = 
        matrix
        |> Seq.collect(fun kvp -> 
            seq {
                yield kvp.Key //fromLabel
                yield! kvp.Value |> Seq.map (fun kkvp -> kkvp.Key ) //toLabel
            })
        |> Seq.distinct
        |> Seq.toArray
        |> Array.sortBy (fun name -> Map.tryFind name eventsDefaultOrder |> Option.defaultValue Int32.MaxValue)

    if 0 = eventLabels.Length 
        then centeredTextBlockInGrid "No events recorded yet." (SolidColorBrush Colors.White) 0 1
        else
            let rowDefinitions = "Auto, " + (eventLabels |> Seq.map(fun _ -> "*") |> String.concat ", ")

            let grid = 
                Grid.create [
                    Grid.columnDefinitions rowDefinitions
                    Grid.rowDefinitions rowDefinitions
                    Grid.row 0
                    Grid.column 2
                    Grid.minWidth 600.0
                    Grid.children [
                        for r = 1 to eventLabels.Length do
                            let fromLabel = eventLabels.[r - 1]
                            let color = Map.tryFind eventLabels.[r - 1] colors |> Option.defaultValue (SolidColorBrush Colors.White)
                            yield centeredTextBlockInGrid fromLabel color r 0
                            yield centeredTextBlockInGrid fromLabel color 0 r
                            for c = 1 to eventLabels.Length do
                                let toLabel = eventLabels.[c - 1]
                                let opacity = Map.tryFind (fromLabel) matrix
                                                |> Option.bind (Map.tryFind toLabel)
                                                |> Option.defaultValue 0.0
                                yield followUpCell opacity r c
                    ]
                ]
            grid
    

let viewInput (model: Model) (dispatch: Msg -> unit) =
    let eventsStackPanels =
        [ 
            for ev in model.events do
                let eventCount = String.Format("{0,3}", ev.counter)
                let percentage = String.Format("{0,3}", Math.Round(ev.percentage, 0))
                let label = $"{eventCount}\t | \t{ ev.eventLabel.name } \t| \t{percentage}%%"
                yield 
                    Border.create [
                        Border.borderThickness (Thickness 2.0)
                        Border.cornerRadius (CornerRadius 6.0)
                        Border.borderBrush Brushes.Gray
                        Border.background (SolidColorBrush(Colors.Black, 0.01))
                        Border.child (
                            TextBlock.create [
                                TextBlock.text label
                                TextBlock.foreground (SolidColorBrush(Color.Parse(ev.eventLabel.htmlColor)))
                                TextBlock.margin (Thickness 10.0)
                            ]
                        )
                        Border.onPointerPressed (
                            fun e ->
                                if e.GetCurrentPoint(null).Properties.IsLeftButtonPressed 
                                    then 
                                        dispatch (AddEvent ev.eventLabel.name)
                                        e.Handled <- true
                                    elif e.GetCurrentPoint(null).Properties.IsRightButtonPressed 
                                        then 
                                            dispatch UndoAddEvent
                                            e.Handled <- true
                            , SubPatchOptions.Always)
                    ] :> FuncUI.Types.IView
            yield 
                TextBlock.create [
                    TextBlock.text $"Total: {model.total}"
                    TextBlock.foreground Brushes.White
                    TextBlock.margin (Thickness 10.0)
                ] :> FuncUI.Types.IView
        ]
    StackPanel.create [
        StackPanel.children eventsStackPanels
        Grid.row 0
        Grid.column 0
        StackPanel.orientation Orientation.Vertical
        StackPanel.spacing 10.0
        StackPanel.margin (Thickness 10.0)
        StackPanel.onPointerPressed (
            fun e -> 
                if e.GetCurrentPoint(null).Properties.IsRightButtonPressed 
                    then 
                        dispatch UndoAddEvent
                        e.Handled <- true
            , SubPatchOptions.Always)
    ]
            
let view (model: Model) (dispatch: Msg -> unit) =
    Grid.create [
        Grid.rowDefinitions "Auto, Auto"
        Grid.columnDefinitions "Auto,* , Auto"
        Grid.children [
            viewInput model dispatch
            viewFollowUpMatrix model
        ]
    ]



// #region Window
type MainWindow() as this =
    inherit HostWindow()
    let initialModel = initModel()
    let init _ =
        initialModel, Cmd.none
    do
        let eventsFileNameStem = 
            if File.Exists eventsFilePath
                then System.IO.Path.GetFileNameWithoutExtension eventsFilePath
                else ""
        base.Title <- "Mecu Statistics" + eventsFileNameStem
        base.MinWidth <- 700.0
        base.MinHeight <- 100.0
        base.SizeToContent <- SizeToContent.WidthAndHeight
        Program.mkProgram init update view
            |> Program.withHost this
            |> Program.run

// Entry point
type App() =
    inherit Application()

    override this.Initialize() =
        this.Styles.Add(FluentTheme())
        this.RequestedThemeVariant <- Styling.ThemeVariant.Dark

    override this.OnFrameworkInitializationCompleted() =
        match this.ApplicationLifetime with
        | :? IClassicDesktopStyleApplicationLifetime as desktopLifetime ->
            let mainWindow = MainWindow()
            
            // Set the window icon
            let iconUri = Uri("avares://BeanCounter/Resources/icon.ico")
            let asset = AssetLoader.Open iconUri
            if asset <> null then
                mainWindow.Icon <- WindowIcon asset
            desktopLifetime.MainWindow <- mainWindow
        | _ -> ()
// #region App
module Program =
    [<EntryPoint>]
    let main args =
        
        if args.Length = 1 
            then 
                let labelsFile = args.[0]
                if System.IO.File.Exists labelsFile 
                    then 
                        eventsFilePath <- labelsFile
                        logFilePath <- args.[0] + "_log"
                        printfn "Using event types from %s" labelsFile
                    else
                        printfn "Event types file %s does not exist. Using default." labelsFile
        try
            AppBuilder
                .Configure<App>()
                .UsePlatformDetect()
                .UseSkia()
                .StartWithClassicDesktopLifetime args
        with e ->
            printfn "An error occurred: %s" (e.ToString())
            23