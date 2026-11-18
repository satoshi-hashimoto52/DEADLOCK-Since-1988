using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Deadlock.Gacha;
using Deadlock.Match;

namespace Deadlock.Economy
{
    /// <summary>
    /// What the official online game's account and economy service (Stage 37D, D-072) and its callers agree on: the
    /// addresses, the request and answer forms, the transaction ids, and the signatures between servers. The service
    /// itself (Deadlock.EconomyServer) runs only on a server; the game and the match server use this contract.
    /// </summary>
    public static class EconomyProtocol
    {
        public const string Id = "deadlock-economy-1";
        public const ushort DefaultPort = 7791;

        public const string HeaderService = "X-Deadlock-Service";
        public const string HeaderTimestamp = "X-Deadlock-Timestamp";
        public const string HeaderSignature = "X-Deadlock-Signature";
        public const string HeaderProtocol = "X-Deadlock-Protocol";

        /// <summary>A signed server-to-server request older or newer than this (seconds) is refused.</summary>
        public const int SignatureWindowSeconds = 300;
        /// <summary>A join ticket is good for this long (seconds).</summary>
        public const int JoinTicketSeconds = 120;

        public const string PathMatchResults = "/v1/match-results";
        public const string PathHealth = "/v1/health";
#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEADLOCK_ECONOMY_DEV
        // Development only (compiled into the Editor, development builds and servers built with DEADLOCK_ECONOMY_DEV).
        public const string PathDevSession = "/dev/session";
        public const string PathDevGrantPoints = "/dev/grant-points";
        public const string PathDevEvent = "/dev/event";
#endif

        public static string AccountPath(string accountId, string rest) => $"/v1/accounts/{accountId}/{rest}";

        static readonly Regex AccountPattern = new Regex("^[a-z0-9][a-z0-9_-]{2,31}$");
        static readonly Regex RequestPattern = new Regex("^[A-Za-z0-9_-]{8,64}$");

        /// <summary>Account ids: lower-case letters, digits, '_' and '-', 3 to 32. Never a connection id.</summary>
        public static bool ValidAccountId(string id) => id != null && AccountPattern.IsMatch(id);

        /// <summary>A client's request id (made once per action and kept for its retries).</summary>
        public static bool ValidRequestId(string id) => id != null && RequestPattern.IsMatch(id);

        public static string NewRequestId() => Guid.NewGuid().ToString("N");

        /// <summary>The match reward of one place of one match: given at most once, ever (D-061 / D-072).</summary>
        public static string RewardTransactionId(string matchId, int slotId) => GachaService.MatchRewardRequestId(matchId, slotId);

        public static string GachaTransactionId(string accountId, string requestId) => $"gacha:{accountId}:{requestId}";
        public static string ExchangeTransactionId(string accountId, string requestId) => $"exchange:{accountId}:{requestId}";

