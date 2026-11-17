using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ZapretOBKHOD
{
    internal sealed class Preset
    {
        public string Id;
        public string Name;
        public string Description;
        public string[] Lists;
        public bool AllSites;
        public bool Soft;
        public bool Voice;
        // Добавляет после профилей со списком доменов общий профиль HTTPS/QUIC.
        // Нужен для Telegram: он соединяется по IP-адресам, и SNI в пакетах нет.
        public bool Fallback;

        public Preset(string id, string name, string description, string[] lists,
            bool allSites, bool soft, bool voice, bool fallback)
        {
            Id = id;
            Name = name;
            Description = description;
            Lists = lists;
            AllSites = allSites;
            Soft = soft;
            Voice = voice;
            Fallback = fallback;
        }
    }

    internal static class PresetCatalog
    {
        public static readonly Preset[] All = new Preset[]
        {
            new Preset("combo", "Комбо: YouTube + Discord + Telegram",
                "Рекомендуется для большинства провайдеров. YouTube и Discord обходятся по доменам, Telegram — общим профилем HTTPS и неизвестных протоколов (он работает по IP). Банки и госуслуги не затрагиваются.",
                new string[] { "youtube.txt", "discord.txt", "telegram.txt" }, false, false, true, true),
            new Preset("youtube", "Только YouTube",
                "YouTube и Google Video. Минимальное вмешательство в остальной трафик.",
                new string[] { "youtube.txt" }, false, false, false, false),
            new Preset("discord", "Только Discord",
                "Сайт Discord, клиент, вложения и голосовые каналы. Дополнительно обрабатывает остальной HTTPS, кроме исключений.",
                new string[] { "discord.txt" }, false, false, true, true),
            new Preset("telegram", "Telegram + весь HTTPS",
                "Telegram не передаёт домен в открытом виде, поэтому профиль обрабатывает весь HTTPS и неизвестные протоколы, кроме списка исключений.",
                null, true, false, false, false),
            new Preset("media", "Соцсети и медиа",
                "Instagram, Facebook, X/Twitter, Rutracker, Spotify, SoundCloud и другие медиа-сервисы.",
                new string[] { "media.txt" }, false, false, false, false),
            new Preset("all", "Все сайты (агрессивно)",
                "Обрабатывает весь HTTPS/HTTP/QUIC и неизвестные протоколы, кроме банков, госуслуг и российских сервисов. Помогает при жёстких блокировках, но может влиять на отдельные сайты.",
                null, true, false, false, false),
            new Preset("soft", "Мягкий режим (только разделение)",
                "Только разделяет TLS-пакеты, без подмены и фейковых пакетов. Самый безопасный режим.",
                null, false, true, false, false),
            new Preset("custom", "Свой список сайтов",
                "Сайты из файла lists\\custom.txt. Добавьте туда нужные домены — по одному на строку.",
                new string[] { "custom.txt" }, false, false, false, false)
        };

        public static Preset Get(string id)
        {
            foreach (Preset p in All)
            {
                if (string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) return p;
            }
            return All[0];
        }

        public static string Build(Preset preset, AppSettings settings, out string error)
        {
            error = null;
            string active = null;

            if (!preset.AllSites && !preset.Soft)
            {
                int count;
                active = PrepareActiveList(preset, out error, out count);
                if (error != null) return null;
            }

            string exclude = null;
            if (settings.ExcludeRu)
            {
                exclude = PrepareList(AppPaths.ExcludeList, AppPaths.ActiveExcludeList, out error);
                if (error != null) return null;
            }

            var profiles = new List<string>();

            if (preset.Soft)
            {
                profiles.Add(Tcp443Soft(exclude));
            }
            else
            {
                if (!preset.AllSites)
                {
                    profiles.Add(Tcp80Targeted(active, exclude));
                    profiles.Add(Tcp443Targeted(active, exclude));
                }

                bool global = preset.AllSites || preset.Fallback;
                if (global)
                {
                    // Профиль неизвестных протоколов идёт ПЕРЕД общими: иначе при
                    // выключенных исключениях общий профиль без hostlist перехватывал
                    // бы пакеты MTProto, но не обрабатывал их (any-protocol выключен).
                    // Общие профили нужны и для профилей со списком доменов:
                    // часть трафика (Telegram, звонки, CDN без SNI) в список не попадает.
                    profiles.Add(Tcp443Unknown());
                    profiles.Add(Tcp80Global(exclude));
                    profiles.Add(Tcp443Global(exclude));
                    profiles.Add(QuicGlobal(exclude));
                }
                else
                {
                    profiles.Add(QuicTargeted(active, exclude));
                }

                if (preset.Voice)
                {
                    profiles.Add(VoiceProfile());
                }
            }

            var args = new StringBuilder();
            args.Append(preset.Soft ? "--wf-tcp=443" : "--wf-tcp=80,443");
            if (!preset.Soft)
            {
                args.Append(" --wf-udp=443");
                if (preset.Voice) args.Append(",50000-50099,19294-19344");
            }
            args.Append(" --wf-filter-lan=1");

            for (int i = 0; i < profiles.Count; i++)
            {
                args.Append(' ');
                args.Append(profiles[i]);
                if (i + 1 < profiles.Count) args.Append(" --new");
            }

            return args.ToString();
        }

        private static string PrepareActiveList(Preset preset, out string error, out int count)
        {
            error = null;
            count = 0;

            var domains = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string listName in preset.Lists)
            {
                string path = Path.Combine(AppPaths.ListsDir, listName);
                if (!File.Exists(path)) continue;

                foreach (string domain in ReadDomains(path))
                {
                    if (seen.Add(domain)) domains.Add(domain);
                }
            }

            if (domains.Count == 0)
            {
                if (preset.Id == "custom")
                {
                    error = "Список lists\\custom.txt пуст.\r\n\r\nОткройте его и добавьте домены — по одному на строку.\r\nПример: example.com";
                }
                else
                {
                    error = "Не удалось прочитать список сайтов для профиля \"" + preset.Name + "\".";
                }
                return null;
            }

            string prepared = WriteList(AppPaths.ActiveList, domains, out error);
            if (prepared == null) return null;

            count = domains.Count;
            return prepared;
        }

        private static string PrepareList(string source, string target, out string error)
        {
            error = null;
            if (!File.Exists(source)) return null;

            List<string> domains = ReadDomains(source);
            if (domains.Count == 0) return null;

            return WriteList(target, domains, out error);
        }

        private static List<string> ReadDomains(string path)
        {
            var domains = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                string line = raw.Trim().TrimStart('\uFEFF');
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (line.StartsWith("//")) continue;
                if (seen.Add(line)) domains.Add(line);
            }

            return domains;
        }

        private static string WriteList(string target, List<string> domains, out string error)
        {
            error = null;
            try
            {
                AppPaths.EnsureDataDirs();
                var text = new StringBuilder();
                foreach (string domain in domains)
                {
                    text.AppendLine(domain);
                }
                File.WriteAllText(target, text.ToString(), new UTF8Encoding(false));
                return target;
            }
            catch (Exception ex)
            {
                error = "Не удалось подготовить список сайтов: " + ex.Message;
                return null;
            }
        }

        // HTTP: точечный профиль для доменов из списка.
        private static string Tcp80Targeted(string hostlist, string exclude)
        {
            var sb = new StringBuilder();
            sb.Append("--filter-tcp=80");
            AppendHostlist(sb, hostlist, exclude);
            sb.Append(" --dpi-desync=fake,multisplit");
            sb.Append(" --dpi-desync-split-pos=method+2");
            sb.Append(" --dpi-desync-fooling=md5sig");
            sb.Append(" --dpi-desync-repeats=6");
            return sb.ToString();
        }

        // HTTP без списка доменов. Стратегия из официального набора zapret для Windows.
        private static string Tcp80Global(string exclude)
        {
            var sb = new StringBuilder();
            sb.Append("--filter-tcp=80");
            AppendHostlist(sb, null, exclude);
            sb.Append(" --dpi-desync=fake,fakedsplit");
            sb.Append(" --dpi-desync-autottl=2");
            sb.Append(" --dpi-desync-fooling=md5sig");
            sb.Append(" --dpi-desync-repeats=6");
            return sb.ToString();
        }

        // HTTPS для доменов из списка: подмена SNI в фейковом ClientHello.
        private static string Tcp443Targeted(string hostlist, string exclude)
        {
            var sb = new StringBuilder();
            sb.Append("--filter-tcp=443");
            AppendHostlist(sb, hostlist, exclude);
            sb.Append(" --dpi-desync=fake,multidisorder");
            sb.Append(" --dpi-desync-split-pos=1,midsld");
            sb.Append(" --dpi-desync-fooling=md5sig");
            sb.Append(" --dpi-desync-repeats=11");
            sb.Append(" --dpi-desync-fake-tls-mod=rnd,dupsid,sni=www.google.com");
            return sb.ToString();
        }

        // HTTPS без списка доменов: всё, что не исключено, включая CDN без SNI.
        private static string Tcp443Global(string exclude)
        {
            var sb = new StringBuilder();
            sb.Append("--filter-tcp=443");
            AppendHostlist(sb, null, exclude);
            sb.Append(" --dpi-desync=fake,multidisorder");
            sb.Append(" --dpi-desync-split-pos=midsld");
            sb.Append(" --dpi-desync-fooling=badseq,md5sig");
            sb.Append(" --dpi-desync-repeats=6");
            return sb.ToString();
        }

        // TCP/443 без TLS/HTTP — сюда попадает MTProto Telegram и другие
        // протоколы без SNI. Без any-protocol zapret такие пакеты не трогает.
        // Исключения не применяются: у таких пакетов нет имени хоста, и профиль
        // с одним exclude-списком вообще не сработал бы.
        private static string Tcp443Unknown()
        {
            var sb = new StringBuilder();
            sb.Append("--filter-tcp=443");
            sb.Append(" --filter-l7=unknown");
            sb.Append(" --dpi-desync=fake,multisplit");
            sb.Append(" --dpi-desync-split-pos=5");
            sb.Append(" --dpi-desync-fooling=md5sig");
            sb.Append(" --dpi-desync-repeats=4");
            sb.Append(" --dpi-desync-any-protocol");
            sb.Append(" --dpi-desync-cutoff=n6");
            return sb.ToString();
        }

        private static string Tcp443Soft(string exclude)
        {
            var sb = new StringBuilder();
            sb.Append("--filter-tcp=443");
            AppendHostlist(sb, null, exclude);
            sb.Append(" --dpi-desync=multisplit");
            sb.Append(" --dpi-desync-split-pos=1");
            return sb.ToString();
        }

        private static string QuicTargeted(string hostlist, string exclude)
        {
            var sb = new StringBuilder();
            sb.Append("--filter-udp=443 --filter-l7=quic");
            AppendHostlist(sb, hostlist, exclude);
            sb.Append(" --dpi-desync=fake");
            sb.Append(" --dpi-desync-repeats=11");

            string fakeQuic = Path.Combine(AppPaths.FakeDir, "quic_initial_www_google_com.bin");
            if (File.Exists(fakeQuic))
            {
                sb.Append(" --dpi-desync-fake-quic=");
                sb.Append(Quote(fakeQuic));
            }

            return sb.ToString();
        }

        private static string QuicGlobal(string exclude)
        {
            var sb = new StringBuilder();
            sb.Append("--filter-udp=443 --filter-l7=quic");
            AppendHostlist(sb, null, exclude);
            sb.Append(" --dpi-desync=fake");
            sb.Append(" --dpi-desync-repeats=11");
            return sb.ToString();
        }

        // Голос Discord: IP discovery и медиапотоки идут на 50000-50099
        // и на старый диапазон 19294-19344.
        private static string VoiceProfile()
        {
            var sb = new StringBuilder();
            sb.Append("--filter-udp=50000-50099,19294-19344");
            sb.Append(" --filter-l7=discord");
            sb.Append(" --dpi-desync=fake");
            sb.Append(" --dpi-desync-repeats=2");

            string fakeDiscord = Path.Combine(AppPaths.FakeDir, "discord-ip-discovery-with-port.bin");
            if (File.Exists(fakeDiscord))
            {
                sb.Append(" --dpi-desync-fake-discord=");
                sb.Append(Quote(fakeDiscord));
            }

            return sb.ToString();
        }

        private static void AppendHostlist(StringBuilder sb, string hostlist, string exclude)
        {
            if (!string.IsNullOrEmpty(hostlist))
            {
                sb.Append(" --hostlist=");
                sb.Append(Quote(hostlist));
            }
            if (!string.IsNullOrEmpty(exclude))
            {
                sb.Append(" --hostlist-exclude=");
                sb.Append(Quote(exclude));
            }
        }

        private static string Quote(string path)
        {
            return "\"" + AppPaths.ToEnginePath(path) + "\"";
        }
    }
}
