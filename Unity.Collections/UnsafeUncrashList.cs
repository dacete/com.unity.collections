using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Collections.Unsafe;
using Unity.Jobs;

namespace Unity.Collections.Unsafe
{
    /// <summary>
    /// An unmanaged, resizable list.
    /// </summary>
    /// <remarks>The elements are stored contiguously in a buffer rather than as linked nodes.</remarks>
    /// <typeparam name="T">The type of the elements.</typeparam>
    [StructLayout(LayoutKind.Sequential)]
    [DebuggerDisplay("Length = {m_ListData == null ? default : m_ListData->Length}, Capacity = {m_ListData == null ? default : m_ListData->Capacity}")]
    [DebuggerTypeProxy(typeof(NativeListDebugView<>))]
    [GenerateTestsForBurstCompatibility(GenericTypeArguments = new [] { typeof(int) })]
    public unsafe struct UncrashUnsafeList<T>
        : INativeDisposable
        , INativeList<T>
        , IEnumerable<T> // Used by collection initializers.
        where T : unmanaged
    {
        [NativeDisableUnsafePtrRestriction]
        internal UnsafeList<T>* m_ListData;

        /// <summary>
        /// Initializes and returns a UncrashUnsafeList with a capacity of one.
        /// </summary>
        /// <param name="allocator">The allocator to use.</param>
        public UncrashUnsafeList(AllocatorManager.AllocatorHandle allocator)
            : this(1, allocator)
        {
        }

        /// <summary>
        /// Initializes and returns a UncrashUnsafeList.
        /// </summary>
        /// <param name="initialCapacity">The initial capacity of the list.</param>
        /// <param name="allocator">The allocator to use.</param>
        public UncrashUnsafeList(int initialCapacity, AllocatorManager.AllocatorHandle allocator)
        {
            this = default;
            AllocatorManager.AllocatorHandle temp = allocator;
            Initialize(initialCapacity, ref temp);
        }

        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new [] { typeof(AllocatorManager.AllocatorHandle) })]
        internal void Initialize<U>(int initialCapacity, ref U allocator) where U : unmanaged, AllocatorManager.IAllocator
        {
            var totalSize = sizeof(T) * (long)initialCapacity;
            m_ListData = UnsafeList<T>.Create(initialCapacity, ref allocator, NativeArrayOptions.UninitializedMemory);
        }

        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new [] { typeof(AllocatorManager.AllocatorHandle) })]
        internal static UncrashUnsafeList<T> New<U>(int initialCapacity, ref U allocator) where U : unmanaged, AllocatorManager.IAllocator
        {
            var nativelist = new UncrashUnsafeList<T>();
            nativelist.Initialize(initialCapacity, ref allocator);
            return nativelist;
        }

        /// <summary>
        /// The element at a given index.
        /// </summary>
        /// <param name="index">An index into this list.</param>
        /// <value>The value to store at the `index`.</value>
        /// <exception cref="IndexOutOfRangeException">Thrown if `index` is out of bounds.</exception>
        public T this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return (*m_ListData)[index];
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                (*m_ListData)[index] = value;
            }
        }

        /// <summary>
        /// Returns a reference to the element at an index.
        /// </summary>
        /// <param name="index">An index.</param>
        /// <returns>A reference to the element at the index.</returns>
        /// <exception cref="IndexOutOfRangeException">Thrown if index is out of bounds.</exception>
        public ref T ElementAt(int index)
        {
            return ref m_ListData->ElementAt(index);
        }

        /// <summary>
        /// The count of elements.
        /// </summary>
        /// <value>The current count of elements. Always less than or equal to the capacity.</value>
        /// <remarks>To decrease the memory used by a list, set <see cref="Capacity"/> after reducing the length of the list.</remarks>
        /// <param name="value>">The new length. If the new length is greater than the current capacity, the capacity is increased.
        /// Newly allocated memory is cleared.</param>
        public int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get
            {
                return CollectionHelper.AssumePositive(m_ListData->Length);
            }

            set
            {
                m_ListData->Resize(value, NativeArrayOptions.ClearMemory);
            }
        }

        /// <summary>
        /// The number of elements that fit in the current allocation.
        /// </summary>
        /// <value>The number of elements that fit in the current allocation.</value>
        /// <param name="value">The new capacity. Must be greater or equal to the length.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if the new capacity is smaller than the length.</exception>
        public int Capacity
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get
            {
                return m_ListData->Capacity;
            }

            set
            {
                m_ListData->Capacity = value;
            }
        }

        /// <summary>
        /// Returns the internal unsafe list.
        /// </summary>
        /// <remarks>Internally, the elements of a UncrashUnsafeList are stored in an UnsafeList.</remarks>
        /// <returns>The internal unsafe list.</returns>
        public UnsafeList<T>* GetUnsafeList() => m_ListData;

        /// <summary>
        /// Appends an element to the end of this list.
        /// </summary>
        /// <param name="value">The value to add to the end of this list.</param>
        /// <remarks>
        /// Length is incremented by 1. Will not increase the capacity.
        /// </remarks>
        /// <exception cref="InvalidOperationException">Thrown if incrementing the length would exceed the capacity.</exception>
        public void AddNoResize(T value)
        {
            m_ListData->AddNoResize(value);
        }

        /// <summary>
        /// Appends elements from a buffer to the end of this list.
        /// </summary>
        /// <param name="ptr">The buffer to copy from.</param>
        /// <param name="count">The number of elements to copy from the buffer.</param>
        /// <remarks>
        /// Length is increased by the count. Will not increase the capacity.
        /// </remarks>
        /// <exception cref="InvalidOperationException">Thrown if the increased length would exceed the capacity.</exception>
        public void AddRangeNoResize(void* ptr, int count)
        {
            m_ListData->AddRangeNoResize(ptr, count);
        }

        /// <summary>
        /// Appends the elements of another list to the end of this list.
        /// </summary>
        /// <param name="list">The other list to copy from.</param>
        /// <remarks>
        /// Length is increased by the length of the other list. Will not increase the capacity.
        /// </remarks>
        /// <exception cref="InvalidOperationException">Thrown if the increased length would exceed the capacity.</exception>
        public void AddRangeNoResize(UncrashUnsafeList<T> list)
        {
            m_ListData->AddRangeNoResize(*list.m_ListData);
        }

        /// <summary>
        /// Appends an element to the end of this list.
        /// </summary>
        /// <param name="value">The value to add to the end of this list.</param>
        /// <remarks>
        /// Length is incremented by 1. If necessary, the capacity is increased.
        /// </remarks>
        public void Add(in T value)
        {
            m_ListData->Add(in value);
        }

        /// <summary>
        /// Appends the elements of an array to the end of this list.
        /// </summary>
        /// <param name="array">The array to copy from.</param>
        /// <remarks>
        /// Length is increased by the number of new elements. Does not increase the capacity.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if the increased length would exceed the capacity.</exception>
        public void AddRange(NativeArray<T> array)
        {
            AddRange(array.GetUnsafeReadOnlyPtr(), array.Length);
        }

        /// <summary>
        /// Appends the elements of a buffer to the end of this list.
        /// </summary>
        /// <param name="ptr">The buffer to copy from.</param>
        /// <param name="count">The number of elements to copy from the buffer.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if count is negative.</exception>
        public void AddRange(void* ptr, int count)
        {
            m_ListData->AddRange(ptr, CollectionHelper.AssumePositive(count));
        }

        /// <summary>
        /// Appends value count times to the end of this list.
        /// </summary>
        /// <param name="value">The value to add to the end of this list.</param>
        /// <param name="count">The number of times to replicate the value.</param>
        /// <remarks>
        /// Length is incremented by count. If necessary, the capacity is increased.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if count is negative.</exception>
        public void AddReplicate(in T value, int count)
        {
            m_ListData->AddReplicate(in value, CollectionHelper.AssumePositive(count));
        }

        /// <summary>
        /// Shifts elements toward the end of this list, increasing its length.
        /// </summary>
        /// <remarks>
        /// Right-shifts elements in the list so as to create 'free' slots at the beginning or in the middle.
        ///
        /// The length is increased by `end - begin`. If necessary, the capacity will be increased accordingly.
        ///
        /// If `end` equals `begin`, the method does nothing.
        ///
        /// The element at index `begin` will be copied to index `end`, the element at index `begin + 1` will be copied to `end + 1`, and so forth.
        ///
        /// The indexes `begin` up to `end` are not cleared: they will contain whatever values they held prior.
        /// </remarks>
        /// <param name="begin">The index of the first element that will be shifted up.</param>
        /// <param name="end">The index where the first shifted element will end up.</param>
        /// <exception cref="ArgumentException">Thrown if `end &lt; begin`.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if `begin` or `end` are out of bounds.</exception>
        public void InsertRangeWithBeginEnd(int begin, int end)
        {
            m_ListData->InsertRangeWithBeginEnd(begin, end);
        }

        /// <summary>
        /// Shifts elements toward the end of this list, increasing its length.
        /// </summary>
        /// <remarks>
        /// Right-shifts elements in the list so as to create 'free' slots at the beginning or in the middle.
        ///
        /// The length is increased by `count`. If necessary, the capacity will be increased accordingly.
        ///
        /// If `count` equals `0`, the method does nothing.
        ///
        /// The element at index `index` will be copied to index `index + count`, the element at index `index + 1` will be copied to `index + count + 1`, and so forth.
        ///
        /// The indexes `index` up to `index + count` are not cleared: they will contain whatever values they held prior.
        /// </remarks>
        /// <param name="index">The index of the first element that will be shifted up.</param>
        /// <param name="count">The number of elements to insert.</param>
        /// <exception cref="ArgumentException">Thrown if `count` is negative.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if `index` is out of bounds.</exception>
        public void InsertRange(int index, int count) => InsertRangeWithBeginEnd(index, index + count);

        /// <summary>
        /// Copies the last element of this list to the specified index. Decrements the length by 1.
        /// </summary>
        /// <remarks>Useful as a cheap way to remove an element from this list when you don't care about preserving order.</remarks>
        /// <param name="index">The index to overwrite with the last element.</param>
        /// <exception cref="IndexOutOfRangeException">Thrown if `index` is out of bounds.</exception>
        public void RemoveAtSwapBack(int index)
        {
            m_ListData->RemoveAtSwapBack(index);
        }

        /// <summary>
        /// Copies the last *N* elements of this list to a range in this list. Decrements the length by *N*.
        /// </summary>
        /// <remarks>
        /// Copies the last `count` elements to the indexes `index` up to `index + count`.
        ///
        /// Useful as a cheap way to remove elements from a list when you don't care about preserving order.
        /// </remarks>
        /// <param name="index">The index of the first element to overwrite.</param>
        /// <param name="count">The number of elements to copy and remove.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if `index` is out of bounds, `count` is negative,
        /// or `index + count` exceeds the length.</exception>
        public void RemoveRangeSwapBack(int index, int count)
        {
            m_ListData->RemoveRangeSwapBack(index, count);
        }

        /// <summary>
        /// Removes the element at an index, shifting everything above it down by one. Decrements the length by 1.
        /// </summary>
        /// <param name="index">The index of the item to remove.</param>
        /// <remarks>
        /// If you don't care about preserving the order of the elements, <see cref="RemoveAtSwapBack(int)"/> is a more efficient way to remove elements.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if `index` is out of bounds.</exception>
        public void RemoveAt(int index)
        {
            m_ListData->RemoveAt(index);
        }

        /// <summary>
        /// Removes *N* elements in a range, shifting everything above the range down by *N*. Decrements the length by *N*.
        /// </summary>
        /// <param name="index">The index of the first element to remove.</param>
        /// <param name="count">The number of elements to remove.</param>
        /// <remarks>
        /// If you don't care about preserving the order of the elements, `RemoveRangeSwapBackWithBeginEnd`
        /// is a more efficient way to remove elements.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if `index` is out of bounds, `count` is negative,
        /// or `index + count` exceeds the length.</exception>
        public void RemoveRange(int index, int count)
        {
            m_ListData->RemoveRange(index, count);
        }

        /// <summary>
        /// Whether this list is empty.
        /// </summary>
        /// <value>True if the list is empty or if the list has not been constructed.</value>
        public readonly bool IsEmpty
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => m_ListData == null || m_ListData->Length == 0;
        }

        /// <summary>
        /// Whether this list has been allocated (and not yet deallocated).
        /// </summary>
        /// <value>True if this list has been allocated (and not yet deallocated).</value>
        public readonly bool IsCreated
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => m_ListData != null;
        }

        /// <summary>
        /// Releases all resources (memory and safety handles).
        /// </summary>
        public void Dispose()
        {
            if (!IsCreated)
            {
                return;
            }

            UnsafeList<T>.Destroy(m_ListData);
            m_ListData = null;
        }

        /// <summary>
        /// Releases all resources (memory and safety handles).
        /// <typeparam name="U">The type of allocator.</typeparam>
        /// <param name="allocator">The allocator that was used to allocate this list.</param>
        /// </summary>
        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] { typeof(AllocatorManager.AllocatorHandle) })]
        internal void Dispose<U>(ref U allocator) where U : unmanaged, AllocatorManager.IAllocator
        {
            if (!IsCreated)
            {
                return;
            }

            UnsafeList<T>.Destroy(m_ListData, ref allocator);
            m_ListData = null;
        }

        /// <summary>
        /// Creates and schedules a job that releases all resources (memory and safety handles) of this list.
        /// </summary>
        /// <param name="inputDeps">The dependency for the new job.</param>
        /// <returns>The handle of the new job. The job depends upon `inputDeps` and releases all resources (memory and safety handles) of this list.</returns>
        public JobHandle Dispose(JobHandle inputDeps)
        {
            if (!IsCreated)
            {
                return inputDeps;
            }

            var jobHandle = new NativeListDisposeJob { Data = new NativeListDispose { m_ListData = (UntypedUnsafeList*)m_ListData } }.Schedule(inputDeps);
            m_ListData = null;

            return jobHandle;
        }

        /// <summary>
        /// Sets the length to 0.
        /// </summary>
        /// <remarks>Does not change the capacity.</remarks>
        public void Clear()
        {
            m_ListData->Clear();
        }

        /// <summary>
        /// Returns a native array that aliases the content of this list.
        /// </summary>
        /// <returns>A native array that aliases the content of this list.</returns>
        public NativeArray<T> AsArray()
        {
            var array = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(m_ListData->Ptr, m_ListData->Length, Allocator.None);

            return array;
        }

        /// <summary>
        /// Returns an array that aliases this list. The length of the array is updated when the length of
        /// this array is updated in a prior job.
        /// </summary>
        /// <remarks>
        /// Useful when a job populates a list that is then used by another job.
        ///
        /// If you pass both jobs the same list, you have to complete the first job before you schedule the second:
        /// otherwise, the second job doesn't see the first job's changes to the list's length.
        ///
        /// If instead you pass the second job a deferred array that aliases the list, the array's length is kept in sync with
        /// the first job's changes to the list's length. Consequently, the first job doesn't have to
        /// be completed before you can schedule the second: the second job simply has to depend upon the first.
        /// </remarks>
        /// <returns>An array that aliases this list and whose length can be specially modified across jobs.</returns>
        /// <example>
        /// The following example populates a list with integers in one job and passes that data to a second job as
        /// a deferred array. If we tried to pass the list directly to the second job, that job would not see any
        /// modifications made to the list by the first job. To avoid this, we instead pass the second job a deferred array that aliases the list.
        /// <code>
        /// using UnityEngine;
        /// using Unity.Jobs;
        /// using Unity.Collections;
        ///
        /// public class DeferredArraySum : MonoBehaviour
        ///{
        ///    public struct Populate : IJob
        ///    {
        ///        public UncrashUnsafeList&lt;int&gt; list;
        ///
        ///        public void Execute()
        ///        {
        ///            for (int i = list.Length; i &lt; list.Capacity; i++)
        ///            {
        ///                list.Add(i);
        ///            }
        ///        }
        ///    }
        ///
        ///    // Sums all numbers from deferred.
        ///    public struct Sum : IJob
        ///    {
        ///        [ReadOnly] public NativeArray&lt;int&gt; deferred;
        ///        public NativeArray&lt;int&gt; sum;
        ///
        ///        public void Execute()
        ///        {
        ///            sum[0] = 0;
        ///            for (int i = 0; i &lt; deferred.Length; i++)
        ///            {
        ///                sum[0] += deferred[i];
        ///            }
        ///        }
        ///    }
        ///
        ///    void Start()
        ///    {
        ///        var list = new UncrashUnsafeList&lt;int&gt;(100, Allocator.TempJob);
        ///        var deferred = list.AsDeferredJobArray(),
        ///        var output = new NativeArray&lt;int&gt;(1, Allocator.TempJob);
        ///
        ///        // The Populate job increases the list's length from 0 to 100.
        ///        var populate = new Populate { list = list }.Schedule();
        ///
        ///        // At time of scheduling, the length of the deferred array given to Sum is 0.
        ///        // When Populate increases the list's length, the deferred array's length field in the
        ///        // Sum job is also modified, even though it has already been scheduled.
        ///        var sum = new Sum { deferred = deferred, sum = output }.Schedule(populate);
        ///
        ///        sum.Complete();
        ///
        ///        Debug.Log("Result: " + output[0]);
        ///
        ///        list.Dispose();
        ///        output.Dispose();
        ///    }
        /// }
        /// </code>
        /// </example>
        public NativeArray<T> AsDeferredJobArray()
        {
            byte* buffer = (byte*)m_ListData;
            // We use the first bit of the pointer to infer that the array is in list mode
            // Thus the job scheduling code will need to patch it.
            buffer += 1;
            var array = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(buffer, 0, Allocator.Invalid);

            return array;
        }

        /// <summary>
        /// Returns an array containing a copy of this list's content.
        /// </summary>
        /// <param name="allocator">The allocator to use.</param>
        /// <returns>An array containing a copy of this list's content.</returns>
        public NativeArray<T> ToArray(AllocatorManager.AllocatorHandle allocator)
        {
            NativeArray<T> result = CollectionHelper.CreateNativeArray<T>(Length, allocator, NativeArrayOptions.UninitializedMemory);
            UnsafeUtility.MemCpy((byte*)result.m_Buffer, (byte*)m_ListData->Ptr, Length * UnsafeUtility.SizeOf<T>());
            return result;
        }

        /// <summary>
        /// Copies all elements of specified container to this container.
        /// </summary>
        /// <param name="other">An container to copy into this container.</param>
        public void CopyFrom(in NativeArray<T> other)
        {
            m_ListData->CopyFrom(other);
        }

        /// <summary>
        /// Copies all elements of specified container to this container.
        /// </summary>
        /// <param name="other">An container to copy into this container.</param>
        public void CopyFrom(in UnsafeList<T> other)
        {
            m_ListData->CopyFrom(other);
        }

        /// <summary>
        /// Copies all elements of specified container to this container.
        /// </summary>
        /// <param name="other">An container to copy into this container.</param>
        public void CopyFrom(in UncrashUnsafeList<T> other)
        {
            CopyFrom(*other.m_ListData);
        }

        /// <summary>
        /// Returns an enumerator over the elements of this list.
        /// </summary>
        /// <returns>An enumerator over the elements of this list.</returns>
        public NativeArray<T>.Enumerator GetEnumerator()
        {
            var array = AsArray();
            return new NativeArray<T>.Enumerator(ref array);
        }

        /// <summary>
        /// This method is not implemented. Use <see cref="GetEnumerator"/> instead.
        /// </summary>
        /// <returns>Throws NotImplementedException.</returns>
        /// <exception cref="NotImplementedException">Method is not implemented.</exception>
        IEnumerator IEnumerable.GetEnumerator()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// This method is not implemented. Use <see cref="GetEnumerator"/> instead.
        /// </summary>
        /// <returns>Throws NotImplementedException.</returns>
        /// <exception cref="NotImplementedException">Method is not implemented.</exception>
        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Sets the length of this list, increasing the capacity if necessary.
        /// </summary>
        /// <param name="length">The new length of this list.</param>
        /// <param name="options">Whether to clear any newly allocated bytes to all zeroes.</param>
        public void Resize(int length, NativeArrayOptions options)
        {
            m_ListData->Resize(length, options);
        }

        /// <summary>
        /// Sets the length of this list, increasing the capacity if necessary.
        /// </summary>
        /// <remarks>Does not clear newly allocated bytes.</remarks>
        /// <param name="length">The new length of this list.</param>
        public void ResizeUninitialized(int length)
        {
            Resize(length, NativeArrayOptions.UninitializedMemory);
        }

        /// <summary>
        /// Sets the capacity.
        /// </summary>
        /// <param name="capacity">The new capacity.</param>
        public void SetCapacity(int capacity)
        {
            m_ListData->SetCapacity(capacity);
        }

        /// <summary>
        /// Sets the capacity to match the length.
        /// </summary>
        public void TrimExcess()
        {
            m_ListData->TrimExcess();
        }

        /// <summary>
        /// Returns a parallel writer of this list.
        /// </summary>
        /// <returns>A parallel writer of this list.</returns>
        public ParallelWriter AsParallelWriter()
        {
            return new ParallelWriter(m_ListData);
        }

        /// <summary>
        /// A parallel writer for a UncrashUnsafeList.
        /// </summary>
        /// <remarks>
        /// Use <see cref="AsParallelWriter"/> to create a parallel writer for a list.
        /// </remarks>
        [NativeContainer]
        [NativeContainerIsAtomicWriteOnly]
        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new [] { typeof(int) })]
        public unsafe struct ParallelWriter
        {
            /// <summary>
            /// The data of the list.
            /// </summary>
            public readonly void* Ptr
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => ListData->Ptr;
            }

            /// <summary>
            /// The internal unsafe list.
            /// </summary>
            /// <value>The internal unsafe list.</value>
            [NativeDisableUnsafePtrRestriction]
            public UnsafeList<T>* ListData;

            internal unsafe ParallelWriter(UnsafeList<T>* listData)
            {
                ListData = listData;
            }

            /// <summary>
            /// Appends an element to the end of this list.
            /// </summary>
            /// <param name="value">The value to add to the end of this list.</param>
            /// <remarks>
            /// Increments the length by 1 unless doing so would exceed the current capacity.
            /// </remarks>
            /// <exception cref="InvalidOperationException">Thrown if adding an element would exceed the capacity.</exception>
            public void AddNoResize(T value)
            {
                var idx = Interlocked.Increment(ref ListData->m_length) - 1;

                UnsafeUtility.WriteArrayElement(ListData->Ptr, idx, value);
            }

            /// <summary>
            /// Appends elements from a buffer to the end of this list.
            /// </summary>
            /// <param name="ptr">The buffer to copy from.</param>
            /// <param name="count">The number of elements to copy from the buffer.</param>
            /// <remarks>
            /// Increments the length by `count` unless doing so would exceed the current capacity.
            /// </remarks>
            /// <exception cref="InvalidOperationException">Thrown if adding the elements would exceed the capacity.</exception>
            public void AddRangeNoResize(void* ptr, int count)
            {

                var idx = Interlocked.Add(ref ListData->m_length, count) - count;

                var sizeOf = sizeof(T);
                void* dst = (byte*)ListData->Ptr + idx * sizeOf;
                UnsafeUtility.MemCpy(dst, ptr, count * sizeOf);
            }

            /// <summary>
            /// Appends the elements of another list to the end of this list.
            /// </summary>
            /// <param name="list">The other list to copy from.</param>
            /// <remarks>
            /// Increments the length of this list by the length of the other list unless doing so would exceed the current capacity.
            /// </remarks>
            /// <exception cref="InvalidOperationException">Thrown if adding the elements would exceed the capacity.</exception>
            public void AddRangeNoResize(UnsafeList<T> list)
            {
                AddRangeNoResize(list.Ptr, list.Length);
            }

            /// <summary>
            /// Appends the elements of another list to the end of this list.
            /// </summary>
            /// <param name="list">The other list to copy from.</param>
            /// <remarks>
            /// Increments the length of this list by the length of the other list unless doing so would exceed the current capacity.
            /// </remarks>
            /// <exception cref="InvalidOperationException">Thrown if adding the elements would exceed the capacity.</exception>
            public void AddRangeNoResize(UncrashUnsafeList<T> list)
            {
                AddRangeNoResize(*list.m_ListData);
            }
        }
    }
}

