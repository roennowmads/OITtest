using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using System.Text;
using System.IO;
using UnityEngine.Rendering;
using System.Runtime.InteropServices;
using System;
using System.Linq;

public class PointCloud : MonoBehaviour {
    public string m_valueDataPath = "OilRigData";
    public int m_lastFrameIndex = 25;
    
    public float m_frameSpeed = 0.5f;
    public int m_textureSideSizePower = 14;

    public Texture2D particleTexture;

    private int m_pointsCount = 61440;
    private Renderer pointRenderer;

    private int m_textureSideSize;
    private int m_textureSize;
    private int m_lookupTextureSize = 256; //Since the values in the value texture are only 0-255, it doesn't make sense to have more values here.

    private int m_textureSwitchFrameNumber = -1;

    private ComputeBuffer m_pointsBuffer, /*m_computeBufferIn,*/ m_computeBufferTemp;

    private List<ComputeBuffer> m_indexComputeBuffers;

    private float m_currentTime = 0;
    private int m_frameIndex = 0;

    public ComputeShader m_radixShader;
    private int m_kernel;                

    private ComputeShader m_myRadixSort;
    private int LocalPrefixSum;
    private int GlobalPrefixSum;
    private int RadixReorder;
    private int WarpScanTest;
    private int inputSize;
    private int m_threadGroupSize;

    public int m_FPSLabelOffsetX = 15;
    public int m_FPSLabelOffsetY = 250;

    private float m_updateFrequency = 1.0f;
    private string m_fpsText;
    private int m_currentFPS;
    private int m_framesSinceUpdate;
    private float m_accumulation;

    private const string UI_FONT_SIZE = "<size=30>";
    private const float UI_FPS_LABEL_SIZE_X = 200.0f;
    private const float UI_FPS_LABEL_SIZE_Y = 200.0f;

    //private ComputeBuffer[] m_inOutBuffers;
    //private int m_actualNumberOfThreadGroups;
                                                    
    private int numberOfRadixSortPasses = 4;
    private int m_bitsPerPass = 2;
    private int m_passLengthMultiplier;
    private int m_elemsPerThread = 4;

    private float m_maxDistance;
    private Vector3 m_pointCloudCenter;
    private List<Vector3> m_ppoints;

    private List<int> inputSizes = new List<int>();
    private List<int> actualNumberOfThreadGroupsList = new List<int>();
    private List<ComputeBuffer> bucketsList = new List<ComputeBuffer>();
    private List<ComputeBuffer> depthsAndValueScansList = new List<ComputeBuffer>();
    private List<ComputeBuffer[]> inOutBufferList = new List<ComputeBuffer[]>();

    private CommandBuffer commandBuffer;


    private float m_currentTimeFrames = 0;

    Texture2D createColorLookupTexture() {
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

    IEnumerator readPointsFile1ValueAsync(int k, Texture2D tex2) {
        byte[] vals = new byte[m_textureSize];

        int index = 0;
        for (int i = k - 1; i < m_lastFrameIndex; i++) {

            TextAsset ta = Resources.Load(m_valueDataPath + "/frame" + i + "0.0", typeof(TextAsset)) as TextAsset; //LoadAsync
            byte[] bytes = ta.bytes;

            int frameSize = bytes.Length;

            Buffer.BlockCopy(bytes, 0, vals, index * frameSize, frameSize);
            index++;

        }
        tex2.LoadRawTextureData(vals);
        tex2.Apply();

        pointRenderer = GetComponent<Renderer>();
        pointRenderer.sharedMaterial.SetTexture("_MainTex2", tex2);
        yield return 0;
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

            //int bufferSize = bytes.Length / 4;//4096 * 4 * 4 * 2;

            uint[] zeroedBytes = new uint[bufferSize];

            Buffer.BlockCopy(bytes, 0, zeroedBytes, 0, bytes.Length);

            //try to cut the size to something like 4096
            
            if (bufferSize > 0) {
                ComputeBuffer indexComputeBuffer = new ComputeBuffer(bufferSize, Marshal.SizeOf(typeof(uint)), ComputeBufferType.Default);

                indexComputeBuffer.SetData(zeroedBytes);  

                computeBuffers.Add(indexComputeBuffer);
            }
            //int frameSize = bytes.Length;
            //Buffer.BlockCopy(bytes, 0, vals, k * frameSize, frameSize);
        }  
    } 

