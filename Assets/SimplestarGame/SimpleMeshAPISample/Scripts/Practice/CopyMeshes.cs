using System.Collections;
using System.Linq;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using System.Threading.Tasks;
using UnityMeshSimplifier;
using Unity.Jobs.LowLevel.Unsafe;

namespace SimplestarGame
{ 
    public class CopyMeshes : MonoBehaviour
    {
        [SerializeField] Transform copyTarget;
        [SerializeField] float quality = 1f;

        float startTime;
        float duration = 0.016f;
        MeshSimplifier[] SimplifyMeshTask(MeshSimplifier[] meshSimplifiers)
        {
            foreach (var meshSimplifier in meshSimplifiers)
            {
                meshSimplifier.SimplifyMesh(this.quality);
            }
            return meshSimplifiers;
        }

        static int GetNearestPowerOfTwo(int value)
        {
            int powerOfTwo = 1;

            while (powerOfTwo < value)
            {
                powerOfTwo *= 2;
            }

            int lowerPowerOfTwo = powerOfTwo / 2;
            int upperPowerOfTwo = powerOfTwo;

            return (upperPowerOfTwo - value) < (value - lowerPowerOfTwo) ? upperPowerOfTwo : lowerPowerOfTwo;
        }

        bool HasTimeElapsed()
        {
            float currentTime = Time.realtimeSinceStartup;
            float elapsedTime = currentTime - this.startTime;

            return elapsedTime >= this.duration;
        }

