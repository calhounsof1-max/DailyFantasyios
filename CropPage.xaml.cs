#if ANDROID
using Android.Graphics;
#endif

namespace DailyFantasyMAUI;

public partial class CropPage : ContentPage, IDrawable
{
    public static TaskCompletionSource<string?> Result = new();
    public static string LastCropDebug = "";

    readonly string _imagePath;
    int   _bmpW, _bmpH;

    // Fractions relative to gvCrop dimensions (0..1)
    float _topFrac   = 0.25f;
    float _botFrac   = 0.70f;
    float _leftFrac  = 0.05f;
    float _rightFrac = 0.95f;
    bool  _defaultsSet;

    enum Handle { None, Top, Bot, Left, Right, Move }
    Handle _dragging = Handle.None;
    float  _dragStartX, _dragStartY;
    float  _dragStartTop, _dragStartBot, _dragStartLeft, _dragStartRight;
    bool   _scanDone;

    public CropPage(string imagePath)
    {
        InitializeComponent();
        _imagePath = imagePath;
        imgPhoto.Source = ImageSource.FromFile(imagePath);
        gvCrop.Drawable = this;
        gvCrop.StartInteraction += OnStart;
        gvCrop.DragInteraction  += OnDrag;
        gvCrop.EndInteraction   += OnEnd;
        gvCrop.SizeChanged      += OnGvSizeChanged;
        LoadBitmapDimensions();
    }

    // Fires once gvCrop has real dimensions
    void OnGvSizeChanged(object? sender, EventArgs e)
    {
        float gvW = (float)gvCrop.Width;
        float gvH = (float)gvCrop.Height;
        if (_defaultsSet || gvW < 2 || gvH < 2) return;
        _defaultsSet = true;

        // Fixed starting box: middle 50% height, near-full width
        // User drags handles or moves whole box to frame A/B/C rows
        _topFrac   = 0.25f;
        _botFrac   = 0.75f;
        _leftFrac  = 0.06f;
        _rightFrac = 0.94f;
        gvCrop.Invalidate();
    }

    void LoadBitmapDimensions()
    {
#if ANDROID
        var opts = new BitmapFactory.Options { InJustDecodeBounds = true };
        BitmapFactory.DecodeFile(_imagePath, opts);
        int rawW = opts.OutWidth, rawH = opts.OutHeight;
        // EXIF rotation: camera often stores landscape pixels for a portrait shot
        int rot = GetExifRotation(_imagePath);
        if (rot == 90 || rot == 270) { _bmpW = rawH; _bmpH = rawW; }
        else                          { _bmpW = rawW; _bmpH = rawH; }
#endif
    }

#if ANDROID
    static int GetExifRotation(string path)
    {
        try
        {
            var exif = new Android.Media.ExifInterface(path);
            int o = exif.GetAttributeInt(Android.Media.ExifInterface.TagOrientation, 1);
            return o switch { 6 => 90, 3 => 180, 8 => 270, _ => 0 };
        }
        catch { return 0; }
    }

    // Load bitmap, rotate it to match display orientation, then return it
    static Bitmap? LoadDisplayBitmap(string path)
    {
        var bmp = BitmapFactory.DecodeFile(path);
        if (bmp == null) return null;
        int rot = GetExifRotation(path);
        if (rot == 0) return bmp;
        var m = new Android.Graphics.Matrix();
        m.PostRotate(rot);
        var rotated = Bitmap.CreateBitmap(bmp, 0, 0, bmp.Width, bmp.Height, m, true)!;
        bmp.Recycle();
        return rotated;
    }
#endif

    // ── Compute displayed image rect within gvCrop (AspectFit letterbox) ─────

    void DisplayBounds(float gvW, float gvH, out float dl, out float dt, out float dw, out float dh)
    {
        if (_bmpW <= 0 || _bmpH <= 0) { dl = 0; dt = 0; dw = gvW; dh = gvH; return; }
        float imgAR = (float)_bmpW / _bmpH;
        float gvAR  = gvW / gvH;
        if (gvAR > imgAR) { dh = gvH; dw = gvH * imgAR; dl = (gvW - dw) / 2f; dt = 0; }
        else               { dw = gvW; dh = gvW / imgAR; dl = 0; dt = (gvH - dh) / 2f; }
    }

    // ── IDrawable ─────────────────────────────────────────────────────────────

