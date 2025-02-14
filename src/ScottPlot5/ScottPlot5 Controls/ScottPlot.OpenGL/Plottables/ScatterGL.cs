﻿using OpenTK;
using OpenTK.Graphics.OpenGL;
using ScottPlot.Control;
using ScottPlot.DataSources;
using ScottPlot.OpenGL;
using ScottPlot.OpenGL.GLPrograms;
using SkiaSharp;
using System;

namespace ScottPlot.Plottables;

/// <summary>
/// This plot type uses an OpenGL shader for rendering.
/// </summary>
public class ScatterGL : Scatter, IPlottableGL
{
    public IPlotControl PlotControl { get; }
    protected int VertexBufferObject;
    protected int VertexArrayObject;
    protected ILinesDrawProgram? LinesProgram;
    protected IMarkersDrawProgram? MarkerProgram;
    protected double[] Vertices;
    protected readonly int VerticesCount;

    protected bool GLHasBeenInitialized = false;

    public GLFallbackRenderStrategy Fallback { get; set; } = GLFallbackRenderStrategy.Software;

    public ScatterGL(IScatterSource data, IPlotControl control) : base(data)
    {
        PlotControl = control;
        var dataPoints = data.GetScatterPoints();
        Vertices = new double[dataPoints.Count * 2];
        for (int i = 0; i < dataPoints.Count; i++)
        {
            Vertices[i * 2] = dataPoints[i].X;
            Vertices[i * 2 + 1] = dataPoints[i].Y;
        }
        VerticesCount = Vertices.Length / 2;
    }

    protected virtual void InitializeGL()
    {
        LinesProgram = new LinesProgram();
        MarkerProgram = new MarkerFillCircleProgram();

        VertexArrayObject = GL.GenVertexArray();
        VertexBufferObject = GL.GenBuffer();
        GL.BindVertexArray(VertexArrayObject);
        GL.BindBuffer(BufferTarget.ArrayBuffer, VertexBufferObject);
        GL.BufferData(BufferTarget.ArrayBuffer, Vertices.Length * sizeof(double), Vertices, BufferUsageHint.StaticDraw);
        GL.VertexAttribLPointer(0, 2, VertexAttribDoubleType.Double, 0, IntPtr.Zero);
        GL.EnableVertexAttribArray(0);
        Vertices = Array.Empty<double>();
        GLHasBeenInitialized = true;
    }

    protected Matrix4d CalcTransform()
    {
        var xRange = Axes.XAxis.Range;
        var yRange = Axes.YAxis.Range;

        Matrix4d translate = Matrix4d.CreateTranslation(
            x: -1.0 * (xRange.Min + xRange.Max) / 2,
            y: -1.0 * (yRange.Min + yRange.Max) / 2,
            z: 0.0);

        Matrix4d scale = Matrix4d.Scale(
            x: 2.0 / (xRange.Max - xRange.Min),
            y: 2.0 / (yRange.Max - yRange.Min),
            z: 1.0);

        return translate * scale;
    }

    public new void Render(SKSurface surface)
    {
        if (PlotControl.GRContext is not null)
        {
            RenderWithOpenGL(surface, PlotControl.GRContext);
            return;
        }

        if (Fallback == GLFallbackRenderStrategy.Software)
        {
            surface.Canvas.ClipRect(Axes.DataRect.ToSKRect());
            base.Render(surface);
        }
    }

    protected virtual void RenderWithOpenGL(SKSurface surface, GRContext context)
    {
        int height = (int)surface.Canvas.LocalClipBounds.Height;

        context.Flush();
        context.ResetContext();

        if (!GLHasBeenInitialized)
            InitializeGL();

        GL.Viewport(
            x: (int)Axes.DataRect.Left,
            y: (int)(height - Axes.DataRect.Bottom),
            width: (int)Axes.DataRect.Width,
            height: (int)Axes.DataRect.Height);

        if (LinesProgram is null)
            throw new NullReferenceException(nameof(LinesProgram));

        LinesProgram.Use();
        LinesProgram.SetTransform(CalcTransform());
        LinesProgram.SetColor(LineStyle.Color.ToTkColor());
        GL.BindVertexArray(VertexArrayObject);
        GL.DrawArrays(PrimitiveType.LineStrip, 0, VerticesCount);

        RenderMarkers();
    }

    protected void RenderMarkers()
    {
        if (MarkerStyle.Shape == MarkerShape.None || MarkerStyle.Size == 0)
            return;

        IMarkersDrawProgram? newProgram = MarkerStyle.Shape switch
        {
            MarkerShape.FilledSquare when MarkerProgram is not MarkerFillSquareProgram => new MarkerFillSquareProgram(),
            MarkerShape.FilledCircle when MarkerProgram is not MarkerFillCircleProgram => new MarkerFillCircleProgram(),
            MarkerShape.OpenCircle when MarkerProgram is not MarkerOpenCircleProgram => new MarkerOpenCircleProgram(),
            MarkerShape.OpenSquare when MarkerProgram is not MarkerOpenSquareProgram => new MarkerOpenSquareProgram(),
            MarkerShape.FilledSquare or MarkerShape.FilledCircle or MarkerShape.OpenCircle or MarkerShape.OpenSquare => null,
            _ => throw new NotSupportedException($"Marker shape `{MarkerStyle.Shape}` is not supported by GLPlottables"),
        };

        if (newProgram is not null)
        {
            MarkerProgram?.Dispose();
            MarkerProgram = newProgram;
        }

        if (MarkerProgram is null)
            throw new NullReferenceException(nameof(MarkerProgram));

        MarkerProgram.Use();
        MarkerProgram.SetTransform(CalcTransform());
        MarkerProgram.SetMarkerSize(MarkerStyle.Size);
        MarkerProgram.SetFillColor(MarkerStyle.Fill.Color.ToTkColor());
        MarkerProgram.SetViewPortSize(Axes.DataRect.Width, Axes.DataRect.Height);
        MarkerProgram.SetOutlineColor(MarkerStyle.Outline.Color.ToTkColor());
        MarkerProgram.SetOpenFactor(1.0f - (float)MarkerStyle.Outline.Width * 2 / MarkerStyle.Size);
        GL.BindVertexArray(VertexArrayObject);
        GL.DrawArrays(PrimitiveType.Points, 0, VerticesCount);
    }

    public void GLFinish() => LinesProgram?.GLFinish();
}
