using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using CsvHelper.Configuration;

using Microsoft.Web.WebView2.Wpf;

namespace DotNetStringRepacker
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        string textFromView = "";
        public MainWindow()
        {
            InitializeComponent();
            MainControls = new Control[] { InputTextBox, OutputTextBox, StringsFileTextBox, RepackButtton };
            TempFolder = Path.Combine(Path.GetTempPath(), nameof(DotNetStringRepacker) + Guid.NewGuid());
            InputTextBox.Text = Properties.Settings.Default.InputFile;
            OutputTextBox.Text = Properties.Settings.Default.OutputFile;
            StringsFileTextBox.Text = Properties.Settings.Default.StringsFile;
            Task.Run(() =>
            {
                foreach (var directory in Directory.GetDirectories(Path.GetTempPath(),
                    nameof(DotNetStringRepacker) + "*"))
                {
                    Directory.Delete(directory, true);
                }
            });
            LogFlusher();

            this.webView21.Source =
                new Uri(System.IO.Path.Combine(
            System.AppDomain.CurrentDomain.BaseDirectory,
            @"Monaco\index.html"));

            webView21.WebMessageReceived += webView21_WebMessageReceived;


            //InputText.AddHandler(ScrollViewer.ScrollChangedEvent, new RoutedEventHandler(InputText_ScrollChanged));
            //OutputText.AddHandler(ScrollViewer.ScrollChangedEvent, new RoutedEventHandler(OutputText_ScrollChanged));
        }

        private void webView21_WebMessageReceived(object sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs args)
        {
            textFromView = args.TryGetWebMessageAsString();
        }

        //private void InputText_ScrollChanged(object sender, RoutedEventArgs e)
        //{
        //    // Obtener la posición de desplazamiento vertical del textBox1.ScrollViewer
        //    ScrollViewer scrollViewer = (ScrollViewer)e.OriginalSource;
        //    double verticalOffset = scrollViewer.VerticalOffset;

        //    // Sincronizar la posición de desplazamiento vertical en textBox2.ScrollViewer
        //    ScrollViewer sv2 = (ScrollViewer)OutputText.Template.FindName("PART_ContentHost", OutputText);
        //    sv2.ScrollToVerticalOffset(verticalOffset);
        //}

        //private void OutputText_ScrollChanged(object sender, RoutedEventArgs e)
        //{
        //    // Obtener la posición de desplazamiento vertical del textBox1.ScrollViewer
        //    ScrollViewer scrollViewer = (ScrollViewer)e.OriginalSource;
        //    double verticalOffset = scrollViewer.VerticalOffset;

        //    // Sincronizar la posición de desplazamiento vertical en textBox2.ScrollViewer
        //    ScrollViewer sv2 = (ScrollViewer)InputText.Template.FindName("PART_ContentHost", InputText);
        //    sv2.ScrollToVerticalOffset(verticalOffset);
        //}

        private Control[] MainControls { get; }
        private CancellationTokenSource CancellationTokenSource { get; set; }
        private StringBuilder LogText { get; } = new StringBuilder();
        private KeyValuePair<string, DateTime> Cached { get; set; }
        private string TempFolder { get; set; }
        private ConcurrentQueue<string> LogBuffer { get; } = new ConcurrentQueue<string>();
        
        private Regex RegExComments => new Regex(@"\s\s\s//\s.*", RegexOptions.Compiled);
        //private Regex RegExByteArray => new Regex(@"(\[?[A-Za-z0-9_]+\]?)\.?(\[?[A-Za-z0-9_]+\]?)", RegexOptions.IgnoreCase);

        private Regex RegExByteArray => new Regex(@"(\[?[A-Za-z0-9_]{5,}\]?)\.?(\[?[A-Za-z0-9_]+\]?)", RegexOptions.IgnoreCase);
        private Regex RegExSecondFilter => new Regex(@"\bo?(SR|sr)([A-Za-z0-9_]+)\b", RegexOptions.IgnoreCase);
        private Regex RegExEscapedN { get; } = new Regex(@"\\n", RegexOptions.Compiled);
        private Regex RegExNewLine { get; } = new Regex("\r?\n", RegexOptions.Compiled);
        private Regex RegExEscapedNewLine { get; } = new Regex(@"(?<!\\)\\n", RegexOptions.Compiled);
        private Regex RegExWhiespace { get; } = new Regex(@"\s+", RegexOptions.Compiled);

        private CsvConfiguration CsvConfiguration { get; } = new CsvConfiguration
        {
            //HasHeaderRecord = false,
            QuoteAllFields = false,
            Delimiter = ";",
            
        };

        private sealed class StringOverride
        {
            public string TargetString { get; set; }
            public string ReplacementString { get; set; }
        }

        private void AuthorGravatar_Loaded(object sender, RoutedEventArgs e)
        {
        }

        private void LogFlusher()
        {
            var needUpdate = false;
            while (LogBuffer.TryDequeue(out var message))
            {
                LogText.AppendLine(message);
                needUpdate = true;
            }
            if (needUpdate)
            {
                Dispatcher.Invoke(() =>
                {
                    LogTextBox.Text = LogText.ToString();
                    LogTextBox.ScrollToEnd();
                });
            }
            Task.Delay(TimeSpan.FromMilliseconds(100))
                .ContinueWith(task => LogFlusher());
        }

        private void Log(string message)
        {
            LogBuffer.Enqueue(message);
        }

        private async Task BlockUi(Func<CancellationToken, Task> action)
        {
            foreach (var control in MainControls)
                control.IsEnabled = false;
            ProgressBar.Value = 0;
            ProgressBar.IsIndeterminate = true;
            LogTextBox.Foreground = Foreground;
            Dispatcher.Invoke(() =>
            {
                LogText.Clear();
                LogTextBox.Clear();
            });
            CancellationTokenSource = new CancellationTokenSource();
            CancelButtton.IsEnabled = true;
            try
            {
                await Task.Run(async () => await action(CancellationTokenSource.Token), CancellationTokenSource.Token);
                ProgressBar.Value = 100;
            }
            catch (Exception e)
            {
                LogTextBox.Foreground = Brushes.Red;
                Log(e.ToString());
            }
            ProgressBar.IsIndeterminate = false;
            CancelButtton.IsEnabled = false;
            foreach (var control in MainControls)
                control.IsEnabled = true;
        }

        async Task<string> waitForText()
        {
            while (textFromView == "");
            return textFromView;
        }

        private async void RepackButtton_Click(object sender, RoutedEventArgs e)
        {
            textFromView = "";
            string text = "";
            if (rdo_archivo.IsChecked ?? false)
                text = File.ReadAllText(InputFile, Encoding.UTF8);
            else
            {
                await webView21.CoreWebView2.ExecuteScriptAsync($"sendText();");
                text = await waitForText();
            }


            await BlockUi(async (cancellationToken) => await InThread(() =>
            {
                //EnsureDissassembled(cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();
                Log("Cargando texto de reemplazo...");
                using (var stream = new FileStream(StringsFile, FileMode.Open))
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                using (var csvReader = new CsvHelper.CsvReader(reader, CsvConfiguration))
                {
                    var overrides = csvReader
                    .GetRecords<StringOverride>()
                    .Where(x => x.TargetString != x.ReplacementString)
                    .ToImmutableDictionary(x => x.TargetString, x => x.ReplacementString);

                    Log($"{overrides.Count} textos por sobreescribir.");
                    Log("Reemplazando textos...");
                    //text = RegExComments.Replace(text, "");
                    GC.Collect();

                    int replaced = 0;
                    text = RegExByteArray.Replace(text, match =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var currentString = ExtractString(match);

                        if (overrides.TryGetValue(currentString, out var replacement))
                        {
                            Log($"Reemplazando \"{currentString}\" con \"{replacement}\"");
                            replaced++;
                            return replacement;
                        }
                        else if (overrides.TryGetValue(currentString.Replace("[", "").Replace("]", ""), out var replacementII))
                        {
                            replacementII = string.Join(".", replacementII.Split('.').Select(txt => $"[{txt}]"));

                            Log($"Reemplazando \"{currentString}\" con \"{replacementII}\"");
                            replaced++;
                            return replacementII;
                        }
                        return match.Value;
                    });

                    text = RegExSecondFilter.Replace(text, match =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var currentString = ExtractString(match);

                        if (overrides.TryGetValue(currentString, out var replacement))
                        {
                            Log($"Reemplazando \"{currentString}\" con \"{replacement}\"");
                            replaced++;
                            return replacement;
                        }
                        else if (currentString.StartsWith("o"))
                        {
                            var withoutSchema = overrides.FirstOrDefault(p => p.Key.Contains(currentString.Substring(1, currentString.Length - 1)));
                            if (withoutSchema.Key == null)
                            {
                                return match.Value;
                            }
                            if(withoutSchema.Value.Contains("."))
                                Log($"Reemplazando \"{currentString}\" con \"o{withoutSchema.Value.Split('.')[1]}\"");
                            else
                                return match.Value;
                            replaced++;
                            return $"o{withoutSchema.Value.Split('.')[1]}";
                        }
                        else
                        {
                            var withoutSchema = overrides.FirstOrDefault(p => p.Key.Contains(currentString.Substring(1, currentString.Length - 1)));
                            if (withoutSchema.Key == null)
                            {
                                return match.Value;
                            }

                            Log($"Reemplazando \"{currentString}\" con \"{withoutSchema.Value.Split('.')[1]}\"");
                            replaced++;
                            return $"{withoutSchema.Value.Split('.')[1]}";
                        }
                    });

                    GC.Collect();
                    Log($"{replaced} textos reemplazados");

                }
                GC.Collect();

                cancellationToken.ThrowIfCancellationRequested();
                //Log("Recompiling application...");
                //RunProcess(Environment.Is64BitOperatingSystem ?
                //            Properties.Settings.Default.IlasmPath64 : Properties.Settings.Default.IlasmPath32
                //            , $" /QUIET \"{DissasebledFile}\" /OUT=\"{OutputFile}\"",
                //        cancellationToken)
                //    .Wait(cancellationToken);

                //Log($"Application successfully recompiled to {OutputFile}");

            }, cancellationToken));

            if (rdo_archivo.IsChecked ?? false)
                File.WriteAllText(OutputFile, text, Encoding.UTF8);
            else
            {
                await webView21.CoreWebView2.ExecuteScriptAsync($"setResult(`{text}`);");
            }
                
        }

        private string InputFile => Dispatcher.Invoke(() => InputTextBox.Text);
        private string OutputFile => Dispatcher.Invoke(() => OutputTextBox.Text);
        private string StringsFile => Dispatcher.Invoke(() => StringsFileTextBox.Text);
        private string DissasebledFile =>Path.Combine(TempFolder, "app.il");
        private string SourceFile => Path.Combine(TempFolder, "app.source.il");

        private async Task RunProcess(string path, string arguments, CancellationToken cancellationToken)
        {
            Log($"{path} {arguments}");
            var task = new TaskCompletionSource<bool>();

            using (var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = path,
                    Arguments = arguments,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
                EnableRaisingEvents = true,
            })
            {
                process.Exited += (o, args) =>
                {
                    try
                    {
                        if (process.ExitCode == 0)
                        {
                            task.SetResult(true);
                        }
                        else
                        {
                            task.SetException(new Exception($"Process ended with code {process.ExitCode}"));
                        }
                    }
                    catch (Exception e)
                    {
                        task.SetException(e);
                    }
                };
                cancellationToken.Register(() =>
                {
                    try
                    {
                        if (!process.HasExited)
                            process.Kill();
                    }
                    catch (Exception)
                    {
                    }
                });
                process.Start();
                process.OutputDataReceived += (o, args) => Log(args.Data);
                process.ErrorDataReceived += (o, args) => Log(args.Data);
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.PriorityClass = ProcessPriorityClass.BelowNormal;
                await task.Task;
            }
        }

        private void EnsureDissassembled(CancellationToken cancellationToken)
        {
            if (!File.Exists(InputFile))
                throw new Exception("Input file not exists");

            var fileWriteTimeUtc = File.GetLastWriteTimeUtc(InputFile);
            if (Cached.Key != InputFile || Cached.Value != fileWriteTimeUtc)
            {
                if (Directory.Exists(TempFolder))
                    Directory.Delete(TempFolder, true);
                Directory.CreateDirectory(TempFolder);
                Cached = default(KeyValuePair<string, DateTime>);
                Log($"Disassembling application...");
                
                RunProcess(Environment.Is64BitOperatingSystem ? 
                    Properties.Settings.Default.IldasmPath64 : Properties.Settings.Default.IldasmPath32
                    , $"/UTF8 \"{InputFile}\" /OUT=\"{DissasebledFile}\"",
                        cancellationToken)
                    .Wait(cancellationToken);

                if (!File.Exists(DissasebledFile))
                    throw new Exception("Disassemble failed");

                File.Copy(DissasebledFile, SourceFile);

                Cached = new KeyValuePair<string, DateTime>(InputFile, fileWriteTimeUtc);
            }
        }

        public static byte[] StringToByteArrayFastest(string hex)
        {
            if (hex.Length % 2 == 1)
                throw new Exception("The binary key cannot have an odd number of digits");

            byte[] arr = new byte[hex.Length >> 1];

            for (int i = 0; i < hex.Length >> 1; ++i)
            {
                arr[i] = (byte)((GetHexVal(hex[i << 1]) << 4) + (GetHexVal(hex[(i << 1) + 1])));
            }

            return arr;
        }

        public static int GetHexVal(char hex)
        {
            int val = (int)hex;
            //For uppercase A-F letters:
            return val - (val < 58 ? 48 : 55);
            //For lowercase a-f letters:
            //return val - (val < 58 ? 48 : 87);
            //Or the two combined, but a bit slower:
            //return val - (val < 58 ? 48 : (val < 97 ? 55 : 87));
        }

        private string ExtractString(Match match)
        {
            //var hex = RegExWhiespace.Replace(match.Groups[2].Value, "");
            //var bytes = StringToByteArrayFastest(hex);
            //var text = Encoding.UTF8.GetString(bytes);
            //text = RegExEscapedN.Replace(text, @"\\n");
            //text = RegExNewLine.Replace(text, @"\n");
            return match.Value;
        }

        private string PackString(Match match, string newString)
        {
            var text = BitConverter.ToString(Encoding.UTF8.GetBytes(RegExEscapedNewLine.Replace(newString, "\r\n")));
            return match.Groups[1].Value + text.Replace("-", " ")+ match.Groups[3].Value;
        }

        private async void ExtractButtton_Click(object sender, RoutedEventArgs e)
        {
            await BlockUi(async (cancellationToken) => await InThread(() =>
            {
                EnsureDissassembled(cancellationToken);

                Log("Parsing strings...");
                var text = File.ReadAllText(DissasebledFile, Encoding.UTF8);

                text = RegExComments.Replace(text, "");
                GC.Collect();

                var strings = RegExByteArray.Matches(text)
                .Cast<Match>()
                .Select(ExtractString)
                .ToImmutableHashSet();
                GC.Collect();

                Log("Saving to file...");
                using (var stream = new FileStream(StringsFile, FileMode.Create))
                using (var writer = new StreamWriter(stream, Encoding.UTF8))
                using (var csvWriter = new CsvHelper.CsvWriter(writer, CsvConfiguration))
                {
                    csvWriter.WriteRecords(strings.Select(x => new StringOverride { TargetString = x, ReplacementString = x }));
                }

                Log($"Strings extracted to file {StringsFile}");

            }, cancellationToken));
        }

        private Task InThread(Action action, CancellationToken cancellationToken)
        {
            var task = new TaskCompletionSource<bool>();
            new Thread(() =>
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        task.SetCanceled();
                        return;
                    }
                    try
                    {
                        action();
                        task.SetResult(true);
                    }
                    catch (Exception e)
                    {
                        task.SetException(e);
                    }
                    GC.Collect();
                })
            {
                IsBackground = true,
                Priority = ThreadPriority.Lowest
            }
                .Start();
            return task.Task;
        }

        private void CancelButtton_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            button.IsEnabled = false;
            CancellationTokenSource.Cancel();
        }

        private void TextBox_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                ((TextBox)sender).Text = files?.FirstOrDefault() ?? string.Empty;
            }
        }

        private void TextBox_PreviewDragOver(object sender, DragEventArgs e)
        {
            e.Handled = true;
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(TempFolder) && Directory.Exists(TempFolder))
                Directory.Delete(TempFolder, true);

            Properties.Settings.Default.InputFile = InputTextBox.Text;
            Properties.Settings.Default.OutputFile = OutputTextBox.Text;
            Properties.Settings.Default.StringsFile = StringsFileTextBox.Text;
            Properties.Settings.Default.Save();
        }

        private void InputTextBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Create OpenFileDialog 
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();



            // Set filter for file extension and default file extension 
            dlg.DefaultExt = ".cs";
            dlg.Filter = "Text|*.cs|All|*.*";


            // Display OpenFileDialog by calling ShowDialog method 
            Nullable<bool> result = dlg.ShowDialog();


            // Get the selected file name and display in a TextBox 
            if (result == true)
            {
                // Open document 
                string filename = dlg.FileName;
                InputTextBox.Text = filename;
            }
        }

        private void StringsFileTextBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Create OpenFileDialog 
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();



            // Set filter for file extension and default file extension 
            dlg.DefaultExt = ".csv";
            dlg.Filter = "CSV files (*.csv)|*csv";


            // Display OpenFileDialog by calling ShowDialog method 
            Nullable<bool> result = dlg.ShowDialog();


            // Get the selected file name and display in a TextBox 
            if (result == true)
            {
                // Open document 
                string filename = dlg.FileName;
                StringsFileTextBox.Text = filename;
            }
        }

        private void OutputTextBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Create OpenFileDialog 
            Microsoft.Win32.SaveFileDialog dlg = new Microsoft.Win32.SaveFileDialog();

            // Set filter for file extension and default file extension 
            dlg.DefaultExt = ".cs";
            dlg.Filter = "All|*.*";

            // Display OpenFileDialog by calling ShowDialog method 
            Nullable<bool> result = dlg.ShowDialog();


            // Get the selected file name and display in a TextBox 
            if (result == true)
            {
                // Open document 
                string filename = dlg.FileName;
                OutputTextBox.Text = filename;
            }
        }
    }
}
