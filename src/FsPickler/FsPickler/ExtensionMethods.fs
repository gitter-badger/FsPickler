﻿namespace Nessos.FsPickler

open System
open System.Runtime.Serialization

/// F# Extension methods for FsPickler

[<AutoOpen>]
module ExtensionMethods =

    /// Object pickle with type annotation
    type Pickle<'T> internal (bytes : byte []) =
        /// Byte array pickle
        member __.Bytes = bytes

    type FsPicklerSerializer with

        /// <summary>
        ///     Creates a type annotated pickle for given value.
        /// </summary>
        /// <param name="value">Value to be pickled.</param>
        /// <param name="streamingContext">streaming context.</param>
        /// <param name="encoding">encoding passed to the binary writer.</param>
        member fsp.PickleTyped(value : 'T, ?streamingContext, ?encoding) : Pickle<'T> = 
            let bytes = fsp.Pickle(value, ?streamingContext = streamingContext, ?encoding = encoding)
            new Pickle<'T>(bytes)

        /// <summary>
        ///     Deserializes a type annotated pickle.
        /// </summary>
        /// <param name="pickle">Type annotated pickle.</param>
        /// <param name="streamingContext">streaming context.</param>
        /// <param name="encoding">encoding passed to the binary reader.</param>
        member fsp.UnPickleTyped(pickle : Pickle<'T>, ?streamingContext, ?encoding) : 'T =
            fsp.UnPickle<'T>(pickle.Bytes, ?streamingContext = streamingContext, ?encoding = encoding)

    type Pickler with
        /// <summary>Initializes a pickler out of a pair of read/write lambdas. Unsafe pickler generation method.</summary>
        /// <param name="reader">Deserialization logic for the pickler.</param>
        /// <param name="writer">Serialization logic for the pickler.</param>
        /// <param name="cacheByRef">Specifies whether objects serialized by this pickler should be cached by reference.</param>
        /// <param name="useWithSubtypes">Specifies whether pickler should also apply for all subtypes.</param>
        static member FromPrimitives<'T>(reader : ReadState -> string -> 'T, writer : WriteState -> string -> 'T -> unit, ?cacheByRef, ?useWithSubtypes) =
            if typeof<'T>.IsPrimitive then
                invalidArg typeof<'T>.FullName "defining custom picklers for primitives not supported."

            let cacheByRef = defaultArg cacheByRef (not typeof<'T>.IsValueType)
            let useWithSubtypes = defaultArg useWithSubtypes false
            CompositePickler.Create(reader, writer, PicklerInfo.UserDefined, cacheByRef = cacheByRef, useWithSubtypes = useWithSubtypes)


    type SerializationInfo with
        /// <summary>
        ///     Adds value of given type to SerializationInfo instance.
        /// </summary>
        /// <param name="name">Name for value.</param>
        /// <param name="value">Input value.</param>
        member inline sI.Add<'T>(name : string, value : 'T) : unit =
            sI.AddValue(name, value, typeof<'T>)

        /// <summary>
        ///     Gets value of given type and provided name from SerializationInfo instance.
        /// </summary>
        /// <param name="name">Name for value.</param>
        member inline sI.Get<'T>(name : string) : 'T =
            sI.GetValue(name, typeof<'T>) :?> 'T

        /// <summary>
        ///     Try getting value of provided type and name from SerializationInfo instance.
        ///     Returns 'None' if not found.
        /// </summary>
        /// <param name="name">Name for value.</param>
        member sI.TryGet<'T>(name : string) : 'T option =
            // we use linear traversal; that's ok since entry count
            // is typically small and this is how it's done in the
            // proper SerializationInfo.GetValue() implementation.
            let e = sI.GetEnumerator()
            let mutable found = false
            let mutable entry = Unchecked.defaultof<SerializationEntry>
            while not found && e.MoveNext() do
                entry <- e.Current
                found <- entry.Name = name

            if found && entry.ObjectType = typeof<'T> then
                Some (entry.Value :?> 'T)
            else None

        /// <summary>
        ///     Try getting value of provided name from SerializationInfo instance.
        ///     Returns 'None' if not found.
        /// </summary>
        /// <param name="name">Name for value.</param>
        member sI.TryGetObj(name : string) : obj option =
            // we use linear traversal; that's ok since entry count
            // is typically small and this is how it's done in the
            // proper SerializationInfo.GetValue() implementation.
            let e = sI.GetEnumerator()
            let mutable found = false
            let mutable entry = Unchecked.defaultof<SerializationEntry>
            while not found && e.MoveNext() do
                entry <- e.Current
                found <- entry.Name = name

            if found then Some entry.Value
            else None