﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using System.Diagnostics;



// WINDOWS CONSOLE RAY MARCHING DEMO
// BY KUBA MARUSZCZYK

namespace ConsoleRenderer
{

    struct Ray
    {
        public Vector3 Direction;
        public Vector3 Orgin;
    }

    class RenderObject
    {
        public Material RenderMaterial;
        public float CalculatedDistance;



        public RenderObject(Material renderMaterial)
        {

            RenderMaterial = renderMaterial;
        }

        virtual public float CalculateDistance(Vector3 hitPoint, float deltaTime)
        {
            CalculatedDistance = RayMarcher.MAX_DIST;
            return CalculatedDistance;
        }
    }

    partial class RayMarcher
    {


        const int MAX_MARCHING_STEPS = 200;
        public const float MIN_DIST = 1.0f;
        public const float MAX_DIST = 100.0f;
        const float EPSILON = 0.01f;
        const float DEG_TO_RAD = 0.017453f;
        const float M_PI = 3.141592653f;


        private const float m_cNearPlane = 1.0f;
        private const float m_cFarPlane = 100.0f;
        private Vector3 m_EyePosition = new Vector3(0, 0, -6.0f);


        private int m_ScrWidth;
        private int m_ScrHeight;

        private float m_AspectRatio;
        private float m_Fov;
        private float m_FovDist;

        static private float m_TotalTime = 0;

        static public float TotalTime
        {
            get
            { return m_TotalTime; }
        }

        public List<RenderObject> m_RenderableObjects;


        public RayMarcher(int width, int height)
        {
            m_ScrWidth = width;
            m_ScrHeight = height;
            Console.SetWindowSize(width + 10, height + 4);
            m_AspectRatio = (float)m_ScrWidth / (float)m_ScrHeight;
            m_Fov = 80.0f * DEG_TO_RAD;
            m_FovDist = (float)Math.Tan(m_Fov * 0.5f);

            m_RenderableObjects = new List<RenderObject>();
        }

        public void RenderLoop()
        {
            FrameTimer.Update();
            Console.CursorVisible = false;
            Console.Clear();
            Buffer.Initialize((short)m_ScrWidth, (short)m_ScrHeight);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            string title = " WINDOWS CONSOLE RAYMARCHING DEMO";
            
            Console.Write(title);

            while (true)//Render Loop
            {
                Console.SetCursorPosition(5, 1);
                FrameTimer.Update();
                Console.ForegroundColor = ConsoleColor.Red;
                Console.BackgroundColor = ConsoleColor.Gray;
                string fpsStr = " FPS: " + FrameTimer.GetFPS() + "   FRAME TIME: " + FrameTimer.GetDeltaTime() + "s ";
                Console.Write(fpsStr);
                Console.Title = fpsStr;

                var resetEvent = new ManualResetEvent(false); 

                //For each scanline..
                for (int y = 0; y < m_ScrHeight; ++y)
                {
                    // Queue new task
                    ThreadPool.QueueUserWorkItem(
                       new WaitCallback(
                     delegate (object state)
                    {
                        object[] array = state as object[];
                        int scanLine = Convert.ToInt32(array[0]);

                        for (int x = 0; x < m_ScrWidth; ++x)
                        {
                            Draw(x, scanLine);// Write to buffer
                        }

                        if (scanLine >= m_ScrHeight - 1) resetEvent.Set();
                    }), new object[] { y });
                }

                resetEvent.WaitOne();
                m_TotalTime +=  FrameTimer.GetDeltaTime();
                Buffer.Swap();
            }
        }

        private void Draw(int x, int y)
        {
            float pixelX = x - m_ScrWidth / 2;
            float pixelY = -(y - m_ScrHeight / 2);

            float px = pixelX / ((float)m_ScrWidth) * 0.5f;
            float py = pixelY / ((float)m_ScrHeight) * 0.5f;
            px *= m_FovDist; 
            py *= m_FovDist;
            Ray ray;
            ray.Orgin = m_EyePosition;
            ray.Direction = Vector3.Normalize(new Vector3(px, py, 1.0f));
            int hitIndex;
            float shortestDistance = RayMarch(ref ray, MIN_DIST, MAX_DIST, out hitIndex);
            //Check hit
            if (shortestDistance > (MAX_DIST - EPSILON)) // NO HIT
            {
                Buffer.AddAsync((char)219, 0x0001 | 0x0000, x, y);
            }
            else //HIT
            {
                Vector3 hitPoint = ray.Orgin + ray.Direction * shortestDistance;

                Material currentMat = m_RenderableObjects[hitIndex].RenderMaterial;

                if (currentMat.IsLit()) // If material requires lighting
                {
                    short colA = ((LitMaterial)currentMat).ColorA;
                    short colB = ((LitMaterial)currentMat).ColorB;

                    Vector3 normal = EstimateNormalRM(hitPoint);
                    float lIntensity = CalculateLight((new Vector3(00, 1, -3) - hitPoint).Normalized(), normal);
                    lIntensity = Helper.Min(lIntensity, 1.0f);
                    char c = (char)0;
                    short bm = 0;
                    ProduceShadedColor(out c, out bm, lIntensity, colA, colB);
                    Buffer.AddAsync(c, bm, x, y);


                }
                else // othewise paint it
                {
                    short colA = ((UnlitMaterial)currentMat).ColorA;
                    short colB = ((UnlitMaterial)currentMat).ColorB;
                    Block bl = ((UnlitMaterial)currentMat).BlockType;
                    Buffer.AddAsync((char)bl, (short)(colA | colB), x, y);
                }

            }


        }


