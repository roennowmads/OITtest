using UnityEngine;
using System.Collections;
using UnityEngine.Rendering;
using System.Collections.Generic;
using System;
using System.Runtime.InteropServices;

//[ExecuteInEditMode]
[RequireComponent(typeof(Camera))]
public class WeightedBlendedManager : MonoBehaviour {

    public enum TransparentMode { ODT = 0, Blended, BlendedAndWeighted }
    public enum WeightFunction { Weight0 = 0,  Weight1, Weight2 }

    #region Public params
    public Shader accumulateShader = null;
    public Shader revealageShader = null;
    public Shader blendShader = null;
    public TransparentMode transparentMode = TransparentMode.ODT;
    public WeightFunction weightFunction = WeightFunction.Weight0;

    public RenderTexture renderTex;
    #endregion

    #region Private params
    private Camera m_camera = null;
    private Camera m_transparentCamera = null;
    private GameObject m_transparentCameraObj = null;
    private RenderTexture m_opaqueTex = null;
    private RenderTexture m_accumTex = null;
    private RenderTexture m_revealageTex = null;
    private Material m_blendMat = null;
    #endregion

    public Mesh m_mesh;
    public GameObject m_object;
    private Renderer m_renderer;
    public Material m_material;

    public string m_valueDataPath = "OilRigData";
    public int m_lastFrameIndex = 25;
    private int m_lookupTextureSize = 256;
    private int m_pointsCount;

    private float m_maxDistance;
    private Vector3 m_pointCloudCenter;

    private ComputeBuffer m_pointsBuffer;
    private List<ComputeBuffer> m_indexComputeBuffers;

    private int numberOfRadixSortPasses = 4;
    private int m_bitsPerPass = 2;
    private int m_passLengthMultiplier;
    private int m_elemsPerThread = 4;

    private ComputeShader m_myRadixSort;
    private int LocalPrefixSum;
    private int GlobalPrefixSum;
    private int RadixReorder;
    private int m_threadGroupSize;

    private List<int> inputSizes = new List<int>();
    private List<int> actualNumberOfThreadGroupsList = new List<int>();
    private List<ComputeBuffer> bucketsList = new List<ComputeBuffer>();
    private List<ComputeBuffer> depthsAndValueScansList = new List<ComputeBuffer>();
    private List<ComputeBuffer[]> inOutBufferList = new List<ComputeBuffer[]>();

    private float m_currentTime = 0;
    private float m_currentTimeFrames = 0;
    private int m_frameIndex = 0;
    public float m_frameSpeed = 0.125f;

    private float m_updateFrequency = 1.0f;
    private string m_fpsText;
    private int m_currentFPS;
    private int m_framesSinceUpdate;
    private float m_accumulation;

    private CommandBuffer commandBuffer;

