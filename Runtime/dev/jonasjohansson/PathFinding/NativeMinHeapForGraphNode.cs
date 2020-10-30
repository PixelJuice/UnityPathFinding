﻿using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace dev.jonasjohansson.PathFinding
{
    [NativeContainerSupportsDeallocateOnJobCompletion]
    [NativeContainerSupportsMinMaxWriteRestriction]
    [NativeContainer]
    public unsafe struct NativeMinHeapForGraphNodes : IDisposable
    {
        [NativeDisableUnsafePtrRestriction] private void* m_Buffer;
        private int m_capacity;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        private AtomicSafetyHandle m_Safety;
        [NativeSetClassTypeToNullOnSchedule] private DisposeSentinel m_DisposeSentinel;
#endif
        private Allocator m_AllocatorLabel;

        private int m_head;
        private int m_length;
        private int m_MinIndex;
        private int m_MaxIndex;

        public NativeMinHeapForGraphNodes(int capacity, Allocator allocator, NativeArrayOptions options = NativeArrayOptions.ClearMemory)
        {
            Allocate(capacity, allocator, out this);
            if ((options & NativeArrayOptions.ClearMemory) == NativeArrayOptions.ClearMemory)
                UnsafeUtility.MemClear(m_Buffer, (long)m_capacity * UnsafeUtility.SizeOf<MinHeapGraphNode>());
        }

        private static void Allocate(int capacity, Allocator allocator, out NativeMinHeapForGraphNodes nativeMinHeap)
        {
            var size = (long)UnsafeUtility.SizeOf<MinHeapGraphNode>() * capacity;
            if (allocator <= Allocator.None)
                throw new ArgumentException("Allocator must be Temp, TempJob or Persistent", nameof(allocator));
            if (capacity < 0)
                throw new ArgumentOutOfRangeException(nameof(capacity), "Length must be >= 0");
            if (size > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(capacity),
                    $"Length * sizeof(T) cannot exceed {(object)int.MaxValue} bytes");

            nativeMinHeap.m_Buffer = UnsafeUtility.Malloc(size, UnsafeUtility.AlignOf<MinHeapGraphNode>(), allocator);
            nativeMinHeap.m_capacity = capacity;
            nativeMinHeap.m_AllocatorLabel = allocator;
            nativeMinHeap.m_MinIndex = 0;
            nativeMinHeap.m_MaxIndex = capacity - 1;
            nativeMinHeap.m_head = -1;
            nativeMinHeap.m_length = 0;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            DisposeSentinel.Create(out nativeMinHeap.m_Safety, out nativeMinHeap.m_DisposeSentinel, 1, allocator);
#endif


        }

        public bool HasNext()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
            return m_head >= 0;
        }

        public void Push(MinHeapGraphNode node)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (m_length == m_capacity)
                throw new IndexOutOfRangeException($"Capacity Reached");
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif

            if (m_head < 0)
            {
                m_head = m_length;
            }
            else if (node.fCost < this[m_head].fCost)
            {
                node.Next = m_head;
                m_head = m_length;
            }
            else
            {
                var currentPtr = m_head;
                var current = this[currentPtr];

                while (current.Next >= 0 && this[current.Next].fCost <= node.fCost)
                {
                    currentPtr = current.Next;
                    current = this[current.Next];
                }

                node.Next = current.Next;
                current.Next = m_length;

                UnsafeUtility.WriteArrayElement(m_Buffer, currentPtr, current);
            }

            UnsafeUtility.WriteArrayElement(m_Buffer, m_length, node);
            m_length += 1;
        }

        public MinHeapGraphNode Pop()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
            var result = m_head;
            m_head = this[m_head].Next;
            return this[result];
        }

        public MinHeapGraphNode this[int index]
        {
            get
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (index < m_MinIndex || index > m_MaxIndex)
                    FailOutOfRangeError(index);
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif

                return UnsafeUtility.ReadArrayElement<MinHeapGraphNode>(m_Buffer, index);
            }
        }

        public void Clear()
        {
            m_head = -1;
            m_length = 0;
        }

        public void Dispose()
        {
            if (!UnsafeUtility.IsValidAllocator(m_AllocatorLabel))
                throw new InvalidOperationException("The NativeArray can not be Disposed because it was not allocated with a valid allocator.");
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            DisposeSentinel.Dispose(ref m_Safety, ref m_DisposeSentinel);
#endif
            UnsafeUtility.Free(m_Buffer, m_AllocatorLabel);
            m_Buffer = null;
            m_capacity = 0;
        }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        private void FailOutOfRangeError(int index)
        {
            if (index < m_capacity && (this.m_MinIndex != 0 || this.m_MaxIndex != m_capacity - 1))
                throw new IndexOutOfRangeException(
                    $"Index {(object)index} is out of restricted IJobParallelFor range [{(object)this.m_MinIndex}...{(object)this.m_MaxIndex}] in ReadWriteBuffer.\nReadWriteBuffers are restricted to only read & write the element at the job index. You can use double buffering strategies to avoid race conditions due to reading & writing in parallel to the same elements from a job.");
            throw new IndexOutOfRangeException(
                $"Index {(object)index} is out of range of '{(object)m_capacity}' Length.");
        }
#endif
    }

    public struct MinHeapGraphNode
    {
        public MinHeapGraphNode(Entity p_entity, int p_fCost, int p_hCost, int p_gCost)
        {
            Entity = p_entity;
            Next = -1;
            hCost = p_hCost;
            fCost = p_fCost;
            gCost = p_gCost;
        }
        public Entity Entity;
        public int Next;
        public int hCost;
        public int fCost;
        public int gCost;
    }
}