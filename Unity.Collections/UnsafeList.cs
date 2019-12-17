using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Unity.Burst;
using Unity.Jobs;
using Unity.Mathematics;

namespace Unity.Collections.LowLevel.Unsafe
{
    /// <summary>
    /// An unmanaged, untyped, resizable list, without any thread safety check features.
    /// </summary>
    [DebuggerDisplay("Length = {Length}, Capacity = {Capacity}, IsCreated = {IsCreated}")]
    public unsafe struct UnsafeList
    {
        [NativeDisableUnsafePtrRestriction]
        public void* Ptr;
        public int Length;
        public int Capacity;
        public Allocator Allocator;

        /// <summary>
        /// Constructs a new container using the specified type of memory allocation.
        /// </summary>
        /// <param name="allocator">A member of the
        /// [Unity.Collections.Allocator](https://docs.unity3d.com/ScriptReference/Unity.Collections.Allocator.html) enumeration.</param>
        /// <remarks>The list initially has a capacity of one. To avoid reallocating memory for the list, specify
        /// sufficient capacity up front.</remarks>
        public unsafe UnsafeList(Allocator allocator)
        {
            Ptr = null;
            Length = 0;
            Capacity = 0;
            Allocator = allocator;
        }

        /// <summary>
        /// Constructs container as view into memory.
        /// </summary>
        /// <param name="ptr">Pointer to data.</param>
        /// <param name="length">Lenght of data in bytes.</param>
        public unsafe UnsafeList(void* ptr, int length)
        {
            Ptr = ptr;
            Length = length;
            Capacity = length;
            Allocator = Allocator.Invalid;
        }

        /// <summary>
        /// Constructs a new container with the specified initial capacity and type of memory allocation.
        /// </summary>
        /// <param name="sizeOf">Size of element.</param>
        /// <param name="alignOf">Alignment of element.</param>
        /// <param name="initialCapacity">The initial capacity of the list. If the list grows larger than its capacity,
        /// the internal array is copied to a new, larger array.</param>
        /// <param name="allocator">A member of the
        /// [Unity.Collections.Allocator](https://docs.unity3d.com/ScriptReference/Unity.Collections.Allocator.html) enumeration.</param>
        /// <param name="options">Memory should be cleared on allocation or left uninitialized.</param>
        public unsafe UnsafeList(int sizeOf, int alignOf, int initialCapacity, Allocator allocator, NativeArrayOptions options = NativeArrayOptions.UninitializedMemory)
        {
            Allocator = allocator;
            Ptr = null;
            Length = 0;
            Capacity = 0;

            if (initialCapacity != 0)
            {
                SetCapacity(sizeOf, alignOf, initialCapacity);
            }

            if (options == NativeArrayOptions.ClearMemory
            && Ptr != null)
            {
                UnsafeUtility.MemClear(Ptr, Capacity * sizeOf);
            }
        }

        /// <summary>
        /// Creates a new container with the specified initial capacity and type of memory allocation.
        /// </summary>
        /// <param name="sizeOf">Size of element.</param>
        /// <param name="alignOf">Alignment of element.</param>
        /// <param name="initialCapacity">The initial capacity of the list. If the list grows larger than its capacity,
        /// the internal array is copied to a new, larger array.</param>
        /// <param name="allocator">A member of the
        /// [Unity.Collections.Allocator](https://docs.unity3d.com/ScriptReference/Unity.Collections.Allocator.html) enumeration.</param>
        /// <param name="options">Memory should be cleared on allocation or left uninitialized.</param>
        public static UnsafeList* Create(int sizeOf, int alignOf, int initialCapacity, Allocator allocator, NativeArrayOptions options = NativeArrayOptions.UninitializedMemory)
        {
            UnsafeList* listData = (UnsafeList*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<UnsafeList>(), UnsafeUtility.AlignOf<UnsafeList>(), allocator);
            UnsafeUtility.MemClear(listData, UnsafeUtility.SizeOf<UnsafeList>());

            listData->Allocator = allocator;

            if (initialCapacity != 0)
            {
                listData->SetCapacity(sizeOf, alignOf, initialCapacity);
            }

            if (options == NativeArrayOptions.ClearMemory
            && listData->Ptr != null)
            {
                UnsafeUtility.MemClear(listData->Ptr, listData->Capacity * sizeOf);
            }

            return listData;
        }

        /// <summary>
        /// Destroys container.
        /// </summary>
        public static void Destroy(UnsafeList* listData)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (listData == null)
            {
                throw new Exception("UnsafeList has yet to be created or has been destroyed!");
            }
#endif
            var allocator = listData->Allocator;
            listData->Dispose();
            UnsafeUtility.Free(listData, allocator);
        }

        /// <summary>
        /// Reports whether memory for the container is allocated.
        /// </summary>
        /// <value>True if this container object's internal storage has been allocated.</value>
        /// <remarks>Note that the container storage is not created if you use the default constructor. You must specify
        /// at least an allocation type to construct a usable container.</remarks>
        public bool IsCreated => Ptr != null;

        /// <summary>
        /// Disposes of this container and deallocates its memory immediately.
        /// </summary>
        public void Dispose()
        {
            if (Allocator != Allocator.Invalid)
            {
                UnsafeUtility.Free(Ptr, Allocator);
                Allocator = Allocator.Invalid;
            }

            Ptr = null;
            Length = 0;
            Capacity = 0;
        }

