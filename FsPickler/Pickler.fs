﻿namespace FsPickler

    open System
    open System.IO
    open System.Text
    open System.Runtime.CompilerServices
    open System.Collections.Generic
    open System.Runtime.Serialization

    open FsPickler.Utils
    open FsPickler.Header


    [<AutoSerializable(false)>]
    [<AbstractClass>]
    type Pickler =

        val private declared_type : Type
        val private is_recursive_type : bool

        val mutable private m_isInitialized : bool

        val mutable private m_pickler_type : Type
        val mutable private m_typeInfo : TypeInfo
        val mutable private m_typeHash : TypeHash

        val mutable private m_picklerInfo : PicklerInfo
        val mutable private m_isCacheByRef : bool
        val mutable private m_useWithSubtypes : bool

        val mutable private m_source : string

        internal new (t : Type) =
            {
                declared_type = t ; 
                is_recursive_type = isRecursiveType t ;

                m_isInitialized = false ;

                m_pickler_type = Unchecked.defaultof<_> ; 
                m_typeInfo = Unchecked.defaultof<_> ; 
                m_typeHash = Unchecked.defaultof<_> ;
                m_picklerInfo = Unchecked.defaultof<_> ; 
                m_isCacheByRef = Unchecked.defaultof<_> ; 
                m_useWithSubtypes = Unchecked.defaultof<_> ;

                m_source = null ;
            }

        internal new (t : Type, picklerInfo, isCacheByRef, useWithSubtypes) =
            {
                declared_type = t ; 
                is_recursive_type = isRecursiveType t ;

                m_isInitialized = true ;

                m_pickler_type = t ; 
                m_typeInfo = computeTypeInfo t ; 
                m_typeHash = computeTypeHash t ;

                m_picklerInfo = picklerInfo ;
                m_isCacheByRef = isCacheByRef ;
                m_useWithSubtypes = useWithSubtypes ;

                m_source = null ;
            }

        member f.Type = f.declared_type
        member f.IsRecursiveType = f.is_recursive_type

        member internal f.TypeInfo = f.m_typeInfo
        member internal f.TypeHash = f.m_typeHash
        member f.ResolverName
            with get () = f.m_source
            and internal set id = f.m_source <- id

        member f.PicklerType =
            if f.m_isInitialized then f.m_pickler_type
            else
                invalidOp "Attempting to consume pickler at construction time."

        member f.PicklerInfo =
            if f.m_isInitialized then f.m_picklerInfo
            else
                invalidOp "Attempting to consume pickler at construction time."

        member f.IsCacheByRef =
            if f.m_isInitialized then f.m_isCacheByRef
            else
                invalidOp "Attempting to consume pickler at construction time."

        member f.UseWithSubtypes =
            if f.m_isInitialized then f.m_useWithSubtypes
            else
                invalidOp "Attempting to consume pickler at construction time."

        member internal f.IsInitialized = f.m_isInitialized

        abstract member UntypedWrite : Writer -> obj -> unit
        abstract member UntypedRead : Reader -> obj

        abstract member ManagedWrite : Writer -> obj -> unit
        abstract member ManagedRead : Reader -> obj

        abstract member Cast<'S> : unit -> Pickler<'S>

        abstract member InitializeFrom : Pickler -> unit
        default f.InitializeFrom(f' : Pickler) : unit =
            if f.m_isInitialized then
                invalidOp "Pickler has already been initialized."
            elif not f'.m_isInitialized then 
                invalidOp "Attempting to consume pickler at construction time."
            elif f.Type <> f'.Type && not (f'.Type.IsAssignableFrom(f.Type) && f'.UseWithSubtypes) then
                raise <| new InvalidCastException(sprintf "Cannot cast pickler from %O to %O." f'.Type f.Type)
            else
                f.m_pickler_type <- f'.m_pickler_type
                f.m_typeHash <- f'.m_typeHash
                f.m_typeInfo <- f'.m_typeInfo
                f.m_picklerInfo <- f'.m_picklerInfo
                f.m_isCacheByRef <- f'.m_isCacheByRef
                f.m_useWithSubtypes <- f'.m_useWithSubtypes
                f.m_isInitialized <- true

    and [<Sealed>][<AutoSerializable(false)>] Pickler<'T> =
        inherit Pickler
        
        val mutable private m_writer : Writer -> 'T -> unit
        val mutable private m_reader : Reader -> 'T

        internal new (reader, writer, picklerInfo, isCacheByRef, useWithSubtypes) = 
            { 
                inherit Pickler(typeof<'T>, picklerInfo, isCacheByRef, useWithSubtypes) ;
                m_writer = writer ;
                m_reader = reader ;
            }

        private new (t, reader, writer, picklerInfo, isCacheByRef, useWithSubtypes) = 
            { 
                inherit Pickler(t, picklerInfo, isCacheByRef, useWithSubtypes) ;
                m_writer = writer ;
                m_reader = reader ;
            }

        internal new () = 
            {
                inherit Pickler(typeof<'T>) ;
                m_writer = fun _ _ -> invalidOp "Attempting to consume pickler at construction time." ;
                m_reader = fun _ -> invalidOp "Attempting to consume pickler at construction time." ;
            }

        override f.UntypedWrite (w : Writer) (o : obj) = f.m_writer w (fastUnbox<'T> o)
        override f.UntypedRead (r : Reader) = f.m_reader r :> obj
        override f.ManagedWrite (w : Writer) (o : obj) = w.Write(f, fastUnbox<'T> o)
        override f.ManagedRead (r : Reader) = r.Read f :> obj

        override f.Cast<'S> () =
            if typeof<'T> = typeof<'S> then f |> fastUnbox<Pickler<'S>>
            elif typeof<'T>.IsAssignableFrom typeof<'S> && f.UseWithSubtypes then
                let writer = let wf = f.m_writer in fun w x -> wf w (fastUnbox<'T> x)
                let reader = let rf = f.m_reader in fun r -> rf r |> fastUnbox<'S>
                new Pickler<'S>(typeof<'T>, reader, writer, f.PicklerInfo, f.IsCacheByRef, f.UseWithSubtypes)
            else
                raise <| new InvalidCastException(sprintf "Cannot cast pickler of type '%O' to type '%O'." typeof<'T> typeof<'S>)
                

        override f.InitializeFrom(f' : Pickler) : unit =
            let f' = f'.Cast<'T> ()
            base.InitializeFrom f'
            f.m_writer <- f'.m_writer
            f.m_reader <- f'.m_reader
            

        member internal f.Write = f.m_writer
        member internal f.Read = f.m_reader


    and IPicklerResolver =
        abstract Id : string
        abstract Resolve : Type -> Pickler
        abstract Resolve<'T> : unit -> Pickler<'T>

    and [<AutoSerializable(false)>]
        Writer internal (stream : Stream, resolver : IPicklerResolver, ?streamingContext, ?leaveOpen, ?encoding) =
        
        do if not stream.CanWrite then invalidOp "Cannot write to stream."

        // using UTF8 gives an observed performance improvement ~200%
        let encoding = defaultArg encoding Encoding.UTF8

        let bw = new BinaryWriter(stream, encoding, defaultArg leaveOpen true)
        let sc = initStreamingContext streamingContext
        let idGen = new ObjectIDGenerator()
        let objStack = new Stack<int64> ()
        let cyclicObjects = new SortedSet<int64> ()

        let tyPickler = resolver.Resolve<Type> ()

        member w.BinaryWriter = bw

        member w.StreamingContext = sc

        member internal w.Resolver = resolver

        // the primary serialization routine; handles all the caching, subtype resolution logic, etc
        member w.Write<'T> (fmt : Pickler<'T>, x : 'T) =

            let inline writeHeader (flags : byte) =
                bw.Write(ObjHeader.create fmt.TypeHash flags)

            let inline writeType (t : Type) =
                let mutable firstOccurence = false
                let id = idGen.GetId(t, &firstOccurence)
                bw.Write firstOccurence
                if firstOccurence then tyPickler.Write w t
                else
                    bw.Write id

            let inline write header =
                if fmt.TypeInfo <= TypeInfo.Sealed || fmt.UseWithSubtypes then
                    writeHeader header
                    fmt.Write w x
                else
                    // object might be of proper subtype, perform reflection resolution
                    let t0 = x.GetType()
                    if t0 <> fmt.Type then
                        let fmt' = resolver.Resolve t0
                        writeHeader (header ||| ObjHeader.isProperSubtype)
                        writeType t0
                        fmt'.UntypedWrite w x
                    else
                        writeHeader header
                        fmt.Write w x

            if fmt.TypeInfo <= TypeInfo.Value then 
                writeHeader ObjHeader.empty
                fmt.Write w x

            elif obj.ReferenceEquals(x, null) then writeHeader ObjHeader.isNull else

            do RuntimeHelpers.EnsureSufficientExecutionStack()

            if fmt.IsCacheByRef || fmt.IsRecursiveType then
                let id, firstOccurence = idGen.GetId x

                if firstOccurence then
                    // push id to the symbolic stack to detect cyclic objects during traversal
                    objStack.Push id
                    write ObjHeader.isNewCachedInstance
                    objStack.Pop () |> ignore
                    cyclicObjects.Remove id |> ignore

                elif objStack.Contains id && not <| cyclicObjects.Contains id then
                    // came across cyclic object, record fixup-related data
                    // cyclic objects are handled once per instance
                    // instanses of cyclic arrays are handled differently than other reference types

                    do cyclicObjects.Add(id) |> ignore
                    
                    if fmt.TypeInfo <= TypeInfo.Sealed || fmt.UseWithSubtypes then
                        if fmt.TypeInfo = TypeInfo.Array then
                            writeHeader ObjHeader.isOldCachedInstance
                        else
                            writeHeader ObjHeader.isCyclicInstance
                    else
                        let t = x.GetType()

                        if t.IsArray then
                            writeHeader ObjHeader.isOldCachedInstance
                        elif t <> fmt.Type then
                            writeHeader (ObjHeader.isCyclicInstance ||| ObjHeader.isProperSubtype)
                            writeType t
                        else
                            writeHeader ObjHeader.isCyclicInstance

                    bw.Write id
                else
                    writeHeader ObjHeader.isOldCachedInstance
                    bw.Write id

            else
                write ObjHeader.empty

        member w.Write<'T>(t : 'T) = let f = resolver.Resolve<'T> () in w.Write(f, t)

        member internal w.WriteObj(t : Type, o : obj) =
            let f = resolver.Resolve t in f.ManagedWrite w o

        interface IDisposable with
            member __.Dispose () = bw.Dispose ()

    and [<AutoSerializable(false)>] 
        Reader internal (stream : Stream, resolver : IPicklerResolver, ?streamingContext : obj, ?leaveOpen, ?encoding) =

        do if not stream.CanRead then invalidOp "Cannot read from stream."

        // using UTF8 gives an observed performance improvement ~200%
        let encoding = defaultArg encoding Encoding.UTF8

        let br = new BinaryReader(stream, encoding, defaultArg leaveOpen true)
        let sc = initStreamingContext streamingContext
        let objCache = new Dictionary<int64, obj> ()
        let fixupIndex = new Dictionary<int64, Type * obj> ()
        let tyPickler = resolver.Resolve<Type> ()

        let mutable counter = 1L
        let mutable currentDeserializedObjectId = 0L

        // objects deserialized with reflection-based rules are registered to the cache
        // at the initialization stage to support cyclic object graphs.
        member internal r.EarlyRegisterObject (o : obj) = 
            objCache.Add(currentDeserializedObjectId, o)

        member r.BinaryReader = br

        member r.StreamingContext = sc

        member internal r.Resolver = resolver

        // the primary deserialization routine; handles all the caching, subtype resolution logic, etc
        member r.Read(fmt : Pickler<'T>) : 'T =

            let inline readType () =
                if br.ReadBoolean () then
                    let t = tyPickler.Read r
                    objCache.Add(counter, t)
                    counter <- counter + 1L
                    t
                else
                    let id = br.ReadInt64()
                    objCache.[id] |> fastUnbox<Type>

            let inline read flags =
                if ObjHeader.hasFlag flags ObjHeader.isProperSubtype then
                    let t = readType ()
                    let fmt' = resolver.Resolve t
                    fmt'.UntypedRead r |> fastUnbox<'T>
                else
                    fmt.Read r

            let flags = ObjHeader.read fmt.Type fmt.TypeHash (br.ReadUInt32())

            if ObjHeader.hasFlag flags ObjHeader.isNull then fastUnbox<'T> null
            elif fmt.TypeInfo <= TypeInfo.Value then fmt.Read r
            elif ObjHeader.hasFlag flags ObjHeader.isCyclicInstance then
                // came across a nested instance of a cyclic object
                // crete an uninitialized object to the cache and schedule
                // reflection-based fixup at the root level.
                let t =
                    if ObjHeader.hasFlag flags ObjHeader.isProperSubtype then readType ()
                    else fmt.Type

                let id = br.ReadInt64()

                let x = FormatterServices.GetUninitializedObject(t)
                fixupIndex.Add(id, (t, x)) 
                objCache.Add(id, x)
                fastUnbox<'T> x

            elif ObjHeader.hasFlag flags ObjHeader.isNewCachedInstance then
                let id = counter
                currentDeserializedObjectId <- id
                counter <- counter + 1L

                let x = read flags

                let found, content = fixupIndex.TryGetValue id
                if found then
                    // deserialization reached root level of a cyclic object
                    // perform fixup by doing reflection-based field copying
                    let t,o = content
                    do shallowCopy t x o
                    fixupIndex.Remove id |> ignore
                    fastUnbox<'T> o
                else
                    objCache.[id] <- x ; x

            elif ObjHeader.hasFlag flags ObjHeader.isOldCachedInstance then
                let id = br.ReadInt64() in objCache.[id] |> fastUnbox<'T>
            else
                read flags

        member r.Read<'T> () : 'T = let f = resolver.Resolve<'T> () in r.Read f

        member internal r.ReadObj(t : Type) = let f = resolver.Resolve t in f.ManagedRead r

        interface IDisposable with
            member __.Dispose () = br.Dispose ()