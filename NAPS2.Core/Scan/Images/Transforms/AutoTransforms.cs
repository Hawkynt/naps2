using System;
using System.Drawing;

namespace NAPS2.Scan.Images.Transforms {
  [Serializable]
  public class AutoRGBTransform : Transform {
    public override Bitmap Perform(Bitmap bitmap) => Hawkynt.AutoContrastImage.ApplyAutoRGB(bitmap);
  }

  [Serializable]
  public class AutoLuminanceTransform : Transform {
    public override Bitmap Perform(Bitmap bitmap) => Hawkynt.AutoContrastImage.ApplyAutoLuminance(bitmap);
  }

  [Serializable]
  public class AutoSaturationTransform : Transform {
    public override Bitmap Perform(Bitmap bitmap) => Hawkynt.AutoContrastImage.ApplyAutoSaturation(bitmap);
  }

}
