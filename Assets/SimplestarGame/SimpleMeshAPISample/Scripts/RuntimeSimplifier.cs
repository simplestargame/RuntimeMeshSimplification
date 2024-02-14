using System.Linq;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;
using UnityMeshSimplifier;

namespace RuntimeMeshSimplification
{
    public class RuntimeSimplifier
    {
        public Mesh Mesh { get; private set; }

        public RuntimeSimplifier(Mesh fromMesh)
        {
            Mesh = fromMesh;
        }

        /// <summary>
        /// Simplify one mesh
        /// </summary>
        /// <returns>simplified mesh</returns>
        public async Task<Mesh> Simplify(float quality01, bool recalculateNormals = true)
        {
            // Copy
            var customLayoutMesh = new CustomLayoutMesh(1);
            customLayoutMesh.SetMeshData(0, Mesh);

            JobHandle combinedHandle = customLayoutMesh.Schedule();
            while (!combinedHandle.IsCompleted)
                await Task.Yield();
            combinedHandle.Complete();

            Mesh.MeshData meshData = new Mesh.MeshData();
            meshData = customLayoutMesh.GetMeshData(0);

            // Simplify
            var meshSimplifier = new MeshSimplifier();
            meshSimplifier.SetMeshData(meshData);
            meshSimplifier.SimplifyMesh(quality01);
            customLayoutMesh.Dispose();

            customLayoutMesh.Allocate(1);
            var newMeshDataArray = Mesh.AllocateWritableMeshData(1);
            MakeCustomLayoutMeshJob(newMeshDataArray, customLayoutMesh, meshSimplifier, 0);

            combinedHandle = customLayoutMesh.Schedule();
            while (!combinedHandle.IsCompleted)
                await Task.Yield();
            combinedHandle.Complete();

            Mesh = customLayoutMesh.ToMesh(0);
            customLayoutMesh.Dispose();

            int meshId = Mesh.GetInstanceID();
            Physics.BakeMesh(meshId, false);

            if (recalculateNormals)
            {
                Mesh.RecalculateNormals();
                Mesh.RecalculateTangents();
            }

            return Mesh;
        }

