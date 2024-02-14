
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace RuntimeMeshSimplification
{
    public struct NativeMeshSource
    {
        public NativeArray<int> indices;
        public NativeArray<int> subIndicesOffsets;
        public NativeArray<float3> vertices;
        public NativeArray<float3> normals;
        public NativeArray<float4> tangents;
        public NativeArray<float4> colors;
        public NativeArray<float2> uv;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CustomVertexLayout
    {
        public half4 pos;
        public Color32 normal;
        public Color32 tangent;
        public Color32 color;
        public half2 uv;
    }

    internal class CustomLayoutMesh
    {
        public CustomLayoutMesh(int meshCount)
        {
            Allocate(meshCount);
        }
        ~CustomLayoutMesh()
        {
            Dispose();
        }

        public void SetMeshData(int meshIdx, Mesh mesh)
        {
            int subMeshCount = mesh.subMeshCount;
            int indexCount = 0;
            var subMeshTrianglesArray = new int[subMeshCount][];
            for (int subMeshIdx = 0; subMeshIdx < subMeshCount; subMeshIdx++)
            {
                subMeshTrianglesArray[subMeshIdx] = mesh.GetIndices(subMeshIdx);
                indexCount += subMeshTrianglesArray[subMeshIdx].Length;
            }
            copyMeshJobs[meshIdx] = MakeCopyJob(Mesh.AllocateWritableMeshData(1), meshIdx, subMeshCount, mesh.vertices.Length, indexCount, new MeshSource
            {
                subMeshTrianglesArray = subMeshTrianglesArray,
                vertices = mesh.vertices,
                normals = mesh.normals,
                tangents = mesh.tangents,
                colors = mesh.colors,
                uv = mesh.uv,
            });
        }

        public void SetMeshData(Mesh.MeshDataArray meshDataArray, int meshIdx, int subMeshCount,
            int[][] subMeshTrianglesArray, Vector3[] vertices, Vector3[] normals, Vector4[] tangents, Color[] colors, Vector2[] uv)
        {
            int indexCount = 0;
            for (int subMeshIdx = 0; subMeshIdx < subMeshCount; subMeshIdx++)
            {
                indexCount += subMeshTrianglesArray[subMeshIdx].Length;
            }
            copyMeshJobs[meshIdx] = MakeCopyJob(meshDataArray, meshIdx, subMeshCount, vertices.Length, indexCount, new MeshSource
            {
                subMeshTrianglesArray = subMeshTrianglesArray,
                vertices = vertices,
                normals = normals,
                tangents = tangents,
                colors = colors,
                uv = uv,
            });
        }

        public JobHandle Schedule()
        {
            copyMeshJobHandles = new NativeArray<JobHandle>(copyMeshJobs.Length, Allocator.Persistent);
            for (int meshIdx = 0; meshIdx < copyMeshJobs.Length; ++meshIdx)
                copyMeshJobHandles[meshIdx] = copyMeshJobs[meshIdx].Schedule();
            return JobHandle.CombineDependencies(copyMeshJobHandles);
        }

        public Mesh ToMesh(int meshIdx)
        {
            var nativeMeshSource = nativeMeshSources[meshIdx];
            var subMeshCount = nativeMeshSource.subIndicesOffsets.Length;
            var meshBounds = nativeMeshBounds[meshIdx];
            var newMesh = new Mesh();
            newMesh.name = "CustomLayoutMesh" + meshIdx;
            newMesh.bounds = new Bounds((meshBounds[subMeshCount].c0 + meshBounds[subMeshCount].c1) * 0.5f, meshBounds[subMeshCount].c1 - meshBounds[subMeshCount].c0);
            for (int subMeshIdx = 0; subMeshIdx < subMeshCount; subMeshIdx++)
            {
                var meshData = nativeMeshDataArrayArray[meshIdx][0];
                var subMeshDesc = meshData.GetSubMesh(subMeshIdx);
                meshData.SetSubMesh(subMeshIdx, new SubMeshDescriptor
                {
                    topology = subMeshDesc.topology,
                    vertexCount = subMeshDesc.vertexCount,
                    indexCount = subMeshDesc.indexCount,
                    baseVertex = subMeshDesc.baseVertex,
                    firstVertex = subMeshDesc.firstVertex,
                    indexStart = subMeshDesc.indexStart,
                    bounds = new Bounds((meshBounds[subMeshIdx].c0 + meshBounds[subMeshIdx].c1) * 0.5f, meshBounds[subMeshIdx].c1 - meshBounds[subMeshIdx].c0)
                }, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers);
            }
            Mesh.ApplyAndDisposeWritableMeshData(nativeMeshDataArrayArray[meshIdx], new[] { newMesh }, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers);
            return newMesh;
        }

        public Mesh.MeshData GetMeshData(int meshIdx)
        {
            var meshData = nativeMeshDataArrayArray[meshIdx][0];
            return meshData;
        }

        public void Allocate(int meshCount)
        {
            Dispose();
            nativeMeshDataArrayArray = new NativeArray<Mesh.MeshDataArray>(meshCount, Allocator.Persistent);
            nativeMeshSources = new NativeArray<NativeMeshSource>(meshCount, Allocator.Persistent);
            nativeMeshBounds = new NativeArray<NativeArray<float3x2>>(meshCount, Allocator.Persistent);
            copyMeshJobs = new CopyMeshJob[meshCount];
            disposed = false;
        }

        public void Dispose()
        {
            if (disposed) return;

            copyMeshJobHandles.Dispose();
            for (int meshIdx = 0; meshIdx < nativeMeshBounds.Length; meshIdx++)
                nativeMeshBounds[meshIdx].Dispose();
            nativeMeshBounds.Dispose();
            for (int meshIdx = 0; meshIdx < nativeMeshSources.Length; meshIdx++)
            {
                nativeMeshSources[meshIdx].indices.Dispose();
                nativeMeshSources[meshIdx].subIndicesOffsets.Dispose();
                nativeMeshSources[meshIdx].vertices.Dispose();
                nativeMeshSources[meshIdx].normals.Dispose();
                nativeMeshSources[meshIdx].tangents.Dispose();
                nativeMeshSources[meshIdx].colors.Dispose();
                nativeMeshSources[meshIdx].uv.Dispose();
            }
            nativeMeshSources.Dispose();
            nativeMeshDataArrayArray.Dispose();
            disposed = true;
        }

        CopyMeshJob MakeCopyJob(Mesh.MeshDataArray meshDataArray, int meshIdx, int subMeshCount, int vertexCount, int indexCount, MeshSource meshSource)
        {
            nativeMeshDataArrayArray[meshIdx] = meshDataArray;

            Mesh.MeshData meshData = nativeMeshDataArrayArray[meshIdx][0];
            meshData.subMeshCount = subMeshCount;
            meshData.SetVertexBufferParams(vertexCount,
               new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float16, 4, stream: 0),
               new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.SNorm8, 4, stream: 0),
               new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.SNorm8, 4, stream: 0),
               new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4, stream: 0),
               new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float16, 2, stream: 0));

            var vertexData = meshData.GetVertexData<CustomVertexLayout>(stream: 0);
            meshData.SetIndexBufferParams(indexCount, IndexFormat.UInt32);
            var indexData = meshData.GetIndexData<int>();
            var nativeIndices = new NativeArray<int>(indexCount, Allocator.Persistent);
            var nativeSubIndicesOffsets = new NativeArray<int>(subMeshCount, Allocator.Persistent);
            int indexStart = 0;
            int indexOffset = 0;
            for (int subMeshIdx = 0; subMeshIdx < subMeshCount; subMeshIdx++)
            {
                var subIndices = meshSource.subMeshTrianglesArray[subMeshIdx];
                int subIndexCount = subIndices.Length;
                for (int idx = 0; idx < subIndexCount; idx++)
                    nativeIndices[idx + indexOffset] = subIndices[idx];
                indexOffset += subIndexCount;
                nativeSubIndicesOffsets[subMeshIdx] = indexOffset;

                var subMeshDesc = new SubMeshDescriptor
                {
                    topology = MeshTopology.Triangles,
                    vertexCount = vertexCount,
                    indexCount = indexOffset - indexStart,
                    baseVertex = 0,
                    firstVertex = 0,
                    indexStart = indexStart,
                    bounds = new Bounds() // It will be recalculated within the ToMesh function.
                };
                indexStart = indexOffset;
                meshData.SetSubMesh(subMeshIdx, subMeshDesc, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers);
            }

            NativeArray<float3> nativeVertices = new NativeArray<float3>(meshSource.vertices.Length, Allocator.Persistent);
            for (int vIdx = 0; vIdx < meshSource.vertices.Length; vIdx++)
                nativeVertices[vIdx] = new float3(meshSource.vertices[vIdx].x, meshSource.vertices[vIdx].y, meshSource.vertices[vIdx].z);

            NativeArray<float3> nativeNormals = new NativeArray<float3>(meshSource.normals.Length, Allocator.Persistent);
            for (int vIdx = 0; vIdx < meshSource.normals.Length; vIdx++)
                nativeNormals[vIdx] = new float3(meshSource.normals[vIdx].x, meshSource.normals[vIdx].y, meshSource.normals[vIdx].z);

            NativeArray<float4> nativeTangents = new NativeArray<float4>(meshSource.tangents.Length, Allocator.Persistent);
            for (int vIdx = 0; vIdx < meshSource.tangents.Length; vIdx++)
                nativeTangents[vIdx] = new float4(meshSource.tangents[vIdx].x, meshSource.tangents[vIdx].y, meshSource.tangents[vIdx].z, meshSource.tangents[vIdx].w);

            NativeArray<float4> nativeColors = new NativeArray<float4>(meshSource.colors.Length, Allocator.Persistent);
            for (int vIdx = 0; vIdx < meshSource.colors.Length; vIdx++)
                nativeColors[vIdx] = new float4(meshSource.colors[vIdx].r, meshSource.colors[vIdx].g, meshSource.colors[vIdx].b, meshSource.colors[vIdx].a);

            NativeArray<float2> nativeUv = new NativeArray<float2>(meshSource.uv.Length, Allocator.Persistent);
            for (int vIdx = 0; vIdx < meshSource.uv.Length; vIdx++)
                nativeUv[vIdx] = new float2(meshSource.uv[vIdx].x, meshSource.uv[vIdx].y);

            nativeMeshSources[meshIdx] = new NativeMeshSource
            {
                indices = nativeIndices,
                subIndicesOffsets = nativeSubIndicesOffsets,
                vertices = nativeVertices,
                normals = nativeNormals,
                tangents = nativeTangents,
                colors = nativeColors,
                uv = nativeUv,
            };
            nativeMeshBounds[meshIdx] = new NativeArray<float3x2>(subMeshCount + 1, Allocator.Persistent);
            return new CopyMeshJob(nativeMeshSources[meshIdx], indexData, vertexData, nativeMeshBounds[meshIdx]);
        }

        struct MeshSource
        {
            public int[][] subMeshTrianglesArray;
            public Vector3[] vertices;
            public Vector3[] normals;
            public Vector4[] tangents;
            public Color[] colors;
            public Vector2[] uv;
        }

        bool disposed = false;

        NativeArray<JobHandle> copyMeshJobHandles;
        NativeArray<Mesh.MeshDataArray> nativeMeshDataArrayArray;
        NativeArray<NativeMeshSource> nativeMeshSources;
        NativeArray<NativeArray<float3x2>> nativeMeshBounds;
        CopyMeshJob[] copyMeshJobs;
    }
}
