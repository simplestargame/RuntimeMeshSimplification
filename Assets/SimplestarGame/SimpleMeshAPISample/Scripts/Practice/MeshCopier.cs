using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace RuntimeMeshSimplification
{
    public class MeshCopier : MonoBehaviour
    {
        public MeshFilter meshFilter;
        public float quality = 1;
        void Start()
        {
            this.Copy(meshFilter, quality);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct CustomVertexLayout0
        {
            public half4 pos;
            public Color32 normal;
            public Color32 tangent;
            public Color32 color;
            public half2 uv;
        }

        Color32 ColorToColor32(Color color)
        {
            return new Color32(
                (byte)(color.r * 255),
                (byte)(color.g * 255),
                (byte)(color.b * 255),
                (byte)(color.a * 255)
            );
        }

        Color32 Vec3ToColor32(Vector3 v, byte a)
        {
            return new Color32(
                (byte)(v.x * 127),
                (byte)(v.y * 127),
                (byte)(v.z * 127),
                a
            );
        }

        Color32 Vec4ToColor32(Vector4 v)
        {
            return new Color32(
                (byte)(v.x * 127),
                (byte)(v.y * 127),
                (byte)(v.z * 127),
                (byte)(v.w * 127)
            );
        }

        half2 Vec2ToHalf2(Vector2 v)
        {
            return new half2((half)v.x, (half)v.y);
        }

        half4 Vec3ToHalf4(Vector3 v)
        {
            return new half4((half)v.x, (half)v.y, (half)v.z, (half)1);
        }

        public GameObject Copy(MeshFilter sourceMeshFilter, float quality)
        {
            // 既存のMeshFilterからメッシュを取得
            Mesh sourceMesh = sourceMeshFilter.sharedMesh;

            // 新しいGameObjectを作成してMeshFilterを追加
            GameObject newGameObject = new GameObject("ClonedMeshObject");
            MeshFilter newMeshFilter = newGameObject.AddComponent<MeshFilter>();
            MeshRenderer newMeshRenderer = newGameObject.AddComponent<MeshRenderer>();

            int vertexCount = sourceMesh.vertices.Length;
            Mesh.MeshDataArray meshDataArray = Mesh.AllocateWritableMeshData(1);
            Mesh.MeshData meshData = meshDataArray[0];
            int subMeshCount = sourceMesh.subMeshCount;
            
            meshData.SetVertexBufferParams(vertexCount,
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float16, 4, stream: 0),
                new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.SNorm8, 4, stream: 0),
                new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.SNorm8, 4, stream: 0),
                new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4, stream: 0),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float16, 2, stream: 0));
            var vertexData = meshData.GetVertexData<CustomVertexLayout0>(0);

            var vertices = sourceMesh.vertices;
            var normals = sourceMesh.normals;
            var tangents = sourceMesh.tangents;
            var colors = sourceMesh.colors;
            var uv = sourceMesh.uv;

            int indexCount = 0;
            for (int subMeshIdx = 0; subMeshIdx < subMeshCount; subMeshIdx++)
            {
                indexCount += sourceMesh.GetIndices(subMeshIdx).Length;
            }
            meshData.SetIndexBufferParams(indexCount, IndexFormat.UInt32);
            var indexData = meshData.GetIndexData<int>();
            int indexOffset = 0;
            for (int subMeshIdx = 0; subMeshIdx < subMeshCount; subMeshIdx++)
            {
                var srcIndices = sourceMesh.GetIndices(subMeshIdx);
                int subIndexCount = srcIndices.Length;
                for (int idx = 0; idx < subIndexCount; idx++)
                {
                    indexData[idx + indexOffset] = srcIndices[idx];
                }
                indexOffset += subIndexCount;
            }
            for (int vIdx = 0; vIdx < vertexCount; vIdx++)
            {
                vertexData[vIdx] = new CustomVertexLayout0
                {
                    pos = Vec3ToHalf4(vertices[vIdx]),
                    normal = Vec3ToColor32(normals[vIdx], 1),
                    tangent = Vec4ToColor32(tangents[vIdx]),
                    color = colors.Length == vertexCount ? ColorToColor32(colors[vIdx]) : ColorToColor32(Color.white),
                    uv = Vec2ToHalf2(uv[vIdx]),
                };
            }

            var newMesh = new Mesh();
            newMesh.name = "CustomMesh";
            meshData.subMeshCount = subMeshCount;
            int indexStart = 0;
            float3x2 bound = new float3x2();
            for (int subMeshIdx = 0; subMeshIdx < subMeshCount; subMeshIdx++)
            {
                var srcIndices = sourceMesh.GetIndices(subMeshIdx);
                int subIndexCount = srcIndices.Length;
                float3x2 b = new float3x2();
                for (int idx = 0; idx < subIndexCount; idx++)
                {
                    var pos = vertices[srcIndices[idx]];
                    b.c0 = math.min(b.c0, pos);
                    b.c1 = math.max(b.c1, pos);
                    bound.c0 = math.min(bound.c0, pos);
                    bound.c1 = math.max(bound.c1, pos);
                }
                var bounds = new Bounds((b.c0 + b.c1) * 0.5f, b.c1 - b.c0);
                var topology = sourceMesh.GetTopology(subMeshIdx);
                var subMeshDesc = new SubMeshDescriptor
                {
                    topology = topology,
                    vertexCount = vertexCount,
                    indexCount = subIndexCount,
                    baseVertex = 0,
                    firstVertex = 0,
                    indexStart = indexStart,
                    bounds = bounds
                };
                meshData.SetSubMesh(subMeshIdx, subMeshDesc, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers);
                indexStart += subIndexCount;
            }
            Mesh.ApplyAndDisposeWritableMeshData(meshDataArray, new[] { newMesh }, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers);
            newMesh.bounds = new Bounds((bound.c0 + bound.c1) * 0.5f, bound.c1 - bound.c0);
            Physics.BakeMesh(newMesh.GetInstanceID(), false);

            var meshSimplifier = new UnityMeshSimplifier.MeshSimplifier();
            meshSimplifier.Initialize(newMesh);
            meshSimplifier.SimplifyMesh(quality);
            newMeshFilter.sharedMesh = meshSimplifier.ToMesh();

            newMeshRenderer.sharedMaterials = sourceMeshFilter.GetComponent<MeshRenderer>().sharedMaterials;

            return newGameObject;
        }
    }
}