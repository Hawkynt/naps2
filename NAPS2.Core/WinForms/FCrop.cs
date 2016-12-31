/*
    NAPS2 (Not Another PDF Scanner 2)
    http://sourceforge.net/projects/naps2/
    
    Copyright (C) 2009       Pavel Sorejs
    Copyright (C) 2012       Michael Adams
    Copyright (C) 2013       Peter De Leeuw
    Copyright (C) 2012-2015  Ben Olden-Cooligan

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.
*/

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using NAPS2.Scan.Images;
using NAPS2.Scan.Images.Transforms;
using NAPS2.Util;

namespace NAPS2.WinForms {
  partial class FCrop : FormBase {
    private readonly ChangeTracker changeTracker;
    private readonly ThumbnailRenderer thumbnailRenderer;

    private Bitmap _workingImage;
    private Bitmap _imageForBackgroundOperations;

    public FCrop(ChangeTracker changeTracker, ThumbnailRenderer thumbnailRenderer) {
      this.changeTracker = changeTracker;
      this.thumbnailRenderer = thumbnailRenderer;
      this.InitializeComponent();

      this.CropTransform = new CropTransform();
    }

    private Bitmap _CurrentPreviewImage {
      set {
        this.pictureBox.Image?.Dispose();
        this.pictureBox.Image = value;
      }
    }

    public ScannedImage Image { get; set; }

    public List<ScannedImage> SelectedImages { get; set; }

    public CropTransform CropTransform { get; private set; }

    private bool _TransformMultiple => this.SelectedImages != null && this.checkboxApplyToSelected.Checked;

    private IEnumerable<ScannedImage> _ImagesToTransform => this._TransformMultiple ? this.SelectedImages : Enumerable.Repeat(this.Image, 1);

    protected override void OnLoad(object sender, EventArgs eventArgs) {
      if (this.SelectedImages != null && this.SelectedImages.Count > 1) {
        this.checkboxApplyToSelected.Text = string.Format(this.checkboxApplyToSelected.Text, this.SelectedImages.Count);
      } else {
        ConditionalControls.Hide(this.checkboxApplyToSelected, 6);
      }

      var lm = new LayoutManager(this)
        .Bind(this.pictureBox)
        .WidthToForm()
        .HeightToForm()
        .Bind(this.tbLeft, this.tbRight)
        .WidthTo(() => (int)(this._GetImageWidthRatio() * this.pictureBox.Width))
        .LeftTo(() => (int)((1 - this._GetImageWidthRatio()) * this.pictureBox.Width / 2))
        .Bind(this.tbTop, this.tbBottom)
        .HeightTo(() => (int)(this._GetImageHeightRatio() * this.pictureBox.Height))
        .TopTo(() => (int)((1 - this._GetImageHeightRatio()) * this.pictureBox.Height / 2))
        .Bind(this.tbBottom, this.btnOK, this.btnCancel)
        .RightToForm()
        .Bind(this.tbRight, this.checkboxApplyToSelected, this.btnRevert, this.btnOK, this.btnCancel)
        .BottomToForm()
        .Activate();
      this.Size = new Size(600, 600);

      this._workingImage = this.Image.GetImage();
      this._imageForBackgroundOperations = this.Image.GetImage();
      this._UpdateCropBounds();
      this._UpdatePreviewBox();

      lm.UpdateLayout();
    }

    private double _GetImageWidthRatio() {
      if (this._workingImage == null) {
        return 1;
      }
      var imageAspect = this._workingImage.Width / (double)this._workingImage.Height;
      var pboxAspect = this.pictureBox.Width / (double)this.pictureBox.Height;
      if (imageAspect > pboxAspect) {
        return 1;
      }
      return imageAspect / pboxAspect;
    }

    private double _GetImageHeightRatio() {
      if (this._workingImage == null) {
        return 1;
      }
      var imageAspect = this._workingImage.Width / (double)this._workingImage.Height;
      var pboxAspect = this.pictureBox.Width / (double)this.pictureBox.Height;
      if (pboxAspect > imageAspect) {
        return 1;
      }
      return pboxAspect / imageAspect;
    }

    private void _UpdateCropBounds() {
      this.tbLeft.Maximum = this.tbRight.Maximum = this._workingImage.Width;
      this.tbTop.Maximum = this.tbBottom.Maximum = this._workingImage.Height;

      this.tbLeft.Value = this.tbTop.Value = 0;
      this.tbRight.Value = this._workingImage.Width;
      this.tbTop.Value = this._workingImage.Height;
    }

