﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Windows.Forms;
using Google.Apis.Calendar.v3.Data;
using log4net;
using Microsoft.Office.Interop.Outlook;

namespace OutlookGoogleCalendarSync {
    /// <summary>
    /// Description of MainForm.
    /// </summary>
    public partial class MainForm : Form {
        public static MainForm Instance;

        public Timer OgcsPushTimer;
        private Timer ogcsTimer;
        private List<int> MinuteOffsets = new List<int>();
        private DateTime lastSyncDate;

        private BackgroundWorker bwSync;
        public Boolean SyncingNow {
            get {
                if (bwSync == null) return false;
                else return bwSync.IsBusy; 
            }
        }
        private static readonly ILog log = LogManager.GetLogger(typeof(MainForm));

        public MainForm() {
            log.Debug("Initialiasing MainForm.");
            InitializeComponent();
            lAboutMain.Text = lAboutMain.Text.Replace("{version}", System.Windows.Forms.Application.ProductVersion);

            Instance = this;

            log.Debug("Setting system proxy.");
            WebProxy wp = (WebProxy)System.Net.GlobalProxySelection.Select;
            //http://www.dreamincode.net/forums/topic/160555-working-with-proxy-servers/
            //WebProxy wp = (WebProxy)WebRequest.DefaultWebProxy;
            wp.UseDefaultCredentials = true;
            System.Net.WebRequest.DefaultWebProxy = wp;

            log.Debug("Loading settings/creating settings file.");
            if (File.Exists(Program.SettingsFile)) {
                Settings.Instance = XMLManager.import<Settings>(Program.SettingsFile);
            } else {
                XMLManager.export(Settings.Instance, Program.SettingsFile);
            }

            #region Update GUI from Settings
            this.SuspendLayout();
            #region Outlook box
            this.gbOutlook.SuspendLayout();
            gbEWS.Enabled = false;
            if (Settings.Instance.OutlookService == OutlookCalendar.Service.AlternativeMailbox) {
                rbOutlookAltMB.Checked = true;
            } else if (Settings.Instance.OutlookService == OutlookCalendar.Service.EWS) {
                rbOutlookEWS.Checked = true;
                gbEWS.Enabled = true;
            } else {
                rbOutlookDefaultMB.Checked = true;
                ddMailboxName.Enabled = false;
            }
            txtEWSPass.Text = Settings.Instance.EWSpassword;
            txtEWSUser.Text = Settings.Instance.EWSuser;
            txtEWSServerURL.Text = Settings.Instance.EWSserver;

            //Mailboxes the user has access to
            log.Debug("Find Accounts");
            if (OutlookCalendar.Instance.Accounts.Count == 1) {
                rbOutlookAltMB.Enabled = false;
                rbOutlookAltMB.Checked = false;
                ddMailboxName.Enabled = false;
            }
            for (int acc = 1; acc <= OutlookCalendar.Instance.Accounts.Count-1; acc++) {
                String mailbox = OutlookCalendar.Instance.Accounts[acc];
                ddMailboxName.Items.Add(mailbox);
                if (Settings.Instance.MailboxName == mailbox) { ddMailboxName.SelectedIndex = acc - 1; }
            }
            if (ddMailboxName.SelectedIndex == -1 && ddMailboxName.Items.Count > 0) { ddMailboxName.SelectedIndex = 0; }

            log.Debug("List Calendar folders");
            cbOutlookCalendars.SelectedIndexChanged -= cbOutlookCalendar_SelectedIndexChanged;
            cbOutlookCalendars.DataSource = new BindingSource(OutlookCalendar.Instance.CalendarFolders, null);
            cbOutlookCalendars.DisplayMember = "Key";
            cbOutlookCalendars.ValueMember = "Value";
            cbOutlookCalendars.SelectedIndex = -1; //Reset to nothing selected
            cbOutlookCalendars.SelectedIndexChanged += cbOutlookCalendar_SelectedIndexChanged;
            //Select the right calendar
            int c = 0;
            foreach (KeyValuePair<String, MAPIFolder> calendarFolder in OutlookCalendar.Instance.CalendarFolders) {
                if (calendarFolder.Value.EntryID == Settings.Instance.UseOutlookCalendar.Id) {
                    cbOutlookCalendars.SelectedIndex = c;
                }
                c++;
            }
            if (cbOutlookCalendars.SelectedIndex == -1) cbOutlookCalendars.SelectedIndex = 0;
            this.gbOutlook.ResumeLayout();
            #endregion
            #region Google box
            this.gbGoogle.SuspendLayout();
            if (Settings.Instance.UseGoogleCalendar != null && Settings.Instance.UseGoogleCalendar.Id != null) {
                cbGoogleCalendars.Items.Add(Settings.Instance.UseGoogleCalendar);
                cbGoogleCalendars.SelectedIndex = 0;
            }
            this.gbGoogle.ResumeLayout();
            #endregion
            #region Sync Options box
            syncDirection.Items.Add(SyncDirection.OutlookToGoogle);
            syncDirection.Items.Add(SyncDirection.GoogleToOutlook);
            //syncDirection.Items.Add(SyncDirection.Bidirectional);
            for (int i = 0; i < syncDirection.Items.Count; i++) {
                SyncDirection sd = (syncDirection.Items[i] as SyncDirection);
                if (sd.Id == Settings.Instance.SyncDirection.Id) {
                    syncDirection.SelectedIndex = i;
                }
            }
            if (syncDirection.SelectedIndex == -1) syncDirection.SelectedIndex = 0;
            this.gbSyncOptions.SuspendLayout();
            tbDaysInThePast.Text = Settings.Instance.DaysInThePast.ToString();
            tbDaysInTheFuture.Text = Settings.Instance.DaysInTheFuture.ToString();
            tbInterval.ValueChanged -= new System.EventHandler(this.tbMinuteOffsets_ValueChanged);
            tbInterval.Value = Settings.Instance.SyncInterval;
            tbInterval.ValueChanged += new System.EventHandler(this.tbMinuteOffsets_ValueChanged);
            cbIntervalUnit.SelectedIndexChanged -= new System.EventHandler(this.cbIntervalUnit_SelectedIndexChanged);
            cbIntervalUnit.Text = Settings.Instance.SyncIntervalUnit;
            cbIntervalUnit.SelectedIndexChanged += new System.EventHandler(this.cbIntervalUnit_SelectedIndexChanged); 
            cbOutlookPush.Checked = Settings.Instance.OutlookPush;
            cbAddDescription.Checked = Settings.Instance.AddDescription;
            cbAddAttendees.Checked = Settings.Instance.AddAttendees;
            cbAddReminders.Checked = Settings.Instance.AddReminders;
            cbMergeItems.Checked = Settings.Instance.MergeItems;
            cbDisableDeletion.Checked = Settings.Instance.DisableDelete;
            cbConfirmOnDelete.Enabled = !Settings.Instance.DisableDelete;
            cbConfirmOnDelete.Checked = Settings.Instance.ConfirmOnDelete;
            this.gbSyncOptions.ResumeLayout();
            #endregion
            #region Application behaviour
            this.gbAppBehaviour.SuspendLayout();
            cbShowBubbleTooltips.Checked = Settings.Instance.ShowBubbleTooltipWhenSyncing;
            cbStartOnStartup.Checked = Settings.Instance.StartOnStartup;
            cbStartInTray.Checked = Settings.Instance.StartInTray;
            cbMinimizeToTray.Checked = Settings.Instance.MinimizeToTray;
            cbCreateFiles.Checked = Settings.Instance.CreateCSVFiles;
            for (int i = 0; i < cbLoggingLevel.Items.Count; i++) {
                if (cbLoggingLevel.Items[i].ToString().ToLower() == Settings.Instance.LoggingLevel.ToLower()) {
                    cbLoggingLevel.SelectedIndex = i;
                    break;
                }
            }
            this.gbAppBehaviour.ResumeLayout();
            #endregion
            lastSyncDate = Settings.Instance.LastSyncDate;
            cbVerboseOutput.Checked = Settings.Instance.VerboseOutput;
            this.ResumeLayout();
            #endregion
            Settings.Instance.LogSettings();

            log.Debug("Create the timer for the auto synchronisation");
            ogcsTimer = new Timer();
            ogcsTimer.Tag = "AutoSyncTimer";
            ogcsTimer.Tick += new EventHandler(ogcsTimer_Tick);
            
            //set up tooltips for some controls
            ToolTip toolTip1 = new ToolTip();
            toolTip1.AutoPopDelay = 10000;
            toolTip1.InitialDelay = 500;
            toolTip1.ReshowDelay = 200;
            toolTip1.ShowAlways = true;
            toolTip1.SetToolTip(cbOutlookCalendars,
                "The Outlook calendar to synchonize with. List also includes subfolders of default calendar.");
            toolTip1.SetToolTip(cbGoogleCalendars,
                "The Google calendar to synchonize with.");
            toolTip1.SetToolTip(btResetGCal,
                "Reset the Google account being used to synchonize with.");
            toolTip1.SetToolTip(tbInterval,
                "Set to zero to disable");
            toolTip1.SetToolTip(cbCreateFiles,
                "If checked, all entries found in Outlook/Google and identified for creation/deletion will be exported \n" +
                "to CSV files in the application's directory (named \"*.csv\"). \n" +
                "Only for debug/diagnostic purposes.");
            toolTip1.SetToolTip(rbOutlookAltMB,
                "Only choose this if you need to use an Outlook Calendar that is not in the default mailbox");
            toolTip1.SetToolTip(cbMergeItems,
                "If the destination calendar has pre-existing items, don't delete them");
            toolTip1.SetToolTip(cbOutlookPush,
                "Synchronise changes in Outlook to Google within a few minutes.");

            //Refresh synchronizations (last and next)
            lLastSyncVal.Text = lastSyncDate.ToLongDateString() + " - " + lastSyncDate.ToLongTimeString();
            setNextSync(getResyncInterval());

            //Set up listener for Outlook calendar changes
            if (Settings.Instance.OutlookPush) OutlookCalendar.Instance.RegisterForAutoSync();

            //Start in tray?
            if (cbStartInTray.Checked) {
                this.WindowState = FormWindowState.Minimized;
                notifyIcon1.Visible = true;
                this.Hide();
                this.ShowInTaskbar = false;
            }
        }

