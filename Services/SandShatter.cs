using System.Diagnostics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Graphics.DirectX;

namespace Novara.Services;
















public sealed class SandShatter : IDisposable
{
    private const int SrcSize = 1024;
    private const int Samples = 389;
    private const float Grain = 2f;
    private const double Life = 1.4;
    private const double FadeTail = 0.7;
    private const float BurstSpdMin = 300f;
    private const float BurstSpdVar = 640f;
    private const float Gravity = 210f;
    private const float DragPerSec = 1.25f;

    private readonly CanvasDevice _device;
    private readonly byte[] _pixels;
    private readonly Random _rng = new();

    private CanvasImageSource? _surface;
    private CanvasBitmap? _bitmap;
    private Image? _layer;
    private byte[] _buf = Array.Empty<byte>();
    private int _bufW, _bufH;
    private int _winW, _winH;
    private double _scale = 1;

    private float[] _px = Array.Empty<float>(), _py = Array.Empty<float>();
    private float[] _vx = Array.Empty<float>(), _vy = Array.Empty<float>();
    private byte[] _pr = Array.Empty<byte>(), _pg = Array.Empty<byte>(), _pb = Array.Empty<byte>();
    private int _n;


    private static readonly float[] SinLut = BuildSinLut();
    private static float[] BuildSinLut()
    {
        var lut = new float[1024];
        for (int i = 0; i < 1024; i++) lut[i] = MathF.Sin(i * MathF.Tau / 1024f);
        return lut;
    }
    private static float FastSin(float x) => SinLut[(int)(x * 162.97466f) & 1023];

    private readonly Stopwatch _clock = new();
    private readonly Stopwatch _frameSw = new();
    private double _costSum; private int _costN;
    private double _lastT;
    private int _frameN, _slowFrames; private double _dtSum, _dtMax, _dtFirstSum, _dtSecondSum;
    private bool _running, _done, _disposed;

    private int _seeded;



    public Image Layer => _layer!;


    public event EventHandler? Finished;

    private SandShatter(CanvasDevice device, byte[] pixels, Size windowSize)
    {
        _device = device;
        _pixels = pixels;
        _layer = new Image { Stretch = Microsoft.UI.Xaml.Media.Stretch.Fill, IsHitTestVisible = false };




        int max = 0;
        for (int iy = 0; iy < Samples; iy++)
        {
            int gy = iy * SrcSize / Samples;
            for (int ix = 0; ix < Samples; ix++)
            {
                if (_pixels[(gy * SrcSize + ix * SrcSize / Samples) * 4 + 3] >= 120) max++;
            }
        }
        _px = new float[max]; _py = new float[max];
        _vx = new float[max]; _vy = new float[max];
        _pr = new byte[max]; _pg = new byte[max]; _pb = new byte[max];
        EnsureSurface(windowSize);
    }


    public void WarmRender()
    {
        try
        {
            Draw(0);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("SandShatter warm render failed: " + ex.Message);
        }
    }


