using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;

namespace ContractEditor
{
    public class EditorTab
    {
        public string FileName { get; set; } = "Untitled.ct";
        public string Content { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public bool IsModified { get; set; }
    }

    public partial class MainPage : ContentPage
    {
        private ObservableCollection<EditorTab> _tabs = new();
        private EditorTab _currentTab;
        private int _untitledCounter = 1;
        private double _fontSize = 14;

        public MainPage()
        {
            InitializeComponent();
            CreateNewTab();
        }

        private void CreateNewTab()
        {
            var newTab = new EditorTab { FileName = $"Untitled-{_untitledCounter++}.ct" };
            _tabs.Add(newTab);
            SwitchToTab(newTab);
            UpdateTabBar();
        }

        private void SwitchToTab(EditorTab tab)
        {
            // Save previous tab content
            if (_currentTab != null)
            {
                // fire-and-forget: update stored content from editor
                _ = GetEditorTextAsync().ContinueWith(t => { if (t.Status == TaskStatus.RanToCompletion) _currentTab.Content = t.Result; });
            }

            _currentTab = tab;
            FileNameLabel.Text = $"📝 {tab.FileName}{(tab.IsModified ? " •" : "")}";

            // If the WebView hasn't loaded an editor yet, set its HTML with initial content
            var lang = GetLanguageForFileName(tab.FileName);
            var html = GenerateCodeEditorHtml(tab.Content, lang);
            CodeWebView.Source = new HtmlWebViewSource { Html = html };

            // Update UI (will be refreshed once editor reports content)
            _ = UpdateStatsAsync();
            _ = UpdateLineNumbersAsync();
        }

        private void UpdateTabBar()
        {
            TabBar.Clear();
            foreach (var tab in _tabs)
            {
                var tabButton = new Border
                {
                    StrokeThickness = 0,
                    Padding = new Thickness(12, 6),
                    BackgroundColor = tab == _currentTab ? Color.FromArgb("#1e1e1e") : Color.FromArgb("#2d2d30"),
                    Margin = new Thickness(0, 0, 2, 0)
                };

                var stackLayout = new HorizontalStackLayout { Spacing = 8 };

                var label = new Label
                {
                    Text = tab.FileName + (tab.IsModified ? " •" : ""),
                    TextColor = Colors.White,
                    VerticalOptions = LayoutOptions.Center,
                    FontSize = 12
                };

                var closeButton = new Button
                {
                    Text = "✖️",
                    FontSize = 10,
                    Padding = new Thickness(4, 2),
                    BackgroundColor = Colors.Transparent,
                    TextColor = Colors.Gray
                };

                closeButton.Clicked += (s, e) => CloseTab(tab);

                // If using WebView-based editor, clicking label should load content into editor
                label.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => SwitchToTab(tab)) });

                stackLayout.Children.Add(label);
                stackLayout.Children.Add(closeButton);
                tabButton.Content = stackLayout;

                var tapGesture = new TapGestureRecognizer();
                tapGesture.Tapped += (s, e) => SwitchToTab(tab);
                tabButton.GestureRecognizers.Add(tapGesture);