        #region Autosync functions
        int getResyncInterval() {
            int min = Settings.Instance.SyncInterval;
            if (Settings.Instance.SyncIntervalUnit == "Hours") {
                min *= 60;
            }
            return min;
        }

        private void ogcsTimer_Tick(object sender, EventArgs e) {
            log.Debug("Scheduled sync triggered.");

            if (cbShowBubbleTooltips.Checked) {
                notifyIcon1.ShowBalloonTip(
                    500,
                    "Outlook Google Calendar Sync",
                    "Autosyncing calendar...",
                    ToolTipIcon.Info
                );
            }
            if (!this.SyncingNow) {
                sync_Click(sender, null);
            } else {
                log.Debug("Busy syncing already. Rescheduled for 5 mins time.");
                setNextSync(5, fromNow:true);
            }
        }

        public void OgcsPushTimer_Tick(object sender, EventArgs e) {
            if (Convert.ToInt16(bSyncNow.Tag) != 0) {
                log.Debug("Push sync triggered.");
                if (cbShowBubbleTooltips.Checked) {
                    notifyIcon1.ShowBalloonTip(
                        500,
                        "Outlook Google Calendar Sync",
                        "Autosyncing calendar...",
                        ToolTipIcon.Info
                    );
                }
                if (!this.SyncingNow) {
                    sync_Click(sender, null);
                } else {
                    log.Debug("Busy syncing already. No need to push.");
                    bSyncNow.Tag = 0;
                }
            }
        }
        
