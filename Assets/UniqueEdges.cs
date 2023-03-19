using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class UniqueEdges
{
    public struct Edge {
        public Vector3 a;
        public Vector3 b;

        public void Sort() {
            if (a.x > b.x) {
                var temp = a;
                a = b;
                b = temp;
            } else if (a.x == b.x) {
                if (a.y > b.y) {
                    var temp = a;
                    a = b;
                    b = temp;
                } else if (a.y == b.y) {
                    if (a.z > b.z) {
                        var temp = a;
                        a = b;
                        b = temp;
                    }
                }
            }
        }
    }

    public struct EdgeVertexIndices {
        public int a;
        public int b;
    }

    public struct EdgeTriangleIndices {
        public int a;
        public int b;
    }
    
    public static void UniqueIndexedEdges(Vector3[] vertices, int[] triangles, out EdgeTriangleIndices[] triangleIndices, out EdgeVertexIndices[] vertexIndices) {
        var allEdges = new Dictionary<Edge, ((int, int), (int, int))>();

        void AddEdge(int a, int b, int i) {
            var edge = new Edge {
                a = vertices[a],
                b = vertices[b]
            };
            edge.Sort();

            if (allEdges.TryGetValue(edge, out var value)) {
                allEdges[edge] = ((a, b), (value.Item2.Item1, i));
            } else {
                allEdges[edge] = ((a, b), (i, -1));
            }
        }

        for (int i = 0; i < triangles.Length; i += 3) {
            var j = Mathf.FloorToInt((float)i / 3.0f);
            AddEdge(triangles[i], triangles[i + 1], j);
            AddEdge(triangles[i + 1], triangles[i + 2], j);
            AddEdge(triangles[i + 2], triangles[i], j);
        }

        var vertexIndicesList = new List<EdgeVertexIndices>();
        var triangleIndicesList = new List<EdgeTriangleIndices>();

        foreach (var edge in allEdges) {
            var vert = edge.Value.Item1;
            var tri = edge.Value.Item2;

            vertexIndicesList.Add(new EdgeVertexIndices {
                a = vert.Item1,
                b = vert.Item2
            });

            triangleIndicesList.Add(new EdgeTriangleIndices {
                a = tri.Item1,
                b = tri.Item2
            });
        }

        triangleIndices = triangleIndicesList.ToArray();
        vertexIndices = vertexIndicesList.ToArray();
    }
}