        /// <summary>
        /// Safely disposes of this container and deallocates its memory when the jobs that use it have completed.
        /// </summary>
        /// <remarks>You can call this function dispose of the container immediately after scheduling the job. Pass
        /// the [JobHandle](https://docs.unity3d.com/ScriptReference/Unity.Jobs.JobHandle.html) returned by
        /// the [Job.Schedule](https://docs.unity3d.com/ScriptReference/Unity.Jobs.IJobExtensions.Schedule.html)
        /// method using the `jobHandle` parameter so the job scheduler can dispose the container after all jobs
        /// using it have run.</remarks>
        /// <param name="jobHandle">The job handle or handles for any scheduled jobs that use this container.</param>
        /// <returns>A new job handle containing the prior handles as well as the handle for the job that deletes
        /// the container.</returns>
        public JobHandle Dispose(JobHandle inputDeps)
        {
            if (Allocator != Allocator.Invalid)
            {
                var jobHandle = new DisposeJob { Ptr = Ptr, Allocator = Allocator }.Schedule(inputDeps);

                Ptr = null;
                Allocator = Allocator.Invalid;

                return jobHandle;
            }

            Ptr = null;

            return default;
        }

        [BurstCompile]
        struct DisposeJob : IJob
        {
            [NativeDisableUnsafePtrRestriction]
            public void* Ptr;
            public Allocator Allocator;

            public void Execute()
            {
                UnsafeUtility.Free(Ptr, Allocator);
            }
        }

        /// <summary>
        /// Clears the list.
        /// </summary>
        /// <remarks>List Capacity remains unchanged.</remarks>
        public void Clear()
        {
            Length = 0;
        }

        /// <summary>
        /// Changes the list length, resizing if necessary.
        /// </summary>
        /// <param name="sizeOf">Size of element.</param>
        /// <param name="alignOf">Alignment of element.</param>
        /// <param name="length">The new length of the list.</param>
        /// <param name="options">Memory should be cleared on allocation or left uninitialized.</param>
        public void Resize(int sizeOf, int alignOf, int length, NativeArrayOptions options = NativeArrayOptions.UninitializedMemory)
        {
            var oldLength = Length;

            if (length > Capacity)
            {
                SetCapacity(sizeOf, alignOf, length);
            }

            Length = length;

            if (options == NativeArrayOptions.ClearMemory
            && oldLength < length)
            {
                var num = length - oldLength;
                byte* ptr = (byte*)Ptr;
                UnsafeUtility.MemClear(ptr + oldLength * sizeOf, num * sizeOf);
            }
        }

        /// <summary>
        /// Changes the list length, resizing if necessary.
        /// </summary>
        /// <typeparam name="T">Source type of elements</typeparam>
        /// <param name="length">The new length of the list.</param>
        /// <param name="options">Memory should be cleared on allocation or left uninitialized.</param>
        public void Resize<T>(int length, NativeArrayOptions options = NativeArrayOptions.UninitializedMemory) where T : struct
        {
            Resize(UnsafeUtility.SizeOf<T>(), UnsafeUtility.AlignOf<T>(), length, options);
        }

        void Realloc(int sizeOf, int alignOf, int capacity)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (Allocator == Allocator.Invalid)
            {
                throw new Exception("UnsafeList is not initialized, it must be initialized with allocator before use.");
            }
#endif
            void* newPointer = null;

            if (capacity > 0)
            {
                var bytesToMalloc = sizeOf * capacity;
                newPointer = UnsafeUtility.Malloc(bytesToMalloc, alignOf, Allocator);

                if (Capacity > 0)
                {
                    var itemsToCopy = Math.Min(capacity, Capacity);
                    var bytesToCopy = itemsToCopy * sizeOf;
                    UnsafeUtility.MemCpy(newPointer, Ptr, bytesToCopy);
                }
            }

            UnsafeUtility.Free(Ptr, Allocator);

            Ptr = newPointer;
            Capacity = capacity;
            Length = Math.Min(Length, capacity);
        }

        void SetCapacity(int sizeOf, int alignOf, int capacity)
        {
            var newCapacity = Math.Max(capacity, 64 / sizeOf);
            newCapacity = math.ceilpow2(newCapacity);

            if (newCapacity == Capacity)
            {
                return;
            }

            Realloc(sizeOf, alignOf, newCapacity);
        }

        /// <summary>
        /// Set the number of items that can fit in the list.
        /// </summary>
        /// <typeparam name="T">Source type of elements</typeparam>
        /// <param name="capacity">The number of items that the list can hold before it resizes its internal storage.</param>
        public void SetCapacity<T>(int capacity) where T : struct
        {
            SetCapacity(UnsafeUtility.SizeOf<T>(), UnsafeUtility.AlignOf<T>(), capacity);
        }

        /// <summary>
        /// Sets the capacity to the actual number of elements in the container.
        /// </summary>
        /// <typeparam name="T">Source type of elements</typeparam>
        public void TrimExcess<T>() where T : struct
        {
            if (Capacity != Length)
            {
                Realloc(UnsafeUtility.SizeOf<T>(), UnsafeUtility.AlignOf<T>(), Length);
            }
        }

        /// <summary>
        /// Searches for the specified element in list.
        /// </summary>
        /// <typeparam name="T">Source type of elements</typeparam>
        /// <param name="value"></param>
        /// <returns>The zero-based index of the first occurrence element if found, otherwise returns -1.</returns>
        public int IndexOf<T>(T value) where T : struct, IEquatable<T>
        {
            return NativeArrayExtensions.IndexOf<T, T>(Ptr, Length, value);
        }

        /// <summary>
        /// Determines whether an element is in the list.
        /// </summary>
        /// <typeparam name="T">Source type of elements</typeparam>
        /// <param name="value"></param>
        /// <returns>True, if element is found.</returns>
        public bool Contains<T>(T value) where T : struct, IEquatable<T>
        {
            return IndexOf(value) != -1;
        }

        /// <summary>
        /// Adds an element to the list.
        /// </summary>
        /// <typeparam name="T">Source type of elements</typeparam>
        /// <param name="value">The value to be added at the end of the list.</param>
        /// <remarks>
        /// If the list has reached its current capacity, internal array won't be resized, and exception will be thrown.
        /// </remarks>
        public void AddNoResize<T>(T value) where T : struct
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (Capacity < Length + 1)
            {
                throw new Exception($"AddNoResize assumes that list capacity is sufficient (Capacity {Capacity}, Lenght {Length})!");
            }