        void setNextSync(int delayMins, Boolean fromNow=false) {
            if (tbInterval.Value != 0) {
                DateTime nextSyncDate = lastSyncDate.AddMinutes(delayMins);
                DateTime now = DateTime.Now;
                if (fromNow)
                    nextSyncDate = now.AddMinutes(delayMins);

                if (ogcsTimer.Interval != (delayMins * 60000)) {
                    ogcsTimer.Stop();
                    TimeSpan diff = nextSyncDate - now;
                    if (diff.Minutes < 1) {
                        nextSyncDate = now.AddMinutes(1);
                        ogcsTimer.Interval = 1 * 60000;
                    } else {
                        ogcsTimer.Interval = diff.Minutes * 60000;
                    }
                    ogcsTimer.Start();
                }
                lNextSyncVal.Text = nextSyncDate.ToLongDateString() + " - " + nextSyncDate.ToLongTimeString();
                log.Info("Next sync scheduled for " + lNextSyncVal.Text);
            } else {
                lNextSyncVal.Text = "Inactive";
                ogcsTimer.Stop();
                log.Info("Schedule disabled.");
            }
        }
        #endregion

        private void sync_Click(object sender, EventArgs e) {
            if (bSyncNow.Text == "Start Sync") {
                if (sender.GetType().ToString().EndsWith("Timer")) {
                    Timer aTimer = sender as Timer;
                    if (aTimer.Tag.ToString() == "PushTimer") sync_Start(updateSyncSchedule: false);
                    else if (aTimer.Tag.ToString() == "AutoSyncTimer") sync_Start(updateSyncSchedule: true);
                } else //Manual button press
                    sync_Start(updateSyncSchedule: false);

            } else if (bSyncNow.Text == "Stop Sync") {
                if (bwSync != null && !bwSync.CancellationPending) {
                    log.Warn("Sync cancellation requested.");
                    bwSync.CancelAsync();
                } else {
                    log.Warn("Repeated cancellation requested - forcefully aborting thread!");
                    bwSync = null;
                }
            }
        } 

        private void sync_Start(Boolean updateSyncSchedule=true) {
            LogBox.Clear();

            if (Settings.Instance.UseGoogleCalendar == null || 
                Settings.Instance.UseGoogleCalendar.Id == null ||
                Settings.Instance.UseGoogleCalendar.Id == "") {
                MessageBox.Show("You need to select a Google Calendar first on the 'Settings' tab.");
                return;
            }
            //Check network availability
            if (!System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable()) {
                Logboxout("There does not appear to be any network available! Sync aborted.");
                return;
            }
            //Check if Outlook is Online
            try {
                if (OutlookCalendar.Instance.IOutlook.Offline() && Settings.Instance.AddAttendees) {
                    Logboxout("You have selected to sync attendees but Outlook is currently offline.");
                    Logboxout("Either put Outlook online or do not sync attendees.");
                    return;
                }
            } catch (System.Exception ex) {
                Logboxout(ex.Message);
                log.Error(ex.StackTrace);
                return;
            }
            bSyncNow.Text = "Stop Sync";
            String cacheNextSync = lNextSyncVal.Text;
            lNextSyncVal.Text = "In progress...";

            DateTime SyncStarted = DateTime.Now;
            Logboxout("Sync started at " + SyncStarted.ToString());
            Logboxout("Syncing from "+ DateTime.Today.AddDays(-Settings.Instance.DaysInThePast).ToShortDateString() +
                " to "+ DateTime.Today.AddDays(+Settings.Instance.DaysInTheFuture+1).ToShortDateString());
            Logboxout(Settings.Instance.SyncDirection.Name);
            Logboxout("--------------------------------------------------");

            if (Settings.Instance.OutlookPush) OutlookCalendar.Instance.DeregisterForAutoSync();

            Boolean syncOk = false;
            int failedAttempts = 0;
            while (!syncOk) {
                if (failedAttempts > 0 &&
                    MessageBox.Show("The synchronisation failed. Do you want to try again?", "Sync Failed",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) == System.Windows.Forms.DialogResult.No) 
                {
                    bSyncNow.Text = "Start Sync";
                    break;
                }

                //Set up a separate thread for the sync to operate in. Keeps the UI responsive.
                bwSync = new BackgroundWorker();
                //Don't need thread to report back. The logbox is updated from the thread anyway.
                bwSync.WorkerReportsProgress = false;
                bwSync.WorkerSupportsCancellation = true;

                //Kick off the sync in the background thread
                bwSync.DoWork += new DoWorkEventHandler(
                delegate(object o, DoWorkEventArgs args) {
                    BackgroundWorker b = o as BackgroundWorker;
                    syncOk = synchronize();
                });

                bwSync.RunWorkerAsync();
                while (bwSync != null && (bwSync.IsBusy || bwSync.CancellationPending)) {
                    System.Windows.Forms.Application.DoEvents();
                    System.Threading.Thread.Sleep(100);
                }
                failedAttempts += !syncOk ? 1 : 0;
            }
            bSyncNow.Text = "Start Sync";

            Logboxout("--------------------------------------------------");
            Logboxout(syncOk ? "Sync finished with success!" : "Operation aborted after "+ failedAttempts +" failed attempts!");

            if (Settings.Instance.OutlookPush) OutlookCalendar.Instance.RegisterForAutoSync();

            if (!updateSyncSchedule) {
                lLastSyncVal.Text = SyncStarted.ToLongDateString() + " - " + SyncStarted.ToLongTimeString();
                lNextSyncVal.Text = cacheNextSync;
            } else {
                if (syncOk) {
                    lastSyncDate = SyncStarted;
                    Settings.Instance.LastSyncDate = lastSyncDate;
                    lLastSyncVal.Text = SyncStarted.ToLongDateString() + " - " + SyncStarted.ToLongTimeString();
                    setNextSync(getResyncInterval());
                } else {
                    if (Settings.Instance.SyncInterval != 0) {
                        Logboxout("Another sync has been scheduled to automatically run in 5 minutes time.");
                        setNextSync(5, fromNow: true);
                    }
                }
            }
            bSyncNow.Enabled = true;
            bSyncNow.Tag = 0; //Reset Push flag regardless of success (don't want it trying every 2 mins)
        }

