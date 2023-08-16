using System.Collections;
using System.Linq;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using System.Threading.Tasks;
using UnityMeshSimplifier;
using Unity.Jobs.LowLevel.Unsafe;

namespace SimplestarGame
{
    public class SimpleMeshAPISample : MonoBehaviour
    {
        [SerializeField] Transform copyTarget;
        [SerializeField] float quality = 1f;

        float startTime;
        float duration = 0.016f;

        MeshSimplifier SimplifyMesh(MeshSimplifier meshSimplifier, Mesh.MeshData meshData)
        {
            meshSimplifier.SetMeshData(meshData);
            meshSimplifier.SimplifyMesh(this.quality);
            return meshSimplifier;
        }

        static void MakeCustomLayoutMeshJob(CustomLayoutMesh customLayoutMesh, MeshSimplifier simplifier, int meshIdx)
        {
            int subMeshCount = simplifier.SubMeshCount;
            int[][] subMeshTrianglesArray = new int[subMeshCount][];
            for (int subIdx = 0; subIdx < subMeshCount; subIdx++)
            {
                subMeshTrianglesArray[subIdx] = simplifier.GetSubMeshTriangles(subIdx);
            }
            customLayoutMesh.SetMeshData(meshIdx, subMeshCount, subMeshTrianglesArray,
                simplifier.Vertices, simplifier.Normals, simplifier.Tangents, simplifier.Colors, simplifier.UV1);
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
            // Copy
            Mesh[] sourceMeshes = meshFilters.Select(meshFilter => meshFilter.sharedMesh).ToArray();
            var customLayoutMesh = new CustomLayoutMesh(sourceMeshes.Length);
            for (int meshIdx = 0; meshIdx < sourceMeshes.Length; ++meshIdx)
            {
                if (this.HasTimeElapsed())
                {
                    this.RestStartTime();
                    yield return null;
                }
                var sourceMesh = sourceMeshes[meshIdx];
                customLayoutMesh.SetMeshData(meshIdx, sourceMesh);
            }
            JobHandle combinedHandle = customLayoutMesh.Schedule();
            if (!combinedHandle.IsCompleted)
            {
                yield return null;
            }
            combinedHandle.Complete();
            Mesh.MeshData[] meshDataArray = new Mesh.MeshData[sourceMeshes.Length];
            for (int meshIdx = 0; meshIdx < sourceMeshes.Length; meshIdx++)
            {
                if (this.HasTimeElapsed())
                {
                    this.RestStartTime();
                    yield return null;
                }
                meshDataArray[meshIdx] = customLayoutMesh.GetMeshData(meshIdx);
            }
            // Simplify
            Task<MeshSimplifier>[] simplifyTasks = new Task<MeshSimplifier>[sourceMeshes.Length];
            for (int meshIdx = 0; meshIdx < sourceMeshes.Length; meshIdx++)
            {
                if (this.HasTimeElapsed())
                {
                    this.RestStartTime();
                    yield return null;
                }
                var meshSimplifier = new MeshSimplifier();
                var meshData = meshDataArray[meshIdx];
                simplifyTasks[meshIdx] = Task.Run(() => SimplifyMesh(meshSimplifier, meshData));
            }
            customLayoutMesh.Dispose();
            int lastCompletedTaskIndex = 0;
            while (lastCompletedTaskIndex < simplifyTasks.Length)
            {
                if (!simplifyTasks[lastCompletedTaskIndex].IsCompleted)
                {
                    yield return null;
                }
                else
                {
                    lastCompletedTaskIndex++;
                }
            }
            customLayoutMesh.Allocate(simplifyTasks.Length);
            Task<CustomLayoutMesh>[] createJobTasks = new Task<CustomLayoutMesh>[simplifyTasks.Length];
            for (int meshIdx = 0; meshIdx < simplifyTasks.Length; ++meshIdx)
            {
                if (this.HasTimeElapsed())
                {
                    this.RestStartTime();
                    yield return null;
                }
                MakeCustomLayoutMeshJob(customLayoutMesh, simplifyTasks[meshIdx].Result, meshIdx);
            }
            combinedHandle = customLayoutMesh.Schedule();
            if (!combinedHandle.IsCompleted)
            {
                yield return null;
            }
            combinedHandle.Complete();
            var newMeshes = new Mesh[simplifyTasks.Length];
            for (int meshIdx = 0; meshIdx < sourceMeshes.Length; meshIdx++)
            {
                if (this.HasTimeElapsed())
                {
                    this.RestStartTime();
                    yield return null;
                }
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
    }
}