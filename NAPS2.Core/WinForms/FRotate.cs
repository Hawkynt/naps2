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
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Hawkynt;
using NAPS2.Scan.Images;
using NAPS2.Scan.Images.Transforms;
using NAPS2.Util;
using Timer = System.Threading.Timer;

namespace NAPS2.WinForms {
  partial class FRotate : FormBase {
    private const double ROTATION_FACTOR = 100;

    private readonly ChangeTracker changeTracker;
    private readonly ThumbnailRenderer thumbnailRenderer;

    private Bitmap workingImage;
    private bool previewOutOfDate;
    private bool working;
    private Timer previewTimer;

    public FRotate(ChangeTracker changeTracker, ThumbnailRenderer thumbnailRenderer) {
      this.changeTracker = changeTracker;
      this.thumbnailRenderer = thumbnailRenderer;
      this.InitializeComponent();

      this.RotationTransform = new RotationTransform();
    }

    public ScannedImage Image { get; set; }

    public List<ScannedImage> SelectedImages { get; set; }

    public RotationTransform RotationTransform { get; private set; }

    private IEnumerable<ScannedImage> _ImagesToTransform => this.SelectedImages != null && this.checkboxApplyToSelected.Checked ? this.SelectedImages : Enumerable.Repeat(this.Image, 1);

    protected override void OnLoad(object sender, EventArgs eventArgs) {
      if (this.SelectedImages != null && this.SelectedImages.Count > 1) {
        this.checkboxApplyToSelected.Text = string.Format(this.checkboxApplyToSelected.Text, this.SelectedImages.Count);
      } else {
        ConditionalControls.Hide(this.checkboxApplyToSelected, 6);
      }

      new LayoutManager(this)
        .Bind(this.tbAngle, this.pictureBox)
        .WidthToForm()
        .Bind(this.pictureBox)
        .HeightToForm()
        .Bind(this.btnOK, this.btnCancel, this.txtAngle)
        .RightToForm()
        .Bind(this.tbAngle, this.txtAngle, this.checkboxApplyToSelected, this.btnRevert, this.btnOK, this.btnCancel)
        .BottomToForm()
        .Activate();
      this.Size = new Size(600, 600);

      this.workingImage = this.Image.GetImage();
      this.pictureBox.Image = (Bitmap)this.workingImage.Clone();
      var skewAngle = (int)Math.Round(DeskewImage.GetSkewAngle(this.workingImage) * ROTATION_FACTOR);
      if (skewAngle >= this.tbAngle.Minimum && skewAngle <= this.tbAngle.Maximum)
        this.tbAngle.Value = skewAngle;

      this.tbAngle_Scroll(this, null);
    }

    private void _UpdateTransform() {
      this.RotationTransform.Angle = this.tbAngle.Value / ROTATION_FACTOR;
      this._UpdatePreviewBox();
    }

    private void _UpdatePreviewBox() {
      if (this.previewTimer == null) {
        this.previewTimer = new Timer(
          obj => {
            if (this.previewOutOfDate && !this.working) {
              this.working = true;
              this.previewOutOfDate = false;
              var result = this.RotationTransform.Perform((Bitmap)this.workingImage.Clone());
              this.Invoke(
                new MethodInvoker(
                  () => {
                    this.pictureBox.Image?.Dispose();
                    this.pictureBox.Image = result;
                  }));
              this.working = false;
            }
          },
          null,
          0,
          100);
      }
      this.previewOutOfDate = true;
    }

    private void btnCancel_Click(object sender, EventArgs e) {
      this.Close();
    }

    private void btnOK_Click(object sender, EventArgs e) {
      if (!this.RotationTransform.IsNull) {
        foreach (var img in this._ImagesToTransform) {
          img.AddTransform(this.RotationTransform);
          img.SetThumbnail(this.thumbnailRenderer.RenderThumbnail(img));
        }
        this.changeTracker.HasUnsavedChanges = true;
      }
      this.Close();
    }

    private void btnRevert_Click(object sender, EventArgs e) {
      this.RotationTransform = new RotationTransform();
      this.tbAngle.Value = 0;
      this.txtAngle.Text = (this.tbAngle.Value / ROTATION_FACTOR).ToString("G");
      this._UpdatePreviewBox();
    }

    private void FRotate_FormClosed(object sender, FormClosedEventArgs e) {
      this.workingImage.Dispose();
      this.pictureBox.Image?.Dispose();
      this.previewTimer?.Dispose();
    }

    private void txtAngle_TextChanged(object sender, EventArgs e) {
      double valueDouble;
      if (double.TryParse(this.txtAngle.Text.Replace(DEGREE_SIGN.ToString(CultureInfo.InvariantCulture), ""), out valueDouble)) {
        var value = (int)Math.Round(valueDouble * ROTATION_FACTOR);
        if (value >= this.tbAngle.Minimum && value <= this.tbAngle.Maximum) {
          this.tbAngle.Value = value;
        }
        if (!this.txtAngle.Text.Contains(DEGREE_SIGN)) {
          this.txtAngle.Text += DEGREE_SIGN;
        }
      }
      this._UpdateTransform();
    }

    private void tbAngle_Scroll(object sender, EventArgs e) {
      this.txtAngle.Text = (this.tbAngle.Value / ROTATION_FACTOR).ToString("G") + DEGREE_SIGN;
      this._UpdateTransform();
    }

    private bool guideExists;
    private Point guideStart, guideEnd;

    private const int MIN_LINE_DISTANCE = 50;
    private const float LINE_PEN_SIZE = 1;
    private const char DEGREE_SIGN = '\u00B0';

    private void pictureBox_MouseDown(object sender, MouseEventArgs e) {
      this.guideExists = true;
      this.guideStart = this.guideEnd = e.Location;
      this.pictureBox.Invalidate();
    }

    private void pictureBox_MouseUp(object sender, MouseEventArgs e) {
      this.guideExists = false;
      var dx = this.guideEnd.X - this.guideStart.X;
      var dy = this.guideEnd.Y - this.guideStart.Y;
      var distance = Math.Sqrt(dx * dx + dy * dy);
      if (distance > MIN_LINE_DISTANCE) {
        var angle = -Math.Atan2(dy, dx) * 180.0 / Math.PI;
        while (angle > 45.0) {
          angle -= 90.0;
        }
        while (angle < -45.0) {
          angle += 90.0;
        }
        var oldAngle = this.tbAngle.Value / ROTATION_FACTOR;
        var newAngle = angle + oldAngle;
        while (newAngle > 180.0) {
          newAngle -= 360.0;
        }
        while (newAngle < -180.0) {
          newAngle += 360.0;
        }
        this.tbAngle.Value = (int)Math.Round(newAngle * ROTATION_FACTOR);
        this.tbAngle_Scroll(null, null);
      }
      this.pictureBox.Invalidate();
    }

    private void pictureBox_MouseMove(object sender, MouseEventArgs e) {
      this.guideEnd = e.Location;
      this.pictureBox.Invalidate();
    }

    private void pictureBox_Paint(object sender, PaintEventArgs e) {
      if (!this.guideExists)
        return;

      var old = e.Graphics.SmoothingMode;
      e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
      e.Graphics.DrawLine(new Pen(Color.Black, LINE_PEN_SIZE), this.guideStart, this.guideEnd);
      e.Graphics.SmoothingMode = old;
    }
  }
}
