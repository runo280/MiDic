﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using Gma.System.MouseKeyHook;
using GoogleTranslateFreeApi;
using LiteDB;
using Clipboard = System.Windows.Clipboard;
using MessageBox = System.Windows.Forms.MessageBox;

namespace MiDic
{
    public partial class MainWindow : Window
    {
        const int WM_DRAWCLIPBOARD = 0x0308;
        const int WM_CHANGECBCHAIN = 0x030D;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern int SendMessage(IntPtr hwnd, int wMsg, IntPtr wParam, IntPtr lParam);

        [DllImport("User32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SetClipboardViewer(IntPtr hWnd);

        [DllImport("User32.dll", CharSet = CharSet.Auto)]
        private static extern bool ChangeClipboardChain(
            IntPtr hWndRemove, // handle to window to remove
            IntPtr hWndNewNext // handle to next window
        );


        private HwndSource _hwndSource;
        private WindowInteropHelper _wih;

        IntPtr _clipboardViewerNext;

        private void RegisterClipboardViewer()
        {
            _clipboardViewerNext = SetClipboardViewer(_hwndSource.Handle);
        }

        private void UnregisterClipboardViewer()
        {
            ChangeClipboardChain(_hwndSource.Handle, _clipboardViewerNext);
        }

        private bool _getCopyValue = false;

        private void CopyFromActiveProgram()
        {
            _getCopyValue = true;
            SendKeys.SendWait("^c");
        }

        private IntPtr MainWindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            switch (msg)
            {
                case WM_DRAWCLIPBOARD:

                    if (_getCopyValue && Clipboard.ContainsText())
                    {
                        _getCopyValue = false;
                        var selectedText = Clipboard.GetText().Trim();
                        if (selectedText.Length > 1)
                        {
                            LblEnglish.Text = selectedText;
                            Translate();
                            Clipboard.Clear();
                        }
                    }

                    // Send message along, there might be other programs listening to the copy command.
                    SendMessage(_clipboardViewerNext, msg, wParam, lParam);
                    break;

                case WM_CHANGECBCHAIN:
                    if (wParam == _clipboardViewerNext)
                    {
                        _clipboardViewerNext = lParam;
                    }
                    else
                    {
                        SendMessage(_clipboardViewerNext, msg, wParam, lParam);
                    }

                    break;
            }

            return IntPtr.Zero;
        }

        private async void Translate()
        {
            if (LblEnglish.Text.Trim().Length >= 0)
            {
                BtnSave.IsEnabled = false;
                BtnTranslate.IsEnabled = false;
                var translator = new GoogleTranslator();

                Language from = GoogleTranslateFreeApi.Language.English;
                Language to = GoogleTranslateFreeApi.Language.Persian;

                TranslationResult result = await translator.TranslateLiteAsync(LblEnglish.Text.Trim(), from, to);

                //The result is separated by the suggestions and the '\n' symbols
                string[] resultSeparated = result.FragmentedTranslation;

                //You can get all text using MergedTranslation property
                string resultMerged = result.MergedTranslation;
                LblPersian.Text = resultMerged;
                //There is also original text transcription
                string transcription = result.TranslatedTextTranscription;
                BtnSave.IsEnabled = true;
                BtnTranslate.IsEnabled = true;
            }
        }

        private IKeyboardMouseEvents _globalHook;

        public MainWindow()
        {
            InitializeComponent();
            HideMainWindow();
            SetUpHotKeys();
        }

        private void SetUpHotKeys()
        {
            _globalHook = Hook.GlobalEvents();

            _globalHook.MouseDown += (o, e) =>
            {
                if (e.X < Left || e.X > Left + Width || e.Y < Top || e.Y > Top + Height)
                {
                    HideMainWindow();
                }
            };

            _globalHook.OnCombination(new Dictionary<Combination, Action>()
            {
                {
                    Combination.FromString("Pause"), () =>
                    {
                        CopyFromActiveProgram();
                        ShowMainWindow();
                    }
                },
                {
                    Combination.FromString("Escape"), HideMainWindow
                }
            });
        }

        private void ShowMainWindow()
        {
            WindowState = WindowState.Normal;
            Show();
            Activate();
        }

        private void HideMainWindow()
        {
            Hide();
            WindowState = WindowState.Minimized;
        }

        private void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
        {
            _wih = new WindowInteropHelper(this);
            _hwndSource = HwndSource.FromHwnd(_wih.Handle);
            _hwndSource?.AddHook(MainWindowProc);
            RegisterClipboardViewer();
        }

        private void MainWindow_OnClosed(object sender, EventArgs e)
        {
            UnregisterClipboardViewer();
        }

        private void saveToDb(string str)
        {
            BtnSave.IsEnabled = false;
            using (var db = new LiteDatabase(@"MyData.db"))
            {
                var col = db.GetCollection<Word>("words");
                var word = new Word
                {
                    Origin = LblEnglish.Text.Trim(), Translation = str
                };
                var results = col.Query()
                    .Where(x => x.Origin == LblEnglish.Text.Trim())
                    .ToList();
                if (results.Count == 0) col.Insert(word);
            }
            BtnSave.IsEnabled = true;
        }

        private void BtnSave_OnClick(object sender, RoutedEventArgs e)
        {
            BtnSave.IsEnabled = false;
            saveToDb(LblPersian.Text.Trim());
            BtnSave.Content = "Saved";
            Task.Delay(2000).ContinueWith(t => {
                BtnSave.Dispatcher.Invoke(() => {
                    BtnSave.Content = "Save";
                    BtnSave.IsEnabled = true;
                });
                
            });

        }
        
        private void BtnTranslate_OnClick(object sender, RoutedEventArgs e)
        {
            BtnTranslate.IsEnabled = false;
            BtnSave.IsEnabled = false;
            Translate();
            BtnTranslate.IsEnabled = true;
            BtnSave.IsEnabled = true;
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            var fileName = @"SavedList_" + GetTimeDate() + ".txt";
            using (var db = new LiteDatabase(@"MyData.db"))
            {
                var col = db.GetCollection<Word>("words");
                var results = col.Query().ToList();
                using (TextWriter tw = new StreamWriter(fileName))
                {
                    foreach (Word w in results)
                        tw.WriteLine(w.Origin + ";" + w.Translation);
                }
            }
        }

        private void BtnEmptyDb_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult messageBoxResult = System.Windows.MessageBox.Show("Are you sure?", "Delete Confirmation", System.Windows.MessageBoxButton.YesNo);
            if (messageBoxResult == MessageBoxResult.Yes)
            {
                using (var db = new LiteDatabase(@"MyData.db"))
                {
                    db.DropCollection("words");
                }
            }
        }

        private void BtnView_Click(object sender, RoutedEventArgs e)
        {
            var view = new SavedItems();
            view.Show();
            HideMainWindow();
        }

        private void Window_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        public static string GetTimeDate()
        {
            return System.DateTime.Now.ToString("yyyy-MM-dd-_HH-mm-ss");
        }
    }
}