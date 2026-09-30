using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

namespace XeroxPrintManager;

public class PrintJob
{
    public int id { get; set; }
    public string token { get; set; } = "";
    public string original_name { get; set; } = "";
    public string stored_name { get; set; } = "";
    public string file_type { get; set; } = "";
    public long file_size { get; set; }
    public int copies { get; set; }
    public string color_mode { get; set; } = "";
    public string paper_size { get; set; } = "";
    public string status { get; set; } = "";
    public string created_at { get; set; } = "";
}

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

public class MainForm : Form
{
    private readonly HttpClient http = new();

    private readonly ListBox jobList = new();
    private readonly WebView2 pdfViewer = new();

    private readonly ComboBox printerCombo = new();
    private readonly NumericUpDown copiesBox = new();

    private readonly RadioButton bwRadio = new();
    private readonly RadioButton colorRadio = new();

    private readonly ComboBox paperCombo = new();
    private readonly ComboBox duplexCombo = new();

    private readonly TextBox pageRangeBox = new();

    private readonly Label connectionLabel = new();
    private readonly Label selectedJobLabel = new();
    private readonly Label printStatusLabel = new();

    private readonly Button printButton = new();
    private readonly Button rejectButton = new();
    private readonly Button refreshButton = new();

    private readonly System.Windows.Forms.Timer refreshTimer = new();

    private List<PrintJob> jobs = new();

    private PrintJob? selectedJob;

    private const string Api = "http://localhost:3000";

    // private const string StoragePath =  @"C:\Users\raika\Documents\GitHub\PrintXerox\storage\jobs";

    // private const string SumatraPath =  @"C:\Users\raika\Documents\XeroxPrintManager\SumatraPDF.exe";


    private static readonly string AppDirectory = AppContext.BaseDirectory;

    private static readonly string StoragePath =
        Path.Combine(
            AppDirectory,
            "storage",
            "jobs"
        );

    private static readonly string SumatraPath =
        Path.Combine(
            AppDirectory,
            "SumatraPDF.exe"
        );
    
    public MainForm()
    {
        Text = "Xerox Print Manager";

        Width = 1400;
        Height = 850;

        MinimumSize = new Size(1100, 650);

        StartPosition = FormStartPosition.CenterScreen;

        BuildInterface();

        Shown += async (_, _) =>
        {
            await pdfViewer.EnsureCoreWebView2Async();

            await LoadJobs();

            refreshTimer.Start();
        };

        refreshTimer.Interval = 3000;

        refreshTimer.Tick += async (_, _) =>
        {
            await LoadJobs();
        };
    }


    private void BuildInterface()
    {
        BackColor = Color.FromArgb(241, 245, 249);


        // HEADER

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 70,
            BackColor = Color.FromArgb(15, 23, 42)
        };

        var title = new Label
        {
            Text = "Xerox Print Manager",
            ForeColor = Color.White,
            Font = new Font(
                "Segoe UI",
                20,
                FontStyle.Bold
            ),
            Location = new Point(20, 10),
            AutoSize = true
        };

        connectionLabel.Text = "Connecting...";

        connectionLabel.ForeColor = Color.Gold;

        connectionLabel.Font =
            new Font("Segoe UI", 10);

        connectionLabel.Location =
            new Point(22, 43);

        connectionLabel.AutoSize = true;

        header.Controls.Add(title);
        header.Controls.Add(connectionLabel);


        // MAIN SPLIT

        var main = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,

            SplitterDistance = 330,

