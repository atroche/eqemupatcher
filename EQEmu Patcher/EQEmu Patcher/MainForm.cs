using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using Microsoft.WindowsAPICodePack.Taskbar;
using System.Diagnostics;
using System.Threading;

namespace EQEmu_Patcher
{

    public partial class MainForm : Form
    {

        public static string serverName; // server title name
        public static string version; //version of file
        string fileName; //base name of executable
        bool isPatching = false;
        bool isPatchCancelled = false;
        bool isPendingPatch = false; // This is used to indicate that someone pressed "Patch" before we did some background update checks
        bool isLoading;
        bool isAutoPatch = false;
        bool isAutoPlay = false;
        CancellationTokenSource cts;
        System.Diagnostics.Process process;

        //Note that for supported versions, the 3 letter suffix is needed on the filelist_###.yml file.
        public static List<VersionTypes> supportedClients = new List<VersionTypes> { //Supported clients for patcher
            //VersionTypes.Unknown, //unk
            //VersionTypes.Titanium, //tit
            //VersionTypes.Underfoot, //und
            //VersionTypes.Secrets_Of_Feydwer, //sof
            //VersionTypes.Seeds_Of_Destruction, //sod
            VersionTypes.Rain_Of_Fear, //rof
            VersionTypes.Rain_Of_Fear_2 //rof
            //VersionTypes.Broken_Mirror, //bro
        };

        private Dictionary<VersionTypes, ClientVersion> clientVersions = new Dictionary<VersionTypes, ClientVersion>();

        VersionTypes currentVersion;

       // TaskbarItemInfo tii = new TaskbarItemInfo();
        public MainForm()
        {
            InitializeComponent();
        }

