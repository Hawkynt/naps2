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
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using NAPS2.Config;
using NAPS2.Lang.Resources;
using NAPS2.Scan;
using NAPS2.Scan.Exceptions;
using NAPS2.Scan.Twain;
using NAPS2.Scan.Wia;
using NAPS2.Util;

namespace NAPS2.WinForms {
  public partial class FEditProfile : FormBase {
    private readonly IScanDriverFactory driverFactory;
    private readonly IErrorOutput errorOutput;
    private readonly ProfileNameTracker profileNameTracker;
    private readonly AppConfigManager appConfigManager;

    private ScanProfile scanProfile;
    private ScanDevice currentDevice;
    private bool isDefault;

    private int iconID;

    private bool suppressChangeEvent;

    public FEditProfile(
      IScanDriverFactory driverFactory,
      IErrorOutput errorOutput,
      ProfileNameTracker profileNameTracker,
      AppConfigManager appConfigManager) {
      this.driverFactory = driverFactory;
      this.errorOutput = errorOutput;
      this.profileNameTracker = profileNameTracker;
      this.appConfigManager = appConfigManager;
      this.InitializeComponent();
      this.AddEnumItems<ScanHorizontalAlign>(this.cmbAlign);
      this.AddEnumItems<ScanBitDepth>(this.cmbDepth);
      this.AddEnumItems<ScanDpi>(this.cmbResolution);
      this.AddEnumItems<ScanScale>(this.cmbScale);
      this.AddEnumItems<ScanSource>(this.cmbSource);
      this.cmbPage.Format += (sender, e) => {
        var item = (PageSizeListItem)e.ListItem;
        e.Value = item.Label;
      };
    }

    protected override void OnLoad(object sender, EventArgs e) {
      // Don't trigger any onChange events
      this.suppressChangeEvent = true;

      this.pctIcon.Image = this.ilProfileIcons.IconsList.Images[this.ScanProfile.IconID];
      this.txtName.Text = this.ScanProfile.DisplayName;
      if (this.CurrentDevice == null) {
        this.CurrentDevice = this.ScanProfile.Device;
      }
      this.isDefault = this.ScanProfile.IsDefault;
      this.iconID = this.ScanProfile.IconID;

      this.cmbSource.SelectedIndex = (int)this.ScanProfile.PaperSource;
      this.cmbDepth.SelectedIndex = (int)this.ScanProfile.BitDepth;
      this.cmbResolution.SelectedIndex = (int)this.ScanProfile.Resolution;
      this.txtContrast.Text = this.ScanProfile.Contrast.ToString("G");
      this.txtBrightness.Text = this.ScanProfile.Brightness.ToString("G");
      this.UpdatePageSizeList();
      this.SelectPageSize();
      this.cmbScale.SelectedIndex = (int)this.ScanProfile.AfterScanScale;
      this.cmbAlign.SelectedIndex = (int)this.ScanProfile.PageAlign;

      this.cbAutoSave.Checked = this.ScanProfile.EnableAutoSave;
      this.cbAutoRotate.Checked = this.ScanProfile.EnableAutoRotate;

      // The setter updates the driver selection checkboxes
      this.DeviceDriverName = this.ScanProfile.DriverName;

      this.rdbNative.Checked = this.ScanProfile.UseNativeUI;
      this.rdbConfig.Checked = !this.ScanProfile.UseNativeUI;

      // Start triggering onChange events again
      this.suppressChangeEvent = false;

      this.UpdateEnabledControls();

      this.linkAutoSaveSettings.Location = new Point(this.cbAutoSave.Right, this.linkAutoSaveSettings.Location.Y);
      new LayoutManager(this)
        .Bind(this.txtName, this.txtDevice, this.panel1, this.panel2)
        .WidthToForm()
        .Bind(this.pctIcon, this.btnChooseDevice, this.btnOK, this.btnCancel)
        .RightToForm()
        .Bind(this.cmbAlign, this.cmbDepth, this.cmbPage, this.cmbResolution, this.cmbScale, this.cmbSource, this.trBrightness, this.trContrast, this.rdbConfig, this.rdbNative)
        .WidthTo(() => this.Width / 2)
        .Bind(this.rdTWAIN, this.rdbNative, this.label3, this.cmbDepth, this.label9, this.cmbAlign, this.label10, this.cmbScale, this.label7, this.trContrast)
        .LeftTo(() => this.Width / 2)
        .Bind(this.txtBrightness)
        .LeftTo(() => this.trBrightness.Right)
        .Bind(this.txtContrast)
        .LeftTo(() => this.trContrast.Right)
        .Activate();
    }