    void readPointsFile1Value(Texture2D tex, Texture2D tex2) {
        //CompressionHelper.CompressFile(path + "fireAtrium0." + fileIndex + ".bytes", "fireAtrium0." + fileIndex +".lzf");
        //byte[] bytes = CompressionHelper.DecompressFileToMem(path + "fireAtrium0." + fileIndex +".lzf");
        //float[] vals = new float[bytes.Length / 4];
        //Buffer.BlockCopy(bytes, 0, vals, 0, bytes.Length);

        bool loadingFromBytes = true;
        
        byte[] vals = new byte[m_textureSize];
        //byte[] vals2 = new byte[m_textureSize * m_textureSize];

        //int nextTexIndex = 0;

        if (loadingFromBytes) {
            for (int k = 0; k < m_lastFrameIndex; k++) {

                //http://answers.unity3d.com/questions/759469/why-doesnt-my-asynchronous-loading-work.html
                //StartCoroutine (LoadFrames ());

                //ResourceRequest bla = Resources.LoadAsync("AtriumData/frame" + k + "0.0", typeof(TextAsset));

                //while (!bla.isDone) {
                //    yield return 0;
                //}

                //TextAsset ta = Resources.Load("AtriumData/binaryDataFull/frame" + k + "0.0", typeof(TextAsset)) as TextAsset; //LoadAsync
                TextAsset ta = Resources.Load(m_valueDataPath + "/frame" + k + "0.0", typeof(TextAsset)) as TextAsset; //LoadAsync
                byte[] bytes = ta.bytes;

                int frameSize = bytes.Length;
                //Debug.Log(k * frameSize);
                /*if (k * frameSize < m_textureSize - frameSize) {
                    
                    Buffer.BlockCopy(bytes, 0, vals, k * frameSize, frameSize);
                    //nextTexIndex = k;
                }*/

                if (k * frameSize >= m_textureSize - frameSize) {
                    m_textureSwitchFrameNumber = k - 1;
                    StartCoroutine(readPointsFile1ValueAsync(k, tex2));
                    break;
                }
                Buffer.BlockCopy(bytes, 0, vals, k * frameSize, frameSize);
                


                //else {
                //    Debug.Log(k * frameSize - (nextTexIndex + 1) * frameSize);
                //    Buffer.BlockCopy(bytes, 0, vals2, k * frameSize - (nextTexIndex+1) * frameSize, frameSize);
                //}

                //Debug.Log(nextTexIndex);

                //times[k] = offset;
                //offset += m_pointsCount;
            }

            //CompressionHelper.CompressMemToFile(vals, "Assets/Resources/OilRigData/compressedValues.bytes");
        }
        else {
            /*TextAsset ta = Resources.Load(m_valueDataPath + "/compressedValues", typeof(TextAsset)) as TextAsset;
            vals = CompressionHelper.DecompressBytes(ta.bytes);

            for (int k = 0; k < m_lastFrameIndex; k++) {
                //times[k] = offset;
                //offset += m_pointsCount;
            }*/
        }

        //We will have to use textures for values because compute buffers require an element size of 4 bytes, hence we can't use bytes.
        //It could be done with bit shifting, but then any advantage would wear off.

        tex.LoadRawTextureData(vals);
        tex.Apply();
        //tex2.LoadRawTextureData(vals2);
        //tex2.Apply();


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

    private Vector3 getPointCloudCenter (List<Vector3> points) {
        float maxX = float.NegativeInfinity;
        float minX = float.PositiveInfinity;
        float maxY = float.NegativeInfinity;
        float minY = float.PositiveInfinity;
        float maxZ = float.NegativeInfinity;
        float minZ = float.PositiveInfinity;

        foreach (Vector3 p in points) {
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

    private float getMaxDistance(List<Vector3> points, Vector3 center) {
        float maxDistance = 0;                      
        foreach (Vector3 p in points) {
            float distance = Vector3.Distance(center, p);  
            if (distance > maxDistance) {
                maxDistance = distance;
            }
        }
        return maxDistance + maxDistance*0.01f; //adding a little more to make sure we're not cutting anything off with inprecision.
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

    void Start () {
        m_textureSideSize = 1 << m_textureSideSizePower;
        m_textureSize = m_textureSideSize * m_textureSideSize;

        Screen.SetResolution(1920, 1080, true);

        //Set up mesh:
        float[] points = readPointsFile3Attribs();
        m_pointsCount = points.Length / 3;

         //Set up textures:
        Texture2D colorTexture = createColorLookupTexture();

        //We don't need more precision than the resolution of the colorTexture. 10 bits is sufficient for 1024 different color values.
        //That means we can pack 3 10bit integer values into a pixel 
        //Texture2D texture = new Texture2D(m_textureSize, m_textureSize, TextureFormat.RFloat, false, false);

        //texture.anisoLevel = 1;
        //readPointsFile1Value(texture, texture2);
        pointRenderer = GetComponent<Renderer>();
        //pointRenderer.material.mainTexture = texture;
        //pointRenderer.material.SetTexture("_MainTex2", texture2);
        pointRenderer.sharedMaterial.SetTexture("_ColorTex", colorTexture);
        pointRenderer.sharedMaterial.SetTexture("_AlbedoTex", particleTexture);

        /*List<int> indices = new List<int>();

        int numberOfParticles = 32768;//131072;//65536;

        m_pointsCount = numberOfParticles;

        for (int i = 0; i < numberOfParticles; i++) {
            indices.Add((i << 8) + (i*4 % 64));
        }

        m_ppoints = new List<Vector3>();

        for (int i = 0; i < numberOfParticles; i++) {
            m_ppoints.Add(new Vector3(0.0f - i*0.01f ,0.0f ,0.0f ));
        }   */


        //m_pointCloudCenter = getPointCloudCenter(m_ppoints);
        //m_maxDistance = getMaxDistance(m_ppoints, m_pointCloudCenter);

        m_pointCloudCenter = getPointCloudCenter(points);
        m_maxDistance = getMaxDistance(points, m_pointCloudCenter);
        
        m_pointsBuffer = new ComputeBuffer (m_pointsCount, Marshal.SizeOf(typeof(Vector3)), ComputeBufferType.Default);
        m_pointsBuffer.SetData(points);
        //m_pointsBuffer.SetData(m_ppoints.ToArray());
        pointRenderer.sharedMaterial.SetBuffer("_Points", m_pointsBuffer);

        //m_indexComputeBuffer = new ComputeBuffer (m_pointsCount, Marshal.SizeOf(typeof(int)), ComputeBufferType.Default);
        //m_indexComputeBuffer.SetData(indices.ToArray());

        //pointRenderer.material.SetInt("_PointsCount", /*m_pointsCount*/pointPositions.Length);
        pointRenderer.sharedMaterial.SetInt("_PointsCount", m_pointsCount);
        float aspect = Camera.main.GetComponent<Camera>().aspect;
        pointRenderer.sharedMaterial.SetFloat("aspect", aspect);

        // m_kernel = m_radixShader.FindKernel("CSMain");
        m_passLengthMultiplier = m_bitsPerPass * m_bitsPerPass;
        if (m_bitsPerPass == 4) {
            m_myRadixSort = (ComputeShader)Resources.Load("MyRadixSort/localSort", typeof(ComputeShader));
        }
        else if (m_bitsPerPass == 2) {
            m_passLengthMultiplier = m_bitsPerPass;
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
        readIndicesAndValues(m_indexComputeBuffers, m_threadGroupSize*m_elemsPerThread); //make index buffer size depend on threadgroupsize

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

        uint[] bufOut = new uint[/*m_pointsCount*//*262144*/m_indexComputeBuffers[m_frameIndex].count];

        m_computeBufferTemp = new ComputeBuffer(bufOut.Length, Marshal.SizeOf(typeof(uint)), ComputeBufferType.Default);
        m_computeBufferTemp.SetData(bufOut);

        Debug.Log("Number of points: " + m_pointsCount);

        commandBuffer = new CommandBuffer();
        //commandBuffer.DrawProcedural(pointRenderer.localToWorldMatrix, pointRenderer.sharedMaterial, 1, MeshTopology.Triangles, m_indexComputeBuffers[m_frameIndex].count * 6);
        //commandBuffer.DrawProcedural(pointRenderer.localToWorldMatrix, pointRenderer.sharedMaterial, 0, MeshTopology.Triangles, m_indexComputeBuffers[m_frameIndex].count * 6); 
        commandBuffer.DrawProcedural(pointRenderer.localToWorldMatrix, pointRenderer.sharedMaterial, 1, MeshTopology.Points, m_indexComputeBuffers[m_frameIndex].count);
        commandBuffer.DrawProcedural(pointRenderer.localToWorldMatrix, pointRenderer.sharedMaterial, 0, MeshTopology.Points, m_indexComputeBuffers[m_frameIndex].count);  
    }
	
	// Update is called once per frame
	void Update () {
        m_currentTime += Time.deltaTime;
        m_currentTimeFrames += Time.deltaTime;
        ++m_framesSinceUpdate;
        m_accumulation += Time.timeScale / Time.deltaTime;

        if (m_currentTimeFrames >= m_frameSpeed) {
            m_frameIndex = (m_frameIndex + 1) % (m_lastFrameIndex-1);
            m_currentTimeFrames = 0;

            commandBuffer.Clear();
            //commandBuffer.DrawProcedural(pointRenderer.localToWorldMatrix, pointRenderer.sharedMaterial, 1, MeshTopology.Triangles, m_indexComputeBuffers[m_frameIndex].count * 6);
            //commandBuffer.DrawProcedural(pointRenderer.localToWorldMatrix, pointRenderer.sharedMaterial, 0, MeshTopology.Triangles, m_indexComputeBuffers[m_frameIndex].count * 6); 
            commandBuffer.DrawProcedural(pointRenderer.localToWorldMatrix, pointRenderer.sharedMaterial, 1, MeshTopology.Points, m_indexComputeBuffers[m_frameIndex].count);
            commandBuffer.DrawProcedural(pointRenderer.localToWorldMatrix, pointRenderer.sharedMaterial, 0, MeshTopology.Points, m_indexComputeBuffers[m_frameIndex].count); 
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
        pointRenderer.sharedMaterial.SetInt("_FrameTime", m_frameIndex);
        float aspect = Camera.main.GetComponent<Camera>().aspect;
        pointRenderer.sharedMaterial.SetFloat("aspect", aspect);

    }

    /*private void OnGUI()
    {
        {
            Color oldColor = GUI.color;
            GUI.color = Color.white;

            GUI.Label(new Rect(m_FPSLabelOffsetX, m_FPSLabelOffsetY,
                               UI_FPS_LABEL_SIZE_X,
                               UI_FPS_LABEL_SIZE_Y), UI_FONT_SIZE + m_fpsText + "</size>");
            GUI.color = oldColor;
        }
    }*/

    private void OnRenderObject()
    {
        //GpuSort.BitonicSort32(m_indexComputeBuffers[m_frameIndex], m_computeBufferTemp, m_pointsBuffer, pointRenderer.localToWorldMatrix);


        m_myRadixSort.SetVector("camPos", Camera.main.transform.forward);     //camera view direction DOT point position == distance to camera.


        

        Matrix4x4 transMatrix = pointRenderer.localToWorldMatrix;
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
        
        pointRenderer.sharedMaterial.SetBuffer("_IndicesValues", inOutBufferList[m_frameIndex][0]);

        //pointRenderer.sharedMaterial.SetPass(0);
        //pointRenderer.sharedMaterial.SetMatrix("model", pointRenderer.localToWorldMatrix);

        //GL.MultMatrix(pointRenderer.localToWorldMatrix); 

        //Debug.Log(m_indexComputeBuffers[m_frameIndex].count);
                                                                             
        //Graphics.DrawProcedural(MeshTopology.Triangles, /*m_pointsCount*6*/m_indexComputeBuffers[m_frameIndex].count*6);  // index buffer.         
        //Graphics.DrawProcedural(MeshTopology.Points, /*m_pointsCount*6*/m_indexComputeBuffers[m_frameIndex].count);  // index buffer.         

        //Graphics.DrawProcedural(MeshTopology.Triangles, /*m_pointsCount*6*//*m_indexComputeBuffers[m_frameIndex].count*6*/m_indexComputeBuffer.count*6 );  // index buffer.
        //Graphics.DrawProcedural(MeshTopology.Points, /*m_pointsCount*6*//*m_indexComputeBuffers[m_frameIndex].count*/m_indexComputeBuffer.count);  // index buffer.

        //Maybe this rendering stuff is supposed to be attached to the camera?

        pointRenderer.sharedMaterial.SetPass(0);
        pointRenderer.sharedMaterial.SetMatrix("model", pointRenderer.localToWorldMatrix);

        GL.MultMatrix(pointRenderer.localToWorldMatrix);            
                                                                             
        //Graphics.DrawProcedural(MeshTopology.Triangles, /*m_pointsCount*6*/m_indexComputeBuffers[m_frameIndex].count*6);  // index buffer.         
        //Graphics.DrawProcedural(MeshTopology.Points, /*m_pointsCount*6*/m_indexComputeBuffers[m_frameIndex].count);  // index buffer.         

        //Graphics.DrawMesh(m_mesh, m_renderer.transform.localToWorldMatrix, m_material, 0);
        Graphics.ExecuteCommandBuffer(commandBuffer);
    }

    void OnDestroy() {
        //m_pointsBuffer.Release();

        /*foreach (ComputeBuffer cb in m_indexComputeBuffers)
        {
            cb.Release();
        }  */
       
    }
}
