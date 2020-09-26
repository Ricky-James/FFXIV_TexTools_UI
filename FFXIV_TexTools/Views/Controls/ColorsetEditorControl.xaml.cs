﻿using FFXIV_TexTools.Helpers;
using FFXIV_TexTools.Resources;
using FFXIV_TexTools.ViewModels;
using HelixToolkit.Wpf.SharpDX;
using Newtonsoft.Json;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using xivModdingFramework.Cache;
using xivModdingFramework.Helpers;
using xivModdingFramework.Materials.DataContainers;
using xivModdingFramework.Materials.FileTypes;

namespace FFXIV_TexTools.Controls
{
    /// <summary>
    /// Interaction logic for ColorsetEditorControl.xaml
    /// </summary>
    public partial class ColorsetEditorControl : UserControl
    {
        List<Image> ColorSetRowImages = new List<Image>();
        StainingTemplateFile DyeTemplateFile;
        int RowId = 0;

        ColorsetEditorViewModel _vm;

        XivMtrl _mtrl;

        List<Half[]> RowData;

        private bool _LOADING = true;

        ObservableCollection<KeyValuePair<ushort, string>> DyeTemplateCollection = new ObservableCollection<KeyValuePair<ushort, string>>();

        public ColorsetEditorControl()
        {
            this.DataContext = _vm = new ColorsetEditorViewModel(this);
            InitializeComponent();

            for (int i = 0; i < 16; i++)
            {
                var elem = new System.Windows.Controls.Image();
                elem.Height = 24;
                elem.Width = 192;
                elem.DataContext = i;
                ColorSetRowImages.Add(elem);

                var border = new Border();
                border.Child = elem;

                border.BorderThickness = new Thickness(0);

                ColorSetRowsPanel.Children.Add(border);


                elem.MouseLeftButtonDown += ColorsetRow_Clicked;
            }

            DyeTemplateIdBox.ItemsSource = DyeTemplateCollection;
            DyeTemplateIdBox.DisplayMemberPath = "Value";
            DyeTemplateIdBox.SelectedValuePath = "Key";

            // Binding handlers for any time the data is changed in the UI.
            DyeDiffuseBox.Checked += ValueChanged;
            DyeDiffuseBox.Unchecked += ValueChanged;
            DyeSpecularBox.Checked += ValueChanged;
            DyeSpecularBox.Unchecked += ValueChanged;
            DyeEmissiveBox.Checked += ValueChanged;
            DyeEmissiveBox.Unchecked += ValueChanged;
            DyeGlossBox.Checked += ValueChanged;
            DyeTileBox.Unchecked += ValueChanged;
            DyeTileBox.Unchecked += ValueChanged;
            DiffuseColorPicker.SelectedColorChanged += ValueChanged;
            SpecularColorPicker.SelectedColorChanged += ValueChanged;
            EmissiveColorPicker.SelectedColorChanged += ValueChanged;

            DiffuseSecondaryBox.TextChanged += ValueChanged;
            GlossBox.TextChanged += ValueChanged;

            TileIdBox.TextChanged += ValueChanged;
            TileSkewXBox.TextChanged += ValueChanged;
            TileSkewYBox.TextChanged += ValueChanged;
            TileCountXBox.TextChanged += ValueChanged;
            TileCountYBox.TextChanged += ValueChanged;

        }

        private void ValueChanged(object sender, RoutedEventArgs e)
        {
            UpdateRow();
        }

        private void ColorsetRow_Clicked(object sender, MouseButtonEventArgs e)
        {
            var selectedImage = (System.Windows.Controls.Image)e.Source;
            SelectedColorsetRowImage.Source = selectedImage.Source;
            var rowNumber = (int)selectedImage.DataContext;
            SetRow(rowNumber);
        }

        private List<Half[]> GetRowData(int row)
        {
            var offset = row * 16;
            var data = new List<Half[]>(4);
            for (int i = 0; i < 4; i++)
            {
                var arr = new Half[4];
                data.Add(arr);
                for (int z = 0; z < 4; z++)
                {
                    arr[z] = _mtrl.ColorSetData[offset];
                    offset++;
                }
            }
            return data;
        }

