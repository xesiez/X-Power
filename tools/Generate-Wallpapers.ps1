$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$source = @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class XPowerWallpaperGenerator
{
    private static readonly string[] Names = {
        "aurora-core.jpg", "deep-current.jpg", "violet-pulse.jpg", "solar-ember.jpg",
        "arctic-grid.jpg", "emerald-drift.jpg", "crimson-orbit.jpg", "midnight-circuit.jpg"
    };
    private static readonly Color[][] Palettes = {
        new[] { Color.FromArgb(5,16,28), Color.FromArgb(8,91,86), Color.FromArgb(58,220,164) },
        new[] { Color.FromArgb(4,14,34), Color.FromArgb(12,57,105), Color.FromArgb(26,177,194) },
        new[] { Color.FromArgb(15,10,38), Color.FromArgb(74,38,130), Color.FromArgb(174,108,255) },
        new[] { Color.FromArgb(30,12,14), Color.FromArgb(129,45,27), Color.FromArgb(255,157,66) },
        new[] { Color.FromArgb(7,22,38), Color.FromArgb(35,91,128), Color.FromArgb(154,218,239) },
        new[] { Color.FromArgb(4,23,22), Color.FromArgb(17,102,74), Color.FromArgb(92,210,142) },
        new[] { Color.FromArgb(25,7,19), Color.FromArgb(117,22,52), Color.FromArgb(240,83,103) },
        new[] { Color.FromArgb(4,9,19), Color.FromArgb(28,40,65), Color.FromArgb(91,115,155) }
    };

    public static void Generate(string output)
    {
        System.IO.Directory.CreateDirectory(output);
        for (int i = 0; i < Names.Length; i++) GenerateOne(System.IO.Path.Combine(output, Names[i]), i);
    }

    private static void GenerateOne(string path, int index)
    {
        const int width = 3840, height = 2160;
        Color[] p = Palettes[index];
        using (Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb))
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            using (var gradient = new LinearGradientBrush(new Rectangle(0,0,width,height), p[0], p[1], 28f + index * 7f))
                g.FillRectangle(gradient, 0, 0, width, height);

            using (var glow = new SolidBrush(Color.FromArgb(105, p[2])))
                g.FillEllipse(glow, width * (index % 3 == 0 ? .48f : .58f), -height * .42f, width * .68f, height * 1.05f);
            using (var shadow = new LinearGradientBrush(new Rectangle(0,0,width,height), Color.FromArgb(5,0,0,0), Color.FromArgb(205,2,6,15), 90f))
                g.FillRectangle(shadow, 0, 0, width, height);

            // Layered orbital geometry gives each wallpaper a recognizable, low-distraction focal shape.
            int cx = index % 2 == 0 ? 2780 : 3050;
            int cy = index % 3 == 0 ? 820 : 1080;
            for (int ring = 0; ring < 9; ring++)
            {
                int diameter = 560 + ring * 185;
                int alpha = Math.Max(10, 62 - ring * 5);
                using (var pen = new Pen(Color.FromArgb(alpha, p[2]), 3 + (ring % 3)))
                    g.DrawEllipse(pen, cx - diameter/2, cy - diameter/2, diameter, diameter);
            }

            Point[] shardA = {
                new Point(0, height), new Point(0, (int)(height*.72)),
                new Point((int)(width*.40), (int)(height*.18)),
                new Point((int)(width*.62), (int)(height*.34)),
                new Point((int)(width*.25), height)
            };
            using (var shard = new SolidBrush(Color.FromArgb(42, p[2])))
                g.FillPolygon(shard, shardA);

            using (var line = new Pen(Color.FromArgb(85, p[2]), 4))
            {
                for (int n = 0; n < 7; n++)
                {
                    int y = 320 + n * 230;
                    g.DrawBezier(line, -150, y + 260, 900, y - 180, 1750, y + 360, 2800, y - 120);
                }
            }

            if (index == 4 || index == 7)
            {
                using (var grid = new Pen(Color.FromArgb(32, p[2]), 2))
                {
                    for (int x = 0; x < width; x += 180) g.DrawLine(grid, x, 0, x, height);
                    for (int y = 0; y < height; y += 180) g.DrawLine(grid, 0, y, width, y);
                }
            }

            AddFineGrain(bitmap, 100 + index * 7919, 9);
            var encoder = ImageCodecInfo.GetImageEncoders();
            ImageCodecInfo jpg = null;
            foreach (var item in encoder) if (item.MimeType == "image/jpeg") jpg = item;
            using (var parameters = new EncoderParameters(1))
            {
                parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 98L);
                bitmap.Save(path, jpg, parameters);
            }
        }
    }

    private static void AddFineGrain(Bitmap bitmap, int seed, int amplitude)
    {
        Rectangle rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        BitmapData data = bitmap.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
        try
        {
            int length = Math.Abs(data.Stride) * data.Height;
            byte[] pixels = new byte[length];
            Marshal.Copy(data.Scan0, pixels, 0, length);
            Random random = new Random(seed);
            for (int i = 0; i < length; i++)
            {
                if (i % 3 == 0) continue;
                int noise = random.Next(-amplitude, amplitude + 1);
                pixels[i] = (byte)Math.Max(0, Math.Min(255, pixels[i] + noise));
            }
            Marshal.Copy(pixels, 0, data.Scan0, length);
        }
        finally { bitmap.UnlockBits(data); }
    }
}
'@

Add-Type -TypeDefinition $source -ReferencedAssemblies System.Drawing
$output = Join-Path $PSScriptRoot "..\src\XPower.App\Assets\Wallpapers"
$null = New-Item -ItemType Directory -Force -Path $output
$output = (Resolve-Path $output).Path
[XPowerWallpaperGenerator]::Generate($output)
$files = Get-ChildItem $output -Filter *.jpg
if ($files.Count -ne 8) { throw "Expected eight 4K wallpaper assets, found $($files.Count)." }
$bytes = ($files | Measure-Object -Property Length -Sum).Sum
Write-Host ("Generated {0} original 4K wallpapers ({1:N2} MB total)." -f $files.Count, ($bytes / 1MB))
if ($bytes -lt 22MB) { throw "The functional 4K wallpaper library is below 22 MB; increase image detail rather than adding padding files." }