    /*Texture2D createColorLookupTexture() {
        int numberOfValues = m_lookupTextureSize;

        Texture2D lookupTexture = new Texture2D(m_lookupTextureSize, 1, TextureFormat.RGB24, false, false);
        lookupTexture.filterMode = FilterMode.Point;
        lookupTexture.anisoLevel = 1;

        for (int i = 0; i < numberOfValues; i++) {
            float textureIndex = i;

            //0 - 255 --> 0.0 - 1.0
            float value = textureIndex / numberOfValues;

            var a = (1.0f - value) / 0.25f; //invert and group
            float X = Mathf.Floor(a);   //this is the integer part
            float Y = a - X; //fractional part from 0 to 255

            Color color;

            switch ((int)X) {
                case 0:
                    color = new Color(1.0f, Y, 0);
                    break;
                case 1:
                    color = new Color((1.0f - Y), 1.0f, 0);
                    break;
                case 2:
                    color = new Color(0, 1.0f, Y);
                    break;
                case 3:
                    color = new Color(0, (1.0f - Y), 1.0f);
                    break;
                case 4:
                    color = new Color(0, 0, 1.0f);
                    break;
                default:
                    color = new Color(1.0f, 0, 0);
                    break;
            }            
            lookupTexture.SetPixel(i, 0, color); 

            //alternatives: (necessary if I want to store only one component per pixel)
            //tex.LoadRawTextureData()
            //tex.SetPixels(x, y, width, height, colors.ToArray()); 
            //pixels are stored in rectangle blocks... maybe it would actually be better for caching anyway? problem is a frame's colors would need to fit in a rectangle.
        }

        lookupTexture.Apply();

        return lookupTexture;
    }

    void readIndicesAndValues(List<ComputeBuffer> computeBuffers, int threadGroupSize)
    {
        //byte[] vals = new byte[m_textureSize];
        for (int k = 0; k < m_lastFrameIndex; k++)
        //int k = 2;
        {
            //TextAsset ta = Resources.Load("AtriumData/binaryDataFull/frame" + k + "0.0", typeof(TextAsset)) as TextAsset; //LoadAsync
            TextAsset ta = Resources.Load(m_valueDataPath + "/frame" + k + "0.0", typeof(TextAsset)) as TextAsset; //LoadAsync
            byte[] bytes = ta.bytes;  

            int bufferSize = bytes.Length / 4;

            int leftoverThreadGroupSpace = Mathf.CeilToInt((float)bufferSize / threadGroupSize) * threadGroupSize - bufferSize;          
            bufferSize +=  leftoverThreadGroupSpace;
                                                                        
            uint[] zeroedBytes = new uint[bufferSize];

            Buffer.BlockCopy(bytes, 0, zeroedBytes, 0, bytes.Length);      
            
            ComputeBuffer indexComputeBuffer = new ComputeBuffer(bufferSize, Marshal.SizeOf(typeof(uint)), ComputeBufferType.Default);
            indexComputeBuffer.SetData(zeroedBytes);

            computeBuffers.Add(indexComputeBuffer);      
        }  
    }
    
    float[] readPointsFile3Attribs()
    {
        TextAsset pointData = Resources.Load(m_valueDataPath + "/frame00.0.pos", typeof(TextAsset)) as TextAsset;
        byte[] bytes = pointData.bytes;
        float[] points = new float[(bytes.Length / 4)];
        Buffer.BlockCopy(bytes, 0, points, 0, bytes.Length);
        return points;
    }

    private Vector3 getPointCloudCenter(float[] points) {
        float maxX = float.NegativeInfinity;
        float minX = float.PositiveInfinity;
        float maxY = float.NegativeInfinity;
        float minY = float.PositiveInfinity;
        float maxZ = float.NegativeInfinity;
        float minZ = float.PositiveInfinity;

        for (int i = 0; i < points.Length; i+=3) {  
            Vector3 p = new Vector3(points[i], points[i + 1], points[i + 2]);
            if (p.x > maxX) {
                maxX = p.x;
            }
            else if (p.x < minX) {
                minX = p.x;
            }

            if (p.y > maxY) {
                maxY = p.y;
            }
            else if (p.y < minY) {
                minY = p.y;
            }

            if (p.z > maxZ) {
                maxZ = p.z;
            }
            else if (p.z < minZ) {
                minZ = p.z;
            }
        }
        return new Vector3(minX + ((maxX - minX) / 2.0f), minY + ((maxY - minY) / 2.0f), minZ + ((maxZ - minZ) / 2.0f));
    }

    private float getMaxDistance(float[] points, Vector3 center) {
        float maxDistance = 0;
        for (int i = 0; i < points.Length; i+=3) {  
            Vector3 p = new Vector3(points[i], points[i + 1], points[i + 2]);
            float distance = Vector3.Distance(center, p);
            if (distance > maxDistance) {
                maxDistance = distance;
            }
        }
        return maxDistance + maxDistance*0.01f;
    }

    void Awake() {
        Screen.SetResolution(1920, 1080, true);
        float[] points = readPointsFile3Attribs();
        m_pointsCount = points.Length / 3;

        Texture2D colorTexture = createColorLookupTexture();

        m_renderer = m_object.GetComponent<Renderer>();

        m_renderer.sharedMaterial.SetTexture("_ColorTex", colorTexture);

        m_pointCloudCenter = getPointCloudCenter(points);
        m_maxDistance = getMaxDistance(points, m_pointCloudCenter);
        
        m_pointsBuffer = new ComputeBuffer (m_pointsCount, Marshal.SizeOf(typeof(Vector3)), ComputeBufferType.Default);
        m_pointsBuffer.SetData(points);              
        m_renderer.sharedMaterial.SetBuffer("_Points", m_pointsBuffer);

        m_renderer.sharedMaterial.SetInt("_PointsCount", m_pointsCount);
        float aspect = Camera.main.GetComponent<Camera>().aspect;
        m_renderer.sharedMaterial.SetFloat("aspect", aspect);
        

        m_passLengthMultiplier = m_bitsPerPass * m_bitsPerPass;
        if (m_bitsPerPass == 4) {
            m_myRadixSort = (ComputeShader)Resources.Load("MyRadixSort/localSort", typeof(ComputeShader));
        }
        else if (m_bitsPerPass == 2) {
            if (m_elemsPerThread == 4) {
                m_myRadixSort = (ComputeShader)Resources.Load("MyRadixSort/localSort2bits4PerThread", typeof(ComputeShader));
            }
            else {
                m_myRadixSort = (ComputeShader)Resources.Load("MyRadixSort/localSort2bits", typeof(ComputeShader));
            }
        }
        else {                                                                                                       
            m_myRadixSort = (ComputeShader)Resources.Load("MyRadixSort/localSort1bit", typeof(ComputeShader));
        }
        LocalPrefixSum = m_myRadixSort.FindKernel("LocalPrefixSum");
        GlobalPrefixSum = m_myRadixSort.FindKernel("GlobalPrefixSum");
        RadixReorder = m_myRadixSort.FindKernel("RadixReorder");

        uint x, y, z;
        m_myRadixSort.GetKernelThreadGroupSizes(LocalPrefixSum, out x, out y, out z);
        m_threadGroupSize = (int)x;

        m_indexComputeBuffers = new List<ComputeBuffer>();
        readIndicesAndValues(m_indexComputeBuffers, m_threadGroupSize); //make index buffer size depend on threadgroupsize


        foreach (var buf in m_indexComputeBuffers) {
            int inputSize = buf.count;
            int actualNumberOfThreadGroups = inputSize / m_threadGroupSize;

            inputSizes.Add(inputSize);
            actualNumberOfThreadGroupsList.Add(actualNumberOfThreadGroups);

            ComputeBuffer[] inOutBuffers = new ComputeBuffer[2];
            inOutBuffers[0] = buf;
            inOutBuffers[1] = new ComputeBuffer(inputSize, Marshal.SizeOf(typeof(uint))*2, ComputeBufferType.Default);
                                  
            inOutBufferList.Add(inOutBuffers);
            bucketsList.Add(new ComputeBuffer(actualNumberOfThreadGroups, Marshal.SizeOf(typeof(Vector2)) * m_passLengthMultiplier, ComputeBufferType.Default));
            depthsAndValueScansList.Add(new ComputeBuffer(inputSize, Marshal.SizeOf(typeof(uint)) * 2, ComputeBufferType.Default));
        }

        ComputeBuffer computeBufferDigitPrefixSum = new ComputeBuffer(1, Marshal.SizeOf(typeof(Vector2))*m_passLengthMultiplier, ComputeBufferType.Default);
        ComputeBuffer computeBufferGlobalPrefixSum = new ComputeBuffer(m_threadGroupSize, Marshal.SizeOf(typeof(Vector2))*m_passLengthMultiplier, ComputeBufferType.Default);

        m_myRadixSort.SetFloat("depthIndices", Mathf.Pow(2.0f, (float)m_bitsPerPass*numberOfRadixSortPasses));

        m_myRadixSort.SetBuffer(LocalPrefixSum, "_Points", m_pointsBuffer);

        m_myRadixSort.SetBuffer(GlobalPrefixSum, "GlobalDigitPrefixSumOut", computeBufferDigitPrefixSum);
        m_myRadixSort.SetBuffer(GlobalPrefixSum, "GlobalPrefixSumOut", computeBufferGlobalPrefixSum);

        m_myRadixSort.SetBuffer(RadixReorder, "GlobalDigitPrefixSumIn", computeBufferDigitPrefixSum);
        m_myRadixSort.SetBuffer(RadixReorder, "GlobalPrefixSumIn", computeBufferGlobalPrefixSum);  

        commandBuffer = new CommandBuffer();
        //commandBuffer.DrawMesh(m_mesh, m_renderer.transform.localToWorldMatrix, m_material, 0);

        commandBuffer.DrawProcedural(m_object.transform.localToWorldMatrix, m_material, 0, MeshTopology.Triangles, m_indexComputeBuffers[m_frameIndex].count * 6);
                                                                                                                                                
    } 

    void Update () {
        m_currentTime += Time.deltaTime;
        m_currentTimeFrames += Time.deltaTime;
        ++m_framesSinceUpdate;
        m_accumulation += Time.timeScale / Time.deltaTime;

        if (m_currentTimeFrames >= m_frameSpeed) {
            //m_frameIndex = (m_frameIndex + 1) % m_lastFrameIndex;
            m_currentTimeFrames = 0;
        }

        if (m_currentTime >= m_updateFrequency)
        {
            
            m_currentFPS = (int)(m_accumulation / m_framesSinceUpdate);
            m_currentTime = 0.0f;
            m_framesSinceUpdate = 0;
            m_accumulation = 0.0f;
            m_fpsText = "FPS: " + m_currentFPS;
        }

        //Debug.Log(t);
        m_renderer.sharedMaterial.SetInt("_FrameTime", m_frameIndex);
        float aspect = Camera.main.GetComponent<Camera>().aspect;
        m_renderer.sharedMaterial.SetFloat("aspect", aspect);
    }*/