        private async Task SetRow(int rowNumber) {

            if (_mtrl == null) return;

            _LOADING = true;

            if (_mtrl.ColorSetDyeData == null || _mtrl.ColorSetDyeData.Length != 32)
            {
                _mtrl.ColorSetDyeData = new byte[32];
            }

            // Triggered when the user clicks on a Colorset row.
            DetailsGroupBox.Header = "Material - Colorset Row Editor - Row #" + (rowNumber + 1).ToString();
            RowId = rowNumber;
            RowData = GetRowData(RowId);

            var r = (byte)Math.Round(RowData[0][0] * 255);
            var g = (byte)Math.Round(RowData[0][1] * 255);
            var b = (byte)Math.Round(RowData[0][2] * 255);
            DiffuseColorPicker.SelectedColor = new System.Windows.Media.Color() { R = r, G = g, B = b, A = 255 };

            r = (byte)Math.Round(RowData[1][0] * 255);
            g = (byte)Math.Round(RowData[1][1] * 255);
            b = (byte)Math.Round(RowData[1][2] * 255);
            SpecularColorPicker.SelectedColor = new System.Windows.Media.Color() { R = r, G = g, B = b, A = 255 };

            r = (byte)Math.Round(RowData[2][0] * 255);
            g = (byte)Math.Round(RowData[2][1] * 255);
            b = (byte)Math.Round(RowData[2][2] * 255);
            EmissiveColorPicker.SelectedColor = new System.Windows.Media.Color() { R = r, G = g, B = b, A = 255 };

            DiffuseSecondaryBox.Text = RowData[0][3].ToString();
            GlossBox.Text = RowData[1][3].ToString();

            TileIdBox.Text = (Math.Floor(RowData[2][3] * 64)).ToString();

            TileCountXBox.Text = RowData[3][0].ToString();
            TileCountYBox.Text = RowData[3][3].ToString();
            TileSkewXBox.Text = RowData[3][1].ToString();
            TileSkewYBox.Text = RowData[3][2].ToString();

            ushort dyeData = 0;
            if (_mtrl.ColorSetDyeData.Length != 0) {
                dyeData = BitConverter.ToUInt16(_mtrl.ColorSetDyeData, rowNumber * 2);
            }

            if(dyeData == ushort.MaxValue)
            {
                dyeData = 0;
            }

            ushort dyeTemplateId = (ushort)(dyeData >> 5);
            DyeTemplateIdBox.SelectedValue = dyeTemplateId;

            DyeDiffuseBox.IsChecked = (dyeData & 0x01) > 0;
            DyeSpecularBox.IsChecked = (dyeData & 0x02) > 0;
            DyeEmissiveBox.IsChecked = (dyeData & 0x04) > 0;
            DyeTileBox.IsChecked = (dyeData & 0x08) > 0;
            DyeGlossBox.IsChecked = (dyeData & 0x10) > 0;

            foreach(var imgElem in ColorSetRowImages)
            {

                var border = (Border)imgElem.Parent;
                border.BorderThickness = new Thickness(0);
                border.BorderBrush = Brushes.Transparent;
            }

            var elem = (Border)ColorSetRowImages[RowId].Parent;
            elem.BorderThickness = new Thickness(2);
            elem.BorderBrush = Brushes.Black;

            UpdateDyeStatus();

            await UpdateViewport();

            _LOADING = false;
        }

        private async Task UpdateViewport()
        {
            await _vm.SetColorsetRow(RowId);
        }

