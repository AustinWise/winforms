﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Drawing.Drawing2D;

namespace System.Windows.Forms;

/// <summary>
/// Helper class for scaling coordinates and images according to current DPI scaling set in Windows for the primary screen.
/// </summary>
internal static class DpiHelper
{
    private const double LogicalDpi = 96.0;
    private static bool s_isInitialized;
    /// <summary>
    /// The primary screen's (device) current horizontal DPI
    /// </summary>
    private static double s_deviceDpiX = LogicalDpi;

    /// <summary>
    /// The primary screen's (device) current vertical DPI
    /// </summary>
    private static double s_deviceDpiY = LogicalDpi;

    private static double s_logicalToDeviceUnitsScalingFactorX;
    private static double s_logicalToDeviceUnitsScalingFactorY;
    private static InterpolationMode s_interpolationMode = InterpolationMode.Invalid;

    private static void Initialize()
    {
        if (s_isInitialized)
        {
            return;
        }

        IntPtr hDC = Interop.User32.GetDC(IntPtr.Zero);
        if (hDC != IntPtr.Zero)
        {
            s_deviceDpiX = Interop.Gdi32.GetDeviceCaps(hDC, Interop.Gdi32.DeviceCapability.LOGPIXELSX);
            s_deviceDpiY = Interop.Gdi32.GetDeviceCaps(hDC, Interop.Gdi32.DeviceCapability.LOGPIXELSY);

            Interop.User32.ReleaseDC(IntPtr.Zero, hDC);
        }

        s_isInitialized = true;
    }

    private static double LogicalToDeviceUnitsScalingFactorX
    {
        get
        {
            if (s_logicalToDeviceUnitsScalingFactorX == 0.0)
            {
                Initialize();
                s_logicalToDeviceUnitsScalingFactorX = s_deviceDpiX / LogicalDpi;
            }

            return s_logicalToDeviceUnitsScalingFactorX;
        }
    }

    private static double LogicalToDeviceUnitsScalingFactorY
    {
        get
        {
            if (s_logicalToDeviceUnitsScalingFactorY == 0.0)
            {
                Initialize();
                s_logicalToDeviceUnitsScalingFactorY = s_deviceDpiY / LogicalDpi;
            }

            return s_logicalToDeviceUnitsScalingFactorY;
        }
    }

    private static InterpolationMode InterpolationMode
    {
        get
        {
            if (s_interpolationMode == InterpolationMode.Invalid)
            {
                int dpiScalePercent = (int)Math.Round(LogicalToDeviceUnitsScalingFactorX * 100);

                // We will prefer NearestNeighbor algorithm for 200, 300, 400, etc zoom factors, in which each pixel become a 2x2, 3x3, 4x4, etc rectangle.
                // This produces sharp edges in the scaled image and doesn't cause distorsions of the original image.
                // For any other scale factors we will prefer a high quality resizing algorithm. While that introduces fuzziness in the resulting image,
                // it will not distort the original (which is extremely important for small zoom factors like 125%, 150%).
                // We'll use Bicubic in those cases, except on reducing (zoom < 100, which we shouldn't have anyway), in which case Linear produces better
                // results because it uses less neighboring pixels.
                if ((dpiScalePercent % 100) == 0)
                {
                    s_interpolationMode = InterpolationMode.NearestNeighbor;
                }
                else if (dpiScalePercent < 100)
                {
                    s_interpolationMode = InterpolationMode.HighQualityBilinear;
                }
                else
                {
                    s_interpolationMode = InterpolationMode.HighQualityBicubic;
                }
            }

            return s_interpolationMode;
        }
    }

    private static Bitmap ScaleBitmapToSize(Bitmap logicalImage, Size deviceImageSize)
    {
        Bitmap deviceImage = new(deviceImageSize.Width, deviceImageSize.Height, logicalImage.PixelFormat);

        using var graphics = Graphics.FromImage(deviceImage);

        graphics.InterpolationMode = InterpolationMode;

        RectangleF sourceRect = new(0, 0, logicalImage.Size.Width, logicalImage.Size.Height);
        RectangleF destRect = new(0, 0, deviceImageSize.Width, deviceImageSize.Height);

        // Specify a source rectangle shifted by half of pixel to account for GDI+ considering the source origin the center of top-left pixel
        // Failing to do so will result in the right and bottom of the bitmap lines being interpolated with the graphics' background color,
        // and will appear black even if we cleared the background with transparent color.
        // The apparition of these artifacts depends on the interpolation mode, on the dpi scaling factor, etc.
        // E.g. at 150% DPI, Bicubic produces them and NearestNeighbor is fine, but at 200% DPI NearestNeighbor also shows them.
        sourceRect.Offset(-0.5f, -0.5f);

        graphics.DrawImage(logicalImage, destRect, sourceRect, GraphicsUnit.Pixel);

        return deviceImage;
    }