        private Boolean synchronize() {
            #region Read Outlook items
            Logboxout("Reading Outlook Calendar Entries...");
            List<AppointmentItem> OutlookEntries = null;
            try {
                OutlookEntries = OutlookCalendar.Instance.getCalendarEntriesInRange();
            } catch (System.Exception ex) {
                Logboxout("Unable to access the Outlook calendar. The following error occurred:");
                Logboxout(ex.Message + "\r\n => Retry later.");
                log.Error(ex.StackTrace);
                try { OutlookCalendar.Instance.Reset(); } catch { }
                return false;
            }
            Logboxout(OutlookEntries.Count + " Outlook calendar entries found.");
            Logboxout("--------------------------------------------------");
            #endregion

            #region Read Google items
            Logboxout("Reading Google Calendar Entries...");
            List<Event> googleEntries = null;
            try {
                googleEntries = GoogleCalendar.Instance.GetCalendarEntriesInRange();
            } catch (System.Exception ex) {
                Logboxout("Unable to connect to the Google calendar. The following error occurred:");
                Logboxout(ex.Message);
                log.Error(ex.StackTrace);
                return false;
            }
            Logboxout(googleEntries.Count + " Google calendar entries found.");
            Logboxout("--------------------------------------------------");
            #endregion

            if (Settings.Instance.SyncDirection == SyncDirection.OutlookToGoogle) {
                return sync_outlookToGoogle(OutlookEntries, googleEntries);
            } else if (Settings.Instance.SyncDirection == SyncDirection.GoogleToOutlook) {
                return sync_googleToOutlook(googleEntries, OutlookEntries);
            }
            return false;
        }

