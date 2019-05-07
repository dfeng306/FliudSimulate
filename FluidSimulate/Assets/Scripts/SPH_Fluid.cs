using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SPH_Fluid : MonoBehaviour
{
    public ComputeShader SPH_CS;

    //属性设置
    public int PARTICLE_NUM = 500;
    public int NEIGHBOUR_NUM = 100;
    public int GRID_MAXNUM = 100;
    public float GRID_WIDTH = 0.1f;
    int THREAD_NUM = 64;
    public Vector3 ContainerSize = new Vector3(10, 10, 10);
    public Vector3 gridPos = new Vector3(-5, 0, -5);
    public float TIME_STEP = 0.05f;
    public static float smoothRadius = 0.2f;
    public float radius = 0.001f;
    [Range(0,1)]
    public float scaleFactor = 0.5f;

    public Vector3 offsetPos = new Vector3(0, 5, 0);
    public Vector3 gravity = new Vector3(0, -0.98f, 0);
    public GameObject particlePrefab;

    //光滑核函数
    static float m_mass = 0.00020543f;
    float m_K = 1.5f;
    float m_Kpoly6 = 315 * m_mass / ((64 * Mathf.PI) * Mathf.Pow(smoothRadius, 9));
    float m_Kspiky = 45.0f / (Mathf.PI * Mathf.Pow(smoothRadius, 6));
    float m_Kviscosity = 0.2f;


    //---------------------------
    //private int computePressureKernel;
    private int computeForceKernel;
    private bool error;
    private bool pause;

    //粒子缓存,邻居粒子缓存,grid缓存
    private Particle[] particles;
    private int[,] grid;
    private int[] gridNum;
    private int[,] neighbours;
    private int[] neighbourNum;

    //compute buffer
    ComputeBuffer particlesBuffer;
    ComputeBuffer gridBuffer;
    ComputeBuffer gridNumBuffer;
    ComputeBuffer neighboursBuffer;
    ComputeBuffer neighboursNumBuffer;


    private GameObject[] particleList;

    struct Particle {
        public Vector3 pos;
        public Vector3 vel;
        public Vector3 velEval;
        public Vector3 acc;
        public float density;
        public float pressure;
        public int gridIndex;
    }

    // Start is called before the first frame update
    void Start()
    {
        pause = true;
        if (SPH_CS == null)
            error = true;
        else {
            //computePressureKernel = SPH_CS.FindKernel("ComputePressure");
            computeForceKernel = SPH_CS.FindKernel("ComputeForce");

            InitGrid();
            InitParticle();
            CreateParticleObj();
            FindNeighbours();
            InitBuffer();
        }
    }

    private void Reset()
    {
        InitGrid();
        InitParticle();
        CreateParticleObj();

    }
    //初始化粒子
    private void InitParticle() {
        particles = new Particle[PARTICLE_NUM];
        int ver_num = Mathf.CeilToInt(PARTICLE_NUM / 100);
        for (int y = 0; y < ver_num; y++)
        {
            for (int z = 0; z < 10; z++)
            {
                for (int x = 0; x < 10; x++)
                {
                    int index = y * 100 + z * 10 + x;
                    particles[index].pos = offsetPos + new Vector3(x * radius, y * radius, z * radius);
                }
            }
        }
    }

    private void CreateParticleObj() {
        particleList = new GameObject[PARTICLE_NUM];
        for (int i = 0; i < PARTICLE_NUM; i++)
        {
            particleList[i] = Instantiate(particlePrefab) as GameObject;
            particleList[i].transform.localScale = Vector3.one * scaleFactor;
            particleList[i].transform.position = particles[i].pos;
        }
    }

    //采用grid优化算法，加速遍历粒子速度
    int gridXNum;
    int gridYNum;
    int gridZNum;
    private void InitGrid()
    {
        gridXNum = Mathf.CeilToInt(ContainerSize.x / GRID_WIDTH);
        gridYNum = Mathf.CeilToInt(ContainerSize.y / GRID_WIDTH);
        gridZNum = Mathf.CeilToInt(ContainerSize.z / GRID_WIDTH);

        int gridNum = gridXNum * gridYNum * gridZNum;
        grid = new int[gridNum, GRID_MAXNUM];
        this.gridNum = new int[gridNum];

        Debug.LogError(gridXNum + "--" + gridYNum + "--" + gridZNum);

        for (int i = 0; i < gridXNum; i++)
        {
            for (int j = 0; j < gridYNum; j++)
            {
                Debug.DrawLine(gridPos + new Vector3(i * GRID_WIDTH, j * GRID_WIDTH, 0), gridPos + new Vector3(i * GRID_WIDTH, j * GRID_WIDTH, 0) + Vector3.forward * GRID_WIDTH * gridZNum, Color.red, 3333f);
            }
            Debug.DrawLine(gridPos + new Vector3(i * GRID_WIDTH, gridYNum * GRID_WIDTH, 0), gridPos + new Vector3(i * GRID_WIDTH, gridYNum * GRID_WIDTH, 0) + Vector3.forward * GRID_WIDTH * gridZNum, Color.red, 3333f);
        }
        for (int i = 0; i < gridXNum; i++)
        {
            for (int j = 0; j < gridZNum; j++)
            {
                Debug.DrawLine(gridPos + new Vector3(i * GRID_WIDTH, 0, j * GRID_WIDTH), gridPos + new Vector3(i * GRID_WIDTH, 0, j * GRID_WIDTH) + Vector3.up * GRID_WIDTH * gridZNum, Color.red, 3333f);
            }
            Debug.DrawLine(gridPos + new Vector3(i * GRID_WIDTH, 0, gridZNum * GRID_WIDTH), gridPos + new Vector3(i * GRID_WIDTH, 0, gridZNum * GRID_WIDTH) + Vector3.up * GRID_WIDTH * gridZNum, Color.red, 3333f);
        }
        for (int i = 0; i < gridYNum; i++)
        {
            for (int j = 0; j < gridZNum; j++)
            {
                Debug.DrawLine(gridPos + new Vector3(0, i * GRID_WIDTH, j * GRID_WIDTH), gridPos + new Vector3(0, i * GRID_WIDTH, j * GRID_WIDTH) + Vector3.right * GRID_WIDTH * gridZNum, Color.red, 3333f);
            }
            Debug.DrawLine(gridPos + new Vector3(0, i * GRID_WIDTH, gridZNum * GRID_WIDTH), gridPos + new Vector3(0, i * GRID_WIDTH, gridZNum * GRID_WIDTH) + Vector3.right * GRID_WIDTH * gridZNum, Color.red, 3333f);
        }
    }

    //寻找邻居粒子
    private void FindNeighbours() {
        for (int i = 0; i < gridXNum * gridYNum * gridZNum; i++)
            gridNum[i] = 0;
        for (int i = 0; i < PARTICLE_NUM; i++) {
            Vector3 dis = particles[i].pos - gridPos;
            int gXIndex = Mathf.CeilToInt(Mathf.Abs(dis.x) / GRID_WIDTH);
            int gYIndex = Mathf.CeilToInt(Mathf.Abs(dis.y) / GRID_WIDTH);
            int gZIndex = Mathf.CeilToInt(Mathf.Abs(dis.z) / GRID_WIDTH);
            if (gXIndex == 0)
                gXIndex = 1;
            if (gYIndex == 0)
                gYIndex = 1;
            if (gZIndex == 0)
                gZIndex = 1;
            int gridIndex = gridXNum * gridZNum * (gYIndex - 1) + gridXNum * (gZIndex - 1) + gXIndex - 1;
            grid[gridIndex, gridNum[gridIndex]] = i;
            gridNum[gridIndex]++;
            particles[i].gridIndex = gridIndex;

            if (gridNum[gridIndex] > GRID_MAXNUM) {
                Debug.LogError("单个grid 粒子过多");
                return;
            }
        }
        neighbours = new int[PARTICLE_NUM, NEIGHBOUR_NUM];
        neighbourNum = new int[PARTICLE_NUM];

        float l = Mathf.Sqrt(GRID_WIDTH * GRID_WIDTH * 3)/2;

        for (int i = 0; i < PARTICLE_NUM; i++)
        {
            for (int ix = 0; ix < gridXNum; ix++)
            {
                for (int iy = 0; iy < gridYNum; iy++)
                {
                    for (int iz = 0; iz < gridZNum; iz++)
                    { 
                        int gridIndex = gridXNum * gridZNum * iy + gridXNum * iz + ix;
                        Vector3 b_Min = new Vector3(ix * GRID_WIDTH, iy * GRID_WIDTH, iz * GRID_WIDTH)+gridPos;
                        Vector3 b_Max = b_Min + Vector3.one * GRID_WIDTH;

                        float dis = Vector3.Distance(particles[i].pos, (b_Min + b_Max) / 2);
                        //Debug.LogError(dis+"--"+l+"--"+smoothRadius);
                        if (dis <= l + smoothRadius)
                        {
                            //Debug.DrawLine(particles[i].pos, (b_Min + b_Max) / 2, Color.black,0.2f);
                            for (int j = 0; j < gridNum[gridIndex]; j++)
                            {
                                if (Vector3.Distance(particles[i].pos, particles[grid[gridIndex, j]].pos) <= smoothRadius)
                                {
                                    neighbours[i, neighbourNum[i]] = grid[gridIndex, j];
                                    neighbourNum[i]++;
                                }
                            }
                        }
                    }
                }
            }

            Debug.LogError("3");
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (!error)
            SPHUpdate();
        
    }

    private void SPHUpdate() {
        if (Input.GetKeyDown(KeyCode.Space))
            pause = !pause;

        //run SPH 
        if (!pause) {
            FindNeighbours();

            //particlesBuffer.Release();
            //gridBuffer.Release();
            //gridNumBuffer.Release();
            //neighboursBuffer.Release();
            //neighboursNumBuffer.Release();

            //particlesBuffer.SetData(particles);
            //gridBuffer.SetData(grid);
            //gridNumBuffer.SetData(gridNum);
            //neighboursBuffer.SetData(neighbours);
            //neighboursNumBuffer.SetData(neighbourNum);
            particlesBuffer.SetData(particles);
            SPH_CS.SetBuffer(computeForceKernel, "particles", particlesBuffer);
            //SPH_CS.SetBuffer(computeForceKernel, "", gridBuffer);
            //SPH_CS.SetBuffer(computeForceKernel, "", gridNumBuffer);
            //SPH_CS.SetBuffer(computeForceKernel, "", neighboursBuffer);
            //SPH_CS.SetBuffer(computeForceKernel, "", neighboursNumBuffer);

            SPH_CS.Dispatch(computeForceKernel,PARTICLE_NUM,1,1);
            particlesBuffer.GetData(particles);
            //particlesBuffer.Release();

            UpdatePos();
        }
    }

    private void InitBuffer() {
        particlesBuffer = new ComputeBuffer(PARTICLE_NUM, 60);
        particlesBuffer.SetData(particles);
        gridBuffer = new ComputeBuffer(gridXNum * gridYNum * gridZNum, 4 * GRID_MAXNUM);
        gridBuffer.SetData(grid);
        gridNumBuffer = new ComputeBuffer(gridXNum * gridYNum * gridZNum, 4);
        gridNumBuffer.SetData(gridNum);
        neighboursBuffer = new ComputeBuffer(PARTICLE_NUM, NEIGHBOUR_NUM * 4);
        neighboursBuffer.SetData(neighbours);
        neighboursNumBuffer = new ComputeBuffer(PARTICLE_NUM, 4);
        neighboursNumBuffer.SetData(neighbourNum);
    }

    private void UpdatePos() {

        for (int i = 0; i < PARTICLE_NUM; i++) {
            particleList[i].transform.position = particles[i].pos;
        }
    }
}