namespace Unity.Collections.LowLevel.Unsafe
{
    /// <summary>
    /// Provides unsafe utility methods for UncrashUnsafeList.
    /// </summary>
    [GenerateTestsForBurstCompatibility]
    public unsafe static class UncrashUnsafeListUnsafeUtility
    {
        /// <summary>
        /// Returns a pointer to this list's internal buffer.
        /// </summary>
        /// <remarks>Performs a job safety check for read-write access.</remarks>
        /// <param name="list">The list.</param>
        /// <typeparam name="T">The type of the elements.</typeparam>
        /// <returns>A pointer to this list's internal buffer.</returns>
        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new [] { typeof(int) })]
        public static T* GetUnsafePtr<T>(this UncrashUnsafeList<T> list) where T : unmanaged
        {
            return list.m_ListData->Ptr;
        }

        /// <summary>
        /// Returns a pointer to this list's internal buffer.
        /// </summary>
        /// <remarks>Performs a job safety check for read-only access.</remarks>
        /// <param name="list">The list.</param>
        /// <typeparam name="T">The type of the elements.</typeparam>
        /// <returns>A pointer to this list's internal buffer.</returns>
        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new [] { typeof(int) })]
        public static unsafe T* GetUnsafeReadOnlyPtr<T>(this UncrashUnsafeList<T> list) where T : unmanaged
        {
            return list.m_ListData->Ptr;
        }

        /// <summary>
        /// Returns a pointer to this list's internal unsafe list.
        /// </summary>
        /// <remarks>Performs no job safety checks.</remarks>
        /// <param name="list">The list.</param>
        /// <typeparam name="T">The type of the elements.</typeparam>
        /// <returns>A pointer to this list's internal unsafe list.</returns>
        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new [] { typeof(int) })]
        public static void* GetInternalListDataPtrUnchecked<T>(ref UncrashUnsafeList<T> list) where T : unmanaged
        {
            return list.m_ListData;
        }
    }
}
