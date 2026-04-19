using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Collections.Generic;

string basePath = @"d:\Antigravity\Test\NotiFlow";
string pngPath = Path.Combine(basePath, "NotiFlow Icon.png");
string icoPath = Path.Combine(basePath, "NotiFlow Icon.ico");

// 从内存流加载，避免文件锁
byte[] srcBytes = File.ReadAllBytes(pngPath);
using var srcMs = new MemoryStream(srcBytes);
using var bmp = new Bitmap(srcMs);

Console.WriteLine($"Original: {bmp.Width}x{bmp.Height}");

// 扫描非透明内容边界
int minX = bmp.Width, minY = bmp.Height, maxX = 0, maxY = 0;
for (int y = 0; y < bmp.Height; y++)
{
    for (int x = 0; x < bmp.Width; x++)
    {
        if (bmp.GetPixel(x, y).A > 5)
        {
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }
    }
}

int cw = maxX - minX + 1;
int ch = maxY - minY + 1;
Console.WriteLine($"Content: {cw}x{ch}");

// 定义需要生成的 ICO 尺寸
int[] sizes = { 16, 32, 48, 256 };
List<byte[]> pngDataList = new List<byte[]>();

foreach (int sz in sizes)
{
    using var outBmp = new Bitmap(sz, sz, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(outBmp))
    {
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);

        // 针对托盘小图标，稍微减少留白（1%），让图标看起来更满
        float padRatio = sz <= 48 ? 0.01f : 0.02f; 
        float pad = sz * padRatio;
        float drawArea = sz - pad * 2f;
        
        float bigSide = Math.Max(cw, ch);
        float scale = drawArea / bigSide;
        float nw = cw * scale;
        float nh = ch * scale;
        float ox = (sz - nw) / 2f;
        float oy = (sz - nh) / 2f;

        g.DrawImage(bmp, new RectangleF(ox, oy, nw, nh),
                          new RectangleF(minX, minY, cw, ch),
                          GraphicsUnit.Pixel);
    }
    
    // 如果是 256x256，顺便更新主 PNG
    if (sz == 256)
    {
        outBmp.Save(pngPath, ImageFormat.Png);
    }

    using var ms = new MemoryStream();
    outBmp.Save(ms, ImageFormat.Png);
    pngDataList.Add(ms.ToArray());
}

// 写入多尺寸 ICO 文件
using var fs = new FileStream(icoPath, FileMode.Create);
using var bw = new BinaryWriter(fs);

// ICO Header
bw.Write((short)0);           // Reserved
bw.Write((short)1);           // Type (1 for icon)
bw.Write((short)sizes.Length);// Image count

int offset = 6 + (16 * sizes.Length);

// ICO Directory
for (int i = 0; i < sizes.Length; i++)
{
    int s = sizes[i];
    bw.Write((byte)(s >= 256 ? 0 : s)); // Width
    bw.Write((byte)(s >= 256 ? 0 : s)); // Height
    bw.Write((byte)0);    // Palette
    bw.Write((byte)0);    // Reserved
    bw.Write((short)1);   // Planes
    bw.Write((short)32);  // Bits per pixel
    bw.Write(pngDataList[i].Length); // Image size
    bw.Write(offset);     // Image offset
    offset += pngDataList[i].Length;
}

// Image Data
foreach (var data in pngDataList)
{
    bw.Write(data);
}

Console.WriteLine("Multi-size ICO saved with sizes: 16, 32, 48, 256");
Console.WriteLine("Done!");
