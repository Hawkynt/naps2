using System;
using System.Drawing;

namespace NAPS2.Scan.Images.Transforms {
  [Serializable]
  public class AutoContrastTransform : Transform {
    public override Bitmap Perform(Bitmap bitmap) => Hawkynt.AutoContrastImage.ApplyAutoContrast(bitmap);
  }
}