        private Boolean sync_outlookToGoogle(List<AppointmentItem> outlookEntries, List<Event> googleEntries) {
            log.Debug("Synchronising from Outlook to Google.");

            //  Make copies of each list of events (Not strictly needed)
            List<AppointmentItem> googleEntriesToBeCreated = new List<AppointmentItem>(outlookEntries);
            List<Event> googleEntriesToBeDeleted = new List<Event>(googleEntries);
            Dictionary<AppointmentItem, Event> entriesToBeCompared = new Dictionary<AppointmentItem, Event>();

            try {
                GoogleCalendar.Instance.ReclaimOrphanCalendarEntries(ref googleEntriesToBeDeleted, ref outlookEntries);
            } catch (System.Exception ex) {
                MainForm.Instance.Logboxout("Unable to reclaim orphan calendar entries in Google calendar. The following error occurred:");
                MainForm.Instance.Logboxout(ex.Message);
                log.Error(ex.StackTrace);
                return false;
            }
            try {
                GoogleCalendar.IdentifyEventDifferences(ref googleEntriesToBeCreated, ref googleEntriesToBeDeleted, entriesToBeCompared);
            } catch (System.Exception ex) {
                MainForm.Instance.Logboxout("Unable to identify calendar differences. The following error occurred:");
                MainForm.Instance.Logboxout(ex.Message);
                log.Error(ex.StackTrace);
                return false;
            }
            
            Logboxout(googleEntriesToBeDeleted.Count + " Google calendar entries to be deleted.");
            Logboxout(googleEntriesToBeCreated.Count + " Google calendar entries to be created.");

            //Protect against very first syncs which may trample pre-existing non-Outlook events in Google
            if (!Settings.Instance.DisableDelete && !Settings.Instance.ConfirmOnDelete &&
                googleEntriesToBeDeleted.Count == googleEntries.Count && googleEntries.Count > 0) {
                if (MessageBox.Show("All Google events are going to be deleted. Do you want to allow this?" +
                    "\r\nNote, " + googleEntriesToBeCreated.Count + " events will then be created.", "Confirm mass deletion",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) == DialogResult.No) 
                {
                    googleEntriesToBeDeleted = new List<Event>();
                }
            }

            #region Delete Google Entries
            if (googleEntriesToBeDeleted.Count > 0) {
                Logboxout("--------------------------------------------------");
                Logboxout("Deleting " + googleEntriesToBeDeleted.Count + " Google calendar entries...");
                try {
                    GoogleCalendar.Instance.DeleteCalendarEntries(googleEntriesToBeDeleted);
                } catch (System.Exception ex) {
                    MainForm.Instance.Logboxout("Unable to delete obsolete entries in Google calendar. The following error occurred:");
                    MainForm.Instance.Logboxout(ex.Message);
                    log.Error(ex.StackTrace);
                    return false;
                }
                Logboxout("Done.");
            }
            #endregion

            #region Create Google Entries
            if (googleEntriesToBeCreated.Count > 0) {
                Logboxout("--------------------------------------------------");
                Logboxout("Creating " + googleEntriesToBeCreated.Count + " Google calendar entries...");
                try {
                    GoogleCalendar.Instance.CreateCalendarEntries(googleEntriesToBeCreated);
                } catch (System.Exception ex) {
                    Logboxout("Unable to add new entries into the Google Calendar. The following error occurred:");
                    Logboxout(ex.Message);
                    log.Error(ex.StackTrace);
                    return false;
                }
                Logboxout("Done.");
            }
            #endregion

            #region Update Google Entries
            if (entriesToBeCompared.Count > 0) {
                Logboxout("--------------------------------------------------");
                Logboxout("Comparing " + entriesToBeCompared.Count + " existing Google calendar entries...");
                int entriesUpdated = 0;
                try {
                    GoogleCalendar.Instance.UpdateCalendarEntries(entriesToBeCompared, ref entriesUpdated);
                } catch (System.Exception ex) {
                    Logboxout("Unable to update existing entries in the Google calendar. The following error occurred:");
                    Logboxout(ex.Message);
                    log.Error(ex.StackTrace);
                    return false;
                }
                Logboxout(entriesUpdated + " entries updated.");
            }
            #endregion

            return true;
        }

        private Boolean sync_googleToOutlook(List<Event> googleEntries, List<AppointmentItem> outlookEntries) {
            log.Debug("Synchronising from Google to Outlook.");

            //  Make copies of each list of events (Not strictly needed)
            List<Event> outlookEntriesToBeCreated = new List<Event>(googleEntries);
            List<AppointmentItem> outlookEntriesToBeDeleted = new List<AppointmentItem>(outlookEntries);
            Dictionary<AppointmentItem, Event> entriesToBeCompared = new Dictionary<AppointmentItem, Event>();
            
            OutlookCalendar.Instance.ReclaimOrphanCalendarEntries(ref outlookEntriesToBeDeleted, ref googleEntries);
            OutlookCalendar.IdentifyEventDifferences(ref outlookEntriesToBeCreated, ref outlookEntriesToBeDeleted, entriesToBeCompared);
            
            Logboxout(outlookEntriesToBeDeleted.Count + " Outlook calendar entries to be deleted.");
            Logboxout(outlookEntriesToBeCreated.Count + " Outlook calendar entries to be created.");

            //Protect against very first syncs which may trample pre-existing non-Google events in Outlook
            if (!Settings.Instance.DisableDelete && !Settings.Instance.ConfirmOnDelete &&
                outlookEntriesToBeDeleted.Count == outlookEntries.Count && outlookEntries.Count > 0) {
                if (MessageBox.Show("All Outlook events are going to be deleted. Do you want to allow this?" +
                    "\r\nNote, " + outlookEntriesToBeCreated.Count + " events will then be created.", "Confirm mass deletion",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) == DialogResult.No) {
                    outlookEntriesToBeDeleted = new List<AppointmentItem>();
                }
            }
            
            #region Delete Outlook Entries
            if (outlookEntriesToBeDeleted.Count > 0) {
                Logboxout("--------------------------------------------------");
                Logboxout("Deleting " + outlookEntriesToBeDeleted.Count + " Outlook calendar entries...");
                try {
                    OutlookCalendar.Instance.DeleteCalendarEntries(outlookEntriesToBeDeleted);
                } catch (System.Exception ex) {
                    MainForm.Instance.Logboxout("Unable to delete obsolete entries in Google calendar. The following error occurred:");
                    MainForm.Instance.Logboxout(ex.Message);
                    log.Error(ex.StackTrace);
                    return false;
                }
                Logboxout("Done.");
            }
            #endregion
            
            #region Create Outlook Entries
            if (outlookEntriesToBeCreated.Count > 0) {
                Logboxout("--------------------------------------------------");
                Logboxout("Creating " + outlookEntriesToBeCreated.Count + " Outlook calendar entries...");
                try {
                    OutlookCalendar.Instance.CreateCalendarEntries(outlookEntriesToBeCreated);
                } catch (System.Exception ex) {
                    Logboxout("Unable to add new entries into the Outlook Calendar. The following error occurred:");
                    Logboxout(ex.Message);
                    log.Error(ex.StackTrace);
                    return false;
                }
                Logboxout("Done.");
            }
            #endregion
            
            #region Update Google Entries
            if (entriesToBeCompared.Count > 0) {
                Logboxout("--------------------------------------------------");
                Logboxout("Comparing " + entriesToBeCompared.Count + " existing Outlook calendar entries...");
                int entriesUpdated = 0;
                try {
                    OutlookCalendar.Instance.UpdateCalendarEntries(entriesToBeCompared, ref entriesUpdated);
                } catch (System.Exception ex) {
                    Logboxout("Unable to update new entries into the Outlook calendar. The following error occurred:");
                    Logboxout(ex.Message);
                    log.Error(ex.StackTrace);
                    return false;
                }
                Logboxout(entriesUpdated + " entries updated.");
            }
            #endregion
            return true;
        }