    private static Bitmap CreateScaledBitmap(Bitmap logicalImage)
    {
        Size deviceImageSize = LogicalToDeviceUnits(logicalImage.Size);
        return ScaleBitmapToSize(logicalImage, deviceImageSize);
    }

    /// <summary>
    /// Returns whether scaling is required when converting between logical-device units,
    /// if the application opted in the automatic scaling in the .config file.
    /// </summary>
    public static bool IsScalingRequired
    {
        get
        {
            Initialize();
            return s_deviceDpiX != LogicalDpi || s_deviceDpiY != LogicalDpi;
        }
    }

    /// <summary>
    /// Transforms a horizontal integer coordinate from logical to device units
    /// by scaling it up  for current DPI and rounding to nearest integer value
    /// Note: this method should be called only inside an if (DpiHelper.IsScalingRequired) clause
    /// </summary>
    /// <param name="value">The horizontal value in logical units</param>
    /// <returns>The horizontal value in device units</returns>
    public static int LogicalToDeviceUnitsX(int value)
    {
        return (int)Math.Round(LogicalToDeviceUnitsScalingFactorX * (double)value);
    }

    /// <summary>
    /// Transforms a vertical integer coordinate from logical to device units
    /// by scaling it up  for current DPI and rounding to nearest integer value
    /// Note: this method should be called only inside an if (DpiHelper.IsScalingRequired) clause
    /// </summary>
    /// <param name="value">The vertical value in logical units</param>
    /// <returns>The vertical value in device units</returns>
    public static int ScaleToInitialSystemDpi(int value)
    {
        return (int)Math.Round(LogicalToDeviceUnitsScalingFactorY * (double)value);
    }

    /// <summary>
    /// Returns a new Size with the input's
    /// dimensions converted from logical units to device units.
    /// Note: this method should be called only inside an if (DpiHelper.IsScalingRequired) clause
    /// </summary>
    /// <param name="logicalSize">Size in logical units</param>
    /// <returns>Size in device units</returns>
    public static Size LogicalToDeviceUnits(Size logicalSize)
    {
        return new Size(LogicalToDeviceUnitsX(logicalSize.Width),
                        ScaleToInitialSystemDpi(logicalSize.Height));
    }

    /// <summary>
    /// Create and return a new bitmap scaled to the specified size.
    /// Note: this method should be called only inside an if (DpiHelper.IsScalingRequired) clause
    /// </summary>
    /// <param name="logicalImage">The image to scale from logical units to device units</param>
    /// <param name="targetImageSize">The size to scale image to</param>
    [return: NotNullIfNotNull(nameof(logicalImage))]
    public static Bitmap? CreateResizedBitmap(Bitmap? logicalImage, Size targetImageSize)
    {
        if (logicalImage is null)
        {
            return null;
        }

        return ScaleBitmapToSize(logicalImage, targetImageSize);
    }

    /// <summary>
    /// Create a new bitmap scaled for the device units.
    /// When displayed on the device, the scaled image will have same size as the original image would have when displayed at 96dpi.
    /// Note: this method should be called only inside an if (DpiHelper.IsScalingRequired) clause
    /// </summary>
    /// <param name="logicalBitmap">The image to scale from logical units to device units</param>
    public static void ScaleBitmapLogicalToDevice([NotNullIfNotNull(nameof(logicalBitmap))]ref Bitmap? logicalBitmap)
    {
        if (logicalBitmap is null)
        {
            return;
        }

        Bitmap deviceBitmap = CreateScaledBitmap(logicalBitmap);
        if (deviceBitmap is not null)
        {
            logicalBitmap.Dispose();
            logicalBitmap = deviceBitmap;
        }
    }
}
