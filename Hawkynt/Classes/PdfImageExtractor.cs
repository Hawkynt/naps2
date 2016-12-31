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
using System.Collections.Generic;
using System.Drawing;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;

namespace Classes {
  /// <summary>
  /// Helper class to extract images from a PDF file. Works with the most
  /// common image types embedded in PDF files, as far as I can tell.
  /// </summary>
  /// <example>
  /// Usage example:
  ///   <code>
  /// foreach (var filename in Directory.GetFiles(searchPath, “*.pdf”, SearchOption.TopDirectoryOnly))
  /// {
  /// var images = ImageExtractor.ExtractImages(filename);
  /// var directory = Path.GetDirectoryName(filename);
  /// foreach (var name in images.Keys)
  /// {
  /// images[name].Save(Path.Combine(directory, name));
  /// }
  /// }
  ///   </code></example>
  public static class PdfImageExtractor {
    /// <summary>
    /// Container class for images found in file.
    /// </summary>
    public struct ImageInfo {
      internal ImageInfo(int page, int indexOnPage, string extension, Image image)
        : this() {
        this.Page = page;
        this.IndexOnPage = indexOnPage;
        this.Image = image;
        this.Extension = extension;
      }

      /// <summary>
      /// Gets the extension.
      /// </summary>
      public string Extension { get; }

      /// <summary>
      /// Gets the page.
      /// </summary>
      public int Page { get; }

      /// <summary>
      /// Gets the index on page.
      /// </summary>
      public int IndexOnPage { get; }

      /// <summary>
      /// Gets the image.
      /// </summary>
      public Image Image { get; }
    }

    #region Methods

    #region Public Methods

    /// <summary>Extracts all images (of types that iTextSharp knows how to decode) from a PDF file.</summary>
    public static IEnumerable<ImageInfo> ExtractImages(string filename) {
      using (var reader = new PdfReader(filename)) {
        var parser = new PdfReaderContentParser(reader);
        for (var i = 1; i <= reader.NumberOfPages; i++) {
          var rotation = reader.GetPageRotation(i);
          var listener = new ImageRenderListener(rotation);
          parser.ProcessContent(i, listener);
          var index = 1;
          if (listener.Images.Count <= 0)
            continue;

          foreach (var pair in listener.Images)
            yield return new ImageInfo(i, index++, pair.Value, pair.Key);
        }
      }
    }

    #endregion Public Methods

    #endregion Methods
  }

  /// <summary>
  /// Listener for getting images from the pdf document.
  /// </summary>
  internal class ImageRenderListener : IRenderListener {
    #region Fields

    private readonly int _rotation;

    #endregion Fields

    #region Properties

    /// <summary>
    /// Gets the images.
    /// </summary>
    public Dictionary<Image, string> Images { get; } = new Dictionary<Image, string>();

    #endregion Properties

    public ImageRenderListener(int rotation) {
      this._rotation = rotation;
    }

    #region Methods

    #region Public Methods

    public void BeginTextBlock() { }
    public void EndTextBlock() { }

    public void RenderImage(ImageRenderInfo renderInfo) {
      var image = renderInfo.GetImage();
      var filter = image.Get(PdfName.FILTER);

      //int width = Convert.ToInt32(image.Get(PdfName.WIDTH).ToString());
      //int bitsPerComponent = Convert.ToInt32(image.Get(PdfName.BITSPERCOMPONENT).ToString());
      //string subtype = image.Get(PdfName.SUBTYPE).ToString();
      //int height = Convert.ToInt32(image.Get(PdfName.HEIGHT).ToString());
      //int length = Convert.ToInt32(image.Get(PdfName.LENGTH).ToString());
      //string colorSpace = image.Get(PdfName.COLORSPACE).ToString();
      /* It appears to be safe to assume that when filter == null, PdfImageObject
       * does not know how to decode the image to a System.Drawing.Image.
       *
       * Uncomment the code above to verify, but when I’ve seen this happen,
       * width, height and bits per component all equal zero as well. */
      if (filter == null)
        return;

      var drawingImage = image.GetDrawingImage();
      var extension = ".";
      if (PdfName.DCTDECODE.Equals(filter))
        extension += PdfImageObject.ImageBytesType.JPG.FileExtension;
      else if (PdfName.JPXDECODE.Equals(filter))
        extension += PdfImageObject.ImageBytesType.JP2.FileExtension;
      else if (PdfName.FLATEDECODE.Equals(filter))
        extension += PdfImageObject.ImageBytesType.PNG.FileExtension;
      else if (PdfName.LZWDECODE.Equals(filter))
        extension += PdfImageObject.ImageBytesType.CCITT.FileExtension;
      /* Rather than struggle with the image stream and try to figure out how to handle
         * BitMapData scan lines in various formats (like virtually every sample I’ve found
         * online), use the PdfImageObject.GetDrawingImage() method, which does the work for us. */

      var angle = (this._rotation + 180) % 360;
      switch (angle) {
        case 0:
        {
          break;
        }
        case 90:
        {
          drawingImage.RotateFlip(RotateFlipType.Rotate90FlipNone);
          break;
        }
        case 180:
        {
          drawingImage.RotateFlip(RotateFlipType.Rotate180FlipNone);
          break;
        }
        case 270:
        {
          drawingImage.RotateFlip(RotateFlipType.Rotate270FlipNone);
          break;
        }
        default:
        {
          var newImage = drawingImage.Rotate(angle);
          drawingImage.Dispose();
          drawingImage = newImage;
          break;
        }
      }

      drawingImage.RotateFlip(RotateFlipType.Rotate180FlipNone);
      this.Images.Add(drawingImage, extension);
    }

    public void RenderText(TextRenderInfo renderInfo) { }

    #endregion Public Methods

    #endregion Methods
  }
}