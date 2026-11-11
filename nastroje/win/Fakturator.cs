// Fakturator.exe (Windows): aplikace bez okna, ktera spusti lokalni server Fakturatoru (node server.js) a otevre ho
// v prohlizeci. Ikona je v oznamovaci oblasti u hodin (Otevrit / Slozka s fakturami / Ukoncit). Obdoba Fakturator.app pro Mac.
//
//   slozka aplikace (instalator, %LOCALAPPDATA%\Programs\Fakturator):  Fakturator.exe, runtime\win\node.exe, program\ (vychozi kopie)
//   pracovni kopie programu:  %LOCALAPPDATA%\Fakturator\program   (sem se kopiruje vychozi kopie a tady se program sam aktualizuje)
//   data uzivatele:           Dokumenty\Fakturator                (data\, pdf-faktury\; serveru se preda v OF_DATA_ROOT)
//
// Prepinace:  --ukoncit           ukonci bezici Fakturator (pouziva instalator a odinstalace)
//             --prevest <slozka>  prevede data ze stare slozky OpenFaktura bez dialogu (navratovy kod 0 = prevedeno)
//             --nahled <png>      ulozi obrazek uvodniho okna (kontrola vzhledu)
// Promenne pro zkousky:  FAKTURATOR_PROG, FAKTURATOR_DATA (jine slozky), FAKTURATOR_INSTANCE (jine jmeno instance),
//                        FAKTURATOR_TICHO=1 (zadne dialogy, bubliny ani prohlizec), PORT (port serveru).
// Preklad (C# 5, .NET Framework 4, je v kazdych Windows):
//   csc /nologo /target:winexe /codepage:65001 /win32icon:Fakturator.ico /resource:Fakturator.ico,Fakturator.ico
//       /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:Fakturator.exe Fakturator.cs
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Fakturator")]
[assembly: AssemblyProduct("Fakturator")]
[assembly: AssemblyCompany("Lineart")]

static class Cesty
{
    public static readonly string Aplikace = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
    public static readonly string Koren = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fakturator");
    public static readonly string Program = Promenna("FAKTURATOR_PROG", Path.Combine(Koren, "program"));
    public static readonly string Data = Promenna("FAKTURATOR_DATA", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Fakturator"));
    public static readonly string Instance = "cz.lineart.fakturator" + Promenna("FAKTURATOR_INSTANCE", "");
    public static readonly bool Ticho = Promenna("FAKTURATOR_TICHO", "") == "1";
    public static string Vychozi { get { return Path.Combine(Aplikace, "program"); } }
    public static string Node { get { return Path.Combine(Aplikace, "runtime", "win", "node.exe"); } }
    public static string Zaznam { get { return Path.Combine(Path.GetDirectoryName(Program), "fakturator.log"); } }
    public static string Adresa { get { return Path.Combine(Path.GetDirectoryName(Program), "adresa.txt"); } }

    static string Promenna(string jmeno, string vychozi)
    {
        string v = Environment.GetEnvironmentVariable(jmeno);
        return string.IsNullOrEmpty(v) ? vychozi : v;
    }
}

static class Soubory
{
    public static string Verze(string slozka)
    {
        try
        {
            Match m = Regex.Match(File.ReadAllText(Path.Combine(slozka, "verze.json"), Encoding.UTF8), "\"verze\"\\s*:\\s*\"([0-9.]+)\"");
            return m.Success ? m.Groups[1].Value : "";
        }
        catch { return ""; }
    }

    public static int Porovnej(string a, string b)
    {
        string[] pa = (a ?? "").Split('.'), pb = (b ?? "").Split('.');
        for (int i = 0; i < 3; i++)
        {
            int x = 0, y = 0;
            if (i < pa.Length) int.TryParse(pa[i], out x);
            if (i < pb.Length) int.TryParse(pb[i], out y);
            if (x != y) return x - y;
        }
        return 0;
    }

