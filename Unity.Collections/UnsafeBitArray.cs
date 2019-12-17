﻿using System;
using System.Diagnostics;
using Unity.Burst;
using Unity.Jobs;
using Unity.Mathematics;

namespace Unity.Collections.LowLevel.Unsafe
{
    /// <summary>
    /// Arbitrary sized array of bits.
    /// </summary>
    [DebuggerDisplay("Length = {Length}, IsCreated = {IsCreated}")]
    [DebuggerTypeProxy(typeof(UnsafeBitArrayDebugView))]
    public unsafe struct UnsafeBitArray
    {
        [NativeDisableUnsafePtrRestriction]
        public ulong* Ptr;
        public int Length;
        public Allocator Allocator;

        /// <summary>
        /// Constructs container as view into memory.
        /// </summary>
        /// <param name="ptr">Pointer to data.</param>
        /// <param name="sizeInBytes">Size of data in bytes. Must be multiple of 8-bytes.</param>
        public unsafe UnsafeBitArray(void* ptr, int sizeInBytes)
        {
            if ((sizeInBytes & 7) != 0)
            {
                throw new ArgumentException($"BitArray invalid arguments: sizeInBytes {sizeInBytes} (must be multiple of 8-bytes, sizeInBytes: {sizeInBytes}).");
            }

            Ptr = (ulong*)ptr;
            Length = sizeInBytes * 8;
            Allocator = Allocator.Invalid;
        }