        #region Compare Event Attributes 
        public static Boolean CompareAttribute(String attrDesc, SyncDirection fromTo, String googleAttr, String outlookAttr, System.Text.StringBuilder sb, ref int itemModified) {
            if (googleAttr == null) googleAttr = "";
            if (outlookAttr == null) outlookAttr = "";
            //Truncate long strings
            String googleAttr_stub = (googleAttr.Length > 50) ? googleAttr.Substring(0, 47) + "..." : googleAttr;
            String outlookAttr_stub = (outlookAttr.Length > 50) ? outlookAttr.Substring(0, 47) + "..." : outlookAttr;
            log.Fine("Comparing " + attrDesc);
            if (googleAttr != outlookAttr) {
                if (fromTo == SyncDirection.GoogleToOutlook) {
                    sb.AppendLine(attrDesc + ": " + outlookAttr_stub + " => " + googleAttr_stub);
                } else {
                    sb.AppendLine(attrDesc + ": " + googleAttr_stub + " => " + outlookAttr_stub);
                }
                itemModified++;
                return true;
            } 
            return false;
        }
        public static Boolean CompareAttribute(String attrDesc, SyncDirection fromTo, Boolean googleAttr, Boolean outlookAttr, System.Text.StringBuilder sb, ref int itemModified) {
            log.Fine("Comparing " + attrDesc);
            if (googleAttr != outlookAttr) {
                if (fromTo == SyncDirection.GoogleToOutlook) {
                    sb.AppendLine(attrDesc + ": " + outlookAttr + " => " + googleAttr);
                } else {
                    sb.AppendLine(attrDesc + ": " + googleAttr + " => " + outlookAttr);
                }
                itemModified++;
                return true;
            } else {
                return false;
            }
        }
        #endregion

        public void Logboxout(string s, bool newLine=true, bool verbose=false) {
            if ((verbose && Settings.Instance.VerboseOutput) || !verbose) {
                String existingText = GetControlPropertyThreadSafe(LogBox, "Text") as String;
                SetControlPropertyThreadSafe(LogBox, "Text", existingText + s + (newLine ? Environment.NewLine : ""));
            }
            if (verbose) log.Debug(s);
            else log.Info(s);
        }

        public void HandleException(System.Exception ex) {
            MessageBox.Show(ex.ToString(), "Exception!", MessageBoxButtons.OK, MessageBoxIcon.Error);
            TextWriter tw = new StreamWriter("exception.txt");
            tw.WriteLine(ex.ToString());
            tw.Close();

            this.Close();
            System.Environment.Exit(-1);
            System.Windows.Forms.Application.Exit();
        }

        
        #region EVENTS
        #region Form actions
        void Save_Click(object sender, EventArgs e) {
            XMLManager.export(Settings.Instance, Program.SettingsFile);

            //Shortcut
            Boolean startupShortcutExists = Program.CheckShortcut(Environment.SpecialFolder.Startup);
            if (Settings.Instance.StartOnStartup && !startupShortcutExists)
                Program.AddShortcut(Environment.SpecialFolder.Startup);
            else if (!Settings.Instance.StartOnStartup && startupShortcutExists)
                Program.RemoveShortcut(Environment.SpecialFolder.Startup);

            //Push Sync
            if (Settings.Instance.OutlookPush) OutlookCalendar.Instance.RegisterForAutoSync();
            else OutlookCalendar.Instance.DeregisterForAutoSync();

            Settings.Instance.LogSettings();
        }

        private void NotifyIcon1_Click(object sender, EventArgs e) {
            this.WindowState = FormWindowState.Normal;
            this.Show();
        }

        void MainFormResize(object sender, EventArgs e) {
            if (!cbMinimizeToTray.Checked) return;
            if (this.WindowState == FormWindowState.Minimized) {
                notifyIcon1.Visible = true;
                this.Hide();
                this.ShowInTaskbar = false;
            } else if (this.WindowState == FormWindowState.Normal) {
                notifyIcon1.Visible = false;
                this.ShowInTaskbar = true;
            }
        }

        void lAboutURL_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e) {
            System.Diagnostics.Process.Start(lAboutURL.Text);
        }
        #endregion
        #region Outlook settings
        public void rbOutlookDefaultMB_CheckedChanged(object sender, EventArgs e) {
            if (rbOutlookDefaultMB.Checked) {
                Settings.Instance.OutlookService = OutlookCalendar.Service.DefaultMailbox;
                OutlookCalendar.Instance.Reset();
                gbEWS.Enabled = false;
                //Update available calendars
                cbOutlookCalendars.DataSource = new BindingSource(OutlookCalendar.Instance.CalendarFolders, null);
            }
        }