        public async Task UpdateRowVisual(int rowId)
        {
            var pixels = new byte[4 * 4];
            for (int x = 0; x < 4; x++)
            {
                for (int y = 0; y < 4; y++)
                {
                    var offset = (y + (x * 4) + (rowId * 16));
                    var half = _mtrl.ColorSetData[offset];
                    var b = half * 255;
                    b = b > 255 ? 255 : b;
                    b = b < 0 ? 0 : b;

                    var bitoffset = x * 4;
                    if (y == 0)
                    {
                        bitoffset += 2;
                    }
                    else if (y == 1)
                    {
                        bitoffset += 1;
                    }
                    else if (y == 2)
                    {
                        bitoffset += 0;
                    }
                    else
                    {
                        bitoffset += 3;
                    }
                    pixels[bitoffset] = (byte)b;
                }
            }

            var multiplier = 8;
            var perRow = 4 * multiplier;

            var npixels = new byte[pixels.Length * multiplier * multiplier];
            for (int x = 0; x < npixels.Length; x += 4)
            {
                var px = x / 4;
                var col = px % perRow;
                var originalCol = col / multiplier;

                var originalOffset = (originalCol * 4);

                npixels[x] = pixels[originalOffset];
                npixels[x + 1] = pixels[originalOffset + 1];
                npixels[x + 2] = pixels[originalOffset + 2];
                npixels[x + 3] = pixels[originalOffset + 3];
            }

            ColorSetRowImages[rowId].Source = BitmapSource.Create(multiplier * 4, multiplier, 1, 1, PixelFormats.Bgra32, null, npixels, 16 * multiplier);

            if(RowId == rowId)
            {
                SelectedColorsetRowImage.Source = ColorSetRowImages[rowId].Source;
            }
        }