	// Use this for initialization
	void Awake () {
        m_camera = GetComponent<Camera>();
        if (m_transparentCameraObj != null) {
            DestroyImmediate(m_transparentCameraObj);
        }
        m_transparentCameraObj = new GameObject("OITCamera");
        m_transparentCameraObj.hideFlags = HideFlags.DontSave;
        m_transparentCameraObj.transform.parent = transform;
        m_transparentCameraObj.transform.localPosition = Vector3.zero;
        m_transparentCamera = m_transparentCameraObj.AddComponent<Camera>();
        m_transparentCamera.CopyFrom(m_camera);
        m_transparentCamera.clearFlags = CameraClearFlags.Nothing;
        m_transparentCamera.enabled = false; 

        m_blendMat = new Material(blendShader);
        m_blendMat.hideFlags = HideFlags.DontSave;    
	}

    void OnDestroy() {
        DestroyImmediate(m_transparentCameraObj);
    }

    void OnPreRender() {
        if (transparentMode == TransparentMode.ODT) {
            // Just render everything as normal
            m_camera.cullingMask = -1;
        } else {
            // The main camera shouldn't render anything
            // Everything is rendered in procedural
            m_camera.cullingMask = 0;
        }     
    }

    /*private void OnRenderObject() {
        m_myRadixSort.SetVector("camPos", Camera.main.transform.forward);     //camera view direction DOT point position == distance to camera.

        Matrix4x4 transMatrix = m_renderer.localToWorldMatrix;
        m_myRadixSort.SetFloats("model", transMatrix[0], transMatrix[1], transMatrix[2], transMatrix[3],
                                  transMatrix[4], transMatrix[5], transMatrix[6], transMatrix[7],
                                  transMatrix[8], transMatrix[9], transMatrix[10], transMatrix[11],
                                  transMatrix[12], transMatrix[13], transMatrix[14], transMatrix[15]);

        Vector3 zero = -m_pointCloudCenter;
        Vector3 transZero = transMatrix.MultiplyPoint(zero);

        float globalScale = transform.lossyScale.x;
        float scaledMaxDistance = m_maxDistance * globalScale;

        m_myRadixSort.SetVector("objectWorldPos", transZero);
        m_myRadixSort.SetFloat("scaledMaxDistance", scaledMaxDistance);
       
        m_myRadixSort.SetBuffer(LocalPrefixSum, "BucketsOut", bucketsList[m_frameIndex]);
        m_myRadixSort.SetBuffer(LocalPrefixSum, "DepthValueScanOut", depthsAndValueScansList[m_frameIndex]);

        m_myRadixSort.SetBuffer(GlobalPrefixSum, "BucketsIn", bucketsList[m_frameIndex]);
        m_myRadixSort.SetBuffer(RadixReorder, "DepthValueScanIn", depthsAndValueScansList[m_frameIndex]);

        m_renderer.material.SetBuffer("_IndicesValues", inOutBufferList[m_frameIndex][0]);

        int outSwapIndex = 1;
        for (int i = 0; i < numberOfRadixSortPasses; i++) {
            int bitshift = m_bitsPerPass * i;
            m_myRadixSort.SetInt("bitshift", bitshift);
            int swapIndex0 = i % 2;
            outSwapIndex = (i + 1) % 2;

            m_myRadixSort.SetBuffer(LocalPrefixSum, "KeysIn", inOutBufferList[m_frameIndex][swapIndex0]);
            m_myRadixSort.SetBuffer(RadixReorder, "KeysIn", inOutBufferList[m_frameIndex][swapIndex0]);
            m_myRadixSort.SetBuffer(RadixReorder, "KeysOut", inOutBufferList[m_frameIndex][outSwapIndex]);

            m_myRadixSort.Dispatch(LocalPrefixSum, actualNumberOfThreadGroupsList[m_frameIndex] / m_elemsPerThread, 1, 1);
            m_myRadixSort.Dispatch(GlobalPrefixSum, 1, 1, 1);
            m_myRadixSort.Dispatch(RadixReorder, actualNumberOfThreadGroupsList[m_frameIndex] / m_elemsPerThread, 1, 1);
        }                                                 

        m_renderer.sharedMaterial.SetPass(0);
        m_renderer.sharedMaterial.SetMatrix("model", m_object.transform.localToWorldMatrix);

        GL.MultMatrix(m_object.transform.localToWorldMatrix);            
                                                                             
        //Graphics.DrawProcedural(MeshTopology.Triangles, m_indexComputeBuffers[m_frameIndex].count*6);  // index buffer.         
        //Graphics.DrawProcedural(MeshTopology.Points, m_indexComputeBuffers[m_frameIndex].count);  // index buffer.         

        //Graphics.DrawMesh(m_mesh, m_renderer.transform.localToWorldMatrix, m_material, 0);
        Graphics.ExecuteCommandBuffer(commandBuffer);
    }*/