            BackColor = Color.White
        };

        // LEFT - JOB LIST

        var leftPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10)
        };

        var queueTitle = new Label
        {
            Text = "PRINT QUEUE",
            Font = new Font(
                "Segoe UI",
                11,
                FontStyle.Bold
            ),
            Dock = DockStyle.Top,
            Height = 35
        };

        jobList.Dock = DockStyle.Fill;

        jobList.Font =
            new Font("Segoe UI", 10);

        jobList.BorderStyle =
            BorderStyle.FixedSingle;

        jobList.SelectedIndexChanged +=
            async (_, _) => await JobSelected();

        leftPanel.Controls.Add(jobList);
        leftPanel.Controls.Add(queueTitle);


        // RIGHT SIDE

        var right = new SplitContainer
        {
            Dock = DockStyle.Fill,

            Orientation = Orientation.Vertical,

            SplitterDistance = 600
        };


        // PDF PREVIEW

        var previewPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10)
        };

        var previewTitle = new Label
        {
            Text = "DOCUMENT PREVIEW",

            Font = new Font(
                "Segoe UI",
                11,
                FontStyle.Bold
            ),

            Dock = DockStyle.Top,

            Height = 35
        };

        pdfViewer.Dock = DockStyle.Fill;

        previewPanel.Controls.Add(
            pdfViewer
        );

        previewPanel.Controls.Add(
            previewTitle
        );


        // SETTINGS PANEL

        var settings = new Panel
        {
            Dock = DockStyle.Fill,

            Padding = new Padding(15),

            AutoScroll = true
        };

        var settingsTitle = new Label
        {
            Text = "PRINT SETTINGS",

            Font = new Font(
                "Segoe UI",
                13,
                FontStyle.Bold
            ),

            Location = new Point(15, 15),

            AutoSize = true
        };

        settings.Controls.Add(settingsTitle);


        // SELECTED JOB

        selectedJobLabel.Text =
            "No job selected";

        selectedJobLabel.Location =
            new Point(15, 55);

        selectedJobLabel.AutoSize = true;

        selectedJobLabel.MaximumSize =
            new Size(380, 50);

        settings.Controls.Add(
            selectedJobLabel
        );


        // PRINTER

        AddLabel(
            settings,
            "Printer",
            15,
            115
        );

        printerCombo.Location =
            new Point(15, 140);

        printerCombo.Width = 350;

        printerCombo.DropDownStyle =
            ComboBoxStyle.DropDownList;

        LoadPrinters();

        settings.Controls.Add(
            printerCombo
        );


        // COPIES

        AddLabel(
            settings,
            "Copies",
            15,
            185
        );

        copiesBox.Location =
            new Point(15, 210);

        copiesBox.Minimum = 1;
        copiesBox.Maximum = 100;
        copiesBox.Value = 1;

        copiesBox.Width = 100;

        settings.Controls.Add(
            copiesBox
        );


        // COLOUR

        AddLabel(
            settings,
            "Colour",
            15,
            255
        );

        bwRadio.Text = "B&W";

        bwRadio.Location =
            new Point(15, 280);

        bwRadio.AutoSize = true;

        bwRadio.Checked = true;

        colorRadio.Text = "Colour";

        colorRadio.Location =
            new Point(85, 280);

        colorRadio.AutoSize = true;

        settings.Controls.Add(
            bwRadio
        );

        settings.Controls.Add(
            colorRadio
        );


        // PAPER

        AddLabel(
            settings,
            "Paper size",
            15,
            325
        );

        paperCombo.Location =
            new Point(15, 350);

        paperCombo.Width = 150;

        paperCombo.DropDownStyle =
            ComboBoxStyle.DropDownList;

        paperCombo.Items.AddRange(
            new object[]
            {
                "A4",
                "A3",
                "Letter"
            }
        );

        paperCombo.SelectedIndex = 0;

        settings.Controls.Add(
            paperCombo
        );


        // DUPLEX

        AddLabel(
            settings,
            "Printing",
            15,
            395
        );

        duplexCombo.Location =
            new Point(15, 420);

        duplexCombo.Width = 180;

        duplexCombo.DropDownStyle =
            ComboBoxStyle.DropDownList;

        duplexCombo.Items.AddRange(
            new object[]
            {
                "One-sided",
                "Long-edge duplex",
                "Short-edge duplex"
            }
        );

        duplexCombo.SelectedIndex = 0;

        settings.Controls.Add(
            duplexCombo
        );


        // PAGE RANGE

        AddLabel(
            settings,
            "Pages",
            15,
            465
        );

        pageRangeBox.Location =
            new Point(15, 490);

        pageRangeBox.Width = 180;

        pageRangeBox.PlaceholderText =
            "All pages";

        settings.Controls.Add(
            pageRangeBox
        );


        // STATUS

        printStatusLabel.Text = "";

        printStatusLabel.Location =
            new Point(15, 535);

        printStatusLabel.MaximumSize =
            new Size(350, 50);

        printStatusLabel.AutoSize = true;

        settings.Controls.Add(
            printStatusLabel
        );


        // BUTTONS

        printButton.Text =
            "APPROVE & PRINT";

        printButton.Location =
            new Point(15, 600);

        printButton.Width = 350;

        printButton.Height = 55;

        printButton.Font =
            new Font(
                "Segoe UI",
                11,
                FontStyle.Bold
            );

        printButton.BackColor =
            Color.FromArgb(22, 163, 74);

        printButton.ForeColor =
            Color.White;

        printButton.FlatStyle =
            FlatStyle.Flat;

        printButton.Click += async (_, _) =>
        {
            await ApproveAndPrint();
        };

        settings.Controls.Add(
            printButton
        );


        rejectButton.Text =
            "REJECT JOB";

        rejectButton.Location =
            new Point(15, 665);

        rejectButton.Width = 350;

        rejectButton.Height = 45;

        rejectButton.Font =
            new Font(
                "Segoe UI",
                10,
                FontStyle.Bold
            );

        rejectButton.BackColor =
            Color.FromArgb(220, 38, 38);

        rejectButton.ForeColor =
            Color.White;

        rejectButton.FlatStyle =
            FlatStyle.Flat;

        rejectButton.Click += async (_, _) =>
        {
            await RejectSelectedJob();
        };

        settings.Controls.Add(
            rejectButton
        );


        refreshButton.Text =
            "Refresh Queue";

        refreshButton.Location =
            new Point(15, 720);

        refreshButton.Width = 350;

        refreshButton.Height = 40;

        refreshButton.Click += async (_, _) =>
        {
            await LoadJobs();
        };

        settings.Controls.Add(
            refreshButton
        );


        right.Panel1.Controls.Add(
            previewPanel
        );

        right.Panel2.Controls.Add(
            settings
        );

        main.Panel1.Controls.Add(
            leftPanel
        );

        main.Panel2.Controls.Add(
            right
        );

        Controls.Add(main);
        Controls.Add(header);
    }


    private void AddLabel(
        Control parent,
        string text,
        int x,
        int y)
    {
        var label = new Label
        {
            Text = text,

            Location =
                new Point(x, y),

            AutoSize = true,

            Font =
                new Font(
                    "Segoe UI",
                    9,
                    FontStyle.Bold
                )
        };

        parent.Controls.Add(label);
    }


    private void LoadPrinters()
    {
        printerCombo.Items.Clear();

        foreach (
            string printer
            in PrinterSettings.InstalledPrinters)
        {
            printerCombo.Items.Add(
                printer
            );
        }

        if (printerCombo.Items.Count > 0)
        {
            printerCombo.SelectedIndex = 0;
        }
    }


    private async Task LoadJobs()
    {
        try
        {
            var result =
                await http.GetFromJsonAsync<List<PrintJob>>(
                    Api + "/api/admin/jobs"
                );

            jobs =
                result ?? new List<PrintJob>();

            connectionLabel.Text =
                $"● PC Server Online   |   " +
                $"{jobs.Count(j => j.status == "PENDING")} pending";

            connectionLabel.ForeColor =
                Color.LightGreen;


            var currentToken =
                selectedJob?.token;

            jobList.Items.Clear();


            foreach (
                var job
                in jobs.Where(j =>
                    j.status == "PENDING"))
            {
                jobList.Items.Add(
                    $"{job.token}   |   " +
                    $"{job.original_name}   |   " +
                    $"{job.copies} copy"
                );
            }


            if (
                currentToken != null &&
                jobList.Items.Count > 0)
            {
                for (
                    int i = 0;
                    i < jobList.Items.Count;
                    i++)
                {
                    var job =
                        jobs
                        .Where(j =>
                            j.status == "PENDING")
                        .ElementAt(i);

                    if (
                        job.token ==
                        currentToken)
                    {
                        jobList.SelectedIndex =
                            i;

                        break;
                    }
                }
            }
        }
        catch
        {
            connectionLabel.Text =
                "● PC Server Offline";

            connectionLabel.ForeColor =
                Color.OrangeRed;
        }
    }


    private async Task JobSelected()
    {
        if (
            jobList.SelectedIndex < 0)
            return;


        var pendingJobs =
            jobs
            .Where(j =>
                j.status == "PENDING")
            .ToList();


        if (
            jobList.SelectedIndex >=
            pendingJobs.Count)
            return;


        selectedJob =
            pendingJobs[
                jobList.SelectedIndex
            ];


        selectedJobLabel.Text =
            $"{selectedJob.token}\n" +
            $"{selectedJob.original_name}";


        copiesBox.Value =
            Math.Clamp(
                selectedJob.copies,
                1,
                100
            );


        bwRadio.Checked =
            selectedJob.color_mode == "BW";

        colorRadio.Checked =
            selectedJob.color_mode == "COLOR";


        int paperIndex =
            paperCombo.Items
                .IndexOf(
                    selectedJob.paper_size
                );

        if (paperIndex >= 0)
        {
            paperCombo.SelectedIndex =
                paperIndex;
        }


        await PreviewSelectedDocument();
    }


    private async Task PreviewSelectedDocument()
    {
        if (selectedJob == null)
            return;


        string filePath =
            Path.Combine(
                StoragePath,
                selectedJob.stored_name
            );


        if (!File.Exists(filePath))
        {
            printStatusLabel.Text =
                "Document file not found.";

            return;
        }


        try
        {
            await pdfViewer
                .EnsureCoreWebView2Async();


            string url =
                new Uri(filePath)
                .AbsoluteUri;


            pdfViewer
                .CoreWebView2
                .Navigate(url);


            printStatusLabel.Text =
                "Document ready.";
        }
        catch (Exception ex)
        {
            printStatusLabel.Text =
                "Preview error: " +
                ex.Message;
        }
    }


    private async Task ApproveAndPrint()
    {
        if (selectedJob == null)
        {
            MessageBox.Show(
                "Select a pending job first."
            );

            return;
        }


        if (
            selectedJob.status !=
            "PENDING")
        {
            MessageBox.Show(
                "This job is no longer pending."
            );

            return;
        }


        if (
            printerCombo.SelectedItem == null)
        {
            MessageBox.Show(
                "Select a printer."
            );

            return;
        }


        string printer =
            printerCombo
            .SelectedItem
            .ToString()!;


        string filePath =
            Path.Combine(
                StoragePath,
                selectedJob.stored_name
            );


        if (!File.Exists(filePath))
        {
            MessageBox.Show(
                "The document file does not exist."
            );

            return;
        }


        var confirmation =
            MessageBox.Show(
                $"Print {selectedJob.original_name}?\n\n" +
                $"Printer: {printer}\n" +
                $"Copies: {copiesBox.Value}\n" +
                $"Mode: {(colorRadio.Checked ? "Colour" : "B&W")}\n" +
                $"Paper: {paperCombo.Text}",
                "Confirm Print",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            );


        if (
            confirmation !=
            DialogResult.Yes)
            return;


        try
        {
            // Tell Node that printing is beginning.

            var response =
                await http.PostAsync(
                    $"{Api}/api/admin/jobs/" +
                    $"{selectedJob.id}/printing",
                    null
                );


            if (!response.IsSuccessStatusCode)
            {
                MessageBox.Show(
                    "Could not reserve this job.\n" +
                    "It may already be processed."
                );

                await LoadJobs();

                return;
            }


            printButton.Enabled = false;

            rejectButton.Enabled = false;


            printStatusLabel.Text =
                "Printing...";


            bool success =
                await PrintWithSumatra(
                    filePath,
                    printer
                );


            if (success)
            {
                await http.PostAsync(
                    $"{Api}/api/admin/jobs/" +
                    $"{selectedJob.id}/completed",
                    null
                );


                printStatusLabel.Text =
                    "✓ Print job completed.";


                MessageBox.Show(
                    "Print job sent successfully.",
                    "Printing Complete",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            }
            else
            {
                await http.PostAsync(
                    $"{Api}/api/admin/jobs/" +
                    $"{selectedJob.id}/failed",
                    null
                );


                printStatusLabel.Text =
                    "✗ Printing failed.";


                MessageBox.Show(
                    "The printer command failed.",
                    "Print Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }


            selectedJob = null;

            await LoadJobs();
        }
        catch (Exception ex)
        {
            printStatusLabel.Text =
                "Print error.";


            MessageBox.Show(
                ex.Message,
                "Print Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        }
        finally
        {
            printButton.Enabled = true;

            rejectButton.Enabled = true;
        }
    }


    private async Task<bool> PrintWithSumatra(
        string filePath,
        string printer)
    {
        if (!File.Exists(SumatraPath))
        {
            MessageBox.Show(
                "SumatraPDF.exe was not found:\n\n" +
                SumatraPath
            );

            return false;
        }


        string settings =
            $"{copiesBox.Value}x";


        if (colorRadio.Checked)
        {
            settings += ",color";
        }
        else
        {
            settings += ",monochrome";
        }


        string duplex =
            duplexCombo.SelectedIndex switch
            {
                1 => ",duplexlong",
                2 => ",duplexshort",
                _ => ""
            };


        settings += duplex;


        if (
            !string.IsNullOrWhiteSpace(
                pageRangeBox.Text))
        {
            settings +=
                "," +
                pageRangeBox.Text.Trim();
        }


        string arguments =
            $"-silent " +
            $"-print-to \"{printer}\" " +
            $"-print-settings \"{settings}\" " +
            $"\"{filePath}\"";


        var psi =
            new ProcessStartInfo
            {
                FileName = SumatraPath,

                Arguments = arguments,

                UseShellExecute = false,

                CreateNoWindow = true,

                RedirectStandardOutput = true,

                RedirectStandardError = true
            };


        using var process =
            new Process
            {
                StartInfo = psi
            };


        process.Start();


        string error =
            await process
            .StandardError
            .ReadToEndAsync();


        await process
            .WaitForExitAsync();


        if (process.ExitCode != 0)
        {
            MessageBox.Show(
                $"SumatraPDF failed.\n\n" +
                $"Exit code: {process.ExitCode}\n\n" +
                error,
                "Printing Error"
            );

            return false;
        }


        return true;
    }


    private async Task RejectSelectedJob()
    {
        if (selectedJob == null)
        {
            MessageBox.Show(
                "Select a pending job first."
            );

            return;
        }


        var confirm =
            MessageBox.Show(
                $"Reject {selectedJob.original_name}?",
                "Reject Job",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning
            );


        if (
            confirm !=
            DialogResult.Yes)
            return;


        var response =
            await http.PostAsync(
                $"{Api}/api/admin/jobs/" +
                $"{selectedJob.id}/reject",
                null
            );


        if (response.IsSuccessStatusCode)
        {
            selectedJob = null;

            printStatusLabel.Text =
                "Job rejected.";

            await LoadJobs();
        }
        else
        {
            MessageBox.Show(
                "Could not reject the job."
            );
        }
    }
}