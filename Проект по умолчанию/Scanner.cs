using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace MineCheck
{
    internal enum Severity
    {
        Critical = 0,
        Warning = 1,
        Info = 2
    }

    internal sealed class Finding
    {
        public Severity Level;
        public string Category;
        public string Title;
        public string Detail;
        public string Path;
        public int Score;
        public bool Confirmed;
        public bool Recent;

        public Finding(Severity level, string category, string title, string detail, string path)
        {
            Level = level;
            Category = category;
            Title = title;
            Detail = detail ?? "";
            Path = path ?? "";
        }
    }

    internal sealed class ScanReport
    {
        public readonly List<Finding> Findings = new List<Finding>();
        public string Code = "";
        public DateTime Started = DateTime.Now;
        public DateTime SessionStart = DateTime.Now;
        public TimeSpan Duration = TimeSpan.Zero;
        public bool Cancelled;
        public bool Elevated;
        public int FilesScanned;
        public int ProcessesScanned;
        public int JarsScanned;
    }

    internal sealed class Scanner
    {
        public event Action<int, string> StageStarted;
        public event Action<int, double> StageProgress;
        public event Action<int, int> StageFinished;
        public event Action<Finding> Found;
        public event Action<string> Log;

        private CancellationToken token;
        private ScanReport report;
        private int stageIndex;
        private int stageFindings;
        private DateTime sessionStart;
        private string selfDir = "";
        private readonly object sync = new object();

        // ---------------------------------------------------------------- база
        // уникальные имена клиентов: не встречаются у легит-модов
        private static readonly string[] Clients =
        {
            "catlavan", "doomsday", "liquidbounce", "wurst", "novoline", "konas",
            "astolfo", "aristois", "meteorclient", "bleachhack", "salhack", "phobos",
            "exhibition", "trollhack", "gamesense", "whiteout", "jigsaw", "rusherhack",
            "tenacity", "dortware", "nursultan", "dreamclient", "venusclient",
            "impactclient", "futureclient", "flugel", "spooky", "zeroday", "fatality",
            "neverlose", "onetap", "aimware", "legendware", "interium", "huzuni",
            "mhub", "systemdlc", "cortexclient", "cortex hack", "cryptix", "pandora",
            "moonlight", "azura", "seppuku", "entropy", "skeet", "matix", "akrien",
            "celestial", "kokscold", "lexis", "sunrise", "dripclient", "riseclient",
            "fluxclient", "sigmaclient", "deltaclient", "blazeclient", "dopeclient",
            "novaclient", "wolfram", "nixware", "hylex", "selenium client", "syphlex",
            "vape.gg", "vapev4", "vapelite", "vape lite", "vape v4", "arcticclient",
            "skidware", "skidfuscator", "noturfriend", "unfairclient",
            "kiwi client", "kiwiclient", "borion", "borest", "netherite", "sparkclient",
            "aetherclient", "energyclient", "deltaforce", "phantomclient", "voidclient",
            "midnightclient", "onyxclient", "apexclient", "lynxclient", "horizonclient",
            "zenithclient", "ravenclient", "ghostclient", "shadowclient", "voidhack",
            "kryptonclient", "bloomclient", "solstice", "equinox", "eclipse client",
            "exhibitionclient", "kawaiiclient", "sakuraclient", "sugarclient",
            "grandclient", "proclient", "loyaltyclient", "helixclient", "ziggoclient",
            "cortex", "mhubclient", "m-hub", "system dlc", "sysdlc", "cranium",
            "prestige", "procyon", "slinky", "augustus", "riven", "xenonclient",
            "breezeclient", "marlow", "nursultanclient", "nurikclient", "akrienclient",
            "kokscoldclient", "lexisclient", "sunriseclient", "dripclient", "koksclient",
            "nnclient", "mhack",
            
            // ========== НОВЫЕ СКРЫТЫЕ КЛИЕНТЫ 2024-2025 ==========
            // Ghost/Hidden клиенты
            "orchardclient", "orchard", "venimiclient", "venimi", "luminaclient",
            "paranoidclient", "paranoid", "dqrkisclient", "dqrkis", "ghostlyclient",
            "autismclient", "autism", "prestigeclient", "venimi client",
            
            // External клиенты
            "latite", "flarial", "onixclient", "fateclient", "bedrockclient",
            "externalclient", "memoryclient", "driverclient",
            
            // Inject клиенты
            "injectclient", "payloadclient", "hookclient", "spectreclient",
            "spectre", "injectorclient", "nativeclient",
            
            // Legit-читы (скрытые)
            "akiraghost", "akira", "fusionclient", "fusion+", "haruclient",
            "haru", "iridiumclient", "iridium", "dripclient", "juulclient",
            "koidclient", "plowwclient", "roseclient", "explicitclient",
            
            // Новые клиенты
            "zamorozka", "wexside", "mincedclient", "thunderhack", "relake",
            "rockstarclient", "eternityclient", "excellentclient", "arbuzclient",
            "deadcode", "melonity", "wildclient", "catlean",
            
            // Private клиенты
            "privateclient", "customclient", "buildclient", "betaclient",
            "alphabuild", "testclient", "devbuild", "nightlybuild"
        };

        // короткие/неоднозначные имена: ищем только целым словом в имени файла
        private static readonly string[] WeakClients =
        {
            "rise", "flux", "sigma", "delta", "blaze", "drip", "dope", "kappa",
            "nix", "fade", "arctic", "saint", "venom", "toxic", "prime", "nebula",
            "orbit", "vortex", "spirit", "aurora", "atom", "zephyr", "alpha", "omega",
            "legit", "horizon", "eclipse", "phantom", "shadow", "ghost", "raven"
        };

        // сильные модули: сами по себе почти не встречаются у легит-модов
        private static readonly string[] StrongModules =
        {
            "killaura", "triggerbot", "aimbot", "aimassist", "antiknockback", "antikb",
            "freecam", "itemesp", "chestesp", "playeresp", "mobesp", "xray", "wallhack",
            "nofall", "scaffold", "nuker", "fastplace", "instabreak", "antiaim",
            "nametags", "tracers", "autototem", "autoarmor", "cheststealer", "silentaura",
            "disabler", "antivoid", "chestfinder", "storageesp", "backtrack", "longjump",
            "speedhack", "flyhack", "noslow", "autosoup", "autopearl", "autopot",
            "keepspray", "holeesp", "autoblock", "fastbreak", "bowaimbot", "chestlooter",
            "nodamage", "nohurtcam", "antifall", "fastmine", "blockesp", "autoeat",
            "autoheal", "autotrap", "burrow", "crystalaura", "anchoraura", "holesnap",
            "packetfly", "elytrafly", "noweb", "noclip", "stepup", "antibot", "antiaim",
            
            // Новые модули
            "killaurab", "smartaura", "multiaura", "switchaura", "tpaura",
            "velocity", "reach", "hitbox", "criticals", "autoclicker",
            "rightclicker", "leftclicker", "jitterclick", "burstclick",
            "bowaimbot", "arrowaimbot", "projectileaim",
            "autocrit", "crits", "antivelocity", "kb",
            "scaffoldtower", "autobridge", "speedmine",
            "flybypass", "speedbypass", "timermove",
            "playerfinder", "basefinder", "radar",
            "projections", "trajectory", "chams",
            "selfdestruct", "ghostmode", "panic",
            "configmanager", "presetmanager",
            "packetbypass", "anticheatbypass",
            "matrixbypass", "vulcanbypass", "aacbypass"
        };

        // неоднозначные модули: только в связке с другими признаками
        private static readonly string[] WeakModules =
        {
            "reach", "velocity", "phase", "blink", "surround", "criticals", "hitbox",
            "searchhack", "speed", "flight", "aura", "esp", "aim", "fly", "nofire",
            "noswing", "autoclicker", "clicker", "macro", "bhop", "strafe", "timer"
        };

        // легитимные моды и лаунчеры — никогда не считаем читом
        private static readonly string[] Legit =
        {
            "optifine", "optifabric", "sodium", "iris", "lithium", "starlight",
            "ferritecore", "krypton", "c2me", "modernfix", "embeddium", "indium",
            "continuity", "fabric-api", "fabricloader", "fabric-loader", "quilt",
            "neoforge", "forge", "mixinextras", "mixin", "architectury", "cloth-config",
            "clothconfig", "modmenu", "yacl", "owo", "geckolib", "trinkets", "jei",
            "roughlyenoughitems", "rei", "emi", "journeymap", "xaero", "wthit", "jade",
            "appleskin", "voicechat", "plasmo", "3dskinlayers", "notenoughanimations",
            "sodium-extra", "reeses", "entityculling", "dynamiclights", "bobby",
            "capes", "citresewn", "animatica", "puzzle", "malilib", "litematica",
            "minihud", "tweakeroo", "itemscroller", "syncmatica", "servux", "carryon",
            "waystones", "ftb", "create", "botania", "mekanism", "ae2", "thermal",
            "farmersdelight", "serene", "tetra", "apotheosis", "curios", "patchouli",
            "attributefix", "balm", "midnightlib", "fzzy", "betterf3", "replaymod",
            "badlion", "lunarclient", "tlauncher", "legacy", "prism", "multimc",
            "atlauncher", "gdlauncher", "sklauncher", "modrinth", "curseforge",
            "overwolf", "5zig", "batmod", "essential", "labymod", "cosmetica",
            "sodiumdynamiclights", "distandhorizons", "noisium", "threadtweak",
            "particlerain", "visuals", "sodiumleafculling", "chunky", "collective"
        };

        private static readonly string[] PanicKeys =
        {
            ".panik", ".panic", "panic_key", "panickey", "panic_keybind", "panicmodule",
            "panic_module", "right_shift", "rightshift", "rshift", "r_shift"
        };

        // общие слова, которые почти не встречаются у легит-модов
        private static readonly string[] GenericHints =
        {
            "cheat", "hack", "bypass", "injector", "injected", "autoclicker",
            "cheatmenu", "cheatgui", "cheatclient", "читы", "чит-", "читклиент",
            "killaura", "aimbot", "xray", "wallhack", "freecam", "triggerbot"
        };

        // известные папки конфигов чит-клиентов
        private static readonly string[] CheatFolders =
        {
            ".mhub", "mhub", ".systemdlc", "systemdlc", ".cortex", "cortex",
            ".catlavan", "catlavan", ".doomsday", "doomsday", ".vape", "vape",
            ".liquidbounce", "liquidbounce", ".wurst", "wurst", ".konas", "konas",
            ".astolfo", "astolfo", ".novoline", "novoline", ".rise", "riseclient",
            ".flux", "fluxclient", ".drip", "dripclient", ".sunrise", "sunriseclient",
            ".nursultan", "nursultan", ".akrien", "akrien", ".koks", "koksclient",
            
            // Новые папки
            ".orchard", "orchard", ".venimi", "venimi", ".lumina", "lumina",
            ".prestige", "prestige", ".paranoid", "paranoid", ".dqrkis", "dqrkis",
            ".ghostly", "ghostly", ".autism", "autism",
            ".wexside", "wexside", ".zamorozka", "zamorozka",
            ".thunderhack", "thunderhack", ".minced", "minced",
            ".eternity", "eternity", ".wild", "wildclient",
            ".celestial", "celestial", ".meteor", "meteor-client",
            ".impact", "impact", ".future", "futureclient",
            ".sigma", "sigmaclient", ".jello", "jelloclient",
            ".akira", "akira", ".fusion", "fusionclient",
            ".haru", "haruclient", ".iridium", "iridium",
            ".entropy", "entropyclient", ".juul", "juulclient",
            ".koid", "koidclient", ".ploww", "plowwclient"
        };

        private static readonly string[] WebHints =
        {
            "catlavan", "doomsday", "mhub", "systemdlc", "cortex client", "wurst",
            "liquidbounce", "impact client", "killaura", "triggerbot", "freecam",
            "xray hack", "esp hack", "cheat minecraft", "читы майнкрафт", "чит клиент",
            "аура", "триггербот", "vape.gg", "neverlose", "onetap", "fatality",
            "gamesense", "skeet", "novoline", "конas", "konas",
            
            // Новые поисковые запросы
            "meteor client download", "aristois download", "future client",
            "rise client", "pyro client", "sigma client", "jello client",
            "flux client", "drip client", "celestial client",
            "ghost client", "inject client", "external client",
            "minecraft hack download", "minecraft cheat download",
            "free minecraft hack", "minecraft aimbot", "killaura download",
            "xray download", "esp download", "autoclicker minecraft",
            "vape lite", "vape v4", "spook client", "akira ghost",
            "fusion+ client", "haru client", "iridium client", "entropy client",
            "prestige client", "orchard client", "venimi client", "lumina client",
            "paranoid client", "dqrkis client", "ghostly client",
            "nursultan client", "wild client", "wexside client", "zamorozka",
            "thunderhack", "minced client", "eternity client",
            "minecraft cheat 2024", "minecraft hack 2024", "minecraft cheat 2025",
            "undetectable minecraft cheat", "closet cheat minecraft",
            "legit hack minecraft", "stream proof cheat",
            "2b2t hack", "anarchy hack", "crystal pvp hack",
            "baritone download", "impact baritone",
            "vape.gg/download", "mhub.gg", "systemdlc.xyz",
            "celka.su", "akrien.wtf", "arbuz.cc"
        };

        // только программы удалённого управления — обычные Discord/OBS не считаем
        private static readonly string[] RemoteTools =
        {
            "anydesk", "teamviewer", "rustdesk", "parsec", "ammyy", "supremo", "dwagent"
        };

        private static readonly string[] SuspiciousExt =
            { ".jar", ".exe", ".dll", ".bat", ".cmd", ".ps1", ".vbs", ".js", ".zip" };

        private static readonly string[] TextExt =
            { ".txt", ".json", ".cfg", ".ini", ".yml", ".yaml", ".properties", ".log", ".conf", ".toml", ".xml" };

        // ---------------------------------------------------------------- запуск
        public ScanReport Run(string code, CancellationToken cancel)
        {
            token = cancel;
            report = new ScanReport();
            report.Code = code;
            report.Started = DateTime.Now;
            report.Elevated = IsAdmin();

            sessionStart = DetectSessionStart();
            report.SessionStart = sessionStart;
            selfDir = AppDomain.CurrentDomain.BaseDirectory ?? "";

            try
            {
                Stage("Процессы и окна", StageProcesses);
                Stage("Клиент Minecraft", StageMinecraft);
                Stage("Версии с вшитыми читами", StageVersionJars);
                Stage("Файлы и папки", StageFiles);
                Stage("Недавние удаления", StageRecentDeletions);
                Stage("Корзина", StageRecycle);
                Stage("Реестр", StageRegistry);
                Stage("Активация и команды", StageTasks);
                Stage("Приложения за 24 часа", StageRecentApps);
                Stage("Prefetch", StagePrefetch);
                Stage("Быстрый доступ", StageJumpLists);
                Stage("Браузеры", StageBrowsers);
                Stage("USB и устройства", StageUsb);
                Stage("Память процессов", StageMemory);
                Stage("Java-процессы детально", StageJava);
                Stage("Инжекторы и DLL", StageInjectors);

                VerifyFindings();
            }
            catch (OperationCanceledException)
            {
                report.Cancelled = true;
            }
            finally
            {
                report.Duration = DateTime.Now - report.Started;
            }

            return report;
        }

        private void Stage(string name, Action body)
        {
            token.ThrowIfCancellationRequested();
            stageFindings = 0;
            if (StageStarted != null) StageStarted(stageIndex, name);
            try
            {
                body();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Note("этап пропущен: " + Short(ex.Message, 110));
            }
            if (StageFinished != null) StageFinished(stageIndex, stageFindings);
            stageIndex++;
        }

        private Finding Add(Severity level, string category, string title, string detail, string path)
        {
            Finding f = new Finding(level, category, title, detail, path);
            return Register(f);
        }

        private Finding Register(Finding f)
        {
            if (f.Level == Severity.Critical && f.Score == 0)
            {
                f.Score = 75;
                f.Confirmed = true;
            }

            // одна и та же улика не должна попадать в отчёт несколько раз
            if (f.Level != Severity.Info && !string.IsNullOrEmpty(f.Path))
            {
                lock (sync)
                {
                    foreach (Finding existing in report.Findings)
                    {
                        if (existing.Level == Severity.Info) continue;
                        if (string.IsNullOrEmpty(existing.Path)) continue;
                        if (!string.Equals(existing.Path, f.Path, StringComparison.OrdinalIgnoreCase)) continue;

                        if (f.Score > existing.Score)
                        {
                            existing.Score = f.Score;
                            if (f.Detail.Length > 0)
                                existing.Detail = existing.Detail.Length == 0 ? f.Detail : existing.Detail + " · " + f.Detail;
                        }
                        if (f.Level < existing.Level) existing.Level = f.Level;
                        if (f.Confirmed) existing.Confirmed = true;
                        return existing;
                    }
                }
            }

            lock (sync)
            {
                report.Findings.Add(f);
            }
            stageFindings++;
            if (Found != null) Found(f);
            return f;
        }

        private Finding AddScored(int score, string category, string title, string detail, string path)
        {
            if (score < 32) return null;
            Severity level = score >= 70 ? Severity.Critical : (score >= 45 ? Severity.Warning : Severity.Info);
            Finding f = new Finding(level, category, title, detail, path);
            f.Score = score;
            f.Confirmed = score >= 70;
            return Register(f);
        }

        private void Note(string message)
        {
            if (Log != null) Log(message);
        }

        private void Progress(double value)
        {
            token.ThrowIfCancellationRequested();
            if (StageProgress != null) StageProgress(stageIndex, value < 0 ? 0 : (value > 1 ? 1 : value));
        }

        // ------------------------------------------------------------ 1. процессы
        private void StageProcesses()
        {
            Dictionary<int, string> commands = CommandLines();
            Process[] processes = Process.GetProcesses();
            int processed = 0;
            int tools = 0;
            HashSet<string> seenTools = new HashSet<string>();

            for (int i = 0; i < processes.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                Process p = processes[i];
                try
                {
                    string name = p.ProcessName.ToLowerInvariant();
                    string path = ProcessPath(p);
                    string title = "";
                    try { title = p.MainWindowTitle ?? ""; } catch { }

                    string cmd;
                    commands.TryGetValue(p.Id, out cmd);
                    cmd = cmd ?? "";

                    if (processed++ % 12 == 0) Progress((double)i / Math.Max(1, processes.Length));

                    if (IsSelfPath(path) || name == "minecheck") continue;

                    string client = MatchClient(name) ?? MatchClient(Path.GetFileName(path));

                    string label = Path.GetFileName(path);
                    if (string.IsNullOrEmpty(label)) label = p.ProcessName;

                    if (client != null)
                    {
                        Add(Severity.Critical, "Процесс",
                            "Запущен чит-клиент: " + Short(label, 52),
                            "Совпадение по базе: «" + client + "»" + When(p),
                            path);
                        continue;
                    }

                    string titleClient = MatchClient(title);
                    if (titleClient != null)
                    {
                        Add(Severity.Critical, "Процесс",
                            "Окно чит-клиента: " + Short(title, 60),
                            "Заголовок содержит «" + titleClient + "»",
                            path);
                        continue;
                    }

                    if (Array.IndexOf(RemoteTools, name) >= 0 && seenTools.Add(name))
                    {
                        if (tools++ < 6)
                            Add(Severity.Info, "Инструменты",
                                "Запущен инструмент показа: " + Short(label, 52),
                                "Используется для screenshare / удалённого доступа",
                                path);
                    }

                    string lowPath = (path ?? "").ToLowerInvariant();
                    bool userFolder = lowPath.IndexOf("\\downloads\\") >= 0 ||
                                      lowPath.IndexOf("\\temp\\") >= 0 ||
                                      lowPath.IndexOf("\\desktop\\") >= 0;
                    if (userFolder && lowPath.EndsWith(".exe") &&
                        !IsLegitName(label) && GenericHits(label) > 0)
                    {
                        Add(Severity.Warning, "Процесс",
                            "Подозрительная программа запущена из загрузок: " + Short(label, 48),
                            "Маркеры чита в имени + пользовательская папка",
                            path);
                    }
                }
                catch { }
                finally
                {
                    p.Dispose();
                }
            }

            report.ProcessesScanned = processed;
            Progress(1);
            Note("проверено процессов: " + processed);
        }

        private string When(Process p)
        {
            try
            {
                DateTime started = p.StartTime;
                if (started >= sessionStart)
                    return " · запущен в " + started.ToString("HH:mm");
            }
            catch { }
            return "";
        }

        // --------------------------------------------------------------- 2. java
        private void ScanJavaEnvironment()
        {
            string[] names = { "JAVA_TOOL_OPTIONS", "_JAVA_OPTIONS", "JDK_JAVA_OPTIONS" };

            foreach (string name in names)
            {
                token.ThrowIfCancellationRequested();
                string value = "";
                try { value = Environment.GetEnvironmentVariable(name) ?? ""; }
                catch { }

                if (value.Length == 0) continue;

                string client = MatchClient(value);
                if (client != null)
                {
                    Add(Severity.Critical, "Java",
                        "Чит-клиент в переменной " + name,
                        "«" + client + "» · " + Short(value, 200),
                        name);
                }

                foreach (string agent in ExtractAgents(value))
                {
                    if (IsLegitName(Path.GetFileName(agent))) continue;
                    Add(File.Exists(agent) ? Severity.Critical : Severity.Warning, "Java",
                        "Внешний java-агент в переменной " + name,
                        "Подгружается в каждый запуск Java: " + Path.GetFileName(agent),
                        agent);
                }

                if (client == null && StrongModuleHits(value) >= 2)
                {
                    Add(Severity.Warning, "Java",
                        "Подозрительные опции Java в " + name,
                        Short(value, 200),
                        name);
                }
            }
        }

        private void StageJava()
        {
            ScanJavaEnvironment();

            Dictionary<int, string> commands = CommandLines();
            Process[] list = Process.GetProcesses();
            List<Process> java = new List<Process>();

            foreach (Process p in list)
            {
                string name = "";
                try { name = p.ProcessName.ToLowerInvariant(); }
                catch { }
                if (name == "java" || name == "javaw") java.Add(p);
                else p.Dispose();
            }

            if (java.Count == 0)
            {
                Note("Java-процессы не запущены");
                Progress(1);
                return;
            }

            for (int j = 0; j < java.Count; j++)
            {
                token.ThrowIfCancellationRequested();
                Process p = java[j];
                try
                {
                    string cmd;
                    commands.TryGetValue(p.Id, out cmd);
                    cmd = cmd ?? "";

                    string client = MatchClient(cmd);
                    if (client != null)
                    {
                        string cheatFile = FindCheatPath(cmd, client);
                        Add(Severity.Critical, "Java",
                            "Чит-клиент в аргументах Java: " +
                                (cheatFile.Length > 0 ? Path.GetFileName(cheatFile) : p.ProcessName),
                            "«" + client + "» в командной строке" + When(p) +
                            (cheatFile.Length > 0 ? " · " + Path.GetFileName(cheatFile) : ""),
                            cheatFile.Length > 0 ? cheatFile : Short(cmd, 260));
                    }

                    // jar-файлы из classpath / -jar
                    int cpJars = 0;
                    foreach (string jar in ExtractClasspathJars(cmd))
                    {
                        token.ThrowIfCancellationRequested();
                        if (cpJars++ > 22) break;
                        if (!File.Exists(jar)) continue;
                        if (IsSelfPath(jar)) continue;
                        if (IsLegitName(Path.GetFileName(jar))) continue;
                        if (IsStandardJavaPath(jar.ToLowerInvariant())) continue;

                        string why;
                        int score = ScoreJar(jar, out why);
                        if (score >= 45)
                        {
                            AddScored(score, "Java",
                                "Чит-файл в classpath Java: " + Short(Path.GetFileName(jar), 48),
                                why,
                                jar);
                        }
                    }

                    // java-агенты
                    foreach (string agent in ExtractAgents(cmd))
                    {
                        token.ThrowIfCancellationRequested();
                        if (!File.Exists(agent)) continue;
                        if (IsLegitName(Path.GetFileName(agent))) continue;

                        if (agent.IndexOf("\\temp\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            agent.IndexOf("\\downloads\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            agent.IndexOf("\\desktop\\", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            Add(Severity.Warning, "Java",
                                "Внешний java-агент в процессе " + p.ProcessName,
                                "Агент загружен из пользовательской папки: " + Path.GetFileName(agent),
                                agent);
                        }
                        else
                        {
                            Add(Severity.Info, "Java",
                                "Java-агент: " + Path.GetFileName(agent),
                                "Процесс " + p.ProcessName,
                                agent);
                        }
                    }

                    // подключённые jar вне стандартных папок
                    int extra = 0;
                    try
                    {
                        foreach (ProcessModule m in p.Modules)
                        {
                            token.ThrowIfCancellationRequested();
                            string file = "";
                            try { file = m.FileName ?? ""; } catch { }
                            if (file.Length == 0) continue;
                            string low = file.ToLowerInvariant();
                            if (!low.EndsWith(".jar")) continue;
                            if (IsSelfPath(file)) continue;
                            if (IsLegitName(Path.GetFileName(file))) continue;
                            if (IsStandardJavaPath(low)) continue;
                            if (MatchClient(low) == null && StrongModuleHits(low) == 0) continue;

                            extra++;
                            if (extra > 12) break;
                            Add(Severity.Warning, "Java",
                                "Лишний модуль подключён к Java",
                                Path.GetFileName(p.ProcessName) + " · " + Path.GetFileName(file),
                                file);
                        }
                    }
                    catch { }
                }
                catch { }
                finally
                {
                    p.Dispose();
                }

                Progress((double)(j + 1) / java.Count);
            }

            Note("проверено Java-процессов: " + java.Count);
        }

        private static bool IsStandardJavaPath(string low)
        {
            return low.IndexOf("\\libraries\\") >= 0 ||
                   low.IndexOf("\\versions\\") >= 0 ||
                   low.IndexOf("\\mods\\") >= 0 ||
                   low.IndexOf("\\jdk") >= 0 ||
                   low.IndexOf("\\jre") >= 0 ||
                   low.IndexOf("\\runtime\\") >= 0 ||
                   low.IndexOf("lunarclient") >= 0 ||
                   low.IndexOf("badlion") >= 0 ||
                   low.IndexOf("tlauncher") >= 0 ||
                   low.IndexOf("prismlauncher") >= 0 ||
                   low.IndexOf("modrinth") >= 0 ||
                   low.IndexOf("curseforge") >= 0;
        }

        private static IEnumerable<string> ExtractAgents(string cmd)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrEmpty(cmd)) return result;

            int index = 0;
            while ((index = cmd.IndexOf("-javaagent:", index, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                int start = index + 11;
                int end = start;
                while (end < cmd.Length && cmd[end] != ' ' && cmd[end] != '"') end++;
                if (end > start + 2)
                {
                    string value = cmd.Substring(start, end - start).Trim('"');
                    int eq = value.IndexOf('=');
                    if (eq > 0) value = value.Substring(0, eq);
                    result.Add(value);
                }
                index = end;
            }
            return result;
        }

        private static List<string> SplitCommand(string cmd)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrEmpty(cmd)) return result;

            StringBuilder current = new StringBuilder();
            bool quoted = false;

            foreach (char ch in cmd)
            {
                if (ch == '"') { quoted = !quoted; continue; }
                if ((ch == ' ' || ch == '\t') && !quoted)
                {
                    if (current.Length > 0) { result.Add(current.ToString()); current.Length = 0; }
                }
                else current.Append(ch);
            }
            if (current.Length > 0) result.Add(current.ToString());
            return result;
        }

        private static string FindCheatPath(string cmd, string client)
        {
            List<string> candidates = new List<string>();

            foreach (string agent in ExtractAgents(cmd)) candidates.Add(agent);
            foreach (string jar in ExtractClasspathJars(cmd)) candidates.Add(jar);
            foreach (string token in SplitCommand(cmd))
                if (token.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)) candidates.Add(token);

            string fallback = "";
            foreach (string candidate in candidates)
            {
                string path;
                try { path = Path.GetFullPath(candidate); }
                catch { continue; }
                if (!File.Exists(path)) continue;

                string name = Path.GetFileName(path).ToLowerInvariant();
                if (client != null && name.IndexOf(client, StringComparison.Ordinal) >= 0) return path;

                string low = path.ToLowerInvariant();
                bool userFolder = low.IndexOf("\\temp\\") >= 0 ||
                                  low.IndexOf("\\downloads\\") >= 0 ||
                                  low.IndexOf("\\desktop\\") >= 0;
                if (fallback.Length == 0 && userFolder) fallback = path;
            }
            return fallback;
        }

        private static IEnumerable<string> ExtractClasspathJars(string cmd)
        {
            List<string> result = new List<string>();
            List<string> tokens = SplitCommand(cmd);

            for (int i = 0; i < tokens.Count; i++)
            {
                string token = tokens[i];
                string value = null;

                if (token == "-cp" || token == "-classpath" || token == "--class-path")
                {
                    if (i + 1 < tokens.Count) value = tokens[i + 1];
                }
                else if (token.StartsWith("-cp=", StringComparison.OrdinalIgnoreCase))
                    value = token.Substring(4);
                else if (token == "-jar" && i + 1 < tokens.Count)
                    value = tokens[i + 1];

                if (string.IsNullOrEmpty(value)) continue;

                foreach (string part in value.Split(';'))
                {
                    string jar = part.Trim();
                    if (jar.Length > 4 && jar.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                        result.Add(jar);
                }
            }

            return result;
        }

        // ---------------------------------------------------------- 3. minecraft
        private void ScanCheatFolders()
        {
            string[] bases =
            {
                Roaming, Home,
                Path.Combine(Roaming, ".minecraft"),
                Path.Combine(Home, ".minecraft"),
                Path.Combine(Home, "AppData", "Local")
            };

            foreach (string baseDir in bases)
            {
                if (!Directory.Exists(baseDir)) continue;
                foreach (string folder in CheatFolders)
                {
                    token.ThrowIfCancellationRequested();
                    string full;
                    try { full = Path.Combine(baseDir, folder); }
                    catch { continue; }
                    if (!Directory.Exists(full)) continue;
                    if (IsSelfPath(full)) continue;

                    string client = MatchClient(folder) ?? MatchClient(full);
                    AddScored(80, "Minecraft",
                        "Папка чит-клиента: " + Path.GetFileName(full),
                        (client != null ? "«" + client + "» · " : "") + "конфиги и настройки чита",
                        full);
                }
            }
        }

        private void StageMinecraft()
        {
            ScanCheatFolders();

            List<string> roots = MineRoots();
            if (roots.Count == 0)
            {
                Note("папка .minecraft не найдена");
                Progress(1);
                return;
            }

            for (int r = 0; r < roots.Count; r++)
            {
                token.ThrowIfCancellationRequested();
                string root = roots[r];
                Progress((double)r / roots.Count);

                string versions = Path.Combine(root, "versions");
                if (Directory.Exists(versions))
                {
                    foreach (string dir in SafeDirs(versions))
                    {
                        token.ThrowIfCancellationRequested();
                        string name = Path.GetFileName(dir).ToLowerInvariant();
                        string client = MatchClient(name);
                        if (client != null)
                        {
                            Add(Severity.Critical, "Minecraft",
                                "Установлена сборка с читом: " + Path.GetFileName(dir),
                                "Папка версии · «" + client + "»",
                                dir);
                        }
                    }
                }

                // моды и конфиги этой сборки
                ScanModsRoot(root);

                // текущий лог — только этот запуск
                ScanLogs(root);
            }

            Progress(1);
        }

        private void StageVersionJars()
        {
            List<string> roots = MineRoots();
            for (int r = 0; r < roots.Count; r++)
            {
                token.ThrowIfCancellationRequested();
                string versions = Path.Combine(roots[r], "versions");
                if (!Directory.Exists(versions)) continue;

                string[] jars = SafeFiles(versions, "*.jar", SearchOption.AllDirectories);
                for (int i = 0; i < jars.Length; i++)
                {
                    token.ThrowIfCancellationRequested();
                    if ((i % 3) == 0) Progress((r + (double)i / Math.Max(1, jars.Length)) / Math.Max(1, roots.Count));

                    string jar = jars[i];
                    if (IsSelfPath(jar)) continue;
                    if (IsLegitName(Path.GetFileName(jar))) continue;

                    string why;
                    int score = ScoreJar(jar, out why);
                    report.JarsScanned++;
                    if (score >= 70)
                    {
                        AddScored(score, "Minecraft",
                            "Чит вшит в версию игры: " + Short(Path.GetFileName(jar), 56),
                            why,
                            jar);
                    }
                }
            }

            Progress(1);
        }

        private void ScanLogs(string root)
        {
            string dir = Path.Combine(root, "logs");
            if (!Directory.Exists(dir)) return;

            string latest = Path.Combine(dir, "latest.log");
            if (!File.Exists(latest)) return;

            DateTime written;
            try { written = File.GetLastWriteTime(latest); }
            catch { return; }
            if (written < sessionStart.AddMinutes(-2)) return; // лог прошлого запуска

            string text = ReadLog(latest, 2 * 1024 * 1024);
            if (text.Length == 0) return;

            string why;
            int score = ScoreText(text, out why) + 15; // лог текущего запуска — сильнее
            if (score >= 45)
            {
                AddScored(score, "Minecraft",
                    "Следы чита в логе текущего запуска",
                    why + " · latest.log",
                    latest);
            }
        }

        // -------------------------------------------------------------- 4. моды
        private void ScanModsRoot(string root)
        {
            string mods = Path.Combine(root, "mods");
            if (Directory.Exists(mods))
            {
                string[] files = SafeFiles(mods, "*.jar", SearchOption.TopDirectoryOnly);
                for (int i = 0; i < files.Length; i++)
                {
                    token.ThrowIfCancellationRequested();
                    if ((i % 4) == 0) Progress((double)i / Math.Max(1, files.Length));

                    string jar = files[i];
                    if (IsSelfPath(jar)) continue;
                    if (IsLegitName(Path.GetFileName(jar))) continue;

                    report.JarsScanned++;

                    string why;
                    int score = ScoreJar(jar, out why);
                    if (score >= 32)
                    {
                        AddScored(score, "Minecraft",
                            "Мод с признаками чита: " + Short(Path.GetFileName(jar), 52),
                            why,
                            jar);
                    }
                }
            }

            string config = Path.Combine(root, "config");
            if (Directory.Exists(config))
            {
                int inspected = 0;
                foreach (string file in SafeFiles(config, "*.*", SearchOption.AllDirectories))
                {
                    if (inspected++ > 600) break;
                    token.ThrowIfCancellationRequested();
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    if (Array.IndexOf(TextExt, ext) < 0) continue;
                    if (IsSelfPath(file)) continue;

                    string why;
                    int score = ScoreTextFile(file, out why);
                    if (score >= 45)
                    {
                        AddScored(score, "Minecraft",
                            "Конфиг чита: " + Short(Path.GetFileName(file), 52),
                            why,
                            file);
                    }
                }
            }

            Note("моды: " + Path.GetFileName(root) + " · проверено");
        }

        // -------------------------------------------------------------- 5. файлы
        private void StageFiles()
        {
            string[] roots =
            {
                Path.Combine(Home, "Downloads"),
                Path.Combine(Home, "Desktop"),
                Path.Combine(Home, "Documents"),
                Path.Combine(Home, "AppData", "Local", "Temp"),
                Path.Combine(Home, "AppData", "Roaming")
            };

            int scanned = 0;
            for (int r = 0; r < roots.Length; r++)
            {
                string root = roots[r];
                if (!Directory.Exists(root)) continue;
                token.ThrowIfCancellationRequested();

                foreach (string file in Walk(root, 3, ref scanned, 25000))
                {
                    token.ThrowIfCancellationRequested();
                    if (IsSelfPath(file)) continue;

                    string name = Path.GetFileName(file);
                    string low = name.ToLowerInvariant();
                    string ext = Path.GetExtension(low);

                    if (name.StartsWith("MINECHECK отчёт", StringComparison.OrdinalIgnoreCase)) continue;

                    // только то, что появилось/изменилось в этом запуске
                    if (!ModifiedThisSession(file)) continue;

                    // имя клиента ищем только у исполняемых/архивных файлов,
                    // чтобы не реагировать на скриншоты и обычные документы
                    string client = null;
                    if (Array.IndexOf(SuspiciousExt, ext) >= 0)
                        client = MatchClient(low) ?? MatchClient(Path.GetFileNameWithoutExtension(low));

                    if (client != null)
                    {
                        int score = 62;
                        if (ext == ".jar")
                        {
                            string why;
                            score = Math.Max(score, ScoreJar(file, out why));
                        }
                        AddScored(score, "Файлы",
                            "Скачан чит-файл: " + Short(name, 56),
                            "Появился в этом запуске · «" + client + "»",
                            file);
                        continue;
                    }

                    if (Array.IndexOf(TextExt, ext) >= 0 && scanned < 1500)
                    {
                        string why;
                        int score = ScoreTextFile(file, out why);
                        if (score >= 45)
                        {
                            AddScored(score, "Файлы",
                                "Текст с признаками чита: " + Short(name, 48),
                                why,
                                file);
                        }
                        continue;
                    }

                    // неизвестный софт: имя содержит явные маркеры чита
                    int generic = GenericHits(low);
                    bool strongWord = low.IndexOf("cheat", StringComparison.Ordinal) >= 0 ||
                                      low.IndexOf("чит", StringComparison.Ordinal) >= 0 ||
                                      low.IndexOf("killaura", StringComparison.Ordinal) >= 0 ||
                                      low.IndexOf("aimbot", StringComparison.Ordinal) >= 0 ||
                                      low.IndexOf("bypass", StringComparison.Ordinal) >= 0;
                    if (generic > 0 && (generic >= 2 || strongWord) &&
                        Array.IndexOf(SuspiciousExt, ext) >= 0)
                    {
                        int score = 42 + Math.Min(20, (generic - 1) * 8);
                        if (ext == ".jar")
                        {
                            string why;
                            score = Math.Max(score, ScoreJar(file, out why));
                        }
                        AddScored(score, "Файлы",
                            "Подозрительный файл: " + Short(name, 56),
                            "Появился в этом запуске · маркеры чита в имени",
                            file);
                    }
                }

                Progress((double)(r + 1) / roots.Length);
            }

            report.FilesScanned = scanned;
            Note("просмотрено файлов: " + scanned);
        }

        // ----------------------------------------------------- 5. недавние удаления
        private void StageRecentDeletions()
        {
            DateTime tenMinutes = DateTime.Now.AddMinutes(-10);
            ScanRecycleBin(tenMinutes, null, "Удаления", 12);
            Progress(1);
        }

        // ------------------------------------------------------------ 6. корзина
        private void StageRecycle()
        {
            DateTime tenMinutes = DateTime.Now.AddMinutes(-10);
            ScanRecycleBin(sessionStart, tenMinutes, "Корзина", 0);
            Progress(1);
        }

        private void ScanRecycleBin(DateTime from, DateTime? to, string category, int bonus)
        {
            List<string> roots = new List<string>();
            try
            {
                foreach (DriveInfo drive in DriveInfo.GetDrives())
                {
                    if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
                    roots.Add(Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin"));
                }
            }
            catch { }

            for (int r = 0; r < roots.Count; r++)
            {
                token.ThrowIfCancellationRequested();
                string root = roots[r];
                if (!Directory.Exists(root)) continue;

                foreach (string sid in SafeDirs(root))
                {
                    foreach (string meta in SafeFiles(sid, "$I*", SearchOption.TopDirectoryOnly))
                    {
                        token.ThrowIfCancellationRequested();
                        DateTime? deleted;
                        string original = RecycleOriginal(meta, out deleted);
                        if (original.Length == 0) continue;
                        if (!deleted.HasValue) continue;
                        if (deleted.Value < from) continue;
                        if (to.HasValue && deleted.Value >= to.Value) continue;

                        string low = original.ToLowerInvariant();
                        string client = MatchClient(low);
                        int mods = StrongModuleHits(low);

                        if (client == null && mods == 0)
                        {
                            // в этапе «недавние удаления» показываем любые стёртые исполняемые файлы
                            string ext = Path.GetExtension(low);
                            bool executable = ext == ".jar" || ext == ".exe" || ext == ".dll" ||
                                              ext == ".bat" || ext == ".cmd" || ext == ".ps1" ||
                                              ext == ".zip" || ext == ".rar" || ext == ".7z";
                            if (bonus > 0 && executable)
                            {
                                AddScored(35, category,
                                    "Недавно удалён файл: " + Short(Path.GetFileName(original), 52),
                                    "удалён " + Ago(deleted.Value) + " · проверьте, что это было",
                                    original);
                            }
                            continue;
                        }

                        bool recent = deleted.Value >= DateTime.Now.AddMinutes(-10);
                        int score = client != null ? 78 : (mods >= 2 ? 62 : 40);
                        if (recent) score += 8;
                        score += bonus;

                        string ago = Ago(deleted.Value);
                        Finding f = AddScored(score, category,
                            "Удалённый файл: " + Short(Path.GetFileName(original), 52),
                            (client != null ? "«" + client + "»" : "модули чита") +
                            " · удалён " + ago,
                            original);
                        if (f != null) f.Recent = recent;
                    }
                }
                Progress((double)(r + 1) / Math.Max(1, roots.Count));
            }
        }

        // ------------------------------------------------------------- 7. реестр
        private void StageRegistry()
        {
            TaskRegistryRuns();
            TaskRegistryUninstall();
            TaskRegistryUserAssist();
            TaskRegistryBam();
            Progress(1);
        }

        private void TaskRegistryRuns()
        {
            string[] keys =
            {
                @"Software\Microsoft\Windows\CurrentVersion\Run",
                @"Software\Microsoft\Windows\CurrentVersion\RunOnce"
            };

            foreach (string key in keys)
            {
                foreach (RegistryKey root in new RegistryKey[] { Registry.CurrentUser, Registry.LocalMachine })
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        using (RegistryKey rk = root.OpenSubKey(key))
                        {
                            if (rk == null) continue;
                            foreach (string name in rk.GetValueNames())
                            {
                                string value = Convert.ToString(rk.GetValue(name));
                                string client = MatchClient(name + " " + value);
                                if (client == null) continue;
                                Add(Severity.Critical, "Реестр",
                                    "Автозапуск чит-клиента: " + Short(name, 48),
                                    "«" + client + "» · " + Short(value, 110),
                                    key);
                            }
                        }
                    }
                    catch { }
                }
            }
        }

        private void TaskRegistryUninstall()
        {
            string[] paths =
            {
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall",
                @"Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (string path in paths)
            {
                token.ThrowIfCancellationRequested();
                foreach (RegistryKey root in new RegistryKey[] { Registry.CurrentUser, Registry.LocalMachine })
                {
                    try
                    {
                        using (RegistryKey rk = root.OpenSubKey(path))
                        {
                            if (rk == null) continue;
                            foreach (string sub in rk.GetSubKeyNames())
                            {
                                using (RegistryKey app = rk.OpenSubKey(sub))
                                {
                                    if (app == null) continue;
                                    string display = Convert.ToString(app.GetValue("DisplayName"));
                                    if (string.IsNullOrEmpty(display)) continue;
                                    string client = MatchClient(display);
                                    if (client == null) continue;
                                    Add(Severity.Critical, "Реестр",
                                        "Установлен чит-клиент: " + Short(display, 56),
                                        "Запись в списке программ",
                                        path + "\\" + sub);
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
        }

        private void TaskRegistryUserAssist()
        {
            const string basePath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist";

            try
            {
                using (RegistryKey root = Registry.CurrentUser.OpenSubKey(basePath))
                {
                    if (root == null) return;
                    foreach (string guid in root.GetSubKeyNames())
                    {
                        token.ThrowIfCancellationRequested();
                        using (RegistryKey count = root.OpenSubKey(guid + "\\Count"))
                        {
                            if (count == null) continue;
                            foreach (string rawName in count.GetValueNames())
                            {
                                string name = Rot13(rawName);
                                string client = MatchClient(name);
                                if (client == null) continue;

                                byte[] data = count.GetValue(rawName) as byte[];
                                DateTime? when = null;
                                if (data != null && data.Length >= 68)
                                    when = FileTime(data, 60);
                                if (!when.HasValue || when.Value < sessionStart) continue;

                                Add(Severity.Critical, "Реестр",
                                    "Запуск чит-клиента: " + Short(Path.GetFileName(name), 48),
                                    "UserAssist · «" + client + "» · " + Ago(when.Value),
                                    name);
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private void TaskRegistryBam()
        {
            if (!report.Elevated) return;

            try
            {
                string sid = System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
                using (RegistryKey rk = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Services\bam\State\UserSettings\" + sid))
                {
                    if (rk == null) return;
                    foreach (string name in rk.GetValueNames())
                    {
                        token.ThrowIfCancellationRequested();
                        string client = MatchClient(name);
                        if (client == null) continue;

                        byte[] data = rk.GetValue(name) as byte[];
                        if (data == null || data.Length < 8) continue;
                        DateTime? when = FileTime(data, 0);
                        if (!when.HasValue || when.Value < sessionStart) continue;

                        Add(Severity.Critical, "Реестр",
                            "Запуск программы: " + Short(Path.GetFileName(name), 48),
                            "BAM (журнал запусков) · " + Ago(when.Value),
                            name);
                    }
                }
            }
            catch { }
        }

        // ------------------------------------------------- 8. задачи и службы
        private void StageTasks()
        {
            TaskServices();
            TaskScheduledJobs();
            Progress(1);
        }

        private void TaskServices()
        {
            try
            {
                using (RegistryKey rk = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Services"))
                {
                    if (rk == null) return;
                    foreach (string name in rk.GetSubKeyNames())
                    {
                        token.ThrowIfCancellationRequested();
                        string client = MatchClient(name);
                        if (client == null) continue;

                        string image = "";
                        try
                        {
                            using (RegistryKey svc = rk.OpenSubKey(name))
                            {
                                if (svc != null) image = Convert.ToString(svc.GetValue("ImagePath"));
                            }
                        }
                        catch { }

                        Add(Severity.Critical, "Службы",
                            "Служба чит-клиента: " + Short(name, 50),
                            "«" + client + "» · " + Short(image, 110),
                            name);
                    }
                }
            }
            catch { }

            string tasks = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                                        "System32\\Tasks");
            if (!Directory.Exists(tasks)) return;

            try
            {
                int inspected = 0;
                foreach (string file in SafeFiles(tasks, "*", SearchOption.AllDirectories))
                {
                    if (inspected++ > 400) break;
                    token.ThrowIfCancellationRequested();
                    if (new FileInfo(file).Length > 256 * 1024) continue;

                    string text = ReadText(file, 256 * 1024);
                    if (text.Length == 0) continue;

                    string why;
                    int score = ScoreText(text, out why) + 10;
                    if (score >= 55)
                    {
                        AddScored(score, "Задачи",
                            "Задача автозапуска: " + Short(Path.GetFileName(file), 50),
                            why,
                            file);
                    }
                }
            }
            catch { }
        }

        private void TaskScheduledJobs()
        {
            // расписание проверяется в TaskServices через папку Tasks
        }

        // -------------------------------------------------------- 10. быстрый доступ
        private void StageJumpLists()
        {
            string recent = Path.Combine(Roaming, "Microsoft", "Windows", "Recent");
            string auto = Path.Combine(recent, "AutomaticDestinations");
            string custom = Path.Combine(recent, "CustomDestinations");

            ScanShortcuts(recent);
            ScanBinaryFolder(auto, "*.automaticDestinations-ms");
            ScanBinaryFolder(custom, "*.customDestinations-ms");

            Progress(1);
        }

        private void ScanShortcuts(string folder)
        {
            if (!Directory.Exists(folder)) return;
            foreach (string file in SafeFiles(folder, "*.lnk", SearchOption.TopDirectoryOnly))
            {
                token.ThrowIfCancellationRequested();
                DateTime written;
                try { written = File.GetLastWriteTime(file); }
                catch { continue; }
                if (written < sessionStart) continue;

                string name = Path.GetFileNameWithoutExtension(file);
                string client = MatchClient(name);
                if (client == null) continue;

                Add(Severity.Warning, "Журналы",
                    "Недавний ярлык: " + Short(name, 50),
                    "Recent · «" + client + "» · " + Ago(written),
                    file);
            }
        }

        private void ScanBinaryFolder(string folder, string pattern)
        {
            if (!Directory.Exists(folder)) return;
            foreach (string file in SafeFiles(folder, pattern, SearchOption.TopDirectoryOnly))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    DateTime written = File.GetLastWriteTime(file);
                    if (written < sessionStart) continue;
                    if (new FileInfo(file).Length > 4 * 1024 * 1024) continue;

                    byte[] data = File.ReadAllBytes(file);
                    HashSet<string> hits = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string s in ExtractStrings(data, 4))
                    {
                        string client = MatchClient(s);
                        if (client != null) hits.Add(client);
                    }
                    foreach (string hit in hits)
                    {
                        Add(Severity.Warning, "Журналы",
                            "След чит-клиента: " + hit,
                            "Jump list · " + Path.GetFileName(file) + " · " + Ago(written),
                            file);
                    }
                }
                catch { }
            }
        }

        // ------------------------------------------------------- 15. инжекторы
        private void StageInjectors()
        {
            string[] hints = { "injector", "inject", "payload", "dllhook", "jvmhook",
                               "processhacker", "extremeinjector", "xenos", "saga", "cheatengine" };

            Dictionary<int, string> commands = CommandLines();
            foreach (Process p in Process.GetProcesses())
            {
                bool relevant = false;
                try
                {
                    string name = p.ProcessName.ToLowerInvariant();
                    relevant = name == "java" || name == "javaw" || name.StartsWith("minecraft");
                }
                catch { }

                if (!relevant)
                {
                    p.Dispose();
                    continue;
                }

                try
                {
                    token.ThrowIfCancellationRequested();
                    foreach (ProcessModule m in p.Modules)
                    {
                        string file = "";
                        try { file = m.FileName ?? ""; } catch { }
                        if (file.Length == 0) continue;
                        string low = file.ToLowerInvariant();
                        if (!low.EndsWith(".dll")) continue;
                        if (IsSelfPath(file)) continue;
                        if (IsStandardJavaPath(low)) continue;
                        if (low.IndexOf("\\windows\\") >= 0) continue;

                        bool userFolder = low.IndexOf("\\temp\\") >= 0 ||
                                          low.IndexOf("\\downloads\\") >= 0 ||
                                          low.IndexOf("\\desktop\\") >= 0 ||
                                          low.IndexOf("\\appdata\\roaming\\") >= 0;

                        string hint = null;
                        foreach (string h in hints)
                            if (low.IndexOf(h, StringComparison.Ordinal) >= 0) { hint = h; break; }

                        if (hint != null)
                        {
                            Add(Severity.Critical, "Инжекторы",
                                "Инжектор подключён к процессу",
                                p.ProcessName + " · " + Path.GetFileName(file) + " · «" + hint + "»",
                                file);
                        }
                        else if (userFolder)
                        {
                            Add(Severity.Warning, "Инжекторы",
                                "Посторонняя DLL в Java-процессе",
                                p.ProcessName + " · " + Path.GetFileName(file),
                                file);
                        }
                    }
                }
                catch { }
                finally
                {
                    p.Dispose();
                }
            }

            // свежие DLL в Temp
            string temp = Path.GetTempPath();
            int checkedDlls = 0;
            foreach (string file in SafeFiles(temp, "*.dll", SearchOption.TopDirectoryOnly))
            {
                if (checkedDlls++ > 200) break;
                token.ThrowIfCancellationRequested();
                if (!ModifiedThisSession(file)) continue;

                string low = Path.GetFileName(file).ToLowerInvariant();
                string hint = null;
                foreach (string h in hints)
                    if (low.IndexOf(h, StringComparison.Ordinal) >= 0) { hint = h; break; }
                if (hint == null) continue;

                Add(Severity.Critical, "Инжекторы",
                    "Свежий инжектор во временной папке: " + Short(Path.GetFileName(file), 50),
                    "«" + hint + "» · создан в этом запуске",
                    file);
            }

            Progress(1);
        }

        // ----------------------------------------------------------- 9. prefetch
        private void StagePrefetch()
        {
            try
            {
                using (RegistryKey rk = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters"))
                {
                    if (rk != null)
                    {
                        object enabled = rk.GetValue("EnablePrefetcher");
                        if (enabled != null && Convert.ToInt32(enabled) == 0)
                            Add(Severity.Warning, "Prefetch", "Prefetch отключён",
                                "EnablePrefetcher = 0 — часто делается для сокрытия запусков", "HKLM");
                    }
                }
            }
            catch { }

            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch");
            if (!Directory.Exists(dir))
            {
                Note("Prefetch недоступен");
                Progress(1);
                return;
            }

            string[] files;
            try { files = Directory.GetFiles(dir, "*.pf"); }
            catch
            {
                Note("Prefetch недоступен без прав администратора");
                Progress(1);
                return;
            }

            int total = files.Length;
            for (int i = 0; i < total; i++)
            {
                token.ThrowIfCancellationRequested();
                if (i % 20 == 0) Progress((double)i / Math.Max(1, total));

                DateTime written;
                try { written = File.GetLastWriteTime(files[i]); }
                catch { continue; }
                if (written < sessionStart) continue;

                string name = Path.GetFileName(files[i]);
                int dash = name.LastIndexOf('-');
                string exe = dash > 0 ? name.Substring(0, dash) : Path.GetFileNameWithoutExtension(name);

                string client = MatchClient(exe);
                if (client != null)
                {
                    Add(Severity.Critical, "Prefetch",
                        "Запускался чит-клиент: " + Short(exe, 50),
                        "Prefetch · «" + client + "» · " + Ago(written),
                        files[i]);
                }
                else
                {
                    int generic = GenericHits(exe);
                    if (generic > 0 && !IsLegitName(exe))
                    {
                        Add(Severity.Warning, "Prefetch",
                            "Запускалась подозрительная программа: " + Short(exe, 48),
                            "Prefetch · маркеры чита в имени · " + Ago(written),
                            files[i]);
                    }
                }
            }

            Progress(1);
            Note("файлов Prefetch: " + total);
        }

        // ------------------------------------------------------------- 9. браузеры
        private void StageBrowsers()
        {
            List<string> historyFiles = new List<string>();

            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string roaming = Roaming;

            AddBrowserHistory(historyFiles, Path.Combine(local, "Google", "Chrome", "User Data"));
            AddBrowserHistory(historyFiles, Path.Combine(local, "Microsoft", "Edge", "User Data"));
            AddBrowserHistory(historyFiles, Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data"));
            AddBrowserHistory(historyFiles, Path.Combine(local, "Yandex", "YandexBrowser", "User Data"));

            string firefox = Path.Combine(roaming, "Mozilla", "Firefox", "Profiles");
            if (Directory.Exists(firefox))
            {
                foreach (string dir in SafeDirs(firefox))
                {
                    string places = Path.Combine(dir, "places.sqlite");
                    if (File.Exists(places)) historyFiles.Add(places);
                }
            }

            int hits = 0;
            for (int i = 0; i < historyFiles.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                Progress((double)(i + 1) / Math.Max(1, historyFiles.Count));
                hits += ScanBrowserDatabase(historyFiles[i], hits);
                if (hits >= 6) break;
            }

            Note("баз браузеров: " + historyFiles.Count);
        }

        private void AddBrowserHistory(List<string> list, string userData)
        {
            if (!Directory.Exists(userData)) return;
            foreach (string dir in SafeDirs(userData))
            {
                string history = Path.Combine(dir, "History");
                if (File.Exists(history)) list.Add(history);
            }
        }

        private int ScanBrowserDatabase(string file, int already)
        {
            int added = 0;
            string temp = null;
            try
            {
                temp = Path.Combine(Path.GetTempPath(), "minecheck_" + Guid.NewGuid().ToString("N") + ".db");
                File.Copy(file, temp, true);

                byte[] data = File.ReadAllBytes(temp);
                HashSet<string> found = new HashSet<string>();
                foreach (string s in ExtractStrings(data, 10))
                {
                    if (!s.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
                    string low = s.ToLowerInvariant();
                    foreach (string hint in WebHints)
                    {
                        if (low.IndexOf(hint, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        found.Add(Short(s, 130));
                        break;
                    }
                    if (found.Count >= 4) break;
                }

                foreach (string url in found)
                {
                    if (already + added >= 6) break;
                    added++;
                    Add(Severity.Info, "Браузеры",
                        "История браузера: посещение чит-сайта",
                        url + " · время посещения не проверялось",
                        file);
                }
            }
            catch { }
            finally
            {
                try { if (temp != null && File.Exists(temp)) File.Delete(temp); } catch { }
            }
            return added;
        }

        // --------------------------------------------------------------- 10. usb
        private void StageUsb()
        {
            try
            {
                using (RegistryKey rk = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Enum\USBSTOR"))
                {
                    if (rk == null) return;
                    List<string> devices = new List<string>();
                    foreach (string sub in rk.GetSubKeyNames())
                    {
                        token.ThrowIfCancellationRequested();
                        string name = sub.Replace("Disk&Ven_", "").Replace("&Prod_", " ").Replace("&Rev_", " · ");
                        devices.Add(Short(name, 60));
                    }
                    if (devices.Count > 0)
                    {
                        Add(Severity.Info, "Устройства",
                            "Подключалось USB-устройств: " + devices.Count,
                            Join(devices, "; "),
                            "HKLM\\SYSTEM\\CurrentControlSet\\Enum\\USBSTOR");
                    }
                }
            }
            catch { }

            Progress(1);
        }

        // -------------------------------------------------------------- 11. память
        private void StageMemory()
        {
            List<Process> targets = new List<Process>();

            foreach (Process p in Process.GetProcesses())
            {
                bool take = false;
                try
                {
                    string name = p.ProcessName.ToLowerInvariant();
                    // любой Java-процесс проверяем полностью
                    take = name == "java" || name == "javaw" || name.StartsWith("minecraft") ||
                           name == "lunarclient" || name == "badlionclient";
                }
                catch { }

                if (take) targets.Add(p);
                else p.Dispose();
            }

            if (targets.Count == 0)
            {
                Note("Java-процессы не запущены");
                Progress(1);
                return;
            }

            string[] patterns = new string[]
            {
                "catlavan", "doomsday", "liquidbounce", "wurst", "novoline", "konas",
                "astolfo", "aristois", "bleachhack", "salhack", "phobos", "exhibition",
                "gamesense", "jigsaw", "rusherhack", "tenacity", "dortware", "vape",
                "mhub", "systemdlc", "cortex", "impactclient", "futureclient",
                "nursultan", "akrien", "koks", "spooky", "killaura", "triggerbot",
                "aimbot", "freecam", "xray", "scaffold", "nuker", "antiknockback",
                "itemesp", "chestesp", "silentaura", "autototem", "cheststealer",
                "backtrack", "disabler", "antiaim", "flyhack", "speedhack", "autoclicker"
            };

            for (int t = 0; t < targets.Count; t++)
            {
                token.ThrowIfCancellationRequested();
                Process p = targets[t];
                try
                {
                    // Java/ Minecraft — большой бюджет и полный обход приватной памяти
                    Dictionary<string, int> hits = ScanProcessMemory(p, patterns, 768L * 1024 * 1024, 15);
                    if (hits.Count > 0)
                    {
                        List<string> list = new List<string>();
                        int clients = 0;
                        foreach (KeyValuePair<string, int> pair in hits)
                        {
                            list.Add(pair.Key);
                            if (MatchClient(pair.Key) != null) clients++;
                        }

                        string detail = "Процесс " + p.ProcessName + " (PID " + p.Id + ") · сигнатуры: " + Join(list, ", ");
                        if (clients >= 2 || (clients >= 1 && list.Count >= 4))
                        {
                            Add(Severity.Critical, "Память",
                                "Чит-модули в памяти процесса",
                                detail + " · требует подтверждения",
                                "PID " + p.Id);
                        }
                        else
                        {
                            Add(Severity.Warning, "Память",
                                "Сигнатура чита в памяти процесса",
                                detail,
                                "PID " + p.Id);
                        }
                    }
                }
                catch { }
                finally
                {
                    p.Dispose();
                }
                Progress((double)(t + 1) / targets.Count);
            }
        }

        private Dictionary<string, int> ScanProcessMemory(Process p, string[] patterns, long budget, int seconds)
        {
            Dictionary<string, int> hits = new Dictionary<string, int>();
            IntPtr handle = OpenProcess(0x0410, false, p.Id);
            if (handle == IntPtr.Zero) return hits;

            try
            {
                long address = 0;
                long max = 0x7FFFFFFF0000L;
                byte[] buffer = new byte[64 * 1024];
                DateTime started = DateTime.UtcNow;

                while (address < max && budget > 0)
                {
                    token.ThrowIfCancellationRequested();
                    if ((DateTime.UtcNow - started).TotalSeconds > seconds) break;

                    MEMORY_BASIC_INFORMATION info;
                    if (VirtualQueryEx(handle, new IntPtr(address), out info, (IntPtr)Marshal.SizeOf(typeof(MEMORY_BASIC_INFORMATION))) == 0)
                        break;

                    long regionSize = (long)info.RegionSize;
                    if (regionSize <= 0) break;

                    // частная память и отображённые файлы: там живут классы и строки Jar
                    bool readable = info.State == 0x1000 &&
                                    (info.Type == 0x20000 || info.Type == 0x40000) &&
                                    (info.Protect == 0x04 || info.Protect == 0x02 ||
                                     info.Protect == 0x20 || info.Protect == 0x40 ||
                                     info.Protect == 0x80 || info.Protect == 0x08);

                    bool enough = false;

                    if (readable)
                    {
                        long offset = 0;
                        while (offset < regionSize && budget > 0)
                        {
                            if ((DateTime.UtcNow - started).TotalSeconds > seconds) break;

                            int chunk = (int)Math.Min(buffer.Length, regionSize - offset);
                            IntPtr read;
                            if (ReadProcessMemory(handle, new IntPtr(address + offset), buffer, new IntPtr(chunk), out read))
                            {
                                int got = read.ToInt32();
                                ScanChunk(buffer, got, patterns, hits);
                                budget -= got;

                                if (HasClientSignature(hits) && hits.Count >= 4) { enough = true; break; }
                            }
                            offset += chunk;
                        }
                    }

                    if (enough) break;

                    address += regionSize;
                }
            }
            finally
            {
                CloseHandle(handle);
            }

            return hits;
        }

        private static void ScanChunk(byte[] buffer, int length, string[] patterns, Dictionary<string, int> hits)
        {
            if (length <= 8) return;
            string ascii = Encoding.ASCII.GetString(buffer, 0, length);

            foreach (string pattern in patterns)
            {
                if (hits.ContainsKey(pattern)) continue;
                if (ascii.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) < 0) continue;
                hits[pattern] = 1;
            }
        }

        private static bool HasClientSignature(Dictionary<string, int> hits)
        {
            foreach (string key in hits.Keys)
                if (MatchClient(key) != null) return true;
            return false;
        }

        // ------------------------------------------------------- проверка находок
        private void VerifyFindings()
        {
            List<Finding> snapshot;
            lock (sync) snapshot = new List<Finding>(report.Findings);

            int checkedCount = 0;
            foreach (Finding f in snapshot)
            {
                token.ThrowIfCancellationRequested();
                if (checkedCount >= 12) break;
                if (string.IsNullOrEmpty(f.Path)) continue;
                if (!File.Exists(f.Path)) continue;
                if (Path.GetExtension(f.Path).ToLowerInvariant() != ".jar") continue;
                if (IsSelfPath(f.Path)) continue;
                checkedCount++;

                string why;
                int score = ScoreJar(f.Path, out why);
                if (score >= 70)
                {
                    f.Level = Severity.Critical;
                    f.Score = score;
                    f.Confirmed = true;
                    if (f.Detail.IndexOf("подтверждено", StringComparison.OrdinalIgnoreCase) < 0)
                        f.Detail += " · подтверждено повторной проверкой";
                }
                else if (score < 45)
                {
                    f.Level = Severity.Info;
                    f.Score = score;
                    f.Confirmed = false;
                    f.Detail += " · повторная проверка не подтвердила";
                }
            }
        }

        // ------------------------------------------------------------ сканирование
        private int ScoreJar(string jar, out string detail)
        {
            detail = "";
            JarEvidence ev = InspectJar(jar);
            string file = Path.GetFileName(jar).ToLowerInvariant();

            if (IsLegitName(file)) return 0;
            if (ev.Error != null) return 0;

            List<string> parts = new List<string>();
            int score = 0;

            string inName = MatchClient(file);
            string weak = MatchWeakClient(Path.GetFileNameWithoutExtension(file));

            if (inName != null) { score += 45; parts.Add("имя клиента «" + inName + "»"); }
            else if (weak != null) { score += 18; parts.Add("похоже на клиент «" + weak + "»"); }

            if (ev.Clients.Count > 0)
            {
                score += Math.Min(45, 22 + ev.Clients.Count * 8);
                parts.Add("пакеты клиента: " + Join(ev.Clients, ", "));
            }

            if (ev.Manifest.Length > 0)
            {
                string manifestClient = MatchClient(ev.Manifest);
                if (manifestClient != null)
                {
                    score += 25;
                    parts.Add("манифест: «" + manifestClient + "»");
                }
            }

            if (ev.MetadataClient != null)
            {
                score += 35;
                parts.Add("метаданные мода: «" + ev.MetadataClient + "»");
            }

            if (ev.HasAgent)
            {
                score += 32;
                parts.Add("java-агент (Premain/Agent-Class)");
            }

            if (ev.Panic)
            {
                score += 14;
                parts.Add("паник-бинд");
            }

            int generic = GenericHits(file);
            if (generic > 0)
            {
                score += Math.Min(24, 18 + (generic - 1) * 4);
                parts.Add("подозрительное имя");
            }

            int mods = ev.Modules.Count;
            if (mods >= 6) score += 48;
            else if (mods == 5) score += 42;
            else if (mods == 4) score += 34;
            else if (mods == 3) score += 26;
            else if (mods == 2) score += 16;
            else if (mods == 1) score += 5;
            if (mods > 0) parts.Add("модули: " + Join(ev.Modules, ", "));

            // один модуль без других признаков — не чит
            if (mods <= 1 && ev.Clients.Count == 0 && inName == null && ev.MetadataClient == null)
                score = Math.Min(score, 30);

            detail = Join(parts, " · ");
            return score;
        }

        private int ScoreTextFile(string file, out string detail)
        {
            detail = "";
            try
            {
                if (new FileInfo(file).Length > 512 * 1024) return 0;
                string text = ReadText(file, 512 * 1024);
                if (text.Length < 6) return 0;
                return ScoreText(text, out detail);
            }
            catch { return 0; }
        }

        private int ScoreText(string text, out string detail)
        {
            detail = "";
            string low = text.ToLowerInvariant();

            string client = MatchClientStrict(low);
            HashSet<string> mods = new HashSet<string>();
            foreach (string m in StrongModules)
                if (low.IndexOf(m, StringComparison.Ordinal) >= 0) mods.Add(m);

            bool panic = false;
            foreach (string key in PanicKeys)
                if (low.IndexOf(key, StringComparison.Ordinal) >= 0) { panic = true; break; }

            int score = 0;
            List<string> parts = new List<string>();

            if (client != null) { score += 55; parts.Add("клиент «" + client + "»"); }

            if (mods.Count >= 4) score += 42;
            else if (mods.Count == 3) score += 32;
            else if (mods.Count == 2) score += 20;
            else if (mods.Count == 1) score += 6;

            if (mods.Count > 0) parts.Add("модули: " + Join(mods, ", "));
            if (panic) { score += 12; parts.Add("паник-бинд"); }

            int generic = GenericHits(low);
            if (generic > 0) { score += Math.Min(16, generic * 8); parts.Add("слова-маркеры чита"); }

            if (mods.Count < 2 && !panic) score = Math.Min(score, 30);

            detail = Join(parts, " · ");
            return score;
        }

        // ------------------------------------------------------------- утилиты
        private DateTime DetectSessionStart()
        {
            DateTime boot = DateTime.Now - TimeSpan.FromMilliseconds(Environment.TickCount);
            DateTime gameStart = DateTime.MaxValue;

            try
            {
                foreach (Process p in Process.GetProcesses())
                {
                    try
                    {
                        string name = p.ProcessName.ToLowerInvariant();
                        bool game = name == "javaw" || name == "java" ||
                                     name.StartsWith("minecraft") || name == "lunarclient" ||
                                     name == "badlionclient" || name == "tlauncher";
                        if (!game) continue;

                        DateTime t = p.StartTime;
                        if (t < gameStart) gameStart = t;
                    }
                    catch { }
                    finally { p.Dispose(); }
                }
            }
            catch { }

            if (gameStart != DateTime.MaxValue) return gameStart;

            // игра не запущена — берём загрузку системы, но не глубже 8 часов
            DateTime floor = DateTime.Now.AddHours(-8);
            return boot < floor ? floor : boot;
        }

        private bool ModifiedThisSession(string file)
        {
            try
            {
                DateTime t = File.GetLastWriteTime(file);
                if (t >= sessionStart) return true;
                DateTime c = File.GetCreationTime(file);
                return c >= sessionStart;
            }
            catch { return false; }
        }

        private bool IsSelfPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string low = path.ToLowerInvariant();

            if (low.IndexOf("minecheck", StringComparison.Ordinal) >= 0) return true;
            if (selfDir.Length > 2 && low.StartsWith(selfDir.ToLowerInvariant(), StringComparison.Ordinal)) return true;
            if (low.IndexOf("\\opencode\\", StringComparison.Ordinal) >= 0) return true;

            return false;
        }

        private static bool IsLegitName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string low = name.ToLowerInvariant();
            foreach (string legit in Legit)
                if (low.IndexOf(legit, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        private static string MatchClient(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            string low = text.ToLowerInvariant();
            foreach (string client in Clients)
                if (low.IndexOf(client, StringComparison.Ordinal) >= 0) return client;
            return null;
        }

        /// <summary>Поиск названия клиента целым словом — для текстов и логов.</summary>
        private static string MatchClientStrict(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            string low = text.ToLowerInvariant();

            foreach (string client in Clients)
            {
                int index = 0;
                while ((index = low.IndexOf(client, index, StringComparison.Ordinal)) >= 0)
                {
                    bool leftOk = index == 0 || !char.IsLetter(low[index - 1]);
                    int end = index + client.Length;
                    bool rightOk = end >= low.Length || !char.IsLetter(low[end]);
                    if (leftOk && rightOk) return client;
                    index = end;
                }
            }
            return null;
        }

        private static string MatchWeakClient(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            string low = text.ToLowerInvariant();

            foreach (string client in WeakClients)
            {
                int index = 0;
                while ((index = low.IndexOf(client, index, StringComparison.Ordinal)) >= 0)
                {
                    bool leftOk = index == 0 || !char.IsLetter(low[index - 1]);
                    int end = index + client.Length;
                    bool rightOk = end >= low.Length || !char.IsLetter(low[end]);
                    if (leftOk && rightOk) return client;
                    index = end;
                }
            }
            return null;
        }

        private static int StrongModuleHits(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            string low = text.ToLowerInvariant();
            int count = 0;
            foreach (string module in StrongModules)
                if (low.IndexOf(module, StringComparison.Ordinal) >= 0) count++;
            return count;
        }

        private static int GenericHits(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            string low = text.ToLowerInvariant();
            int count = 0;
            foreach (string hint in GenericHints)
                if (low.IndexOf(hint, StringComparison.Ordinal) >= 0) count++;
            return count;
        }

        private static Dictionary<int, string> CommandLines()
        {
            Dictionary<int, string> result = new Dictionary<int, string>();
            try
            {
                using (ManagementObjectSearcher searcher =
                       new ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process"))
                {
                    foreach (ManagementBaseObject item in searcher.Get())
                    {
                        try
                        {
                            int pid = Convert.ToInt32(item["ProcessId"]);
                            string line = item["CommandLine"] as string;
                            if (!string.IsNullOrEmpty(line)) result[pid] = line;
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return result;
        }

        private static List<string> MineRoots()
        {
            List<string> roots = new List<string>();
            AddRoot(roots, Path.Combine(Roaming, ".minecraft"));
            AddRoot(roots, Path.Combine(Home, ".minecraft"));
            AddRoot(roots, Path.Combine(Roaming, ".tlauncher"));
            AddRoot(roots, Path.Combine(Roaming, ".lunarclient"));
            AddRoot(roots, Path.Combine(Roaming, "com.modrinth.theseus"));
            AddRoot(roots, Path.Combine(Roaming, "PrismLauncher"));
            AddRoot(roots, Path.Combine(Roaming, ".minecraft", "versions"));
            return roots;
        }

        private static string Roaming
        {
            get { return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); }
        }

        private static string Home
        {
            get { return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); }
        }

        private static void AddRoot(List<string> roots, string path)
        {
            if (Directory.Exists(path) && !roots.Contains(path)) roots.Add(path);
        }

        private static string[] SafeDirs(string path)
        {
            try { return Directory.GetDirectories(path); }
            catch { return new string[0]; }
        }

        private static string[] SafeFiles(string path, string pattern, SearchOption option)
        {
            try { return Directory.GetFiles(path, pattern, option); }
            catch { return new string[0]; }
        }

        private static IEnumerable<string> Walk(string root, int depth, ref int scanned, int cap)
        {
            List<string> result = new List<string>();
            Queue<KeyValuePair<string, int>> queue = new Queue<KeyValuePair<string, int>>();
            queue.Enqueue(new KeyValuePair<string, int>(root, 0));

            while (queue.Count > 0)
            {
                KeyValuePair<string, int> item = queue.Dequeue();
                string dir = item.Key;
                int level = item.Value;

                string[] files;
                try { files = Directory.GetFiles(dir); }
                catch { continue; }

                foreach (string file in files)
                {
                    result.Add(file);
                    scanned++;
                    if (result.Count >= cap) return result;
                }

                if (level >= depth) continue;

                string[] subs;
                try { subs = Directory.GetDirectories(dir); }
                catch { continue; }

                foreach (string sub in subs)
                {
                    string low = sub.ToLowerInvariant();
                    if (low.Contains("\\windows\\") || low.Contains("\\node_modules") ||
                        low.Contains("\\$recycle.bin") || low.Contains("\\.git\\") ||
                        low.Contains("\\packages\\") || low.Contains("\\steamapps\\") ||
                        low.Contains("\\.minecraft\\mods") || low.Contains("\\.minecraft\\versions") ||
                        low.Contains("\\.minecraft\\libraries") || low.Contains("\\.minecraft\\assets") ||
                        low.Contains("\\.minecraft\\logs") || low.Contains("\\.minecraft\\saves"))
                        continue;
                    queue.Enqueue(new KeyValuePair<string, int>(sub, level + 1));
                }
            }

            return result;
        }

        private sealed class JarEvidence
        {
            public readonly HashSet<string> Clients = new HashSet<string>();
            public readonly HashSet<string> Modules = new HashSet<string>();
            public string Manifest = "";
            public string MetadataClient;
            public bool HasAgent;
            public bool Panic;
            public string Error;
        }

        private JarEvidence InspectJar(string jar)
        {
            JarEvidence ev = new JarEvidence();
            try
            {
                using (ZipArchive archive = ZipFile.OpenRead(jar))
                {
                    int entries = 0;
                    int textEntries = 0;
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        if (++entries > 8000) break;
                        if ((entries % 250) == 0) token.ThrowIfCancellationRequested();

                        string name = entry.FullName;
                        if (name.Length == 0) continue;
                        string low = name.ToLowerInvariant();

                        foreach (string client in Clients)
                            if (low.IndexOf(client, StringComparison.Ordinal) >= 0) ev.Clients.Add(client);

                        foreach (string module in StrongModules)
                            if (low.IndexOf(module, StringComparison.Ordinal) >= 0) ev.Modules.Add(module);

                        foreach (string key in PanicKeys)
                            if (low.IndexOf(key, StringComparison.Ordinal) >= 0) { ev.Panic = true; break; }

                        if (name.EndsWith("MANIFEST.MF", StringComparison.OrdinalIgnoreCase) && ev.Manifest.Length == 0)
                        {
                            ev.Manifest = ReadZipEntry(entry, 4096);
                            if (ev.Manifest.IndexOf("Premain-Class", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                ev.Manifest.IndexOf("Agent-Class", StringComparison.OrdinalIgnoreCase) >= 0)
                                ev.HasAgent = true;
                        }

                        bool metadata = name.EndsWith("fabric.mod.json", StringComparison.OrdinalIgnoreCase) ||
                                        name.EndsWith("mcmod.info", StringComparison.OrdinalIgnoreCase) ||
                                        name.EndsWith("mods.toml", StringComparison.OrdinalIgnoreCase) ||
                                        name.EndsWith("quilt.mod.json", StringComparison.OrdinalIgnoreCase);

                        bool config = textEntries < 60 && entry.Length > 0 && entry.Length < 65536 &&
                                      (low.EndsWith(".json") || low.EndsWith(".toml") || low.EndsWith(".yml") ||
                                       low.EndsWith(".yaml") || low.EndsWith(".cfg") || low.EndsWith(".txt") ||
                                       low.EndsWith(".properties"));

                        if (metadata || config)
                        {
                            if (metadata) textEntries++;
                            string content = ReadZipEntry(entry, 8192);
                            if (content.Length == 0) continue;
                            string text = content.ToLowerInvariant();

                            string metaClient = MatchClientStrict(text);
                            if (metaClient != null)
                            {
                                ev.Clients.Add(metaClient);
                                if (metadata && ev.MetadataClient == null) ev.MetadataClient = metaClient;
                            }

                            foreach (string module in StrongModules)
                                if (text.IndexOf(module, StringComparison.Ordinal) >= 0) ev.Modules.Add(module);

                            foreach (string key in PanicKeys)
                                if (text.IndexOf(key, StringComparison.Ordinal) >= 0) { ev.Panic = true; break; }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ev.Error = ex.Message;
            }
            return ev;
        }

        private static string ReadZipEntry(ZipArchiveEntry entry, int max)
        {
            try
            {
                using (Stream stream = entry.Open())
                {
                    byte[] buffer = new byte[max];
                    int read = stream.Read(buffer, 0, buffer.Length);
                    return Encoding.UTF8.GetString(buffer, 0, read);
                }
            }
            catch { return ""; }
        }

        private static string ProcessPath(Process p)
        {
            try { return p.MainModule.FileName ?? ""; }
            catch { return ""; }
        }

        private static string ReadText(string path, int maxBytes)
        {
            try
            {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    int length = (int)Math.Min(stream.Length, maxBytes);
                    byte[] data = new byte[length];
                    int read = stream.Read(data, 0, length);
                    return Encoding.UTF8.GetString(data, 0, read);
                }
            }
            catch { return ""; }
        }

        private static string ReadLog(string path, int maxBytes)
        {
            try
            {
                if (path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                {
                    using (FileStream stream = File.OpenRead(path))
                    using (GZipStream gzip = new GZipStream(stream, CompressionMode.Decompress))
                    using (StreamReader reader = new StreamReader(gzip, Encoding.UTF8))
                    {
                        char[] buffer = new char[maxBytes];
                        int read = reader.Read(buffer, 0, buffer.Length);
                        return new string(buffer, 0, read);
                    }
                }
                return ReadText(path, maxBytes);
            }
            catch { return ""; }
        }

        private static List<string> ExtractStrings(byte[] data, int minLength)
        {
            List<string> result = new List<string>();
            StringBuilder current = new StringBuilder();

            for (int i = 0; i < data.Length; i++)
            {
                byte b = data[i];
                if (b >= 32 && b < 127) current.Append((char)b);
                else
                {
                    if (current.Length >= minLength && current.Length < 400) result.Add(current.ToString());
                    current.Length = 0;
                }
            }
            if (current.Length >= minLength && current.Length < 400) result.Add(current.ToString());

            current.Length = 0;
            for (int i = 0; i + 1 < data.Length; i += 2)
            {
                char c = (char)(data[i] | (data[i + 1] << 8));
                if (c >= 32 && c < 0x5000 && c != 0xFFFF) current.Append(c);
                else
                {
                    if (current.Length >= minLength && current.Length < 400) result.Add(current.ToString());
                    current.Length = 0;
                }
            }
            if (current.Length >= minLength && current.Length < 400) result.Add(current.ToString());

            return result;
        }

        private static string RecycleOriginal(string metaFile, out DateTime? deleted)
        {
            deleted = null;
            try
            {
                byte[] data = File.ReadAllBytes(metaFile);
                if (data.Length < 32) return "";

                if (data.Length >= 24) deleted = FileTime(data, 16);

                int start = 28;
                int len = BitConverter.ToInt32(data, 24);
                if (len > 0 && start + len <= data.Length)
                {
                    string s = Encoding.Unicode.GetString(data, start, len).TrimEnd('\0');
                    if (s.Length > 2 && s[1] == ':') return s;
                }

                foreach (string candidate in ExtractStrings(data, 4))
                {
                    if (candidate.Length > 3 && candidate[1] == ':') return candidate.TrimEnd('\0');
                }
            }
            catch { }
            return "";
        }

        private static DateTime? FileTime(byte[] data, int offset)
        {
            try
            {
                if (offset + 8 > data.Length) return null;
                long value = BitConverter.ToInt64(data, offset);
                if (value <= 0) return null;
                return DateTime.FromFileTimeUtc(value).ToLocalTime();
            }
            catch { return null; }
        }

        private static string Ago(DateTime time)
        {
            TimeSpan span = DateTime.Now - time;
            if (span.TotalMinutes < 1) return "только что";
            if (span.TotalMinutes < 60) return ((int)span.TotalMinutes) + " мин назад";
            if (span.TotalHours < 24) return ((int)span.TotalHours) + " ч назад";
            return time.ToString("dd.MM HH:mm");
        }

        private static string Rot13(string value)
        {
            char[] chars = value.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (c >= 'a' && c <= 'z') chars[i] = (char)('a' + (c - 'a' + 13) % 26);
                else if (c >= 'A' && c <= 'Z') chars[i] = (char)('A' + (c - 'A' + 13) % 26);
            }
            return new string(chars);
        }

        private static string Join(IEnumerable<string> items, string separator)
        {
            StringBuilder sb = new StringBuilder();
            foreach (string item in items)
            {
                if (sb.Length > 0) sb.Append(separator);
                sb.Append(item);
                if (sb.Length > 360) break;
            }
            return sb.ToString();
        }

        private static string Short(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return text.Length <= max ? text : text.Substring(0, max - 1) + "…";
        }

        private static bool IsAdmin()
        {
            try
            {
                using (System.Security.Principal.WindowsIdentity identity =
                       System.Security.Principal.WindowsIdentity.GetCurrent())
                {
                    System.Security.Principal.WindowsPrincipal principal =
                        new System.Security.Principal.WindowsPrincipal(identity);
                    return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
                }
            }
            catch { return false; }
        }

        // ----------------------------------------------------------- приложения 24ч
        private void StageRecentApps()
        {
            DateTime cutoff = DateTime.Now.AddHours(-24);
            List<RecentApp> apps = new List<RecentApp>();

            // UserAssist (расшифрованное)
            apps.AddRange(GetUserAssistApps(cutoff));

            // Prefetch
            apps.AddRange(GetPrefetchApps(cutoff));

            // Recent documents
            apps.AddRange(GetRecentDocs(cutoff));

            // Run history
            apps.AddRange(GetRunHistory(cutoff));

            // Сортируем по времени
            apps.Sort((a, b) => b.Time.CompareTo(a.Time));

            // Убираем дубликаты (одинаковые EXE)
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<RecentApp> unique = new List<RecentApp>();
            foreach (RecentApp app in apps)
            {
                string key = app.Name.ToLowerInvariant();
                if (seen.Add(key))
                    unique.Add(app);
            }

            // Показываем топ 50
            int shown = Math.Min(50, unique.Count);
            for (int i = 0; i < shown; i++)
            {
                RecentApp app = unique[i];
                
                // Проверяем на чит-клиент
                string client = MatchClient(app.Name) ?? MatchWeakClient(app.Name);
                if (client != null)
                {
                    Add(Severity.Critical, "Приложения 24ч",
                        "Чит-клиент запускался: " + Short(app.Name, 60),
                        "Время: " + app.Time.ToString("dd.MM HH:mm") + " · " + client,
                        app.Path);
                }
                else if (i < 20) // Показываем первые 20 как Info
                {
                    Add(Severity.Info, "Приложения 24ч",
                        "Запускалось: " + Short(app.Name, 60),
                        "Время: " + app.Time.ToString("dd.MM HH:mm"),
                        app.Path);
                }
            }

            Note("найдено приложений за 24ч: " + unique.Count);
            Progress(1);
        }

        private struct RecentApp
        {
            public string Name;
            public string Path;
            public DateTime Time;
        }

        private List<RecentApp> GetUserAssistApps(DateTime cutoff)
        {
            List<RecentApp> result = new List<RecentApp>();
            try
            {
                using (RegistryKey root = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist"))
                {
                    if (root == null) return result;

                    foreach (string guid in root.GetSubKeyNames())
                    {
                        token.ThrowIfCancellationRequested();
                        using (RegistryKey count = root.OpenSubKey(guid + "\\Count"))
                        {
                            if (count == null) continue;

                            foreach (string rawName in count.GetValueNames())
                            {
                                string name = Rot13(rawName);
                                byte[] data = count.GetValue(rawName) as byte[];

                                if (data == null || data.Length < 68) continue;
                                DateTime? time = FileTime(data, 60);
                                if (time == null || time < cutoff) continue;

                                result.Add(new RecentApp
                                {
                                    Name = Path.GetFileName(name),
                                    Path = name,
                                    Time = time.Value
                                });
                            }
                        }
                    }
                }
            }
            catch { }
            return result;
        }

        private List<RecentApp> GetPrefetchApps(DateTime cutoff)
        {
            List<RecentApp> result = new List<RecentApp>();
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch");
            
            if (!Directory.Exists(dir))
            {
                Note("Prefetch недоступен");
                return result;
            }

            try
            {
                foreach (string file in Directory.GetFiles(dir, "*.pf"))
                {
                    token.ThrowIfCancellationRequested();
                    FileInfo fi = new FileInfo(file);
                    if (fi.LastWriteTime < cutoff) continue;

                    string name = Path.GetFileName(file);
                    int dash = name.LastIndexOf('-');
                    string exe = dash > 0 ? name.Substring(0, dash) : Path.GetFileNameWithoutExtension(name);

                    result.Add(new RecentApp
                    {
                        Name = exe + ".exe",
                        Path = file,
                        Time = fi.LastWriteTime
                    });
                }
            }
            catch { }

            return result;
        }

        private List<RecentApp> GetRecentDocs(DateTime cutoff)
        {
            List<RecentApp> result = new List<RecentApp>();
            string recent = Path.Combine(Roaming, "Microsoft", "Windows", "Recent");

            if (!Directory.Exists(recent)) return result;

            try
            {
                foreach (string file in Directory.GetFiles(recent, "*.lnk"))
                {
                    token.ThrowIfCancellationRequested();
                    FileInfo fi = new FileInfo(file);
                    if (fi.LastWriteTime < cutoff) continue;

                    result.Add(new RecentApp
                    {
                        Name = Path.GetFileNameWithoutExtension(file),
                        Path = file,
                        Time = fi.LastWriteTime
                    });
                }
            }
            catch { }

            return result;
        }

        private List<RecentApp> GetRunHistory(DateTime cutoff)
        {
            List<RecentApp> result = new List<RecentApp>();
            try
            {
                using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Explorer\RunMRU"))
                {
                    if (rk == null) return result;

                    foreach (string name in rk.GetValueNames())
                    {
                        if (name == "MRUList") continue;
                        string value = Convert.ToString(rk.GetValue(name));
                        if (string.IsNullOrEmpty(value)) continue;

                        value = value.TrimEnd('\\', '\0');
                        
                        result.Add(new RecentApp
                        {
                            Name = Path.GetFileNameWithoutExtension(value),
                            Path = value,
                            Time = DateTime.Now // Точное время недоступно
                        });
                    }
                }
            }
            catch { }

            return result;
        }

        // ------------------------------------------------------------------ Win32
        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORY_BASIC_INFORMATION
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public uint AllocationProtect;
            public IntPtr RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }

        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32.dll")]
        private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, IntPtr size, out IntPtr read);

        [DllImport("kernel32.dll")]
        private static extern int VirtualQueryEx(IntPtr process, IntPtr address, out MEMORY_BASIC_INFORMATION info, IntPtr length);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