#endif
            UnsafeUtility.WriteArrayElement(Ptr, Length, value);
            Length += 1;
        }

        private void AddRangeNoResize(int sizeOf, int alignOf, void* ptr, int length)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (Capacity < Length + length)
            {
                throw new Exception($"AddRangeNoResize assumes that list capacity is sufficient (Capacity {Capacity}, Lenght {Length})!");
            }
#endif
            void* dst = (byte*)Ptr + Length * sizeOf;
            UnsafeUtility.MemCpy(dst, ptr, length * sizeOf);
            Length += length;
        }

        /// <summary>
        /// Adds elements from a buffer to this list.
        /// </summary>
        /// <typeparam name="T">Source type of elements</typeparam>
        /// <param name="ptr">A pointer to the buffer.</param>
        /// <param name="length">The number of elements to add to the list.</param>
        /// <remarks>
        /// If the list has reached its current capacity, internal array won't be resized, and exception will be thrown.
        /// </remarks>
        public void AddRangeNoResize<T>(void* ptr, int length) where T : struct
        {
            AddRangeNoResize(UnsafeUtility.SizeOf<T>(), UnsafeUtility.AlignOf<T>(), ptr, length);
        }

        /// <summary>
        /// Adds elements from a list to this list.
        /// </summary>
        /// <typeparam name="T">Source type of elements</typeparam>
        /// <remarks>
        /// If the list has reached its current capacity, internal array won't be resized, and exception will be thrown.
        /// </remarks>
        public void AddRangeNoResize<T>(UnsafeList list) where T : struct
        {
            AddRangeNoResize(UnsafeUtility.SizeOf<T>(), UnsafeUtility.AlignOf<T>(), list.Ptr, list.Length);
        }

        /// <summary>
        /// Adds an element to the list.
        /// </summary>
        /// <typeparam name="T">Source type of elements</typeparam>
        /// <param name="value">The value to be added at the end of the list.</param>
        /// <remarks>
        /// If the list has reached its current capacity, it copies the original, internal array to
        /// a new, larger array, and then deallocates the original.
        /// </remarks>
        public void Add<T>(T value) where T : struct
        {
            var idx = Length;

            if (Length + 1 > Capacity)
            {
                Resize<T>(idx + 1);
            }
            else
            {
                Length += 1;
            }

            UnsafeUtility.WriteArrayElement(Ptr, idx, value);
        }

        private void AddRange(int sizeOf, int alignOf, void* ptr, int length)
        {
            var idx = Length;

            if (Length + length > Capacity)
            {
                Resize(sizeOf, alignOf, Length + length);
            }
            else
            {
                Length += length;
            }

            void* dst = (byte*)Ptr + idx * sizeOf;
            UnsafeUtility.MemCpy(dst, ptr, length * sizeOf);
        }

        /// <summary>
        /// Adds elements from a buffer to this list.
        /// </summary>
        /// <typeparam name="T">Source type of elements</typeparam>
        /// <param name="ptr">A pointer to the buffer.</param>
        /// <param name="length">The number of elements to add to the list.</param>
        public void AddRange<T>(void* ptr, int length) where T : struct
        {
            AddRange(UnsafeUtility.SizeOf<T>(), UnsafeUtility.AlignOf<T>(), ptr, length);
        }

        /// <summary>
        /// Adds elements from a list to this list.
        /// </summary>
        /// <remarks>
        /// If the list has reached its current capacity, it copies the original, internal array to
        /// a new, larger array, and then deallocates the original.
        /// </remarks>
        /// <typeparam name="T">Source type of elements</typeparam>
        public void AddRange<T>(UnsafeList list) where T : struct
        {
            AddRange(UnsafeUtility.SizeOf<T>(), UnsafeUtility.AlignOf<T>(), list.Ptr, list.Length);
        }

        /// <summary>
        /// Truncates the list by replacing the item at the specified index with the last item in the list. The list
        /// is shortened by one.
        /// </summary>
        /// <typeparam name="T">Source type of elements</typeparam>
        /// <param name="index">The index of the item to delete.</param>
        public void RemoveAtSwapBack<T>(int index) where T : struct
        {
            RemoveRangeSwapBack<T>(index, index + 1);
        }

        private void RemoveRangeSwapBack(int sizeOf, int begin, int end)
        {
            int itemsToRemove = end - begin;
            if (itemsToRemove > 0)
            {
                int copyFrom = Math.Max(Length - itemsToRemove, end);
                void* dst = (byte*)Ptr + begin * sizeOf;
                void* src = (byte*)Ptr + copyFrom * sizeOf;
                UnsafeUtility.MemCpy(dst, src, Math.Min(itemsToRemove, Length - copyFrom) * sizeOf);
                Length -= itemsToRemove;
            }
        }

        /// <summary>
        /// Truncates the list by replacing the item at the specified index range with the items from the end the list. The list
        /// is shortened by number of elements in range.
        /// </summary>
        /// <typeparam name="T">Source type of elements</typeparam>
        /// <param name="begin">The first index of the item to delete.</param>
        /// <param name="end">The last index of the item to delete.</param>
        public void RemoveRangeSwapBack<T>(int begin, int end) where T : struct
        {
            RemoveRangeSwapBack(UnsafeUtility.SizeOf<T>(), begin, end);
        }

        /// <summary>
        /// Returns parallel reader instance.
        /// </summary>
        public ParallelReader AsParallelReader()
        {
            return new ParallelReader(Ptr, Length);
        }

        /// <summary>
        /// Implements parallel reader. Use AsParallelReader to obtain it from container.
        /// </summary>
        public unsafe struct ParallelReader
        {
            [NativeDisableUnsafePtrRestriction]
            public readonly void* Ptr;
            public readonly int Length;

            public ParallelReader(void* ptr, int length)
            {
                Ptr = ptr;
                Length = length;
            }

            public int IndexOf<T>(T value) where T : struct, IEquatable<T>
            {
                return NativeArrayExtensions.IndexOf<T, T>(Ptr, Length, value);
            }

            public bool Contains<T>(T value) where T : struct, IEquatable<T>
            {
                return IndexOf(value) != -1;
            }
        }

        /// <summary>
        /// Returns parallel writer instance.
        /// </summary>
        public ParallelWriter AsParallelWriter()
        {
            return new ParallelWriter(Ptr, (UnsafeList*)UnsafeUtility.AddressOf(ref this));
        }

        public unsafe struct ParallelWriter
        {
            [NativeDisableUnsafePtrRestriction]
            public readonly void* Ptr;

            [NativeDisableUnsafePtrRestriction]
            public UnsafeList* ListData;

            public unsafe ParallelWriter(void* ptr, UnsafeList* listData)
            {
                Ptr = ptr;
                ListData = listData;
            }

            /// <summary>
            /// Adds an element to the list.
            /// </summary>
            /// <typeparam name="T">Source type of elements</typeparam>
            /// <param name="value">The value to be added at the end of the list.</param>
            /// <remarks>
            /// If the list has reached its current capacity, internal array won't be resized, and exception will be thrown.
            /// </remarks>
            public void AddNoResize<T>(T value) where T : struct
            {
                var idx = Interlocked.Increment(ref ListData->Length) - 1;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (ListData->Capacity < idx + 1)
                {
                    throw new Exception($"AddNoResize assumes that list capacity is sufficient (Capacity {ListData->Capacity}, Lenght {ListData->Length})!");
                }
#endif
                UnsafeUtility.WriteArrayElement(Ptr, idx, value);
            }

            private void AddRangeNoResize(int sizeOf, int alignOf, void* ptr, int length)
            {
                var idx = Interlocked.Add(ref ListData->Length, length) - length;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (ListData->Capacity < idx + length)
                {
                    throw new Exception($"AddRangeNoResize assumes that list capacity is sufficient (Capacity {ListData->Capacity}, Lenght {ListData->Length})!");
                }
#endif
                void* dst = (byte*)Ptr + idx * sizeOf;
                UnsafeUtility.MemCpy(dst, ptr, length * sizeOf);
            }

            /// <summary>
            /// Adds elements from a buffer to this list.
            /// </summary>
            /// <typeparam name="T">Source type of elements</typeparam>
            /// <param name="ptr">A pointer to the buffer.</param>
            /// <param name="length">The number of elements to add to the list.</param>
            /// <remarks>
            /// If the list has reached its current capacity, internal array won't be resized, and exception will be thrown.
            /// </remarks>
            public void AddRangeNoResize<T>(void* ptr, int length) where T : struct
            {
                AddRangeNoResize(UnsafeUtility.SizeOf<T>(), UnsafeUtility.AlignOf<T>(), ptr, length);
            }

            /// <summary>
            /// Adds elements from a list to this list.
            /// </summary>
            /// <typeparam name="T">Source type of elements</typeparam>
            /// <remarks>
            /// If the list has reached its current capacity, internal array won't be resized, and exception will be thrown.
            /// </remarks>
            public void AddRangeNoResize<T>(UnsafeList list) where T : struct
            {
                AddRangeNoResize(UnsafeUtility.SizeOf<T>(), UnsafeUtility.AlignOf<T>(), list.Ptr, list.Length);
            }
        }
    }

    /// <summary>
    /// An managed, resizable list, without any thread safety check features.
    /// </summary>
    /// <typeparam name="T">Source type of elements</typeparam>
    [DebuggerDisplay("Length = {Length}, Capacity = {Capacity}, IsCreated = {IsCreated}")]
    [DebuggerTypeProxy(typeof(UnsafeListTDebugView<>))]
    public unsafe struct UnsafeList<T>
        where T : unmanaged, IEquatable<T>
    {
        [NativeDisableUnsafePtrRestriction]
        public T* Ptr;
        public int Length;
        public int Capacity;
        public Allocator Allocator;

        /// <summary>
        /// Constructs list as view into memory.
        /// </summary>
        public unsafe UnsafeList(T* ptr, int length)
        {
            Ptr = ptr;
            Length = length;
            Capacity = 0;
            Allocator = Allocator.Invalid;
        }

        /// <summary>
        /// Constructs a new list using the specified type of memory allocation.
        /// </summary>
        /// <param name="allocator">A member of the
        /// [Unity.Collections.Allocator](https://docs.unity3d.com/ScriptReference/Unity.Collections.Allocator.html) enumeration.</param>
        /// <param name="options">Memory should be cleared on allocation or left uninitialized.</param>
        /// <remarks>The list initially has a capacity of one. To avoid reallocating memory for the list, specify
        /// sufficient capacity up front.</remarks>
        public unsafe UnsafeList(int initialCapacity, Allocator allocator, NativeArrayOptions options = NativeArrayOptions.UninitializedMemory)
        {
            Ptr = null;
            Length = 0;
            Capacity = 0;
            Allocator = Allocator.Invalid;
            var sizeOf = UnsafeUtility.SizeOf<T>();
            this.ListData() = new UnsafeList(sizeOf, sizeOf, initialCapacity, allocator, options);
        }

        /// <summary>
        /// Reports whether memory for the list is allocated.
        /// </summary>
        /// <value>True if this list object's internal storage  has been allocated.</value>
        /// <remarks>Note that the list storage is not created if you use the default constructor. You must specify
        /// at least an allocation type to construct a usable UnsafeList.</remarks>
        public bool IsCreated => Ptr != null;

        /// <summary>
        /// Disposes of this container and deallocates its memory immediately.
        /// </summary>
        public void Dispose()
        {
            this.ListData().Dispose();
        }

        /// <summary>
        /// Safely disposes of this container and deallocates its memory when the jobs that use it have completed.
        /// </summary>
        /// <remarks>You can call this function dispose of the container immediately after scheduling the job. Pass
        /// the [JobHandle](https://docs.unity3d.com/ScriptReference/Unity.Jobs.JobHandle.html) returned by
        /// the [Job.Schedule](https://docs.unity3d.com/ScriptReference/Unity.Jobs.IJobExtensions.Schedule.html)
        /// method using the `jobHandle` parameter so the job scheduler can dispose the container after all jobs
        /// using it have run.</remarks>
        /// <param name="jobHandle">The job handle or handles for any scheduled jobs that use this container.</param>
        /// <returns>A new job handle containing the prior handles as well as the handle for the job that deletes
        /// the container.</returns>
        public JobHandle Dispose(JobHandle inputDeps)
        {
            return this.ListData().Dispose(inputDeps);
        }

        /// <summary>
        /// Clears the list.
        /// </summary>
        /// <remarks>List Capacity remains unchanged.</remarks>
        public void Clear()
        {
            this.ListData().Clear();
        }

        /// <summary>
        /// Changes the list length, resizing if necessary.
        /// </summary>
        /// <param name="length">The new length of the list.</param>
        /// <param name="options">Memory should be cleared on allocation or left uninitialized.</param>
        public void Resize(int length, NativeArrayOptions options = NativeArrayOptions.UninitializedMemory)
        {
            this.ListData().Resize<T>(length, options);
        }

        /// <summary>
        /// Set the number of items that can fit in the list.
        /// </summary>
        /// <param name="capacity">The number of items that the list can hold before it resizes its internal storage.</param>
        public void SetCapacity(int capacity)
        {
            this.ListData().SetCapacity<T>(capacity);
        }

        /// <summary>
        /// Sets the capacity to the actual number of elements in the container.
        /// </summary>
        public void TrimExcess()
        {
            this.ListData().TrimExcess<T>();
        }

        /// <summary>
        /// Searches for the specified element in list.
        /// </summary>
        /// <param name="value"></param>
        /// <returns>The zero-based index of the first occurrence element if found, otherwise returns -1.</returns>
        public int IndexOf(T value)
        {
            return this.ListData().IndexOf(value);
        }

        /// <summary>
        /// Determines whether an element is in the list.
        /// </summary>
        /// <param name="value"></param>
        /// <returns>True, if element is found.</returns>
        public bool Contains(T value)
        {
            return this.ListData().Contains(value);
        }

        /// <summary>
        /// Adds an element to the list.
        /// </summary>
        /// <param name="value">The struct to be added at the end of the list.</param>
        public void Add(T value)
        {
            this.ListData().Add(value);
        }

        /// <summary>
        /// Adds the elements of a UnsafePtrList to this list.
        /// </summary>
        /// <param name="list">The items to add.</param>
        public void AddRange(UnsafeList<T> src)
        {
            this.ListData().AddRange<T>(src.ListData());
        }

        /// <summary>
        /// Truncates the list by replacing the item at the specified index with the last item in the list. The list
        /// is shortened by one.
        /// </summary>
        /// <param name="index">The index of the item to delete.</param>
        public void RemoveAtSwapBack(int index)
        {
            this.ListData().RemoveAtSwapBack<T>(index);
        }

        /// <summary>
        /// Returns parallel reader instance.
        /// </summary>
        public ParallelReader AsParallelReader()
        {
            return new ParallelReader(Ptr, Length);
        }

        /// <summary>
        /// Implements parallel reader. Use AsParallelReader to obtain it from container.
        /// </summary>
        public unsafe struct ParallelReader
        {
            [NativeDisableUnsafePtrRestriction]
            public readonly T* Ptr;
            public readonly int Length;

            internal ParallelReader(T* ptr, int length)
            {
                Ptr = ptr;
                Length = length;
            }

            /// <summary>
            /// </summary>
            /// <param name="value"></param>
            /// <returns></returns>
            public int IndexOf(T value)
            {
                for (int i = 0; i < Length; ++i)
                {
                    if (EqualityComparer<T>.Default.Equals(Ptr[i], value))
                    {
                        return i;
                    }
                }

                return -1;
            }

            /// <summary>
            /// </summary>
            /// <param name="value"></param>
            /// <returns></returns>
            public bool Contains(T value)
            {
                return IndexOf(value) != -1;
            }
        }

        public ParallelWriter AsParallelWriter()
        {
            return new ParallelWriter((UnsafeList*)UnsafeUtility.AddressOf(ref this));
        }

        /// <summary>
        /// Returns parallel writer instance.
        /// </summary>
        public unsafe struct ParallelWriter
        {
            public UnsafeList.ParallelWriter Writer;

            internal unsafe ParallelWriter(UnsafeList* listData)
            {
                Writer = listData->AsParallelWriter();
            }

            /// <summary>
            /// Adds an element to the list.
            /// </summary>
            /// <param name="value">The value to be added at the end of the list.</param>
            /// <remarks>
            /// If the list has reached its current capacity, internal array won't be resized, and exception will be thrown.
            /// </remarks>
            public void AddNoResize(T value)
            {
                Writer.AddNoResize(value);
            }

            /// <summary>
            /// </summary>
            /// <param name="ptr"></param>
            /// <param name="length"></param>
            public void AddRangeNoResize(void* ptr, int length)
            {
                Writer.AddRangeNoResize<T>(ptr, length);
            }

            /// <summary>
            /// </summary>
            /// <param name="list"></param>
            public void AddRangeNoResize(UnsafeList<T> list)
            {
                Writer.AddRangeNoResize<T>(list.ListData());
            }
        }
    }

    internal static class UnsafeListExtensions
    {
        public static ref UnsafeList ListData<T>(ref this UnsafeList<T> from) where T : unmanaged, IEquatable<T> => ref UnsafeUtilityEx.As<UnsafeList<T>, UnsafeList>(ref from);
    }

    internal sealed class UnsafeListTDebugView<T>
        where T : unmanaged, IEquatable<T>
    {
        UnsafeList<T> Data;

        public UnsafeListTDebugView(UnsafeList<T> data)
        {
            Data = data;
        }

        public unsafe T[] Items
        {
            get
            {
                T[] result = new T[Data.Length];

                for (var i = 0; i < result.Length; ++i)
                {
                    result[i] = Data.Ptr[i];
                }

                return result;
            }
        }
    }

    /// <summary>
    /// An unmanaged, resizable list, without any thread safety check features.
    /// </summary>
    [DebuggerDisplay("Length = {Length}, Capacity = {Capacity}, IsCreated = {IsCreated}")]
    [DebuggerTypeProxy(typeof(UnsafePtrListDebugView))]
    public unsafe struct UnsafePtrList
    {
        [NativeDisableUnsafePtrRestriction]
        public readonly void** Ptr;
        public readonly int Length;
        public readonly int Capacity;
        public readonly Allocator Allocator;

        /// <summary>
        /// Constructs list as view into memory.
        /// </summary>
        public unsafe UnsafePtrList(void** ptr, int length)
        {
            Ptr = ptr;
            Length = length;
            Capacity = length;
            Allocator = Allocator.Invalid;
        }

        /// <summary>
        /// Constructs a new list using the specified type of memory allocation.
        /// </summary>
        /// <param name="allocator">A member of the
        /// [Unity.Collections.Allocator](https://docs.unity3d.com/ScriptReference/Unity.Collections.Allocator.html) enumeration.</param>
        /// <param name="options">Memory should be cleared on allocation or left uninitialized.</param>
        /// <remarks>The list initially has a capacity of one. To avoid reallocating memory for the list, specify
        /// sufficient capacity up front.</remarks>
        public unsafe UnsafePtrList(int initialCapacity, Allocator allocator, NativeArrayOptions options = NativeArrayOptions.UninitializedMemory)
        {
            Ptr = null;
            Length = 0;
            Capacity = 0;
            Allocator = Allocator.Invalid;

            var sizeOf = IntPtr.Size;
            this.ListData() = new UnsafeList(sizeOf, sizeOf, initialCapacity, allocator, options);
        }

        /// <summary>
        /// </summary>
        public static UnsafePtrList* Create(void** ptr, int length)
        {
            UnsafePtrList* listData = (UnsafePtrList*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<UnsafePtrList>(), UnsafeUtility.AlignOf<UnsafePtrList>(), Allocator.Persistent);
            *listData = new UnsafePtrList(ptr, length);
            return listData;
        }

        /// <summary>
        /// Creates a new list with the specified initial capacity and type of memory allocation.
        /// </summary>
        /// <param name="initialCapacity">The initial capacity of the list. If the list grows larger than its capacity,
        /// the internal array is copied to a new, larger array.</param>
        /// <param name="allocator">A member of the
        /// [Unity.Collections.Allocator](https://docs.unity3d.com/ScriptReference/Unity.Collections.Allocator.html) enumeration.</param>
        /// <param name="options">Memory should be cleared on allocation or left uninitialized.</param>
        public static UnsafePtrList* Create(int initialCapacity, Allocator allocator, NativeArrayOptions options = NativeArrayOptions.UninitializedMemory)
        {
            UnsafePtrList* listData = (UnsafePtrList*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<UnsafePtrList>(), UnsafeUtility.AlignOf<UnsafePtrList>(), allocator);
            *listData = new UnsafePtrList(initialCapacity, allocator, options);
            return listData;
        }

        /// <summary>
        /// Destroys list.
        /// </summary>
        public static void Destroy(UnsafePtrList* listData)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (listData == null)
            {
                throw new Exception("UnsafeList has yet to be created or has been destroyed!");
            }
#endif
            var allocator = listData->ListData().Allocator == Allocator.Invalid
                ? Allocator.Persistent
                : listData->ListData().Allocator
                ;
            listData->Dispose();
            UnsafeUtility.Free(listData, allocator);
        }

        /// <summary>
        /// Reports whether memory for the list is allocated.
        /// </summary>
        /// <value>True if this list object's internal storage  has been allocated.</value>
        /// <remarks>Note that the list storage is not created if you use the default constructor. You must specify
        /// at least an allocation type to construct a usable UnsafeList.</remarks>
        public bool IsCreated => Ptr != null;

        /// <summary>
        /// Disposes of this container and deallocates its memory immediately.
        /// </summary>
        public void Dispose()
        {
            this.ListData().Dispose();
        }

        /// <summary>
        /// Safely disposes of this container and deallocates its memory when the jobs that use it have completed.
        /// </summary>
        /// <remarks>You can call this function dispose of the container immediately after scheduling the job. Pass
        /// the [JobHandle](https://docs.unity3d.com/ScriptReference/Unity.Jobs.JobHandle.html) returned by
        /// the [Job.Schedule](https://docs.unity3d.com/ScriptReference/Unity.Jobs.IJobExtensions.Schedule.html)
        /// method using the `jobHandle` parameter so the job scheduler can dispose the container after all jobs
        /// using it have run.</remarks>
        /// <param name="jobHandle">The job handle or handles for any scheduled jobs that use this container.</param>
        /// <returns>A new job handle containing the prior handles as well as the handle for the job that deletes
        /// the container.</returns>
        public JobHandle Dispose(JobHandle inputDeps)
        {
            return this.ListData().Dispose(inputDeps);
        }

        /// <summary>
        /// Clears the list.
        /// </summary>
        /// <remarks>List Capacity remains unchanged.</remarks>
        public void Clear()
        {
            this.ListData().Clear();
        }

        /// <summary>
        /// Changes the list length, resizing if necessary.
        /// </summary>
        /// <param name="length">The new length of the list.</param>
        /// <param name="options">Memory should be cleared on allocation or left uninitialized.</param>
        public void Resize(int length, NativeArrayOptions options = NativeArrayOptions.UninitializedMemory)
        {
            this.ListData().Resize<IntPtr>(length, options);
        }

        /// <summary>
        /// Set the number of items that can fit in the list.
        /// </summary>
        /// <param name="capacity">The number of items that the list can hold before it resizes its internal storage.</param>
        public void SetCapacity(int capacity)
        {
            this.ListData().SetCapacity<IntPtr>(capacity);
        }

        /// <summary>
        /// Sets the capacity to the actual number of elements in the container.
        /// </summary>
        public void TrimExcess()
        {
            this.ListData().TrimExcess<IntPtr>();
        }

        /// <summary>
        /// Searches for the specified element in list.
        /// </summary>
        /// <param name="value"></param>
        /// <returns>The zero-based index of the first occurrence element if found, otherwise returns -1.</returns>
        public int IndexOf(void* value)
        {
            for (int i = 0; i < Length; ++i)
            {
                if (Ptr[i] == value) return i;
            }

            return -1;
        }

        /// <summary>
        /// Determines whether an element is in the list.
        /// </summary>
        /// <param name="value"></param>
        /// <returns>True, if element is found.</returns>
        public bool Contains(void* value)
        {
            return IndexOf(value) != -1;
        }

        /// <summary>
        /// Adds an element to the list.
        /// </summary>
        /// <param name="value">The value to be added at the end of the list.</param>
        /// <remarks>
        /// If the list has reached its current capacity, internal array won't be resized, and exception will be thrown.
        /// </remarks>
        public void AddNoResize(void* value)
        {
            this.ListData().AddNoResize((IntPtr)value);
        }

        /// <summary>
        /// Adds elements from a buffer to this list.
        /// </summary>
        /// <param name="ptr">A pointer to the buffer.</param>
        /// <param name="length">The number of elements to add to the list.</param>
        /// <remarks>
        /// If the list has reached its current capacity, internal array won't be resized, and exception will be thrown.
        /// </remarks>
        public void AddRangeNoResize(void** ptr, int length)
        {
            this.ListData().AddRangeNoResize<IntPtr>(ptr, length);
        }

        /// <summary>
        /// Adds elements from a list to this list.
        /// </summary>
        /// <remarks>
        /// If the list has reached its current capacity, internal array won't be resized, and exception will be thrown.
        /// </remarks>
        public void AddRangeNoResize(UnsafePtrList list)
        {
            this.ListData().AddRangeNoResize<IntPtr>(list.Ptr, list.Length);
        }

        /// <summary>
        /// Adds an element to the list.
        /// </summary>
        /// <param name="value">The struct to be added at the end of the list.</param>
        public void Add(void* value)
        {
            this.ListData().Add((IntPtr)value);
        }

        /// <summary>
        /// Adds the elements of a UnsafePtrList to this list.
        /// </summary>
        /// <param name="list">The items to add.</param>
        public void AddRange(UnsafePtrList list)
        {
            this.ListData().AddRange<IntPtr>(list.ListData());
        }

        /// <summary>
        /// Truncates the list by replacing the item at the specified index with the last item in the list. The list
        /// is shortened by one.
        /// </summary>
        /// <param name="index">The index of the item to delete.</param>
        public void RemoveAtSwapBack(int index)
        {
            RemoveRangeSwapBack(index, index + 1);
        }

        /// <summary>
        /// Truncates the list by replacing the item at the specified index range with the items from the end the list. The list
        /// is shortened by number of elements in range.
        /// </summary>
        /// <param name="begin">The first index of the item to delete.</param>
        /// <param name="end">The last index of the item to delete.</param>
        public void RemoveRangeSwapBack(int begin, int end)
        {
            this.ListData().RemoveRangeSwapBack<IntPtr>(begin, end);
        }

        /// <summary>
        /// Returns parallel reader instance.
        /// </summary>
        public ParallelReader AsParallelReader()
        {
            return new ParallelReader(Ptr, Length);
        }

        /// <summary>
        /// Implements parallel reader. Use AsParallelReader to obtain it from container.
        /// </summary>
        public unsafe struct ParallelReader
        {
            [NativeDisableUnsafePtrRestriction]
            public readonly void** Ptr;
            public readonly int Length;

            public ParallelReader(void** ptr, int length)
            {
                Ptr = ptr;
                Length = length;
            }

            public int IndexOf(void* value)
            {
                for (int i = 0; i < Length; ++i)
                {
                    if (Ptr[i] == value) return i;
                }

                return -1;
            }

            public bool Contains(void* value)
            {
                return IndexOf(value) != -1;
            }
        }

        /// <summary>
        /// Returns parallel writer instance.
        /// </summary>
        public ParallelWriter AsParallelWriter()
        {
            return new ParallelWriter(Ptr, (UnsafeList*)UnsafeUtility.AddressOf(ref this));
        }

        public unsafe struct ParallelWriter
        {
            [NativeDisableUnsafePtrRestriction]
            public readonly void* Ptr;

            [NativeDisableUnsafePtrRestriction]
            public UnsafeList* ListData;

            public unsafe ParallelWriter(void* ptr, UnsafeList* listData)
            {
                Ptr = ptr;
                ListData = listData;
            }

            /// <summary>
            /// Adds an element to the list.
            /// </summary>
            /// <param name="value">The value to be added at the end of the list.</param>
            /// <remarks>
            /// If the list has reached its current capacity, internal array won't be resized, and exception will be thrown.
            /// </remarks>
            public void AddNoResize(void* value)
            {
                ListData->AddNoResize((IntPtr)value);
            }

            /// <summary>
            /// Adds elements from a buffer to this list.
            /// </summary>
            /// <param name="ptr">A pointer to the buffer.</param>
            /// <param name="length">The number of elements to add to the list.</param>
            /// <remarks>
            /// If the list has reached its current capacity, internal array won't be resized, and exception will be thrown.
            /// </remarks>
            public void AddRangeNoResize(void** ptr, int length)
            {
                ListData->AddRangeNoResize<IntPtr>(ptr, length);
            }

            /// <summary>
            /// Adds elements from a list to this list.
            /// </summary>
            /// <remarks>
            /// If the list has reached its current capacity, internal array won't be resized, and exception will be thrown.
            /// </remarks>
            public void AddRangeNoResize(UnsafePtrList list)
            {
                ListData->AddRangeNoResize<IntPtr>(list.Ptr, list.Length);
            }
        }
    }

    internal static class UnsafePtrListExtensions
    {
        public static ref UnsafeList ListData(ref this UnsafePtrList from) => ref UnsafeUtilityEx.As<UnsafePtrList, UnsafeList>(ref from);
    }

    internal sealed class UnsafePtrListDebugView
    {
        private UnsafePtrList Data;

        public UnsafePtrListDebugView(UnsafePtrList data)
        {
            Data = data;
        }

        public unsafe IntPtr[] Items
        {
            get
            {
                IntPtr[] result = new IntPtr[Data.Length];

                for (var i = 0; i < result.Length; ++i)
                {
                    result[i] = (IntPtr)Data.Ptr[i];
                }

                return result;
            }
        }
    }
}
