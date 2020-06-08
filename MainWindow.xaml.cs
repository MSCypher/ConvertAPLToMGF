using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.ComponentModel;
using System.Threading;
using System.Text.RegularExpressions;

namespace ConvertAPLToMGF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public struct ClonedPeaklist
        {
            public string Filename;     // actual full filename
            public long StartPos;       // peaklist start position
            public long EndPos;         // peaklist end position
        }

        public struct MSMSPepID
        {
            public string PepSeq;       // unmodified peptide sequence
            public string PEP;          // posterior error prob of peptide
            public string Score;        // peptide score
        }
        
        public struct MSMSFeature
        {
            public string Rt;           // retention time for msms
            public string Pif;			// precursor ion fraction
            public string Score;        // peptide score - empty if no score
        }

        public struct PepFeature
        {
            public string FeatureNum;   // feature number aka line number
            public string Rawfile;      // rawfile name
            public string DataPoints;   // number of data points
            public string NumScans;     // number of scans
            public string IsoPeaks;     // number of isotopic peaks
            public string RtApex;       // Rt at feature apex
            public string Pif;          // Precursor ion fraction
            public string Intensity;	// precursor intensity - using yyy after pepmass
            public string ScanNumbers;  // number of msms scans
            public string Scans;        // original msms scans
            public string BestScan;     // highest scoring scan number
            public string BestScore;    // highest score
            public string Sequence;     // MQ peptide sequence
        }
        
        BackgroundWorker bgWorker;
        string combinedFolder = null;
        bool bCombinedFolder = false;
        bool bMSMSPepTxt = false;
        bool bAllPeptidesTxt = false;
        bool bEvidenceTxt = false;
        bool bOriginalPeaklist = true;
        bool bOriginalFileStructure = true;
        bool bPasef = false;

        public MainWindow()
        {
            InitializeComponent();
            bgWorker = new BackgroundWorker();
            bgWorker.DoWork += new DoWorkEventHandler(BgWorker_DoWork);
            bgWorker.ProgressChanged += new ProgressChangedEventHandler(BgWorker_ProgressChanged);
            bgWorker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(BgWorker_RunWorkerCompleted);
            bgWorker.WorkerReportsProgress = true;
            bgWorker.WorkerSupportsCancellation = true;
        }

        void BgWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            //If it was cancelled midway
            if (e.Cancelled)
            {
                textUpdate.Text = "Task Cancelled.";
            }
            else if (e.Error != null)
            {
                textUpdate.Text = "Error while performing background operation.";
            }
            else
            {
                textUpdate.Text = "Task Completed...";
            }
            process.IsEnabled = true;
            cancel.IsEnabled = false;
        }

        void BgWorker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            //Here you play with the main UI thread
            fileProgress.Value = e.ProgressPercentage;
            textUpdate.Text = e.UserState as String + fileProgress.Value.ToString() + "%";
        }

        void BgWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            // NOTE : Never play with the UI thread here...
            SortedDictionary<int, PepFeature> allFeatures = null;
            SortedDictionary<string, MSMSFeature> msmsScans = null;
            SortedDictionary<string, MSMSPepID> msmsPepID = null;
            SortedDictionary<string, int> scanFeatures = null;
            SortedDictionary<string, int> secpepFeatures = null;
            SortedDictionary<string, string> evidence = null;

            //byte[] newLine = Encoding.Unicode.GetBytes(Environment.NewLine);
            //int newLineLen = newLine.Length;

            if (!String.IsNullOrEmpty(combinedFolder))
            {
                string msmsScansTextFile = combinedFolder + "\\" + "msmsScans.txt";
                string allPeptidesTextFile = combinedFolder + "\\" + "allPeptides.txt";
                string evidenceTextFile = combinedFolder + "\\" + "evidence.txt";
                string msmsTextFile = combinedFolder + "\\" + "msms.txt";
                bCombinedFolder = true;

                // open msms.txt file and fill Sorted dictionary rawname_scan_type plus scores
                FileInfo msmsPep = new FileInfo(msmsTextFile);    // "C:\\Users\\kapp\\Programs\\APLToMGF\\msms.txt"
                if (msmsPep.Exists)
                {
                    bMSMSPepTxt = true;
                    long totalBytes = msmsPep.Length;
                    long bytesRead = 0;
                    using (StreamReader msmsPepStream = msmsPep.OpenText())
                    {
                        string input = null;
                        msmsPepID = new SortedDictionary<string, MSMSPepID>();
                        int pctComplete = 0;
                        int pctUpdate = 1;
                        char[] delimiters = new char[] { '\t' };
                        int nCounter = 0;
                        int nRawCounter = -1;
                        int nScanCounter = -1;
                        int nSeqCounter = -1;
                        int nTypeCounter = -1;
                        int nScoreCounter = -1;
                        int nPEPCounter = -1;
                        int nIgnore = 0;

                        // read header line and store headings
                        if ((input = msmsPepStream.ReadLine()) != null)
                        {
                            string[] parts = input.Split(delimiters, StringSplitOptions.None);
                            foreach (string s in parts)
                            {
                                if (s == "Raw file")
                                    nRawCounter = nCounter;
                                else if (s == "Scan number")
                                    nScanCounter = nCounter;
                                else if (s == "Sequence")
                                    nSeqCounter = nCounter;
                                else if (s == "Type")
                                    nTypeCounter = nCounter;
                                else if (s == "PEP")
                                    nPEPCounter = nCounter;
                                else if (s == "Score")
                                    nScoreCounter = nCounter;
                                else
                                    nIgnore++;

                                ++nCounter;
                            }
                        }

                        while ((input = msmsPepStream.ReadLine()) != null)
                        {
                            string key = null;
                            string value = null;
                            string value2 = null;
                            string value3 = null;
                            string[] parts = input.Split(delimiters, StringSplitOptions.None);

                            // create a unique key that can be associated between msms.txt and the apl files
                            if (nTypeCounter != -1 && nRawCounter != -1 && nScanCounter != -1)
                            {
                                if (parts[nTypeCounter].Contains("SECPEP"))
                                    key = parts[nRawCounter] + "_" + parts[nScanCounter] + "_sec";
                                else
                                    key = parts[nRawCounter] + "_" + parts[nScanCounter] + "_pre";
                            }

                            if (nSeqCounter != -1 && !String.IsNullOrEmpty(parts[nSeqCounter]))
                                value = parts[nSeqCounter];
                            else
                                value = "NaN";

                            if (nPEPCounter != -1 && !String.IsNullOrEmpty(parts[nPEPCounter]))
                                value2 = parts[nPEPCounter];
                            else
                                value2 = "NaN";

                            if (nScoreCounter != -1 && !String.IsNullOrEmpty(parts[nScoreCounter]))
                                value3 = parts[nScoreCounter];
                            else
                                value3 = "NaN";

                            MSMSPepID msmsTxtID;
                            msmsTxtID.PepSeq = value;
                            msmsTxtID.PEP = value2;
                            msmsTxtID.Score = value3;

                            if (!msmsPepID.ContainsKey(key))
                                msmsPepID.Add(key, msmsTxtID);

                            bytesRead += msmsPepStream.CurrentEncoding.GetByteCount(input);
                            pctComplete = (int)(((double)bytesRead / (double)totalBytes) * 100);

                            if (pctComplete >= pctUpdate)
                            {
                                Thread.Sleep(10);
                                bgWorker.ReportProgress(pctComplete, "Reading msms file...  ");

                                // If cancel button was pressed while the execution is in progress
                                // Change the state from cancellation ---> cancel'ed
                                if (bgWorker.CancellationPending)
                                {
                                    e.Cancel = true;
                                    bgWorker.ReportProgress(0, "");
                                    return;
                                }
                                pctUpdate += 1;
                            }
                        }
                        msmsPepStream.Close();
                    }
                }

                // open msmsScans.txt file and fill Sorted dictionary rawname_scan plus Rt
                FileInfo msmsInfo = new FileInfo(msmsScansTextFile);    // "C:\\Users\\kapp\\Programs\\APLToMGF\\msmsScans.txt"
                if (msmsInfo.Exists)
                {
                    long totalBytes = msmsInfo.Length;
                    long bytesRead = 0;
                    using (StreamReader msmsStream = msmsInfo.OpenText())
                    {
                        string input = null;
                        msmsScans = new SortedDictionary<string, MSMSFeature>();
                        int pctComplete = 0;
                        int pctUpdate = 1;
                        char[] delimiters = new char[] { '\t' };
                        int nCounter = 0;
                        int nRawCounter = -1;
                        int nScanCounter = -1;
                        int nRtCounter = -1;
                        int nPIFCounter = -1;
                        int nScoreCounter = -1;
                        int nIgnore = 0;

                        // read header line and store headings
                        if ((input = msmsStream.ReadLine()) != null)
                        {
                            string[] parts = input.Split(delimiters, StringSplitOptions.None);
                            foreach (string s in parts)
                            {
                                if (s == "Raw file")
                                    nRawCounter = nCounter;
                                else if (s == "Scan number")
                                    nScanCounter = nCounter;
                                else if (s == "Retention time")
                                    nRtCounter = nCounter;
                                else if (s == "Parent intensity fraction")
                                    nPIFCounter = nCounter;
                                else if (s == "Score")
                                    nScoreCounter = nCounter;
                                else
                                    nIgnore++;

                                ++nCounter;
                            }
                        }

                        while ((input = msmsStream.ReadLine()) != null)
                        {
                            string key = null;
                            string value = null;
                            string value2 = null;
                            string value3 = null;
                            string[] parts = input.Split(delimiters, StringSplitOptions.None);

                            // create a unique key that can be associated between msmsScans.txt and the apl files
                            if (nRawCounter != -1 && nScanCounter != -1)
                                key = parts[nRawCounter] + "_" + parts[nScanCounter];

                            if (nRtCounter != -1 && !String.IsNullOrEmpty(parts[nRtCounter]))
                                value = parts[nRtCounter];
                            else
                                value = "0";

                            if (nPIFCounter != -1 && !String.IsNullOrEmpty(parts[nPIFCounter]))
                                value2 = parts[nPIFCounter];
                            else
                                value2 = "NaN";

                            if (nScoreCounter != -1 && !String.IsNullOrEmpty(parts[nScoreCounter]))
                                value3 = parts[nScoreCounter];
                            else
                                value3 = "0";

                            MSMSFeature feature;
                            feature.Rt = value;
                            feature.Pif = value2;
                            feature.Score = value3;

                            if (!msmsScans.ContainsKey(key))
                                msmsScans.Add(key, feature);

                            bytesRead += msmsStream.CurrentEncoding.GetByteCount(input);
                            pctComplete = (int)(((double)bytesRead / (double)totalBytes) * 100);

                            if (pctComplete >= pctUpdate)
                            {
                                Thread.Sleep(10);
                                bgWorker.ReportProgress(pctComplete, "Reading msmsScans file...  ");

                                // If cancel button was pressed while the execution is in progress
                                // Change the state from cancellation ---> cancel'ed
                                if (bgWorker.CancellationPending)
                                {
                                    e.Cancel = true;
                                    bgWorker.ReportProgress(0, "");
                                    return;
                                }
                                pctUpdate += 1;
                            }
                        }
                        msmsStream.Close();
                    }
                }

                // open evidence.txt file and fill Sorted dictionary
                FileInfo evidenceInfo = new FileInfo(evidenceTextFile);    // "C:\\Users\\kapp\\Programs\\APLToMGF\\evidence.txt"
                if (evidenceInfo.Exists)
                {
                    bEvidenceTxt = true;
                    long totalBytes = evidenceInfo.Length;
                    long bytesRead = 0;
                    using (StreamReader evidenceStream = evidenceInfo.OpenText())
                    {
                        string input = null;
                        evidence = new SortedDictionary<string, string>();
                        int pctComplete = 0;
                        int pctUpdate = 1;
                        char[] delimiters = new char[] { '\t' };
                        int nCounter = 0;
                        int nRawCounter = -1;
                        int nScanCounter = -1;
                        int nMSMSCounter = -1;
                        int nChargeCounter = -1;
                        int nSeqCounter = -1;
                        int nIgnore = 0;

                        // read header line and store headings
                        if ((input = evidenceStream.ReadLine()) != null)
                        {
                            string[] parts = input.Split(delimiters, StringSplitOptions.None);
                            foreach (string s in parts)
                            {
                                if (s == "Sequence")
                                    nSeqCounter = nCounter;
                                else if (s == "Raw file")
                                    nRawCounter = nCounter;
                                else if (s == "Charge")
                                    nChargeCounter = nCounter;
                                else if (s == "MS/MS count")
                                    nMSMSCounter = nCounter;
                                else if (s == "MS/MS scan number")
                                    nScanCounter = nCounter;
                                else
                                    nIgnore++;

                                ++nCounter;
                            }
                        }

                        while ((input = evidenceStream.ReadLine()) != null)
                        {
                            string key = null;
                            string value = null;
                            string[] parts = input.Split(delimiters, StringSplitOptions.None);

                            // create a unique key that can be associated between evidence.txt and the apl files
                            if (nMSMSCounter != -1 && parts[nMSMSCounter] != "0")
                            {
                                if(nRawCounter != -1 && nScanCounter != -1 && nChargeCounter != -1)
                                    key = parts[nRawCounter] + "_" + parts[nScanCounter] + "_" + parts[nChargeCounter];

                                if (nSeqCounter != -1 && !String.IsNullOrEmpty(parts[nSeqCounter]))
                                    value = parts[nSeqCounter];
                                else
                                    value = "";

                                if (!evidence.ContainsKey(key))
                                    evidence.Add(key, value);
                            }

                            bytesRead += evidenceStream.CurrentEncoding.GetByteCount(input);
                            pctComplete = (int)(((double)bytesRead / (double)totalBytes) * 100);

                            if (pctComplete >= pctUpdate)
                            {
                                Thread.Sleep(10);
                                bgWorker.ReportProgress(pctComplete, "Reading evidence file...  ");

                                // If cancel button was pressed while the execution is in progress
                                // Change the state from cancellation ---> cancel'ed
                                if (bgWorker.CancellationPending)
                                {
                                    e.Cancel = true;
                                    bgWorker.ReportProgress(0, "");
                                    return;
                                }
                                pctUpdate += 1;
                            }
                        }
                        evidenceStream.Close();
                    }
                }

                // open allPeptides.txt and fill Sorted dictionary with feature# plus Intensity and scan#s
                FileInfo allPeptidesInfo = new FileInfo(allPeptidesTextFile);
                if (allPeptidesInfo.Exists)
                {
                    bAllPeptidesTxt = true;
                    long totalBytes = allPeptidesInfo.Length;
                    long bytesRead = 0;
                    using (StreamReader pepStream = allPeptidesInfo.OpenText())
                    {
                        string input = null;
                        allFeatures = new SortedDictionary<int, PepFeature>();
                        scanFeatures = new SortedDictionary<string, int>();
                        secpepFeatures = new SortedDictionary<string, int>();
                        int pctComplete = 0;
                        int pctUpdate = 1;
                        char[] delimiters = new char[] { '\t' };
                        int nCounter = 0;
                        int nRawFile = -1;
                        int nDataPoints = -1;
                        int nScans = -1;
                        int nIsoPeaks = -1;
                        int nIntensityCounter = -1;
                        int nSequence = -1;
                        int nRtApex = -1;
                        int nMSMSScanCounter = -1;
                        int nMSMSNumCounter = -1;
                        int nPIF = -1;
                        int nIgnore = 0;
                        string strTempRaw = null;
                        int nSecPepCounter = 0;

                        // read header line and store headings
                        if ((input = pepStream.ReadLine()) != null)
                        {
                            string[] parts = input.Split(delimiters, StringSplitOptions.None);
                            foreach (string s in parts)
                            {
                                if (s == "Raw file")
                                    nRawFile = nCounter;
                                else if (s == "Number of data points")
                                    nDataPoints = nCounter;
                                else if (s == "Number of scans")
                                    nScans = nCounter;
                                else if (s == "Number of isotopic peaks")
                                    nIsoPeaks = nCounter;
                                else if (s == "PIF")
                                    nPIF = nCounter;
                                else if (s == "Retention time")
                                    nRtApex = nCounter;
                                else if (s == "Sequence")
                                    nSequence = nCounter;
                                else if (s == "Intensity")
                                    nIntensityCounter = nCounter;
                                else if (s == "MS/MS Count")
                                    nMSMSNumCounter = nCounter;
                                else if (s == "Number of pasef MS/MS")
                                    nMSMSNumCounter = nCounter;
                                else if (s == "MSMS Scan Numbers")
                                    nMSMSScanCounter = nCounter;
                                else if (s == "MS/MS scan number")
                                    nMSMSScanCounter = nCounter;
                                else
                                    nIgnore++;

                                ++nCounter;
                            }
                        }

                        nCounter = 0;
                        while ((input = pepStream.ReadLine()) != null)
                        {
                            int key = 0;
                            string counter = null;
                            string rawfile = null;
                            string dataPointsValue = null;
                            string scansValue = null;
                            string isoPeaksValue = null;
                            string pifValue = null;
                            string sequence = null;
                            string value = null;
                            string value1 = null;
                            string value2 = null;
                            string value3 = null;

                            double bestScore = 0;
                            double bestPIF = 0;
                            string bestScan = null;
                            string nextbestScan = null;

                            string[] parts = input.Split(delimiters, StringSplitOptions.None);

                            // create a unique key that can be associated between allpeptides.txt and the apl files
                            key = nCounter++;
                            counter = Convert.ToString(nCounter);

                            if (nRawFile != -1 && !String.IsNullOrEmpty(parts[nRawFile]))
                                rawfile = parts[nRawFile];
                            else
                                rawfile = "";

                            if (nDataPoints != -1 && !String.IsNullOrEmpty(parts[nDataPoints]))
                                dataPointsValue = parts[nDataPoints];
                            else
                                dataPointsValue = "NaN";

                            if (nScans != -1 && !String.IsNullOrEmpty(parts[nScans]))
                                scansValue = parts[nScans];
                            else
                                scansValue = "NaN";

                            if (nIsoPeaks != -1 && !String.IsNullOrEmpty(parts[nIsoPeaks]))
                                isoPeaksValue = parts[nIsoPeaks];
                            else
                                isoPeaksValue = "NaN";

                            if (nPIF != -1 && !String.IsNullOrEmpty(parts[nPIF]))
                                pifValue = parts[nPIF];
                            else
                                pifValue = "NaN";

                            if (nRtApex != -1 && !String.IsNullOrEmpty(parts[nRtApex]))
                                value = parts[nRtApex];
                            else
                                value = "0";

                            if (nSequence != -1 && !String.IsNullOrEmpty(parts[nSequence]))
                                sequence = parts[nSequence];
                            else
                                sequence = "NaN";

                            if (sequence == " ")
                                sequence = "NaN";

                            if (nIntensityCounter != -1 && !String.IsNullOrEmpty(parts[nIntensityCounter]))
                                value1 = parts[nIntensityCounter];
                            else
                                value1 = "0.0";

                            if (nMSMSScanCounter != -1 && !String.IsNullOrEmpty(parts[nMSMSScanCounter]))
                                value2 = parts[nMSMSScanCounter];
                            else
                                value2 = "";

                            if (nMSMSNumCounter != -1 && !String.IsNullOrEmpty(parts[nMSMSNumCounter]))
                                value3 = parts[nMSMSNumCounter];
                            else
                                value3 = "";

                            // check whether the filename has changed
                            if (!String.IsNullOrEmpty(strTempRaw))
                            {
                                if (strTempRaw == rawfile)
                                    ++nSecPepCounter;
                                else
                                {
                                    nSecPepCounter = 0;
                                    strTempRaw = rawfile;
                                }
                            }
                            else
                                strTempRaw = rawfile;

                            // Before adding feature check the msms scan numbers for best score and set best scan for feature
                            // value2 are the scan numbers if any i.e. 2345;3457;23555
                            if (!String.IsNullOrEmpty(value2))
                            {
                                char[] scanDelimit = new char[] { ';' };
                                string[] scannums = value2.Split(scanDelimit, StringSplitOptions.None);
                                bestScan = scannums[0];
                                nextbestScan = scannums[0];
                                
                                foreach (string s in scannums)
                                {
                                    // use rawfile_scan as key and get msmsfeature information
                                    string strKey = rawfile + "_" + s;
                                    MSMSFeature msmsFeature;
                                    MSMSPepID msmsPeptide;

                                    if (msmsScans != null && msmsScans.TryGetValue(strKey, out msmsFeature))
                                    {
                                        double score = Convert.ToDouble(msmsFeature.Score);
                                        if (score > bestScore)
                                        {
                                            bestScore = score;
                                            bestScan = s;
                                        }

                                        double lfPIF = Convert.ToDouble(msmsFeature.Pif);
                                        if(lfPIF > bestPIF)
                                        {
                                            bestPIF = lfPIF;
                                            nextbestScan = s;
                                        }

                                        if (!scanFeatures.ContainsKey(strKey))
                                            scanFeatures.Add(strKey, key);
                                    }
                                    else
                                    {
                                        strKey += "_pre";
                                        if (msmsPepID != null && msmsPepID.TryGetValue(strKey, out msmsPeptide))
                                        {
                                            double score = Convert.ToDouble(msmsPeptide.Score);
                                            if (score > bestScore)
                                            {
                                                bestScore = score;
                                                bestScan = s;
                                            }

                                            if (!scanFeatures.ContainsKey(strKey))
                                                scanFeatures.Add(strKey, key);
                                        }
                                    }
                                }

                                // if no score then choose the best scan based on PIF value
                                if(bestScore <= 0)
                                    bestScan = nextbestScan;
                            }

                            PepFeature feature;
                            feature.FeatureNum = counter;
                            feature.Rawfile = rawfile;
                            feature.DataPoints = dataPointsValue;
                            feature.NumScans = scansValue;
                            feature.IsoPeaks = isoPeaksValue;
                            feature.RtApex = value;
                            feature.Pif = pifValue;
                            feature.Intensity = value1;
                            feature.Scans = value2;
                            feature.ScanNumbers = value3;
                            feature.BestScan = bestScan;
                            feature.BestScore = Convert.ToString(bestScore);
                            feature.Sequence = sequence;

                            if (!allFeatures.ContainsKey(key))
                                allFeatures.Add(key, feature);

                            // rawfile_row for secpeps - key from above is the value and new key is the string (rawfile_row)
                            string strSecPepKey = rawfile + "_" + Convert.ToString(nSecPepCounter);
                            if (!secpepFeatures.ContainsKey(strSecPepKey))
                                secpepFeatures.Add(strSecPepKey, key);

                            bytesRead += pepStream.CurrentEncoding.GetByteCount(input);
                            pctComplete = (int)(((double)bytesRead / (double)totalBytes) * 100);

                            if (pctComplete >= pctUpdate)
                            {
                                Thread.Sleep(10);
                                bgWorker.ReportProgress(pctComplete,"Reading allPeptides file...  ");

                                // If cancel button was pressed while the execution is in progress
                                // Change the state from cancellation ---> cancel'ed
                                if (bgWorker.CancellationPending)
                                {
                                    e.Cancel = true;
                                    bgWorker.ReportProgress(0, "");
                                    return;
                                }
                                pctUpdate += 1;
                            }
                        }
                        pepStream.Close();
                    }
                }
            }

            int files = fileNames.Items.Count;
            string fragmentType = "INSTRUMENT=";
            bool bIgnore = false;

            fragSelector.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Normal, new Action(delegate() { fragmentType += fragSelector.Text; }));

            bgWorker.ReportProgress(0,"Reading apl files...  ");
            Thread.Sleep(10);

            var mgffileList = new List<string>();
            SortedDictionary<string, ClonedPeaklist> mgfOriginalPeaklist = new SortedDictionary<string, ClonedPeaklist>();

            // go through all files - do the iso's first and then secpep in order to borrow original peaklists
            for (int i = 0; i < files; i++)
            {
                Thread.Sleep(100);
                bgWorker.ReportProgress((int)((double)(i + 1) / (double)files * 100.0), "Processing precursor apl files...  ");

                // If cancel button was pressed while the execution is in progress
                // Change the state from cancellation ---> cancel'ed
                if (bgWorker.CancellationPending)
                {
                    e.Cancel = true;
                    bgWorker.ReportProgress(0, "");
                    return;
                }
                
                string aplFile = fileNames.Items[i].ToString();
                FileInfo aplInfo = new FileInfo(aplFile);
                string aplPath = aplInfo.DirectoryName;
                string aplFileName = System.IO.Path.GetFileNameWithoutExtension(aplInfo.Name);

                // if secpep file continue
                if (aplInfo.Exists && aplInfo.Name.Contains(".secpep."))
                    continue;

                if (aplInfo.Exists && aplInfo.Extension == ".apl")
                {
                    using (StreamReader aplStream = new StreamReader(aplFile))
                    {
                        var inputBuild = new System.Text.StringBuilder();
                        string input = null;
                        long currentPosition = 0;
                        long currentStart = 0;
                        long currentEnd = 0;
                        int len = 0;
                        string mgfout = null;
                        string pepmass = null;
                        string charge = null;
                        string chargeNum = null;
                        int nCharge = 0;
                        string title = null;
                        string tempTitle = null;
                        string rettime = null;
                        string scans = null;
                        string peaklist = null;
                        int nIgnore = 0;

                        while (aplStream.Peek() > -1)
                        {
                            int nextCharNum = aplStream.Read();
                            char nextChar = (char)nextCharNum;

                            // if hit end of line
                            if (nextChar == '\r' || nextChar == '\n')
                            {
                                // convert stringbuilder to local string - check line endings etc ???
                                input = inputBuild.ToString();

                                // use the input string
                                if (input.Contains("peaklist start"))
                                    ++nIgnore;
                                else if (input.Contains("mz="))
                                {
                                    len = input.Length;
                                    pepmass = "PEPMASS=";
                                    pepmass += input.Substring(3, len - 3);
                                }
                                else if (input.Contains("fragmentation="))
                                    ++nIgnore;
                                else if (input.Contains("charge="))
                                {
                                    len = input.Length;
                                    charge = "CHARGE=" + input.Substring(7, len - 7) + "+";
                                    chargeNum = " Charge: " + input.Substring(7, len - 7);
                                    nCharge = Convert.ToInt32(input.Substring(7, len - 7));
                                }
                                else if (input.Contains("header="))
                                {
                                    len = input.Length;
                                    title = "TITLE=";
                                    tempTitle = input.Substring(7, len - 7);
                                    title += input.Substring(7, len - 7);
                                    title += chargeNum;

                                    if (bCombinedFolder && bAllPeptidesTxt && bEvidenceTxt && bMSMSPepTxt)
                                    {
                                        // retention time added after header line - extract raw file name plus scan# (parts1 and 3 from apl Title header line)
                                        // look for key in msmsScans list - use Value for Rt*60
                                        // allow for spaces in rawfile
                                        int posS = 0;
                                        int posE = 0;
                                        string strfilename = null;
                                        string strScanNum = null;
                                        posS = tempTitle.IndexOf("RawFile: ");
                                        posE = tempTitle.IndexOf(" Index:");
                                        if (posS >= 0 && posE > posS)
                                        {
                                            posS += 9;
                                            strfilename = tempTitle.Substring(posS, posE - posS);
                                        }

                                        posS = tempTitle.IndexOf("Index: ");
                                        posE = tempTitle.IndexOf(" Precursor:");
                                        if (posS > 0 && posE > posS)
                                        {
                                            posS += 7;
                                            strScanNum = tempTitle.Substring(posS, posE - posS);
                                        }

                                        string key = strfilename + "_" + strScanNum;
                                        string key2 = strfilename + "_" + strScanNum + "_pre";

                                        MSMSFeature feature;
                                        MSMSPepID msmsID;
                                        string strRettime = "0.0";
                                        string strRetApex = "0.0";
                                        string strMS2Pif = "NaN";
                                        string strFeaturePif = "NaN";
                                        string strSequence = "NaN";
                                        string strPepSeq = "NaN";
                                        string strIntensity = "NaN";
                                        string strDataPoints = "NaN";
                                        string strNumScans = "NaN";
                                        string strIsoPeaks = "NaN";
                                        string strFeatureNum = "NaN";
                                        string strMS2Score = "NaN";
                                        string strPEP = "NaN";
                                        string strBestScore = "0";
                                        string strBestScan = "NaN";
                                        string strMultiplicity = "NaN";

                                        if (msmsScans != null && msmsScans.TryGetValue(key, out feature))
                                        {
                                            // convert rettime to seconds
                                            double retTimeinsecs = Convert.ToDouble(feature.Rt) * 60.0;
                                            strRettime = Convert.ToString(retTimeinsecs);
                                            strMS2Pif = feature.Pif;
                                            strMS2Score = feature.Score;
                                        }

                                        if (msmsPepID != null && msmsPepID.TryGetValue(key2, out msmsID))
                                        {
                                            strPepSeq = msmsID.PepSeq;
                                            strPEP = msmsID.PEP;
                                            strMS2Score = msmsID.Score;
                                        }

                                        // use allpeptides to get intensity of peptide feature
                                        // for secpep get feature# from Silind: xxx
                                        // for precursor (iso) get feature# indirectly from scan number
                                        posS = tempTitle.IndexOf("Silind: ");
                                        if (posS > 0)
                                        {
                                            int first = posS + 8;
                                            int last = tempTitle.Length;
                                            string str2 = tempTitle.Substring(first, last - first);
                                            int index = 0;
                                            string secpepKey = strfilename + "_" + str2;

                                            if (secpepFeatures != null && secpepFeatures.TryGetValue(secpepKey, out index))
                                            {
                                                PepFeature featurePep;
                                                if (allFeatures != null && allFeatures.TryGetValue(index, out featurePep))
                                                {
                                                    // convert apex rettime to seconds
                                                    double retTimeinsecs = Convert.ToDouble(featurePep.RtApex) * 60.0;
                                                    strRetApex = Convert.ToString(retTimeinsecs);
                                                    strIntensity = featurePep.Intensity;
                                                    strFeaturePif = featurePep.Pif;
                                                    strSequence = featurePep.Sequence;
                                                    strNumScans = featurePep.NumScans;
                                                    strIsoPeaks = featurePep.IsoPeaks;
                                                    strDataPoints = featurePep.DataPoints;
                                                    strFeatureNum = featurePep.FeatureNum;
                                                    strBestScore = featurePep.BestScore;
                                                    strBestScan = featurePep.BestScan;
                                                    strMultiplicity = featurePep.ScanNumbers;
                                                }
                                            }
                                        }
                                        else
                                        {
                                            // precursor (iso) - no feature# so use scan number to get feature#
                                            int featureNum = 0;
                                            if (scanFeatures != null && scanFeatures.TryGetValue(key, out featureNum))
                                            {
                                                PepFeature featurePep;
                                                if (allFeatures != null && allFeatures.TryGetValue(featureNum, out featurePep))
                                                {
                                                    // convert apex rettime to seconds
                                                    double retTimeinsecs = 0;

                                                    if (!bPasef)
                                                        retTimeinsecs = Convert.ToDouble(featurePep.RtApex) * 60.0;
                                                    else
                                                        retTimeinsecs = Convert.ToDouble(featurePep.RtApex);

                                                    strRetApex = Convert.ToString(retTimeinsecs);
                                                    strIntensity = featurePep.Intensity;
                                                    strFeaturePif = featurePep.Pif;
                                                    strSequence = featurePep.Sequence;
                                                    strNumScans = featurePep.NumScans;
                                                    strIsoPeaks = featurePep.IsoPeaks;
                                                    strDataPoints = featurePep.DataPoints;
                                                    strFeatureNum = featurePep.FeatureNum;
                                                    strBestScore = featurePep.BestScore;
                                                    strBestScan = featurePep.BestScan;
                                                    strMultiplicity = featurePep.ScanNumbers;
                                                }
                                            }
                                            else
                                            {
                                                if (scanFeatures != null && scanFeatures.TryGetValue(key2, out featureNum))
                                                {
                                                    PepFeature featurePep;
                                                    if (allFeatures != null && allFeatures.TryGetValue(featureNum, out featurePep))
                                                    {
                                                        // convert apex rettime to seconds
                                                        double retTimeinsecs = 0;

                                                        if (!bPasef)
                                                            retTimeinsecs = Convert.ToDouble(featurePep.RtApex) * 60.0;
                                                        else
                                                            retTimeinsecs = Convert.ToDouble(featurePep.RtApex);

                                                        strRetApex = Convert.ToString(retTimeinsecs);
                                                        strIntensity = featurePep.Intensity;
                                                        strFeaturePif = featurePep.Pif;
                                                        strSequence = featurePep.Sequence;
                                                        strNumScans = featurePep.NumScans;
                                                        strIsoPeaks = featurePep.IsoPeaks;
                                                        strDataPoints = featurePep.DataPoints;
                                                        strFeatureNum = featurePep.FeatureNum;
                                                        strBestScore = featurePep.BestScore;
                                                        strBestScan = featurePep.BestScan;
                                                        strMultiplicity = featurePep.ScanNumbers;
                                                    }
                                                }
                                            }
                                        }

                                        // If sequence is NaN then try the evidence file
                                        if (strSequence == "NaN")
                                        {
                                            string zString = Regex.Match(chargeNum, @"\d+").Value;
                                            if (!String.IsNullOrEmpty(zString))
                                            {
                                                string Ekey = key + "_" + zString;
                                                string strTemp = "NaN";
                                                if (evidence != null && evidence.TryGetValue(Ekey, out strTemp))
                                                    strSequence = strTemp;
                                            }
                                        }

                                        // Add to title
                                        title += " FeatureIntensity: ";
                                        title += strIntensity;
                                        title += " Feature#: ";
                                        title += strFeatureNum;
                                        title += " RtApex: ";
                                        title += strRetApex;
                                        title += " FeaturePif: ";
                                        title += strFeaturePif;
                                        title += " MS2Pif: ";
                                        title += strMS2Pif;
                                        title += " Ndp: ";
                                        title += strDataPoints;
                                        title += " Ns: ";
                                        title += strNumScans;
                                        title += " Nip: ";
                                        title += strIsoPeaks;
                                        title += " Seq: ";
                                        title += strPepSeq;         // was strSequence
                                        title += " Score: ";
                                        title += strMS2Score;       // was strBestScore
                                        title += " #MS2: 1";
                                        //title += strMultiplicity;

                                        rettime = "RTINSECONDS=";
                                        if (!String.IsNullOrEmpty(strRettime) && strRettime != "0.0")
                                            rettime += strRettime;
                                        else
                                            rettime += strRetApex;

                                        scans = "SCANS=";
                                        if (!String.IsNullOrEmpty(strScanNum))
                                            scans += strScanNum;

                                        // Add to pepmass - user specified minimum intensity - set Ignore flag for whether peaklist is written out
                                        bIgnore = false;
                                        if (!String.IsNullOrEmpty(strIntensity) && strIntensity != "NaN")
                                        {
                                            pepmass += " ";
                                            pepmass += strIntensity;

                                            double lfInt = Convert.ToDouble(strIntensity);
                                            if (lfInt < 20000)          // maybe 20000 ?
                                                bIgnore = true;
                                        }
                                        else
                                        {
                                            pepmass += " ";
                                            pepmass += "0";
                                            bIgnore = true;
                                        }

                                        // check the number of isotopic peaks based on charge state
                                        if (!bIgnore)
                                        {
                                            if (!String.IsNullOrEmpty(strIsoPeaks) && strIsoPeaks != "NaN")
                                            {
                                                int nIsoPeaks = Convert.ToInt32(strIsoPeaks);

                                                if ((nCharge == 1 || nCharge >= 4) && nIsoPeaks < 3)
                                                    bIgnore = true;
                                            }
                                        }

                                        if (bPasef)
                                            bIgnore = false;
                                    }
                                }
                                else if (input.Contains("peaklist end"))
                                {
                                    // set position for end of peaklist
                                    currentEnd = currentPosition - aplStream.CurrentEncoding.GetByteCount(input);   // + newLineLen);

                                    // store original peaklist by rawfile_scan in dictionary for secpeps
                                    int posS = 0;
                                    int posE = 0;
                                    string strfilename = null;
                                    string strScanNum = null;
                                    posS = tempTitle.IndexOf("RawFile: ");
                                    posE = tempTitle.IndexOf(" Index:");
                                    if (posS >= 0 && posE > posS)
                                    {
                                        posS += 9;
                                        strfilename = tempTitle.Substring(posS, posE - posS);
                                    }

                                    posS = tempTitle.IndexOf("Index: ");
                                    posE = tempTitle.IndexOf(" Precursor:");
                                    if (posS > 0 && posE > posS)
                                    {
                                        posS += 7;
                                        strScanNum = tempTitle.Substring(posS, posE - posS);
                                    }

                                    string key = strfilename + "_" + strScanNum;

                                    ClonedPeaklist cPeaklist;
                                    cPeaklist.Filename = aplFile;
                                    cPeaklist.StartPos = currentStart;
                                    cPeaklist.EndPos = currentEnd;

                                    if (bOriginalPeaklist && !mgfOriginalPeaklist.ContainsKey(key))
                                        mgfOriginalPeaklist.Add(key, cPeaklist);

                                    // title, frag, pepmass, charge and then Rt (optional)
                                    if (bCombinedFolder && bAllPeptidesTxt && bEvidenceTxt && bMSMSPepTxt)
                                        mgfout = "BEGIN IONS" + Environment.NewLine + title + Environment.NewLine + fragmentType + Environment.NewLine + pepmass + Environment.NewLine + charge + Environment.NewLine + rettime + Environment.NewLine + scans + Environment.NewLine + peaklist + Environment.NewLine + "END IONS";
                                    else
                                        mgfout = "BEGIN IONS" + Environment.NewLine + title + Environment.NewLine + fragmentType + Environment.NewLine + pepmass + Environment.NewLine + charge + Environment.NewLine + scans + Environment.NewLine + peaklist + Environment.NewLine + "END IONS";

                                    // which file?
                                    if (bOriginalFileStructure)
                                        key = strfilename;
                                    else
                                        key = aplFileName;

                                    if (!bIgnore)
                                    {
                                        string mgfFile = aplPath + "\\" + key + ".mgf";
                                        if (!mgffileList.Contains(key))
                                        {
                                            using (StreamWriter outputFile = new StreamWriter(mgfFile, false))
                                                outputFile.WriteLine(mgfout);

                                            mgffileList.Add(key);
                                        }
                                        else
                                        {
                                            using (StreamWriter outputFile = new StreamWriter(mgfFile, true))
                                                outputFile.WriteLine(mgfout);
                                        }
                                    }

                                    // clear the peaklist for the next spectrum header and mz int pairs etc.
                                    peaklist = String.Empty;
                                }
                                else
                                {
                                    if (input.Length > 0)
                                    {
                                        // m/z int pair - if tab then replace with space
                                        mgfout = input.Replace('\t', ' ');

                                        if (String.IsNullOrEmpty(peaklist))
                                        {
                                            peaklist = mgfout;
                                            currentStart = currentPosition - aplStream.CurrentEncoding.GetByteCount(input);
                                        }
                                        else
                                        {
                                            peaklist += Environment.NewLine;
                                            peaklist += mgfout;
                                        }
                                    }
                                }

                                // clear the input string builder string
                                inputBuild.Clear();
                            }
                            else   // no cr or lf
                            {
                                // add to string
                                inputBuild.Append(nextChar);
                            }

                            currentPosition += aplStream.CurrentEncoding.GetByteCount(nextChar.ToString());
                        }
                    }
                }
            }

            // go through all the files - now do the secpep files
            for (int i = 0; i < files; i++)
            {
                Thread.Sleep(100);
                bgWorker.ReportProgress((int)((double)(i + 1) / (double)files * 100.0), "Processing secondary apl files...  ");

                // If cancel button was pressed while the execution is in progress
                // Change the state from cancellation ---> cancel'ed
                if (bgWorker.CancellationPending)
                {
                    e.Cancel = true;
                    bgWorker.ReportProgress(0, "");
                    return;
                }

                string aplFile = fileNames.Items[i].ToString();
                FileInfo aplInfo = new FileInfo(aplFile);
                string aplPath = aplInfo.DirectoryName;
                string aplFileName = System.IO.Path.GetFileNameWithoutExtension(aplInfo.Name);

                // if not a secpep file continue
                if (aplInfo.Exists && !aplInfo.Name.Contains(".secpep."))
                    continue;

                if (aplInfo.Exists && aplInfo.Extension == ".apl")
                {
                    using (StreamReader aplStream = new StreamReader(aplFile))
                    {
                        var inputBuild = new System.Text.StringBuilder();
                        string input = null;
                        int len = 0;
                        string mgfout = null;
                        string pepmass = null;
                        string charge = null;
                        string chargeNum = null;
                        int nCharge = 0;
                        string title = null;
                        string tempTitle = null;
                        string rettime = null;
                        string scans = null;
                        string peaklist = null;
                        int nIgnore = 0;

                        while (aplStream.Peek() > -1)
                        {
                            int nextCharNum = aplStream.Read();
                            char nextChar = (char)nextCharNum;

                            // if hit end of line
                            if (nextChar == '\r' || nextChar == '\n')
                            {
                                // convert stringbuilder to local string - check line endings etc ???
                                input = inputBuild.ToString();

                                if (input.Contains("peaklist start"))
                                    ++nIgnore;
                                else if (input.Contains("mz="))
                                {
                                    len = input.Length;
                                    pepmass = "PEPMASS=";
                                    pepmass += input.Substring(3, len - 3);
                                }
                                else if (input.Contains("fragmentation="))
                                    ++nIgnore;
                                else if (input.Contains("charge="))
                                {
                                    len = input.Length;
                                    charge = "CHARGE=" + input.Substring(7, len - 7) + "+";
                                    chargeNum = " Charge: " + input.Substring(7, len - 7);
                                    nCharge = Convert.ToInt32(input.Substring(7, len - 7));
                                }
                                else if (input.Contains("header="))
                                {
                                    len = input.Length;
                                    title = "TITLE=";
                                    tempTitle = input.Substring(7, len - 7);
                                    title += input.Substring(7, len - 7);
                                    title += chargeNum;

                                    if (bCombinedFolder && bAllPeptidesTxt && bEvidenceTxt && bMSMSPepTxt)
                                    {
                                        // retention time added after header line - extract raw file name plus scan# (parts1 and 3 from apl Title header line)
                                        // look for key in msmsScans list - use Value for Rt*60
                                        // allow for spaces in rawfile
                                        int posS = 0;
                                        int posE = 0;
                                        string strfilename = null;
                                        string strScanNum = null;
                                        posS = tempTitle.IndexOf("RawFile: ");
                                        posE = tempTitle.IndexOf(" Index:");
                                        if (posS >= 0 && posE > posS)
                                        {
                                            posS += 9;
                                            strfilename = tempTitle.Substring(posS, posE - posS);
                                        }

                                        posS = tempTitle.IndexOf("Index: ");
                                        posE = tempTitle.IndexOf(" Silind:");
                                        if (posS > 0 && posE > posS)
                                        {
                                            posS += 7;
                                            strScanNum = tempTitle.Substring(posS, posE - posS);
                                        }

                                        string key = strfilename + "_" + strScanNum;
                                        string key2 = strfilename + "_" + strScanNum + "_sec";

                                        MSMSPepID msmsID;
                                        MSMSFeature feature;
                                        string strRettime = "0.0";
                                        string strRetApex = "0.0";
                                        string strMS2Pif = "NaN";
                                        string strFeaturePif = "NaN";
                                        string strSequence = "NaN";
                                        string strPepSeq = "NaN";
                                        string strIntensity = "NaN";
                                        string strDataPoints = "NaN";
                                        string strNumScans = "NaN";
                                        string strIsoPeaks = "NaN";
                                        string strFeatureNum = "NaN";
                                        string strMS2Score = "NaN";
                                        string strPEP = "NaN";
                                        string strBestScore = "0";
                                        string strBestScan = "NaN";
                                        string strMultiplicity = "NaN";

                                        if (msmsScans != null && msmsScans.TryGetValue(key, out feature))
                                        {
                                            // convert rettime to seconds
                                            double retTimeinsecs = Convert.ToDouble(feature.Rt) * 60.0;
                                            strRettime = Convert.ToString(retTimeinsecs);
                                            strMS2Pif = feature.Pif;
                                            strMS2Score = feature.Score;
                                        }

                                        if (msmsPepID != null && msmsPepID.TryGetValue(key2, out msmsID))
                                        {
                                            strPepSeq = msmsID.PepSeq;
                                            strPEP = msmsID.PEP;
                                            strMS2Score = msmsID.Score;
                                        }

                                        // use allpeptides to get intensity of peptide feature
                                        // for secpep get feature# from Silind: xxx
                                        // for precursor (iso) get feature# indirectly from scan number
                                        posS = 0;
                                        posS = tempTitle.IndexOf("Silind: ");
                                        if (posS > 0)
                                        {
                                            int first = posS + 8;
                                            int last = tempTitle.Length;
                                            string str2 = tempTitle.Substring(first, last - first);
                                            int index = 0;  //Convert.ToInt32(str2);
                                            string secpepKey = strfilename + "_" + str2;

                                            if (secpepFeatures != null && secpepFeatures.TryGetValue(secpepKey, out index))
                                            {
                                                PepFeature featurePep;
                                                if (allFeatures != null && allFeatures.TryGetValue(index, out featurePep))
                                                {
                                                    // convert apex rettime to seconds
                                                    double retTimeinsecs = Convert.ToDouble(featurePep.RtApex) * 60.0;
                                                    strRetApex = Convert.ToString(retTimeinsecs);
                                                    strIntensity = featurePep.Intensity;
                                                    strFeaturePif = featurePep.Pif;
                                                    strSequence = featurePep.Sequence;
                                                    strNumScans = featurePep.NumScans;
                                                    strIsoPeaks = featurePep.IsoPeaks;
                                                    strDataPoints = featurePep.DataPoints;
                                                    strFeatureNum = featurePep.FeatureNum;
                                                    strBestScore = featurePep.BestScore;
                                                    strBestScan = featurePep.BestScan;
                                                    strMultiplicity = featurePep.ScanNumbers;
                                                }
                                            }
                                        }
                                        else
                                        {
                                            // precursor (iso) - no feature# so use scan number to get feature#
                                            int featureNum = 0;
                                            if (scanFeatures != null && scanFeatures.TryGetValue(key, out featureNum))
                                            {
                                                PepFeature featurePep;
                                                if (allFeatures != null && allFeatures.TryGetValue(featureNum, out featurePep))
                                                {
                                                    // convert apex rettime to seconds
                                                    double retTimeinsecs = Convert.ToDouble(featurePep.RtApex) * 60.0;
                                                    strRetApex = Convert.ToString(retTimeinsecs);
                                                    strIntensity = featurePep.Intensity;
                                                    strFeaturePif = featurePep.Pif;
                                                    strSequence = featurePep.Sequence;
                                                    strNumScans = featurePep.NumScans;
                                                    strIsoPeaks = featurePep.IsoPeaks;
                                                    strDataPoints = featurePep.DataPoints;
                                                    strFeatureNum = featurePep.FeatureNum;
                                                    strBestScore = featurePep.BestScore;
                                                    strBestScan = featurePep.BestScan;
                                                    strMultiplicity = featurePep.ScanNumbers;
                                                }
                                            }
                                        }

                                        // If sequence is NaN then try the evidence file
                                        if (strSequence == "NaN")
                                        {
                                            string zString = Regex.Match(chargeNum, @"\d+").Value;
                                            if (!String.IsNullOrEmpty(zString))
                                            {
                                                string Ekey = key + "_" + zString;
                                                string strTemp = "NaN";
                                                if (evidence != null && evidence.TryGetValue(Ekey, out strTemp))
                                                    strSequence = strTemp;
                                            }
                                        }

                                        // Add to title
                                        title += " FeatureIntensity: ";
                                        title += strIntensity;
                                        title += " Feature#: ";
                                        title += strFeatureNum;
                                        title += " RtApex: ";
                                        title += strRetApex;
                                        title += " FeaturePif: ";
                                        title += strFeaturePif;
                                        title += " MS2Pif: ";
                                        title += strMS2Pif;
                                        title += " Ndp: ";
                                        title += strDataPoints;
                                        title += " Ns: ";
                                        title += strNumScans;
                                        title += " Nip: ";
                                        title += strIsoPeaks;
                                        title += " Seq: ";
                                        title += strPepSeq;
                                        title += " Score: ";
                                        title += strMS2Score;
                                        title += " #MS2: 1";

                                        rettime = "RTINSECONDS=";
                                        if (!String.IsNullOrEmpty(strRettime) && strRettime != "0.0")
                                            rettime += strRettime;
                                        else
                                            rettime += strRetApex;

                                        scans = "SCANS=";
                                        if (!String.IsNullOrEmpty(strScanNum))
                                            scans += strScanNum;

                                        // Add to pepmass
                                        bIgnore = false;
                                        if (!bPasef)
                                        {
                                            if (!String.IsNullOrEmpty(strIntensity) && strIntensity != "NaN")
                                            {
                                                pepmass += " ";
                                                pepmass += strIntensity;

                                                double lfInt = Convert.ToDouble(strIntensity);
                                                if (lfInt < 20000)       // 20000 maybe ?
                                                    bIgnore = true;
                                            }
                                            else
                                            {
                                                pepmass += " ";
                                                pepmass += "0";
                                                bIgnore = true;
                                            }
                                        }

                                        // check the number of isotopic peaks based on charge state
                                        if (!bIgnore && !bPasef)
                                        {
                                            if (!String.IsNullOrEmpty(strIsoPeaks) && strIsoPeaks != "NaN")
                                            {
                                                int nIsoPeaks = Convert.ToInt32(strIsoPeaks);

                                                if ((nCharge == 1 || nCharge >= 4) && nIsoPeaks < 3)
                                                    bIgnore = true;
                                            }
                                        }
                                    }
                                }
                                else if (input.Contains("peaklist end"))
                                {
                                    // get original peaklist by rawfile_scan in dictionary
                                    int posS = 0;
                                    int posE = 0;
                                    string strfilename = null;
                                    string strScanNum = null;
                                    posS = tempTitle.IndexOf("RawFile: ");
                                    posE = tempTitle.IndexOf(" Index:");
                                    if (posS >= 0 && posE > posS)
                                    {
                                        posS += 9;
                                        strfilename = tempTitle.Substring(posS, posE - posS);
                                    }

                                    posS = tempTitle.IndexOf("Index: ");
                                    posE = tempTitle.IndexOf(" Silind:");
                                    if (posS > 0 && posE > posS)
                                    {
                                        posS += 7;
                                        strScanNum = tempTitle.Substring(posS, posE - posS);
                                    }

                                    string key = strfilename + "_" + strScanNum;
                                    ClonedPeaklist cPeaklist;

                                    if (bOriginalPeaklist && mgfOriginalPeaklist.TryGetValue(key, out cPeaklist))
                                    {
                                        // Open original apl file and seek to peaklist within file
                                        string origPeaklist = null;
                                        using (FileStream fs = new FileStream(cPeaklist.Filename, FileMode.Open, FileAccess.Read))
                                        {
                                            int bytes = (int)(cPeaklist.EndPos - cPeaklist.StartPos);
                                            var data = new byte[bytes];

                                            if(fs.CanSeek)
                                            {
                                                fs.Seek(cPeaklist.StartPos, SeekOrigin.Begin);
                                                fs.Read(data,0,bytes);
                                                string origPeaklist1 = Encoding.UTF8.GetString(data);
                                                origPeaklist = origPeaklist1.Replace('\t', ' ');
                                                origPeaklist = origPeaklist.TrimEnd('\r', '\n');
                                            }
                                        }

                                        if (!String.IsNullOrEmpty(origPeaklist))
                                        {
                                            // tidy up peaklist
                                            peaklist = origPeaklist;
                                        }
                                    }

                                    // title, frag, pepmass, charge and then Rt (optional)
                                    if (bCombinedFolder && bAllPeptidesTxt && bEvidenceTxt)
                                        mgfout = "BEGIN IONS" + Environment.NewLine + title + Environment.NewLine + fragmentType + Environment.NewLine + pepmass + Environment.NewLine + charge + Environment.NewLine + rettime + Environment.NewLine + scans + Environment.NewLine + peaklist + Environment.NewLine + "END IONS";
                                    else
                                        mgfout = "BEGIN IONS" + Environment.NewLine + title + Environment.NewLine + fragmentType + Environment.NewLine + pepmass + Environment.NewLine + charge + Environment.NewLine + scans + Environment.NewLine + peaklist + Environment.NewLine + "END IONS";

                                    // which file?
                                    if (bOriginalFileStructure)
                                        key = strfilename;
                                    else
                                        key = aplFileName;

                                    if (!bIgnore)
                                    {
                                        string mgfFile = aplPath + "\\" + key + ".mgf";
                                        if (!mgffileList.Contains(key))
                                        {
                                            using (StreamWriter outputFile = new StreamWriter(mgfFile, false))
                                                outputFile.WriteLine(mgfout);

                                            mgffileList.Add(key);
                                        }
                                        else
                                        {
                                            using (StreamWriter outputFile = new StreamWriter(mgfFile, true))
                                                outputFile.WriteLine(mgfout);
                                        }
                                    }

                                    // clear the peaklist for the next spectrum header and mz int pairs etc.
                                    peaklist = String.Empty;
                                }
                                else
                                {
                                    // skip blank lines
                                    if (input.Length > 0)
                                    {
                                        // m/z int pair - if tab then replace with space
                                        mgfout = input.Replace('\t', ' ');

                                        if (String.IsNullOrEmpty(peaklist))
                                            peaklist = mgfout;
                                        else
                                        {
                                            peaklist += Environment.NewLine;
                                            peaklist += mgfout;
                                        }
                                    }
                                }

                                // clear the input string builder string
                                inputBuild.Clear();
                            }
                            else   // no cr or lf
                            {
                                // add to string
                                inputBuild.Append(nextChar);
                            }
                        }
                    }
                }
            }

            //Report 100% completion on operation completed
            bgWorker.ReportProgress(100, "");
        }

        private void DragDrop(object sender, DragEventArgs e)
        {
            string[] s = (string[])e.Data.GetData(DataFormats.FileDrop, false);

            for (int i = 0; i < s.Length; i++)
            {
                if (s[i].Contains(".apl"))
                {
                    // no duplicates
                    if (!fileNames.Items.Contains(s[i]))
                        fileNames.Items.Add(s[i]);
                }
            }

            if (fileNames.Items.Count > 0)
                process.IsEnabled = true;
            else
                process.IsEnabled = false;

            if (fileNames.SelectedItems.Count > 0)
                deleteButton.IsEnabled = true;
            else
                deleteButton.IsEnabled = false;
        }

        private void OnProcess(object sender, EventArgs e)
        {
            process.IsEnabled = false;
            cancel.IsEnabled = true;

            //Start the async operation here
            bgWorker.RunWorkerAsync();
        }

        private void OnCancel(object sender, EventArgs e)
        {
            if (bgWorker.IsBusy)
            {
                //Stop/Cancel the async operation here
                bgWorker.CancelAsync();
            }
        }
        
        private void QuitClick(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void OnBrowse(object sender, RoutedEventArgs e)
        {
            System.Windows.Forms.FolderBrowserDialog fbd = new System.Windows.Forms.FolderBrowserDialog();
            fbd.ShowNewFolderButton = false;
            fbd.RootFolder = Environment.SpecialFolder.MyComputer;

            System.Windows.Forms.DialogResult result = fbd.ShowDialog();
            if (result == System.Windows.Forms.DialogResult.OK)
            {
                allPeptidesLocation.Text = fbd.SelectedPath;
                combinedFolder = fbd.SelectedPath;
            }
        }

        private void CheckedCloneOriginalPeaklist(object sender, RoutedEventArgs e)
        {
            bOriginalPeaklist = true;
        }

        private void UncheckedCloneOriginalPeaklist(object sender, RoutedEventArgs e)
        {
            bOriginalPeaklist = false;
        }

        private void CheckedOrigFileStructure(object sender, RoutedEventArgs e)
        {
            bOriginalFileStructure = true;
        }

        private void UncheckedOrigFileStructure(object sender, RoutedEventArgs e)
        {
            bOriginalFileStructure = false;
        }

        private void CheckedTimsDDA(object sender, RoutedEventArgs e)
        {
            bPasef = true;
        }

        private void UncheckedTimsDDA(object sender, RoutedEventArgs e)
        {
            bPasef = false;
        }

        private void AddAPLFiles(object sender, RoutedEventArgs e)
        {
            // Create OpenFileDialog 
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();

            // Set filter for file extension and default file extension 
            dlg.DefaultExt = ".apl";
            dlg.Filter = "APL Files (*.apl)|*.apl";
            dlg.Multiselect = true;

            // Display OpenFileDialog by calling ShowDialog method 
            Nullable<bool> result = dlg.ShowDialog();

            // Get the selected file name and display 
            if (result == true)
            {
                // Open document 
                string[] aplFilenames = dlg.FileNames;

                for (int i = 0; i < aplFilenames.Length; i++)
                {
                    // no duplicates
                    if (!fileNames.Items.Contains(aplFilenames[i]))
                        fileNames.Items.Add(aplFilenames[i]);
                }                   

                if (fileNames.Items.Count > 0)
                    process.IsEnabled = true;
                else
                    process.IsEnabled = false;

                if (fileNames.SelectedItems.Count > 0)
                    deleteButton.IsEnabled = true;
                else
                    deleteButton.IsEnabled = false;
            }
        }

        private void DeleteAPLFiles(object sender, RoutedEventArgs e)
        {
            while (fileNames.SelectedItems.Count > 0)
            {
                var index = fileNames.Items.IndexOf(fileNames.SelectedItem);
                fileNames.Items.RemoveAt(index);
            }

            if (fileNames.Items.Count > 0)
                process.IsEnabled = true;
            else
                process.IsEnabled = false;

            if (fileNames.SelectedItems.Count > 0)
                deleteButton.IsEnabled = true;
            else
                deleteButton.IsEnabled = false;
        }

        private void AplSelectChanged(object sender, SelectionChangedEventArgs e)
        {
            if(fileNames.SelectedItems.Count > 0)
                deleteButton.IsEnabled = true;
            else
                deleteButton.IsEnabled = false;
        }
    }
}
