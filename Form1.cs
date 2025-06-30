// Main form for AQtion Demo Launcher.
// Handles UI, demo browsing/downloading, Q2PRO management, and launching demos.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using HtmlAgilityPack;
using System.Diagnostics;
using System.Drawing;
using System.IO.Compression;
using NuGet.Versioning; // Add this at the top of your file

namespace AQtionDemoLauncher
{
    public partial class Form1 : Form
    {
        // Path to the selected q2pro.exe (Quake 2 engine)
        private string q2proPath = string.Empty;

        // Directory containing q2pro.exe (root of Q2PRO install)
        private string q2proDir = string.Empty;

        // Base URL for demo files (root of remote demo repository)
        private readonly Dictionary<string, string> demoSources;
        private readonly string q2proZipUrl;
        private readonly string mapZipUrlPattern;
        private readonly string s3BucketRoot;
        private readonly string updateApiUrl;
        private readonly string currentVersionString = "1.0.0"; // Use your current version string here

        // The current folder URL being browsed in the demo list
        private string currentFolderUrl = string.Empty;

        // Maps display names in the UI to actual file/folder names for download/navigation
        private Dictionary<string, string> demoMap = new();

        // Stack to keep track of folder navigation history for the Back button
        private Stack<string> folderHistory = new();

        // Label showing the current folder path as a breadcrumb
        private Label breadcrumbLabel = new();

        // UI controls for demo list and actions
        private ListBox demoListBox = new();
        private ContextMenuStrip demoContextMenu = new ContextMenuStrip();
        private Button chooseButton = new();
        private Button playButton = new();
        private Button refreshButton = new();
        private Button backButton = new();
        private Button downloadQ2proButton = new();
        private readonly Button removeQ2proButton = new();
        private ProgressBar downloadProgressBar = new();
        private Label statusLabel = new();
        private TextBox filterTextBox = new();
        private List<string> allDemoDisplayNames = new();
        // Local folder where downloaded demos are stored (relative to q2pro.exe)
        private string demoDownloadRoot = string.Empty;
        private string currentDemoSourceUrl = "";
        private string rootDemoUrl = "";

        // Sort order flag
        private bool sortDescending = false;

        private Button? sortButton; // Add this as a field at the top with other controls