    void OnRenderImage(RenderTexture src, RenderTexture dst) {
        if (transparentMode == TransparentMode.ODT) {
            Graphics.Blit(src, dst);
        } else {
            switch (transparentMode) {
                case TransparentMode.Blended:
                    Shader.DisableKeyword("_WEIGHTED_ON");
                break;
                case TransparentMode.BlendedAndWeighted:
                    Shader.EnableKeyword("_WEIGHTED_ON");
                break;
            }
            switch (weightFunction) {
                case WeightFunction.Weight0:
                    Shader.EnableKeyword("_WEIGHTED0");
                    Shader.DisableKeyword("_WEIGHTED1");
                    Shader.DisableKeyword("_WEIGHTED2");
                break;
                case WeightFunction.Weight1:
                    Shader.EnableKeyword("_WEIGHTED1");
                    Shader.DisableKeyword("_WEIGHTED0");
                    Shader.DisableKeyword("_WEIGHTED2");
                break;
                case WeightFunction.Weight2:
                    Shader.EnableKeyword("_WEIGHTED2");
                    Shader.DisableKeyword("_WEIGHTED0");
                    Shader.DisableKeyword("_WEIGHTED1");
                break;
            }

            m_opaqueTex = RenderTexture.GetTemporary(Screen.width, Screen.height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            m_accumTex = RenderTexture.GetTemporary(Screen.width, Screen.height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            m_revealageTex = RenderTexture.GetTemporary(Screen.width, Screen.height, 0, RenderTextureFormat.RHalf, RenderTextureReadWrite.Linear);

            //Graphics.Blit(src, dst);

            // First render all opaque objects
            m_transparentCamera.targetTexture = m_opaqueTex;
            m_transparentCamera.backgroundColor = m_camera.backgroundColor;
            m_transparentCamera.clearFlags = m_camera.clearFlags;
            m_transparentCamera.cullingMask = ~(1 << LayerMask.NameToLayer("Transparent"));
            m_transparentCamera.Render();

            

            // Clear accumTexture to float4(0)
            m_transparentCamera.targetTexture = m_accumTex;
            m_transparentCamera.backgroundColor = new Color(0.0f, 0.0f, 0.0f, 0.0f);
            m_transparentCamera.clearFlags = CameraClearFlags.SolidColor;
            m_transparentCamera.cullingMask = 0;
            m_transparentCamera.Render();
            // Render accumTexture
            m_transparentCamera.SetTargetBuffers(m_accumTex.colorBuffer, m_opaqueTex.depthBuffer);
            m_transparentCamera.clearFlags = CameraClearFlags.Nothing;
            m_transparentCamera.cullingMask = 1 << LayerMask.NameToLayer("Transparent");
            m_transparentCamera.RenderWithShader(accumulateShader, null);

            // Clear revealageTex to float4(1)
            m_transparentCamera.targetTexture = m_revealageTex;
            m_transparentCamera.backgroundColor = new Color(1.0f, 1.0f, 1.0f, 1.0f);
            m_transparentCamera.clearFlags = CameraClearFlags.SolidColor;
            m_transparentCamera.cullingMask = 0;
            m_transparentCamera.Render();
            // Render revealageTex
            m_transparentCamera.SetTargetBuffers(m_revealageTex.colorBuffer, m_opaqueTex.depthBuffer);
            m_transparentCamera.clearFlags = CameraClearFlags.Nothing;
            m_transparentCamera.cullingMask = 1 << LayerMask.NameToLayer("Transparent");
            m_transparentCamera.RenderWithShader(revealageShader, null);

            m_blendMat.SetTexture("_AccumTex", m_accumTex);
            m_blendMat.SetTexture("_RevealageTex", m_revealageTex);

            Graphics.Blit(src, dst, m_blendMat);     

            //Graphics.Blit(m_opaqueTex, renderTex, m_blendMat);

            RenderTexture.ReleaseTemporary(m_opaqueTex);
            RenderTexture.ReleaseTemporary(m_accumTex);
            RenderTexture.ReleaseTemporary(m_revealageTex); 
        }
    }             
           
}    