        private async void MainForm_Load(object sender, EventArgs e)
        {
            isLoading = true;
            version = Assembly.GetEntryAssembly().GetName().Version.ToString();
            Console.WriteLine($"Initializing {version}");
            Console.WriteLine($"Current Directory: {Directory.GetCurrentDirectory()}");
            cts = new CancellationTokenSource();

            serverName = Assembly.GetExecutingAssembly().GetCustomAttribute<ServerName>().Value;
#if (DEBUG)
            serverName = "EQEMU Patcher";
#endif
            if (serverName == "") {
                MessageBox.Show("This patcher was built incorrectly. Please contact the distributor of this and inform them the server name is not provided or screenshot this message.");
                this.Close();
                return;
            }

            fileName = Assembly.GetExecutingAssembly().GetCustomAttribute<FileName>().Value;
#if (DEBUG)
            fileName = "eqemupatcher";
#endif
            if (fileName == "")
            {
                MessageBox.Show("This patcher was built incorrectly. Please contact the distributor of this and inform them the file name is not provided or screenshot this message.");
                this.Close();
                return;
            }

            // filelistUrl and patcherUrl no longer needed - we download directly from Spire

            txtList.Visible = false;
            splashLogo.Visible = true;
            if (this.Width < 432) {
                this.Width = 432;
            }
            if (this.Height < 550)
            {
                this.Height = 550;
            }
            buildClientVersions();
            IniLibrary.Load();
            detectClientVersion();
            isAutoPlay = (IniLibrary.instance.AutoPlay.ToLower() == "true");
            isAutoPatch = (IniLibrary.instance.AutoPatch.ToLower() == "true");
            chkAutoPlay.Checked = isAutoPlay;
            chkAutoPatch.Checked = isAutoPatch;
            try
            {
                if (File.Exists(Application.ExecutablePath + ".old"))
                {
                    File.Delete(Application.ExecutablePath + ".old");
                }

            } catch (Exception exDelete)
            {
                Console.WriteLine($"Failed to delete .old file: {exDelete.Message}");
            }

            if (IniLibrary.instance.ClientVersion == VersionTypes.Unknown)
            {
                detectClientVersion();
                if (currentVersion == VersionTypes.Unknown)
                {
                    this.Close();
                }
                IniLibrary.instance.ClientVersion = currentVersion;
                IniLibrary.Save();
            }
            string suffix = "unk";
            if (currentVersion == VersionTypes.Titanium) suffix = "tit";
            if (currentVersion == VersionTypes.Underfoot) suffix = "und";
            if (currentVersion == VersionTypes.Seeds_Of_Destruction) suffix = "sod";
            if (currentVersion == VersionTypes.Broken_Mirror) suffix = "bro";
            if (currentVersion == VersionTypes.Secrets_Of_Feydwer) suffix = "sof";
            if (currentVersion == VersionTypes.Rain_Of_Fear || currentVersion == VersionTypes.Rain_Of_Fear_2) suffix = "rof";

            bool isSupported = false;
            foreach (var ver in supportedClients)
            {
                if (ver != currentVersion) continue;
                isSupported = true;
                break;
            }
            if (!isSupported) {
                MessageBox.Show("The server " + serverName + " does not work with this copy of Everquest (" + currentVersion.ToString().Replace("_", " ") + ")", serverName);
                this.Close();
                return;
            }

            this.Text = serverName + " (Client: " + currentVersion.ToString().Replace("_", " ") + ")";
            progressBar.Minimum = 0;
            progressBar.Maximum = 10000;
            progressBar.Value = 0;
            StatusLibrary.SubscribeProgress(new StatusLibrary.ProgressHandler((int value) => {
                Invoke((MethodInvoker)delegate {
                    progressBar.Value = value;
                    if (Environment.OSVersion.Version.Major < 6) {
                        return;
                    }
                    var taskbar = TaskbarManager.Instance;
                    taskbar.SetProgressValue(value, 10000);
                    taskbar.SetProgressState((value == 10000) ? TaskbarProgressBarState.NoProgress : TaskbarProgressBarState.Normal);
                });
            }));

            StatusLibrary.SubscribeLogAdd(new StatusLibrary.LogAddHandler((string message) => {
                Invoke((MethodInvoker)delegate {
                    if (!txtList.Visible)
                    {
                        txtList.Visible = true;
                        splashLogo.Visible = false;
                    }
                    txtList.AppendText(message + "\r\n");
                });
            }));

            StatusLibrary.SubscribePatchState(new StatusLibrary.PatchStateHandler((bool isPatchGoing) => {
                Invoke((MethodInvoker)delegate {

                    btnCheck.BackColor = SystemColors.Control;
                    if (isPatchGoing)
                    {
                        btnCheck.Text = "Cancel";
                        return;
                    }

                    btnCheck.Text = "Patch";
                });
            }));

            // No filelist needed - we download directly from Spire each time
            isLoading = false;
            
            var path = System.IO.Path.GetDirectoryName(Application.ExecutablePath) + "\\eqemupatcher.png";
            if (File.Exists(path))
            {
                splashLogo.Load(path);
            }
            cts.Cancel();
        }