    private void _UpdateTransform() {
      this.CropTransform.Left = Math.Min(this.tbLeft.Value, this.tbRight.Value);
      this.CropTransform.Right = this._workingImage.Width - Math.Max(this.tbLeft.Value, this.tbRight.Value);
      this.CropTransform.Bottom = Math.Min(this.tbTop.Value, this.tbBottom.Value);
      this.CropTransform.Top = this._workingImage.Height - Math.Max(this.tbTop.Value, this.tbBottom.Value);
      this._UpdatePreviewBox();
    }


    private void _UpdateThread(Image sourceImage, CropTransform transformation) {
      var srcWidth = sourceImage.Width;
      var srcHeight = sourceImage.Height;

      // only calculate actual shown pixels
      var downScaleFactor = (double)this.pictureBox.Width / srcWidth;
      var tgtWidth = (int)(srcWidth * downScaleFactor);
      var tgtHeight = (int)(srcHeight * downScaleFactor);

      var cropBorderRect = new Rectangle(
        (int)(transformation.Left * downScaleFactor),
        (int)(transformation.Top * downScaleFactor),
        (int)((srcWidth - transformation.Left - transformation.Right) * downScaleFactor),
        (int)((srcHeight - transformation.Top - transformation.Bottom) * downScaleFactor)
      );

      this._nextImageForPreview?.Dispose();
      var bitmap = this._nextImageForPreview = new Bitmap(tgtWidth, tgtHeight);

      using (var g = Graphics.FromImage(bitmap)) {

        g.DrawImage(
          sourceImage,
          new Rectangle(0, 0, tgtWidth, tgtHeight),
          0,
          0,
          srcWidth,
          srcHeight,
          GraphicsUnit.Pixel
        );

        g.FillRectangles(new SolidBrush(Color.FromArgb(64, 0, 0, 0)), new[] {
          Rectangle.FromLTRB(0,0,tgtWidth,cropBorderRect.Top),
          Rectangle.FromLTRB(0,cropBorderRect.Bottom,tgtWidth,tgtHeight),
          Rectangle.FromLTRB(0,cropBorderRect.Top,cropBorderRect.Left,cropBorderRect.Bottom),
          Rectangle.FromLTRB(cropBorderRect.Right,cropBorderRect.Top,tgtWidth,cropBorderRect.Bottom),
        });

        g.DrawRectangle(new Pen(SystemColors.HotTrack, 2.0f), cropBorderRect);
      }

      this.Invoke(new MethodInvoker(
        () => {
          this._nextImageForPreview = null;
          this._CurrentPreviewImage = bitmap;
        }));

    }


    private Thread _updateThread;
    private Bitmap _nextImageForPreview;

    private void _UpdatePreviewBox() {

      Thread thread = null;
      thread = new Thread(
        () => {
          try {
            this._UpdateThread(this._imageForBackgroundOperations, this.CropTransform);
          } finally {
            // ReSharper disable once AccessToModifiedClosure
            Interlocked.CompareExchange(ref this._updateThread, null, thread);
          }
        }) { IsBackground = true, Name = "CropPreview" };

      var oldThread = Interlocked.CompareExchange(ref this._updateThread, thread, null);
      if (oldThread == null) {

        // start new thread if none yet running
        thread.Start();
      } else {

        // abort existing thread and retry
        oldThread.Abort();
        oldThread.Join();
        this._updateThread = null;
        this._UpdatePreviewBox();
      }
    }

    private void btnCancel_Click(object sender, EventArgs e) {
      this.Close();
    }

    private void btnOK_Click(object sender, EventArgs e) {
      if (!this.CropTransform.IsNull) {
        if (this._TransformMultiple) {
          // With multiple images, we need to have the transform scaled in case they're different sizes
          using (var referenceBitmap = this.Image.GetImage()) {
            foreach (var img in this._ImagesToTransform) {
              img.AddTransform(this._ScaleCropTransform(img, referenceBitmap));
              img.SetThumbnail(this.thumbnailRenderer.RenderThumbnail(img));
            }
          }
        } else {
          this.Image.AddTransform(this.CropTransform);
          this.Image.SetThumbnail(this.thumbnailRenderer.RenderThumbnail(this.Image));
        }
        this.changeTracker.HasUnsavedChanges = true;
      }
      this.Close();
    }