        public Form1()
        {
            // DEBUG: List all embedded resources before anything else
            // var names = typeof(Form1).Assembly.GetManifestResourceNames();
            // MessageBox.Show(string.Join("\n", names));

            // Now try to load config from file, fallback to embedded resource if missing
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string configPath = Path.Combine(exeDir, "appsettings.json");
            string configText;

            if (File.Exists(configPath))
            {
                configText = File.ReadAllText(configPath);
            }
            else
            {
                // Fallback to embedded resource
                var asm = typeof(Form1).Assembly;
                using var stream = asm.GetManifestResourceStream("AQtionDemoLauncher.appsettings.json");
                if (stream == null)
                {
                    MessageBox.Show("Could not find appsettings.json (file or embedded resource).", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    throw new FileNotFoundException("appsettings.json not found as file or embedded resource.");
                }
                using var reader = new StreamReader(stream);
                configText = reader.ReadToEnd();
            }

            try
            {
                var config = System.Text.Json.JsonDocument.Parse(configText).RootElement;

                demoSources = config.GetProperty("DemoSources")
                    .EnumerateObject()
                    .ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");

                q2proZipUrl = config.GetProperty("Q2ProZipUrl").GetString() ?? "";
                mapZipUrlPattern = config.GetProperty("MapZipUrlPattern").GetString() ?? "";
                s3BucketRoot = config.GetProperty("S3BucketRoot").GetString() ?? "";
                updateApiUrl = config.GetProperty("UpdateApiUrl").GetString() ?? "";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Config load error: " + ex.ToString());
                throw;
            }

            InitializeComponent();

            // Set window icon if available
            // if (File.Exists("icon.ico"))
            //     this.Icon = new Icon("icon.ico");

            // Apply Quake 2 themed colors/fonts to the UI
            ApplyQuake2Theme();

            // Set initial demo source and root
            currentDemoSourceUrl = demoSources.Values.First();
            rootDemoUrl = currentDemoSourceUrl;
            currentFolderUrl = currentDemoSourceUrl;

            // On form shown: check for Q2PRO, set up demo folder, and load demo list
            this.Shown += async (s, e) =>
            {
                EnsureQ2Pro();
                await LoadDemoListAsync(currentFolderUrl);
                await CheckForUpdatesAsync();
            };
        }

        /// Sets up all UI controls, their layout, and event handlers.
        private void InitializeComponent()
        {
            this.Text = "AQtion Demo Launcher";
            this.ClientSize = new Size(800, 600);
            this.MinimumSize = new Size(320, 240);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = true;

            int rightPanelX = 800 - 112; // 12px margin, 100px button width
            int buttonW = 100, buttonH = 30, buttonSpacing = 10;
            int buttonY = 40;

            // --- Breadcrumb Label ---
            breadcrumbLabel.Location = new Point(12, 12);
            breadcrumbLabel.Size = new Size(660, 20);
            breadcrumbLabel.Font = new Font("Consolas", 9, FontStyle.Bold);
            breadcrumbLabel.ForeColor = Color.Orange;
            breadcrumbLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            this.Controls.Add(breadcrumbLabel);

            // --- Filter TextBox ---
            filterTextBox.Location = new Point(12, 36);
            filterTextBox.Size = new Size(300, 24);
            filterTextBox.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            filterTextBox.PlaceholderText = "Filter demos...";
            filterTextBox.TextChanged += FilterTextBox_TextChanged;
            this.Controls.Add(filterTextBox);

            // --- Sort Button ---
            sortButton = new Button();
            sortButton.Text = "Sort Z–A";
            sortButton.Size = new Size(80, 24);
            sortButton.Location = new Point(filterTextBox.Right + 8, filterTextBox.Top);
            sortButton.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            sortButton.Click += (s, e) => ToggleSortOrder();
            this.Controls.Add(sortButton);

            // --- Demo List ---
            demoListBox.Location = new Point(12, 36 + 28);
            demoListBox.Size = new Size(660, 480);
            demoListBox.Font = new Font("Consolas", 9);
            demoListBox.BorderStyle = BorderStyle.None;
            demoListBox.HorizontalScrollbar = true;
            demoListBox.SelectedIndexChanged += DemoListBox_SelectedIndexChanged;
            demoListBox.MouseDoubleClick += (s, e) => PlayButton_Click(s, e);
            demoListBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            this.Controls.Add(demoListBox);
            var copyUrlItem = new ToolStripMenuItem("Copy download URL");
            copyUrlItem.Click += (s, e) => CopyDemoUrlForSelectedItem();
            demoContextMenu.Items.Add(copyUrlItem);
            demoListBox.ContextMenuStrip = demoContextMenu;
            demoContextMenu.Items.Add(copyUrlItem);

            // --- Q2PRO Download Button ---
            downloadQ2proButton.Text = "Download";
            downloadQ2proButton.Location = new Point(rightPanelX, buttonY);
            downloadQ2proButton.Size = new Size(buttonW, buttonH);
            downloadQ2proButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            downloadQ2proButton.Click += async (s, e) => await DownloadQ2ProAndSetAsync();
            this.Controls.Add(downloadQ2proButton);

            // --- Choose Q2PRO Button ---
            chooseButton.Text = "Choose";
            chooseButton.Location = new Point(rightPanelX, buttonY += buttonH + buttonSpacing);
            chooseButton.Size = new Size(buttonW, buttonH);
            chooseButton.Click += ChooseButton_Click;
            chooseButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            this.Controls.Add(chooseButton);

            // --- Play Demo Button ---
            playButton.Text = "Play Demo";
            playButton.Location = new Point(rightPanelX, buttonY += buttonH + buttonSpacing);
            playButton.Size = new Size(buttonW, buttonH);
            playButton.Click += PlayButton_Click;
            playButton.Enabled = false;
            playButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            this.Controls.Add(playButton);

            // --- Refresh Button ---
            refreshButton.Text = "Refresh";
            refreshButton.Location = new Point(rightPanelX, buttonY += buttonH + buttonSpacing);
            refreshButton.Size = new Size(buttonW, buttonH);
            refreshButton.Click += async (s, e) => await LoadDemoListAsync(currentFolderUrl);
            refreshButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            this.Controls.Add(refreshButton);

            // --- Back Button ---
            backButton.Text = "Back";
            backButton.Location = new Point(rightPanelX, buttonY += buttonH + buttonSpacing);
            backButton.Size = new Size(buttonW, buttonH);
            backButton.Click += BackButton_Click;
            backButton.Enabled = false;
            backButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            this.Controls.Add(backButton);

            // --- Remove Q2PRO Button ---
            removeQ2proButton.Text = "Remove";
            removeQ2proButton.Location = new Point(rightPanelX, buttonY += buttonH + buttonSpacing);
            removeQ2proButton.Size = new Size(buttonW, buttonH);
            removeQ2proButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            removeQ2proButton.Click += RemoveQ2proButton_Click;
            this.Controls.Add(removeQ2proButton);

            // --- Download Progress Bar ---
            downloadProgressBar.Location = new Point(12, 560);
            downloadProgressBar.Size = new Size(776, 20);
            downloadProgressBar.ForeColor = Color.Orange;
            downloadProgressBar.BackColor = Color.FromArgb(30, 30, 30);
            downloadProgressBar.Minimum = 0;
            downloadProgressBar.Maximum = 100;
            downloadProgressBar.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            this.Controls.Add(downloadProgressBar);

            // --- Status Label ---
            statusLabel.Location = new Point(12, 540);
            statusLabel.Size = new Size(776, 20);
            statusLabel.ForeColor = Color.White;
            statusLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            this.Controls.Add(statusLabel);

            // --- Source ComboBox ---
            ComboBox sourceComboBox = new ComboBox();
            sourceComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            sourceComboBox.Location = new Point(rightPanelX, buttonY += buttonH + buttonSpacing);
            sourceComboBox.Size = new Size(buttonW, buttonH);
            sourceComboBox.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            sourceComboBox.Font = new Font("Consolas", 9);
            sourceComboBox.ForeColor = Color.Orange;
            sourceComboBox.BackColor = Color.FromArgb(30, 30, 30);
            sourceComboBox.FlatStyle = FlatStyle.Popup;
            sourceComboBox.Items.AddRange(demoSources.Keys.ToArray());
            sourceComboBox.SelectedIndex = 0; // Default selection
            sourceComboBox.SelectedIndexChanged += SourceComboBox_SelectedIndexChanged;
            this.Controls.Add(sourceComboBox);

            // --- Set dark background for the whole form ---
            this.BackColor = Color.FromArgb(20, 20, 20);
        }

        /// Applies Quake 2 themed colors and fonts to all controls.
        private void ApplyQuake2Theme()
        {
            // Colors/fonts for quake2 vibe
            this.ForeColor = Color.Orange;
            this.Font = new Font("Consolas", 9);
            this.BackColor = Color.FromArgb(20, 20, 20);
            foreach (Control c in this.Controls)
            {
                if (c is Button)
                {
                    c.BackColor = Color.FromArgb(50, 50, 50);
                    c.ForeColor = Color.Orange;
                    ((Button)c).FlatStyle = FlatStyle.Flat;
                    ((Button)c).FlatAppearance.BorderColor = Color.Orange;
                }
                else if (c is Label)
                {
                    c.ForeColor = Color.Orange;
                }
                else if (c is ListBox)
                {
                    c.BackColor = Color.FromArgb(30, 30, 30);
                    c.ForeColor = Color.Orange;
                    ((ListBox)c).HorizontalScrollbar = true;
                }
                else if (c is ProgressBar)
                {
                    c.ForeColor = Color.Orange;
                    c.BackColor = Color.FromArgb(30, 30, 30);
                }
            }
        }

        /// Handles source selection change: updates current demo source URL and reloads demo list.
        private void FilterTextBox_TextChanged(object? sender, EventArgs e)
        {
            string filter = filterTextBox.Text.Trim().ToLowerInvariant();
            demoListBox.Items.Clear();

            foreach (var displayName in allDemoDisplayNames)
            {
                if (displayName.ToLowerInvariant().Contains(filter))
                    demoListBox.Items.Add(displayName);
            }
        }

        /// If the demo is compressed (.gz), decompresses it and returns the path to the uncompressed file.
        /// Otherwise, returns the original path.
        private string EnsureDemoIsUncompressed(string demoPath)
        {
            if (demoPath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            {
                string extractedPath = demoPath.Substring(0, demoPath.Length - 3); // remove '.gz'
                if (!File.Exists(extractedPath))
                {
                    using (var inStream = File.OpenRead(demoPath))
                    using (var gzStream = new GZipStream(inStream, CompressionMode.Decompress))
                    using (var outStream = File.Create(extractedPath))
                    {
                        gzStream.CopyTo(outStream);
                    }
                }
                return extractedPath;
            }
            return demoPath;
        }

        /// Ensures the required map .pkz file is present in the Q2PRO install.
        /// Downloads and renames the map zip if missing.
        private async Task<bool> EnsureMapPresentAsync(string mapName)
        {
            string actionDir = Path.Combine(q2proDir, "action");
            string pkzPath = Path.Combine(actionDir, $"{mapName}.pkz");
            // If .pkz already exists, no need to download
            if (File.Exists(pkzPath))
                return true;
            // Download the zip and rename to pkz
            string zipUrl = string.Format(mapZipUrlPattern, mapName);
            string tempZipPath = Path.Combine(actionDir, $"{mapName}.zip");
            try
            {
                using (HttpClient client = new())
                using (var response = await client.GetAsync(zipUrl))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        statusLabel.Text = $"Map package for {mapName} not found.";
                        return false;
                    }
                    using (var fs = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write))
                    {
                        await response.Content.CopyToAsync(fs);
                    }
                }
                // Rename zip to pkz
                if (File.Exists(pkzPath))
                    File.Delete(pkzPath);
                File.Move(tempZipPath, pkzPath);
                statusLabel.Text = $"Map package {mapName}.pkz downloaded.";
                return true;
            }
            catch (Exception ex)
            {
                statusLabel.Text = $"Failed to download map package: {ex.Message}";
                return false;
            }
        }