        void RestStartTime()
        {
            this.startTime = Time.realtimeSinceStartup;
        }
        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                this.RestStartTime();
                var targetMeshes = this.copyTarget.GetComponentsInChildren<MeshFilter>(true);
                StartCoroutine(CoCopyMesh(targetMeshes));
            }
        }
        IEnumerator CoCopyMesh(MeshFilter[] meshFilters)
        {
            if (this.HasTimeElapsed())
            {
                this.RestStartTime();
                yield return null;
            }
            Mesh[] sourceMeshes = meshFilters.Select(meshFilter => meshFilter.sharedMesh).ToArray();
            var nativeMeshDataArrayArray = new NativeArray<Mesh.MeshDataArray>(sourceMeshes.Length, Allocator.Persistent);
            var meshSources = new NativeArray<NativeMeshSource>(sourceMeshes.Length, Allocator.Persistent);
            var meshBounds = new NativeArray<NativeArray<float3x2>>(sourceMeshes.Length, Allocator.Persistent);
            var copyMeshJobs = new CopyMeshJob[sourceMeshes.Length];
            for (int meshIdx = 0; meshIdx < sourceMeshes.Length; ++meshIdx)
            {
                if (this.HasTimeElapsed())
                {
                    this.RestStartTime();
                    yield return null;
                }
                var sourceMesh = sourceMeshes[meshIdx];
                nativeMeshDataArrayArray[meshIdx] = Mesh.AllocateWritableMeshData(1);
                Mesh.MeshData meshData = nativeMeshDataArrayArray[meshIdx][0];

                int subMeshCount = sourceMesh.subMeshCount;
                meshData.subMeshCount = subMeshCount;

                int vertexCount = sourceMesh.vertices.Length;
                meshData.SetVertexBufferParams(vertexCount,
                   new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float16, 4, stream: 0),
                   new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.SNorm8, 4, stream: 0),
                   new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.SNorm8, 4, stream: 0),
                   new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4, stream: 0),
                   new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float16, 2, stream: 0));
                var vertexData = meshData.GetVertexData<CustomVertexLayout>(stream: 0);

                int indexCount = 0;
                for (int subMeshIdx = 0; subMeshIdx < subMeshCount; subMeshIdx++)
                {
                    indexCount += sourceMesh.GetIndices(subMeshIdx).Length;
                }
                meshData.SetIndexBufferParams(indexCount, IndexFormat.UInt32);
                var indexData = meshData.GetIndexData<int>();

                var nativeIndices = new NativeArray<int>(indexCount, Allocator.Persistent);
                var nativeSubIndicesOffsets = new NativeArray<int>(subMeshCount, Allocator.Persistent);
                int indexOffset = 0;
                for (int subMeshIdx = 0; subMeshIdx < subMeshCount; subMeshIdx++)
                {
                    var srcIndices = sourceMesh.GetIndices(subMeshIdx);
                    int subIndexCount = srcIndices.Length;
                    for (int idx = 0; idx < subIndexCount; idx++)
                    {
                        nativeIndices[idx + indexOffset] = srcIndices[idx];
                    }
                    indexOffset += subIndexCount;
                    nativeSubIndicesOffsets[subMeshIdx] = indexOffset;
                }
                var vertices = sourceMesh.vertices;
                NativeArray<float3> nativeVertices = new NativeArray<float3>(vertices.Length, Allocator.Persistent);
                for (int vIdx = 0; vIdx < vertices.Length; vIdx++)
                {
                    nativeVertices[vIdx] = new float3(vertices[vIdx].x, vertices[vIdx].y, vertices[vIdx].z);
                }
                var normals = sourceMesh.normals;
                NativeArray<float3> nativeNormals = new NativeArray<float3>(normals.Length, Allocator.Persistent);
                for (int vIdx = 0; vIdx < normals.Length; vIdx++)
                {
                    nativeNormals[vIdx] = new float3(normals[vIdx].x, normals[vIdx].y, normals[vIdx].z);
                }
                var tangents = sourceMesh.tangents;
                NativeArray<float4> nativeTangents = new NativeArray<float4>(tangents.Length, Allocator.Persistent);
                for (int vIdx = 0; vIdx < tangents.Length; vIdx++)
                {
                    nativeTangents[vIdx] = new float4(tangents[vIdx].x, tangents[vIdx].y, tangents[vIdx].z, tangents[vIdx].w);
                }
                var colors = sourceMesh.colors;
                NativeArray<float4> nativeColors = new NativeArray<float4>(colors.Length, Allocator.Persistent);
                for (int vIdx = 0; vIdx < colors.Length; vIdx++)
                {
                    nativeColors[vIdx] = new float4(colors[vIdx].r, colors[vIdx].g, colors[vIdx].b, colors[vIdx].a);
                }
                var uv = sourceMesh.uv;
                NativeArray<float2> nativeUv = new NativeArray<float2>(uv.Length, Allocator.Persistent);
                for (int vIdx = 0; vIdx < uv.Length; vIdx++)
                {
                    nativeUv[vIdx] = new float2(uv[vIdx].x, uv[vIdx].y);
                }
                meshSources[meshIdx] = new NativeMeshSource
                {
                    indices = nativeIndices,
                    subIndicesOffsets = nativeSubIndicesOffsets,
                    vertices = nativeVertices,
                    normals = nativeNormals,
                    tangents = nativeTangents,
                    colors = nativeColors,
                    uv = nativeUv,
                };
                meshBounds[meshIdx] = new NativeArray<float3x2>(subMeshCount + 1, Allocator.Persistent);
                copyMeshJobs[meshIdx] = new CopyMeshJob(meshSources[meshIdx], indexData, vertexData, meshBounds[meshIdx]);
            }
            var copyMeshJobHandles = new NativeArray<JobHandle>(sourceMeshes.Length, Allocator.Persistent);
            for (int meshIdx = 0; meshIdx < sourceMeshes.Length; ++meshIdx)
            {
                copyMeshJobHandles[meshIdx] = copyMeshJobs[meshIdx].Schedule();
            }
            JobHandle combinedHandle = JobHandle.CombineDependencies(copyMeshJobHandles);
            if (!combinedHandle.IsCompleted)
            {
                yield return null;
            }
            combinedHandle.Complete();
            copyMeshJobHandles.Dispose();

            Mesh[] newMeshes = new Mesh[sourceMeshes.Length];
            for (int meshIdx = 0; meshIdx < meshSources.Length; meshIdx++)
            {
                if (this.HasTimeElapsed())
                {
                    this.RestStartTime();
                    yield return null;
                }
                newMeshes[meshIdx] = CreateNewMesh(sourceMeshes[meshIdx], nativeMeshDataArrayArray[meshIdx], meshBounds[meshIdx], meshIdx);
                meshBounds[meshIdx].Dispose();
                meshSources[meshIdx].indices.Dispose();
                meshSources[meshIdx].subIndicesOffsets.Dispose();
                meshSources[meshIdx].vertices.Dispose();
                meshSources[meshIdx].normals.Dispose();
                meshSources[meshIdx].tangents.Dispose();
                meshSources[meshIdx].colors.Dispose();
                meshSources[meshIdx].uv.Dispose();
            }
            meshBounds.Dispose();
            meshSources.Dispose();
            nativeMeshDataArrayArray.Dispose();
            MeshSimplifier[] meshSimplifiers = new MeshSimplifier[newMeshes.Length];
            for (int meshIdx = 0; meshIdx < meshSources.Length; meshIdx++)
            {
                if (this.HasTimeElapsed())
                {
                    this.RestStartTime();
                    yield return null;
                }
                var newMesh = newMeshes[meshIdx];
                var meshSimplifier = new MeshSimplifier();
                meshSimplifier.Vertices = newMesh.vertices;
                meshSimplifier.Normals = newMesh.normals;
                meshSimplifier.Tangents = newMesh.tangents;
                meshSimplifier.Colors = newMesh.colors;
                meshSimplifier.UV1 = newMesh.uv;
                for (int i = 0; i < newMesh.subMeshCount; i++)
                {
                    meshSimplifier.AddSubMeshTriangles(newMesh.GetTriangles(i));
                }
                meshSimplifiers[meshIdx] = meshSimplifier;
            }
            var simplifyMeshTask = Task.Run(() => SimplifyMeshTask(meshSimplifiers));
            while (!simplifyMeshTask.IsCompleted)
            {
                yield return null;
            }
            for (int meshIdx = 0; meshIdx < meshSimplifiers.Length; meshIdx++)
            {
                if (this.HasTimeElapsed())
                {
                    this.RestStartTime();
                    yield return null;
                }
                newMeshes[meshIdx].Clear();
                newMeshes[meshIdx] = simplifyMeshTask.Result[meshIdx].ToCustomMesh();
            }
            NativeArray<int> meshIds = new NativeArray<int>(newMeshes.Length, Allocator.Persistent);
            for (int meshIdx = 0; meshIdx < newMeshes.Length; ++meshIdx)
            {
                meshIds[meshIdx] = newMeshes[meshIdx].GetInstanceID();
            }
            var bakeMeshJob = new BakeMeshJob(meshIds);
            int innerloopBatchCount = GetNearestPowerOfTwo(meshIds.Length / JobsUtility.JobWorkerCount);
            var bakeMeshJobHandle = bakeMeshJob.Schedule(meshIds.Length, innerloopBatchCount);
            while (!bakeMeshJobHandle.IsCompleted)
            {
                yield return null;
            }
            bakeMeshJobHandle.Complete();
            meshIds.Dispose();
            for (int i = 0; i < newMeshes.Length; i++)
            {
                if (this.HasTimeElapsed())
                {
                    this.RestStartTime();
                    yield return null;
                }
                var newMesh = newMeshes[i];
                var sourceMeshFilter = meshFilters[i];
                GameObject newGameObject = new GameObject("ClonedMeshObject");
                newGameObject.transform.position = sourceMeshFilter.transform.position;
                newGameObject.transform.rotation = sourceMeshFilter.transform.rotation;
                newGameObject.AddComponent<MeshFilter>().sharedMesh = newMesh;
                newGameObject.AddComponent<MeshRenderer>().sharedMaterials = sourceMeshFilter.GetComponent<MeshRenderer>().sharedMaterials;
                newGameObject.AddComponent<MeshCollider>().sharedMesh = newMesh;
            }
        }

        private static Mesh CreateNewMesh(Mesh sourceMesh, Mesh.MeshDataArray nativeMeshDataArray, NativeArray<float3x2> meshBounds, int meshIdx)
        {
            int subMeshCount = sourceMesh.subMeshCount;
            var newMesh = new Mesh();
            newMesh.name = "CustomMesh" + meshIdx;
            newMesh.bounds = new Bounds((meshBounds[subMeshCount].c0 + meshBounds[subMeshCount].c1) * 0.5f, meshBounds[subMeshCount].c1 - meshBounds[subMeshCount].c0);

            int indexStart = 0;
            for (int subMeshIdx = 0; subMeshIdx < subMeshCount; subMeshIdx++)
            {
                var topology = sourceMesh.GetTopology(subMeshIdx);
                var srcIndices = sourceMesh.GetIndices(subMeshIdx);
                int subIndexCount = srcIndices.Length;
                var bound = new Bounds((meshBounds[subMeshIdx].c0 + meshBounds[subMeshIdx].c1) * 0.5f, meshBounds[subMeshIdx].c1 - meshBounds[subMeshIdx].c0);
                var subMeshDesc = new SubMeshDescriptor
                {
                    topology = topology,
                    vertexCount = sourceMesh.vertices.Length,
                    indexCount = subIndexCount,
                    baseVertex = 0,
                    firstVertex = 0,
                    indexStart = indexStart,
                    bounds = bound
                };
                nativeMeshDataArray[0].SetSubMesh(subMeshIdx, subMeshDesc, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers);
                indexStart += subIndexCount;
            }
            Mesh.ApplyAndDisposeWritableMeshData(nativeMeshDataArray, new[] { newMesh }, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers);
            return newMesh;
        }
    }
}