    public static void Kopiruj(string odkud, string kam, bool prepsat)
    {
        Directory.CreateDirectory(kam);
        foreach (string f in Directory.GetFiles(odkud))
        {
            string cil = Path.Combine(kam, Path.GetFileName(f));
            if (prepsat || !File.Exists(cil)) File.Copy(f, cil, true);
        }
        foreach (string d in Directory.GetDirectories(odkud)) Kopiruj(d, Path.Combine(kam, Path.GetFileName(d)), prepsat);
    }

    // Vychozi kopie programu z instalace se do pracovni slozky kopiruje poprve a pak jen kdyz je novejsi (program se jinak aktualizuje sam).
    public static void PripravProgram()
    {
        string moje = Verze(Cesty.Program), nova = Verze(Cesty.Vychozi);
        if (nova == "") { if (moje == "") throw new Exception("V instalaci chybí program (" + Cesty.Vychozi + ")."); return; }
        if (moje == "" || Porovnej(nova, moje) > 0 || !File.Exists(Path.Combine(Cesty.Program, "server.js"))) Kopiruj(Cesty.Vychozi, Cesty.Program, true);
    }

    public static bool JeStaraSlozka(string slozka)
    {
        return !string.IsNullOrEmpty(slozka) && Directory.Exists(Path.Combine(slozka, "data"));
    }

    // Prevod = KOPIE slozek data a pdf-faktury ze stare slozky OpenFaktura; stara slozka zustava beze zmeny.
    public static void Preved(string stara)
    {
        if (!JeStaraSlozka(stara)) throw new Exception("Ve složce " + stara + " není složka data. Vyber složku OpenFaktura, ze které jsi program spouštěl(a).");
        Kopiruj(Path.Combine(stara, "data"), Path.Combine(Cesty.Data, "data"), false);
        string pdf = Path.Combine(stara, "pdf-faktury");
        if (Directory.Exists(pdf)) Kopiruj(pdf, Path.Combine(Cesty.Data, "pdf-faktury"), false);
    }

    public static string NajdiStarou()
    {
        string doma = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] kde = {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Path.Combine(doma, "Downloads"), doma, "C:\\" };
        foreach (string k in kde)
            foreach (string j in new string[] { "OpenFaktura", "Fakturator-windows\\OpenFaktura", "OpenFaktura\\OpenFaktura" })
                try { string c = Path.Combine(k, j); if (JeStaraSlozka(c) && File.Exists(Path.Combine(c, "server.js"))) return c; } catch { }
        return "";
    }

    public static void Zapis(string radek)
    {
        try { File.AppendAllText(Cesty.Zaznam, radek + Environment.NewLine, Encoding.UTF8); } catch { }
    }
}

// Uloha Windows (Job Object): kdyz Fakturator.exe skonci jakkoli (i nasilne), system ukonci i server, aby nezustal viset na portu.
static class Uloha
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr atributy, string jmeno);
    [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr uloha, int trida, IntPtr info, uint delka);
    [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr uloha, IntPtr proces);

    [StructLayout(LayoutKind.Sequential)]
    struct ZakladniLimity
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct Citace { public ulong a, b, c, d, e, f; }
    [StructLayout(LayoutKind.Sequential)]
    struct RozsireneLimity
    {
        public ZakladniLimity Zakladni;
        public Citace Io;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    static IntPtr uloha = IntPtr.Zero;

    public static void Pridej(Process p)
    {
        try
        {
            if (uloha == IntPtr.Zero)
            {
                uloha = CreateJobObject(IntPtr.Zero, null);
                RozsireneLimity l = new RozsireneLimity();
                l.Zakladni.LimitFlags = 0x2000;   // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                int delka = Marshal.SizeOf(typeof(RozsireneLimity));
                IntPtr pamet = Marshal.AllocHGlobal(delka);
                Marshal.StructureToPtr(l, pamet, false);
                SetInformationJobObject(uloha, 9, pamet, (uint)delka);   // JobObjectExtendedLimitInformation
                Marshal.FreeHGlobal(pamet);
            }
            AssignProcessToJobObject(uloha, p.Handle);
        }
        catch { }
    }
}