        private void rbOutlookAltMB_CheckedChanged(object sender, EventArgs e) {
            if (rbOutlookAltMB.Checked) {
                Settings.Instance.OutlookService = OutlookCalendar.Service.AlternativeMailbox;
                Settings.Instance.MailboxName = ddMailboxName.Text;
                OutlookCalendar.Instance.Reset();
                gbEWS.Enabled = false;
                //Update available calendars
                cbOutlookCalendars.DataSource = new BindingSource(OutlookCalendar.Instance.CalendarFolders, null);
            }
            Settings.Instance.MailboxName = (rbOutlookAltMB.Checked ? ddMailboxName.Text : "");
            ddMailboxName.Enabled = rbOutlookAltMB.Checked;
        }

        private void rbOutlookEWS_CheckedChanged(object sender, EventArgs e) {
            if (rbOutlookEWS.Checked) {
                Settings.Instance.OutlookService = OutlookCalendar.Service.EWS;
                OutlookCalendar.Instance.Reset();
                gbEWS.Enabled = true;
                //Update available calendars
                cbOutlookCalendars.DataSource = new BindingSource(OutlookCalendar.Instance.CalendarFolders, null);
            }
        }

        private void ddMailboxName_SelectedIndexChanged(object sender, EventArgs e) {
            if (this.Visible) {
                Settings.Instance.MailboxName = ddMailboxName.Text;
                OutlookCalendar.Instance.Reset();
            }
        }

        private void txtEWSUser_TextChanged(object sender, EventArgs e) {
            Settings.Instance.EWSuser = txtEWSUser.Text;
        }

        private void txtEWSPass_TextChanged(object sender, EventArgs e) {
            Settings.Instance.EWSpassword = txtEWSPass.Text;
        }

        private void txtEWSServerURL_TextChanged(object sender, EventArgs e) {
            Settings.Instance.EWSserver = txtEWSServerURL.Text;
        }

        private void cbOutlookCalendar_SelectedIndexChanged(object sender, EventArgs e) {
            KeyValuePair<String, MAPIFolder> calendar = (KeyValuePair<String, MAPIFolder>)cbOutlookCalendars.SelectedItem;
            OutlookCalendar.Instance.UseOutlookCalendar = calendar.Value;
        }
        #endregion
        #region Google settings
        private void GetMyGoogleCalendars_Click(object sender, EventArgs e) {
            bGetGoogleCalendars.Enabled = false;
            cbGoogleCalendars.Enabled = false;
            List<MyGoogleCalendarListEntry> calendars = null;
            try {
                calendars = GoogleCalendar.Instance.GetCalendars();
            } catch (System.Exception ex) {
                Logboxout("Unable to get the list of Google Calendars. The following error occurred:");
                Logboxout(ex.Message + "\r\n => Check your network connection.");
            }
            if (calendars != null) {
                cbGoogleCalendars.Items.Clear();
                foreach (MyGoogleCalendarListEntry mcle in calendars) {
                    cbGoogleCalendars.Items.Add(mcle);
                }
                cbGoogleCalendars.SelectedIndex = 0;
            }

            bGetGoogleCalendars.Enabled = true;
            cbGoogleCalendars.Enabled = true;
        }

        private void cbGoogleCalendars_SelectedIndexChanged(object sender, EventArgs e) {
            Settings.Instance.UseGoogleCalendar = (MyGoogleCalendarListEntry)cbGoogleCalendars.SelectedItem;
        }