    private void UpdatePageSizeList() {
      this.cmbPage.Items.Clear();

      // Defaults
      foreach (ScanPageSize item in Enum.GetValues(typeof(ScanPageSize))) {
        this.cmbPage.Items.Add(
          new PageSizeListItem {
            Type = item,
            Label = item.Description()
          });
      }

      // Custom Presets
      foreach (var preset in this.UserConfigManager.Config.CustomPageSizePresets.OrderBy(x => x.Name)) {
        this.cmbPage.Items.Insert(this.cmbPage.Items.Count - 1,
          new PageSizeListItem {
            Type = ScanPageSize.Custom,
            Label =
              string.Format(
                MiscResources.NamedPageSizeFormat,
                preset.Name,
                preset.Dimens.Width,
                preset.Dimens.Height,
                preset.Dimens.Unit.Description()),
            CustomName = preset.Name,
            CustomDimens = preset.Dimens
          });
      }
    }

    private void SelectPageSize() {
      if (this.ScanProfile.PageSize == ScanPageSize.Custom) {
        this.SelectCustomPageSize(this.ScanProfile.CustomPageSizeName, this.ScanProfile.CustomPageSize);
      } else {
        this.cmbPage.SelectedIndex = (int)this.ScanProfile.PageSize;
      }
    }

    private void SelectCustomPageSize(string name, PageDimensions dimens) {
      for (var i = 0; i < this.cmbPage.Items.Count; i++) {
        var item = (PageSizeListItem)this.cmbPage.Items[i];
        if (item.Type == ScanPageSize.Custom && item.CustomName == name && item.CustomDimens == dimens) {
          this.cmbPage.SelectedIndex = i;
          return;
        }
      }

      // Not found, so insert a new item
      this.cmbPage.Items.Insert(this.cmbPage.Items.Count - 1,
        new PageSizeListItem {
          Type = ScanPageSize.Custom,
          Label = string.IsNullOrEmpty(name)
            ? string.Format(MiscResources.CustomPageSizeFormat, dimens.Width, dimens.Height, dimens.Unit.Description())
            : string.Format(
              MiscResources.NamedPageSizeFormat,
              name,
              dimens.Width,
              dimens.Height,
              dimens.Unit.Description()),
          CustomName = name,
          CustomDimens = dimens
        });
      this.cmbPage.SelectedIndex = this.cmbPage.Items.Count - 2;
    }

    public bool Result { get; private set; }

    public ScanProfile ScanProfile {
      get { return this.scanProfile; }
      set { this.scanProfile = value.Clone(); }
    }

    private string DeviceDriverName {
      get { return this.rdTWAIN.Checked ? TwainScanDriver.DRIVER_NAME : WiaScanDriver.DRIVER_NAME; }
      set {
        if (value == TwainScanDriver.DRIVER_NAME) {
          this.rdTWAIN.Checked = true;
        } else {
          this.rdWIA.Checked = true;
        }
      }
    }

    public ScanDevice CurrentDevice {
      get { return this.currentDevice; }
      set {
        this.currentDevice = value;
        this.txtDevice.Text = (value == null ? "" : value.Name);
      }
    }

    private void ChooseDevice(string driverName) {
      var driver = this.driverFactory.Create(driverName);
      try {
        driver.DialogParent = this;
        driver.ScanProfile = this.ScanProfile;
        var device = driver.PromptForDevice();
        if (device != null) {
          if (string.IsNullOrEmpty(this.txtName.Text) || this.CurrentDevice != null && this.CurrentDevice.Name == this.txtName.Text) {
            this.txtName.Text = device.Name;
          }
          this.CurrentDevice = device;
        }
      } catch (ScanDriverException e) {
        if (e is ScanDriverUnknownException) {
          Log.ErrorException(e.Message, e.InnerException);
          this.errorOutput.DisplayError(e.Message, e);
        } else {
          this.errorOutput.DisplayError(e.Message);
        }
      }
    }

    private void btnChooseDevice_Click(object sender, EventArgs e) {
      this.ChooseDevice(this.DeviceDriverName);
    }