    public void Draw(ICanvas canvas, Microsoft.Maui.Graphics.RectF r)
    {
        float w = r.Width, h = r.Height;
        float topY  = _topFrac   * h;
        float botY  = _botFrac   * h;
        float leftX = _leftFrac  * w;
        float rightX= _rightFrac * w;

        // Dim everything outside the crop rectangle
        var dim = Microsoft.Maui.Graphics.Color.FromRgba(0, 0, 0, 0.62f);
        canvas.FillColor = dim;
        canvas.FillRectangle(0,      0,    w,      topY);           // above
        canvas.FillRectangle(0,      botY, w,      h - botY);       // below
        canvas.FillRectangle(0,      topY, leftX,  botY - topY);    // left
        canvas.FillRectangle(rightX, topY, w - rightX, botY - topY);// right

        // Crop rectangle border
        canvas.StrokeColor = Colors.Yellow;
        canvas.StrokeSize  = 2;
        canvas.DrawRectangle(leftX, topY, rightX - leftX, botY - topY);

        // Handles
        float cx = (leftX + rightX) / 2f;
        float cy = (topY  + botY)  / 2f;
        DrawHBar(canvas, leftX, rightX, topY);   // top
        DrawHBar(canvas, leftX, rightX, botY);   // bottom
        DrawVBar(canvas, leftX, topY, botY);     // left
        DrawVBar(canvas, rightX, topY, botY);    // right
    }

    static void DrawHBar(ICanvas c, float x1, float x2, float y)
    {
        c.FillColor = Colors.Yellow;
        c.FillRectangle(x1, y - 2, x2 - x1, 4);
        float cx = (x1 + x2) / 2f;
        c.FillColor = Microsoft.Maui.Graphics.Color.FromRgba(0.08f, 0.08f, 0.08f, 0.88f);
        c.FillRoundedRectangle(cx - 38, y - 14, 76, 28, 14);
        c.FontColor = Colors.Yellow; c.FontSize = 12;
        c.DrawString("— drag —", cx - 38, y - 14, 76, 28, HorizontalAlignment.Center, VerticalAlignment.Center);
    }

    static void DrawVBar(ICanvas c, float x, float y1, float y2)
    {
        c.FillColor = Colors.Yellow;
        c.FillRectangle(x - 2, y1, 4, y2 - y1);
        float cy = (y1 + y2) / 2f;
        c.FillColor = Microsoft.Maui.Graphics.Color.FromRgba(0.08f, 0.08f, 0.08f, 0.88f);
        c.FillRoundedRectangle(x - 14, cy - 38, 28, 76, 14);
        c.FontColor = Colors.Yellow; c.FontSize = 11;
        c.DrawString("drag", x - 14, cy - 38, 28, 76, HorizontalAlignment.Center, VerticalAlignment.Center);
    }

    // ── Touch ─────────────────────────────────────────────────────────────────

    void OnStart(object? s, TouchEventArgs e)
    {
        float tx = (float)e.Touches[0].X;
        float ty = (float)e.Touches[0].Y;
        float w  = (float)(gvCrop.Width  > 1 ? gvCrop.Width  : Width);
        float h  = (float)(gvCrop.Height > 1 ? gvCrop.Height : Height);
        const float hit = 70f;

        // Check side handles first — they use tx proximity; top/bot use ty proximity
        if (Math.Abs(tx - _leftFrac * w) < hit) { _dragging = Handle.Left;  return; }
        if (Math.Abs(tx - _rightFrac* w) < hit) { _dragging = Handle.Right; return; }
        if (Math.Abs(ty - _topFrac  * h) < hit) { _dragging = Handle.Top;   return; }
        if (Math.Abs(ty - _botFrac  * h) < hit) { _dragging = Handle.Bot;   return; }

        // Inside box → move the whole rectangle
        if (tx > _leftFrac * w && tx < _rightFrac * w &&
            ty > _topFrac  * h && ty < _botFrac   * h)
        {
            _dragging = Handle.Move;
            _dragStartX = tx; _dragStartY = ty;
            _dragStartTop = _topFrac; _dragStartBot = _botFrac;
            _dragStartLeft = _leftFrac; _dragStartRight = _rightFrac;
            return;
        }
        _dragging = Handle.None;
    }

