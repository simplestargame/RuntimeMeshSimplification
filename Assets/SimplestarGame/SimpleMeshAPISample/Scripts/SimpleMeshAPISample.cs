﻿using System.Linq;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;
using UnityMeshSimplifier;

namespace RuntimeMeshSimplification.Sample
{
    /// <summary>
    /// Example using
    /// </summary>
    public class SimpleMeshAPISample : MonoBehaviour
    {
        [SerializeField] private Transform referenceTarget;
        [SerializeField] private Vector3 offsetPosition;
        [SerializeField, Range(0, 1)] private float quality = .5f;
        [SerializeField] private bool recalculateNormals = true;

        private void MakeCustomLayoutMeshJob(Mesh.MeshDataArray meshDataArray, CustomLayoutMesh customLayoutMesh, MeshSimplifier simplifier, int meshIdx)
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

        private void Start()
        {
            _ = Simplify();
        }

        private async Task Simplify()
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
                meshSimplifier.SimplifyMesh(quality);
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

            for (int i = 0; i < newMeshes.Length; i++)
            {
                await Task.Yield();
                var newMesh = newMeshes[i];
                var sourceMeshFilter = meshFilters[i];
                GameObject newGameObject = new GameObject("ClonedMeshObject");
                newGameObject.transform.position = sourceMeshFilter.transform.position + offsetPosition;
                newGameObject.transform.rotation = sourceMeshFilter.transform.rotation;
                if (recalculateNormals)
                {
                    newMesh.RecalculateNormals();
                    newMesh.RecalculateTangents();
                }
                newGameObject.AddComponent<MeshFilter>().sharedMesh = newMesh;
                newGameObject.AddComponent<MeshRenderer>().sharedMaterials = sourceMeshFilter.GetComponent<MeshRenderer>().sharedMaterials;
                newGameObject.AddComponent<MeshCollider>().sharedMesh = newMesh;
            }
        }
    }
}