        public async Task SetMaterial(XivMtrl mtrl, int row = 0)
        {
            DyeTemplateFile = await STM.GetStainingTemplateFile(false);
            DyeTemplateCollection.Clear();

            var keys = DyeTemplateFile.GetKeys();
            DyeTemplateCollection.Add(new KeyValuePair<ushort, string>(0, "Undyable"));
            foreach (var key in keys)
            {
                DyeTemplateCollection.Add(new KeyValuePair<ushort, string>(key, key.ToString()));
            }

            if(CopiedRow == null)
            {
                PasteRowButton.IsEnabled = false;
            } else
            {
                PasteRowButton.IsEnabled = true;
            }


            _mtrl = mtrl;
            for (int i = 0; i < 16; i++)
            {
                await UpdateRowVisual(i);
            }

            await _vm.SetMaterial(_mtrl);
            await SetRow(row);
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var mw = MainWindow.GetMainWindow();
            await mw.LockUi();
            try
            {

                var mtrlLib = new Mtrl(XivCache.GameInfo.GameDirectory, IOUtil.GetDataFileFromPath(_mtrl.MTRLPath), XivCache.GameInfo.GameLanguage);

                var item = mw.GetSelectedItem();
                await mtrlLib.ImportMtrl(_mtrl, item, XivStrings.TexTools);
            }
            catch(Exception ex)
            {
                FlexibleMessageBox.Show("Unable to save Material.\n\nError: " + ex.Message, "Material Save Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                return;
            }
            finally
            {
                await mw.UnlockUi();
            }
        }

        private void UpdateDyeStatus()
        {
            var value = DyeTemplateIdBox.SelectedValue;
            if (value == null) return;

            var entry = DyeTemplateFile.GetTemplate((ushort)value);

            if(entry == null)
            {

                DyeDiffuseBox.IsChecked = false;
                DyeDiffuseBox.IsEnabled = false;
                DyeSpecularBox.IsChecked = false;
                DyeSpecularBox.IsEnabled = false;
                DyeEmissiveBox.IsChecked = false;
                DyeEmissiveBox.IsEnabled = false;
                DyeTileBox.IsChecked = false;
                DyeTileBox.IsEnabled = false;
                DyeGlossBox.IsChecked = false;
                DyeGlossBox.IsEnabled = false;
                return;
            }

            if (entry.DiffuseEntries.Count == 0)
            {
                DyeDiffuseBox.IsChecked = false;
                DyeDiffuseBox.IsEnabled = false;
            } else
            {
                DyeDiffuseBox.IsChecked = true;
                DyeDiffuseBox.IsEnabled = true;
            }

            if (entry.SpecularEntries.Count == 0)
            {
                DyeSpecularBox.IsChecked = false;
                DyeSpecularBox.IsEnabled = false;
            }
            else
            {
                DyeSpecularBox.IsChecked = true;
                DyeSpecularBox.IsEnabled = true;
            }

            if (entry.EmissiveEntries.Count == 0)
            {
                DyeEmissiveBox.IsChecked = false;
                DyeEmissiveBox.IsEnabled = false;
            }
            else
            {
                DyeEmissiveBox.IsChecked = true;
                DyeEmissiveBox.IsEnabled = true;
            }

            if (entry.TileMaterialEntries.Count == 0)
            {
                DyeTileBox.IsChecked = false;
                DyeTileBox.IsEnabled = false;
            }
            else
            {
                DyeTileBox.IsChecked = true;
                DyeTileBox.IsEnabled = true;
            }

            if (entry.GlossEntries.Count == 0)
            {
                DyeGlossBox.IsChecked = false;
                DyeGlossBox.IsEnabled = false;
            }
            else
            {
                DyeGlossBox.IsChecked = true;
                DyeGlossBox.IsEnabled = true;
            }
        }

        private void UpdateRow()
        {
            if (_mtrl == null) return;
            if (_mtrl.ColorSetData.Count == 0) return;
            if (_LOADING) return;

            try
            {
                RowData[0][0] = new Half(DiffuseColorPicker.SelectedColor.Value.R / 255.0f);
                RowData[0][1] = new Half(DiffuseColorPicker.SelectedColor.Value.G / 255.0f);
                RowData[0][2] = new Half(DiffuseColorPicker.SelectedColor.Value.B / 255.0f);
                
                var fl = 1.0f;
                float.TryParse(DiffuseSecondaryBox.Text, out fl);
                RowData[0][3] = new Half(fl);


                RowData[1][0] = new Half(SpecularColorPicker.SelectedColor.Value.R / 255.0f);
                RowData[1][1] = new Half(SpecularColorPicker.SelectedColor.Value.G / 255.0f);
                RowData[1][2] = new Half(SpecularColorPicker.SelectedColor.Value.B / 255.0f);

                fl = 1.0f;
                float.TryParse(GlossBox.Text, out fl);
                RowData[1][3] = new Half(fl);

                RowData[2][0] = new Half(EmissiveColorPicker.SelectedColor.Value.R / 255.0f);
                RowData[2][1] = new Half(EmissiveColorPicker.SelectedColor.Value.G / 255.0f);
                RowData[2][2] = new Half(EmissiveColorPicker.SelectedColor.Value.B / 255.0f);

                fl = 0.0f;
                float.TryParse(TileIdBox.Text, out fl);
                RowData[2][3] = new Half((float)((Math.Floor(fl) + 0.5f)/ 64.0f));

                fl = 16.0f;
                float.TryParse(TileCountXBox.Text, out fl);
                RowData[3][0] = new Half(fl);

                fl = 16.0f;
                float.TryParse(TileCountYBox.Text, out fl);
                RowData[3][3] = new Half(fl);

                fl = 0f;
                float.TryParse(TileSkewXBox.Text, out fl);
                RowData[3][1] = new Half(fl);

                fl = 0f;
                float.TryParse(TileSkewYBox.Text, out fl);
                RowData[3][2] = new Half(fl);

                var templateId= (ushort)DyeTemplateIdBox.SelectedValue;
                ushort modifier = (ushort)(templateId << 5);

                if (DyeDiffuseBox.IsChecked == true)
                {
                    modifier = (ushort)(modifier | 0x01);
                }
                if (DyeSpecularBox.IsChecked == true)
                {
                    modifier = (ushort)(modifier | 0x02);
                }
                if (DyeEmissiveBox.IsChecked == true)
                {
                    modifier = (ushort)(modifier | 0x04);
                }
                if (DyeTileBox.IsChecked == true)
                {
                    modifier = (ushort)(modifier | 0x08);
                }
                if (DyeGlossBox.IsChecked == true)
                {
                    modifier = (ushort)(modifier | 0x10);
                }

                if (_mtrl.ColorSetDyeData.Length != 32)
                {
                    _mtrl.ColorSetDyeData = new byte[32];
                }

                var offset = RowId * 2;

                var bytes = BitConverter.GetBytes(modifier);

                Array.Copy(bytes, 0, _mtrl.ColorSetDyeData, offset, 2);

                offset = RowId * 16;
                for(int x = 0; x < 4; x++)
                {
                    for(int y = 0; y < 4; y++)
                    {
                        _mtrl.ColorSetData[offset] = RowData[x][y];
                        offset++;
                    }
                }

                UpdateDyeStatus();
                UpdateRowVisual(RowId);
                _vm.SetColorsetRow(RowId);
            }
            catch(Exception ex)
            {
                // No-Op...?
            }
        }

        private void DyeTemplateIdBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateDyeStatus();
        }