                TabBar.Children.Add(tabButton);
            }
        }

        private async void CloseTab(EditorTab tab)
        {
            if (tab.IsModified)
            {
                bool answer = await DisplayAlert("Unsaved Changes", $"Save changes to {tab.FileName}?", "Yes", "No");
                if (answer)
                {
                    await SaveTab(tab);
                }
            }

            _tabs.Remove(tab);

            if (_tabs.Count == 0)
            {
                CreateNewTab();
            }
            else if (tab == _currentTab)
            {
                SwitchToTab(_tabs[0]);
            }

            UpdateTabBar();
        }

        private void OnTextChanged(object? sender, TextChangedEventArgs e)
        {
            if (_currentTab != null)
            {
                _currentTab.IsModified = true;
            }
            UpdateStats();
            UpdateLineNumbers();
            UpdateTabBar();
            // Update preview if visible
            if (CodeWebView != null && CodeWebView.IsVisible)
            {
                UpdatePreview();
            }
        }

        private void UpdateStats()
        {
            var text = TextEditor.Text ?? string.Empty;
            var charCount = text.Length;
            var lines = text.Split('\n');
            var lineCount = lines.Length;
            var wordCount = string.IsNullOrWhiteSpace(text) ? 0 : text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;

            StatsLabel.Text = $"Chars: {charCount} | Words: {wordCount} | Lines: {lineCount}";

            var cursorPosition = TextEditor.CursorPosition;
            var textBeforeCursor = text.Substring(0, Math.Min(cursorPosition, text.Length));
            var currentLine = textBeforeCursor.Split('\n').Length;
            var lastNewLine = textBeforeCursor.LastIndexOf('\n');
            var currentColumn = lastNewLine >= 0 ? cursorPosition - lastNewLine : cursorPosition + 1;

            PositionLabel.Text = $"Ln {currentLine}, Col {currentColumn}";
        }

        private void UpdateLineNumbers()
        {
            var text = TextEditor?.Text ?? string.Empty;
            var lineCount = text.Split('\n').Length;

            LineNumbers.Clear();
            for (int i = 1; i <= lineCount; i++)
            {
                LineNumbers.Children.Add(new Label
                {
                    Text = i.ToString(),
                    TextColor = Color.FromArgb("#858585"),
                    FontFamily = "CourierNew",
                    FontSize = _fontSize,
                    HorizontalOptions = LayoutOptions.End,
                    Margin = new Thickness(0, 0, 5, 0)
                });
            }
        }

        private void OnNewClicked(object? sender, EventArgs e)
        {
            CreateNewTab();
        }

        private async void OnOpenClicked(object? sender, EventArgs e)
        {
            try
            {
                var customFileType = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.iOS, new[] { "public.plain-text", "public.source-code" } },
                    { DevicePlatform.Android, new[] { "text/plain", "text/*" } },
                    { DevicePlatform.WinUI, new[] { ".ct", ".oil", ".ctproj", ".cs", ".json", ".xml", ".md", ".txt" } },
                    { DevicePlatform.macOS, new[] { "ct", "oil", "ctproj", "cs", "json", "xml", "md", "txt" } }
                });

                var result = await FilePicker.PickAsync(new PickOptions
                {
                    PickerTitle = "Select a Contract file",
                    FileTypes = customFileType
                });

                if (result != null)
                {
                    using var stream = await result.OpenReadAsync();
                    using var reader = new StreamReader(stream);
                    var content = await reader.ReadToEndAsync();

                    var newTab = new EditorTab
                    {
                        FileName = result.FileName,
                        Content = content,
                        FilePath = result.FullPath,
                        IsModified = false
                    };

                    _tabs.Add(newTab);
                    SwitchToTab(newTab);
                    UpdateTabBar();
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to open file: {ex.Message}", "OK");
            }
        }

        private async void OnSaveClicked(object? sender, EventArgs e)
        {
            if (_currentTab != null)
            {
                _currentTab.Content = await GetEditorTextAsync();
                await SaveTab(_currentTab);
            }
        }

        private async Task SaveTab(EditorTab tab)
        {
            try
            {
                var fileName = await DisplayPromptAsync("Save File", "Enter file name:", initialValue: tab.FileName, placeholder: "filename.ct");

                if (string.IsNullOrWhiteSpace(fileName))
                    return;

                var filePath = string.IsNullOrEmpty(tab.FilePath) 
                    ? Path.Combine(FileSystem.AppDataDirectory, fileName)
                    : Path.Combine(Path.GetDirectoryName(tab.FilePath) ?? FileSystem.AppDataDirectory, fileName);

                await File.WriteAllTextAsync(filePath, tab.Content);

                tab.FileName = fileName;
                tab.FilePath = filePath;
                tab.IsModified = false;

                FileNameLabel.Text = $"📝 {tab.FileName}";
                UpdateTabBar();

                await DisplayAlert("Success", $"Saved to: {filePath}", "OK");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to save file: {ex.Message}", "OK");
            }
        }

        private void OnFindClicked(object? sender, EventArgs e)
        {
            FindPanel.IsVisible = !FindPanel.IsVisible;
            if (FindPanel.IsVisible)
            {
                FindEntry.Focus();
            }
        }

        private void OnCloseFindClicked(object? sender, EventArgs e)
        {
            FindPanel.IsVisible = false;
        }

        private void OnFindNextClicked(object? sender, EventArgs e)
        {
            FindText(forward: true);
        }

        private void OnFindPreviousClicked(object? sender, EventArgs e)
        {
            FindText(forward: false);
        }

        private void FindText(bool forward)
        {
            var searchText = FindEntry.Text;
            if (string.IsNullOrEmpty(searchText))
                return;

            var text = TextEditor.Text ?? string.Empty;
            var startIndex = forward ? TextEditor.CursorPosition : TextEditor.CursorPosition - 1;

            var index = forward 
                ? text.IndexOf(searchText, startIndex, StringComparison.OrdinalIgnoreCase)
                : text.LastIndexOf(searchText, startIndex, StringComparison.OrdinalIgnoreCase);

            if (index >= 0)
            {
                TextEditor.CursorPosition = index;
                TextEditor.Focus();
            }
            else
            {
                DisplayAlert("Find", "No more matches found", "OK");
            }
        }

        private void OnReplaceAllClicked(object? sender, EventArgs e)
        {
            var searchText = FindEntry.Text;
            var replaceText = ReplaceEntry.Text ?? string.Empty;

            if (string.IsNullOrEmpty(searchText))
                return;

            var text = TextEditor.Text ?? string.Empty;
            var newText = text.Replace(searchText, replaceText, StringComparison.OrdinalIgnoreCase);

            var count = (text.Length - newText.Length) / Math.Max(1, searchText.Length - replaceText.Length);
            TextEditor.Text = newText;

            DisplayAlert("Replace All", $"Replaced {count} occurrence(s)", "OK");
        }

        private void OnFontSizeIncreaseClicked(object? sender, EventArgs e)
        {
            _fontSize = Math.Min(_fontSize + 2, 32);
            TextEditor.FontSize = _fontSize;
            UpdateLineNumbers();
        }

        private void OnFontSizeDecreaseClicked(object? sender, EventArgs e)
        {
            _fontSize = Math.Max(_fontSize - 2, 8);
            TextEditor.FontSize = _fontSize;
            UpdateLineNumbers();
        }

        private void OnPreviewClicked(object? sender, EventArgs e)
        {
            if (CodeWebView == null)
                return;

            // Toggle preview visibility
            CodeWebView.IsVisible = !CodeWebView.IsVisible;
            TextEditor.IsVisible = !CodeWebView.IsVisible;

            if (CodeWebView.IsVisible)
            {
                UpdatePreview();
            }
        }

        private void OnWebViewNavigating(object? sender, WebNavigatingEventArgs e)
        {
            // Could show activity indicator here
        }

        private void OnWebViewNavigated(object? sender, WebNavigatedEventArgs e)
        {
            // Could hide activity indicator here
        }

        private async void OnRunClicked(object? sender, EventArgs e)
        {
            if (_currentTab == null) return;

            RunResultsPanel.IsVisible = true;
            RunResultsEditor.Text = "Running...\n";

            try
            {
                var content = await GetEditorTextAsync();
                var tempFile = Path.Combine(Path.GetTempPath(), _currentTab.FileName);
                await File.WriteAllTextAsync(tempFile, content);

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ccl",
                    Arguments = $"\"{tempFile}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(psi);
                if (process == null)
                {
                    RunResultsEditor.Text = "Error: Could not start ccl process";
                    return;
                }

                var stdout = await process.StandardOutput.ReadToEndAsync();
                var stderr = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                var output = "";
                if (!string.IsNullOrEmpty(stdout)) output += stdout;
                if (!string.IsNullOrEmpty(stderr)) output += (output.Length > 0 ? "\n" : "") + stderr;
                if (string.IsNullOrEmpty(output)) output = "Process exited with code " + process.ExitCode;

                RunResultsEditor.Text = output;
            }
            catch (Exception ex)
            {
                RunResultsEditor.Text = $"Error: {ex.Message}\n\nMake sure 'ccl' is on your PATH.";
            }
        }

        private void OnCloseRunResultsClicked(object? sender, EventArgs e)
        {
            RunResultsPanel.IsVisible = false;
        }

        private void UpdatePreview()
        {
            var code = TextEditor.Text ?? string.Empty;
            var language = GetLanguageForFileName(_currentTab?.FileName ?? string.Empty);
            var html = GenerateHighlightedHtml(code, language);
            CodeWebView.Source = new HtmlWebViewSource { Html = html };
        }

        // Two-way sync helpers for CodeMirror in WebView
        private async Task<string> GetEditorTextAsync()
        {
            try
            {
                if (CodeWebView == null)
                    return TextEditor?.Text ?? string.Empty;

                // Ask the WebView's JS for the content; if not available, fallback to Editor
                var result = await CodeWebView.EvaluateJavaScriptAsync("(window.getCode && window.getCode()) || null");
                if (!string.IsNullOrWhiteSpace(result) && result != "null")
                {
                    // result comes quoted, remove surrounding quotes if present
                    return TrimJsString(result);
                }
            }
            catch
            {
                // ignore and fallback
            }

            return TextEditor?.Text ?? string.Empty;
        }

        private static string TrimJsString(string s)
        {
            if (s.Length >= 2 && ((s[0] == '"' && s[^1] == '"') || (s[0] == '\'' && s[^1] == '\'')))
                return s.Substring(1, s.Length - 2).Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\\"", "\"");
            return s;
        }

        private static string GetLanguageForFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return "plaintext";

            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            return ext switch
            {
                ".ct" => "contract",
                ".cs" => "csharp",
                ".xaml" => "xml",
                ".xml" => "xml",
                ".json" => "json",
                ".html" => "xml",
                ".htm" => "xml",
                ".md" => "markdown",
                ".txt" => "plaintext",
                _ => "plaintext",
            };
        }

        private static string GenerateHighlightedHtml(string code, string language)
        {
            // Use highlight.js from CDN to highlight code. Keep minimal CSS for dark greyscale theme.
            var escaped = EscapeForHtml(code);
            var template = "<!doctype html>\n" +
                           "<html>\n" +
                           "<head>\n" +
                           "  <meta charset='utf-8'>\n" +
                           "  <meta name='viewport' content='width=device-width, initial-scale=1'>\n" +
                           "  <link rel='stylesheet' href='https://cdnjs.cloudflare.com/ajax/libs/highlight.js/11.8.0/styles/monokai.min.css'>\n" +
                           "  <script src='https://cdnjs.cloudflare.com/ajax/libs/highlight.js/11.8.0/highlight.min.js'></script>\n" +
                           "  <script>\n" +
                           "    hljs.registerLanguage('contract', function (hljs) {\n" +
                           "      return {\n" +
                           "        keywords: {\n" +
                           "          keyword: 'Contract fn fun let var if else while for switch case return break continue new static public private protected internal constructor struct import export type throw try catch finally',\n" +
                           "          type: 'int int64 long byte sbyte short ushort uint string bool double float object void',\n" +
                           "          literal: 'true false null'\n" +
                           "        },\n" +
                           "        contains: [\n" +
                           "          hljs.C_LINE_COMMENT_MODE,\n" +
                           "          hljs.QUOTE_STRING_MODE,\n" +
                           "          hljs.APOS_STRING_MODE,\n" +
                           "          hljs.C_NUMBER_MODE,\n" +
                           "          { className: 'title.function', begin: /[A-Za-z_][A-Za-z0-9_]*(?=\\s*\\()/ }\n" +
                           "        ]\n" +
                           "      };\n" +
                           "    });\n" +
                           "  </script>\n" +
                           "  <script>hljs.highlightAll();</script>\n" +
                           "  <style> body { background: #141414; color: #e0e0e0; padding:10px; font-family: Consolas, 'Courier New', monospace; } pre { white-space: pre-wrap; word-wrap: break-word; }</style>\n" +
                           "</head>\n" +
                           "<body>\n" +
                           "  <pre><code class='language-" + language + "'>\n" +
                           escaped + "\n" +
                           "  </code></pre>\n" +
                           "</body>\n" +
                           "</html>";

            return template;
        }

        private static string EscapeForHtml(string s)
        {
            if (string.IsNullOrEmpty(s))
                return string.Empty;
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;");
        }

        // Generate HTML that hosts CodeMirror for an editable syntax highlighted editor
        private static string GenerateCodeEditorHtml(string code, string language)
        {
            // Escape code for JavaScript string literal
            var escaped = EscapeForHtml(code).Replace("\r", "\\r").Replace("\n", "\\n").Replace("\"", "\\\"");

            var template = "<!doctype html>\n" +
                           "<html>\n" +
                           "<head>\n" +
                           "  <meta charset='utf-8'>\n" +
                           "  <meta name='viewport' content='width=device-width, initial-scale=1'>\n" +
                           "  <link rel='stylesheet' href='https://cdnjs.cloudflare.com/ajax/libs/codemirror/5.65.13/codemirror.min.css'>\n" +
                           "  <script src='https://cdnjs.cloudflare.com/ajax/libs/codemirror/5.65.13/codemirror.min.js'></script>\n" +
                           "  <script src='https://cdnjs.cloudflare.com/ajax/libs/codemirror/5.65.13/mode/javascript/javascript.min.js'></script>\n" +
                           "  <script src='https://cdnjs.cloudflare.com/ajax/libs/codemirror/5.65.13/mode/clike/clike.min.js'></script>\n" +
                           "  <script src='https://cdnjs.cloudflare.com/ajax/libs/codemirror/5.65.13/mode/xml/xml.min.js'></script>\n" +
                           "  <script src='https://cdnjs.cloudflare.com/ajax/libs/codemirror/5.65.13/mode/markdown/markdown.min.js'></script>\n" +
                           "  <script src='https://cdnjs.cloudflare.com/ajax/libs/codemirror/5.65.13/addon/edit/matchbrackets.min.js'></script>\n" +
                           "  <style>html, body { height: 100%; margin:0; background:#141414; color:#e0e0e0; } .CodeMirror { height: auto; min-height: 100vh; background:#141414; color:#e0e0e0; font-family: monospace; }</style>\n" +
                           "  <script>\n" +
                           "    function words(str) { var o = {}; str.split(' ').forEach(function (w) { o[w] = true; }); return o; }\n" +
                           "    CodeMirror.defineMIME('text/x-contract', {\n" +
                           "      name: 'clike',\n" +
                           "      keywords: words('Contract fn fun let var if else while for switch case return break continue new static public private protected internal constructor struct import export type throw try catch finally'),\n" +
                           "      types: words('int int64 long byte sbyte short ushort uint string bool double float object void'),\n" +
                           "      atoms: words('true false null')\n" +
                           "    });\n" +
                           "    var editor;\n" +
                           "    function initializeEditor(content, language) {\n" +
                           "      var mode = 'null';\n" +
                           "      if (language === 'contract') mode = 'text/x-contract';\n" +
                           "      else if (language === 'csharp') mode = 'text/x-csharp';\n" +
                           "      else if (language === 'xml') mode = 'application/xml';\n" +
                           "      else if (language === 'json') mode = JSON.parse('{\"name\":\"javascript\",\"json\":true}');\n" +
                           "      else if (language === 'markdown') mode = 'markdown';\n" +
                           "      editor = CodeMirror(document.body, { value: content, lineNumbers: true, mode: mode, matchBrackets: true, theme: 'default' });\n" +
                           "    }\n" +
                           "    function getCode() { if (!editor) return null; return editor.getValue(); }\n" +
                           "    function setCode(s) { if (editor) editor.setValue(s); }\n" +
                           "  </script>\n" +
                           "</head>\n" +
                           "<body>\n" +
                           "  <script>initializeEditor(\"__ESCAPED__\", \"__LANG__\");</script>\n" +
                           "</body>\n" +
                           "</html>\n";

            return template.Replace("__ESCAPED__", escaped).Replace("__LANG__", language);
        }

        private async Task UpdateStatsAsync()
        {
            var text = await GetEditorTextAsync();
            var charCount = text.Length;
            var lines = text.Split('\n');
            var lineCount = lines.Length;
            var wordCount = string.IsNullOrWhiteSpace(text) ? 0 : text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
            
            StatsLabel.Text = $"Chars: {charCount} | Words: {wordCount} | Lines: {lineCount}";
        }

        private async Task UpdateLineNumbersAsync()
        {
            var text = await GetEditorTextAsync();
            var lineCount = text.Split('\n').Length;

            LineNumbers.Clear();
            for (int i = 1; i <= lineCount; i++)
            {
                LineNumbers.Children.Add(new Label
                {
                    Text = i.ToString(),
                    TextColor = Color.FromArgb("#858585"),
                    FontFamily = "CourierNew",
                    FontSize = _fontSize,
                    HorizontalOptions = LayoutOptions.End,
                    Margin = new Thickness(0, 0, 5, 0)
                });
            }
        }
    }
}
