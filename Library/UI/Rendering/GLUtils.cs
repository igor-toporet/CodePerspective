﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenTK.Graphics.OpenGL;
using System.Runtime.InteropServices;
using OpenTK;
using System.Drawing;

namespace XLibrary
{
    public static class GLUtils
    {
        public static void SafeBegin(BeginMode mode, Action code)
        {
            GL.Begin(mode);

            code();

            GL.End();
        }

        public static void SafeEnable(EnableCap cap, Action code)
        {
            GL.Enable(cap);

            code();

            GL.Disable(cap);
        }

        public static void SafeDisable(EnableCap cap, Action code)
        {
            GL.Disable(cap);

            code();

            GL.Enable(cap);
        }

        public static void SafeBlend(Action code)
        {
            GL.BlendFunc(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha);
            GL.Enable(EnableCap.Blend);

            code();

            GL.Disable(EnableCap.Blend);
        }
    }

    public class Vbo
    {
        public int VboID;
        public int EboID;
        public int NumElements;

        int VertexCount = 0;
        VertexPositionColor[] Vertices = new VertexPositionColor[1000];

        int ElementCount = 0;
        int[] Elements = new int[1000];


        public void Reset()
        {
            VertexCount = 0;
            ElementCount = 0;
        }

        public void Load()
        {
            int size;

            // To create a VBO:
            // 1) Generate the buffer handles for the vertex and element buffers.
            // 2) Bind the vertex buffer handle and upload your vertex data. Check that the buffer was uploaded correctly.
            // 3) Bind the element buffer handle and upload your element data. Check that the buffer was uploaded correctly.

            GL.BindBuffer(BufferTarget.ArrayBuffer, VboID);
            GL.BufferData(BufferTarget.ArrayBuffer, (IntPtr)(VertexCount * BlittableValueType.StrideOf(Vertices)), Vertices, BufferUsageHint.StaticDraw);
            GL.GetBufferParameter(BufferTarget.ArrayBuffer, BufferParameterName.BufferSize, out size);
            if (VertexCount * BlittableValueType.StrideOf(Vertices) != size)
                throw new ApplicationException("Vertex data not uploaded correctly");


            GL.BindBuffer(BufferTarget.ElementArrayBuffer, EboID);
            GL.BufferData(BufferTarget.ElementArrayBuffer, (IntPtr)(ElementCount * sizeof(int)), Elements, BufferUsageHint.StaticDraw);
            GL.GetBufferParameter(BufferTarget.ElementArrayBuffer, BufferParameterName.BufferSize, out size);
            if (ElementCount * sizeof(int) != size)
                throw new ApplicationException("Element data not uploaded correctly");


            NumElements = ElementCount;
        }

        public void Draw()
        {
            // To draw a VBO:
            // 1) Ensure that the VertexArray client state is enabled.
            // 2) Bind the vertex and element buffer handles.
            // 3) Set up the data pointers (vertex, normal, color) according to your vertex format.
            // 4) Call DrawElements. (Note: the last parameter is an offset into the element buffer
            //    and will usually be IntPtr.Zero).

            GL.EnableClientState(ArrayCap.ColorArray);
            GL.EnableClientState(ArrayCap.VertexArray);
            GL.EnableClientState(ArrayCap.NormalArray);

            GL.BindBuffer(BufferTarget.ArrayBuffer, VboID);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, EboID);

            GL.VertexPointer(3, VertexPointerType.Float, BlittableValueType.StrideOf(Vertices), new IntPtr(0));
            GL.ColorPointer(4, ColorPointerType.UnsignedByte, BlittableValueType.StrideOf(Vertices), new IntPtr(12));
            GL.NormalPointer(NormalPointerType.Float, BlittableValueType.StrideOf(Vertices), new IntPtr(16));

            GL.DrawElements(BeginMode.Quads, NumElements, DrawElementsType.UnsignedInt, IntPtr.Zero);
        }

        internal void AddVertex(Vector3 point, Color color, Vector3 normal)
        {
            if (VertexCount + 1 >= Vertices.Length)
            {
                var newArray = new VertexPositionColor[Vertices.Length * 2];
                Array.Copy(Vertices, newArray, VertexCount);
                Vertices = newArray;
            }

            if (ElementCount + 1 >= Elements.Length)
            {
                var newArray = new int[Elements.Length * 2];
                Array.Copy(Elements, newArray, ElementCount);
                Elements = newArray;
            }

            Elements[ElementCount++] = VertexCount;

            Vertices[VertexCount++].Set(point, color, normal);
        }
    }


    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct VertexPositionColor
    {
        public Vector3 Position;
        public uint Color;
        public Vector3 Normal;

        public void Set(Vector3 pos, Color color, Vector3 normal)
        {
            Position = pos;
            Color = ToRgba(color);
            Normal = normal;
        }

        public void Set(float x, float y, float z, Color color)
        {
            Position.X = x;
            Position.Y = y;
            Position.Z = z;

            Color = ToRgba(color);
        }

        public void Set(float x, float y, float z, Color color, float nX, float nY, float nZ)
        {
            Set(x, y, z, color);

            Normal.X = nX;
            Normal.Y = nY;
            Normal.Z = nZ;
        }

        static uint ToRgba(Color color)
        {
            return (uint)color.A << 24 | (uint)color.B << 16 | (uint)color.G << 8 | (uint)color.R;
        }
    }
}