        private void EditRawDiffuse_Click(object sender, RoutedEventArgs e)
        {

        }

        private void EditRawSpecular_Click(object sender, RoutedEventArgs e)
        {

        }
        private void EditRawEmmissive_Click(object sender, RoutedEventArgs e)
        {

        }

        List<Half[]> CopiedRow;
        byte[] CopiedRowDye;
        private void CopyRowButton_Click(object sender, RoutedEventArgs e)
        {
            CopiedRow = GetRowData(RowId);
            CopiedRowDye = new byte[2];
            Array.Copy(_mtrl.ColorSetDyeData, RowId * 2, CopiedRowDye, 0, 2);

            PasteRowButton.IsEnabled = true;
        }

        private void PasteRowButton_Click(object sender, RoutedEventArgs e)
        {
            if (CopiedRow == null) return;

            var offset = RowId * 2;
            Array.Copy(CopiedRowDye, 0, _mtrl.ColorSetDyeData, offset, 2);

            offset = RowId * 16;
            for (int x = 0; x < 4; x++)
            {
                for (int y = 0; y < 4; y++)
                {
                    _mtrl.ColorSetData[offset] = CopiedRow[x][y];
                    offset++;
                }
            }

            UpdateRowVisual(RowId);
            SetRow(RowId);
        }

        private async void MoveRowUpButton_Click(object sender, RoutedEventArgs e)
        {
            if (RowId == 0) return;
            var prevRowId = RowId - 1;
            await SwapRows(RowId, prevRowId);
        }

        private async void MoveRowDownButton_Click(object sender, RoutedEventArgs e)
        {
            if (RowId == 15) return;
            var prevRowId = RowId + 1;
            await SwapRows(RowId, prevRowId);
        }

        private async Task SwapRows(int row1, int row2)
        {
            var myRowData = GetRowData(row1);
            var otherRowData = GetRowData(row2);

            var myData = new Half[16];
            var otherData = new Half[16];
            for (int i = 0; i < 4; i++)
            {
                Array.Copy(myRowData[i], 0, myData, i * 4, 4);
                Array.Copy(otherRowData[i], 0, otherData, i * 4, 4);
            }

            var myOffset = row1 * 16;
            var otherOffset = row2 * 16;

            var arr = _mtrl.ColorSetData.ToArray();
            Array.Copy(myData, 0, arr, otherOffset, 16);
            Array.Copy(otherData, 0, arr, myOffset, 16);

            var offset1 = row1 * 2;
            var offset2 = row2 * 2;

            var b1 = _mtrl.ColorSetDyeData[offset1];
            var b2 = _mtrl.ColorSetDyeData[offset1 + 1];

            _mtrl.ColorSetDyeData[offset1] = _mtrl.ColorSetDyeData[offset2];
            _mtrl.ColorSetDyeData[offset1 + 1] = _mtrl.ColorSetDyeData[offset2 + 1];
            _mtrl.ColorSetDyeData[offset2] = b1;
            _mtrl.ColorSetDyeData[offset2 + 1] = b2;

            _mtrl.ColorSetData = arr.ToList();

            await SetMaterial(_mtrl, row2);
        }
    }
}
