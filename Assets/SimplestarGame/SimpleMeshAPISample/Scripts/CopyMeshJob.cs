using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace SimplestarGame
{
    [BurstCompile]
    public struct CopyMeshJob : IJob
    {
        NativeMeshSource meshSource;
        [NativeDisableContainerSafetyRestriction] NativeArray<int> indexData;
        [NativeDisableContainerSafetyRestriction] NativeArray<CustomVertexLayout> vertexData;
        [NativeDisableContainerSafetyRestriction] NativeArray<float3x2> boundsData;

        public CopyMeshJob(NativeMeshSource meshSource,
            NativeArray<int> indexData,
            NativeArray<CustomVertexLayout> vertexData,
            NativeArray<float3x2> boundsData)
        {
            this.meshSource = meshSource;
            this.indexData = indexData;
            this.vertexData = vertexData;
            this.boundsData = boundsData;
        }
        half4 float3ToHalf4(float3 v)
        {
            return new half4((half)v.x, (half)v.y, (half)v.z, (half)1);
        }
        Color32 float3ToColor32(float3 v, byte a)
        {
            return new Color32(
                (byte)(v.x * 127),
                (byte)(v.y * 127),
                (byte)(v.z * 127),
                a
            );
        }
        Color32 float4ToColor32(float4 v)
        {
            return new Color32(
                (byte)(v.x * 127),
                (byte)(v.y * 127),
                (byte)(v.z * 127),
                (byte)(v.w * 127)
            );
        }
        half2 float2ToHalf2(float2 v)
        {
            return new half2((half)v.x, (half)v.y);
        }
        public void Execute()
        {
            float3x2 bounds = new float3x2();
            var indices = this.meshSource.indices;
            var subIndicesOffsets = this.meshSource.subIndicesOffsets;
            var vertices = this.meshSource.vertices;
            int subMeshCount = subIndicesOffsets.Length;
            int lastIndexOffset = 0;
            for (int subMeshIdx = 0; subMeshIdx < subMeshCount; subMeshIdx++)
            {
                int indexOffset = subIndicesOffsets[subMeshIdx];
                float3x2 bound = new float3x2();
                for (int idx = lastIndexOffset; idx < indexOffset; idx++)
                {
                    int index = indices[idx];
                    this.indexData[idx] = index;
                    var pos = vertices[index];
                    bound.c0 = math.min(bound.c0, pos);
                    bound.c1 = math.max(bound.c1, pos);

                    bounds.c0 = math.min(bounds.c0, pos);
                    bounds.c1 = math.max(bounds.c1, pos);
                }
                lastIndexOffset = indexOffset;
                this.boundsData[subMeshIdx] = bound;
            }
            this.boundsData[subMeshCount] = bounds;
            int vertexCount = vertices.Length;
            for (int vIdx = 0; vIdx < vertexCount; vIdx++)
            {
                vertexData[vIdx] = new CustomVertexLayout
                {
                    pos = float3ToHalf4(meshSource.vertices[vIdx]),
                    normal = float3ToColor32(meshSource.normals[vIdx], 1),
                    tangent = float4ToColor32(meshSource.tangents[vIdx]),
                    color = meshSource.colors.Length == vertexCount ? float4ToColor32(meshSource.colors[vIdx]) : new Color32(255, 255, 255, 255),
                    uv = float2ToHalf2(meshSource.uv[vIdx]),
                };
            }
        }
    }
}
