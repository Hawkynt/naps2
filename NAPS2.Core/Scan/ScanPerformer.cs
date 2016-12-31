﻿/*
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
using System.Linq;
using System.Windows.Forms;
using NAPS2.Config;
using NAPS2.ImportExport;
using NAPS2.Scan.Exceptions;
using NAPS2.Scan.Images;
using NAPS2.Scan.Images.Transforms;
using NAPS2.Util;

namespace NAPS2.Scan {
  public class ScanPerformer : IScanPerformer {
    private readonly IScanDriverFactory driverFactory;
    private readonly IErrorOutput errorOutput;
    private readonly IAutoSave autoSave;
    private readonly AppConfigManager appConfigManager;
    private readonly IProfileManager profileManager;

    public ScanPerformer(
      IScanDriverFactory driverFactory,
      IErrorOutput errorOutput,
      IAutoSave autoSave,
      AppConfigManager appConfigManager,
      IProfileManager profileManager) {
      this.driverFactory = driverFactory;
      this.errorOutput = errorOutput;
      this.autoSave = autoSave;
      this.appConfigManager = appConfigManager;
      this.profileManager = profileManager;
    }

    public void PerformScan(
      ScanProfile scanProfile,
      ScanParams scanParams,
      IWin32Window dialogParent,
      ISaveNotify notify,
      Action<ScannedImage> imageCallback) {
      var driver = this.driverFactory.Create(scanProfile.DriverName);
      driver.DialogParent = dialogParent;
      driver.ScanProfile = scanProfile;
      driver.ScanParams = scanParams;
      try {
        if (scanProfile.Device == null) {
          // The profile has no device specified, so prompt the user to choose one
          var device = driver.PromptForDevice();
          if (device == null) {
            // User cancelled
            return;
          }
          if (this.appConfigManager.Config.AlwaysRememberDevice) {
            scanProfile.Device = device;
            this.profileManager.Save();
          }
          driver.ScanDevice = device;
        } else {
          // The profile has a device specified, so use it
          driver.ScanDevice = scanProfile.Device;
        }

        var doAutoSave = !scanParams.NoAutoSave && !this.appConfigManager.Config.DisableAutoSave &&
                         scanProfile.EnableAutoSave && scanProfile.AutoSaveSettings != null;
        if (doAutoSave) {
          if (scanProfile.AutoSaveSettings.ClearImagesAfterSaving) {

            // Auto save without piping images
            var images = driver.Scan().ToList();

            // auto-rotate
            if (scanProfile.EnableAutoRotate)
              foreach (var image in images)
                using (var bitmap = image.GetImage())
                  image.AddTransform(new RotationTransform { Angle = Hawkynt.DeskewImage.GetSkewAngle(bitmap) });

            if (this.autoSave.Save(scanProfile.AutoSaveSettings, images, notify)) {
              foreach (var img in images) {
                img.Dispose();
              }
            } else {
              // Fallback in case auto save failed; pipe all the images back at once
              foreach (var img in images) {
                imageCallback(img);
              }
            }
          } else {
            // Basic auto save, so keep track of images as we pipe them and try to auto save afterwards
            var images = new List<ScannedImage>();
            foreach (var scannedImage in driver.Scan()) {

              // auto-rotate
              if (scanProfile.EnableAutoRotate)
                using (var bitmap = scannedImage.GetImage())
                  scannedImage.AddTransform(new RotationTransform { Angle = Hawkynt.DeskewImage.GetSkewAngle(bitmap) });

              imageCallback(scannedImage);
              images.Add(scannedImage);
            }
            this.autoSave.Save(scanProfile.AutoSaveSettings, images, notify);
          }
        } else {
          // No auto save, so just pipe images back as we get them
          foreach (var scannedImage in driver.Scan()) {

            // auto-rotate
            if (scanProfile.EnableAutoRotate)
              using (var bitmap = scannedImage.GetImage()) {
                var skewAngle = Hawkynt.DeskewImage.GetSkewAngle(bitmap);
                if (skewAngle >= -45 && skewAngle <= 45)
                  scannedImage.AddTransform(new RotationTransform { Angle = skewAngle });
              }

            imageCallback(scannedImage);
          }

        }
      } catch (ScanDriverException e) {

        if (e is ScanDriverUnknownException) {
          Log.ErrorException(e.Message, e.InnerException);
          this.errorOutput.DisplayError(e.Message, e);
        } else
          this.errorOutput.DisplayError(e.Message);
      }
    }
  }
}