        public static string Sha256Hex(string text)
        {
            using var sha = SHA256.Create();
            return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? "")));
        }

        public static string Hex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes)
                sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        /// <summary>Compares without stopping at the first difference (signatures).</summary>
        public static bool FixedTimeEquals(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length)
                return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++)
                diff |= a[i] ^ b[i];
            return diff == 0;
        }

        public static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);
    }

    /// <summary>Answer codes (status "ok", "already_applied", "refused", "error").</summary>
    public static class EconomyCodes
    {
        public const string Ok = "ok";
        public const string AlreadyApplied = "already_applied";
        public const string Refused = "refused";
        public const string Error = "error";

        public const string BadRequest = "bad_request";
        public const string Unauthorized = "unauthorized";
        public const string Forbidden = "forbidden";
        public const string NotFound = "not_found";
        /// <summary>The same transaction id with different contents.</summary>
        public const string Conflict = "conflict";
        public const string NotEnoughPoints = "not_enough_points";
        public const string NotEnoughMedals = "not_enough_medals";
        public const string InvalidConfiguration = "invalid_configuration";
        public const string NotOffered = "not_offered";
        public const string AlreadyOwned = "already_owned";
        public const string UnknownAccount = "unknown_account";
        public const string InvalidResult = "invalid_result";
        /// <summary>The service could not finish (database error): nothing changed, the same request may be sent again.</summary>
        public const string Unavailable = "unavailable";
    }

    // ------------------------------------------------------------------------------------------------ match results

    /// <summary>One place of a finished match as the match server decided it.</summary>
    [Serializable]
    public sealed class MatchReportRow
    {
        public int slotId;
        public int rank;
        /// <summary>The proved account of a human (join ticket); empty for NPCs and unproved development players.</summary>
        public string accountId = "";
        public bool startedAsHuman;
        /// <summary>Left, disconnected or failed to load (an NPC took the place over).</summary>
        public bool departed;
        public bool alive;
        public int kills;
        public float damageDealt;
        /// <summary>The points the match server worked out; the service works them out again and refuses a difference.</summary>
        public int points;

        /// <summary>Gets points (D-061): a proved human who stayed to the end alive. Never an NPC, a dead or a departed player.</summary>
        public bool Eligible => startedAsHuman && !departed && alive && !string.IsNullOrEmpty(accountId);

        public int ExpectedPoints => Eligible ? MatchRules.PointsForRank(rank) : 0;

        public string Canonical() => string.Join(",", slotId.ToString(CultureInfo.InvariantCulture), rank.ToString(CultureInfo.InvariantCulture),
            accountId ?? "", startedAsHuman ? "1" : "0", departed ? "1" : "0", alive ? "1" : "0",
            kills.ToString(CultureInfo.InvariantCulture), EconomyProtocol.F(Math.Round(damageDealt, 2)), points.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>The fixed result of one match, sent once by the match server (and again, unchanged, until it is confirmed).</summary>
    [Serializable]
    public sealed class MatchReport
    {
        public string protocol = EconomyProtocol.Id;
        public string matchId;
        public string serverId;
        public string endReason;
        public float durationSeconds;
        public List<MatchReportRow> rows = new List<MatchReportRow>();

        /// <summary>The contents that must be the same in a resend (order of rows does not matter).</summary>
        public string Canonical()
        {
            var lines = new List<string>();
            foreach (var r in rows)
                lines.Add(r?.Canonical() ?? "null");
            lines.Sort(string.CompareOrdinal);
            return $"{matchId}|{serverId}|{endReason}|{EconomyProtocol.F(Math.Round(durationSeconds, 2))}|{string.Join(";", lines)}";
        }

        public string Hash() => EconomyProtocol.Sha256Hex(Canonical());

        /// <summary>
        /// The match server's report of a match session's result: every place with its rank; a human's account only when
        /// it was proved (<paramref name="accountOf"/> returns null for NPCs and development players), so only proved
        /// humans can get points.
        /// </summary>
        public static MatchReport From(MatchResult result, string serverId, Func<int, string> accountOf)
        {
            var report = new MatchReport
            {
                matchId = result.MatchId,
                serverId = serverId,
                endReason = result.Reason.ToString(),
                durationSeconds = (float)Math.Round(result.Duration, 2),
            };
            foreach (var row in result.Rows)
            {
                string account = row.StartedAsHuman ? accountOf(row.SlotId) ?? "" : "";
                var line = new MatchReportRow
                {
                    slotId = row.SlotId,
                    rank = row.Rank,
                    accountId = account,
                    startedAsHuman = row.StartedAsHuman,
                    departed = row.Departed,
                    alive = row.Alive,
                    kills = row.Kills,
                    damageDealt = (float)Math.Round(row.DamageDealt, 2),
                };
                line.points = line.ExpectedPoints;
                report.rows.Add(line);
            }
            return report;
        }

        /// <summary>Problems of the report's form and rules (empty: none). The service refuses a report with any.</summary>
        public List<string> Problems()
        {
            var p = new List<string>();
            if (protocol != EconomyProtocol.Id)
                p.Add("protocol");
            if (string.IsNullOrEmpty(matchId) || matchId.Length > 64)
                p.Add("matchId");
            if (string.IsNullOrEmpty(serverId) || serverId.Length > 64)
                p.Add("serverId");
            if (rows == null || rows.Count != MatchRules.Slots)
            {
                p.Add("rows");
                return p;
            }
            var slots = new HashSet<int>();
            var ranks = new HashSet<int>();
            var accounts = new HashSet<string>();
            foreach (var r in rows)
            {
                if (r == null)
                {
                    p.Add("row");
                    continue;
                }
                if (r.slotId < 1 || r.slotId > MatchRules.Slots || !slots.Add(r.slotId))
                    p.Add($"slot {r.slotId}");
                if (r.rank < 1 || r.rank > MatchRules.Slots || !ranks.Add(r.rank))
                    p.Add($"rank {r.rank}");
                if (!string.IsNullOrEmpty(r.accountId))
                {
                    if (!r.startedAsHuman)
                        p.Add($"slot {r.slotId}: an NPC with an account");
                    if (!EconomyProtocol.ValidAccountId(r.accountId))
                        p.Add($"slot {r.slotId}: account id");
                    else if (!accounts.Add(r.accountId))
                        p.Add($"account {r.accountId} twice");
                }
                if (r.kills < 0 || r.damageDealt < 0 || float.IsNaN(r.damageDealt) || float.IsInfinity(r.damageDealt))
                    p.Add($"slot {r.slotId}: record");
                if (r.points != r.ExpectedPoints)
                    p.Add($"slot {r.slotId}: points {r.points} (expected {r.ExpectedPoints})");
            }
            return p;
        }
    }

    [Serializable]
    public sealed class RewardLine
    {
        public int slotId;
        public string accountId;
        public int points;
        public string transactionId;
    }

    [Serializable]
    public sealed class MatchReportAnswer
    {
        public string status;
        public string code;
        public string message;
        public string matchId;
        public List<RewardLine> rewards = new List<RewardLine>();
    }

    // ------------------------------------------------------------------------------------------------ player requests

    [Serializable]
    public sealed class DrawRequest
    {
        public string requestId;
        /// <summary>"single" or "ten".</summary>
        public string kind;
        /// <summary>
        /// The account the caller believes it acts for. Only checked against the signed-in account (a different one is
        /// refused); the account is always the session's, never taken from here.
        /// </summary>
        public string accountId;
    }

    [Serializable]
    public sealed class ExchangeRequest
    {
        public string requestId;
        public string itemId;
        public string accountId;
    }

    [Serializable]
    public sealed class OwnedView
    {
        public string itemId;
        public string displayName;
        public string rarity;
        public string category;
        public string source;
        public int duplicates;
        public string acquiredUtc;
    }

    /// <summary>An account's balance and items as the service holds them (the only official values).</summary>
    [Serializable]
    public sealed class WalletAnswer
    {
        public string status;
        public string code;
        public string message;
        public string accountId;
        public int points;
        public int medals;
        public string serverTimeUtc;
        /// <summary>The LEGENDARY event now by the server's clock, or empty.</summary>
        public string activeEventId;
        public string catalog;
        /// <summary>The gacha can be drawn now (every rarity has items); otherwise why not (Stage 37E, for the screen).</summary>
        public bool gachaOpen;
        public List<string> gachaProblems = new List<string>();
        /// <summary>The running event's period and whether it is a rerun (for the screen's rates).</summary>
        public bool activeEventRerun;
        public List<OwnedView> owned = new List<OwnedView>();
    }

    /// <summary>The answer to a draw: the saved results (the same ones again for a repeated request).</summary>
    [Serializable]
    public sealed class DrawAnswer
    {
        public string status;
        public string code;
        public string message;
        public string transactionId;
        public HistoryRecord record;
        public int points;
        public int medals;

        public bool Confirmed => status == EconomyCodes.Ok || status == EconomyCodes.AlreadyApplied;
    }

    [Serializable]
    public sealed class ExchangeAnswer
    {
        public string status;
        public string code;
        public string message;
        public string transactionId;
        public string itemId;
        public int price;
        public int points;
        public int medals;

        public bool Confirmed => status == EconomyCodes.Ok || status == EconomyCodes.AlreadyApplied;
    }

    [Serializable]
    public sealed class ShopItemView
    {
        public string itemId;
        public string rarity;
        public int price;
        public string displayName;
        public string category;
    }

    [Serializable]
    public sealed class ShopAnswer
    {
        public string status;
        public string code;
        public string message;
        public List<ShopItemView> items = new List<ShopItemView>();
    }

    [Serializable]
    public sealed class HistoryAnswer
    {
        public string status;
        public string code;
        public string message;
        public List<HistoryRecord> records = new List<HistoryRecord>();
    }

    [Serializable]
    public sealed class SimpleAnswer
    {
        public string status;
        public string code;
        public string message;
    }

    // ------------------------------------------------------------------------------------------------ development only
#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEADLOCK_ECONOMY_DEV

    [Serializable]
    public sealed class DevSessionRequest
    {
        public string accountId;
        public string displayName;
    }

    [Serializable]
    public sealed class DevSessionAnswer
    {
        public string status;
        public string code;
        public string message;
        public string accountId;
        public string token;
        public string expiresUtc;
    }

    [Serializable]
    public sealed class DevGrantRequest
    {
        public string requestId;
        public string accountId;
        public int points;
    }

#endif

    [Serializable]
    public sealed class JoinTicketAnswer
    {
        public string status;
        public string code;
        public string message;
        public string ticket;
        public string expiresUtc;
    }

    // ------------------------------------------------------------------------------------------------ signatures

    /// <summary>
    /// Server-to-server signatures (the match server's results to the economy service): HMAC-SHA256 of the method, path,
    /// time and body with a key both servers hold. A stand-in until the servers authenticate each other with
    /// certificates or issued service tokens (DECISIONS D-072); the key lives outside the repository.
    /// </summary>
    public static class ServiceSignature
    {
        public const int MinKeyBytes = 32;

        public static string Sign(byte[] key, string service, string method, string path, long unixSeconds, string body)
        {
            string text = string.Join("\n", EconomyProtocol.Id, service ?? "", method ?? "", path ?? "", unixSeconds.ToString(CultureInfo.InvariantCulture),
                EconomyProtocol.Sha256Hex(body ?? ""));
            using var h = new HMACSHA256(key);
            return EconomyProtocol.Hex(h.ComputeHash(Encoding.UTF8.GetBytes(text)));
        }

        /// <summary>Null when the signature is good and fresh; else the reason.</summary>
        public static string Check(byte[] key, string service, string method, string path, string timestamp, string signature, string body, long nowUnix)
        {
            if (key == null || key.Length < MinKeyBytes)
                return "no key";
            if (!long.TryParse(timestamp, NumberStyles.Integer, CultureInfo.InvariantCulture, out long t))
                return "timestamp";
            if (Math.Abs(nowUnix - t) > EconomyProtocol.SignatureWindowSeconds)
                return "stale";
            string expected = Sign(key, service, method, path, t, body);
            return EconomyProtocol.FixedTimeEquals(expected, signature?.ToLowerInvariant()) ? null : "signature";
        }

        public static long UnixNow() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        /// <summary>A key file: hex digits (64 or more = 32 bytes or more), made outside the repository (Tools/economy-key.sh).</summary>
        public static byte[] ReadKeyFile(string path)
        {
            string hex = System.IO.File.ReadAllText(path).Trim();
            if (hex.Length % 2 != 0 || hex.Length < MinKeyBytes * 2)
                throw new FormatException("key file: 64 or more hex digits expected");
            var key = new byte[hex.Length / 2];
            for (int i = 0; i < key.Length; i++)
                key[i] = byte.Parse(hex.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return key;
        }
    }

    /// <summary>
    /// A join ticket (Stage 37D, local development of the official flow): issued by the account service to a signed-in
    /// account, checked by the match server with the shared key. It names the account and expires after
    /// <see cref="EconomyProtocol.JoinTicketSeconds"/>; the match server takes the account from it, never from the
    /// client. Form: v1.account.name(base64url).expiry.nonce.signature.
    /// </summary>
    public static class JoinTicket
    {
        static byte[] SubKey(byte[] key)
        {
            using var h = new HMACSHA256(key);
            return h.ComputeHash(Encoding.UTF8.GetBytes("join-ticket"));
        }

        public static string Issue(byte[] key, string accountId, string displayName, long expiresUnix, string nonce)
        {
            string name = Base64Url(Encoding.UTF8.GetBytes(displayName ?? accountId));
            string body = $"v1.{accountId}.{name}.{expiresUnix.ToString(CultureInfo.InvariantCulture)}.{nonce}";
            using var h = new HMACSHA256(SubKey(key));
            return body + "." + EconomyProtocol.Hex(h.ComputeHash(Encoding.UTF8.GetBytes(body)));
        }

        public static bool Verify(byte[] key, string ticket, long nowUnix, out string accountId, out string displayName, out string reason)
        {
            accountId = displayName = reason = null;
            var parts = ticket?.Split('.');
            if (key == null || key.Length < ServiceSignature.MinKeyBytes)
            {
                reason = "参加チケットを確認できません（鍵がありません）";
                return false;
            }
            if (parts == null || parts.Length != 6 || parts[0] != "v1" || !EconomyProtocol.ValidAccountId(parts[1]))
            {
                reason = "参加チケットの形式が不正です";
                return false;
            }
            string body = string.Join(".", parts, 0, 5);
            using (var h = new HMACSHA256(SubKey(key)))
            {
                if (!EconomyProtocol.FixedTimeEquals(EconomyProtocol.Hex(h.ComputeHash(Encoding.UTF8.GetBytes(body))), parts[5]))
                {
                    reason = "参加チケットの署名が一致しません";
                    return false;
                }
            }
            if (!long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out long expires) || nowUnix > expires)
            {
                reason = "参加チケットの期限が切れています";
                return false;
            }
            try
            {
                displayName = Encoding.UTF8.GetString(FromBase64Url(parts[2]));
            }
            catch (FormatException)
            {
                reason = "参加チケットの形式が不正です";
                return false;
            }
            accountId = parts[1];
            return true;
        }

        static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        static byte[] FromBase64Url(string s)
        {
            s = s.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
            }
            return Convert.FromBase64String(s);
        }
    }
}
