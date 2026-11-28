using System;
using System.Formats.Asn1;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace OptimizerWpf.Services
{
    public record DriverUpdate(string UpdateId, string Title, string? DriverClass, string? Manufacturer, string? Model, string? Date)
    {
        public int Revision { get; init; }
        public string RowId => $"{UpdateId}|{Revision}";
    }
    public record DriverScanResult(bool PolicyBlocked, IReadOnlyList<DriverUpdate> Updates)
    {
        public bool ScanSucceeded { get; init; } = true;
    }
    public record DriverStoreEntry(string Driver, string OriginalFileName, string? Version, string? Date, string? Class, bool InUse = false, string? KeptInfo = null);
    public record PnpDeviceInfo(string DeviceName, string HardwareId, Version CurrentVersion, DateTime CurrentDate)
    {
        public string PnpClass { get; init; } = "";
        public string DeviceInstanceId { get; init; } = "";
    }

    public record CatalogCandidate(string DeviceName, string HardwareId, string UpdateId, string Title, Version Version, DateTime ReleaseDate, bool IsWhql)
    {
        public string ResultClass { get; init; } = "";
        public string DeviceInstanceId { get; init; } = "";
    }

    // Port των 4 πρόσθετων πηγών του Optimizer.ps1's Start-DriverScan (~11977-12289, βλ. HANDOFF.md
    // ενότητα 0/§Α.3) - AMD/NVIDIA ζωντανά επιβεβαιωμένα σε πραγματικό hardware, Dell/SDIO μόνο μέσω
    // τεκμηρίωσης+headless flags. Η σύγκριση "νεότερο" γίνεται ΠΑΝΤΑ με ΗΜΕΡΟΜΗΝΙΕΣ, ΠΟΤΕ version
    // strings (το AMD Adrenalin/NVIDIA marketing versioning δεν αντιστοιχεί στο εσωτερικό Windows
    // DriverVersion) - ίδια σχεδιαστική επιλογή με το ps1 original.
    public record AmdDriverInfo(string GpuName, string LatestVersion, string? LatestReleaseDate, string? InstalledVersion, string? InstalledDate, string DownloadUrl)
    {
        public bool IsNewer => DriverService.IsDateNewer(LatestReleaseDate, InstalledDate);
    }
    public record NvidiaDriverInfo(string GpuName, string LatestVersion, string? LatestReleaseDate, string? InstalledVersion, string? InstalledDate, string DownloadUrl)
    {
        public bool IsNewer => DriverService.IsDateNewer(LatestReleaseDate, InstalledDate);
    }
    public record DellUpdateInfo(string? Name, string? Version, string? Date, string? Urgency, string? Type, string? Category);
    public record DellScanResult(bool Installed, IReadOnlyList<DellUpdateInfo> Updates, string? Error);
    public record SdioScanResult(bool Installed, bool TimedOut, bool ReportFound, bool HasDriverPacks, string? ReportPath, string? Error);
    public record OemInfo(string OemName, int? LenovoPackageCount = null);
    public record VendorScanResult(IReadOnlyList<AmdDriverInfo> Amd, IReadOnlyList<NvidiaDriverInfo> Nvidia, DellScanResult? Dell, SdioScanResult? Sdio, OemInfo? Oem);

    // Port του Optimizer.ps1's driver-ενημερώσεων συστήματος (~11847-13411) + ενσωμάτωση της τεχνικής
    // αναζήτησης στο Microsoft Update Catalog ανά Hardware ID (χρήστης παρείχε reference script
    // Driver_Updater.ps1) ως ΔΕΥΤΕΡΗ, συμπληρωματική πηγή - βλ. HANDOFF.md §0.4ιε. AMD/NVIDIA/Dell
    // ΠΑΡΑΜΕΝΟΥΝ εκτός εμβέλειας: narrow, εύθραυστο web scraping συγκεκριμένων vendor ιστοσελίδων
    // (ανά GPU/OEM), δεν καλύπτουν τη γενική περίπτωση όπως το Catalog - ΣΗΜΕΙΩΣΗ ΕΙΛΙΚΡΙΝΕΙΑΣ.
    public static class DriverService
    {
        private static readonly HttpClient Http = BuildHttpClient();
        private static readonly HttpClient CatalogHttp = new(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = System.Threading.Timeout.InfiniteTimeSpan
        };

        private static async Task<string> ReadCatalogHtmlAsync(HttpRequestMessage request, CancellationToken ct)
        {
            const int limit = 2 * 1024 * 1024;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(20));
            using var response = await CatalogHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode(); // no redirects to arbitrary origins
            if (response.Content.Headers.ContentLength > limit || response.Content.Headers.ContentEncoding.Count != 0)
                throw new InvalidDataException("Unsupported Catalog HTML size or encoding.");
            var charset = response.Content.Headers.ContentType?.CharSet;
            var encoding = string.IsNullOrWhiteSpace(charset) ? Encoding.UTF8 : Encoding.GetEncoding(charset.Trim('"'));
            using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            return await CatalogPackage.ReadBoundedHtmlAsync(stream, limit, encoding, deadline.Token).ConfigureAwait(false);
        }

        // Τελευταίο SHA256 hash που υπολογίστηκε σε εγκατάσταση Catalog driver - διαθέσιμο στο UI
        // για εμφάνιση/log, ίδια λογική με το "κράτα ίχνος τι πραγματικά εγκαταστάθηκε".
        public static string? LastInstallLog { get; private set; }

        private static HttpClient BuildHttpClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            client.Timeout = TimeSpan.FromSeconds(20);
            return client;
        }

        // ===== Microsoft Update Catalog (ανά Hardware ID) =====

        internal static bool ValidDeviceIdentity(string? value) =>
            !string.IsNullOrWhiteSpace(value) && value.Length <= 512 &&
            !value.Any(ch => char.IsControl(ch) || ch is '"' or ',' or ';');

        internal static bool KnownDriverVersion(Version? value) =>
            value != null && value > new Version(0, 0, 0, 0);

        private static ManagementObjectSearcher DeviceQuery(string query) =>
            new(new ManagementScope(@"root\CIMV2"), new ObjectQuery(query),
                new System.Management.EnumerationOptions { ReturnImmediately = true, Rewindable = false, Timeout = TimeSpan.FromSeconds(15) });

        public static Task<IReadOnlyList<PnpDeviceInfo>> GetSystemDevicesAsync() => Task.Run(() =>
        {
            // Fixed WQL, two queries rather than N+1 dynamically interpolated queries.
            var installed = new Dictionary<string, (Version? Version, DateTime Date)>(StringComparer.OrdinalIgnoreCase);
            using (var searcher = DeviceQuery("SELECT DeviceID, DriverVersion, DriverDate FROM Win32_PnPSignedDriver"))
            using (var rows = searcher.Get())
            {
                int count = 0;
                foreach (ManagementObject row in rows)
                {
                    using (row)
                    {
                        if (++count > 4096) throw new InvalidDataException("Driver inventory exceeds limit.");
                        if (row["DeviceID"] is not string id || !ValidDeviceIdentity(id)) continue;
                        Version.TryParse(row["DriverVersion"] as string, out var version);
                        DateTime date = DateTime.MinValue;
                        try { if (row["DriverDate"] is string ds) date = ManagementDateTimeConverter.ToDateTime(ds); }
                        catch (ArgumentException) { }
                        if (!installed.TryAdd(id, (version, date))) installed[id] = (null, DateTime.MinValue);
                    }
                }
            }

            var devices = new List<PnpDeviceInfo>();
            using var entities = DeviceQuery("SELECT Name, DeviceID, HardwareID, PNPClass FROM Win32_PnPEntity WHERE ConfigManagerErrorCode = 0");
            using var collection = entities.Get();
            int seen = 0;
            foreach (ManagementObject entity in collection)
            {
                using (entity)
                {
                    if (++seen > 4096) throw new InvalidDataException("Device inventory exceeds limit.");
                    var name = entity["Name"] as string;
                    var hwIds = entity["HardwareID"] as string[];
                    var deviceId = entity["DeviceID"] as string;
                    if (string.IsNullOrEmpty(name) || hwIds == null || hwIds.Length == 0 ||
                        !ValidDeviceIdentity(deviceId) || !ValidDeviceIdentity(hwIds[0])) continue;
                    installed.TryGetValue(deviceId!, out var driver);
                    devices.Add(new PnpDeviceInfo(name, hwIds[0], driver.Version ?? new Version(0, 0, 0, 0), driver.Date)
                    { PnpClass = entity["PNPClass"] as string ?? "", DeviceInstanceId = deviceId! });
                }
            }
            return (IReadOnlyList<PnpDeviceInfo>)devices;
        });

        // ΔΙΟΡΘΩΣΗ (ρητό αίτημα χρήστη: "για τα ρίσκα βρες λύσεις και ενσωμάτωσε") - 3 άμυνες πάνω από
        // την απλή έκδοση:
        // 1. Throttled + jittered delay ανάμεσα σε requests (ρητή σχεδιαστική επιλογή, ΔΕΝ υπήρχε στο
        //    reference script) - το catalog.update.microsoft.com είναι τεκμηριωμένα ευαίσθητο σε
        //    rate-limiting για αυτοματοποιημένη πρόσβαση, ένα πλήρες σύστημα έχει 20-60 συσκευές.
        // 2. Retry-with-backoff (1 επανάληψη) σε μεμονωμένο αποτυχημένο request πριν το εγκαταλείψει.
        // 3. Ανίχνευση "ύποπτου μηδενικού αποτελέσματος": αν σαρωθούν ≥5 συσκευές και ΚΑΜΙΑ δεν
        //    επιστρέψει έστω ένα αποτέλεσμα (πριν καν το φιλτράρισμα stable/newer), είναι πολύ πιθανό
        //    να άλλαξε η μορφή της σελίδας (σπασμένο regex) ή να μπλοκαρίστηκε η πρόσβαση - ΔΕΝ πρέπει
        //    να αναφερθεί σιωπηλά ως "όλα ενημερωμένα" (ψευδώς καθησυχαστικό), αναφέρεται ρητά.
        // ΝΕΟ - βελτίωση απόδοσης: τοπική cache ανά Hardware ID στο δίσκο (24ωρη διάρκεια ζωής) -
        // η σάρωση αυτή είναι σκόπιμα αργή (throttled delay 350-600ms/συσκευή παραπάνω, για να μην
        // μπλοκαριστεί από το catalog.update.microsoft.com) και ένα πλήρες σύστημα έχει 20-60
        // συσκευές, άρα μπορεί να πάρει αρκετά λεπτά. Αν ο χρήστης ξανατρέξει τη σάρωση μέσα στο ίδιο
        // 24ωρο (π.χ. μετά από ένα Full Maintenance), δεν έχει νόημα να ξαναπεριμένει το ίδιο διάστημα
        // για ΤΟ ΙΔΙΟ αποτέλεσμα - το hardware δεν άλλαξε. ΣΗΜΕΙΩΣΗ ΕΙΛΙΚΡΙΝΕΙΑΣ: ένα νέο driver που
        // δημοσιεύτηκε ΜΕΣΑ σε αυτό το 24ωρο δεν θα φανεί μέχρι να λήξει η cache - αποδεκτός συμβιβασμός
        // δεδομένου του πόσο σπάνια αλλάζει το διαθέσιμο driver ενός συγκεκριμένου hardware ID.
        // Session-only bounded cache: legacy disk cache is deliberately not trusted or deleted.
        // No idle I/O, no cross-process writable metadata used to propose privileged changes.
        private const int MaxCatalogEntries = 128;
        private const int MaxCatalogRows = 32;
        private static readonly TimeSpan CatalogCacheTtl = TimeSpan.FromHours(24);
        private static readonly object CatalogCacheGate = new();
        private static readonly Dictionary<string, (DateTime CachedAtUtc, CatalogCandidate[] Rows)> CatalogCache =
            new(StringComparer.OrdinalIgnoreCase);

        internal static bool ValidCatalogRow(CatalogCandidate c) =>
            ValidDeviceIdentity(c.HardwareId) && Guid.TryParseExact(c.UpdateId, "D", out _) &&
            c.Title is { Length: > 0 and <= 1024 } && c.ResultClass is { Length: <= 256 } &&
            c.DeviceName is { Length: > 0 and <= 512 } && KnownDriverVersion(c.Version);

        private static List<CatalogCandidate>? ReadCatalogCache(PnpDeviceInfo device)
        {
            lock (CatalogCacheGate)
            {
                if (!CatalogCache.TryGetValue(device.HardwareId, out var entry)) return null;
                var age = DateTime.UtcNow - entry.CachedAtUtc;
                if (age < TimeSpan.Zero || age >= CatalogCacheTtl)
                { CatalogCache.Remove(device.HardwareId); return null; }
                return entry.Rows.Select(c => c with { DeviceName = device.DeviceName, DeviceInstanceId = device.DeviceInstanceId }).ToList();
            }
        }

        private static void CacheCatalogRows(PnpDeviceInfo device, List<CatalogCandidate> candidates)
        {
            // Empty or malformed results are not authoritative. Do not truncate
            // a list and then pretend the truncated search is complete.
            if (candidates.Count == 0 || candidates.Count > MaxCatalogRows || candidates.Any(c => !ValidCatalogRow(c))) return;
            lock (CatalogCacheGate)
            {
                if (CatalogCache.Count >= MaxCatalogEntries && !CatalogCache.ContainsKey(device.HardwareId))
                    CatalogCache.Remove(CatalogCache.OrderBy(x => x.Value.CachedAtUtc).First().Key);
                CatalogCache[device.HardwareId] = (DateTime.UtcNow, candidates.ToArray());
            }
        }

        public static async Task<(IReadOnlyList<CatalogCandidate> Candidates, bool LooksBlocked)> ScanCatalogAsync(
            IReadOnlyList<PnpDeviceInfo> devices, IProgress<string>? progress = null, CancellationToken ct = default)
        {
            var results = new List<CatalogCandidate>();
            var devicesWithAnyResult = 0;
            var devicesScanned = 0;
            var rnd = new Random();
            var requestFailed = false;

            foreach (var device in devices)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(device.DeviceName);
                devicesScanned++;

                if (!KnownDriverVersion(device.CurrentVersion) || !ValidDeviceIdentity(device.HardwareId) ||
                    !ValidDeviceIdentity(device.DeviceInstanceId)) { requestFailed = true; continue; }

                var cachedCandidates = ReadCatalogCache(device);
                if (cachedCandidates != null)
                {
                    if (cachedCandidates.Count > 0) devicesWithAnyResult++;
                    var bestCached = PickBest(device, cachedCandidates);
                    if (bestCached != null) results.Add(bestCached);
                    continue;
                }

                try
                {
                    var candidates = await SearchCatalogForDeviceWithRetryAsync(device, ct);
                    if (candidates.Count > 0) devicesWithAnyResult++;
                    CacheCatalogRows(device, candidates);
                    var best = PickBest(device, candidates);
                    if (best != null) results.Add(best);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch { requestFailed = true; /* do not cache failed requests as empty successes */ }
                await Task.Delay(350 + rnd.Next(0, 250), ct);
            }

            var looksBlocked = CatalogScanIncomplete(requestFailed, devicesScanned, devicesWithAnyResult);
            return (results, looksBlocked);
        }

        // The legacy LooksBlocked flag now also covers incomplete coverage.
        internal static bool CatalogScanIncomplete(bool requestFailed, int scanned, int withResults) =>
            requestFailed || scanned == 0 || (scanned >= 5 && withResults == 0);

        private static async Task<List<CatalogCandidate>> SearchCatalogForDeviceWithRetryAsync(PnpDeviceInfo device, CancellationToken ct)
        {
            try { return await SearchCatalogForDeviceAsync(device, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch
            {
                await Task.Delay(1500, ct);
                return await SearchCatalogForDeviceAsync(device, ct);
            }
        }

        private static async Task<List<CatalogCandidate>> SearchCatalogForDeviceAsync(PnpDeviceInfo device, CancellationToken ct)
        {
            var url = $"https://www.catalog.update.microsoft.com/Search.aspx?q={Uri.EscapeDataString(device.HardwareId)}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            var html = await ReadCatalogHtmlAsync(request, ct);

            // Ίδιο regex με το reference script - parsing του πίνακα αποτελεσμάτων του Catalog.
            const string pattern = "(?s)<tr id=\"(?<UpdateId>[a-f0-9\\-]+)_row\".*?class=\"tableRow\">.*?<a.*?>(?<Title>.*?)</a>.*?<td>(?<Class>.*?)</td>.*?<td>(?<Version>.*?)</td>.*?<td>(?<Date>.*?)</td>";
            var list = new List<CatalogCandidate>();
            foreach (Match m in Regex.Matches(html, pattern, RegexOptions.None, TimeSpan.FromSeconds(2)))
            {
                var title = m.Groups["Title"].Value.Trim();
                if (title.Contains("Beta", StringComparison.OrdinalIgnoreCase) || title.Contains("Preview", StringComparison.OrdinalIgnoreCase)) continue;
                if (!Version.TryParse(m.Groups["Version"].Value.Trim(), out var version)) continue;
                if (!DateTime.TryParse(m.Groups["Date"].Value.Trim(), out var date)) continue;

                // HTML text alone does not establish WHQL certification.
                var candidate = new CatalogCandidate(device.DeviceName, device.HardwareId, m.Groups["UpdateId"].Value, title, version, date, IsWhql: false)
                { ResultClass = m.Groups["Class"].Value.Trim(), DeviceInstanceId = device.DeviceInstanceId };
                if (ValidCatalogRow(candidate)) list.Add(candidate);
                if (list.Count > 256) throw new InvalidDataException("Catalog result limit exceeded.");
            }
            return list;
        }

        // ΔΙΟΡΘΩΣΗ (ρίσκο #3 - "η αναζήτηση Catalog είναι ελεύθερο κείμενο, όχι αυστηρό Hardware ID
        // φιλτράρισμα, μπορεί να επιστρέψει άσχετα αποτελέσματα"): πρόσθετος έλεγχος λογικότητας -
        // αν η κλάση συσκευής (π.χ. "Net"/"Display"/"HDC") είναι γνωστή ΚΑΙ το αποτέλεσμα του Catalog
        // έχει ΔΙΑΦΟΡΕΤΙΚΗ, μη-κενή κλάση, απορρίπτεται ως πιθανό ψευδές ταίριασμα - όχι τέλεια
        // επικύρωση, αλλά αρκετά καλή για να πιάσει τα πιο προφανή άσχετα αποτελέσματα.
        internal static CatalogCandidate? PickBest(PnpDeviceInfo device, List<CatalogCandidate> candidates)
        {
            CatalogCandidate? best = null;
            var bestScore = -1;
            foreach (var c in candidates)
            {
                if (!KnownDriverVersion(device.CurrentVersion) || !ValidCatalogRow(c) ||
                    !string.Equals(c.HardwareId, device.HardwareId, StringComparison.OrdinalIgnoreCase) || c.Version <= device.CurrentVersion) continue;
                if (!string.IsNullOrEmpty(device.PnpClass) && !string.IsNullOrEmpty(c.ResultClass) &&
                    !c.ResultClass.Contains(device.PnpClass, StringComparison.OrdinalIgnoreCase) &&
                    !device.PnpClass.Contains(c.ResultClass, StringComparison.OrdinalIgnoreCase))
                    continue;

                var score = (c.IsWhql ? 40 : 0) + 30 + (c.ReleaseDate > device.CurrentDate ? 20 : 0);
                if (score > bestScore || (score == bestScore && best != null && c.Version > best.Version)) { bestScore = score; best = c; }
            }
            return best;
        }

        // ΔΙΟΡΘΩΣΗ σε σχέση με το reference script: το `DownloadDialog.aspx?updateIDs=<guid>` ως απλό
        // GET link ΔΕΝ επιστρέφει το πραγματικό αρχείο - επιστρέφει HTML σελίδα διαλόγου. Η πραγματική,
        // λειτουργική τεχνική (επιβεβαιώθηκε μέσω έρευνας σε καθιερωμένα open-source εργαλεία σαν το
        // MSCatalog) απαιτεί POST στο ίδιο endpoint με συγκεκριμένο JSON body, μετά εξαγωγή του
        // πραγματικού CDN link από την απάντηση.
        public static async Task<bool> InstallCatalogDriverAsync(CatalogCandidate candidate)
        {
            using var lease = await GearWinCore.EnterAsync(CoreWork.SoftwareChange);
            LastInstallLog = null;
            string? tempDir = null;
            try
            {
                if (!ValidCatalogRow(candidate) || !ValidDeviceIdentity(candidate.DeviceInstanceId))
                    throw new InvalidDataException("Rescan devices before installing this driver.");
                var downloadUrl = CatalogPackage.Normalize(await ResolveCatalogDownloadUrlAsync(candidate.UpdateId), fromCatalog: true);
                if (downloadUrl == null) { LastInstallLog = "Unsupported or ambiguous Catalog package (CAB required)."; return false; }
                tempDir = SecureStorage.NewPrivateTempDir("DriverCatalog");
                var filePath = Path.Combine(tempDir, "driver.cab");
                var root = Path.GetPathRoot(tempDir)!;
                if (new DriveInfo(root).AvailableFreeSpace < 3 * CatalogPackage.MaxBytes)
                    throw new IOException("Insufficient free space for bounded package staging.");
                var sha256 = await CatalogPackage.DownloadAsync(downloadUrl, filePath);
                ProtectedOperationJournal.SetTrustedOwner(filePath);
                if (!SecureStorage.IsProtectedFile(filePath)) throw new IOException("Unprotected driver package.");
                LastInstallLog = $"{candidate.Title} · SHA256: {sha256}";
                if (!await VerifyAuthenticodeAsync(filePath)) { LastInstallLog += " · Signature not verified"; return false; }

                using (var cabinet = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    CatalogPackage.ValidateCabinet(cabinet);
                var extractPath = Path.Combine(tempDir, "extracted");
                SecureStorage.SecureDirectory(extractPath);
                int expand = await RunCatalogCommandAsync("expand.exe", filePath, "-F:*", extractPath);
                if (expand != 0) { LastInstallLog += " · CAB extraction failed"; return false; }
                var inf = CatalogPackage.SelectInf(extractPath, candidate.HardwareId);
                if (inf == null) { LastInstallLog += " · No unique exact hardware-ID INF"; return false; }

                // Recheck the selected device is still present and the
                // candidate is newer. Do not rely solely on cached metadata.
                var devices = await GetSystemDevicesAsync();
                if (!CatalogTargetStillEligible(candidate, devices))
                {
                    LastInstallLog += " · Device/version no longer eligible";
                    return false;
                }
                int install = await RunCatalogCommandAsync("pnputil.exe", "/add-driver", inf, "/install");
                LastInstallLog += " · PnPUtil exit " + install +
                    (install == 3010 ? " (restart required)" : "");
                if (install is not (0 or 3010)) return false;
                if (install == 3010) WingetService.RestartRequiredAfterUpdates = true;
                // Acceptance is distinct from active version. A reboot or Windows
                // ranking may defer or prevent activation.
                try
                {
                    var after = await GetSystemDevicesAsync();
                    var active = after.Where(d => string.Equals(d.DeviceInstanceId, candidate.DeviceInstanceId,
                        StringComparison.OrdinalIgnoreCase)).ToArray();
                    LastInstallLog += active.Length == 1 && active[0].CurrentVersion == candidate.Version
                        ? " · Selected device reports requested version"
                        : " · Accepted; active version not verified";
                }
                catch { LastInstallLog += " · Accepted; active version readback unavailable"; }
                return true;
            }
            catch (Exception ex) { LastInstallLog = (LastInstallLog ?? "Catalog installation") + " · " + ex.Message; return false; }
            finally
            {
                if (tempDir != null) try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        internal static bool CatalogTargetStillEligible(CatalogCandidate candidate, IReadOnlyList<PnpDeviceInfo> devices)
        {
            if (!ValidCatalogRow(candidate) || !ValidDeviceIdentity(candidate.DeviceInstanceId)) return false;
            var matching = devices.Where(d => string.Equals(d.HardwareId, candidate.HardwareId,
                StringComparison.OrdinalIgnoreCase)).ToArray();
            // PnPUtil can affect several matching devices. Refuse ambiguous
            // instance identity or unknown/equal/newer versions in this set.
            return matching.Count(d => string.Equals(d.DeviceInstanceId, candidate.DeviceInstanceId,
                StringComparison.OrdinalIgnoreCase)) == 1 &&
                matching.All(d => ValidDeviceIdentity(d.DeviceInstanceId) &&
                    KnownDriverVersion(d.CurrentVersion) && candidate.Version > d.CurrentVersion);
        }

        private static async Task<int> RunCatalogCommandAsync(string executable, params string[] arguments)
        {
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, executable))
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var arg in arguments) start.ArgumentList.Add(arg);
            using var child = Process.Start(start) ?? throw new IOException("Cannot start " + executable);
            var stdout = HealthCleanupService.ReadBoundedOutputAsync(child.StandardOutput, 32768);
            var stderr = HealthCleanupService.ReadBoundedOutputAsync(child.StandardError, 4096);
            // Do not kill a running driver installation on an arbitrary deadline.
            await child.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr);
            return child.ExitCode;
        }

        private static async Task<string?> ResolveCatalogDownloadUrlAsync(string updateId)
        {
            // Το updateId μπαίνει σε σώμα JSON/form: δέχεται μόνο GUID (προστασία από injection, π.χ. από παραποιημένη cache στο δίσκο).
            if (!Guid.TryParseExact(updateId, "D", out _)) return null;
            var body = $"updateIDs=[{{\"size\":0,\"updateID\":\"{updateId}\",\"uidInfo\":\"{updateId}\"}}]";
            using var content = new StringContent(Uri.UnescapeDataString(body));
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-www-form-urlencoded");
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://www.catalog.update.microsoft.com/DownloadDialog.aspx") { Content = content };
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            var html = await ReadCatalogHtmlAsync(request, CancellationToken.None);
            var urls = Regex.Matches(html, @"(https?://(?:dl\.delivery\.mp\.microsoft\.com|(?:catalog\.s\.|www\.)?download\.windowsupdate\.com)/[^'""]*)", RegexOptions.None, TimeSpan.FromSeconds(2))
                .Select(m => CatalogPackage.Normalize(System.Net.WebUtility.HtmlDecode(m.Groups[1].Value), fromCatalog: true))
                .Where(u => u != null).Select(u => u!.AbsoluteUri).Distinct(StringComparer.Ordinal).ToArray();
            return urls.Length == 1 ? urls[0] : null;
        }

        // Require Valid Authenticode AND the actual organization attribute of
        // the signer certificate. Do not split a formatted DN on commas:
        // a quoted common name may itself contain a fake organization string.
        internal static bool IsTrustedMicrosoftSignature(string? output)
        {
            if (string.IsNullOrWhiteSpace(output) || output.Length > 16384) return false;
            var parts = output.Trim().Split('|', 2);
            if (parts.Length != 2 || parts[0] != "Valid") return false;
            try
            {
                var subject = new AsnReader(Convert.FromBase64String(parts[1]), AsnEncodingRules.DER);
                var sequence = subject.ReadSequence();
                int organizations = 0;
                bool matched = false;
                while (sequence.HasData)
                {
                    var rdn = sequence.ReadSetOf();
                    while (rdn.HasData)
                    {
                        var attribute = rdn.ReadSequence();
                        var oid = attribute.ReadObjectIdentifier();
                        if (oid == "2.5.4.10")
                        {
                            organizations++;
                            var tag = attribute.PeekTag();
                            if (tag.TagClass != TagClass.Universal ||
                                (tag.TagValue != (int)UniversalTagNumber.UTF8String &&
                                 tag.TagValue != (int)UniversalTagNumber.PrintableString &&
                                 tag.TagValue != (int)UniversalTagNumber.BMPString)) return false;
                            matched = string.Equals(attribute.ReadCharacterString((UniversalTagNumber)tag.TagValue),
                                "Microsoft Corporation", StringComparison.OrdinalIgnoreCase);
                        }
                        else attribute.ReadEncodedValue();
                        attribute.ThrowIfNotEmpty();
                    }
                }
                subject.ThrowIfNotEmpty();
                return organizations == 1 && matched;
            }
            catch (Exception ex) when (ex is FormatException or AsnContentException or ArgumentException)
            { return false; }
        }

        private static async Task<bool> VerifyAuthenticodeAsync(string filePath)
        {
            var script = $"$ErrorActionPreference='Stop'; $s=Get-AuthenticodeSignature -LiteralPath '{filePath.Replace("'", "''")}'; " +
                "if ($s.Status -ne 'Valid' -or $null -eq $s.SignerCertificate) { exit 1 }; " +
                "'Valid|' + [Convert]::ToBase64String($s.SignerCertificate.SubjectName.RawData)";
            var executable = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            var psi = new ProcessStartInfo(executable, PowerShellArgs.Command(script))
            {
                RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process == null) return false;
            var stdout = HealthCleanupService.ReadBoundedOutputAsync(process.StandardOutput, 16384);
            var stderr = HealthCleanupService.ReadBoundedOutputAsync(process.StandardError, 4096);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
                await Task.WhenAll(stdout, stderr).WaitAsync(timeout.Token);
                return process.ExitCode == 0 && string.IsNullOrWhiteSpace(await stderr) &&
                    IsTrustedMicrosoftSignature(await stdout);
            }
            catch (OperationCanceledException)
            {
                // This child only verifies a signature; no installation has started.
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
                return false;
            }
        }
        public static Task<DriverScanResult> ScanAsync() => ScanCoordinatedAsync(CorePriority.User);
        internal static Task<DriverScanResult> ScanCoordinatedAsync(CorePriority priority) =>
            GearWinCore.RunAsync(CoreWork.UpdateScan, ScanCoreAsync, priority);

        private static async Task<DriverScanResult> ScanCoreAsync()
        {
            const string script = @"
$ErrorActionPreference = 'Continue'
$policyBlocked = $false
foreach ($p in @(
    'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate',
    'HKLM:\SOFTWARE\Microsoft\PolicyManager\current\device\Update',
    'HKLM:\SOFTWARE\Microsoft\PolicyManager\default\Update',
    'HKLM:\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings'
)) {
    try {
        $v = (Get-ItemProperty -Path $p -Name 'ExcludeWUDriversInQualityUpdate' -ErrorAction SilentlyContinue).ExcludeWUDriversInQualityUpdate
        if ($v -eq 1) { $policyBlocked = $true }
    } catch {}
}

$updates = @()
$scanSucceeded = $false
if (-not $policyBlocked) {
    try {
        $session = New-Object -ComObject Microsoft.Update.Session
        $searcher = $session.CreateUpdateSearcher()
        $result = $searcher.Search(""IsInstalled=0 and IsHidden=0 and Type='Driver'"")
        foreach ($u in $result.Updates) {
            $updates += [PSCustomObject]@{
                UpdateId     = $u.Identity.UpdateID
                Revision     = $u.Identity.RevisionNumber
                Title        = $u.Title
                DriverClass  = $u.DriverClass
                Manufacturer = $u.DriverManufacturer
                Model        = $u.DriverModel
                Date         = if ($u.DriverVerDate) { $u.DriverVerDate.ToString('yyyy-MM-dd') } else { $null }
            }
        }
        $scanSucceeded = ($result.ResultCode -eq 2)
    } catch {}
}

[PSCustomObject]@{ PolicyBlocked = $policyBlocked; ScanSucceeded = $scanSucceeded; Updates = @($updates) } | ConvertTo-Json -Depth 4 -Compress
";
            var output = await RunPowerShellScriptAsync(script, TimeSpan.FromMinutes(3));
            return ParseDriverScan(output);
        }

        internal static DriverScanResult ParseDriverScan(string output)
        {
            static DriverScanResult Unknown() => new(false, Array.Empty<DriverUpdate>()) { ScanSucceeded = false };
            if (string.IsNullOrWhiteSpace(output)) return Unknown();
            try
            {
                using var document = JsonDocument.Parse(output);
                var root = document.RootElement;
                var policyBlocked = root.GetProperty("PolicyBlocked").GetBoolean();
                var succeeded = root.GetProperty("ScanSucceeded").GetBoolean();
                var updates = new List<DriverUpdate>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var u in root.GetProperty("Updates").EnumerateArray())
                {
                    var update = new DriverUpdate(
                        u.GetProperty("UpdateId").GetString() ?? "",
                        u.GetProperty("Title").GetString() ?? "",
                        u.TryGetProperty("DriverClass", out var dc) ? dc.GetString() : null,
                        u.TryGetProperty("Manufacturer", out var mf) ? mf.GetString() : null,
                        u.TryGetProperty("Model", out var md) ? md.GetString() : null,
                        u.TryGetProperty("Date", out var dt) ? dt.GetString() : null)
                    { Revision = u.GetProperty("Revision").GetInt32() };
                    if (!WindowsUpdateService.TryParseRowId(update.RowId, out _, out _) || string.IsNullOrWhiteSpace(update.Title))
                    { succeeded = false; continue; }
                    if (!seen.Add(update.RowId)) { succeeded = false; ambiguous.Add(update.RowId); continue; }
                    updates.Add(update);
                }
                updates.RemoveAll(update => ambiguous.Contains(update.RowId));
                return new DriverScanResult(policyBlocked, policyBlocked ? Array.Empty<DriverUpdate>() : updates)
                    { ScanSucceeded = succeeded && !policyBlocked };
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
            { return Unknown(); }
        }

        // "Νεότερο" πάντα με ημερομηνίες (yyyy-MM-dd) - ίδια λογική με το Complete-DriverScan του ps1
        // original (Test-DriverSourceOverlap/amdNewer/nvNewer). Αν λείπει η εγκατεστημένη ημερομηνία
        // αλλά υπάρχει η επίσημη release date, θεωρείται συντηρητικά "πιθανή ενημέρωση" (ίδιο με ps1).
        internal static bool IsDateNewer(string? latest, string? installed)
        {
            if (string.IsNullOrEmpty(latest)) return false;
            if (!DateTime.TryParse(latest, out var latestDate)) return false;
            if (string.IsNullOrEmpty(installed)) return true;
            return DateTime.TryParse(installed, out var installedDate) && latestDate > installedDate;
        }

        // Port του Test-DriverSourceOverlap (ίδιο πνεύμα best-effort string overlap, ΟΧΙ Hardware ID -
        // όταν 2 ανεξάρτητες πηγές συμφωνούν για το ΙΔΙΟ φυσικό GPU, η "πιθανή" ενημέρωση γίνεται
        // "επιβεβαιωμένη" υψηλότερης εμπιστοσύνης στο UI).
        public static bool HasSourceOverlap(string? deviceName, IEnumerable<string> confirmedNames)
        {
            if (string.IsNullOrEmpty(deviceName)) return false;
            foreach (var confirmed in confirmedNames)
            {
                if (string.IsNullOrEmpty(confirmed)) continue;
                if (deviceName.Contains(confirmed, StringComparison.OrdinalIgnoreCase) ||
                    confirmed.Contains(deviceName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        // Port των 4 πρόσθετων πηγών (AMD/NVIDIA/Dell/SDIO) + OEM ανίχνευση (Lenovo/HP) από το
        // Start-DriverScan (~11977-12289) - ΕΝΑ combined script, ίδια δομή/λογική με το ps1 original,
        // ΚΑΝΕΝΑ αυτόματο install (μόνο σύνδεσμοι λήψης/κουμπιά - ο χρήστης αποφασίζει). Το SDIO
        // τρέχει ΠΑΝΤΑ headless (-nogui, βλ. σχόλιο ps1 ~12187) - ΠΟΤΕ δεν εμφανίζει το δικό του
        // παράθυρο· ΣΚΟΠΙΜΑ ΔΕΝ κάνει parsing μεμονωμένων driver γραμμών από το log του (η ακριβής
        // μορφή δεν έχει ζωντανά επιβεβαιωθεί - ίδια ΣΗΜΕΙΩΣΗ ΕΙΛΙΚΡΙΝΕΙΑΣ με το ps1), μόνο αναφέρει αν
        // βρέθηκαν driverpacks (Sum>0) με κουμπί να ανοίξει τον φάκελο αναφοράς για χειροκίνητο έλεγχο.
        // ΔΙΟΡΘΩΣΗ (ρητό αίτημα χρήστη: "η ενημέρωση των οδηγών συστήματος να μην γίνεται αυτόματα") -
        // αφαιρέθηκε το -autoupdate flag που το ps1 original είχε προσθέσει (βλ. σχόλιο ps1 ~12203):
        // η επίσημη τεκμηρίωση του SDI το περιγράφει ως "starts downloading automatically" και σε
        // περαιτέρω έρευνα ενδέχεται να κατεβάζει/εγκαθιστά ΚΑΙ τα ίδια τα driverpacks, όχι μόνο τον
        // τοπικό index - αντίκειτο στο "ΚΑΝΕΝΑ αυτόματο install" που ισχυριζόταν το UI. Χωρίς το flag,
        // το SDIO τρέχει αυστηρά scan-only (συγκρίνει έναντι ΚΕΝΟΥ τοπικού index, άρα πάντα Sum:0/
        // HasDriverPacks=false) - καμία λήψη/εγκατάσταση καμίας μορφής χωρίς ρητή ενέργεια χρήστη.
        public static async Task<VendorScanResult> ScanVendorSourcesAsync()
        {
            const string script = @"
$ErrorActionPreference = 'Continue'
$installed = @()
try {
    $installed = Get-CimInstance Win32_PnPSignedDriver -ErrorAction Stop |
        Where-Object { $_.DeviceName -and $_.DriverVersion } |
        Select-Object DeviceName, DriverVersion, DriverDate
} catch {}

$amdResults = @()
try {
    $amdGpus = Get-CimInstance Win32_VideoController -ErrorAction Stop | Where-Object { $_.Name -match 'AMD|Radeon' }
    foreach ($gpu in $amdGpus) {
        $gpuName = $gpu.Name
        $slugFamily = $null; $slugSeries = $null
        if ($gpuName -match 'RX\s*9\d{3}') { $slugFamily = 'radeon-rx'; $slugSeries = 'radeon-rx-9000-series' }
        elseif ($gpuName -match 'RX\s*7\d{3}') { $slugFamily = 'radeon-rx'; $slugSeries = 'radeon-rx-7000-series' }
        elseif ($gpuName -match 'RX\s*6\d{3}') { $slugFamily = 'radeon-rx'; $slugSeries = 'radeon-rx-6000-series' }
        elseif ($gpuName -match 'RX\s*5\d{3}') { $slugFamily = 'radeon-rx'; $slugSeries = 'radeon-rx-5000-series' }
        elseif ($gpuName -match 'RX\s*Vega') { $slugFamily = 'radeon-rx'; $slugSeries = 'radeon-rx-vega-series' }
        if (-not $slugFamily) { continue }
        $modelSlug = ($gpuName -replace '[™®()]', '').Trim()
        $modelSlug = ($modelSlug -replace '\s+', '-').ToLower()
        if ($modelSlug -notmatch '^amd-') { $modelSlug = ""amd-$modelSlug"" }
        $amdUrl = ""https://www.amd.com/en/support/downloads/drivers.html/graphics/$slugFamily/$slugSeries/$modelSlug.html""
        try {
            $amdResp = Invoke-WebRequest -Uri $amdUrl -UseBasicParsing -TimeoutSec 15 -ErrorAction Stop
            $amdHtml = $amdResp.Content
            $verMatch = [regex]::Match($amdHtml, 'amd-software-adrenalin-edition-([\d.]+)-[^""]*\.exe')
            $dateMatch = [regex]::Match($amdHtml, '<strong>\s*Release Date\s*</strong>\s*<p>\s*([\d-]+)\s*</p>')
            if ($verMatch.Success) {
                $installedGpuDrv = $installed | Where-Object { $_.DeviceName -eq $gpuName } | Select-Object -First 1
                $installedDateStr = $null
                if ($installedGpuDrv -and $installedGpuDrv.DriverDate) {
                    try { $installedDateStr = ([datetime]$installedGpuDrv.DriverDate).ToString('yyyy-MM-dd') } catch {}
                }
                $amdResults += [PSCustomObject]@{
                    GpuName = $gpuName
                    LatestVersion = $verMatch.Groups[1].Value
                    LatestReleaseDate = if ($dateMatch.Success) { $dateMatch.Groups[1].Value } else { $null }
                    InstalledVersion = if ($installedGpuDrv) { $installedGpuDrv.DriverVersion } else { $null }
                    InstalledDate = $installedDateStr
                    DownloadUrl = $amdUrl
                }
            }
        } catch {}
    }
} catch {}

$nvidiaResults = @()
try {
    $nvidiaGpus = Get-CimInstance Win32_VideoController -ErrorAction Stop | Where-Object { $_.Name -match 'NVIDIA|GeForce|Quadro|RTX|GTX' }
    if ($nvidiaGpus) {
        $osId = if ([Environment]::OSVersion.Version.Build -ge 22000) { 135 } else { 57 }
        $gpuListResp = Invoke-WebRequest -Uri 'https://www.nvidia.com/Download/API/lookupValueSearch.aspx?TypeID=3' -UseBasicParsing -TimeoutSec 20 -ErrorAction Stop
        [xml]$gpuListXml = $gpuListResp.Content
        $allGpuEntries = @($gpuListXml.LookupValueSearch.LookupValues.LookupValue)
        foreach ($gpu in $nvidiaGpus) {
            $gpuName = $gpu.Name
            $cleanName = $gpuName -replace '^NVIDIA\s+', ''
            $cleanName = $cleanName -replace '\s+with Max-Q Design', ''
            $cleanName = $cleanName -replace '\s*\(OEM\)', ''
            $cleanName = $cleanName -replace '\s+\d+GB$', ''
            $cleanName = $cleanName -replace 'Super', 'SUPER'
            $cleanName = $cleanName.Trim()
            $gpuEntry = $allGpuEntries | Where-Object { $_.Name -eq $cleanName } | Select-Object -First 1
            if (-not $gpuEntry) { continue }
            $pfid = $gpuEntry.Value
            try {
                $lookupUrl = ""https://gfwsl.geforce.com/services_toolkit/services/com/nvidia/services/AjaxDriverService.php?func=DriverManualLookup&pfid=$pfid&osID=$osId&upCRD=0&dch=1""
                $nvResp = Invoke-WebRequest -Uri $lookupUrl -UseBasicParsing -TimeoutSec 15 -ErrorAction Stop
                $nvJson = $nvResp.Content | ConvertFrom-Json -ErrorAction Stop
                if ($nvJson.Success -eq 1 -and $nvJson.IDS -and $nvJson.IDS.Count -gt 0) {
                    $dlInfo = $nvJson.IDS[0].downloadInfo
                    $latestDateStr = $null
                    try { $latestDateStr = ([datetime]::Parse($dlInfo.ReleaseDateTime)).ToString('yyyy-MM-dd') } catch {}
                    $installedGpuDrvN = $installed | Where-Object { $_.DeviceName -eq $gpuName } | Select-Object -First 1
                    $installedDateStrN = $null
                    if ($installedGpuDrvN -and $installedGpuDrvN.DriverDate) {
                        try { $installedDateStrN = ([datetime]$installedGpuDrvN.DriverDate).ToString('yyyy-MM-dd') } catch {}
                    }
                    $nvidiaResults += [PSCustomObject]@{
                        GpuName = $gpuName
                        LatestVersion = $dlInfo.Version
                        LatestReleaseDate = $latestDateStr
                        InstalledVersion = if ($installedGpuDrvN) { $installedGpuDrvN.DriverVersion } else { $null }
                        InstalledDate = $installedDateStrN
                        DownloadUrl = $dlInfo.DownloadURL
                    }
                }
            } catch {}
        }
    }
} catch {}

$dellResult = $null
try {
    $cs = Get-CimInstance Win32_ComputerSystem -ErrorAction Stop
    if ($cs.Manufacturer -match 'Dell') {
        $progFilesBase = if ([Environment]::Is64BitOperatingSystem) { ${env:ProgramFiles(x86)} } else { $env:ProgramFiles }
        $dcuPath = Join-Path -Path $progFilesBase -ChildPath 'Dell\CommandUpdate\dcu-cli.exe'
        if (Test-Path $dcuPath) {
            $dcuReportDir = Join-Path $env:TEMP ""OptimizerDCU_$([guid]::NewGuid().ToString('N'))""
            $dcuReportPath = Join-Path $dcuReportDir 'DCUApplicableUpdates.xml'
            try {
                Start-Process -FilePath $dcuPath -ArgumentList ""/scan -report=`""$dcuReportDir`"""" -Wait -WindowStyle Hidden -ErrorAction Stop
                if (Test-Path $dcuReportPath) {
                    [xml]$dcuXml = Get-Content -Path $dcuReportPath -Raw -ErrorAction Stop
                    $dcuUpdates = @($dcuXml.updates.update | Select-Object -Property name, version, date, urgency, type, category)
                    $dellResult = [PSCustomObject]@{ Installed = $true; Updates = $dcuUpdates; Error = $null }
                } else {
                    $dellResult = [PSCustomObject]@{ Installed = $true; Updates = @(); Error = $null }
                }
            } catch {
                $dellResult = [PSCustomObject]@{ Installed = $true; Updates = @(); Error = $_.Exception.Message }
            } finally {
                if (Test-Path $dcuReportDir) { Remove-Item -Path $dcuReportDir -Recurse -Force -ErrorAction SilentlyContinue }
            }
        } else {
            $dellResult = [PSCustomObject]@{ Installed = $false; Updates = @(); Error = $null }
        }
    }
} catch {}

$sdioResult = $null
try {
    $sdioLinksPath = Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Links\sdio.exe'
    $sdioPath = $null
    if (Test-Path $sdioLinksPath) { $sdioPath = $sdioLinksPath }
    else { $sdioCmd = Get-Command 'sdio.exe' -ErrorAction SilentlyContinue; if ($sdioCmd) { $sdioPath = $sdioCmd.Source } }
    if ($sdioPath) {
        $sdioOutDir = Join-Path $env:TEMP ""OptimizerSDIO_$([guid]::NewGuid().ToString('N'))""
        New-Item -ItemType Directory -Path $sdioOutDir -Force | Out-Null
        try {
            $sdioArgs = ""-nogui -autoclose -nostamp -output_dir:`""$sdioOutDir`"" -log_dir:`""$sdioOutDir`""""
            $sdioProc = Start-Process -FilePath $sdioPath -ArgumentList $sdioArgs -WindowStyle Hidden -PassThru -ErrorAction Stop
            $sdioFinished = $sdioProc.WaitForExit(600000)
            if (-not $sdioFinished) { try { $sdioProc.Kill() } catch {} }
            $sdioReportFiles = @(Get-ChildItem -Path $sdioOutDir -Filter '*.txt' -Recurse -ErrorAction SilentlyContinue)
            $sdioHasDriverPacks = $false
            $sdioLogFile = $sdioReportFiles | Where-Object { $_.Name -eq 'log.txt' } | Select-Object -First 1
            if ($sdioLogFile) {
                try {
                    $sdioLogText = [System.IO.File]::ReadAllText($sdioLogFile.FullName, [System.Text.Encoding]::Default)
                    $sdioSumMatch = [regex]::Match($sdioLogText, 'Driverpacks[\s\S]{0,200}?Sum:\s*(\d+)')
                    if ($sdioSumMatch.Success -and [int]$sdioSumMatch.Groups[1].Value -gt 0) { $sdioHasDriverPacks = $true }
                } catch {}
            }
            $sdioResult = [PSCustomObject]@{
                Installed = $true
                TimedOut = (-not $sdioFinished)
                ReportFound = ($sdioReportFiles.Count -gt 0)
                HasDriverPacks = $sdioHasDriverPacks
                ReportPath = if ($sdioReportFiles.Count -gt 0) { $sdioOutDir } else { $null }
                Error = $null
            }
            if ($sdioReportFiles.Count -eq 0) { Remove-Item -Path $sdioOutDir -Recurse -Force -ErrorAction SilentlyContinue }
        } catch {
            $sdioResult = [PSCustomObject]@{ Installed = $true; TimedOut = $false; ReportFound = $false; HasDriverPacks = $false; ReportPath = $null; Error = $_.Exception.Message }
        }
    } else {
        $sdioResult = [PSCustomObject]@{ Installed = $false; TimedOut = $false; ReportFound = $false; HasDriverPacks = $false; ReportPath = $null; Error = $null }
    }
} catch {}

$oemResult = $null
try {
    $csOem = Get-CimInstance Win32_ComputerSystem -ErrorAction Stop
    if ($csOem.Manufacturer -match 'Lenovo') {
        # ΝΕΟ (πρόσθετη πηγή - βλ. HANDOFF.md, jantari/LSUClient): ο επίσημος κατάλογος Lenovo ανά
        # μοντέλο (MTM code, τα πρώτα 4 χαρακτήρες του Model) - ΜΟΝΟ το ΠΡΩΤΟ, ελαφρύ XML ευρετήριο
        # (μετράει πακέτα), ΧΩΡΙΣ τις δεκάδες επιπλέον HTTP κλήσεις ανά πακέτο (τεκμηριωμένα αργό/
        # εύθραυστο αν γίνει πλήρες parsing - ΣΚΟΠΙΜΑ εκτός εμβέλειας εδώ, ίδια σύνεση με το ps1 original).
        $lenovoPkgCount = $null
        try {
            $mtm = $csOem.Model.Substring(0, 4)
            $lenovoResp = Invoke-WebRequest -Uri ""https://download.lenovo.com/catalog/${mtm}_Win11.xml"" -UseBasicParsing -TimeoutSec 15 -ErrorAction Stop
            $bom = [System.Text.Encoding]::UTF8.GetString(@(239,187,191))
            $lenovoContent = $lenovoResp.Content -replace ""^$bom"", ''
            [xml]$lenovoXml = $lenovoContent
            $lenovoPkgCount = @($lenovoXml.packages.package).Count
        } catch {}
        $oemResult = [PSCustomObject]@{ OemName = 'Lenovo'; LenovoPackageCount = $lenovoPkgCount }
    }
    elseif ($csOem.Manufacturer -match 'HP|Hewlett-Packard') { $oemResult = [PSCustomObject]@{ OemName = 'HP'; LenovoPackageCount = $null } }
} catch {}

[PSCustomObject]@{ AmdResults = @($amdResults); NvidiaResults = @($nvidiaResults); DellResult = $dellResult; SdioResult = $sdioResult; OemResult = $oemResult } | ConvertTo-Json -Depth 6 -Compress
";
            var output = await RunPowerShellScriptAsync(script, TimeSpan.FromMinutes(12));
            var empty = new VendorScanResult(Array.Empty<AmdDriverInfo>(), Array.Empty<NvidiaDriverInfo>(), null, null, null);
            if (string.IsNullOrWhiteSpace(output)) return empty;

            try
            {
                var root = JsonDocument.Parse(output).RootElement;

                var amd = new List<AmdDriverInfo>();
                foreach (var a in root.GetProperty("AmdResults").EnumerateArray())
                    amd.Add(new AmdDriverInfo(
                        a.GetProperty("GpuName").GetString() ?? "", a.GetProperty("LatestVersion").GetString() ?? "",
                        a.TryGetProperty("LatestReleaseDate", out var lrd) ? lrd.GetString() : null,
                        a.TryGetProperty("InstalledVersion", out var iv) ? iv.GetString() : null,
                        a.TryGetProperty("InstalledDate", out var id) ? id.GetString() : null,
                        a.GetProperty("DownloadUrl").GetString() ?? ""));

                var nvidia = new List<NvidiaDriverInfo>();
                foreach (var n in root.GetProperty("NvidiaResults").EnumerateArray())
                    nvidia.Add(new NvidiaDriverInfo(
                        n.GetProperty("GpuName").GetString() ?? "", n.GetProperty("LatestVersion").GetString() ?? "",
                        n.TryGetProperty("LatestReleaseDate", out var nlrd) ? nlrd.GetString() : null,
                        n.TryGetProperty("InstalledVersion", out var niv) ? niv.GetString() : null,
                        n.TryGetProperty("InstalledDate", out var nid) ? nid.GetString() : null,
                        n.GetProperty("DownloadUrl").GetString() ?? ""));

                DellScanResult? dell = null;
                if (root.TryGetProperty("DellResult", out var dr) && dr.ValueKind != JsonValueKind.Null)
                {
                    var dellUpdates = new List<DellUpdateInfo>();
                    if (dr.TryGetProperty("Updates", out var du) && du.ValueKind == JsonValueKind.Array)
                        foreach (var u in du.EnumerateArray())
                            dellUpdates.Add(new DellUpdateInfo(
                                u.TryGetProperty("name", out var n2) ? n2.GetString() : null,
                                u.TryGetProperty("version", out var v2) ? v2.GetString() : null,
                                u.TryGetProperty("date", out var d2) ? d2.GetString() : null,
                                u.TryGetProperty("urgency", out var ur2) ? ur2.GetString() : null,
                                u.TryGetProperty("type", out var t2) ? t2.GetString() : null,
                                u.TryGetProperty("category", out var c2) ? c2.GetString() : null));
                    dell = new DellScanResult(dr.GetProperty("Installed").GetBoolean(), dellUpdates,
                        dr.TryGetProperty("Error", out var de) ? de.GetString() : null);
                }

                SdioScanResult? sdio = null;
                if (root.TryGetProperty("SdioResult", out var sr) && sr.ValueKind != JsonValueKind.Null)
                    sdio = new SdioScanResult(
                        sr.GetProperty("Installed").GetBoolean(),
                        sr.TryGetProperty("TimedOut", out var to) && to.GetBoolean(),
                        sr.TryGetProperty("ReportFound", out var rf) && rf.GetBoolean(),
                        sr.TryGetProperty("HasDriverPacks", out var hdp) && hdp.GetBoolean(),
                        sr.TryGetProperty("ReportPath", out var rp) ? rp.GetString() : null,
                        sr.TryGetProperty("Error", out var se) ? se.GetString() : null);

                OemInfo? oem = null;
                if (root.TryGetProperty("OemResult", out var or) && or.ValueKind != JsonValueKind.Null)
                    oem = new OemInfo(
                        or.GetProperty("OemName").GetString() ?? "",
                        or.TryGetProperty("LenovoPackageCount", out var lpc) && lpc.ValueKind == JsonValueKind.Number ? lpc.GetInt32() : null);

                return new VendorScanResult(amd, nvidia, dell, sdio, oem);
            }
            catch (JsonException)
            {
                return empty;
            }
        }

        // Await the exact WUA driver result; no detached installation route.
        public static async Task<bool> InstallAndWaitAsync(string rowId, CancellationToken ct = default)
        {
            var result = await WindowsUpdateService.InstallDriverAsync(rowId, ct);
            if (result.RebootRequired) WingetService.RestartRequiredAfterUpdates = true;
            if (!result.Ok && result.Error != null) Trace.TraceWarning("WUA driver: " + result.Error);
            return result.Ok;
        }

        // 6.9.1: αντίγραφο των τρεχόντων οδηγών τρίτων (pnputil /export-driver) σε φάκελο με timestamp ΠΡΙΝ από κάθε εγκατάσταση οδηγού· κρατούνται τα 3 τελευταία.
        public static async Task<string?> ExportCurrentDriversAsync()
        {
            using var lease = await GearWinCore.EnterAsync(CoreWork.Backup);
            try
            {
                var rootDir = Path.Combine(SecureStorage.RootDir, "DriverExports");
                SecureStorage.SecureDirectory(rootDir);
                // Retain prior backups, including incomplete/legacy folders. Never
                // recursively delete an arbitrary directory merely because it is old.
                if (Directory.EnumerateDirectories(rootDir).Take(32).Count() >= 32) return null;
                var dest = Path.Combine(rootDir, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
                SecureStorage.SecureDirectory(dest);
                int code = await RunCatalogCommandAsync("pnputil.exe", "/export-driver", "*", dest);
                // No proof of a bootable or complete backup: require actual INF
                // output before allowing this export to serve as a precondition.
                return code == 0 && Directory.EnumerateFiles(dest, "*.inf", SearchOption.AllDirectories)
                    .Any(MaintenancePaths.IsOrdinaryLocalPath) ? dest : null;
            }
            catch { return null; }
        }
        private const string BS_Q = "\"";

        internal static bool DriverBackupReady(bool restorePoint, string? exportPath) =>
            restorePoint && !string.IsNullOrWhiteSpace(exportPath);

        // DISM Export-Driver - εξάγει ΟΛΟΥΣ τους οδηγούς τρίτων κατασκευαστών (ΟΧΙ τους εργοστασιακούς
        // Windows) σε φάκελο. Ίδιος μηχανισμός με καθιερωμένα εργαλεία δημιουργίας αντιγράφων οδηγών.
        public static Task<bool> BackupAsync(string destinationPath) =>
            GearWinCore.RunAsync(CoreWork.Backup, () => RunProcessAsync("dism.exe", $"/Online /Export-Driver /Destination:\"{destinationPath}\"", TimeSpan.FromMinutes(10)));

        // pnputil - εγκαθιστά ξανά όλους τους οδηγούς από φάκελο αντιγράφου. Απαιτεί δικαιώματα
        // διαχειριστή (ήδη διαθέσιμα - βλ. app.manifest).
        public static Task<bool> RestoreAsync(string sourcePath) =>
            GearWinCore.RunAsync(CoreWork.SoftwareChange, () => RunProcessAsync("pnputil.exe", $"/add-driver \"{sourcePath}\\*.inf\" /subdirs /install", TimeSpan.FromMinutes(10)));

        // Get-WindowsDriver (DISM cmdlet, ΟΧΙ ανάλυση κειμένου pnputil - το τελευταίο είναι localized
        // ανά γλώσσα Windows, ήδη προκάλεσε bug αλλού στο ps1 original). Φιλτράρει Inbox=false (ΜΟΝΟ
        // 3rd-party, ΠΟΤΕ εργοστασιακούς οδηγούς Windows).
        public static async Task<IReadOnlyList<DriverStoreEntry>> ScanStoreAsync()
        {
            const string script = @"
$ErrorActionPreference = 'Continue'
$drivers = Get-WindowsDriver -Online -ErrorAction SilentlyContinue | Where-Object { -not $_.Inbox }
# WMT 6.6: πακέτο ""σε χρήση"" = το InfName που είναι δεσμευμένο σε κάποια συσκευή (Win32_PnPSignedDriver).
$inUse = @{}
Get-CimInstance Win32_PnPSignedDriver -ErrorAction SilentlyContinue | ForEach-Object { if ($_.InfName) { $inUse[([string]$_.InfName).ToLowerInvariant()] = 1 } }
$result = @($drivers | ForEach-Object {
    [PSCustomObject]@{
        Driver           = $_.Driver
        OriginalFileName = [System.IO.Path]::GetFileName($_.OriginalFileName)
        Version          = $_.Version
        Date             = if ($_.Date) { $_.Date.ToString('yyyy-MM-dd') } else { $null }
        ClassName        = $_.ClassName
        InUse            = $inUse.ContainsKey(([string]$_.Driver).ToLowerInvariant())
    }
})
$result | ConvertTo-Json -Depth 4 -Compress
";
            var output = await RunPowerShellScriptAsync(script, TimeSpan.FromMinutes(3));
            if (string.IsNullOrWhiteSpace(output)) return Array.Empty<DriverStoreEntry>();

            try
            {
                var root = JsonDocument.Parse(output).RootElement;
                var entries = new List<DriverStoreEntry>();
                var elements = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray() : new[] { root }.AsEnumerable();
                foreach (var d in elements)
                {
                    entries.Add(new DriverStoreEntry(
                        d.GetProperty("Driver").GetString() ?? "",
                        d.GetProperty("OriginalFileName").GetString() ?? "",
                        d.TryGetProperty("Version", out var v) ? v.GetString() : null,
                        d.TryGetProperty("Date", out var dt) ? dt.GetString() : null,
                        d.TryGetProperty("ClassName", out var c) ? c.GetString() : null,
                        d.TryGetProperty("InUse", out var iu) && iu.ValueKind == JsonValueKind.True));
                }
                return entries;
            }
            catch (JsonException)
            {
                return Array.Empty<DriverStoreEntry>();
            }
        }

        // WMT 6.6 ("Reworked Clean Old Drivers"): ανά ομάδα πακέτων με το ίδιο OriginalFileName, κρατιέται
        // ΠΡΩΤΑ ό,τι είναι σε χρήση, μετά η ΥΨΗΛΟΤΕΡΗ ΕΚΔΟΣΗ, μετά η νεότερη ημερομηνία (πριν: μόνο ημερομηνία -
        // ένας νεότερος οδηγός με παλιότερη ημερομηνία κυκλοφορίας κρατούσε λάθος πακέτο). Πακέτο σε χρήση δεν
        // προτείνεται ΠΟΤΕ προς διαγραφή. Το KeptInfo λέει ποιο πακέτο/έκδοση επιβιώνει.
        public static IReadOnlyList<DriverStoreEntry> SelectOldDrivers(IEnumerable<DriverStoreEntry> all)
        {
            static Version V(string? s) => Version.TryParse(s, out var v) ? v : new Version(0, 0);
            var result = new List<DriverStoreEntry>();
            foreach (var g in all.GroupBy(d => d.OriginalFileName, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            {
                var ordered = g.OrderByDescending(d => d.InUse).ThenByDescending(d => V(d.Version))
                    .ThenByDescending(d => d.Date ?? "", StringComparer.Ordinal).ToList();
                var keeper = ordered[0];
                var info = string.Format(LanguageService.T("Opt_DriverKeptInfo"), keeper.Driver, keeper.Version ?? "?");
                foreach (var d in ordered.Skip(1))
                    if (!d.InUse) result.Add(d with { KeptInfo = info });
            }
            return result;
        }

        // pnputil αρνείται από μόνο του να διαγράψει driver που βρίσκεται ενεργά σε χρήση - καμία
        // πρόσθετη προστασία χρειάζεται εδώ (ίδια λογική με το ps1 original).
        // ΑΣΦΑΛΕΙΑ: το "παλιότερο" πακέτο (κατά ημερομηνία) μπορεί να είναι ο ΕΝΕΡΓΟΣ οδηγός μιας συσκευής (π.χ. ο
        // χρήστης έκανε rollback ενός προβληματικού νέου οδηγού GPU/δικτύου). Με "/uninstall /force" το pnputil τον
        // αφαιρούσε ΚΑΙ από τη συσκευή (μαύρη οθόνη / χωρίς δίκτυο). Χωρίς αυτά τα flags το pnputil αρνείται να διαγράψει
        // οδηγό που χρησιμοποιείται — η αποτυχία εμφανίζεται στον χρήστη αντί για βλάβη. Επίσης ελέγχουμε το όνομα (oemNN.inf).
        public static Task<bool> DeleteStoreDriverAsync(string driverOemName)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(driverOemName ?? "", @"^oem\d+\.inf$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                return Task.FromResult(false);
            return GearWinCore.RunAsync(CoreWork.SoftwareChange, () => RunProcessAsync("pnputil.exe", $"/delete-driver {driverOemName}", TimeSpan.FromMinutes(2)));
        }

        private static async Task<string> RunPowerShellScriptAsync(string script, TimeSpan timeout)
        {
            // ΑΣΦΑΛΕΙΑ: -EncodedCommand αντί για script στο %TEMP% (βλ. PowerShellArgs) - αρχείο μόνο αν το
            // script ξεπερνά το όριο γραμμής εντολών.
            var scriptFile = PowerShellArgs.Fits(script) ? null : SecureStorage.NewPrivateTempFile("ps", ".ps1");
            if (scriptFile != null) File.WriteAllText(scriptFile, script);
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = scriptFile == null ? PowerShellArgs.Command(script) : $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptFile}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                };
                using var process = Process.Start(psi);
                if (process == null) return "";
                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                _ = process.StandardError.ReadToEndAsync(); // drain — αλλιώς το PowerShell μπλοκάρει όταν γεμίσει το stderr pipe
                using var cts = new System.Threading.CancellationTokenSource(timeout);
                try { await process.WaitForExitAsync(cts.Token); }
                catch (OperationCanceledException) { try { process.Kill(true); } catch { } return ""; }
                return await stdoutTask;
            }
            finally
            {
                if (scriptFile != null) { try { File.Delete(scriptFile); } catch { } }
            }
        }

        private static async Task<bool> RunProcessAsync(string fileName, string arguments, TimeSpan timeout)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using var process = Process.Start(psi);
                if (process == null) return false;
                // Και τα δύο pipes ήταν redirected αλλά ΠΟΤΕ δεν διαβάζονταν — pnputil/expand/wusa με πολλή έξοδο
                // μπλόκαραν για πάντα μέχρι το timeout και αναφέρονταν ως αποτυχία.
                _ = process.StandardOutput.ReadToEndAsync();
                _ = process.StandardError.ReadToEndAsync();
                using var cts = new System.Threading.CancellationTokenSource(timeout);
                try { await process.WaitForExitAsync(cts.Token); }
                catch (OperationCanceledException) { try { process.Kill(true); } catch { } return false; }
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