    void OnDrag(object? s, TouchEventArgs e)
    {
        if (_dragging == Handle.None) return;
        float tx = (float)e.Touches[0].X;
        float ty = (float)e.Touches[0].Y;
        float w  = (float)(gvCrop.Width  > 1 ? gvCrop.Width  : Width);
        float h  = (float)(gvCrop.Height > 1 ? gvCrop.Height : Height);

        DisplayBounds(w, h, out float dl, out float dt, out float dw, out float dh);
        float minY = dt / h, maxY = (dt + dh) / h;
        float minX = dl / w, maxX = (dl + dw) / w;
        const float gap = 0.04f;

        switch (_dragging)
        {
            case Handle.Top:
                _topFrac = Math.Clamp(ty / h, 0f, _botFrac - gap);
                break;
            case Handle.Bot:
                _botFrac = Math.Clamp(ty / h, _topFrac + gap, 1f);
                break;
            case Handle.Left:
                _leftFrac = Math.Clamp(tx / w, 0f, _rightFrac - gap);
                break;
            case Handle.Right:
                _rightFrac = Math.Clamp(tx / w, _leftFrac + gap, 1f);
                break;
            case Handle.Move:
                float dy = (ty - _dragStartY) / h;
                float dx = (tx - _dragStartX) / w;
                float boxH = _dragStartBot  - _dragStartTop;
                float boxW = _dragStartRight - _dragStartLeft;
                _topFrac   = Math.Clamp(_dragStartTop  + dy, 0f, 1f - boxH);
                _botFrac   = _topFrac + boxH;
                _leftFrac  = Math.Clamp(_dragStartLeft + dx, 0f, 1f - boxW);
                _rightFrac = _leftFrac + boxW;
                break;
        }
        gvCrop.Invalidate();
    }

    void OnEnd(object? s, TouchEventArgs e) => _dragging = Handle.None;

    // ── Scan ──────────────────────────────────────────────────────────────────

    async void BtnScan_Clicked(object? sender, EventArgs e)
    {
        if (_scanDone) return;
        _scanDone = true;
        btnScan.IsEnabled = false;
        btnScan.Text = "Processing…";

        // Capture fractions + view size on main thread
        float topFrac = _topFrac, botFrac = _botFrac;
        float leftFrac = _leftFrac, rightFrac = _rightFrac;
        float gvW = (float)(gvCrop.Width  > 1 ? gvCrop.Width  : Width);
        float gvH = (float)(gvCrop.Height > 1 ? gvCrop.Height : Math.Max(1, Height - 115));

        string croppedPath = await Task.Run(() => CropBitmap(topFrac, botFrac, leftFrac, rightFrac, gvW, gvH));
        await Navigation.PopModalAsync(false);
        Result.TrySetResult(croppedPath);
    }

    string CropBitmap(float topFrac, float botFrac, float leftFrac, float rightFrac, float gvW, float gvH)
    {
#if ANDROID
        // Load bitmap with EXIF rotation applied so dimensions match what's shown on screen
        var bmp = LoadDisplayBitmap(_imagePath);
        if (bmp == null) return _imagePath;

        int bmpW = bmp.Width, bmpH = bmp.Height;

        // Recompute AspectFit letterbox using real bitmap dimensions
        float imgAR = (float)bmpW / bmpH;
        float gvAR  = gvW / gvH;
        float dl, dt, dw, dh;
        if (gvAR > imgAR) { dh = gvH; dw = gvH * imgAR; dl = (gvW - dw) / 2f; dt = 0; }
        else               { dw = gvW; dh = gvW / imgAR; dl = 0; dt = (gvH - dh) / 2f; }

        int bmpTop   = Math.Max(0,    (int)((topFrac   * gvH - dt) / dh * bmpH));
        int bmpBot   = Math.Min(bmpH, (int)((botFrac   * gvH - dt) / dh * bmpH));
        int bmpLeft  = Math.Max(0,    (int)((leftFrac  * gvW - dl) / dw * bmpW));
        int bmpRight = Math.Min(bmpW, (int)((rightFrac * gvW - dl) / dw * bmpW));

        // Store debug info for status display
        LastCropDebug = $"bmp:{bmpW}x{bmpH} gv:{gvW:0}x{gvH:0} dt:{dt:0} dh:{dh:0} crop:T{bmpTop} B{bmpBot} L{bmpLeft} R{bmpRight}";

        int cropW = bmpRight - bmpLeft;
        int cropH = bmpBot   - bmpTop;
        if (cropW < 10 || cropH < 10) { bmp.Recycle(); return _imagePath; }

        var cropped = Bitmap.CreateBitmap(bmp, bmpLeft, bmpTop, cropW, cropH)!;
        bmp.Recycle();

        string path = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(_imagePath)!, "crop_ticket.jpg");
        using var fs = System.IO.File.Create(path);
        cropped.Compress(Bitmap.CompressFormat.Jpeg, 95, fs);
        cropped.Recycle();
        return path;
#else
        return _imagePath;
#endif
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (!_scanDone) Result.TrySetResult(null);
    }
}
