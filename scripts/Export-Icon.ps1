# Copyright (C) 2026 igedaena
# SPDX-License-Identifier: GPL-3.0-or-later
#
# This file is part of 7-Zip Extension Menu.
# This program is free software: you can redistribute it and/or modify
# it under the terms of the GNU General Public License as published by
# the Free Software Foundation, either version 3 of the License, or
# (at your option) any later version.
#
# This program is distributed in the hope that it will be useful,
# but WITHOUT ANY WARRANTY; without even the implied warranty of
# MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See LICENSE
# or https://www.gnu.org/licenses/ for the complete license.

[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Collections.Generic;
public static class IconExporter {
    public static void Export(string input, string destination, string preview) {
        using (var original = new Bitmap(input)) {
            int left = original.Width, top = original.Height, right = -1, bottom = -1;
            for (int y = 0; y < original.Height; y++) for (int x = 0; x < original.Width; x++) {
                if (original.GetPixel(x, y).A <= 16) continue;
                left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y);
            }
            if (right < left) throw new InvalidDataException("No visible icon content");
            var crop = new Rectangle(left, top, right-left+1, bottom-top+1);
            int[] sizes = {16,20,24,32,40,48,64,128,256};
            var frames = new List<byte[]>();
            foreach (int size in sizes) using (var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb)) {
                using (var graphics = Graphics.FromImage(bitmap)) {
                    graphics.Clear(Color.Transparent);
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    graphics.CompositingQuality = CompositingQuality.HighQuality;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    float scale = (size-2f)/Math.Max(crop.Width,crop.Height);
                    float width = crop.Width*scale, height=crop.Height*scale;
                    graphics.DrawImage(original,new RectangleF((size-width)/2f,(size-height)/2f,width,height),crop,GraphicsUnit.Pixel);
                }
                if (size == 256) bitmap.Save(preview,ImageFormat.Png);
                using (var stream = new MemoryStream()) { bitmap.Save(stream,ImageFormat.Png); frames.Add(stream.ToArray()); }
            }
            using (var writer = new BinaryWriter(File.Create(destination))) {
                writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
                int offset = 6 + 16*sizes.Length;
                for (int i=0;i<sizes.Length;i++) {
                    writer.Write((byte)(sizes[i]==256?0:sizes[i])); writer.Write((byte)(sizes[i]==256?0:sizes[i]));
                    writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
                    writer.Write(frames[i].Length); writer.Write(offset); offset += frames[i].Length;
                }
                foreach (var frame in frames) writer.Write(frame);
            }
        }
    }
}
'@
[IconExporter]::Export((Join-Path $projectRoot 'assets\sevenzip-extension-source.png'),(Join-Path $projectRoot 'assets\sevenzip-extension.ico'),(Join-Path $projectRoot 'assets\sevenzip-extension.png'))
Write-Output 'Exported transparent PNG preview and 9-size Windows ICO (16..256 pixels).'
