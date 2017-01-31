#region (c)2016 Hawkynt
/*
    NAPS2(Not Another PDF Scanner 2)
    http://sourceforge.net/projects/naps2/
    
    Copyright (C) 2016       Hawkynt
    
    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using AForge;
using AForge.Imaging;
using AForge.Imaging.Filters;

namespace Hawkynt {
  public class Extractor {

    private readonly FileInfo _source;
    public Extractor(FileInfo source) {
      this._source = source;
      this._images = new Lazy<Classes.PdfImageExtractor.ImageInfo[]>(() => Classes.PdfImageExtractor.ExtractImages(source.FullName).ToArray());
    }

    private readonly Lazy<Classes.PdfImageExtractor.ImageInfo[]> _images;

    public int Count => this._images.Value.Length;
    public IEnumerable<Tuple<Bitmap, bool>> ExtractImages() => this._images.Value.Select(image => Tuple.Create(new Bitmap(image.Image), image.Extension == "png"));

  }

  public static class AutoContrastImage {

    private static Range _GetMinMaxFloat(int[] values) {
      var result = _GetMinMax(values);
      return new Range(result.Min / 255f, result.Max / 255f);
    }

    private static IntRange _GetMinMax(int[] values) {
      var totalCount = values.Sum();
      const double LOWER_CLIP_PERCENT = 0.5;
      const double UPPER_CLIP_PERCENT = 0.5;
      var clipCount = LOWER_CLIP_PERCENT * totalCount / 100;
      int min;
      var sum = 0;
      for (min = 0; min < values.Length; ++min) {
        sum += values[min];
        if (sum >= clipCount)
          break;
      }

      clipCount = UPPER_CLIP_PERCENT * totalCount / 100;
      int max;
      sum = 0;
      for (max = values.Length - 1; max >= 0; --max) {
        sum += values[max];
        if (sum >= clipCount)
          break;
      }

      return new IntRange(min, max);
    }

    public static Bitmap ApplyAutoRGB(Bitmap image) {
      var stats = new ImageStatistics(image);

      var redBoundaries = _GetMinMax(stats.Red.Values);
      var greenBoundaries = _GetMinMax(stats.Green.Values);
      var blueBoundaries = _GetMinMax(stats.Blue.Values);
      var filter = new LevelsLinear {
        InRed = redBoundaries,
        InGreen = greenBoundaries,
        InBlue = blueBoundaries,
      };
      var result = filter.Apply(image);
      return result;
    }

    public static Bitmap ApplyAutoLuminance(Bitmap image) {
      var stats = new ImageStatisticsHSL(image);

      var range = _GetMinMaxFloat(stats.Luminance.Values);
      var filter = new HSLLinear {
        InLuminance = range,
      };
      var result = filter.Apply(image);
      return result;
    }

    public static Bitmap ApplyAutoSaturation(Bitmap image) {
      var stats = new ImageStatisticsHSL(image);

      var range = _GetMinMaxFloat(stats.Saturation.Values);
      var filter = new HSLLinear {
        InSaturation = new Range(0, range.Max)
      };
      var result = filter.Apply(image);
      return result;
    }

  }

  public static class DeskewImage {
    public static double GetSkewAngle(Bitmap image) {

      var width = image.Width;
      var height = image.Height;
      const double cropPercentage = 10;

      // crop side to avoid detecting black borders in skew
      using (
        var croppedImage = new Crop(new Rectangle((int)(width * cropPercentage / 100), (int)(height * cropPercentage / 100), (int)(width * (100 - cropPercentage) / 100), (int)(height * (100 - cropPercentage) / 100))).Apply(image)) {

        // make grayscale image (BT709)
        using (var greyImage = new Grayscale(0.2125, 0.7154, 0.0721).Apply(croppedImage)) {

          new ContrastStretch().ApplyInPlace(greyImage);
          new Threshold(100).ApplyInPlace(greyImage);

          // get documents skew angle
          var angle = new DocumentSkewChecker().GetSkewAngle(greyImage);
          return angle;
        }
      }
    }
  }
}
