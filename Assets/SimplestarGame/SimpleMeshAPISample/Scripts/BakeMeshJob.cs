using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace RuntimeMeshSimplification
{
    [BurstCompile]
    public struct BakeMeshJob : IJobParallelFor
    {
        private NativeArray<int> meshIds;

        public BakeMeshJob(NativeArray<int> meshIds)
        {
            this.meshIds = meshIds;
        }

        public void Execute(int index)
        {
            Physics.BakeMesh(meshIds[index], false);
        }
    }
}