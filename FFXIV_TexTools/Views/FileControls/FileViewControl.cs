﻿using FFXIV_TexTools.Helpers;
using FFXIV_TexTools.Properties;
using FFXIV_TexTools.Resources;
using FFXIV_TexTools.Views.Item;
using FFXIV_TexTools.Views.Projects;
using MahApps.Metro.Controls;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Markup;
using xivModdingFramework.Cache;
using xivModdingFramework.Helpers;
using xivModdingFramework.Items.Interfaces;
using xivModdingFramework.Mods;
using xivModdingFramework.Mods.Enums;
using xivModdingFramework.SqPack.FileTypes;
using Control = System.Windows.Controls.Control;

namespace FFXIV_TexTools.Views.Controls
{

    public enum EFileViewType
    {
        Viewer,
        Editor
    };

    public abstract class FileViewControl : System.Windows.Controls.UserControl, INotifyPropertyChanged, IDisposable
    {
        public delegate void FileDeletedEventHandler(string internalPath);
        public event FileDeletedEventHandler FileDeleted;

        // State control vars.
        internal bool _UpdateQueued;
        protected bool _IS_SAVING;
        private bool _Disposed;

        private List<ModTransaction> _listeningTransactions = new List<ModTransaction>();

        private EFileViewType _ViewType;
        public EFileViewType ViewType { get
            {
                return _ViewType;
            }
            protected set
            {
                _ViewType = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ViewType)));
            }
        }

        private bool _UnsavedChanges;
        public bool UnsavedChanges
        {
            get => _UnsavedChanges;
            set
            {
                _UnsavedChanges = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UnsavedChanges)));
            }
        }

        private EModState _ModState;
        public EModState ModState
        {
            get => _ModState;
            protected set
            {
                _ModState = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ModState)));
            }
        }

        private string _InternalFilePath;
        public string InternalFilePath { get
            {
                return _InternalFilePath;
            }
            protected set
            {
                _InternalFilePath = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(InternalFilePath)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasFile)));
            }
        }

        private string _ExternalFilePath;
        public string ExternalFilePath {
            get
            {
                return _ExternalFilePath;
            }
            protected set
            {
                _ExternalFilePath = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ExternalFilePath)));
            }
        }

        private IItem _ReferenceItem;
        public IItem ReferenceItem
        {
            get
            {
                return _ReferenceItem;
            }
            protected set
            {
                _ReferenceItem = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ReferenceItem)));
            }
        }

        public bool HasFile { 
            get
            {
                return !string.IsNullOrWhiteSpace(InternalFilePath);
            } 
        }

        protected SmartImportOptions LastImportOptions;

        protected SaveFileDialog _SaveDialog;
        protected OpenFileDialog _OpenDialog;

        public FileViewControl()
        {
            if (MainWindow.UserTransaction != null)
            {
                _listeningTransactions.Add(MainWindow.UserTransaction);
                MainWindow.UserTransaction.FileChanged += Tx_FileChanged;
                MainWindow.UserTransaction.TransactionClosed += Tx_Closed;
            }

            ModTransaction.FileChangedOnCommit += Tx_FileChanged;
            TxWatcher.UserTxStarted += OnUserTransactionStarted;


            DebouncedUpdate = ViewHelpers.Debounce<string>(_UpdateOnMainThread, 200);


            IsEnabled = false;
        }


        private Action<string> DebouncedUpdate;

        private void OnUserTransactionStarted(ModTransaction tx)
        {
            if (tx != null)
            {
                tx.FileChanged += Tx_FileChanged;
                tx.TransactionClosed += Tx_Closed;
                _listeningTransactions.Add(tx);
            }
        }


        private void Tx_Closed(ModTransaction sender)
        {
            sender.TransactionClosed -= Tx_Closed;
            sender.FileChanged -= Tx_FileChanged;
            _listeningTransactions.Remove(sender);
        }

        private async void Tx_FileChanged(string changedFile, long newOffset)
        {

            try
            {
                await INTERNAL_HandlTxNotification(changedFile, newOffset);
            }
            catch (Exception ex)
            {
                // No Op
                Trace.WriteLine(ex);
            }
        }

        protected virtual async Task INTERNAL_HandlTxNotification(string changedFile, long newOffset)
        {
            if (_IS_SAVING || string.IsNullOrWhiteSpace(changedFile) || string.IsNullOrWhiteSpace(InternalFilePath))
            {
                return;
            }

            if (_UpdateQueued)
            {
                // Already in queue.
                return;
            }

            if (newOffset == 0 && changedFile == InternalFilePath)
            {
                _UpdateQueued = true;
                // File was deleted.
                DebouncedUpdate(InternalFilePath);
                FileDeleted?.Invoke(InternalFilePath);
                return;
            }

            // Run update checks on a new thread.
            await Task.Run(async () =>
            {
                if (await ShouldUpdateOnFileChange(changedFile))
                {
                    _UpdateQueued = true;
                    DebouncedUpdate(InternalFilePath);
                }
            });
        }

        /// <summary>
        /// Determines if this file view should refresh when a given file is changed.
        /// </summary>
        /// <param name="changedFile"></param>
        /// <returns></returns>
        protected virtual async Task<bool> ShouldUpdateOnFileChange(string changedFile)
        {
            if(string.Equals(changedFile, InternalFilePath))
            {
                return true;
            }

            return false;
        }

        private async void  _UpdateOnMainThread(string file)
        {
            Trace.WriteLine("Main Thread Update Called: " + file);
            try
            {
                await await Dispatcher.InvokeAsync(async () =>
                {
                    if (file != InternalFilePath)
                    {
                        // No longer viewing the same file.
                        return;
                    }

                    await ReloadFile();
                });
            }
            catch(Exception ex)
            {
                Trace.WriteLine(ex);
            }
        }

        public delegate void FileActionEventHandler(FileViewControl sender, bool success);

        /// <summary>
        /// Invoked whenever a file load attempt has completed.
        /// </summary>
        public event FileActionEventHandler FileLoaded;

        /// <summary>
        /// Invoked whenever the file is saved to Transaction/Game Files.
        /// </summary>
        public event FileActionEventHandler FileSaved;
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Invoke the PropertyChanged event for the given property.
        /// </summary>
        /// <param name="propertyName"></param>
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }


        /// <summary>
        /// Validates if this view can load the file in question.
        /// 
        /// This function is expected to catch and handle its own errors if there are any.
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public virtual async Task<bool> CanLoadFile(string filePath, byte[] data, ModTransaction tx)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return false;
            }

            var exts = GetValidFileExtensions();
            if(exts == null)
            {
                return false;
            }

            var ext = Path.GetExtension(filePath).ToLower();
            if (exts.ContainsKey(ext))
            {
                if (IOUtil.IsFFXIVInternalPath(filePath))
                {
                    return await INTERNAL_CanLoadFile(filePath,data, tx);
                }
                return true;
            }

            return false;
        }

        protected virtual async Task<bool> INTERNAL_CanLoadFile(string filePath, byte[] data, ModTransaction tx)
        {
            if(data != null)
            {
                return true;
            }

            return await tx.FileExists(filePath);
        }

        /// <summary>
        /// Gets the dictionary of file extensions that this view can load.
        /// Extension (including . ) => Nice Name.
        /// </summary>
        /// <returns></returns>
        public virtual Dictionary<string, string> GetValidFileExtensions()
        {
            return new Dictionary<string, string>();
        }

        /// <summary>
        /// Returns the 'nice' human readable name for the type of files read by this display.
        /// Ex "Image" or "Model"
        /// </summary>
        /// <returns></returns>
        public virtual string GetNiceName()
        {
            return "Unknown";
        }



        /// <summary>
        /// Reloads the current item, returning true if successful.
        /// 
        /// This function is expected to catch and handle its own errors.
        /// </summary>
        /// <returns></returns>
        public virtual async Task<bool> ReloadFile()
        {
            if(string.IsNullOrWhiteSpace(InternalFilePath))
            {
                return false;
            }

            return await LoadInternalFile(InternalFilePath, false, ReferenceItem);
        }

        /// <summary>
        /// Clears the currently visible file, cleaning up any resources as necessary.
        /// 
        /// This function is expected to catch and handle its own errors.
        /// </summary>
        /// <returns></returns>
        public void ClearFile()
        {
            if (HasFile)
            {
                var file = InternalFilePath;
                try
                {
                    LastImportOptions = null;
                    IsEnabled = false;
                    INTERNAL_ClearFile();
                    InternalFilePath = null;
                    ExternalFilePath = null;
                    ReferenceItem = null;
                    _SaveDialog = null;
                    _OpenDialog = null;

                } catch(Exception ex)
                {
                    Trace.WriteLine("An error occurred when unloading file: " + file + "\n" + ex.Message);
                }
            }
        }

        public abstract void INTERNAL_ClearFile();


        protected bool _LOADING = false;
        /// <summary>
        /// Load the given external file into view, assuming it will go to the target internal path.
        /// Returns true if the data was successfully loaded.
        /// 
        /// This function is expected to catch and handle its own errors.
        /// </summary>
        /// <param name="externalFile"></param>
        /// <param name="internalFile"></param>
        /// <returns></returns>
        public virtual async Task<bool> LoadInternalFile(string internalFile, bool forceOriginal = false, IItem referenceItem = null, byte[] decompData = null)
        {

            if (_LOADING)
            {
                // Safety reject.
                return false;
            }
            _UpdateQueued = false;

            if (UnsavedChanges && !string.IsNullOrWhiteSpace(InternalFilePath))
            {
                if (!this.ConfirmDiscardChanges(InternalFilePath))
                {
                    return false;
                }
            }

            bool success = false;
            try
            {
                IsEnabled = false;
                _LOADING = true;
                if (HasFile)
                {
                    ClearFile();
                }


                var tx = MainWindow.DefaultTransaction;
                if (!await CanLoadFile(internalFile, decompData, tx))
                {
                    // If we can't load the file, but still have it assigned, keep it posted here..?
                    InternalFilePath = internalFile;
                    await UpdateModState();
                    return success;
                }

                ReferenceItem = referenceItem;
                if (ReferenceItem == null)
                {
                    var root = await XivCache.GetFirstRoot(internalFile);
                    if (root != null) {
                        ReferenceItem = root.GetFirstItem();
                    }
                }

                var data = new byte[0];

                InternalFilePath = internalFile;
                ExternalFilePath = "";

                // Thread the loading, which may involve substantial decompression tasks.
                await Task.Run(async () =>
                {
                    if (decompData == null)
                    {
                        data = await INTERNAL_GetDataFromPath(internalFile, forceOriginal, tx);
                    }
                    else
                    {
                        UnsavedChanges = true;
                        data = decompData;
                    }
                    await UpdateModState(tx);
                });


                success = await INTERNAL_LoadFile(data, internalFile, referenceItem, tx);


                if (decompData != null)
                {
                    UnsavedChanges = true;
                } else
                {
                    UnsavedChanges = false;
                }
                IsEnabled = success;

                return success;
            }
            catch (Exception ex)
            {
                this.ShowError("File Load Error", "There was an error loading the file:\n\n" + ex.Message);
                InternalFilePath = "";
                ExternalFilePath = "";
                ModState = EModState.UnModded;
                UnsavedChanges = false;
                return success;
            }
            finally
            {
                _LOADING = false;
                IsEnabled = success;
                FileLoaded?.Invoke(this, success);
            }
        }

        public virtual async Task<bool> LoadRawData(byte[] data)
        {
            if (UnsavedChanges && !this.ConfirmDiscardChanges(InternalFilePath))
            {
                return false;
            }

            var tx = MainWindow.DefaultTransaction;
            var success = await INTERNAL_LoadFile(data, InternalFilePath, ReferenceItem, tx);
            UnsavedChanges = true;
            return success;
        }

        protected virtual async Task UpdateModState(ModTransaction tx = null)
        {
            try
            {
                if (tx == null)
                {
                    tx = MainWindow.DefaultTransaction;
                }
                ModState = await Modding.GetModState(InternalFilePath, tx);
            }
            catch(Exception ex)
            {
                Trace.WriteLine(ex);
            }
        }

        protected virtual async Task<byte[]> INTERNAL_GetDataFromPath(string internalPath, bool forceOriginal, ModTransaction tx)
        {
            return await tx.ReadFile(internalPath, forceOriginal);
        }

        /// <summary>
        /// Load the given external file into view, assuming it will go to the target internal path.
        /// Returns true if the data was successfully loaded.
        /// 
        /// This function is expected to catch and handle its own errors.
        /// </summary>
        /// <param name="externalFile"></param>
        /// <param name="internalFile"></param>
        /// <returns></returns>
        public virtual async Task<bool> LoadExternalFile(string externalFile, string internalFile = null, IItem referenceItem = null)
        {
            var originalPath = InternalFilePath;
            var originalItem = ReferenceItem;
            var sfd = _SaveDialog;
            var ofd = _OpenDialog;

            if (UnsavedChanges && !string.IsNullOrWhiteSpace(InternalFilePath))
            {
                if (!this.ConfirmDiscardChanges(InternalFilePath))
                {
                    return false;
                }
            }
            _UpdateQueued = false;

            IsEnabled = false;
            var success = false;
            try
            { 

                var tx = MainWindow.DefaultTransaction;
                if (!await CanLoadFile(externalFile, null, tx))
                {
                    // If we can't load the file, but still have it assigned, keep it posted here..?
                    return success;
                }

                if (internalFile == null)
                { 
                    internalFile = InternalFilePath;
                    referenceItem = ReferenceItem;
                }

                if (!IOUtil.IsFFXIVInternalPath(internalFile))
                {
                    throw new Exception("Must have a valid internal path to prepare the external file for.");
                }

                if (referenceItem == null)
                {
                    var root = await XivCache.GetFirstRoot(internalFile);
                    if (root != null)
                    {
                        referenceItem = root.GetFirstItem();
                    }
                }

                // Don't clear the file until after calling this, since some files may be partial imports.
                byte[] data = null;
                await Task.Run(async () =>
                {
                    // Thread the actual loading process which could involve extensive file manipulation.
                    data = await INTERNAL_ExternalToUncompressedFile(externalFile, internalFile, referenceItem, tx);
                });
                if (data == null || data.Length == 0)
                {
                    return false;
                }

                // Preserve import options here.
                var lastOp = LastImportOptions;
                ClearFile();
                LastImportOptions = lastOp;

                ExternalFilePath = externalFile;
                InternalFilePath = internalFile;
                ReferenceItem = referenceItem;

                success = await INTERNAL_LoadFile(data, internalFile, referenceItem, tx);
                UnsavedChanges = true;
                IsEnabled = success;

                return success;
            } catch (Exception ex)
            {
                this.ShowError("File Load Error", "There was an error loading the file:\n\n" + ex.Message);
                ExternalFilePath = "";
                return success;
            }
            finally
            {
                if (!success)
                {
                    ClearFile();
                }

                // Ensure we're still marked on the correct file coming out.
                if (IOUtil.IsFFXIVInternalPath(internalFile))
                {
                    InternalFilePath = internalFile;
                    ReferenceItem = referenceItem;
                    _SaveDialog = sfd;
                    _OpenDialog = ofd;
                }  else if(IOUtil.IsFFXIVInternalPath(originalPath))
                {
                    InternalFilePath = originalPath;
                    ReferenceItem = originalItem;
                    _SaveDialog = null;
                    _OpenDialog = null;
                } else
                {
                    InternalFilePath = null;
                    ReferenceItem = null;
                    _SaveDialog = null;
                    _OpenDialog = null;
                }

                await UpdateModState();

                IsEnabled = success;
                FileLoaded?.Invoke(this,success);
            }
        }
        protected virtual async Task<byte[]> INTERNAL_ExternalToUncompressedFile(string externalFile, string internalFile, IItem referenceItem, ModTransaction tx)
        {
            return await SmartImport.CreateUncompressedFile(externalFile, internalFile, tx);
        }

        /// <summary>
        /// Loads the given byte data into the workspace.
        /// Returns true if the data was successfully loaded.
        /// Data is an Uncompressed FFXIV file type.
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        protected abstract Task<bool> INTERNAL_LoadFile(byte[] data, string internalFile, IItem referenceItem, ModTransaction tx);

        /// <summary>
        /// Gets the currently viewed file in compressed/SQPacked format.
        /// </summary>
        /// <returns></returns>
        public async Task<byte[]> GetCompressedData()
        {
            var data = await GetUncompressedData();

            var forceType2 = InternalFilePath.EndsWith(".atex");
            return await SmartImport.CreateCompressedFile(data, forceType2);
        }

        /// <summary>
        /// Converts the currently viewed file into uncompresed byte data.
        /// </summary>
        /// <returns></returns>
        public async Task<byte[]> GetUncompressedData()
        {
            return await INTERNAL_GetUncompressedData();
        }

        /// <summary>
        /// Converts the currently viewed file into uncompresed byte data.
        /// </summary>
        /// <returns></returns>
        protected abstract Task<byte[]> INTERNAL_GetUncompressedData();

        /// <summary>
        /// Loads a given file into the view, then saves it to the current user transaction/game files.
        /// 
        /// This function is expected to catch and handle its own errors.
        /// </summary>
        /// <param name="externalFilePath"></param>
        /// <param name="internalFilePath"></param>
        /// <returns></returns>
        public async Task<bool> ImportFile(string externalFilePath, string internalFilePath, IItem referenceItem = null)
        {
            var success = await LoadExternalFile(externalFilePath, internalFilePath, referenceItem);
            if (!success)
            {
                return false;
            }
            return await SaveCurrentFile();
        }

        /// <summary>
        /// Saves the current file to the current transaction/Game DATs, returning true if successful.
        /// 
        /// This function is expected to catch and handle its own errors.
        /// </summary>
        /// <returns></returns>
        public async Task<bool> SaveCurrentFile(ModTransaction tx = null, bool ignoreWarning = false)
        {
            if ((!ignoreWarning) && !this.CheckFileWrite())
            {
                return false;
            }

            var success = false;
            _IS_SAVING = true;
            try
            {
                // Thread this since mod writing may involve some pretty hefty tasks in some cases.
                // And nothing in here should be touching our UI.
                success = await Task.Run(async () =>
                {
                    if (string.IsNullOrWhiteSpace(InternalFilePath))
                    {
                        return false;
                    }

                    if (tx == null)
                    {
                        tx = MainWindow.UserTransaction;
                    }


                    var res = await INTERNAL_WriteModFile(tx);
                    if (!res)
                    {
                        return false;
                    }

                    ModState = EModState.Enabled;
                    UnsavedChanges = false;
                    return true;
                });

                FileSaved?.Invoke(this, success);
                if (success)
                {

                    ProjectWindow.AddExternalSource(InternalFilePath, ExternalFilePath, true, LastImportOptions);

                    // Reload the file after to ensure user has correct-to-file-system state.
                    // This is maybe unnecessary, but until we're 200% sure TT's file writing is perfectly
                    // stable, it's probably better to be safe here.
                    await ReloadFile();
                }
                return success;
            }
            catch(Exception ex)
            {
                this.ShowError("File Save Error", "There was an error saving the file:\n\n" + ex.Message);
                return success;
            } finally
            {
                _IS_SAVING = false;
            }
        }

        protected virtual async Task<bool> INTERNAL_WriteModFile(ModTransaction tx)
        {
            var data = await GetUncompressedData();
            await Dat.WriteModFile(data, InternalFilePath, XivStrings.TexTools, ReferenceItem, tx, false);
            return true;
        }


        protected virtual KeyValuePair<string, string> GetDefaultExtension()
        {
            var extensions = GetValidFileExtensions();
            if(extensions == null || extensions.Count == 0)
            {
                return new KeyValuePair<string, string>(".bin", "Unknown");
            }
            return extensions.First();
        }
        public virtual string GetDefaultSaveDirectory()
        {
            var mw = MainWindow.GetMainWindow();
            var path = Path.GetFullPath(IOUtil.MakeItemSavePath(ReferenceItem, new DirectoryInfo(Settings.Default.Save_Directory), IOUtil.GetRaceFromPath(InternalFilePath)));
            Directory.CreateDirectory(path);
            return path;
        }
        public virtual string GetDefaultSaveName(string extension = null)
        {
            var name = Path.GetFileNameWithoutExtension(InternalFilePath);

            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = GetDefaultExtension().Key;
            }

            return name + extension;
        }


        /// <summary>
        /// Loads a given file via windows dialog, assuming it will go to the given internal file path.
        /// If no path is provided, retains the current internal file path.
        /// </summary>
        /// <returns></returns>
        public async Task<bool> LoadFileByDialog(string internalFilePath = null, IItem referenceItem = null)
        {
            if (!string.IsNullOrWhiteSpace(internalFilePath))
            {
                InternalFilePath = internalFilePath;
            } else
            {
                referenceItem = ReferenceItem;
            }

            if (string.IsNullOrWhiteSpace(InternalFilePath))
            {
                return false;
            }

            var exts = GetValidFileExtensions();
            if (exts == null || exts.Count == 0)
            {
                return false;
            }

            var ofd = GetOpenDialog();
            var res = ofd.ShowDialog();
            if(res != DialogResult.OK)
            {
                return false;
            }

            var success = await LoadExternalFile(ofd.FileName, InternalFilePath, referenceItem);
            if (!success)
            {
                await ReloadFile();
            }
            return success;
        }

        protected OpenFileDialog GetOpenDialog()
        {
            var exts = GetValidFileExtensions();
            if (exts == null || exts.Count == 0)
            {
                return null;
            }

            if(_OpenDialog == null)
            {
                _OpenDialog = new OpenFileDialog();
                _OpenDialog.InitialDirectory = GetDefaultSaveDirectory();
                _OpenDialog.FileName = GetDefaultSaveName();
            } else if (!string.IsNullOrWhiteSpace(_OpenDialog.FileName))
            {
                try
                {
                    _OpenDialog.InitialDirectory = Path.GetDirectoryName(_OpenDialog.FileName);
                }
                catch
                {
                    //No-OP
                }
            }

            var extStrings = "*" + string.Join(";*", exts.Keys);
            var filter = GetNiceName() + " Files|" + extStrings;
            _OpenDialog.Filter = filter;
            return _OpenDialog;
        }

        public async Task<bool> ImportFileByDialog(string internalFilePath = null)
        {
            var success = await LoadFileByDialog(internalFilePath);
            if (!success)
            {
                return false;
            }

            return await SaveCurrentFile();
        }

        /// <summary>
        /// Saves the current file to the given location in raw FFXIV binary format.
        /// </summary>
        /// <param name="externalFilePath"></param>
        /// <returns></returns>
        public async Task<bool> SaveAsRaw(string externalFilePath, bool compressed = false)
        {
            if (!HasFile)
            {
                return false;
            }

            var exts = GetValidFileExtensions();
            if (exts == null || exts.Count == 0)
            {
                return false;
            }

            var ext = Path.GetExtension(externalFilePath).ToLower();
            if (!exts.ContainsKey(ext))
            {
                return false;
            }

            try
            {
                byte[] data;
                if (compressed)
                {
                    data = await GetCompressedData();
                }
                else
                {
                    data = await GetUncompressedData();
                }

                Directory.CreateDirectory(Path.GetDirectoryName(externalFilePath));
                File.WriteAllBytes(externalFilePath, data);
                return true;
            }
            catch(Exception ex)
            {
                this.ShowError("File Save Error", "An error occurred while saving the file:\n\n" + ex.Message);
                return false;
            }
        }


        protected abstract Task<bool> INTERNAL_SaveAs(string externalFilePath);

        /// <summary>
        /// Triggers the Modpack Save dialog.
        /// </summary>
        /// <returns></returns>
        public async Task<bool> SaveAsModpack()
        {
            // Because of the way some of the system internals work, the single-file modpack export needs us to temporarily save the file first.
            var tx = MainWindow.UserTransaction;
            var boiler = await TxBoiler.BeginWrite(tx);
            tx = boiler.Transaction;
            try
            {
                if(!await SaveCurrentFile(tx))
                {
                    // This shouldn't really ever occur, but...
                    throw new Exception("Failed to save file state to temporary internal transaction.");
                }

                SingleFileModpackCreator.ExportFile(InternalFilePath, Window.GetWindow(this), tx);
                return true;
            }
            catch(Exception ex)
            {
                this.ShowError("Modpack Export Error", "An error occured when trying to export the file(s):\n" + ex.Message);
                return false;
            }
            finally
            {
                await boiler.Cancel(true);
            }
        }

        /// <summary>
        /// Saves the current file to an arbitrary location, prompted by user dialog.
        /// </summary>
        /// <returns></returns>
        public async Task<bool> SaveAsByDialog(string extension = null)
        {
            try
            {
                if (!HasFile)
                {
                    return false;
                }


                var exts = GetValidFileExtensions();
                if (exts == null || exts.Count == 0)
                {
                    return false;
                }

                KeyValuePair<string, string> ext;
                if (string.IsNullOrEmpty(extension))
                {
                    ext = GetDefaultExtension();
                } else if(exts.ContainsKey(extension))
                {
                    ext = exts.First(x => x.Key == extension);
                } else
                {
                    return false;
                }

                if (_SaveDialog == null)
                {
                    _SaveDialog = new SaveFileDialog();
                    _SaveDialog.InitialDirectory = GetDefaultSaveDirectory();
                    _SaveDialog.FileName = GetDefaultSaveName(extension);
                } else if(!string.IsNullOrWhiteSpace(_SaveDialog.FileName))
                {
                    try
                    {
                        if (extension != null)
                        {
                            var path = Path.GetFileNameWithoutExtension(_SaveDialog.FileName);
                            _SaveDialog.FileName = path + extension;
                        }
                        _SaveDialog.InitialDirectory = Path.GetDirectoryName(_SaveDialog.FileName);
                    }
                    catch
                    {
                        //No-OP
                    }
                }

                if (string.IsNullOrWhiteSpace(extension))
                {
                    var extStrings = "*" + string.Join(";*", exts.Keys);
                    var filter = GetNiceName() + " Files|" + extStrings;

                    foreach(var f in exts)
                    {
                        filter += "|" + f.Value + "|" + "*" + f.Key;
                    }
                    _SaveDialog.Filter = filter;
                }
                else
                {
                    _SaveDialog.Filter = ext.Value + " Files|*" + ext.Key;
                }

                var res = _SaveDialog.ShowDialog();
                if (res != DialogResult.OK)
                {
                    return false;
                }

                var userExt = Path.GetExtension(_SaveDialog.FileName).ToLower();
                if (!exts.ContainsKey(userExt))
                {
                    return false;
                }


                return await INTERNAL_SaveAs(_SaveDialog.FileName);
            } catch (Exception ex)
            {
                this.ShowError("Save Error", "An Error occured while saving the file:\n\n" + ex.Message);
                return false;
            }
        }

        public virtual bool HasAdditionalSaveFunctions()
        {
            return false;
        }

        public virtual async Task<List<(string Name, Func<Task> Function, bool Enabled)>> GetAdditionalSaveFunctions()
        {
            return new List<(string Name, Func<Task> Function, bool Enabled)>();
        }

        protected ItemViewControl GetItemControlParent()
        {
            return ViewHelpers.FindParentOfType<ItemViewControl>(this);
        }
        private void Dispose(bool disposing)
        {
            if (!_Disposed)
            {
                // Remove event handlers to ensure we don't get GC locked.
                TxWatcher.UserTxStarted -= OnUserTransactionStarted;
                ModTransaction.FileChangedOnCommit -= Tx_FileChanged;
                if (_listeningTransactions != null)
                {
                    foreach (var t in _listeningTransactions)
                    {
                        t.FileChanged -= Tx_FileChanged;
                        t.TransactionClosed -= Tx_Closed;
                    }
                    _listeningTransactions = null;
                }

                if (disposing)
                {
                    ClearFile();
                    FreeManaged();
                }

                FreeUnmanaged();
                _Disposed = true;
            }
        }

        /// <summary>
        /// Keydown handler that is passed from the file wrapper in the event of a ctrl-key combo that is not yet handled.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public virtual void OnControlKey(object sender, System.Windows.Input.KeyEventArgs e)
        {
        }



        protected virtual void FreeManaged()
        {
        }


        protected virtual void FreeUnmanaged()
        {

        }

        ~FileViewControl()
        {
             // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
             Dispose(disposing: false);
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
