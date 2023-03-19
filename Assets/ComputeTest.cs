using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public class ComputeTest : MonoBehaviour
{
    enum TestMode
    {
        Cpu,
        Compute,
        ComputeAndTransfer
    }

    [SerializeField]
    TestMode mode = TestMode.ComputeAndTransfer;

    [SerializeField]
    bool draw = true;

    [SerializeField]
    uint iterations = 100;

    public static ComputeTest instance;

    private void Awake()
    {
        instance = this;
    }

    [SerializeField]
    ComputeShader compute;

    [SerializeField]
    MeshFilter filter;

    [SerializeField]
    new Transform light;

    [SerializeField]
    Transform plane;

    Buffers buffers;

    UniqueEdges.EdgeVertexIndices[] edgeVertexIndices;
    UniqueEdges.EdgeTriangleIndices[] edgeTriangleIndices;
    int[] triangles;
    Vector3[] vertices;

    private void Start()
    {
        Setup(out edgeVertexIndices, out edgeTriangleIndices, out triangles, out vertices);

        startTime = Time.realtimeSinceStartup;
    }

    uint iteration = 0;
    float startTime;

    private void Update()
    {
        // if (iteration >= iterations) return;

        if (mode == TestMode.Compute || mode == TestMode.ComputeAndTransfer)
            ComputeCalculation();
        else
            CpuCalculation();

        if (iteration == iterations - 1)
        {
            var endTime = Time.realtimeSinceStartup;
            var time = endTime - startTime;
            Debug.Log($"Time: {time}");
            var average = time / (float)iterations;
            Debug.Log($"Average: {average}");
        }

        iteration++;
    }

    private void ComputeCalculation()
    {
        var kernel = compute.FindKernel("Transform");

        compute.SetVector("light", light.position);
        compute.SetMatrix("transform", filter.transform.localToWorldMatrix);
        compute.SetBuffer(kernel, "vertices", buffers.vertices);
        compute.SetBuffer(kernel, "transformed", buffers.transformed);

        var threads = CalculateThreadGroupSize(buffers.vertices.count, 1024);
        compute.Dispatch(kernel, threads.x, threads.y, threads.z);

        // FACING

        threads = CalculateThreadGroupSize(buffers.facing.count, 1024);
        kernel = compute.FindKernel("Facing");
        compute.SetBuffer(kernel, "transformed", buffers.transformed);
        compute.SetBuffer(kernel, "triangles", buffers.triangles);
        compute.SetBuffer(kernel, "facing", buffers.facing);
        compute.Dispatch(kernel, threads.x, threads.y, threads.z);

        kernel = compute.FindKernel("Projection");
        compute.SetBuffer(kernel, "transformed", buffers.transformed);
        compute.SetBuffer(kernel, "projection", buffers.projection);

        var planeDistance = Vector3.Dot(plane.position, plane.up);
        compute.SetFloat("planeDistance", planeDistance);
        compute.SetVector("planeNormal", plane.up);

        threads = CalculateThreadGroupSize(buffers.transformed.count, 1024);
        compute.Dispatch(kernel, threads.x, threads.y, threads.z);

        // SILHOUETTE

        kernel = compute.FindKernel("Silhouette");
        compute.SetBuffer(kernel, "facing", buffers.facing);
        compute.SetBuffer(kernel, "edges", buffers.edges);
        compute.SetBuffer(kernel, "silhouette", buffers.silhouette);

        threads = CalculateThreadGroupSize(buffers.edges.count, 1024);
        compute.Dispatch(kernel, threads.x, threads.y, threads.z);

        if (!(mode == TestMode.ComputeAndTransfer)) return;

        var silhouette = new int[buffers.silhouette.count];
        buffers.silhouette.GetData(silhouette);

        var projection = new Vector3[buffers.transformed.count];
        buffers.projection.GetData(projection);

        if (draw)
            for (int i = 0; i < silhouette.Length; i++)
            {
                if (silhouette[i] == 1)
                {
                    var edge = edgeVertexIndices[i];
                    Debug.DrawLine(projection[edge.a], projection[edge.b], Color.yellow);
                }
            }
    }

    void CpuCalculation()
    {
        var transformed = new Vector3[vertices.Length];

        var transform = filter.transform.localToWorldMatrix;
        for (int i = 0; i < vertices.Length; i++)
        {
            transformed[i] = transform.MultiplyPoint(vertices[i]);
        }

        var facing = new int[Mathf.FloorToInt((float)triangles.Length / 3.0f)];
        for (int i = 0; i < facing.Length; i++)
        {
            var j = i * 3;
            var a = transformed[triangles[j]];
            var b = transformed[triangles[j + 1]];
            var c = transformed[triangles[j + 2]];

            var normal = Vector3.Cross(b - a, c - a).normalized;
            var lightDir = a - light.position;
            var dot = Vector3.Dot(normal, lightDir);

            if (dot < 0)
            {
                facing[i] = 1;
            }
        }

        var projection = new Vector3[transformed.Length];
        var planeDistance = Vector3.Dot(plane.position, plane.up);
        var planeNormal = plane.up;
        var lightPos = light.position;

        for (int i = 0; i < projection.Length; i++)
        {
            var point = transformed[i];
            var d = point - lightPos;
            var t = (planeDistance - Vector3.Dot(point, planeNormal)) / Vector3.Dot(d, planeNormal);
            projection[i] = point + d * t;
        }

        var silhouette = new int[edgeVertexIndices.Length];
        for (int i = 0; i < edgeTriangleIndices.Length; i++)
        {
            var edge = edgeTriangleIndices[i];

            if (edge.b == -1)
            {
                silhouette[i] = 1;
                continue;
            }

            if (facing[edge.a] != facing[edge.b])
            {
                silhouette[i] = 1;
            }
        }

        if (draw)
            for (int i = 0; i < silhouette.Length; i++)
            {
                if (silhouette[i] == 1)
                {
                    var edge = edgeVertexIndices[i];
                    Debug.DrawLine(projection[edge.a], projection[edge.b], Color.yellow);
                }
            }
    }

    void Setup(out UniqueEdges.EdgeVertexIndices[] edgeVertexIndices, out UniqueEdges.EdgeTriangleIndices[] edgeTriangleIndices, out int[] triangles, out Vector3[] vertices)
    {
        var mesh = filter.sharedMesh;

        vertices = mesh.vertices;
        triangles = mesh.triangles;
        UniqueEdges.UniqueIndexedEdges(vertices, triangles, out edgeTriangleIndices, out edgeVertexIndices);

        buffers = new Buffers
        {
            vertices = new ComputeBuffer(vertices.Length, sizeof(float) * 3),
            transformed = new ComputeBuffer(vertices.Length, sizeof(float) * 3),
            triangles = new ComputeBuffer(triangles.Length, sizeof(int)),
            facing = new ComputeBuffer(Mathf.FloorToInt((float)triangles.Length / 3.0f), sizeof(int)),
            projection = new ComputeBuffer(vertices.Length, sizeof(float) * 3),
            edges = new ComputeBuffer(edgeTriangleIndices.Length, sizeof(int) * 2),
            silhouette = new ComputeBuffer(edgeTriangleIndices.Length, sizeof(int)),
        };

        buffers.vertices.SetData(vertices);
        buffers.triangles.SetData(triangles);
        buffers.edges.SetData(edgeTriangleIndices);
    }

    public static Vector3Int CalculateThreadGroupSize(int size, int maxThreads)
    {
        var threadsX = (int)Mathf.Min(size, maxThreads);
        var threadsY = (int)Mathf.Min(Mathf.CeilToInt(size / (float)maxThreads), maxThreads);
        var threadsZ = Mathf.CeilToInt(size / (float)(maxThreads * maxThreads));

        return new Vector3Int(threadsX, threadsY, threadsZ);
    }

    class Buffers
    {
        public ComputeBuffer vertices;
        public ComputeBuffer triangles;
        public ComputeBuffer transformed;
        public ComputeBuffer facing;
        public ComputeBuffer projection;
        public ComputeBuffer silhouette;
        public ComputeBuffer edges;

        public void Dispose()
        {
            vertices.Dispose();
            triangles.Dispose();
            transformed.Dispose();
            facing.Dispose();
            projection.Dispose();
            silhouette.Dispose();
            edges.Dispose();
        }
    }

    private void OnApplicationQuit()
    {
        buffers.Dispose();
    }

#if UNITY_EDITOR
    [InitializeOnLoadMethod]
    static void Init()
    {
        EditorApplication.playModeStateChanged += (state) =>
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                instance.buffers.Dispose();
            }
        };
    }
#endif
}