    private void SaveSettings() {
      if (this.ScanProfile.IsLocked) {
        if (!this.ScanProfile.IsDeviceLocked) {
          this.ScanProfile.Device = this.CurrentDevice;
        }
        return;
      }
      var pageSize = (PageSizeListItem)this.cmbPage.SelectedItem;
      if (this.ScanProfile.DisplayName != null) {
        this.profileNameTracker.RenamingProfile(this.ScanProfile.DisplayName, this.txtName.Text);
      }
      this.scanProfile = new ScanProfile {
        Version = ScanProfile.CURRENT_VERSION,
        Device = this.CurrentDevice,
        IsDefault = this.isDefault,
        DriverName = this.DeviceDriverName,
        DisplayName = this.txtName.Text,
        IconID = this.iconID,
        MaxQuality = this.ScanProfile.MaxQuality,
        UseNativeUI = this.rdbNative.Checked,
        AfterScanScale = (ScanScale)this.cmbScale.SelectedIndex,
        BitDepth = (ScanBitDepth)this.cmbDepth.SelectedIndex,
        Brightness = this.trBrightness.Value,
        Contrast = this.trContrast.Value,
        PageAlign = (ScanHorizontalAlign)this.cmbAlign.SelectedIndex,
        PageSize = pageSize.Type,
        CustomPageSizeName = pageSize.CustomName,
        CustomPageSize = pageSize.CustomDimens,
        Resolution = (ScanDpi)this.cmbResolution.SelectedIndex,
        PaperSource = (ScanSource)this.cmbSource.SelectedIndex,
        EnableAutoSave = this.cbAutoSave.Checked,
        EnableAutoRotate = this.cbAutoRotate.Checked,
        AutoSaveSettings = this.ScanProfile.AutoSaveSettings,
        Quality = this.ScanProfile.Quality,
        BrightnessContrastAfterScan = this.ScanProfile.BrightnessContrastAfterScan,
        WiaOffsetWidth = this.ScanProfile.WiaOffsetWidth,
        ForcePageSize = this.ScanProfile.ForcePageSize,
        FlipDuplexedPages = this.ScanProfile.FlipDuplexedPages,
        TwainImpl = this.ScanProfile.TwainImpl,
        ExcludeBlankPages = this.ScanProfile.ExcludeBlankPages,
        BlankPageWhiteThreshold = this.ScanProfile.BlankPageWhiteThreshold,
        BlankPageCoverageThreshold = this.ScanProfile.BlankPageCoverageThreshold
      };
    }

    private void btnOK_Click(object sender, EventArgs e) {
      // Note: If CurrentDevice is null, that's fine. A prompt will be shown when scanning.

      if (this.txtName.Text == "") {
        this.errorOutput.DisplayError(MiscResources.NameMissing);
        return;
      }
      this.Result = true;
      this.SaveSettings();
      this.Close();
    }

    private void btnCancel_Click(object sender, EventArgs e) {
      this.Close();
    }

    private void rdbConfig_CheckedChanged(object sender, EventArgs e) {
      this.UpdateEnabledControls();
    }

    private void rdbNativeWIA_CheckedChanged(object sender, EventArgs e) {
      this.UpdateEnabledControls();
    }

    private void UpdateEnabledControls() {
      if (!this.suppressChangeEvent) {
        this.suppressChangeEvent = true;

        var locked = this.ScanProfile.IsLocked;
        var deviceLocked = this.ScanProfile.IsDeviceLocked;
        var settingsEnabled = !locked && this.rdbConfig.Checked;

        this.txtName.Enabled = !locked;
        this.rdWIA.Enabled = this.rdTWAIN.Enabled = !locked;
        this.txtDevice.Enabled = !deviceLocked;
        this.btnChooseDevice.Enabled = !deviceLocked;
        this.rdbConfig.Enabled = this.rdbNative.Enabled = !locked;

        this.cmbSource.Enabled = settingsEnabled;
        this.cmbResolution.Enabled = settingsEnabled;
        this.cmbPage.Enabled = settingsEnabled;
        this.cmbDepth.Enabled = settingsEnabled;
        this.cmbAlign.Enabled = settingsEnabled;
        this.cmbScale.Enabled = settingsEnabled;
        this.trBrightness.Enabled = settingsEnabled;
        this.trContrast.Enabled = settingsEnabled;
        this.txtBrightness.Enabled = settingsEnabled;
        this.txtContrast.Enabled = settingsEnabled;

        this.cbAutoSave.Enabled = !locked && !this.appConfigManager.Config.DisableAutoSave;
        this.linkAutoSaveSettings.Visible = !locked && !this.appConfigManager.Config.DisableAutoSave;

        this.btnAdvanced.Enabled = !locked;

        this.suppressChangeEvent = false;
      }
    }