class Uvod : Form
{
    public bool Prevest;

    public Uvod(Icon ikona)
    {
        Text = "Fakturator"; Icon = ikona; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen; Font = new Font("Segoe UI", 10f); ClientSize = new Size(520, 318); ShowInTaskbar = true;
        Label nadpis = new Label { Text = "Vítej ve Fakturatoru", Font = new Font("Segoe UI", 15f, FontStyle.Bold), Location = new Point(24, 20), AutoSize = true };
        Label text = new Label
        {
            Location = new Point(24, 64), Size = new Size(472, 188),
            Text = "Faktury a nastavení se ukládají do složky Dokumenty → Fakturator, hotová PDF do její podsložky pdf-faktury.\r\n\r\n" +
                   "Fakturator běží na pozadí a otevírá se v prohlížeči. Jeho ikonu najdeš vpravo dole u hodin (někdy je schovaná pod šipkou ^), přes ni ho také ukončíš.\r\n\r\n" +
                   "Používal(a) jsi Fakturator už dřív ze složky OpenFaktura? Můžu z ní převést tvoje faktury a nastavení."
        };
        Button nacisto = new Button { Text = "Začít načisto", Size = new Size(150, 36), Location = new Point(188, 262) };
        Button prevest = new Button { Text = "Převést data", Size = new Size(150, 36), Location = new Point(346, 262) };
        nacisto.Click += delegate { Prevest = false; DialogResult = DialogResult.OK; };
        prevest.Click += delegate { Prevest = true; DialogResult = DialogResult.OK; };
        Controls.Add(nadpis); Controls.Add(text); Controls.Add(nacisto); Controls.Add(prevest);
        AcceptButton = Soubory.NajdiStarou() != "" ? prevest : nacisto;
    }
}

class Aplikace : ApplicationContext
{
    readonly NotifyIcon ikona = new NotifyIcon();
    readonly Control ui = new Control();
    readonly Icon obrazek;
    Process server;
    string adresa = "";
    bool koncim, otevreno;

