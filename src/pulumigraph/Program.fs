// Learn more about F# at http://fsharp.org

open System
open System.Diagnostics

open Argu
type Arguments =
    | [<AltCommandLine("-c")>] Connection_String of string
    | [<AltCommandLine("-h")>] Host of string
    | [<AltCommandLine("-p")>] Port of string
    | [<AltCommandLine("-pass")>] Password of string  
    | [<AltCommandLine("-u")>] User of string
    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Connection_String _ -> "Optional connection string to neo4j, if not provided bolt://localhost:7687 will be used, take precedence over host and port."
            | Host _ -> "Optional host name, will only be used if provided and connection isn't specified"
            | Port _ -> "Optional port, will only be used if provided and connection isn't specified, will default to 7687 if not provided"
            | User _ -> "Optional user, if not provided anonymous authentication will be used (the server must be configured to allow that)"
            | Password _ -> "Optional password, required if user is provided"

type Neo4jConnectionConfig =
    | Anonymous of string
    | Authenticated of connectionString: string * user: string * password: string

[<RequireQualifiedAccess>]
module Result =
    let sequence (x: List<Result<'a, 'b>>): Result<'a list, 'b> =
        x
        |> List.fold (
                fun curr r ->
                    match curr, r with
                    | Error e, _ -> Error e
                    | _, Error e -> Error e
                    | Ok list, Ok item -> item::list |> Ok
            ) ([] |> Ok)

    let flatten (r: Result<Result<'a, 'b>, 'b>): Result<'a, 'b> =
        match r with
        | Error e -> Error e
        | Ok (Error e) -> Error e
        | Ok (Ok x) -> Ok x

[<RequireQualifiedAccess>]
module Process =
    let execute cmd (args: string list) =
        use cmdProcess =
            new Process(
                StartInfo = ProcessStartInfo(
                    FileName = cmd,
                    Arguments = String.Join(" ", args),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                )
            )
        cmdProcess.Start() |> ignore
        cmdProcess.StandardOutput.ReadToEnd()

[<RequireQualifiedAccess>]
module Pulumi =
    open System
    open Thoth.Json.Net

    module Dto =
        type Stack = 
            {
                Name: string
                Current: bool
                UpdateInProgress: bool
                Url: string
                ResourceCount: int
                LastUpdate: DateTime
            }
            static member Decoder : Decoder<Stack> =
                Decode.object
                    (fun get ->
                        { 
                            Name = get.Required.Field "name" Decode.string
                            Current = get.Required.Field "current" Decode.bool
                            UpdateInProgress = get.Required.Field "updateInProgress" Decode.bool
                            Url = get.Required.Field "url" Decode.string
                            ResourceCount = 
                                get.Optional.Field "resourceCount" Decode.int
                                |> Option.defaultValue 0
                            LastUpdate = 
                                get.Optional.Field "lastUpdate" Decode.datetime
                                |> Option.defaultValue DateTime.MinValue
                        }
                    )

        type Resource =
            {
                Urn: string
                Type: string
                Parent: string option
                Id: string
                Dependencies: string list
                Provider: string option
            }
            static member Decoder : Decoder<Resource> =
                Decode.object
                    (fun get ->
                        { 
                            Urn = get.Required.Field "urn" Decode.string
                            Type = get.Required.Field "type" Decode.string
                            Parent = get.Optional.Field "parent" Decode.string
                            Id = get.Optional.Field "id" Decode.string |> Option.defaultValue ""
                            Dependencies = 
                                get.Optional.Field "dependencies" (Decode.list Decode.string)
                                |> Option.defaultValue []
                            Provider = get.Optional.Field "provider" Decode.string
                        }
                    )

        type Deployment =
            {
                Resources: Resource list
            }
            static member Decoder : Decoder<Deployment> =
                Decode.object
                    (fun get ->
                        { 
                            Resources = 
                                get.Required.Field "resources" (Decode.list Resource.Decoder)
                        }
                    )

        type StackExport =
            {
                Version: int
                Deployment: Deployment
            }
            static member Decoder : Decoder<StackExport> =
                Decode.object
                    (fun get ->
                        { 
                            Version = get.Required.Field "version" Decode.int
                            Deployment = get.Required.Field "deployment" Deployment.Decoder
                        }
                    )

    type Resource = {
        Urn: string
        Type: string
        Parent: string option
        Provider: string option
        Id: string
        Dependencies: string list
    }

    type Stack = {
        Name: string
        LastUpdate: DateTime
        Resources: Resource list
    }

    let getStackExport (stack: Dto.Stack) =
        Process.execute "pulumi" ["stack"; "export"; "-s"; stack.Name]
        |> Decode.fromString (Dto.StackExport.Decoder)

    let getStacks(): Result<Stack list, string> =
        Process.execute "pulumi" ["stack"; "ls"; "-a"; "-j"]
        |> Decode.fromString (Decode.list Dto.Stack.Decoder)
        |> Result.map (List.filter (fun s -> s.ResourceCount > 0))
        |> Result.bind (fun stacks ->
            stacks
            |> List.map (fun stack ->
                getStackExport stack
                |> Result.map (fun se -> 
                    let resources = 
                        se.Deployment.Resources
                        |> List.filter (fun r ->
                            [ "pulumi:providers:pulumi" ]
                            |> List.contains r.Type
                            |> not)
                        |> List.map (fun r -> {
                                Urn = r.Urn
                                Type = r.Type
                                Parent = r.Parent
                                Id = r.Id
                                Dependencies = r.Dependencies
                                Provider = r.Provider
                        })
                    {
                        Name = stack.Name
                        LastUpdate = stack.LastUpdate
                        Resources = resources
                    }
                )
            )
            |> Result.sequence
        )

module Graph =
    open Thoth.Json.Net
    let getNodeTypeLabel nodeCategory (resourceType: string) =
        let resourceLabel = resourceType.Replace("/", "__").Replace(":", "_")
        [nodeCategory; resourceLabel]

    let getResourceNodeTypeLabel = getNodeTypeLabel "Resource"
    let getProviderNodeTypeLabel = getNodeTypeLabel "Provider"

    type Node = 
        {
            NodeLabels: string list
            Urn: string
            Id: string
            NodeType: string
            Name: string
        }
        static member Encoder (node : Node) =
            Encode.object
                [ 
                    "nodeLabels", Encode.list (node.NodeLabels |> List.map Encode.string)
                    "urn", Encode.string node.Urn
                    "id", Encode.string node.Id
                    "nodeType", Encode.string node.NodeType
                ]

    type Urn = string
    type Edge =
        {
            EdgeType: string
            FromId: string
            ToId: string
        } 
        static member Encoder (edge : Edge) =
            Encode.object
                [ 
                    "edgeType", Encode.string edge.EdgeType
                    "fromId", Encode.string edge.FromId
                    "toId", Encode.string edge.ToId
                ]
    let getNodes (stack: Pulumi.Stack): (Node list) =
        stack.Resources |> List.iter (fun r -> printfn "==> Type: %s" r.Type)
        stack.Resources
        |> List.map (fun r ->
            if r.Type = "pulumi:pulumi:Stack"
            then 
                {
                    NodeLabels = ["Stack"]
                    Urn = r.Urn
                    Id = stack.Name
                    NodeType = r.Type
                    Name = stack.Name
                }
            else if r.Type.Contains("pulumi:providers")
            then
                {
                    NodeLabels = (getProviderNodeTypeLabel r.Type)
                    Urn = r.Urn
                    Id = (sprintf "%s::%s" r.Urn r.Id)
                    NodeType = r.Type
                    Name = r.Id
                }
            else
                {
                    NodeLabels = (getResourceNodeTypeLabel r.Type)
                    Urn = r.Urn
                    Id = r.Urn
                    NodeType = r.Type
                    Name = r.Id
                })

    let getEdges (stack: Pulumi.Stack): (Edge list) =
        let stackResource = stack.Resources |> List.find (fun r -> r.Type = "pulumi:pulumi:Stack")
        let getToId parent =
            if parent = stackResource.Urn then stack.Name else parent
        stack.Resources
        |> List.collect (fun s -> 
            seq {
                if s.Parent |> Option.isSome
                then
                    yield {EdgeType = "HAS_PARENT"; FromId = s.Urn; ToId = (s.Parent |> Option.get |> getToId) }
                if s.Provider |> Option.isSome
                then
                    yield {EdgeType = "USES_PROVIDER"; FromId = s.Urn; ToId = (s.Provider |> Option.get)}
                if s.Type = "pulumi:pulumi:StackReference"
                then yield {EdgeType = "DEPENDS_ON_STACK"; FromId = s.Urn; ToId = s.Id }
                yield! 
                    s.Dependencies 
                    |> List.map (fun d -> { EdgeType = "DEPENDS_ON"; FromId = s.Urn; ToId = d })
            } |> List.ofSeq
        )

module Neo4j =
    open Graph
    open Thoth.Json.Net
    open Neo4j.Driver

    let connect conf =
        match conf with
        | Anonymous uri ->
            GraphDatabase.Driver(uri)
        | Authenticated (uri, user, password) ->
            GraphDatabase.Driver(uri, AuthTokens.Basic(user, password))

    let runTask a = a |> Async.AwaitTask |> Async.RunSynchronously
    let deleteAll (client: IDriver) =
        let session = client.AsyncSession()
        let tx = session.BeginTransactionAsync() |> runTask
        tx.RunAsync("""
MATCH ()-[r]-()
DELETE r
        """) |> runTask |> ignore
        tx.RunAsync("""
MATCH (n)
DELETE n
        """) |> runTask |> ignore
        tx.CommitAsync() |> Async.AwaitTask |> Async.RunSynchronously

    let writeNodes (client: IDriver) (nodes: Node list) =
        let query = """
UNWIND $nodes as row
CALL apoc.merge.node(row.NodeLabels, {id: row.Id, type: row.NodeType, urn: row.Id, name: row.Name}) YIELD node
RETURN node
        """

        let session = client.AsyncSession()
        session.WriteTransactionAsync(
            Func<IAsyncTransaction, System.Threading.Tasks.Task>(
                fun (tx: IAsyncTransaction)  ->
                    tx.RunAsync(query, {|nodes = nodes|}) :> System.Threading.Tasks.Task
            )) |> Async.AwaitTask |> Async.RunSynchronously        

    let writeEdges (client: IDriver) (edges: Edge list) =
        printfn "==> Edges: %A" (edges |> List.filter (fun e -> e.FromId.ToLower().Contains("application")))
        let query = """
UNWIND $edges as row
MATCH (fromNode {id: row.FromId})
MATCH (toNode {id: row.ToId})
CALL apoc.create.relationship(fromNode, row.EdgeType, {}, toNode) YIELD rel
REMOVE rel.noOp
"""

        let session = client.AsyncSession()
        session.WriteTransactionAsync(
            Func<IAsyncTransaction, System.Threading.Tasks.Task>(
                fun (tx: IAsyncTransaction)  ->
                    tx.RunAsync(query, {|edges = edges|}) :> System.Threading.Tasks.Task
            )) |> Async.AwaitTask |> Async.RunSynchronously        


let execute neo4jConf =
    let stacks = Pulumi.getStacks()
    printfn "==> Stacks: %A" (stacks |> Result.map(fun stacks -> stacks |> List.filter (fun stack -> stack.Name.Contains("Application"))))
    let allNodes =
        stacks
        |> Result.map (List.collect Graph.getNodes)

    let allEdges =
        stacks
        |> Result.map (List.collect Graph.getEdges)

    let client = Neo4j.connect neo4jConf
    Neo4j.deleteAll client
    match allNodes, allEdges with
    | Ok nodes, Ok edges ->
        Neo4j.writeNodes client nodes
        Neo4j.writeEdges client edges
    | Error ne, _ -> failwith (sprintf "Node error: %s" ne)
    | _, Error ee -> failwith (sprintf "Edge error: %s" ee)

let parseArgs (argv: string[]) =
    let argParser = ArgumentParser.Create<Arguments>(programName = "pulumigraph")
    let args : ParseResults<Arguments> = argParser.Parse(argv)
    let connectionString =
        match args.Contains(Connection_String), args.Contains(Host), args.Contains(Port) with
        | true, _, _ -> args.GetResult(Connection_String)
        | false, true, false -> sprintf "bolt://%s:7687" (args.GetResult(Host))
        | false, true, true -> sprintf "bolt://%s:%s" (args.GetResult(Host)) (args.GetResult(Port))
        | _, _, _ -> "bolt://localhost:7687"
    match args.Contains(User), args.Contains(Password) with
    | true, true -> 
        Authenticated (connectionString, args.GetResult(User), args.GetResult(Password)) |> Some
    | true, false ->
        printfn "==> Password required if user is provided"
        printfn "%s" (argParser.PrintUsage())
        None
    | false, false ->
        Anonymous connectionString |> Some
    | _, _ ->
        printfn "%s" (argParser.PrintUsage())
        None

[<EntryPoint>]
let main argv =
    let argParser = ArgumentParser.Create<Arguments>(programName = "pulumigraph")
    let arguments = argParser.Parse(argv)
    match parseArgs argv with
    | Some neo4jConf ->
        printfn "==> Uploading data to neo4j"
        execute neo4jConf
        printfn "==> Done uploading data to neo4j"
        0
    | None -> 1