    private void rdWIA_CheckedChanged(object sender, EventArgs e) {
      if (!this.suppressChangeEvent) {
        this.ScanProfile.Device = null;
        this.CurrentDevice = null;
        this.UpdateEnabledControls();
      }
    }

    private void txtBrightness_TextChanged(object sender, EventArgs e) {
      int value;
      if (int.TryParse(this.txtBrightness.Text, out value)) {
        if (value >= this.trBrightness.Minimum && value <= this.trBrightness.Maximum) {
          this.trBrightness.Value = value;
        }
      }
    }

    private void trBrightness_Scroll(object sender, EventArgs e) {
      this.txtBrightness.Text = this.trBrightness.Value.ToString("G");
    }

    private void txtContrast_TextChanged(object sender, EventArgs e) {
      int value;
      if (int.TryParse(this.txtContrast.Text, out value)) {
        if (value >= this.trContrast.Minimum && value <= this.trContrast.Maximum) {
          this.trContrast.Value = value;
        }
      }
    }

    private void trContrast_Scroll(object sender, EventArgs e) {
      this.txtContrast.Text = this.trContrast.Value.ToString("G");
    }

    private int lastPageSizeIndex = -1;
    private PageSizeListItem lastPageSizeItem;

    private void cmbPage_SelectedIndexChanged(object sender, EventArgs e) {
      if (this.cmbPage.SelectedIndex == this.cmbPage.Items.Count - 1) {
        // "Custom..." selected
        var form = this.FormFactory.Create<FPageSize>();
        form.PageSizeDimens = this.lastPageSizeItem.Type == ScanPageSize.Custom
          ? this.lastPageSizeItem.CustomDimens
          : this.lastPageSizeItem.Type.PageDimensions();
        if (form.ShowDialog() == DialogResult.OK) {
          this.UpdatePageSizeList();
          this.SelectCustomPageSize(form.PageSizeName, form.PageSizeDimens);
        } else {
          this.cmbPage.SelectedIndex = this.lastPageSizeIndex;
        }
      }
      this.lastPageSizeIndex = this.cmbPage.SelectedIndex;
      this.lastPageSizeItem = (PageSizeListItem)this.cmbPage.SelectedItem;
    }

    private void linkAutoSaveSettings_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e) {
      if (this.appConfigManager.Config.DisableAutoSave) {
        return;
      }
      var form = this.FormFactory.Create<FAutoSaveSettings>();
      this.ScanProfile.DriverName = this.DeviceDriverName;
      form.ScanProfile = this.ScanProfile;
      form.ShowDialog();
    }

    private void btnAdvanced_Click(object sender, EventArgs e) {
      var form = this.FormFactory.Create<FAdvancedScanSettings>();
      this.ScanProfile.DriverName = this.DeviceDriverName;
      this.ScanProfile.BitDepth = (ScanBitDepth)this.cmbDepth.SelectedIndex;
      form.ScanProfile = this.ScanProfile;
      form.ShowDialog();
    }

    private void cbAutoSave_CheckedChanged(object sender, EventArgs e) {
      if (!this.suppressChangeEvent) {
        if (this.cbAutoSave.Checked) {
          this.linkAutoSaveSettings.Enabled = true;
          var form = this.FormFactory.Create<FAutoSaveSettings>();
          form.ScanProfile = this.ScanProfile;
          form.ShowDialog();
          if (!form.Result) {
            this.cbAutoSave.Checked = false;
          }
        }
      }
      this.linkAutoSaveSettings.Enabled = this.cbAutoSave.Checked;
    }

    private void txtDevice_KeyDown(object sender, KeyEventArgs e) {
      if (e.KeyCode == Keys.Delete) {
        this.CurrentDevice = null;
      }
    }

    private class PageSizeListItem {
      public string Label { get; set; }

      public ScanPageSize Type { get; set; }

      public string CustomName { get; set; }

      public PageDimensions CustomDimens { get; set; }
    }
  }
}