        /// Loads the list of demos and folders from the given URL into the UI.
        private async Task LoadDemoListAsync(string folderUrl)
        {
            if (folderUrl.Contains("s3.amazonaws.com"))
            {
                await LoadS3DemoListAsync(folderUrl);
                return;
            }
            try
            {
                // Prevent loading if outside root URL
                if (!IsUrlInsideRoot(folderUrl, rootDemoUrl))
                {
                    MessageBox.Show("Cannot browse outside the root demo folder.", "Navigation blocked", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                statusLabel.Text = "Fetching demos...";
                demoListBox.Items.Clear();
                demoMap.Clear();
                var web = new HtmlWeb();
                var doc = await Task.Run(() => web.Load(folderUrl));
                var links = doc.DocumentNode.SelectNodes("//a[@href]")
                    ?.Select(n => n.GetAttributeValue("href", ""))
                    .Where(h =>
                        h != "../" &&
                        h != ".." &&
                        h != "./" &&
                        h != "." &&
                        !h.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                        !h.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                        !h.Contains("browsehappy.com") &&
                        !h.Contains("larsjung.de/h5ai")
                    )
                    .ToList();
                if (links == null)
                {
                    statusLabel.Text = "No files or folders found.";
                    return;
                }
                var folders = links.Where(l => l.EndsWith("/"))
                    .OrderBy(l => l.TrimEnd('/').Split('/').Last(), StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var files = links.Where(l => !l.EndsWith("/"))
                    .OrderBy(l => Path.GetFileName(l), StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var folder in folders)
                {
                    // Only show the immediate folder name
                    string folderName = folder.TrimEnd('/').Split('/').Last();
                    demoListBox.Items.Add("[DIR] " + folderName);
                    demoMap["[DIR] " + folderName] = folder;
                }
                foreach (var file in files)
                {
                    // Only allow .gz, .mvd2, .dm2 (case-insensitive)
                    if (!(file.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ||
                          file.EndsWith(".mvd2", StringComparison.OrdinalIgnoreCase) ||
                          file.EndsWith(".dm2", StringComparison.OrdinalIgnoreCase)))
                        continue;

                    string absoluteUrl = CombineUrl(folderUrl, file);
                    string relative = absoluteUrl.StartsWith(rootDemoUrl)
                        ? absoluteUrl.Substring(rootDemoUrl.Length)
                        : absoluteUrl;
                    string fileNameOnly = Path.GetFileName(file);
                    string localPath = Path.Combine(demoDownloadRoot, fileNameOnly);
                    string visual = File.Exists(localPath) ? "✓ " + fileNameOnly : fileNameOnly;
                    demoListBox.Items.Add(visual);
                    demoMap[visual] = file;
                    allDemoDisplayNames.Add(visual);
                }
                // NEW: update allDemoDisplayNames and apply filter
                allDemoDisplayNames = demoListBox.Items.Cast<string>().ToList();
                UpdateBreadcrumb(currentFolderUrl);

                statusLabel.Text = $"Loaded {demoMap.Count(kvp => kvp.Key.StartsWith("[DIR] "))} folders and {demoMap.Count(kvp => !kvp.Key.StartsWith("[DIR] "))} files.";
                backButton.Enabled = folderHistory.Count > 0;
                playButton.Enabled = false;
                downloadProgressBar.Value = 0;
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Failed to load folder.";
                MessageBox.Show($"Error loading demos:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// Updates the breadcrumb label to show the current folder path.
        private void UpdateBreadcrumb(string folderUrl)
        {
            try
            {
                if (folderUrl.Contains("s3.amazonaws.com"))
                {
                    string rel = folderUrl;
                    if (folderUrl.StartsWith(s3BucketRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        rel = folderUrl.Substring(s3BucketRoot.Length);
                    }
                    rel = rel.Trim('/');

                    var segments = rel.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    breadcrumbLabel.Text = segments.Length == 0
                        ? "Root"
                        : "Root / " + string.Join(" / ", segments);
                }
                else
                {
                    // Robustly remove the rootDemoUrl from folderUrl
                    string rel = folderUrl;
                    if (folderUrl.StartsWith(rootDemoUrl, StringComparison.OrdinalIgnoreCase))
                    {
                        rel = folderUrl.Substring(rootDemoUrl.Length);
                    }
                    rel = rel.Trim('/');

                    var segments = rel.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    breadcrumbLabel.Text = segments.Length == 0
                        ? "Root"
                        : "Root / " + string.Join(" / ", segments);
                }
            }
            catch
            {
                breadcrumbLabel.Text = folderUrl;
            }
        }

        /// Handles selection in the demo list: navigates into folders or enables Play for files.
        private async void DemoListBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (demoListBox.SelectedItem == null)
            {
                playButton.Enabled = false;
                return;
            }
            string selected = demoListBox.SelectedItem?.ToString() ?? string.Empty;
            if (selected.StartsWith("[DIR] "))
            {
                string folderName = demoMap[selected];
                folderHistory.Push(currentFolderUrl);

                if (currentDemoSourceUrl.Contains("s3.amazonaws.com"))
                {
                    // S3 navigation
                    string bucketRoot = s3BucketRoot;
                    string newUrl = bucketRoot.TrimEnd('/') + "/" + folderName.TrimStart('/');
                    currentFolderUrl = newUrl;
                    await LoadS3DemoListAsync(currentFolderUrl);
                    return;
                }
                else
                {
                    // vrol.se or other HTTP directory navigation
                    string newUrl = CombineUrl(currentFolderUrl, folderName);
                    currentFolderUrl = newUrl;
                    await LoadDemoListAsync(currentFolderUrl);
                    return;
                }
            }
            else
            {
                playButton.Enabled = true;
            }
        }

        private async Task LoadS3DemoListAsync(string url)
        {
            statusLabel.Text = "Fetching S3 demos...";
            demoListBox.Items.Clear();
            demoMap.Clear();

            string bucketRoot = s3BucketRoot;
            string prefix = url.Replace(bucketRoot, "").Trim('/');
            string listUrlBase;
            if (string.IsNullOrEmpty(prefix))
                listUrlBase = $"{bucketRoot}?list-type=2&delimiter=/";
            else
                listUrlBase = $"{bucketRoot}?list-type=2&prefix={Uri.EscapeDataString(prefix)}/&delimiter=/";
            string? continuationToken = null;

            do
            {
                string listUrl = listUrlBase;
                if (!string.IsNullOrEmpty(continuationToken))
                {
                    listUrl += $"&continuation-token={Uri.EscapeDataString(continuationToken)}";
                }

                using var client = new HttpClient();
                string xmlString = await client.GetStringAsync(listUrl);
                var doc = new System.Xml.XmlDocument();
                doc.LoadXml(xmlString);
                var nsmgr = new System.Xml.XmlNamespaceManager(doc.NameTable);
                nsmgr.AddNamespace("s3", "http://s3.amazonaws.com/doc/2006-03-01/");

                // --- Add folders ---
                var folderNodes = doc.SelectNodes("//s3:CommonPrefixes/s3:Prefix", nsmgr);
                if (folderNodes != null)
                {
                    var folderNames = new List<(string displayName, string folderKey)>();
                    foreach (System.Xml.XmlNode node in folderNodes)
                    {
                        string folderKey = node.InnerText;
                        if (folderKey == $"{prefix}/") continue;
                        string displayName = folderKey.Substring(prefix.Length).Trim('/').Split('/').Last();
                        folderNames.Add((displayName, folderKey));
                    }
                    foreach (var (displayName, folderKey) in folderNames.OrderBy(f => f.displayName, StringComparer.OrdinalIgnoreCase))
                    {
                        demoListBox.Items.Add($"[DIR] {displayName}");
                        demoMap[$"[DIR] {displayName}"] = folderKey;
                    }
                }

                // --- Add files ---
                var fileNodes = doc.SelectNodes("//s3:Contents/s3:Key", nsmgr);
                if (fileNodes != null)
                {
                    var fileNames = new List<(string displayName, string key)>();
                    foreach (System.Xml.XmlNode node in fileNodes)
                    {
                        string key = node.InnerText;
                        if (key.EndsWith("/")) continue;
                        if (!(key.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ||
                              key.EndsWith(".mvd2", StringComparison.OrdinalIgnoreCase) ||
                              key.EndsWith(".dm2", StringComparison.OrdinalIgnoreCase)))
                            continue;
                        string displayName = Path.GetFileName(key);
                        fileNames.Add((displayName, key));
                    }
                    foreach (var (displayName, key) in fileNames.OrderBy(f => f.displayName, StringComparer.OrdinalIgnoreCase))
                    {
                        demoListBox.Items.Add(displayName);
                        demoMap[displayName] = key;
                    }
                }

                // --- Check for continuation token ---
                var nextContinuationTokenNode = doc.SelectSingleNode("//s3:NextContinuationToken", nsmgr);
                continuationToken = nextContinuationTokenNode?.InnerText;
            } while (!string.IsNullOrEmpty(continuationToken));

            // --- Add this line to update allDemoDisplayNames and apply filter ---
            allDemoDisplayNames = demoListBox.Items.Cast<string>().ToList();

            statusLabel.Text = $"Loaded {demoMap.Count(kvp => kvp.Key.StartsWith("[DIR] "))} folders and {demoMap.Count(kvp => !kvp.Key.StartsWith("[DIR] "))} files from S3.";
            backButton.Enabled = prefix != "demos/aqtion/";
            playButton.Enabled = false;
            downloadProgressBar.Value = 0;
            currentFolderUrl = $"{bucketRoot}{prefix}/";
            UpdateBreadcrumb(currentFolderUrl);
        }

        /// Checks if a URL is inside the allowed root demo folder.
        private bool IsUrlInsideRoot(string urlToCheck, string rootUrl)
        {
            if (!rootUrl.EndsWith("/")) rootUrl += "/";
            return urlToCheck.StartsWith(rootUrl, StringComparison.OrdinalIgnoreCase);
        }

        /// Navigates back to the previous folder in history.
        private void BackButton_Click(object? sender, EventArgs e)
        {
            if (folderHistory.Count > 0)
            {
                string prevUrl = folderHistory.Pop();
                if (IsUrlInsideRoot(prevUrl, rootDemoUrl))
                {
                    currentFolderUrl = prevUrl;
                    _ = LoadDemoListAsync(currentFolderUrl);
                }
                else
                {
                    MessageBox.Show("Cannot navigate outside the root folder.", "Navigation blocked", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    folderHistory.Clear();
                    currentFolderUrl = rootDemoUrl;
                    _ = LoadDemoListAsync(currentFolderUrl);
                }
            }
        }

        /// Lets the user select a q2pro.exe manually and sets up paths.
        private void ChooseButton_Click(object? sender, EventArgs e)
        {
            using OpenFileDialog ofd = new OpenFileDialog();
            ofd.Filter = "Q2PRO executable (q2pro.exe)|q2pro.exe";
            ofd.Title = "Select q2pro.exe";
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                q2proPath = ofd.FileName;
                q2proDir = Path.GetDirectoryName(q2proPath) ?? string.Empty;
                demoDownloadRoot = Path.Combine(q2proDir, "action", "demos");
                if (!Directory.Exists(demoDownloadRoot))
                    Directory.CreateDirectory(demoDownloadRoot);
                statusLabel.Text = $"q2pro set: {q2proPath}";
                UpdateQ2ProButtons();
            }
        }

        /// Handles Play Demo: downloads demo if needed, ensures map, launches q2pro with demo.
        private async void PlayButton_Click(object? sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(q2proPath) || !File.Exists(q2proPath))
            {
                MessageBox.Show("Please choose or download q2pro.exe first!", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Prevent multiple q2pro instances
            if (IsQ2ProRunning())
            {
                MessageBox.Show("Q2PRO is already running. Please close it before starting a new demo.", "Already running", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (demoListBox.SelectedItem == null)
            {
                return;
            }
            string selected = demoListBox.SelectedItem?.ToString() ?? string.Empty;
            if (string.IsNullOrEmpty(selected) || selected.StartsWith("[DIR] "))
                return;

            // Remove "★ " and "✓ " prefixes if present
            if (selected.StartsWith("★ "))
                selected = selected.Substring(2);
            if (selected.StartsWith("✓ "))
                selected = selected.Substring(2);

            if (!demoMap.TryGetValue(selected, out var fileName))
            {
                MessageBox.Show("Could not find demo file for: " + selected, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Now use fileName instead of demoMap[selected] everywhere below
            string fileUrl;
            if (currentDemoSourceUrl.Contains("s3.amazonaws.com"))
            {
                // Remove the path part of s3BucketRoot from fileName if present
                var s3RootUri = new Uri(s3BucketRoot);
                var s3RootPath = s3RootUri.AbsolutePath.Trim('/');
                string key = fileName;
                if (!string.IsNullOrEmpty(s3RootPath) && key.StartsWith(s3RootPath + "/"))
                    key = key.Substring(s3RootPath.Length + 1);
                fileUrl = s3BucketRoot.TrimEnd('/') + "/" + key.TrimStart('/');
            }
            else
            {
                fileUrl = CombineUrl(currentFolderUrl, fileName);
            }
            string localPath = Path.Combine(demoDownloadRoot, Path.GetFileName(fileName));

            // Download the demo if it's missing
            if (!File.Exists(localPath))
            {
                statusLabel.Text = "Downloading demo...";
                downloadProgressBar.Value = 0;
                try
                {
                    await DownloadFileWithProgressAsync(fileUrl, localPath);
                    statusLabel.Text = "Download complete.";
                }
                catch (Exception ex)
                {
                    statusLabel.Text = "Download failed.";
                    MessageBox.Show($"Failed to download demo:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }
            else
            {
                statusLabel.Text = "Demo already downloaded.";
            }
            // ---- NEW: Uncompress if needed, extract mapname, download .pkz if missing ----
            string demoFileForParsing = EnsureDemoIsUncompressed(localPath);
            string? mapName = TryExtractMapName(demoFileForParsing);

            // Delete the uncompressed .mvd2 file if it was created from a .gz
            if (localPath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(demoFileForParsing) &&
                !string.Equals(localPath, demoFileForParsing, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    File.Delete(demoFileForParsing);
                }
                catch
                {
                    // Ignore errors if file is in use or locked
                }
            }

            if (!string.IsNullOrEmpty(mapName))
            {
                bool mapOk = await EnsureMapPresentAsync(mapName);
                if (!mapOk)
                {
                    MessageBox.Show(
                        $"Demo may require map '{mapName}' which could not be downloaded. The demo may not play correctly.",
                        "Map missing", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }

            string demoFileName = Path.GetFileName(demoMap[selected]);
            string argCmd = demoFileName.EndsWith(".dm2", StringComparison.OrdinalIgnoreCase)
                ? "+demo"
                : "+mvdplay";

            var psi = new ProcessStartInfo(q2proPath)
            {
                Arguments = $"+name AQtionDemoLauncher {argCmd} {demoFileName}",
                WorkingDirectory = Path.GetDirectoryName(q2proPath)
            };
            Process.Start(psi);
            statusLabel.Text = "Launching AQtion...";
            playButton.Enabled = false;
        }

        /// Downloads a file from a URL to disk, updating the progress bar.
        private async Task DownloadFileWithProgressAsync(string url, string destinationFilePath)
        {
            using HttpClient client = new();
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            var canReportProgress = totalBytes != -1;

            using var stream = await response.Content.ReadAsStreamAsync();
            using var fs = new FileStream(destinationFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
            var buffer = new byte[8192];

            long totalRead = 0;
            var startTime = DateTime.UtcNow;

            while (true)
            {
                int readCount = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length));
                if (readCount == 0) break; // done
                await fs.WriteAsync(buffer.AsMemory(0, readCount));
                totalRead += readCount;

                if (canReportProgress && totalBytes > 0)
                {
                    // Calculate speed
                    var elapsed = DateTime.UtcNow - startTime;
                    double speedKbps = totalRead / 1024d / elapsed.TotalSeconds;
                    // Estimate ETA
                    double remainingSeconds = (totalBytes - totalRead) / 1024d / speedKbps;
                    string etaStr = remainingSeconds > 0 ? $"{remainingSeconds:F1}s" : "Calculating...";
                    // Update UI
                    int progress = (int)(totalRead * 100 / totalBytes);
                    downloadProgressBar.Value = progress;
                    statusLabel.Text = $"Downloading {progress}% @ {speedKbps:F1} KB/s, ETA {etaStr}";
                }
            }
            downloadProgressBar.Value = 100;
        }

        /// Combines a base URL and a relative path into a full URL.
        private string CombineUrl(string baseUrl, string relative)
        {
            if (!baseUrl.EndsWith("/")) baseUrl += "/";
            return new Uri(new Uri(baseUrl), relative).ToString();
        }

        /// Checks for Q2PRO in the default location, enables/disables buttons accordingly.
        private void EnsureQ2Pro()
        {
            q2proDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "q2pro");
            string? foundExe = Directory.Exists(q2proDir)
                ? Directory.GetFiles(q2proDir, "q2pro.exe", SearchOption.AllDirectories).FirstOrDefault()
                : null;
            if (!string.IsNullOrEmpty(foundExe))
            {
                q2proPath = foundExe;
                q2proDir = Path.GetDirectoryName(foundExe) ?? q2proDir;
                demoDownloadRoot = Path.Combine(q2proDir, "action", "demos");
                if (!Directory.Exists(demoDownloadRoot))
                    Directory.CreateDirectory(demoDownloadRoot);
                statusLabel.Text = $"Q2PRO found at {q2proPath}";
            }
            else
            {
                statusLabel.Text = "Q2PRO not found. Please download or choose manually.";
            }
            UpdateQ2ProButtons();
        }

        /// Downloads and extracts Q2PRO, sets up paths, and updates UI.
        private async Task DownloadQ2ProAndSetAsync()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            q2proDir = Path.Combine(baseDir, "q2pro");
            string tempZipPath = Path.Combine(baseDir, "aqtion_dl.zip");
            try
            {
                statusLabel.Text = "Downloading AQtion, please wait...";
                using (HttpClient client = new())
                using (var response = await client.GetAsync(q2proZipUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    var total = response.Content.Headers.ContentLength ?? -1L;
                    var canReportProgress = total != -1;
                    using (var stream = await response.Content.ReadAsStreamAsync())
                    using (var fs = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write))
                    {
                        byte[] buffer = new byte[8192];
                        long totalRead = 0;
                        int read;
                        downloadProgressBar.Value = 0;
                        while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0)
                        {
                            await fs.WriteAsync(buffer.AsMemory(0, read));
                            totalRead += read;
                            if (canReportProgress)
                            {
                                int progress = (int)(totalRead * 100 / total);
                                downloadProgressBar.Value = progress;
                                statusLabel.Text = $"Downloading AQtion... {progress}%";
                                Application.DoEvents();
                            }
                        }
                        downloadProgressBar.Value = 100;
                    }
                }
                if (Directory.Exists(q2proDir))
                    Directory.Delete(q2proDir, true);
                Directory.CreateDirectory(q2proDir);
                statusLabel.Text = "Extracting AQtion...";
                ZipFile.ExtractToDirectory(tempZipPath, q2proDir);
                File.Delete(tempZipPath);
                // record when installed
                string outerQ2proFolder = Path.Combine(baseDir, "q2pro"); // always the same
                string markerFile = Path.Combine(outerQ2proFolder, ".downloaded_by_aqtiondemolauncher");
                File.WriteAllText(markerFile, DateTime.UtcNow.ToString("O"));
                // Search for q2pro.exe after extracting
                string? foundExe = Directory.GetFiles(q2proDir, "q2pro.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (foundExe == null)
                {
                    throw new Exception("q2pro.exe not found after extracting release.");
                }

                q2proDir = Path.GetDirectoryName(foundExe) ?? q2proDir;
                q2proPath = foundExe;
                demoDownloadRoot = Path.Combine(q2proDir, "action", "demos");
                if (!Directory.Exists(demoDownloadRoot))
                    Directory.CreateDirectory(demoDownloadRoot);
                statusLabel.Text = $"AQtion ready at {q2proPath}";
                UpdateQ2ProButtons();
                downloadProgressBar.Value = 0;
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Failed to download or extract AQtion";
                MessageBox.Show($"Failed to download/extract AQtion.\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                chooseButton.Enabled = true;
            }
        }

        /// Removes the downloaded Q2PRO folder if it was installed by this launcher.
        private void RemoveQ2proButton_Click(object? sender, EventArgs e)
        {
            string exeFolder = q2proDir;
            string appBaseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            // Walk up to find the outermost "q2pro" folder under the app base directory
            string? folderToDelete = null;
            DirectoryInfo? dirInfo = new DirectoryInfo(exeFolder);
            while (dirInfo != null && dirInfo.FullName.TrimEnd(Path.DirectorySeparatorChar) != appBaseDir)
            {
                if (string.Equals(dirInfo.Name, "q2pro", StringComparison.OrdinalIgnoreCase))
                {
                    folderToDelete = dirInfo.FullName;
                    break;
                }
                dirInfo = dirInfo.Parent;
            }
            if (folderToDelete != null && Directory.Exists(folderToDelete))
            {
                string markerFile = Path.Combine(folderToDelete, ".downloaded_by_aqtiondemolauncher");
                if (File.Exists(markerFile))
                {
                    var result = MessageBox.Show(
                        "Delete the downloaded AQtion release? This will remove the entire 'q2pro' folder and all its contents.",
                        "Confirm Remove",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);
                    if (result == DialogResult.Yes)
                    {
                        try
                        {
                            Directory.Delete(folderToDelete, true);
                            statusLabel.Text = "AQtion deleted.";
                            UpdateQ2ProButtons();
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show("Could not delete folder:\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                }
                else
                {
                    MessageBox.Show(
                        "The AQtion folder does not appear to have been downloaded by this launcher, so it will not be removed for safety.",
                        "Remove Not Allowed",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            }
            else
            {
                MessageBox.Show(
                    "Q2PRO folder was not found.",
                    "Nothing to Delete",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }

        private async void SourceComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            ComboBox cb = (ComboBox)sender!;
            if (cb.SelectedItem == null) return;
            string key = cb.SelectedItem.ToString()!;
            if (demoSources.TryGetValue(key, out var newUrl))
            {
                currentDemoSourceUrl = newUrl;
                rootDemoUrl = newUrl;
                currentFolderUrl = currentDemoSourceUrl;
                folderHistory.Clear();
                await LoadDemoListAsync(currentFolderUrl);
            }
        }

        /// Attempts to extract the map name from a demo file (supports .gz).
        private string? TryExtractMapName(string demoFilePath)
        {
            string uncompressedPath = EnsureDemoIsUncompressed(demoFilePath);
            try
            {
                byte[] buffer = File.ReadAllBytes(uncompressedPath);
                var ascii = System.Text.Encoding.ASCII.GetString(buffer);
                // Find maps/mapname pattern
                var match = System.Text.RegularExpressions.Regex.Match(ascii, @"maps/([a-zA-Z0-9_]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success)
                    return match.Groups[1].Value.ToLowerInvariant(); // always lowercase for URL use
                // fallback to .bsp finder if needed
                match = System.Text.RegularExpressions.Regex.Match(ascii, @"([a-zA-Z0-9_]{3,20})\.bsp", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success)
                    return match.Groups[1].Value.ToLowerInvariant();
            }
            catch
            {
                // Ignore errors and return null
            }
            return null;
        }

        private void UpdateQ2ProButtons()
        {
            bool q2proExists = !string.IsNullOrEmpty(q2proPath) && File.Exists(q2proPath);

            // Always check for marker in the outermost q2pro folder
            string appBaseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            string outerQ2proFolder = Path.Combine(appBaseDir, "q2pro");
            string markerFile = Path.Combine(outerQ2proFolder, ".downloaded_by_aqtiondemolauncher");
            bool markerExists = File.Exists(markerFile);

            chooseButton.Enabled = true;
            downloadQ2proButton.Enabled = !q2proExists;
            removeQ2proButton.Enabled = q2proExists && markerExists;
        }

        private void CopyDemoUrlForSelectedItem()
        {
            if (demoListBox.SelectedItem == null) return;
            string demo = demoListBox.SelectedItem.ToString()!;
            if (demoMap.TryGetValue(demo, out var real))
            {
                string url;
                if (currentDemoSourceUrl.Contains("s3.amazonaws.com"))
                {
                    var s3RootUri = new Uri(s3BucketRoot);
                    var s3RootPath = s3RootUri.AbsolutePath.Trim('/');
                    string key = real;
                    if (!string.IsNullOrEmpty(s3RootPath) && key.StartsWith(s3RootPath + "/"))
                        key = key.Substring(s3RootPath.Length + 1);
                    url = s3BucketRoot.TrimEnd('/') + "/" + key.TrimStart('/');
                }
                else
                {
                    url = CombineUrl(currentFolderUrl, real);
                }
                Clipboard.SetText(url);
                statusLabel.Text = "Demo URL copied!";
            }
        }

        private void ToggleSortOrder()
        {
            sortDescending = !sortDescending;
            if (sortButton != null)
                sortButton.Text = sortDescending ? "Sort A–Z" : "Sort Z–A";

            // Helper to strip check mark for sorting
            string StripPrefix(string s) => s.StartsWith("✓ ") ? s.Substring(2) : s;

            // Sort folders and files separately, folders always on top
            var folders = sortDescending
                ? allDemoDisplayNames.Where(x => x.StartsWith("[DIR] "))
                    .OrderByDescending(x => StripPrefix(x), StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : allDemoDisplayNames.Where(x => x.StartsWith("[DIR] "))
                    .OrderBy(x => StripPrefix(x), StringComparer.OrdinalIgnoreCase)
                    .ToList();

            var files = sortDescending
                ? allDemoDisplayNames.Where(x => !x.StartsWith("[DIR] "))
                    .OrderByDescending(x => StripPrefix(x), StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : allDemoDisplayNames.Where(x => !x.StartsWith("[DIR] "))
                    .OrderBy(x => StripPrefix(x), StringComparer.OrdinalIgnoreCase)
                    .ToList();

            demoListBox.Items.Clear();
            foreach (var f in folders.Concat(files))
                demoListBox.Items.Add(f);
        }

        private bool IsQ2ProRunning()
        {
            try
            {
                // Only match q2pro.exe in the same directory as q2proPath
                string exeDir = Path.GetDirectoryName(q2proPath) ?? "";
                return Process.GetProcessesByName("q2pro")
                    .Any(p =>
                    {
                        try
                        {
                            return string.Equals(
                                Path.GetDirectoryName(p.MainModule?.FileName ?? ""),
                                exeDir,
                                StringComparison.OrdinalIgnoreCase);
                        }
                        catch
                        {
                            return false; // Access denied for some processes
                        }
                    });
            }
            catch
            {
                return false;
            }
        }

        private async Task CheckForUpdatesAsync()
        {
            if (string.IsNullOrWhiteSpace(updateApiUrl))
                return;
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("AQtionDemoLauncher");
                string json = await client.GetStringAsync(updateApiUrl);
                var release = System.Text.Json.JsonDocument.Parse(json).RootElement;
                string? latestVersionStr = release.GetProperty("tag_name").GetString();
                if (!string.IsNullOrEmpty(latestVersionStr))
                {
                    var latestSemVer = NuGetVersion.Parse(latestVersionStr.TrimStart('v'));
                    var currentSemVer = NuGetVersion.Parse(currentVersionString);
                    if (latestSemVer > currentSemVer)
                    {
                        string url = release.GetProperty("html_url").GetString() ?? "";
                        var result = MessageBox.Show(
                            $"A new version ({latestSemVer}) is available! Download now?",
                            "Update Available",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Information);
                        if (result == DialogResult.Yes && !string.IsNullOrEmpty(url))
                        {
                            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                        }
                    }
                }
            }
            catch
            {
                // Optional: log or ignore if no connection or error
            }
        }
    }
}
