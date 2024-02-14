
using RuntimeMeshSimplification;
using UnityEngine;

namespace UnityMeshSimplifier
{
    public static class MeshSimplifierExtensions
    {
        const float c = 0.007874f; // for SNorm8
        const float c2 = 0.015748f; // foir UNorm8
        static Vector3 Color32ToVector3(Color32 color32)
        {
            return new Vector3(color32.r, color32.g, color32.b) * c;
        }

        static Vector4 Color32ToVector4(Color32 color32)
        {
            return new Vector4(color32.r, color32.g, color32.b, color32.a) * c;
        }

        static Vector4 Color32ToColor(Color32 color32)
        {
            return new Color(color32.r, color32.g, color32.b, color32.a) * c2;
        }

        public static void SetMeshData(this MeshSimplifier meshSimplifier, Mesh.MeshData meshData)
        {
            var subMeshCount = meshData.subMeshCount;
            var vertexData = meshData.GetVertexData<CustomVertexLayout>(stream: 0);
            var vertexCount = vertexData.Length;
            var vertices = new Vector3[vertexCount];
            var normals = new Vector3[vertexCount];
            var tangents = new Vector4[vertexCount];
            var colors = new Color[vertexCount];
            var uv = new Vector2[vertexCount];
            for (int vIdx = 0; vIdx < vertexCount; vIdx++)
            {
                var vertexLayout = vertexData[vIdx];
                vertices[vIdx] = new Vector3((float)vertexLayout.pos.x, (float)vertexLayout.pos.y, (float)vertexLayout.pos.z);
                normals[vIdx] = Color32ToVector3(vertexLayout.normal);
                tangents[vIdx] = Color32ToVector4(vertexLayout.tangent);
                colors[vIdx] = Color32ToColor(vertexLayout.color);
                uv[vIdx] = new Vector2((float)vertexLayout.uv.x, (float)vertexLayout.uv.y);
            }
            meshSimplifier.Vertices = vertices;
            meshSimplifier.Normals = normals;
            meshSimplifier.Tangents = tangents;
            meshSimplifier.Colors = colors;
            meshSimplifier.UV1 = uv;
            var indexData = meshData.GetIndexData<int>();
            for (int subMeshIdx = 0; subMeshIdx < subMeshCount; subMeshIdx++)
            {
                var subMeshDesc = meshData.GetSubMesh(subMeshIdx);
                var triangles = new int[subMeshDesc.indexCount];
                for (int subTriIdx = 0; subTriIdx < subMeshDesc.indexCount; subTriIdx++)
                {
                    triangles[subTriIdx] = indexData[subMeshDesc.firstVertex + subTriIdx];
                }
                meshSimplifier.AddSubMeshTriangles(triangles);
            }
        }

        public static Mesh ToCustomMesh(this MeshSimplifier meshSimplifier)
        {
            var customLayoutMesh = new CustomLayoutMesh(1);
            int subMeshCount = meshSimplifier.SubMeshCount;
            int[][] subMeshTrianglesArray = new int[subMeshCount][];
            for (int subIdx = 0; subIdx < subMeshCount; subIdx++)
            {
                subMeshTrianglesArray[subIdx] = meshSimplifier.GetSubMeshTriangles(subIdx);
            }
            customLayoutMesh.SetMeshData(Mesh.AllocateWritableMeshData(1), 0, subMeshCount, 
                subMeshTrianglesArray, meshSimplifier.Vertices, meshSimplifier.Normals, meshSimplifier.Tangents, meshSimplifier.Colors, meshSimplifier.UV1);
            customLayoutMesh.Schedule().Complete();
            var mesh = customLayoutMesh.ToMesh(0);
            customLayoutMesh.Dispose();
            return mesh;
        }
    }
}