        private void detectClientVersion()
        {
            /*
            try
            {
                var hash = UtilityLibrary.GetEverquestExecutableHash(AppDomain.CurrentDomain.BaseDirectory);
                if (hash == "")
                {
                    MessageBox.Show("Please run this patcher in your Everquest directory.");
                    this.Close();
                    return;
                }
                switch (hash)
                {
                    case "240C80800112ADA825C146D7349CE85B":
                    case "A057A23F030BAA1C4910323B131407105ACAD14D": //This is a custom ROF2 from a torrent download
                    case "178C9C8FDDDF8F78B6B9142D025FE059": // Custom THJ
                    case "36968E793EBFDB3A1A1C55C7FF1D7C1A": // Retribution
                    case "6574AC667D4C522D21A47F4D00920CC2": // LAA
                    case "389709EC0E456C3DAE881A61218AAB3F": // This is a 4gb patched eqgame
                    case "AE4E4C995DF8842DAE3127E88E724033": // gangsta of RoT 4gb patched eqgame
                    case "3B44C6CD42313CB80C323647BCB296EF": // https://github.com/xackery/eqemupatcher/issues/15
                    case "513FDC2B5CC63898D7962F0985D5C207": // aslr checksum removed
                    case "26DC13388395A20B73E1B5A08415B0F8": // Legacy of Norrath Custom RoF2 Client https://github.com/xackery/eqemupatcher/issues/16
                        currentVersion = VersionTypes.Rain_Of_Fear_2;
                        splashLogo.Image = Properties.Resources.rof;
                        break;
                    default:
                        currentVersion = VersionTypes.Unknown;
                        break;
                }
                if (currentVersion == VersionTypes.Unknown)
                {
                    if (MessageBox.Show("Unable to recognize the Everquest client in this directory, open a web page to report to devs?", "Visit", MessageBoxButtons.YesNo, MessageBoxIcon.Asterisk) == DialogResult.Yes)
                    {
                        System.Diagnostics.Process.Start("https://github.com/Xackery/eqemupatcher/issues/new?title=A+New+EQClient+Found&body=Hi+I+Found+A+New+Client!+Hash:+" + hash);
                    }
                    StatusLibrary.Log($"Unable to recognize the Everquest client in this directory, send to developers: {hash}");
                }
                else
                {
                    //StatusLibrary.Log($"You seem to have put me in a {clientVersions[currentVersion].FullName} client directory");
                }

                //MessageBox.Show(""+currentVersion);
                //StatusLibrary.Log($"If you wish to help out, press the scan button on the bottom left and wait for it to complete, then copy paste this data as an Issue on github!");
            }
            catch (UnauthorizedAccessException err)
            {
                MessageBox.Show("You need to run this program with Administrative Privileges" + err.Message);
                return;
            }
            */
            currentVersion = VersionTypes.Rain_Of_Fear_2;
            splashLogo.Image = Properties.Resources.rof;
        }

        //Build out all client version's dictionary
        private void buildClientVersions()
        {
            clientVersions.Clear();
            clientVersions.Add(VersionTypes.Titanium, new ClientVersion("Titanium", "titanium"));
            clientVersions.Add(VersionTypes.Secrets_Of_Feydwer, new ClientVersion("Secrets Of Feydwer", "sof"));
            clientVersions.Add(VersionTypes.Seeds_Of_Destruction, new ClientVersion("Seeds of Destruction", "sod"));
            clientVersions.Add(VersionTypes.Rain_Of_Fear, new ClientVersion("Rain of Fear", "rof"));
            clientVersions.Add(VersionTypes.Rain_Of_Fear_2, new ClientVersion("Rain of Fear 2", "rof2"));
            clientVersions.Add(VersionTypes.Underfoot, new ClientVersion("Underfoot", "underfoot"));
            clientVersions.Add(VersionTypes.Broken_Mirror, new ClientVersion("Broken Mirror", "brokenmirror"));
        }


        private void btnStart_Click(object sender, EventArgs e)
        {
            PlayGame();
        }