        private void btResetGCal_Click(object sender, EventArgs e) {
            if (MessageBox.Show("This will reset the Google account you are using to synchronise with.\r\n" +
                "Useful if you want to start syncing to a different account.",
                "Reset Google account?", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == System.Windows.Forms.DialogResult.Yes) {
                    Settings.Instance.UseGoogleCalendar.Id = null;
                    Settings.Instance.UseGoogleCalendar.Name = null;
                    this.cbGoogleCalendars.Items.Clear();
                    GoogleCalendar.Instance.Reset();
            }
        }
        #endregion
        #region Sync options
        private void syncDirection_SelectedIndexChanged(object sender, EventArgs e) {
            Settings.Instance.SyncDirection = (SyncDirection)syncDirection.SelectedItem;
            if (Settings.Instance.SyncDirection == SyncDirection.OutlookToGoogle) {
                tbHelp.Text = "It's advisable to create a dedicated calendar in Google for synchronising to from Outlook. \r\n" +
                    "Otherwise you may end up with duplicates or non-Outlook entries deleted.";
            } else if (Settings.Instance.SyncDirection == SyncDirection.GoogleToOutlook &&
                cbOutlookCalendars.Text == "Default Calendar") {
                tbHelp.Text = "It's advisable to create a dedicated calendar in Outlook for synchronising to from Google. \r\n" +
                    "Otherwise you may end up with duplicates or non-Google entries deleted.";
            } else if (Settings.Instance.SyncDirection == SyncDirection.Bidirectional) {
                tbHelp.Text = "It's advisable to run a one-way sync first before configuring bi-directional. Have you done this?"; 
            }
        }

        private void tbDaysInThePast_ValueChanged(object sender, EventArgs e) {
            Settings.Instance.DaysInThePast = (int)tbDaysInThePast.Value;
        }

        private void tbDaysInTheFuture_ValueChanged(object sender, EventArgs e) {
            Settings.Instance.DaysInTheFuture = (int)tbDaysInTheFuture.Value;
        }

        private void tbMinuteOffsets_ValueChanged(object sender, EventArgs e) {
            Settings.Instance.SyncInterval = (int)tbInterval.Value;
            setNextSync(getResyncInterval());
        }

        private void cbIntervalUnit_SelectedIndexChanged(object sender, EventArgs e) {
            Settings.Instance.SyncIntervalUnit = cbIntervalUnit.Text;
            setNextSync(getResyncInterval());
        }

        private void cbOutlookPush_CheckedChanged(object sender, EventArgs e) {
            Settings.Instance.OutlookPush = cbOutlookPush.Checked;
        }

        private void CbAddDescriptionCheckedChanged(object sender, EventArgs e) {
            Settings.Instance.AddDescription = cbAddDescription.Checked;
        }

        private void CbAddRemindersCheckedChanged(object sender, EventArgs e) {
            Settings.Instance.AddReminders = cbAddReminders.Checked;
        }

        private void cbAddAttendees_CheckedChanged(object sender, EventArgs e) {
            Settings.Instance.AddAttendees = cbAddAttendees.Checked;
        }

        private void cbMergeItems_CheckedChanged(object sender, EventArgs e) {
            Settings.Instance.MergeItems = cbMergeItems.Checked;
        }

        private void cbConfirmOnDelete_CheckedChanged(object sender, System.EventArgs e) {
            Settings.Instance.ConfirmOnDelete = cbConfirmOnDelete.Checked;
        }

        private void cbDisableDeletion_CheckedChanged(object sender, System.EventArgs e) {
            Settings.Instance.DisableDelete = cbDisableDeletion.Checked;
            cbConfirmOnDelete.Enabled = !cbDisableDeletion.Checked;
        }
        #endregion
        #region Application settings
        private void cbStartOnStartup_CheckedChanged(object sender, EventArgs e) {
            Settings.Instance.StartOnStartup = cbStartOnStartup.Checked;
        }

        private void cbShowBubbleTooltipsCheckedChanged(object sender, System.EventArgs e) {
            Settings.Instance.ShowBubbleTooltipWhenSyncing = cbShowBubbleTooltips.Checked;
        }

        private void cbStartInTrayCheckedChanged(object sender, System.EventArgs e) {
            Settings.Instance.StartInTray = cbStartInTray.Checked;
        }

        private void cbMinimizeToTrayCheckedChanged(object sender, System.EventArgs e) {
            Settings.Instance.MinimizeToTray = cbMinimizeToTray.Checked;
        }

        private void cbCreateFiles_CheckedChanged(object sender, EventArgs e) {
            Settings.Instance.CreateCSVFiles = cbCreateFiles.Checked;
        }
        
        private void cbLoggingLevel_SelectedIndexChanged(object sender, EventArgs e) {
            ((log4net.Repository.Hierarchy.Hierarchy)LogManager.GetRepository()).Root.Level = log.Logger.Repository.LevelMap[MainForm.Instance.cbLoggingLevel.Text];
            ((log4net.Repository.Hierarchy.Hierarchy)LogManager.GetRepository()).RaiseConfigurationChanged(EventArgs.Empty);
            Settings.Instance.LoggingLevel = MainForm.Instance.cbLoggingLevel.Text.ToUpper();
            log.Info("Logging level changing to " + Settings.Instance.LoggingLevel);
        }

        private void btLogLocation_Click(object sender, EventArgs e) {
            log4net.Appender.IAppender[] appenders = log.Logger.Repository.GetAppenders();
            String logFileLocation = (((log4net.Appender.FileAppender)appenders[0]).File);
            logFileLocation = logFileLocation.Substring(0, logFileLocation.LastIndexOf("\\"));
            System.Diagnostics.Process.Start(@logFileLocation);
        }
        #endregion

        private void cbVerboseOutput_CheckedChanged(object sender, EventArgs e) {
            Settings.Instance.VerboseOutput = cbVerboseOutput.Checked;
        }

        private void pbDonate_Click(object sender, EventArgs e) {
            System.Diagnostics.Process.Start("https://www.paypal.com/cgi-bin/webscr?cmd=_s-xclick&hosted_button_id=RT46CXQDSSYWJ");
        }
        #endregion

        #region Thread safe access to form components
        //private delegate Control getControlThreadSafeDelegate(Control control);
        //Used to update the logbox from the Sync() thread
        public delegate void SetControlPropertyThreadSafeDelegate(Control control, string propertyName, object propertyValue);
        public delegate object GetControlPropertyThreadSafeDelegate(Control control, string propertyName);
        
        //private static Control getControlThreadSafe(Control control) {
        //    if (control.InvokeRequired) {
        //        return (Control)control.Invoke(new getControlThreadSafeDelegate(getControlThreadSafe), new object[] { control });
        //    } else {
        //        return control;
        //    }
        //}
        public object GetControlPropertyThreadSafe(Control control, string propertyName) {
            if (control.InvokeRequired) {
                return control.Invoke(new GetControlPropertyThreadSafeDelegate(GetControlPropertyThreadSafe), new object[] { control, propertyName });
            } else {
                return control.GetType().InvokeMember(propertyName, System.Reflection.BindingFlags.GetProperty, null, control, null);
            }
        }
        public void SetControlPropertyThreadSafe(Control control, string propertyName, object propertyValue) {
            if (control.InvokeRequired) {
                control.Invoke(new SetControlPropertyThreadSafeDelegate(SetControlPropertyThreadSafe), new object[] { control, propertyName, propertyValue });
            } else {
                var theObject = control.GetType().InvokeMember(propertyName, System.Reflection.BindingFlags.SetProperty, null, control, new object[] { propertyValue });
                if (control.GetType().Name == "TextBox") {
                    TextBox tb = control as TextBox;
                    tb.SelectionStart = tb.Text.Length;
                    tb.ScrollToCaret();
                }
            }
        }
        #endregion

    }
}