        private float RayMarch(ref Ray ray, float start, float end, out int hitIndex)
        {
            float depth = start;
            hitIndex = 0;
            for (int i = 0; i < MAX_MARCHING_STEPS; ++i)
            {
                float dist = SceneSDF(ray.Orgin + depth * ray.Direction, false, out hitIndex);
                if (dist < EPSILON) // hit 
                {
                    return depth;
                }
                depth += dist;
                if (depth >= end) // overrun
                {
                    return end;
                }
            }
            return end;
        }


        float SceneSDF(Vector3 rayHit, bool normalPass, out int hitIndex)
        {
            hitIndex = 0;
            float nearestHit = MAX_DIST + 10; 
            if (m_RenderableObjects.Count == 0) return nearestHit; 
            rayHit *= Matrix3.CreateRotationY((float)Math.Sin(TotalTime * 0.8) * 0.06f);
            for (int i = 0; i < m_RenderableObjects.Count; ++i)
            {
                float currentDist = m_RenderableObjects[i].CalculateDistance(rayHit, FrameTimer.GetDeltaTime());
                nearestHit = Op_Union(nearestHit, currentDist);

                if (!normalPass)
                {
                    if (nearestHit == currentDist)
                    {
                        hitIndex = i;
                    }
                }
            }

            return nearestHit;
        }



        Vector3 EstimateNormalRM(Vector3 p)
        {
            int dummy;
            return
               new Vector3(
                SceneSDF(new Vector3(p.X + EPSILON, p.Y, p.Z), true, out dummy) - SceneSDF(new Vector3(p.X - EPSILON, p.Y, p.Z), true, out dummy),
                SceneSDF(new Vector3(p.X, p.Y + EPSILON, p.Z), true, out dummy) - SceneSDF(new Vector3(p.X, p.Y - EPSILON, p.Z), true, out dummy),
                SceneSDF(new Vector3(p.X, p.Y, p.Z + EPSILON), true, out dummy) - SceneSDF(new Vector3(p.X, p.Y, p.Z - EPSILON), true, out dummy)
                ).Normalized();
        }


        /* DIFFUSE LIGHTING  */

        float CalculateLight(Vector3 lightDir, Vector3 normal)
        {
            Vector3 nLightDirection = lightDir.Normalized();
            float theta = Vector3.Dot(nLightDirection, normal);
            float totalDiffuse = theta;

            return totalDiffuse;
        }




        /* DISTANCE PRIMITIVES */

        static public float DistanceSphere(Vector3 p, float s)
        {

            float ret = p.Length - s;
            return ret;
        }

        static public float DistanceBox(Vector3 p, Vector3 b)
        {
            Vector3 d = Helper.Abs(p);
            d -= b;
            return Helper.Min(Helper.Max(d.X, Helper.Max(d.Y, d.Z)), 0.0f) + (Helper.Max(d, 0.0f)).Length;
        }

        static public float DistanceTorus(Vector3 p, Vector2 t)
        {
            Vector2 q = new Vector2(p.Xz.Length - t.X, p.Y);
            return q.Length - t.Y;
        }


        /*  OPERATIONS */
        static public float Op_Union(float d1, float d2)
        {
            return Helper.Min(d1, d2);
        }


        /* COLOR INTRNSITY CREATOR FOR LIGHT */
        //TO DO: Replace conditionals with LUT
        void ProduceShadedColor(out char block, out short bitCol, float intensity, short bColor, short fColor)
        {
            block = (char)Block.Solid;
            bitCol = 0;
            if (intensity > 0 && intensity < 0.04)
            {
                block = (char)Block.Weak;
                bitCol = 0;
            }
            if (intensity > 0.04 && intensity < 0.15)
            {
                block = (char)Block.Weak;
                bitCol = fColor;
            }
            else if (intensity > 0.15 && intensity <= 0.2)
            {
                block = (char)Block.Middle;
                bitCol = fColor;
            }

            else if (intensity > 0.2 && intensity <= 0.26)
            {
                block = (char)Block.Strong;
                bitCol = fColor;
            }

            else if (intensity > 0.26 && intensity <= 0.35)
            {
                block = (char)Block.Weak;
                bitCol = (short)(fColor | bColor);
            }

            else if (intensity > 0.35 && intensity <= 0.45)
            {
                block = (char)Block.Weak;
                bitCol = (short)(fColor | bColor | FOREGROUND_INTENSITY);
            }

            else if (intensity > 0.45 && intensity <= 0.55)
            {
                block = (char)Block.Middle;
                bitCol = (short)(fColor | bColor | FOREGROUND_INTENSITY);
            }
            else if (intensity > 0.55 && intensity <= 0.61)
            {
                block = (char)Block.Strong;
                bitCol = (short)(fColor | bColor | FOREGROUND_INTENSITY);
            }


            else if (intensity > 0.61)
            {
                block = (char)Block.Solid;
                bitCol = (short)(fColor | FOREGROUND_INTENSITY);
            }

        }

    }



}