        /// <summary>
        /// Constructs a new container with the specified initial capacity and type of memory allocation.
        /// </summary>
        /// <param name="numBits">Number of bits.</param>
        /// <param name="allocator">A member of the
        /// [Unity.Collections.Allocator](https://docs.unity3d.com/ScriptReference/Unity.Collections.Allocator.html) enumeration.</param>
        /// <param name="options">Memory should be cleared on allocation or left uninitialized.</param>
        public UnsafeBitArray(int numBits, Allocator allocator, NativeArrayOptions options = NativeArrayOptions.UninitializedMemory)
        {
            Allocator = allocator;
            var sizeInBytes = Bitwise.AlignUp(numBits, 64) / 8;
            Ptr = (ulong*)UnsafeUtility.Malloc(sizeInBytes, 16, allocator);
            Length = sizeInBytes * 8;

            if (options == NativeArrayOptions.ClearMemory)
            {
                UnsafeUtility.MemClear(Ptr, sizeInBytes);
            }
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
        /// Clear all bits to 0.
        /// </summary>
        public void Clear()
        {
            var sizeInBytes = Length / 8;
            UnsafeUtility.MemClear(Ptr, sizeInBytes);
        }

        /// <summary>
        /// Set single bit to desired boolean value.
        /// </summary>
        /// <param name="pos">Position in bit array.</param>
        /// <param name="value">Value of bits to set.</param>
        public void Set(int pos, bool value)
        {
            CheckArgs(pos);

            var idx = pos >> 6;
            var shift = pos & 0x3f;
            var mask = 1ul << shift;
            var bits = (Ptr[idx] & ~mask) | ((ulong)-Bitwise.FromBool(value) & mask);
            Ptr[idx] = bits;
        }

        /// <summary>
        /// Set bits to desired boolean value.
        /// </summary>
        /// <param name="pos">Position in bit array.</param>
        /// <param name="value">Value of bits to set.</param>
        /// <param name="numBits">Number of bits to set.</param>
        public void SetBits(int pos, bool value, int numBits)
        {
            CheckArgs(pos);

            var end = math.min(pos + numBits, Length);
            var idxB = pos >> 6;
            var shiftB = pos & 0x3f;
            var idxE = end >> 6;
            var shiftE = end & 0x3f;
            var maskB = 0xfffffffffffffffful << shiftB;
            var maskE = 0xfffffffffffffffful >> (63 - shiftE);
            var orBits = (ulong)-Bitwise.FromBool(value);
            var orBitsB = maskB & orBits;
            var orBitsE = maskE & orBits;
            var cmaskB = ~maskB;
            var cmaskE = ~maskE;

            if (idxB == idxE)
            {
                var maskBE = maskB & maskE;
                var cmaskBE = ~maskBE;
                var orBitsBE = orBitsB | orBitsE;
                Ptr[idxB] = (Ptr[idxB] & cmaskBE) | orBitsBE;
                return;
            }

            Ptr[idxB] = (Ptr[idxB] & cmaskB) | orBitsB;

            for (var idx = idxB+1; idx < idxE; ++idx)
            {
                Ptr[idx] = orBits;
            }

            Ptr[idxE] = (Ptr[idxE] & cmaskE) | orBitsE;
        }

        /// <summary>
        /// Returns true is bit at position is set.
        /// </summary>
        /// <param name="pos">Position in bit array.</param>
        /// <returns>Returns true if bit is set.</returns>
        public bool IsSet(int pos)
        {
            CheckArgs(pos);

            var idx = pos >> 6;
            var shift = pos & 0x3f;
            var mask = 1ul << shift;
            return 0ul != (Ptr[idx] & mask);
        }

        /// <summary>
        /// Returns true if none of bits in range are set.
        /// </summary>
        /// <param name="pos">Position in bit array.</param>
        /// <param name="numBits">Number of bits to test.</param>
        /// <returns>Returns true if none of bits are set.</returns>
        public bool TestNone(int pos, int numBits = 1)
        {
            CheckArgs(pos);

            var end = math.min(pos + numBits, Length);
            var idxB = pos >> 6;
            var shiftB = pos & 0x3f;
            var idxE = end >> 6;
            var shiftE = end & 0x3f;
            var maskB = 0xfffffffffffffffful << shiftB;
            var maskE = 0xfffffffffffffffful >> (63 - shiftE);

            if (idxB == idxE)
            {
                var mask = maskB & maskE;
                return 0ul == (Ptr[idxB] & mask);
            }

            if (0ul != (Ptr[idxB] & maskB))
            {
                return false;
            }

            for (var idx = idxB + 1; idx < idxE; ++idx)
            {
                if (0ul != Ptr[idx])
                {
                    return false;
                }
            }

            return 0ul == (Ptr[idxE] & maskE);
        }

        /// <summary>
        /// Returns true if any of bits in range are set.
        /// </summary>
        /// <param name="pos">Position in bit array.</param>
        /// <param name="numBits">Number of bits to test.</param>
        /// <returns>Returns true if at least one bit is set.</returns>
        public bool TestAny(int pos, int numBits = 1)
        {
            CheckArgs(pos);

            var end = math.min(pos + numBits, Length);
            var idxB = pos >> 6;
            var shiftB = pos & 0x3f;
            var idxE = end >> 6;
            var shiftE = end & 0x3f;
            var maskB = 0xfffffffffffffffful << shiftB;
            var maskE = 0xfffffffffffffffful >> (63 - shiftE);

            if (idxB == idxE)
            {
                var mask = maskB & maskE;
                return 0ul != (Ptr[idxB] & mask);
            }

            if (0ul != (Ptr[idxB] & maskB))
            {
                return true;
            }

            for (var idx = idxB + 1; idx < idxE; ++idx)
            {
                if (0ul != Ptr[idx])
                {
                    return true;
                }
            }

            return 0ul != (Ptr[idxE] & maskE);
        }

        /// <summary>
        /// Returns true if all of bits in range are set.
        /// </summary>
        /// <param name="pos">Position in bit array.</param>
        /// <param name="numBits">Number of bits to test.</param>
        /// <returns>Returns true if all bits are set.</returns>
        public bool TestAll(int pos, int numBits = 1)
        {
            CheckArgs(pos);

            var end = math.min(pos + numBits, Length);
            var idxB = pos >> 6;
            var shiftB = pos & 0x3f;
            var idxE = end >> 6;
            var shiftE = end & 0x3f;
            var maskB = 0xfffffffffffffffful << shiftB;
            var maskE = 0xfffffffffffffffful >> (63 - shiftE);

            if (idxB == idxE)
            {
                var mask = maskB & maskE;
                return mask == (Ptr[idxB] & mask);
            }

            if (maskB != (Ptr[idxB] & maskB))
            {
                return false;
            }

            for (var idx = idxB + 1; idx < idxE; ++idx)
            {
                if (0xfffffffffffffffful != Ptr[idx])
                {
                    return false;
                }
            }

            return maskE == (Ptr[idxE] & maskE);
        }

        /// <summary>
        /// Calculate number of set bits.
        /// </summary>
        /// <param name="pos">Position in bit array.</param>
        /// <param name="numBits">Number of bits to perform count.</param>
        /// <returns>Number of set bits.</returns>
        public int CountBits(int pos, int numBits = 1)
        {
            CheckArgs(pos);

            var end = math.min(pos + numBits, Length);
            var idxB = pos >> 6;
            var shiftB = pos & 0x3f;
            var idxE = end >> 6;
            var shiftE = end & 0x3f;
            var maskB = 0xfffffffffffffffful << shiftB;
            var maskE = 0xfffffffffffffffful >> (63 - shiftE);

            if (idxB == idxE)
            {
                var mask = maskB & maskE;
                return math.countbits(Ptr[idxB] & mask);
            }

            var count = math.countbits(Ptr[idxB] & maskB);

            for (var idx = idxB + 1; idx < idxE; ++idx)
            {
                count += math.countbits(Ptr[idx]);
            }

            count += math.countbits(Ptr[idxE] & maskE);

            return count;
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        [BurstDiscard]
        private void CheckArgs(int pos)
        {
            if (pos < 0
            ||  pos >= Length)
            {
                throw new ArgumentException($"BitArray invalid arguments: pos {pos} (must be 0-{Length-1}).");
            }
        }
    }

    sealed class UnsafeBitArrayDebugView
    {
        UnsafeBitArray Data;

        public UnsafeBitArrayDebugView(UnsafeBitArray data)
        {
            Data = data;
        }

        public bool[] Bits
        {
            get
            {
                var array = new bool[Data.Length];
                for (int i = 0; i < Data.Length; ++i)
                {
                    array[i] = Data.IsSet(i);
                }
                return array;
            }
        }
    }
}