        private void PlayGame()
        {
            try
            {
                StatusLibrary.Log("Checking THJ UI...");
               
                string eqPath = Path.GetDirectoryName(Application.ExecutablePath);
                var di = new DirectoryInfo(eqPath);
                var files = di.GetFiles("UI*_thj.ini");

                foreach (var file in files)
                {
                    if (file.Length > 10240)
                    {
                        continue;
                    }
                    StatusLibrary.Log("Found corrupted UI file: " + file.Name);
                    string bakFile = file.FullName + ".bak";
                    if (File.Exists(bakFile))
                    {
                        if (MessageBox.Show($"UI file {file.Name} appears to be corrupted. Would you like to restore the backup?", "Restore Backup", MessageBoxButtons.YesNo) == DialogResult.Yes)
                        {
                            File.Copy(bakFile, file.FullName, true);
                        }
                        continue;
                    }
                    if (MessageBox.Show($"UI file {file.Name} appears to be corrupted. Would you like to restore the default UI?", "Restore Default", MessageBoxButtons.YesNo) == DialogResult.Yes)
                    {
                        File.WriteAllText(file.FullName, DefaultUI.String());
                    }
                }


                // now just back up all the UI files
                foreach (var file in files)
                {
                    if (file.Length < 10240)
                    {
                        continue;
                    }
                    string bakFile = file.FullName + ".bak";                   
                    // force overwrite ther existing file
                    if (File.Exists(bakFile))
                    {
                        File.Delete(bakFile);
                    }
                    File.Copy(file.FullName, bakFile);
                }
                StatusLibrary.Log("THJ UI check complete.");
            }
            catch (Exception err) 
            {
                MessageBox.Show("An error occured while trying to check UI files: " + err.Message);
            }

            try
            {
                process = UtilityLibrary.StartEverquest();
                if (process != null) this.Close();
                else MessageBox.Show("The process failed to start");
            }
            catch (Exception err)
            {
                MessageBox.Show("An error occured while trying to start everquest: " + err.Message);
            }
        }


        private void btnCheck_Click(object sender, EventArgs e)
        {
            if (isLoading && !isPendingPatch)
            {
                isPendingPatch = true;
                pendingPatchTimer.Enabled = true;
                StatusLibrary.Log("Checking for updates...");
                btnCheck.Text = "Cancel";
                return;
            }

            if (isPatching)
            {
                isPatchCancelled = true;
                cts.Cancel();
            }
            Console.WriteLine("patch button called");
            StartPatch();
        }

        public static async Task<string> DownloadFile(CancellationTokenSource cts, string url, string path)
        {
            path = path.Replace("/", "\\");
            if (path.Contains("\\")) { //Make directory if needed.
                string dir = System.IO.Path.GetDirectoryName(Application.ExecutablePath) + "\\" + path.Substring(0, path.LastIndexOf("\\"));
                Directory.CreateDirectory(dir);
            }
            return await UtilityLibrary.DownloadFile(cts, url, path);
        }

        public static async Task<byte[]> Download(CancellationTokenSource cts, string url)
        {
            return await UtilityLibrary.Download(cts, url);
        }

        private void StartPatch()
        {
            if (isPatching)
            {
                Console.WriteLine("premature patch call");
                return;
            }
            cts = new CancellationTokenSource();
            isPatchCancelled = false;
            txtList.Text = "";
            StatusLibrary.SetPatchState(true);
            isPatching = true;
            Task.Run(async () =>
            {
                try
                {
                    await AsyncPatch();
                } catch (Exception e)
                {
                    StatusLibrary.Log($"Exception during patch: {e.Message}");
                }
                StatusLibrary.SetPatchState(false);
                isPatching = false;
                isPatchCancelled = false;
                cts.Cancel();
            });
        }

        // Spire API endpoints for dynamic client files
        // Key = local file path (relative to EQ folder), Value = Spire API endpoint
        private static readonly Dictionary<string, string> SpireExports = new Dictionary<string, string>
        {
            { "spells_us.txt", "eqemuserver/export-client-file/spells" },
            { "Resources\\SkillCaps.txt", "eqemuserver/export-client-file/skills" },
            { "Resources\\BaseData.txt", "eqemuserver/export-client-file/basedata" },
            { "dbstr_us.txt", "eqemuserver/export-client-file/dbstring" }
        };

        // Base URL for Spire API - change this to your server
        private static readonly string SpireBaseUrl = "http://mud.sunburnt.country:3000/api/v1/";