    private CropTransform _ScaleCropTransform(ScannedImage img, Bitmap referenceBitmap) {
      using (var bitmap = img.GetImage()) {
        double xScale = bitmap.Width / (double)referenceBitmap.Width,
          yScale = bitmap.Height / (double)referenceBitmap.Height;
        return new CropTransform {
          Left = (int)Math.Round(this.CropTransform.Left * xScale),
          Right = (int)Math.Round(this.CropTransform.Right * xScale),
          Top = (int)Math.Round(this.CropTransform.Top * yScale),
          Bottom = (int)Math.Round(this.CropTransform.Bottom * yScale)
        };
      }
    }

    private void btnRevert_Click(object sender, EventArgs e) {
      this.CropTransform = new CropTransform();
      this._UpdatePreviewBox();
    }

    private void tbLeft_Scroll(object sender, EventArgs e) {
      this._UpdateTransform();
    }

    private void tbRight_Scroll(object sender, EventArgs e) {
      this._UpdateTransform();
    }

    private void tbBottom_Scroll(object sender, EventArgs e) {
      this._UpdateTransform();
    }

    private void tbTop_Scroll(object sender, EventArgs e) {
      this._UpdateTransform();
    }

    private void FCrop_FormClosed(object sender, FormClosedEventArgs e) {
      this._workingImage.Dispose();
      this._imageForBackgroundOperations.Dispose();
      this._CurrentPreviewImage = null;
      this._updateThread?.Abort();
      this._nextImageForPreview?.Dispose();
    }

    private Point dragStartCoords;

    private void pictureBox_MouseDown(object sender, MouseEventArgs e) {
      this.dragStartCoords = this._TranslatePboxCoords(e.Location);
    }

    private void pictureBox_MouseMove(object sender, MouseEventArgs e) {
      if (e.Button == MouseButtons.Left) {
        var dragEndCoords = this._TranslatePboxCoords(e.Location);

        var oldCoordinates = new {
          left=this.tbLeft.Value,
          top=this.tbTop.Value,
          right=this.tbRight.Value,
          bottom=this.tbBottom.Value
        };

        if (dragEndCoords.X > this.dragStartCoords.X) {
          this.tbLeft.Value = this.dragStartCoords.X;
          this.tbRight.Value = dragEndCoords.X;
        } else {
          this.tbLeft.Value = dragEndCoords.X;
          this.tbRight.Value = this.dragStartCoords.X;
        }
        if (dragEndCoords.Y > this.dragStartCoords.Y) {
          this.tbTop.Value = this._workingImage.Height - this.dragStartCoords.Y;
          this.tbBottom.Value = this._workingImage.Height - dragEndCoords.Y;
        } else {
          this.tbTop.Value = this._workingImage.Height - dragEndCoords.Y;
          this.tbBottom.Value = this._workingImage.Height - this.dragStartCoords.Y;
        }

        if(
          this.tbLeft.Value!=oldCoordinates.left
          || this.tbTop.Value != oldCoordinates.top
          || this.tbRight.Value != oldCoordinates.right
          || this.tbBottom.Value != oldCoordinates.bottom
        )
        this._UpdateTransform();
      }
    }

    private Point _TranslatePboxCoords(Point point) {
      double px = point.X - 1;
      double py = point.Y - 1;
      var imageAspect = this._workingImage.Width / (double)this._workingImage.Height;
      double pboxWidth = (this.pictureBox.Width - 2);
      double pboxHeight = (this.pictureBox.Height - 2);
      var pboxAspect = pboxWidth / pboxHeight;
      if (pboxAspect > imageAspect) {
        // Empty space on left/right
        var emptyWidth = ((1 - imageAspect / pboxAspect) / 2 * pboxWidth);
        px = (pboxAspect / imageAspect * (px - emptyWidth));
      } else {
        // Empty space on top/bottom
        var emptyHeight = ((1 - pboxAspect / imageAspect) / 2 * pboxHeight);
        py = (imageAspect / pboxAspect * (py - emptyHeight));
      }
      var x = px / pboxWidth * this._workingImage.Width;
      var y = py / pboxHeight * this._workingImage.Height;
      x = Math.Max(Math.Min(x, this._workingImage.Width), 0);
      y = Math.Max(Math.Min(y, this._workingImage.Height), 0);
      return new Point((int)Math.Round(x), (int)Math.Round(y));
    }
  }
}