        /// <summary>
        /// Simplify complex mesh to new GameObject
        /// </summary>
        /// <returns>New GameObject with simplified meshes</returns>
        public static async Task<GameObject> Simplify(GameObject referenceTarget, float quality01, bool recalculateNormals = true)
        {
            MeshFilter[] meshFilters = referenceTarget.GetComponentsInChildren<MeshFilter>(true);
            await Task.Yield();

            // Copy
            Mesh[] sourceMeshes = meshFilters.Select(meshFilter => meshFilter.sharedMesh).ToArray();
            var customLayoutMesh = new CustomLayoutMesh(sourceMeshes.Length);
            for (int meshIdx = 0; meshIdx < sourceMeshes.Length; ++meshIdx)
            {
                await Task.Yield();
                var sourceMesh = sourceMeshes[meshIdx];
                customLayoutMesh.SetMeshData(meshIdx, sourceMesh);
            }

            JobHandle combinedHandle = customLayoutMesh.Schedule();
            if (!combinedHandle.IsCompleted)
                await Task.Yield();
            combinedHandle.Complete();

            Mesh.MeshData[] meshDataArray = new Mesh.MeshData[sourceMeshes.Length];
            for (int meshIdx = 0; meshIdx < sourceMeshes.Length; meshIdx++)
            {
                await Task.Yield();
                meshDataArray[meshIdx] = customLayoutMesh.GetMeshData(meshIdx);
            }

            // Simplify
            var meshSimplifiers = new MeshSimplifier[sourceMeshes.Length];
            for (int meshIdx = 0; meshIdx < sourceMeshes.Length; meshIdx++)
            {
                await Task.Yield();
                var meshSimplifier = new MeshSimplifier();
                var meshData = meshDataArray[meshIdx];
                meshSimplifier.SetMeshData(meshData);
                meshSimplifier.SimplifyMesh(quality01);
                meshSimplifiers[meshIdx] = meshSimplifier;
            }
            customLayoutMesh.Dispose();

            customLayoutMesh.Allocate(sourceMeshes.Length);
            for (int meshIdx = 0; meshIdx < sourceMeshes.Length; ++meshIdx)
            {
                await Task.Yield();
                var newMeshDataArray = Mesh.AllocateWritableMeshData(1);
                var meshSimplifier = meshSimplifiers[meshIdx];
                var newMeshIndex = meshIdx;
                MakeCustomLayoutMeshJob(newMeshDataArray, customLayoutMesh, meshSimplifier, newMeshIndex);
            }

            combinedHandle = customLayoutMesh.Schedule();
            if (!combinedHandle.IsCompleted)
                await Task.Yield();
            combinedHandle.Complete();

            var newMeshes = new Mesh[meshSimplifiers.Length];
            for (int meshIdx = 0; meshIdx < sourceMeshes.Length; meshIdx++)
            {
                await Task.Yield();
                newMeshes[meshIdx] = customLayoutMesh.ToMesh(meshIdx);
            }
            customLayoutMesh.Dispose();

            // Bake
            NativeArray<int> meshIds = new NativeArray<int>(newMeshes.Length, Allocator.Persistent);
            for (int meshIdx = 0; meshIdx < newMeshes.Length; ++meshIdx)
            {
                meshIds[meshIdx] = newMeshes[meshIdx].GetInstanceID();
            }
            var bakeMeshJob = new BakeMeshJob(meshIds);
            int innerloopBatchCount = GetNearestPowerOfTwo(meshIds.Length / JobsUtility.JobWorkerCount);
            var bakeMeshJobHandle = bakeMeshJob.Schedule(meshIds.Length, innerloopBatchCount);
            while (!bakeMeshJobHandle.IsCompleted)
                await Task.Yield();
            bakeMeshJobHandle.Complete();
            meshIds.Dispose();

            GameObject newParent = new GameObject(referenceTarget.name + quality01);

            for (int i = 0; i < newMeshes.Length; i++)
            {
                await Task.Yield();
                var newMesh = newMeshes[i];
                var sourceMeshFilter = meshFilters[i];
                GameObject newGameObject = new GameObject("ClonedMeshObject");
                newGameObject.transform.position = sourceMeshFilter.transform.position;
                newGameObject.transform.rotation = sourceMeshFilter.transform.rotation;
                if (recalculateNormals)
                {
                    newMesh.RecalculateNormals();
                    newMesh.RecalculateTangents();
                }
                newGameObject.AddComponent<MeshFilter>().sharedMesh = newMesh;
                newGameObject.AddComponent<MeshRenderer>().sharedMaterials = sourceMeshFilter.GetComponent<MeshRenderer>().sharedMaterials;
                newGameObject.AddComponent<MeshCollider>().sharedMesh = newMesh;

                newGameObject.transform.SetParent(newParent.transform);
            }

            return newParent;
        }

        private static void MakeCustomLayoutMeshJob(Mesh.MeshDataArray meshDataArray, CustomLayoutMesh customLayoutMesh, MeshSimplifier simplifier, int meshIdx)
        {
            int subMeshCount = simplifier.SubMeshCount;
            int[][] subMeshTrianglesArray = new int[subMeshCount][];
            for (int subIdx = 0; subIdx < subMeshCount; subIdx++)
                subMeshTrianglesArray[subIdx] = simplifier.GetSubMeshTriangles(subIdx);
            customLayoutMesh.SetMeshData(meshDataArray, meshIdx, subMeshCount, subMeshTrianglesArray,
                simplifier.Vertices, simplifier.Normals, simplifier.Tangents, simplifier.Colors, simplifier.UV1);
        }

        private static int GetNearestPowerOfTwo(int value)
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
    }
}