        private async Task AsyncPatch()
        {
            Stopwatch start = Stopwatch.StartNew();
            StatusLibrary.Log($"Patching with patcher version {version}...");
            StatusLibrary.SetProgress(0);

            // Download the 4 Spire export files (dynamic from database)
            StatusLibrary.Log("Downloading server data files...");
            int spireFileCount = 0;
            double totalBytes = 0;

            foreach (var spireFile in SpireExports)
            {
                if (isPatchCancelled)
                {
                    StatusLibrary.Log("Patching cancelled.");
                    return;
                }

                string spireFilePath = spireFile.Key;
                string endpoint = spireFile.Value;
                string url = SpireBaseUrl + endpoint;
                string localPath = Path.GetDirectoryName(Application.ExecutablePath) + "\\" + spireFilePath;
                string displayName = Path.GetFileName(spireFilePath);

                try
                {
                    // Create directory if needed (for Resources subfolder)
                    string dir = Path.GetDirectoryName(localPath);
                    if (!Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    StatusLibrary.Log($"Downloading {displayName}...");
                    var data = await Download(cts, url);
                    if (data != null && data.Length > 0)
                    {
                        File.WriteAllBytes(localPath, data);
                        StatusLibrary.Log($"  {displayName} ({generateSize(data.Length)})");
                        spireFileCount++;
                        totalBytes += data.Length;
                    }
                    else
                    {
                        StatusLibrary.Log($"  Warning: {displayName} was empty or failed");
                    }
                }
                catch (Exception ex)
                {
                    StatusLibrary.Log($"  Failed to download {displayName}: {ex.Message}");
                }

                StatusLibrary.SetProgress((int)(((spireFileCount) / (double)SpireExports.Count) * 10000));
            }

            StatusLibrary.SetProgress(10000);
            string elapsed = start.Elapsed.ToString("ss\\.ff");
            
            if (spireFileCount == SpireExports.Count)
            {
                StatusLibrary.Log($"Complete! Downloaded {spireFileCount} files ({generateSize(totalBytes)}) in {elapsed} seconds. Press Play to begin.");
            }
            else
            {
                StatusLibrary.Log($"Finished with warnings. Downloaded {spireFileCount}/{SpireExports.Count} files in {elapsed} seconds.");
            }
            return;
        }

        private void chkAutoPlay_CheckedChanged(object sender, EventArgs e)
        {
            if (isLoading) return;
            isAutoPlay = chkAutoPlay.Checked;
            IniLibrary.instance.AutoPlay = (isAutoPlay) ? "true" : "false";
            if (isAutoPlay) StatusLibrary.Log("To disable autoplay: edit eqemupatcher.yml or wait until next patch.");

            IniLibrary.Save();
        }

        private void chkAutoPatch_CheckedChanged(object sender, EventArgs e)
        {
            if (isLoading) return;
            isAutoPatch = chkAutoPatch.Checked;
            IniLibrary.instance.AutoPatch = (isAutoPatch) ? "true" : "false";
            IniLibrary.Save();
        }

        private void MainForm_Shown(object sender, EventArgs e)
        {
        }

        private string generateSize(double size) {
            if (size < 1024) {
                return $"{Math.Round(size, 2)} bytes";
            }

            size /= 1024;
            if (size < 1024)
            {
                return $"{Math.Round(size, 2)} KB";
            }

            size /= 1024;
            if (size < 1024)
            {
                return $"{Math.Round(size, 2)} MB";
            }

            size /= 1024;
            if (size < 1024)
            {
                return $"{Math.Round(size, 2)} GB";
            }

            return $"{Math.Round(size, 2)} TB";
        }

        private void pendingPatchTimer_Tick(object sender, EventArgs e)
        {
            if (isLoading) return;
            pendingPatchTimer.Enabled = false;
            isPendingPatch = false;
            btnCheck_Click(sender, e);
        }
    }

    public class FileList
    {
        public string version { get; set; }

        public List<FileEntry> deletes { get; set; }
        public string downloadprefix { get; set; }
        public List<FileEntry> downloads { get; set; }
        public List<FileEntry> unpacks { get; set; }

    }

    public class FileEntry
    {
        public string name { get; set;  }
        public string md5 { get; set; }
        public string date { get; set; }
        public string zip { get; set; }
        public int size { get; set; }
    }
}