    public Aplikace(Icon obr)
    {
        obrazek = obr;
        IntPtr h = ui.Handle;   // vytvori okno pro BeginInvoke z jinych vlaken
        ContextMenuStrip m = new ContextMenuStrip();
        ToolStripItem otevrit = m.Items.Add("Otevřít Fakturator", null, delegate { Otevri(); });
        otevrit.Font = new Font(otevrit.Font, FontStyle.Bold);
        m.Items.Add("Složka s fakturami", null, delegate { OtevriSlozku(); });
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("Ukončit Fakturator", null, delegate { Ukonci(); });
        ikona.Icon = new Icon(obr, SystemInformation.SmallIconSize); ikona.Text = "Fakturator"; ikona.ContextMenuStrip = m; ikona.Visible = true;
        ikona.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) Otevri(); };

        Thread t = new Thread(Posluchej); t.IsBackground = true; t.Start();
        try
        {
            PrvniSpusteni();
            Soubory.PripravProgram();
            Spust();
        }
        catch (Exception e) { Chyba("Fakturator se nepodařilo spustit", e.Message); KonecAplikace(); }
    }

    void PrvniSpusteni()
    {
        if (Directory.Exists(Path.Combine(Cesty.Data, "data"))) return;
        if (!Cesty.Ticho)
        {
            using (Uvod u = new Uvod(obrazek))
            {
                u.ShowDialog();
                if (u.Prevest)
                {
                    using (FolderBrowserDialog d = new FolderBrowserDialog())
                    {
                        d.Description = "Vyber starou složku OpenFaktura (jsou v ní složky data a pdf-faktury):";
                        d.ShowNewFolderButton = false; d.SelectedPath = Soubory.NajdiStarou();
                        if (d.ShowDialog() == DialogResult.OK)
                        {
                            try
                            {
                                Soubory.Preved(d.SelectedPath);
                                MessageBox.Show("Data jsou převedená do složky Dokumenty → Fakturator.\r\n\r\nStarou složku OpenFaktura už nepoužívej (můžeš si ji nechat jako zálohu), Fakturator teď spouštěj ikonou na ploše.",
                                    "Fakturator", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            }
                            catch (Exception e) { Chyba("Data se nepodařilo převést", e.Message + "\r\n\r\nFakturator se spustí bez nich, převod můžeš zopakovat po smazání složky Dokumenty → Fakturator."); }
                        }
                    }
                }
            }
        }
        Directory.CreateDirectory(Path.Combine(Cesty.Data, "data"));
    }

    void Spust()
    {
        if (!File.Exists(Cesty.Node)) throw new Exception("Chybí " + Cesty.Node + ". Nainstaluj Fakturator znovu.");
        try { if (File.Exists(Cesty.Zaznam) && new FileInfo(Cesty.Zaznam).Length > 1000000) File.Delete(Cesty.Zaznam); } catch { }
        try { File.Delete(Cesty.Adresa); } catch { }
        ProcessStartInfo i = new ProcessStartInfo(Cesty.Node, "\"" + Path.Combine(Cesty.Program, "server.js") + "\"");
        i.WorkingDirectory = Cesty.Program; i.UseShellExecute = false; i.CreateNoWindow = true;
        i.RedirectStandardOutput = true; i.RedirectStandardError = true;
        i.StandardOutputEncoding = Encoding.UTF8; i.StandardErrorEncoding = Encoding.UTF8;
        i.EnvironmentVariables["OF_DATA_ROOT"] = Cesty.Data;
        i.EnvironmentVariables["OF_NO_OPEN"] = "1";   // prohlizec otevira tato aplikace, az zna adresu
        Process p = new Process(); p.StartInfo = i; p.EnableRaisingEvents = true;
        p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { Radek(e.Data); };
        p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { Radek(e.Data); };
        p.Exited += delegate { try { ui.BeginInvoke((MethodInvoker)delegate { Skoncil(p); }); } catch { } };
        Soubory.Zapis("--- " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " start (aplikace " + Soubory.Verze(Cesty.Vychozi) + ", program " + Soubory.Verze(Cesty.Program) + ")");
        p.Start();
        Uloha.Pridej(p);
        p.BeginOutputReadLine(); p.BeginErrorReadLine();
        server = p;
    }

    void Radek(string r)
    {
        if (r == null) return;
        Soubory.Zapis(r);
        Match m = Regex.Match(r, "http://localhost:\\d+/");
        if (!m.Success) return;
        try { ui.BeginInvoke((MethodInvoker)delegate { Bezi(m.Value); }); } catch { }
    }

    void Bezi(string url)
    {
        adresa = url;
        try { File.WriteAllText(Cesty.Adresa, url, Encoding.ASCII); } catch { }
        if (Cesty.Ticho) return;
        Otevri();
        if (!otevreno)
        {
            otevreno = true;
            ikona.ShowBalloonTip(6000, "Fakturator běží", "Otevřel se v prohlížeči. Ikonu najdeš tady u hodin (někdy pod šipkou ^), přes ni ho také ukončíš.", ToolTipIcon.Info);
        }
    }

    void Skoncil(Process p)
    {
        if (koncim || p != server) return;
        int kod = -1;
        try { kod = p.ExitCode; } catch { }
        Soubory.Zapis("--- server skončil s kódem " + kod);
        if (kod == 75)   // server se po aktualizaci chce spustit znovu
        {
            try { Spust(); return; } catch (Exception e) { Chyba("Fakturator se po aktualizaci nespustil", e.Message); }
        }
        else Chyba("Fakturator se neočekávaně ukončil", "Podrobnosti jsou v souboru\r\n" + Cesty.Zaznam);
        KonecAplikace();
    }

    void Otevri()
    {
        if (adresa == "" || Cesty.Ticho) return;
        try { Process.Start(adresa); } catch (Exception e) { Chyba("Prohlížeč se nepodařilo otevřít", "Otevři ručně adresu " + adresa + "\r\n" + e.Message); }
    }

    void OtevriSlozku()
    {
        try { string pdf = Path.Combine(Cesty.Data, "pdf-faktury"); Directory.CreateDirectory(pdf); Process.Start(pdf); } catch { }
    }

    void Chyba(string nadpis, string text)
    {
        Soubory.Zapis("CHYBA: " + nadpis + ": " + text);
        if (!Cesty.Ticho) MessageBox.Show(text, nadpis, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    // signaly od dalsich spusteni: "otevrit" (druhe spusteni ikonou) a "ukoncit" (instalator, odinstalace)
    void Posluchej()
    {
        using (EventWaitHandle otevrit = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\" + Cesty.Instance + ".otevrit"))
        using (EventWaitHandle ukoncit = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\" + Cesty.Instance + ".ukoncit"))
        {
            while (true)
            {
                int k = WaitHandle.WaitAny(new WaitHandle[] { otevrit, ukoncit });
                try { if (k == 0) ui.BeginInvoke((MethodInvoker)Otevri); else { ui.BeginInvoke((MethodInvoker)Ukonci); return; } } catch { return; }
            }
        }
    }

    void Ukonci()
    {
        koncim = true;
        try { if (server != null && !server.HasExited) { server.Kill(); server.WaitForExit(5000); } } catch { }
        try { File.Delete(Cesty.Adresa); } catch { }
        Soubory.Zapis("--- " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ukončeno");
        KonecAplikace();
    }

    void KonecAplikace()
    {
        koncim = true;
        ikona.Visible = false;
        try { if (server != null && !server.HasExited) server.Kill(); } catch { }
        ExitThread();
        Environment.Exit(0);
    }
}

static class Start
{
    [STAThread]
    static int Main(string[] arg)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        if (arg.Length >= 2 && arg[0] == "--prevest")
        {
            try { Soubory.Preved(arg[1]); return 0; }
            catch (Exception e) { Soubory.Zapis("CHYBA prevodu: " + e.Message); return 1; }
        }

        if (arg.Length >= 2 && arg[0] == "--nahled")   // obrazek uvodniho okna do souboru (kontrola vzhledu pri sestaveni)
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Fakturator.ico"))
            using (Uvod u = new Uvod(new Icon(s)))
            {
                u.StartPosition = FormStartPosition.Manual; u.Location = new Point(-3000, -3000); u.ShowInTaskbar = false;
                u.Show(); Application.DoEvents();
                using (Bitmap b = new Bitmap(u.Width, u.Height)) { u.DrawToBitmap(b, new Rectangle(0, 0, u.Width, u.Height)); b.Save(arg[1], System.Drawing.Imaging.ImageFormat.Png); }
            }
            return 0;
        }

        bool prvni;
        using (Mutex zamek = new Mutex(true, "Local\\" + Cesty.Instance, out prvni))
        {
            if (arg.Length >= 1 && arg[0] == "--ukoncit")
            {
                if (prvni) return 0;   // nic nebezi
                Signal("ukoncit");
                for (int i = 0; i < 100; i++)   // pockat, az bezici aplikace skonci (nejdele 10 s)
                {
                    try { if (zamek.WaitOne(100)) { zamek.ReleaseMutex(); return 0; } } catch (AbandonedMutexException) { return 0; }
                }
                return 1;
            }
            if (!prvni) { Signal("otevrit"); return 0; }   // uz bezi: jen otevrit v prohlizeci

            Directory.CreateDirectory(Path.GetDirectoryName(Cesty.Program));
            Icon obrazek;
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Fakturator.ico")) obrazek = new Icon(s);
            Application.Run(new Aplikace(obrazek));
            zamek.ReleaseMutex();
        }
        return 0;
    }

    static void Signal(string jmeno)
    {
        try { using (EventWaitHandle e = EventWaitHandle.OpenExisting("Local\\" + Cesty.Instance + "." + jmeno)) e.Set(); } catch { }
    }
}