    public static async Task<SandShatter?> TryCreateAsync(Size windowSize)
    {
        try
        {
            var device = CanvasDevice.GetSharedDevice();
            CanvasBitmap? bmp = null;
            try
            {

                bmp = await CanvasBitmap.LoadAsync(device, new Uri("ms-appx:///Assets/Novara-R.png"));
            }
            catch
            {
                bmp = await CanvasBitmap.LoadAsync(device, System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Novara-R.png"));
            }
            var pixels = bmp.GetPixelBytes();
            bmp.Dispose();
            if (pixels.Length < SrcSize * SrcSize * 4) return null;
            var shatter = new SandShatter(device, pixels, windowSize);
            shatter.WarmRender();
            return shatter;
        }
        catch (Exception ex)
        {
            CrashLogger.LogNote("SandShatter", "warm failed: " + ex);
            return null;
        }
    }



    public void Burst(Rect logoRect, Point origin, Size windowSize)
    {
        if (_disposed) return;
        EnsureSurface(windowSize);
        BuildParticles(logoRect, origin);
        _lastT = 0;
        _costSum = 0; _costN = 0;
        _frameN = 0; _slowFrames = 0; _dtSum = 0; _dtMax = 0; _dtFirstSum = 0; _dtSecondSum = 0;
        _clock.Restart();
        _done = false;
        _running = true;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnRendering;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnRendering;
    }


    public void Cancel()
    {
        _running = false;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnRendering;
        if (_layer != null) _layer.Opacity = 1;
    }

    private void EnsureSurface(Size windowSize)
    {
        int w = Math.Max(2, (int)windowSize.Width), h = Math.Max(2, (int)windowSize.Height);
        if (_surface != null && _winW == w && _winH == h && _layer != null) return;
        _winW = w; _winH = h;



        double scale = Math.Min(1.0, Math.Sqrt(1_600_000.0 / (w * h)));
        _scale = scale;
        _bufW = Math.Max(2, (int)(w * scale));
        _bufH = Math.Max(2, (int)(h * scale));
        _buf = new byte[_bufW * _bufH * 4];
        _bitmap?.Dispose();


        _bitmap = CanvasBitmap.CreateFromBytes(_device, _buf, _bufW, _bufH, DirectXPixelFormat.B8G8R8A8UIntNormalized);
        _surface = new CanvasImageSource(_device, _bufW, _bufH, 96f);
        if (_layer == null)
        {
            _layer = new Image { Stretch = Microsoft.UI.Xaml.Media.Stretch.Fill, IsHitTestVisible = false, Width = w, Height = h };
        }
        else
        {
            _layer.Width = w; _layer.Height = h;
        }
        _layer.Source = _surface;
    }

    private void BuildParticles(Rect logoRect, Point origin)
    {

        float sx = (float)(logoRect.Width / SrcSize), sy = (float)(logoRect.Height / SrcSize);
        float ox = (float)logoRect.X, oy = (float)logoRect.Y;
        float cx = ox + (float)logoRect.Width / 2f, cy = oy + (float)logoRect.Height / 2f;

        float bx = cx - (float)origin.X, by = cy - (float)origin.Y;
        float bd = MathF.Sqrt(bx * bx + by * by) + 1f;

        int n = 0;

        for (int iy = 0; iy < Samples; iy++)
        {
            int gy = iy * SrcSize / Samples;
            for (int ix = 0; ix < Samples; ix++)
            {
                int gx = ix * SrcSize / Samples;
                int idx = (gy * SrcSize + gx) * 4;
                if (_pixels[idx + 3] < 120) continue;
                float x = ox + gx * sx, y = oy + gy * sy;
                float dx = x - cx, dy = y - cy;
                float d = MathF.Sqrt(dx * dx + dy * dy) + 1f;

                float spd = BurstSpdMin + _rng.NextSingle() * BurstSpdVar;
                _px[n] = x; _py[n] = y;
                _vx[n] = dx / d * spd + (_rng.NextSingle() - 0.5f) * 180f + bx / bd * 150f;
                _vy[n] = dy / d * spd + (_rng.NextSingle() - 0.5f) * 180f + by / bd * 150f;

                _pb[n] = _pixels[idx]; _pg[n] = _pixels[idx + 1]; _pr[n] = _pixels[idx + 2];
                n++;
            }
        }
        _n = n;
        _seeded = n;
    }

    private void OnRendering(object? sender, object e)
    {
        if (!_running || _disposed || _surface == null) return;
        try
        {
            _frameSw.Restart();
            double t = _clock.Elapsed.TotalSeconds;
            double rawDt = t - _lastT;
            float dt = (float)Math.Min(rawDt, 1.0 / 20.0);
            if (dt <= 0) dt = 1f / 60f;
            _lastT = t;
            if (rawDt > 0 && rawDt < 1.0)
            {
                _frameN++;
                _dtSum += rawDt * 1000.0;
                if (rawDt * 1000.0 > _dtMax) _dtMax = rawDt * 1000.0;
                if (rawDt > 0.020) _slowFrames++;
                if (t < Life / 2) _dtFirstSum += rawDt * 1000.0;
                else _dtSecondSum += rawDt * 1000.0;
            }

            for (int i = 0; i < _n; i++)
            {
                _vy[i] += Gravity * dt;
                float drag = 1f - DragPerSec * dt;
                if (drag < 0.15f) drag = 0.15f;
                _vx[i] *= drag; _vy[i] *= drag;
                _vx[i] += FastSin(_py[i] * 0.021f + (float)t * 3.1f) * 230f * dt;
                _vy[i] += FastSin(_px[i] * 0.017f + (float)t * 2.7f) * 150f * dt;
                _px[i] += _vx[i] * dt;
                _py[i] += _vy[i] * dt;
            }


            float cull = 160f;
            for (int i = _n - 1; i >= 0; i--)
            {
                if (_px[i] > -cull && _px[i] < _winW + cull && _py[i] > -cull && _py[i] < _winH + cull) continue;
                _n--;
                _px[i] = _px[_n]; _py[i] = _py[_n];
                _vx[i] = _vx[_n]; _vy[i] = _vy[_n];
                _pr[i] = _pr[_n]; _pg[i] = _pg[_n]; _pb[i] = _pb[_n];
            }

            double alpha = Math.Clamp((Life - t) / FadeTail, 0, 1);
            Draw(alpha);
            _costSum += _frameSw.Elapsed.TotalMilliseconds; _costN++;

            if (t >= Life)
            {
                _running = false;
                Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnRendering;
                if (!_done)
                {
                    _done = true;



                    LastSummary = $"burst done: {_seeded:N0} seeded, {_n:N0} alive, frame cost avg {(_costN > 0 ? _costSum / _costN : 0):F2}ms; "
                        + $"frames={_frameN} dt avg {(_frameN > 0 ? _dtSum / _frameN : 0):F1}ms max {_dtMax:F1}ms slow>20ms={_slowFrames}"
                        + (t >= Life ? $"; dt first-half avg {(_dtFirstSum / Math.Max(1, _frameN / 2.0)):F1}ms second-half avg {(_dtSecondSum / Math.Max(1, _frameN - _frameN / 2.0)):F1}ms" : "");
                    System.Diagnostics.Debug.WriteLine("SandShatter " + LastSummary);
                    Finished?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        catch (Exception ex)
        {


            System.Diagnostics.Debug.WriteLine("SandShatter frame failed: " + ex);
            try { _layer?.DispatcherQueue.TryEnqueue(() => CrashLogger.LogNote("SandShatter", "frame failed: " + ex)); } catch { }
            Cancel();
            if (!_done)
            {
                _done = true;
                Finished?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private void Draw(double alpha)
    {
        Array.Clear(_buf);
        byte a = (byte)Math.Clamp(alpha * 255, 0, 255);
        float sc = (float)_scale;
        int gi = Math.Max(1, (int)(Grain * sc + 0.5f));
        for (int i = 0; i < _n; i++)
        {
            int x0 = (int)(_px[i] * sc), y0 = (int)(_py[i] * sc);
            byte b = (byte)(_pb[i] * a >> 8), g = (byte)(_pg[i] * a >> 8), r = (byte)(_pr[i] * a >> 8);
            for (int dy = 0; dy < gi; dy++)
            {
                int y = y0 + dy;
                if ((uint)y >= _bufH) continue;
                int row = y * _bufW;
                for (int dx = 0; dx < gi; dx++)
                {
                    int x = x0 + dx;
                    if ((uint)x >= _bufW) continue;
                    int idx = (row + x) * 4;
                    _buf[idx] = b; _buf[idx + 1] = g; _buf[idx + 2] = r; _buf[idx + 3] = a;
                }
            }
        }
        _bitmap!.SetPixelBytes(_buf);
        using (var ds = _surface!.CreateDrawingSession(default))
        {
            ds.DrawImage(_bitmap, new Rect(0, 0, _bufW, _bufH));
        }
    }




    public void Resize(Windows.Foundation.Size windowSize)
    {
        if (_disposed || !_running) return;
        int oldW = _winW, oldH = _winH;
        EnsureSurface(windowSize);
        if (oldW <= 1 || oldH <= 1) return;
        float sx = (float)_winW / oldW, sy = (float)_winH / oldH;
        if (Math.Abs(sx - 1f) < 0.001f && Math.Abs(sy - 1f) < 0.001f) return;
        for (int i = 0; i < _n; i++) { _px[i] *= sx; _py[i] *= sy; }
    }


    public string? LastSummary { get; private set; }

    public string? TakeSummary()
    {
        var summary = LastSummary;
        LastSummary = null;
        return summary;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _running = false;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnRendering;
        _bitmap?.Dispose();
        _bitmap = null;


        _surface = null;
    }
}
